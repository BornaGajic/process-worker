using Polly;
using Polly.Retry;
using ProcessWorker.Model;
using Serilog;
using System.Collections.Concurrent;
using Timer = System.Timers.Timer;

namespace ProcessWorker.Service
{
    public class ProcessWorker : IProcessWorker
    {
        public ProcessWorkerConfiguration Configuration { get; private set; }

        protected readonly ConcurrentQueue<ProcessMetadata> _queue = new();
        protected readonly ConcurrentDictionary<Guid, WorkItemToolbox> _workItemToolbox = new();
        protected int CountOfWorkingItems { get; set; } = 0;

        private readonly AsyncRetryPolicy<ProcessMetadata> _startWorkItemRetryPolicy;
        private readonly Timer _timer;
        private readonly ILogger _logger;

        public ProcessWorker(ILogger logger, ProcessWorkerConfiguration configuration = null)
        {
            _logger = logger;
            Configuration = configuration ?? new();
            _timer = new(Configuration.TimerIntervalMs)
            {
                AutoReset = true,
                Enabled = false
            };
            _timer.Elapsed += async (sender, e) => await RunWorkerAsync();

            _startWorkItemRetryPolicy = Policy<ProcessMetadata>
                .HandleResult(result => result is null)
                .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(1000));

            _timer.Start();
        }

        public Task CancelWorkItemAsync(Guid processId, int? millisecondsDelay = null)
        {
            _workItemToolbox.TryGetValue(processId, out var toolbox);

            if (toolbox.State is null)
                throw new Exception($"State must not be null!");

            if (toolbox.State.Status is ProcessStatus.Queued)
            {
                var cancelItem = _queue.FirstOrDefault(processItem => processItem.ProcessInfo.ProcessId == processId);

                if (cancelItem is not null)
                {
                    cancelItem.IsCanceledBeforeRunning = true;

                    toolbox.Progress.Report(ProcessStatus.Canceled);
                    toolbox.TaskCompletionSrc.TrySetResult();
                    
                    _workItemToolbox.TryRemove(processId, out var _);
                }
            }
            else if (toolbox.State.Status is ProcessStatus.Running)
            {
                toolbox.Progress.Report(ProcessStatus.CancellationRequested);

                if (millisecondsDelay is not null)
                    toolbox.CancellationTokenSrc.CancelAfter(millisecondsDelay.Value);
                else
                    toolbox.CancellationTokenSrc.Cancel();
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer.Dispose();

            foreach (var (_, cancelTokenSource) in _workItemToolbox)
                cancelTokenSource.CancellationTokenSrc.Dispose();

            _workItemToolbox.Clear();
        }

        public ProcessInfo EnqueueWorkItem<TState>(Func<CancellationToken, Task> process, ProcessWorkerStatusChangeCallback<TState> statusChangeCallback = null)
            where TState : ProcessWorkerState
        {
            var cancelTokenSource = new CancellationTokenSource();
            var taskCompletionSrc = new TaskCompletionSource();

            var processId = Guid.NewGuid();

            var processInfo = new ProcessInfo
            {
                StoppingToken = cancelTokenSource.Token,
                WaitingToken = taskCompletionSrc.Task,
                ProcessId = processId
            };

            var processMetadata = new ProcessMetadata
            {
                ProcessInfo = processInfo,
                Process = async () =>
                {
                    await process(cancelTokenSource.Token);
                }
            };

            var progress = new Progress<ProcessStatus>();
            progress.ProgressChanged += async (sender, newStatus) =>
            {
                try
                {
                    var state = await statusChangeCallback(processId, newStatus) as ProcessWorkerState;

                    if (state is not null && state.ProcessId != default && _workItemToolbox.TryGetValue(state.ProcessId, out var toolbox))
                    {
                        toolbox.State = state;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, ex.Message);

                    if (_workItemToolbox.TryGetValue(processId, out var toolbox) && toolbox.State is not null)
                    {
                        toolbox.State.EndTime = DateTime.UtcNow;
                        toolbox.State.Status = ex is TaskCanceledException or OperationCanceledException ? ProcessStatus.Canceled : ProcessStatus.Failed;
                    }
                }
            };

            (progress as IProgress<ProcessStatus>).Report(ProcessStatus.Queued);

            _workItemToolbox.TryAdd(processId, new WorkItemToolbox
            {
                CancellationTokenSrc = cancelTokenSource,
                TaskCompletionSrc = taskCompletionSrc,
                Progress = progress
            });
            _queue.Enqueue(processMetadata);

            return processInfo;
        }

        protected async Task RunWorkerAsync()
        {
            if (_queue.IsEmpty || CountOfWorkingItems == Configuration.Concurrency)
            {
                return;
            }

            async Task workItem()
            {
                bool isCancellationRequested = false;

                CountOfWorkingItems++;

                var processMetadata = await _startWorkItemRetryPolicy.ExecuteAsync(
                    () => Task.FromResult(_queue.TryDequeue(out var metadata) ? metadata : null)
                );

                if (processMetadata is not null && processMetadata.IsCanceledBeforeRunning == false)
                {
                    _workItemToolbox.TryGetValue(processMetadata.ProcessInfo.ProcessId, out var workItemToolbox);

                    try
                    {
                        if (processMetadata.ProcessInfo.StoppingToken.IsCancellationRequested)
                        {
                            workItemToolbox.Progress.Report(ProcessStatus.Canceled);
                        }
                        else
                        {
                            workItemToolbox.Progress.Report(ProcessStatus.Running);

                            using var cancellationRequestedCallback = processMetadata.ProcessInfo.StoppingToken.Register(() =>
                            {
                                isCancellationRequested = true;
                                CountOfWorkingItems--;
                            });

                            await processMetadata.Process();

                            if (processMetadata.ProcessInfo.StoppingToken.IsCancellationRequested)
                                workItemToolbox.Progress.Report(ProcessStatus.Canceled);
                            else
                                workItemToolbox.Progress.Report(ProcessStatus.Done);
                        }
                    }
                    catch (Exception e)
                    {
                        if (e is TaskCanceledException or OperationCanceledException)
                        {
                            _logger.Information($"Process canceled.");
                            workItemToolbox.Progress.Report(ProcessStatus.Canceled);
                        }
                        else
                        {
                            _logger.Error(e, e.Message);
                            workItemToolbox.Progress.Report(ProcessStatus.Failed);
                        }
                    }
                    finally
                    {
                        workItemToolbox.TaskCompletionSrc.TrySetResult();
                        _workItemToolbox.TryRemove(processMetadata.ProcessInfo.ProcessId, out var _);
                    }
                }
                else if (processMetadata is null)
                {
                    _logger.Error(new Exception($"Could not dequeue and start background process in {nameof(ProcessWorker)}!)"), Environment.StackTrace.ToString());
                }

                if (isCancellationRequested == false)
                    CountOfWorkingItems--;
            };

            var concurrencyOffset = Configuration.Concurrency - CountOfWorkingItems;

            var workerSlotsLeft = Math.Clamp(
                concurrencyOffset,
                0,
                concurrencyOffset >= _queue.Count ? _queue.Count : concurrencyOffset
            );

            await Task.WhenAll(
                Enumerable
                    .Range(0, workerSlotsLeft)
                    .Select(_ => workItem())
            );
        }

        protected class ProcessMetadata
        {
            public bool IsCanceledBeforeRunning { get; set; } = false;
            public Func<Task> Process { get; init; }
            public ProcessInfo ProcessInfo { get; set; }
        }

        protected class WorkItemToolbox
        {
            public CancellationTokenSource CancellationTokenSrc { get; init; }
            public IProgress<ProcessStatus> Progress { get; init; }
            public TaskCompletionSource TaskCompletionSrc { get; init; }
            public ProcessWorkerState State { get; set; }
        }
    }
}
