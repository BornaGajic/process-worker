using Microsoft.Extensions.DependencyInjection;
using ProcessWorker.Common;
using ProcessWorker.Model;
using System.Collections.Concurrent;

namespace ProcessWorker.Service
{
    public class ProcessWorkerProvider : IProcessWorkerProvider
    {
        private static readonly ConcurrentDictionary<string, IProcessWorker> _store = new();

        private readonly IServiceScopeFactory _serviceScopeFactory;

        public ProcessWorkerProvider(IServiceScopeFactory serviceScopeFactory) => _serviceScopeFactory = serviceScopeFactory;

        public IProcessWorker GetOrCreateCached(string key, ProcessWorkerConfiguration configuration = default)
        {
            return _store.GetOrAdd(key, ProcessWorker.Create(_serviceScopeFactory, configuration ?? new()));
        }

        public bool Remove(string key)
        {
            if (_store.TryRemove(key, out var processWorker))
            {
                processWorker.Dispose();
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}