using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ProcessWorkerV2
{
    internal class ProcessWorkerProducer : IProcessWorkerProducer
    {
        protected readonly ConcurrentDictionary<Guid, WorkItem> _workItems = new();

        public ProcessWorkerProducer(ChannelWriter<WorkItem> channelWriter)
        {
            ChannelWriter = channelWriter;
        }

        public ChannelWriter<WorkItem> ChannelWriter { get; }

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

        public Task<ProcessWorkerInfo> EnqueueWorkItemAsync(Func<CancellationToken, Task> process)
        {
            return EnqueueWorkItemAsync(process, (_, _) => Task.CompletedTask);
        }

        public Task<ProcessWorkerInfo> EnqueueWorkItemAsync(Func<CancellationToken, Task> process, Func<Guid, ProcessStatus, Task> progressCallback)
        {
            var state = new ProcessWorkerState();

            return EnqueueWorkItemAsync(process, async (processId, newStatus) =>
            {
                await progressCallback(processId, newStatus);

                if (newStatus is ProcessStatus.Running)
                    state.StartTime = DateTime.UtcNow;
                else if (newStatus is ProcessStatus.Queued)
                    state.QueuedAt = DateTime.UtcNow;
                else if (newStatus is ProcessStatus.Done or ProcessStatus.Canceled or ProcessStatus.Failed)
                    state.EndTime = DateTime.UtcNow;

                return state with
                {
                    ProcessId = processId,
                    Status = newStatus
                };
            });
        }

        public async Task<ProcessWorkerInfo> EnqueueWorkItemAsync<TState>(Func<CancellationToken, Task> process, Func<Guid, ProcessStatus, Task<TState>> statusChangeCallback)
            where TState : ProcessWorkerState
        {
            var cancelTokenSource = new CancellationTokenSource();
            var taskCompletionSrc = new TaskCompletionSource();

            var processId = Guid.NewGuid();

            async Task progressAsync(ProcessStatus newStatus, Exception processException = null)
            {
                var state = await statusChangeCallback(processId, newStatus);

                if (_workItems.TryGetValue(processId, out var workItem))
                {
                    lock (workItem)
                    {
                        workItem.Status = newStatus;
                    }
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
    }
}