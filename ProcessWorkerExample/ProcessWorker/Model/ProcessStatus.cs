namespace ProcessWorker.Model;

[Flags]
public enum ProcessStatus
{
    Queued = 0,
    Running = 1 << 0,
    Done = 1 << 1,
    Canceled = 1 << 2,
    Failed = 1 << 3,
    CancellationRequested = 1 << 4,
}