using ProcessWorker.Common;
using ProcessWorker.Model;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ProcessWorker.Service
{
    internal class ProcessWorkerProducer : IProcessWorkerProducer
    {
        protected readonly ConcurrentDictionary<Guid, WorkItem> _workItems = new();

        public ProcessWorkerProducer(ChannelWriter<WorkItem> channelWriter)
        {
            ChannelWriter = channelWriter;
        }

        public ChannelWriter<WorkItem> ChannelWriter { get; }
        public Exception FatalException { get; internal set; }
        public bool IsOperational => FatalException is null;

        public void CancelWorkItemAsync(Guid processId, int? millisecondsDelay = null)
        {
            if (_workItems.TryGetValue(processId, out var workItem) && Monitor.TryEnter(workItem))
            {
                try
                {
                    if (workItem.Status is ProcessStatus.Queued)
                    {
                        workItem.ProcessMetadata.IsCanceledBeforeRunning = true;
                        workItem.TaskCompletionSrc.TrySetCanceled();
                        workItem.Progress(ProcessStatus.Canceled);
                        _workItems.TryRemove(processId, out var _);
                        workItem.Dispose();
                    }
                    else if (workItem.Status is ProcessStatus.Running)
                    {
                        workItem.Progress(ProcessStatus.CancellationRequested);

                        if (millisecondsDelay is not null)
                            workItem.CancellationTokenSrc.CancelAfter(millisecondsDelay.Value);
                        else
                            workItem.CancellationTokenSrc.Cancel();
                    }
                }
                finally
                {
                    Monitor.Exit(workItem);
                }
            }
        }

        public Task<ProcessWorkerInfo> EnqueueAsync(Func<CancellationToken, Task> process)
        {
            return EnqueueAsync(process, (_, _) => Task.CompletedTask);
        }

        public Task<ProcessWorkerInfo> EnqueueAsync(Func<CancellationToken, Task> process, Func<Guid, ProcessStatus, Task> progressCallback)
        {
            var state = new ProcessWorkerState();

            return EnqueueAsync(process, async (processId, newStatus) =>
            {
                await progressCallback(processId, newStatus);

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

        public async Task<ProcessWorkerInfo> EnqueueAsync<TState>(Func<CancellationToken, Task> process, Func<Guid, ProcessStatus, Task<TState>> statusChangeCallback)
            where TState : ProcessWorkerState
        {
            if (!IsOperational)
            {
                throw new Exception("Process worker is down, see inner exception for details.", FatalException);
            }

            var cancelTokenSource = new CancellationTokenSource();
            var taskCompletionSrc = new TaskCompletionSource();

            var processId = Guid.NewGuid();

            async Task progressAsync(ProcessStatus newStatus, Exception processException = null)
            {
                var state = await statusChangeCallback(processId, newStatus);

                if (_workItems.TryGetValue(processId, out var workItem))
                {
                    workItem.Status = newStatus;
                }
            }

            await progressAsync(ProcessStatus.Queued);

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
                    DoWorkAsync = async (stoppingToken) =>
                    {
                        try
                        {
                            await process(stoppingToken);
                        }
                        finally
                        {
                            if (_workItems.TryRemove(processInfo.ProcessId, out var item))
                            {
                                item.Dispose();
                            }
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