namespace ProcessWorkerV2;

public interface IProcessWorkerProvider
{
    IProcessWorkerProducer GetOrCreate(string key);

    IProcessWorkerProducer GetOrCreate(string key, ProcessWorkerConfiguration configuration);
}