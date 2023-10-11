using System.Threading.Channels;

namespace ProcessWorkerV2
{
    internal class ProcessWorkerConsumer : IProcessWorkerConsumer
    {
        public readonly ChannelReader<WorkItem> ChannelReader;

        public ProcessWorkerConsumer(ChannelReader<WorkItem> channelReader, ProcessWorkerConfiguration configuration)
        {
            ChannelReader = channelReader;
            Configuration = configuration;
        }

        public ProcessWorkerConfiguration Configuration { get; }
        private bool ThreadExists { get; set; }

        public void TryCreateConsumingThread()
        {
            if (ThreadExists)
            {
                return;
            }

            var workerThread = new Thread(async () =>
            {
                using var semaphore = new SemaphoreSlim(Configuration.Concurrency, Configuration.Concurrency);

                while (true)
                {
                    await ChannelReader.WaitToReadAsync();

                    await semaphore.WaitAsync();

                    var workItem = await ChannelReader.ReadAsync();

                    _ = RunWorkerAsync(workItem)
                        .ContinueWith(result => semaphore.Release());
                }
            });

            workerThread.IsBackground = true;
            workerThread.Priority = ThreadPriority.Normal;

            ThreadExists = true;

            workerThread.Start();
        }

        private static async Task RunWorkerAsync(WorkItem item)
        {
            if (item.ProcessMetadata.IsCanceledBeforeRunning)
            {
                return;
            }

            try
            {
                if (item.ProcessMetadata.ProcessInfo.StoppingToken.IsCancellationRequested)
                {
                    await item.Progress(ProcessStatus.Canceled);
                    item.TaskCompletionSrc.TrySetCanceled();
                }
                else
                {
                    await item.Progress(ProcessStatus.Running);

                    await item.ProcessMetadata.DoWorkAsync(item.ProcessMetadata.ProcessInfo.StoppingToken);

                    if (item.ProcessMetadata.ProcessInfo.StoppingToken.IsCancellationRequested)
                    {
                        await item.Progress(ProcessStatus.Canceled);
                        item.TaskCompletionSrc.TrySetCanceled();
                    }
                    else
                    {
                        await item.Progress(ProcessStatus.Done);
                        item.TaskCompletionSrc.TrySetResult();
                    }
                }
            }
            catch (Exception e)
            {
                await item.Progress(ProcessStatus.Failed, e);
                item.TaskCompletionSrc.TrySetException(e);
            }
        }
    }
}