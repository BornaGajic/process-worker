namespace ProcessWorker.Model;

internal class WorkItem : IDisposable
{
    private readonly Func<ProcessStatus, IServiceProvider, Exception, Task> _progress;
    private bool _disposed;

    public WorkItem(Func<ProcessStatus, IServiceProvider, Exception, Task> progress)
    {
        _progress = progress;
    }

    public CancellationTokenSource CancellationTokenSrc { get; internal set; }
    public ProcessMetadata ProcessMetadata { get; init; }
    public ProcessStatus Status { get; set; }
    public TaskCompletionSource TaskCompletionSrc { get; init; }

    public void Dispose() => Dispose(true);

    public Task Progress(ProcessStatus newStatus, IServiceProvider serviceProvider, Exception processException = null)
    {
        Status = newStatus;

        return _progress(newStatus, serviceProvider, processException);
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