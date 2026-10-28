using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using ProcessWorker.Common;
using ProcessWorker.Model;
using ProcessWorker.Startup;
using System.Collections.Concurrent;
using Service = ProcessWorker.Service;
using Xunit;

namespace Specs
{
    public class ProcessWorkerProducerTest : TestSetup
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly IServiceScopeFactory _serviceScopeFactory;

        public ProcessWorkerProducerTest()
        {
            var container = SetupContainer(builder =>
            {
                builder.RegisterProcessWorker();
            });
            _serviceScopeFactory = container.GetRequiredService<IServiceScopeFactory>();
        }

        [Fact(DisplayName = "W01: Successful item reports Queued, Running, Done")]
        public async Task W01()
        {
            using var processWorker = CreateWorker(concurrency: 1);
            var statuses = new StatusLog();

            var info = await processWorker.Producer.EnqueueAsync(async (svc, cancellation) => await Task.Yield(), statuses.Callback);

            await info.Completion;

            statuses.Items.Should().Equal(ProcessStatus.Queued, ProcessStatus.Running, ProcessStatus.Done);
        }

        [Fact(DisplayName = "W02: Failing item faults its completion, reports Failed and does not take the worker down")]
        public async Task W02()
        {
            using var processWorker = CreateWorker(concurrency: 1);
            var statuses = new StatusLog();

            var failing = await processWorker.Producer.EnqueueAsync(
                (svc, cancellation) => throw new InvalidOperationException("boom"),
                statuses.Callback
            );

            Func<Task> awaitFailing = () => failing.Completion;

            (await awaitFailing.Should().ThrowAsync<InvalidOperationException>()).WithMessage("boom");

            statuses.Items.Should().Equal(ProcessStatus.Queued, ProcessStatus.Running, ProcessStatus.Failed);
            processWorker.IsOperational.Should().BeTrue();

            var ran = false;
            var next = await processWorker.Producer.EnqueueAsync(async (svc, cancellation) =>
            {
                ran = true;
                await Task.Yield();
            });

            await next.Completion;

            ran.Should().BeTrue();
        }

        [Fact(DisplayName = "W03: Never runs more items at once than the configured concurrency")]
        public async Task W03()
        {
            const int concurrency = 2;

            using var processWorker = CreateWorker(concurrency);

            var current = 0;
            var max = 0;

            var completions = new List<Task>();

            foreach (var _ in Enumerable.Range(1, 6))
            {
                var info = await processWorker.Producer.EnqueueAsync(async (svc, cancellation) =>
                {
                    var now = Interlocked.Increment(ref current);

                    int seen;
                    while (now > (seen = Volatile.Read(ref max)) && Interlocked.CompareExchange(ref max, now, seen) != seen) { }

                    await Task.Delay(150, cancellation);

                    Interlocked.Decrement(ref current);
                });

                completions.Add(info.Completion);
            }

            await Task.WhenAll(completions);

            max.Should().Be(concurrency);
        }

        [Fact(DisplayName = "W04: Canceling a running item reports CancellationRequested then Canceled and is a no-op afterwards")]
        public async Task W04()
        {
            using var processWorker = CreateWorker(concurrency: 1);
            var statuses = new StatusLog();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var info = await processWorker.Producer.EnqueueAsync(
                async (svc, cancellation) =>
                {
                    started.SetResult();
                    await Task.Delay(System.Threading.Timeout.Infinite, cancellation);
                },
                statuses.Callback
            );

            await started.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

            await processWorker.Producer.CancelWorkItemAsync(info.ProcessId);

            Func<Task> awaitItem = () => info.Completion;

            await awaitItem.Should().ThrowAsync<OperationCanceledException>();

            statuses.Items.Should().Equal(
                ProcessStatus.Queued,
                ProcessStatus.Running,
                ProcessStatus.CancellationRequested,
                ProcessStatus.Canceled
            );

            await processWorker.Producer.CancelWorkItemAsync(info.ProcessId);

            statuses.Items.Should().HaveCount(4);
            processWorker.IsOperational.Should().BeTrue();
        }

        [Fact(DisplayName = "W05: Canceling an unknown item does nothing")]
        public async Task W05()
        {
            using var processWorker = CreateWorker(concurrency: 1);

            Func<Task> cancel = () => processWorker.Producer.CancelWorkItemAsync(Guid.NewGuid());

            await cancel.Should().NotThrowAsync();
            processWorker.IsOperational.Should().BeTrue();
        }

        [Fact(DisplayName = "W06: Canceling a finished item does not report anything")]
        public async Task W06()
        {
            using var processWorker = CreateWorker(concurrency: 1);
            var statuses = new StatusLog();

            var info = await processWorker.Producer.EnqueueAsync(async (svc, cancellation) => await Task.Yield(), statuses.Callback);

            await info.Completion;

            var reported = statuses.Items.ToArray();

            await processWorker.Producer.CancelWorkItemAsync(info.ProcessId);

            statuses.Items.Should().Equal(reported);
        }

        [Fact(DisplayName = "W07: Item that starts right after a cancellation was requested is not reported as running")]
        public async Task W07()
        {
            // The consumer creates its scope right after it claimed the item and right before it reports the item as running,
            // so holding that scope creation holds the consumer exactly between the two.
            var gatedFactory = new GatedScopeFactory(_serviceScopeFactory, gatedCall: 2);

            using var processWorker = Service.ProcessWorker.Create(gatedFactory, new ProcessWorkerConfiguration { Concurrency = 1 });

            var statuses = new StatusLog();
            var cancelCallbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCancelCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var info = await processWorker.Producer.EnqueueAsync(
                async (svc, cancellation) => await Task.Delay(System.Threading.Timeout.Infinite, cancellation),
                async (processId, status, svc, exception) =>
                {
                    if (status is ProcessStatus.CancellationRequested)
                    {
                        cancelCallbackEntered.SetResult();
                        await releaseCancelCallback.Task;
                    }

                    statuses.Items.Enqueue(status);
                }
            );

            // The consumer owns the item now, but did not report it as running yet.
            await gatedFactory.Entered.WaitAsync(Timeout, TestContext.Current.CancellationToken);

            var cancelTask = processWorker.Producer.CancelWorkItemAsync(info.ProcessId);

            // The cancellation request is being reported (held), the token is not canceled yet.
            await cancelCallbackEntered.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

            gatedFactory.Release();

            // Lets the consumer queue its running update behind the cancellation request.
            await Task.Delay(200, TestContext.Current.CancellationToken);

            releaseCancelCallback.SetResult();

            Func<Task> awaitItem = () => info.Completion;

            await awaitItem.Should().ThrowAsync<OperationCanceledException>();
            await cancelTask;

            statuses.Items.Should().Equal(ProcessStatus.Queued, ProcessStatus.CancellationRequested, ProcessStatus.Canceled);
        }

        private IProcessWorker CreateWorker(int concurrency)
            => Service.ProcessWorker.Create(_serviceScopeFactory, new ProcessWorkerConfiguration { Concurrency = concurrency });

        private sealed class StatusLog
        {
            public ConcurrentQueue<ProcessStatus> Items { get; } = new();

            public Func<Guid, ProcessStatus, IServiceProvider, Exception, ValueTask> Callback => (processId, status, svc, exception) =>
            {
                Items.Enqueue(status);

                return ValueTask.CompletedTask;
            };
        }

        /// <summary>
        /// Holds the Nth scope creation until released, everything else goes straight through.
        /// </summary>
        private sealed class GatedScopeFactory : IServiceScopeFactory
        {
            private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly int _gatedCall;
            private readonly ManualResetEventSlim _gate = new(false);
            private readonly IServiceScopeFactory _inner;
            private int _calls;

            public GatedScopeFactory(IServiceScopeFactory inner, int gatedCall)
            {
                _inner = inner;
                _gatedCall = gatedCall;
            }

            public Task Entered => _entered.Task;

            public IServiceScope CreateScope()
            {
                if (Interlocked.Increment(ref _calls) == _gatedCall)
                {
                    _entered.TrySetResult();
                    _gate.Wait();
                }

                return _inner.CreateScope();
            }

            public void Release() => _gate.Set();
        }
    }
}
