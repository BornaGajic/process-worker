namespace ProcessWorker.Model;

internal class ProcessMetadata
{
    public Func<CancellationToken, Task> DoWorkAsync { get; init; }
    public bool IsCanceledBeforeRunning { get; set; }
    public ProcessWorkerInfo ProcessInfo { get; set; }
}