namespace ProcessWorkerV2;

internal class WorkItem
{
    private readonly Func<ProcessStatus, Exception, Task> _progress;

    public WorkItem(Func<ProcessStatus, Exception, Task> progress)
    {
        _progress = progress;
    }

    public CancellationTokenSource CancellationTokenSrc { get; init; }
    public ProcessMetadata ProcessMetadata { get; init; }
    public ProcessStatus Status { get; set; }
    public TaskCompletionSource TaskCompletionSrc { get; init; }

    public Task Progress(ProcessStatus newStatus, Exception processException = null)
    {
        Status = newStatus;

        return _progress(newStatus, processException);
    }
}