using Microsoft.Extensions.DependencyInjection;
using ProcessWorker.Common;
using ProcessWorker.Model;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ProcessWorker.Service
{
    internal class ProcessWorkerProducer : IProcessWorkerProducer
    {
        protected readonly ConcurrentDictionary<Guid, WorkItem> _workItems = new();
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public ProcessWorkerProducer(ChannelWriter<WorkItem> channelWriter, IServiceScopeFactory serviceScopeFactory)
        {
            ChannelWriter = channelWriter;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public ChannelWriter<WorkItem> ChannelWriter { get; }
        public Exception FatalException { get; internal set; }
        public bool IsOperational => FatalException is null;

        private ConcurrentDictionary<Guid, WorkItem> CancellationLock { get; } = [];

        public async Task CancelWorkItemAsync(Guid processId, int? millisecondsDelay = null)
        {
            if (_workItems.TryGetValue(processId, out var workItem) && CancellationLock.TryAdd(processId, workItem))
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateAsyncScope();

                    if (workItem.Status is ProcessStatus.Queued)
                    {
                        workItem.ProcessMetadata.IsCanceledBeforeRunning = true;
                        workItem.TaskCompletionSrc.TrySetCanceled();
                        await workItem.Progress(ProcessStatus.Canceled, scope.ServiceProvider);
                        _workItems.TryRemove(processId, out var _);
                        workItem.Dispose();
                    }
                    else if (workItem.Status is ProcessStatus.Running)
                    {
                        await workItem.Progress(ProcessStatus.CancellationRequested, scope.ServiceProvider);

                        if (millisecondsDelay is not null)
                            workItem.CancellationTokenSrc.CancelAfter(millisecondsDelay.Value);
                        else
                            workItem.CancellationTokenSrc.Cancel();
                    }
                }
                finally
                {
                    CancellationLock.TryRemove(processId, out _);
                }
            }
        }

        public Task<ProcessWorkerInfo> EnqueueAsync(Func<IServiceProvider, CancellationToken, ValueTask> process)
        {
            return EnqueueAsync(process, (_, _, _, _) => ValueTask.CompletedTask);
        }

        public Task<ProcessWorkerInfo> EnqueueAsync(
            Func<IServiceProvider, CancellationToken, ValueTask> process,
            Func<Guid, ProcessStatus, IServiceProvider, Exception, ValueTask> progressCallback
        )
        {
            var state = new ProcessWorkerState();

            return EnqueueAsync(process, async (processId, newStatus, serviceProvider, exception) =>
            {
                await progressCallback(processId, newStatus, serviceProvider, exception);

                if (newStatus is ProcessStatus.Running)
                    state.StartTime = DateTime.UtcNow;
                else if (newStatus is ProcessStatus.Queued)
                    state.QueuedAt = DateTime.UtcNow;
                else if (newStatus is ProcessStatus.Done or ProcessStatus.Canceled or ProcessStatus.Failed or ProcessStatus.Fatal)
                    state.EndTime = DateTime.UtcNow;

                return state with
                {
                    ProcessId = processId,
                    Status = newStatus
                };
            });
        }

        public async Task<ProcessWorkerInfo> EnqueueAsync<TState>(
            Func<IServiceProvider, CancellationToken, ValueTask> process,
            Func<Guid, ProcessStatus, IServiceProvider, Exception, ValueTask<TState>> statusChangeCallback
        ) where TState : ProcessWorkerState
        {
            if (!IsOperational)
            {
                throw new Exception("Process worker is down, see inner exception for details.", FatalException);
            }

            var cancelTokenSource = new CancellationTokenSource();
            var taskCompletionSrc = new TaskCompletionSource();

            var processId = Guid.NewGuid();

            async Task progressAsync(ProcessStatus newStatus, IServiceProvider serviceProvider, Exception processException = null)
            {
                var state = await statusChangeCallback(processId, newStatus, serviceProvider, processException);

                if (_workItems.TryGetValue(processId, out var workItem))
                {
                    workItem.Status = newStatus;
                }
            }

            using (var scope = _serviceScopeFactory.CreateAsyncScope())
            {
                await progressAsync(ProcessStatus.Queued, scope.ServiceProvider);
            }

            var processInfo = new ProcessWorkerInfo
            {
                StoppingToken = cancelTokenSource.Token,
                Completion = taskCompletionSrc.Task,
                ProcessId = processId
            };

            var workItem = new WorkItem(progressAsync)
            {
                Status = ProcessStatus.Queued,
                CancellationTokenSrc = cancelTokenSource,
                TaskCompletionSrc = taskCompletionSrc,
                ProcessMetadata = new ProcessMetadata
                {
                    DoWorkAsync = async (serviceProvider, stoppingToken) =>
                    {
                        try
                        {
                            await process(serviceProvider, stoppingToken);
                        }
                        finally
                        {
                            _workItems.TryRemove(processInfo.ProcessId, out var _);
                        }
                    },
                    ProcessInfo = processInfo
                }
            };

            _workItems.TryAdd(processId, workItem);

            // For Unbounded Channels this will always return true
            ChannelWriter.TryWrite(workItem);

            return processInfo;
        }

        internal void StopProducing(Exception ex)
        {
            FatalException = ex;
            ChannelWriter.TryComplete();
            _workItems.Clear();
        }
    }
}