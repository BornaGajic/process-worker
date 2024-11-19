using Microsoft.Extensions.DependencyInjection;
using ProcessWorker.Common;
using ProcessWorker.Extensions;
using ProcessWorker.Model;
using System.Threading.Channels;

namespace ProcessWorker.Service
{
    internal class ProcessWorkerConsumer : IProcessWorkerConsumer
    {
        internal readonly ChannelReader<WorkItem> ChannelReader;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public ProcessWorkerConsumer(ChannelReader<WorkItem> channelReader, IServiceScopeFactory serviceScopeFactory, ProcessWorkerConfiguration configuration = default)
        {
            ChannelReader = channelReader;
            _serviceScopeFactory = serviceScopeFactory;
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

                        _ = RunWorkerAsync(_serviceScopeFactory, workItem)
                            .ContinueWith(result => semaphore.Release());
                    }
                    catch (Exception) when (CancellationTokenSource.Token.IsCancellationRequested)
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
                            using var scope = _serviceScopeFactory.CreateScope();

                            // Flush all work items and transition them to "Fatal" state
                            // if we want to be sure that producer aknowledged that the fatal error has occured we can call
                            // .Complete(Exception) on the producer's side
                            // if so, await foreach will throw an error.
                            await foreach (var item in ChannelReader.ReadAllAsync())
                            {
                                await item.Progress(ProcessStatus.Fatal, scope.ServiceProvider, fatalEx);
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

        private static async Task RunWorkerAsync(IServiceScopeFactory serviceScopeFactory, WorkItem item)
        {
            if (item.ProcessMetadata.IsCanceledBeforeRunning)
            {
                return;
            }

            var asyncScope = serviceScopeFactory.CreateAsyncScope();

            try
            {
                if (item.ProcessMetadata.ProcessInfo.StoppingToken.IsCancellationRequested)
                {
                    await item.Progress(ProcessStatus.Canceled, asyncScope.ServiceProvider);
                    item.TaskCompletionSrc.TrySetCanceled();
                }
                else
                {
                    await item.Progress(ProcessStatus.Running, asyncScope.ServiceProvider);

                    await item.ProcessMetadata.DoWorkAsync(asyncScope.ServiceProvider, item.ProcessMetadata.ProcessInfo.StoppingToken);

                    await item.Progress(ProcessStatus.Done, asyncScope.ServiceProvider);
                    item.TaskCompletionSrc.TrySetResult();
                }
            }
            catch (Exception opEx) when (item.CancellationTokenSrc.IsCancellationRequested)
            {
                await item.Progress(ProcessStatus.Canceled, asyncScope.ServiceProvider, opEx);
                item.TaskCompletionSrc.TrySetCanceled();
            }
            catch (Exception e)
            {
                await item.Progress(ProcessStatus.Failed, asyncScope.ServiceProvider, e);
                item.TaskCompletionSrc.TrySetException(e);
            }
            finally
            {
                // Once the TaskCompletionSrc is GC'd the finalizer will not throw UnobservedTaskException if the user did not handle the exception.
                // This means that all unhandled exceptions should be handled via Completion by awaiting the task.
                // There are two cases where Unobserver can happen:
                // 1. The user handled the exception inside the callback, but rethrown it and did not handle it on the Completion object.
                // 2. The user did not handle the exception inside the callback and did not handle the exception on the Completion object.
                item.TaskCompletionSrc.Task.IgnoreUnobservedExceptions();
                await asyncScope.DisposeAsync();
                item.Dispose();
            }
        }

        private void OnFatalErrorOccured(Exception ex)
        {
            FatalException = ex;
            FatalErrorOccured?.Invoke(this, FatalException);
        }
    }
}