namespace ProcessWorker.Model;

public record ProcessWorkerInfo
{
    public CancellationToken StoppingToken { get; init; }

    /// <summary>
    /// Asynchronously wait for process to finish
    /// </summary>
    public Task Completion { get; init; }

    /// <summary>
    /// Uniquely identifies a process
    /// </summary>
    public Guid ProcessId { get; init; }
}