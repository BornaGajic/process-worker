namespace ProcessWorker.Model;

internal class WorkItem : IDisposable
{
    private readonly Func<ProcessStatus, IServiceProvider, Exception, Task> _progress;
    private int _claimed;
    private bool _disposed;

    public WorkItem(Func<ProcessStatus, IServiceProvider, Exception, Task> progress)
    {
        _progress = progress;
    }

    ~WorkItem() => Dispose();

    public CancellationTokenSource CancellationTokenSrc { get; internal set; }
    public ProcessMetadata ProcessMetadata { get; init; }
    public ProcessStatus Status { get; set; }
    public TaskCompletionSource TaskCompletionSrc { get; init; }

    public void Dispose() => Dispose(true);

    /// <summary>
    /// Claims ownership of a queued item. Either the consumer that is about to run it or the cancellation of a still
    /// queued item gets it, never both, so an item can't be run and canceled-and-disposed at the same time.
    /// </summary>
    public bool TryClaim() => Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;

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
                CancellationTokenSrc?.Dispose();
                CancellationTokenSrc = null;
            }

            _disposed = true;
        }
    }
}