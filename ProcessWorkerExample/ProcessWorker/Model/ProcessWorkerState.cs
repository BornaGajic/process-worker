namespace ProcessWorker.Model;

public record ProcessWorkerState
{
    public TimeSpan? Duration
    {
        get => EndTime - StartTime;
    }
    public ProcessStatus Status { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public Guid ProcessId { get; init; }
}
