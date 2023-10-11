using System.Collections.Concurrent;

namespace ProcessWorkerV2
{
    public class ProcessWorkerProvider : IProcessWorkerProvider
    {
        private static readonly ConcurrentDictionary<string, IProcessWorker> _processWorkerCache = new();

        public IProcessWorkerProducer GetOrCreate(string key) => GetOrCreate(key, new());

        public IProcessWorkerProducer GetOrCreate(string key, ProcessWorkerConfiguration configuration)
        {
            var instanceExists = _processWorkerCache.ContainsKey(key);
            var processWorker = _processWorkerCache.GetOrAdd(key, new ProcessWorker(configuration));

            if (!instanceExists)
            {
                processWorker.Consumer.TryCreateConsumingThread();
            }

            return processWorker.Producer;
        }
    }
}