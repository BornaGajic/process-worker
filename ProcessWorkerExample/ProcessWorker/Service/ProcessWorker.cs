using ProcessWorker.Common;
using ProcessWorker.Model;
using System.Threading.Channels;

namespace ProcessWorker.Service
{
    public static class ProcessWorker
    {
        public static IProcessWorker Create(ProcessWorkerConfiguration configuration = default)
        {
            var unBoundedChannel = Channel.CreateUnbounded<WorkItem>();

            var producer = new ProcessWorkerProducer(unBoundedChannel.Writer);
            var consumer = new ProcessWorkerConsumer(unBoundedChannel.Reader, configuration);

            consumer.FatalErrorOccured += (sender, ex) => producer.StopProducing(ex);
            consumer.TryCreateConsumingThread();

            return new ProcessWorkerDefault
            {
                Consumer = consumer,
                Producer = producer
            };
        }
    }

    internal class ProcessWorkerDefault : IProcessWorker
    {
        public IProcessWorkerConsumer Consumer { get; internal set; }
        public bool IsOperational => Consumer.IsOperational && Producer.IsOperational;
        public IProcessWorkerProducer Producer { get; internal set; }

        public void Dispose() => Consumer.Dispose();
    }
}