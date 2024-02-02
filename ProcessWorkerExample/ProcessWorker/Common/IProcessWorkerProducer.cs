using ProcessWorker.Model;

namespace ProcessWorker.Common;

public interface IProcessWorkerProducer
{
    bool IsOperational { get; }

    /// <summary>
    /// Cancels work item and removes it from queue.
    /// </summary>
    void CancelWorkItemAsync(Guid processId, int? millisecondsDelay = null);

    /// <summary>
    /// Enqueues <c>workItem</c> to worker's queue with default state.
    /// </summary>
    Task<ProcessWorkerInfo> EnqueueAsync(Func<CancellationToken, Task> process);

    /// <summary>
    /// Enqueues <c>workItem</c> to worker's queue with progress callback and default state.
    /// </summary>
    Task<ProcessWorkerInfo> EnqueueAsync(Func<CancellationToken, Task> process, Func<Guid, ProcessStatus, Task> progressCallback);

    /// <summary>
    /// Enqueues <c>workItem</c> to worker's queue with progress callback and custom state.
    /// </summary>
    Task<ProcessWorkerInfo> EnqueueAsync<TState>(Func<CancellationToken, Task> process, Func<Guid, ProcessStatus, Task<TState>> statusChangeCallback)
        where TState : ProcessWorkerState;
}