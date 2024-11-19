using ProcessWorker.Model;

namespace ProcessWorker.Common;

public interface IProcessWorkerProducer
{
    bool IsOperational { get; }

    /// <summary>
    /// Cancels work item and removes it from queue.
    /// </summary>
    Task CancelWorkItemAsync(Guid processId, int? millisecondsDelay = null);

    /// <summary>
    /// Enqueues <c>workItem</c> to worker's queue with default state.
    /// </summary>
    Task<ProcessWorkerInfo> EnqueueAsync(Func<IServiceProvider, CancellationToken, ValueTask> process);

    /// <summary>
    /// Enqueues <c>workItem</c> to worker's queue with progress callback and default state.
    /// </summary>
    Task<ProcessWorkerInfo> EnqueueAsync(Func<IServiceProvider, CancellationToken, ValueTask> process, Func<Guid, ProcessStatus, IServiceProvider, Exception, ValueTask> progressCallback);

    /// <summary>
    /// Enqueues <c>workItem</c> to worker's queue with progress callback and custom state.
    /// </summary>
    Task<ProcessWorkerInfo> EnqueueAsync<TState>(Func<IServiceProvider, CancellationToken, ValueTask> process, Func<Guid, ProcessStatus, IServiceProvider, Exception, ValueTask<TState>> statusChangeCallback)
        where TState : ProcessWorkerState;
}