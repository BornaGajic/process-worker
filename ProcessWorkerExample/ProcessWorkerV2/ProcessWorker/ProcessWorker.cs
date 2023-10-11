using System.Threading.Channels;

namespace ProcessWorkerV2
{
    public class ProcessWorker : IProcessWorker
    {
        public ProcessWorker(ProcessWorkerConfiguration configuration)
        {
            var unBoundedChannel = Channel.CreateUnbounded<WorkItem>();

            Producer = new ProcessWorkerProducer(unBoundedChannel.Writer);
            Consumer = new ProcessWorkerConsumer(unBoundedChannel.Reader, configuration);
        }

        public IProcessWorkerConsumer Consumer { get; protected set; }
        public IProcessWorkerProducer Producer { get; protected set; }
    }
}