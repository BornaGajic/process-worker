namespace ProcessWorker.Model;

public record ProcessInfo
{
    public CancellationToken StoppingToken { get; init; }

    /// <summary>
    /// Asynchronously wait for process to finish
    /// </summary>
    public Task WaitingToken { get; init; }

    /// <summary>
    /// Uniquely identifies a process
    /// </summary>
    public Guid ProcessId { get; init; }
}
