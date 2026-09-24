using ProcessWorker.Model;

namespace ProcessWorker.Common;

public interface IProcessWorkerProvider
{
    IProcessWorker GetOrCreate(string key, ProcessWorkerConfiguration configuration = default);

    bool Remove(string key);
}