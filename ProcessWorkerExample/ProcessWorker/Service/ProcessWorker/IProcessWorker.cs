using ProcessWorker.Model;

namespace ProcessWorker.Service;

public interface IProcessWorker : IDisposable
{
    /// <summary>
    /// Cancels work item and removes it from queue.
    /// </summary>
    Task CancelWorkItemAsync(Guid processId, int? millisecondsDelay = null);

    /// <summary>
    /// Enqueues <c>workItem</c> to worker's queue. <b>Do NOT await the same instance of <c>ValueTask</c> twice!</b>
    /// </summary>
    ProcessInfo EnqueueWorkItem<TState>(Func<CancellationToken, Task> process, ProcessWorkerStatusChangeCallback<TState> statusChangeCallback = null)
        where TState : ProcessWorkerState;
}
