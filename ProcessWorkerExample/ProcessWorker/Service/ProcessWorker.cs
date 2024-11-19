using Microsoft.Extensions.DependencyInjection;
using ProcessWorker.Common;
using ProcessWorker.Model;
using System.Threading.Channels;

namespace ProcessWorker.Service
{
    public static class ProcessWorker
    {
        public static IProcessWorker Create(IServiceScopeFactory serviceScopeFactory, ProcessWorkerConfiguration configuration = default)
        {
            var unBoundedChannel = Channel.CreateUnbounded<WorkItem>();

            var producer = new ProcessWorkerProducer(unBoundedChannel.Writer, serviceScopeFactory);
            var consumer = new ProcessWorkerConsumer(unBoundedChannel.Reader, serviceScopeFactory, configuration);

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