namespace ProcessWorker.Common;

public interface IProcessWorker : IDisposable
{
    IProcessWorkerConsumer Consumer { get; }
    bool IsOperational { get; }
    IProcessWorkerProducer Producer { get; }
}