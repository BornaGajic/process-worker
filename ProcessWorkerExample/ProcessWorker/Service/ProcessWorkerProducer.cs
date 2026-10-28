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

                    if (workItem.Status is ProcessStatus.Queued && workItem.TryClaim())
                    {
                        workItem.ProcessMetadata.IsCanceledBeforeRunning = true;

                        try
                        {
                            // The status is reported before the item completes, so whoever awaits the completion already sees the final state.
                            await workItem.Progress(ProcessStatus.Canceled, scope.ServiceProvider);
                        }
                        finally
                        {
                            // The claim can't be handed over, so the item has to be finished here even if the status update throws,
                            // because the consumer skips it and whoever awaits its completion would wait forever.
                            workItem.TaskCompletionSrc.TrySetCanceled();
                            _workItems.TryRemove(processId, out var _);
                            workItem.Dispose();
                        }
                    }
                    else if (workItem.Status is ProcessStatus.Queued or ProcessStatus.Running)
                    {
                        // Running, or still marked as queued but already claimed by the consumer that is about to run it.
                        await workItem.Progress(ProcessStatus.CancellationRequested, scope.ServiceProvider);

                        // The item can finish and dispose itself while the status update above is in flight.
                        var cancellationTokenSrc = workItem.CancellationTokenSrc;

                        try
                        {
                            if (cancellationTokenSrc is not null)
                            {
                                if (millisecondsDelay is not null)
                                    cancellationTokenSrc.CancelAfter(millisecondsDelay.Value);
                                else
                                    await cancellationTokenSrc.CancelAsync();
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // Already finished, nothing left to cancel.
                        }
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

            // Status updates of one item are serialized, so the guards below can't race with each other.
            // The semaphore is deliberately not disposed: updates can still arrive after the final one and would throw on a disposed semaphore.
            // It holds no unmanaged resources (its wait handle is never used), so the GC reclaims it together with the closure.
            var semaphore = new SemaphoreSlim(1, 1);
            var isFinished = false;
            var lastStatus = ProcessStatus.Queued;

            static bool isTerminal(ProcessStatus status)
                => (status & (ProcessStatus.Done | ProcessStatus.Canceled | ProcessStatus.Failed | ProcessStatus.Fatal)) != 0;

            async Task progressAsync(ProcessStatus newStatus, IServiceProvider serviceProvider, Exception processException = null)
            {
                await semaphore.WaitAsync();

                try
                {
                    // A status update that arrives after the final one (e.g. a cancellation request racing the item finishing)
                    // must not overwrite the final state.
                    if (isFinished)
                    {
                        return;
                    }

                    // Same for an item that starts running just after a cancellation was requested: the request is already reported
                    // and the item is going to be canceled, so it must not be reported as running again.
                    if (newStatus is ProcessStatus.Running && lastStatus is ProcessStatus.CancellationRequested)
                    {
                        return;
                    }

                    lastStatus = newStatus;
                    isFinished = isTerminal(newStatus);

                    var state = await statusChangeCallback(processId, newStatus, serviceProvider, processException);

                    if (_workItems.TryGetValue(processId, out var workItem))
                    {
                        workItem.Status = newStatus;
                    }
                }
                finally
                {
                    semaphore.Release();

                    if (isTerminal(newStatus))
                    {
                        // Whatever path got the item here (including canceled before it ever started), it is finished and must not stay in the store.
                        _workItems.TryRemove(processId, out _);
                    }
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