namespace ProcessWorkerV2;

public record ProcessWorkerState
{
    public TimeSpan? Duration
    {
        get => EndTime - StartTime;
    }
    public ProcessStatus Status { get; set; }
    public Exception Error { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public Guid ProcessId { get; init; }
    public string UserName { get; init; }
    public int UserId { get; init; }
    public string WorkerName { get; init; }
}