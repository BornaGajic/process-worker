namespace ProcessWorker.Model;

public record ProcessWorkerConfiguration
{
    /// <summary>
    /// How many items should be ran at the same time? MinValue = <c>1</c>, MaxValue = <c>10</c>.
    /// </summary>
    public int Concurrency { get; init; } = 3;

    /// <summary>
    /// Interval in milliseconds, length of time between worker getting its next batch of work items. MinValue = <c>1_000</c>.
    /// </summary>
    public int TimerIntervalMs { get; init; } = 15_000;
}
