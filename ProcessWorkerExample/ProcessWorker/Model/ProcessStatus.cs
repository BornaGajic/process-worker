namespace ProcessWorker.Model;

[Flags]
public enum ProcessStatus
{
    Queued = 0,
    Running = 1 << 0,
    Done = 1 << 1,
    Canceled = 1 << 2,

    /// <summary>
    /// Work item's process failed.
    /// </summary>
    Failed = 1 << 3,

    /// <summary>
    /// Time between request for the cancellation and the actual cancellation.
    /// </summary>
    CancellationRequested = 1 << 4,

    /// <summary>
    /// Engine failed.
    /// </summary>
    Fatal = 1 << 5
}