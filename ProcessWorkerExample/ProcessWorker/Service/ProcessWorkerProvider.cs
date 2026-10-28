using Microsoft.Extensions.DependencyInjection;
using ProcessWorker.Common;
using ProcessWorker.Model;
using System.Collections.Concurrent;

namespace ProcessWorker.Service
{
    public class ProcessWorkerProvider : IProcessWorkerProvider
    {
        private static readonly ConcurrentDictionary<string, Lazy<IProcessWorker>> _store = new();

        private readonly IServiceScopeFactory _serviceScopeFactory;

        public ProcessWorkerProvider(IServiceScopeFactory serviceScopeFactory) => _serviceScopeFactory = serviceScopeFactory;

        public IProcessWorker GetOrCreate(string key, ProcessWorkerConfiguration configuration = default)
        {
            // Lazy makes sure only one worker is ever created per key, even when it is requested from several threads at once.
            var worker = _store.GetOrAdd(key, _ => new Lazy<IProcessWorker>(() => ProcessWorker.Create(_serviceScopeFactory, configuration ?? new())));

            try
            {
                return worker.Value;
            }
            catch
            {
                // Don't keep a failed creation cached, so the next call retries.
                _store.TryRemove(new KeyValuePair<string, Lazy<IProcessWorker>>(key, worker));
                throw;
            }
        }

        public bool Remove(string key)
        {
            if (_store.TryRemove(key, out var processWorker))
            {
                // A worker that was never created (or failed to be created) has nothing to dispose.
                if (processWorker.IsValueCreated)
                    processWorker.Value.Dispose();

                return true;
            }
            else
            {
                return false;
            }
        }
    }
}