using ProcessWorker.Model;

namespace ProcessWorker.Common;

public interface IProcessWorkerProvider
{
    IProcessWorker GetOrCreateCached(string key, ProcessWorkerConfiguration configuration = default);

    bool Remove(string key);
}