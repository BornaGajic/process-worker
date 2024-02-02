namespace ProcessWorker.Model;

internal class WorkItem : IDisposable
{
    private readonly Func<ProcessStatus, Exception, Task> _progress;
    private bool _disposed;

    public WorkItem(Func<ProcessStatus, Exception, Task> progress)
    {
        _progress = progress;
    }

    public CancellationTokenSource CancellationTokenSrc { get; internal set; }
    public ProcessMetadata ProcessMetadata { get; init; }
    public ProcessStatus Status { get; set; }
    public TaskCompletionSource TaskCompletionSrc { get; init; }

    public void Dispose() => Dispose(true);

    public Task Progress(ProcessStatus newStatus, Exception processException = null)
    {
        Status = newStatus;

        return _progress(newStatus, processException);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                CancellationTokenSrc.Dispose();
                CancellationTokenSrc = null;
            }

            _disposed = true;
        }
    }
}