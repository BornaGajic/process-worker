namespace ProcessWorkerV2;

public record ProcessWorkerConfiguration
{
    /// <summary>
    /// How many items should be ran at the same time?
    /// </summary>
    public int Concurrency { get; init; } = 3;
}