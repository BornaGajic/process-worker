using ProcessWorker.Common;
using ProcessWorker.Model;
using System.Threading.Channels;

namespace ProcessWorker.Service
{
    internal class ProcessWorkerConsumer : IProcessWorkerConsumer
    {
        internal readonly ChannelReader<WorkItem> ChannelReader;

        public ProcessWorkerConsumer(ChannelReader<WorkItem> channelReader, ProcessWorkerConfiguration configuration = default)
        {
            ChannelReader = channelReader;
            Configuration = configuration;
        }

        public event EventHandler<Exception> FatalErrorOccured;

        public Task Completion { get; private set; }
        public ProcessWorkerConfiguration Configuration { get; }
        public Exception FatalException { get; internal set; }
        public bool IsOperational => FatalException is null && !CancellationTokenSource.IsCancellationRequested;

        private CancellationTokenSource CancellationTokenSource { get; } = new();

        public void Dispose() => CancellationTokenSource.Cancel();

        public bool TryCreateConsumingThread()
        {
            if (Completion is not null)
            {
                return false;
            }

            Completion = Task.Run(async () =>
            {
                using var semaphore = new SemaphoreSlim(Configuration.Concurrency, Configuration.Concurrency);

                while (!CancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await ChannelReader.WaitToReadAsync(CancellationTokenSource.Token);

                        await semaphore.WaitAsync(CancellationTokenSource.Token);

                        var workItem = await ChannelReader.ReadAsync(CancellationTokenSource.Token);

                        _ = RunWorkerAsync(workItem)
                            .ContinueWith(result => semaphore.Release());
                    }
                    catch (OperationCanceledException) when (CancellationTokenSource.Token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception fatalEx)
                    {
                        // Notify that a fatal error occured, this will clear all work items from the producer's store and set the
                        // channel to the completed state
                        OnFatalErrorOccured(fatalEx);

                        try
                        {
                            // Flush all work items and transition them to "Fatal" state
                            // if we want to be sure that producer aknowledged that the fatal error has occured we can call
                            // .Complete(Exception) on the producer's side
                            // if so, await foreach will throw an error.
                            await foreach (var item in ChannelReader.ReadAllAsync())
                            {
                                await item.Progress(ProcessStatus.Fatal, fatalEx);
                                item.TaskCompletionSrc.TrySetException(fatalEx);
                                item.Dispose();
                            }
                        }
                        // All good, no need to re-throw. ReadAllAsync will throw if .Complete(Exception) is used.
                        catch (Exception innerEx) when (innerEx == fatalEx) { }
                        finally
                        {
                            // Clear everything (WaitToReadAsync, WaitAsync, ReadAsync...)
                            CancellationTokenSource.Cancel();
                        }
                    }
                }
            });

            return true;
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

                    await item.Progress(ProcessStatus.Done);
                    item.TaskCompletionSrc.TrySetResult();
                }
            }
            catch (OperationCanceledException) when (item.CancellationTokenSrc.IsCancellationRequested)
            {
                await item.Progress(ProcessStatus.Canceled);
                item.TaskCompletionSrc.TrySetCanceled();
            }
            catch (Exception e)
            {
                await item.Progress(ProcessStatus.Failed, e);
                item.TaskCompletionSrc.TrySetException(e);
            }
        }

        private void OnFatalErrorOccured(Exception ex)
        {
            FatalException = ex;
            FatalErrorOccured?.Invoke(this, FatalException);
        }
    }
}