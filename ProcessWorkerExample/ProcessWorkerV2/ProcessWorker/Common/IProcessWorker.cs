namespace ProcessWorkerV2;

public interface IProcessWorker
{
    IProcessWorkerConsumer Consumer { get; }
    IProcessWorkerProducer Producer { get; }
}