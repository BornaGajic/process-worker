using ProcessWorker.Model;

namespace ProcessWorker.Service;

public interface IProcessWorkerProvider : IDisposable
{
    IProcessWorker GetOrCreate(string key);

    IProcessWorker GetOrCreate(string key, ProcessWorkerConfiguration configuration);
}
