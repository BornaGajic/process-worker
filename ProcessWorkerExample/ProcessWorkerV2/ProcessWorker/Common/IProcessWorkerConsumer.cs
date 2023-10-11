namespace ProcessWorkerV2;

public interface IProcessWorkerConsumer
{
    void TryCreateConsumingThread();
}