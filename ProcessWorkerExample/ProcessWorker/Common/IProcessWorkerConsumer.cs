namespace ProcessWorker.Common;

public interface IProcessWorkerConsumer : IDisposable
{
    event EventHandler<Exception> FatalErrorOccured;

    /// <summary>
    /// Consumer; completed only when the current instance stops processing requests.
    /// </summary>
    Task Completion { get; }

    bool IsOperational { get; }
}