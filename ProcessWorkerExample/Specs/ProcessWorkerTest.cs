using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using ProcessWorker.Common;
using ProcessWorker.Model;
using ProcessWorker.Startup;
using Service = ProcessWorker.Service;
using Xunit;

namespace Specs
{
    public class ProcessWorkerTest : TestSetup, IDisposable
    {
        private readonly IServiceProvider _container;
        private readonly IProcessWorker _processWorkerMT;
        private readonly IProcessWorker _processWorkerST;
        private readonly IProcessWorkerProvider _provider;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public ProcessWorkerTest()
        {
            _container = SetupContainer(builder =>
            {
                builder.RegisterProcessWorker();
            });
            _serviceScopeFactory = _container.GetRequiredService<IServiceScopeFactory>();
            _provider = _container.GetRequiredService<IProcessWorkerProvider>();
            _processWorkerMT = _provider.GetOrCreate($"MT-{nameof(ProcessWorkerTest)}", new ProcessWorkerConfiguration
            {
                Concurrency = 3
            });
            _processWorkerST = _provider.GetOrCreate($"ST-{nameof(ProcessWorkerTest)}", new ProcessWorkerConfiguration
            {
                Concurrency = 1
            });
        }

        public void Dispose()
        {
            _provider.Remove($"MT-{nameof(ProcessWorkerTest)}").Should().BeTrue();
            _provider.Remove($"ST-{nameof(ProcessWorkerTest)}").Should().BeTrue();
            _processWorkerMT.IsOperational.Should().BeFalse();
            _processWorkerST.IsOperational.Should().BeFalse();
        }

        [Fact(DisplayName = $"T01: MT-{nameof(Service.ProcessWorker)}")]
        public async Task T01()
        {
            int numOfIntegers = 10;
            var expectedResult = Enumerable.Range(1, numOfIntegers).Sum(i => i); // 55

            var sumIntegersMtTest = await SumFirstNIntegers(numOfIntegers, useParallel: true);

            sumIntegersMtTest.Should().Be(expectedResult);
        }

        [Fact(DisplayName = $"T02: ST-{nameof(Service.ProcessWorker)}")]
        public async Task T02()
        {
            int numOfIntegers = 10;
            var expectedResult = Enumerable.Range(1, numOfIntegers).Sum(i => i); // 55

            var sumIntegersStTest = await SumFirstNIntegers(numOfIntegers, useParallel: false);

            sumIntegersStTest.Should().Be(expectedResult);
        }

        [Fact(DisplayName = $"T03: Cancel {nameof(Service.ProcessWorker)}")]
        public async Task T03()
        {
            using var processWorker = Service.ProcessWorker.Create(_serviceScopeFactory, new ProcessWorkerConfiguration
            {
                Concurrency = 1
            });

            var info = await processWorker.Producer.EnqueueAsync(async (svc, cancellation) =>
            {
                foreach (var item in Enumerable.Range(1, 20))
                {
                    cancellation.ThrowIfCancellationRequested();
                    await Task.Delay(500);
                }
            });

            await Task.Delay(1_000, TestContext.Current.CancellationToken);

            await processWorker.Producer.CancelWorkItemAsync(info.ProcessId, 250);

            var threwOperationCanceledException = false;

            try
            {
                await info.Completion;
            }
            catch (Exception ex)
            {
                ex.Should().BeAssignableTo<OperationCanceledException>();
                threwOperationCanceledException = true;
            }

            threwOperationCanceledException.Should().BeTrue();
            processWorker.IsOperational.Should().BeTrue();
        }

        [Fact(DisplayName = $"T04: {nameof(ProcessWorker)} consumer completion")]
        public async Task T04()
        {
            var processWorker = Service.ProcessWorker.Create(_serviceScopeFactory, new ProcessWorkerConfiguration
            {
                Concurrency = 1
            });

            processWorker.IsOperational.Should().BeTrue();

            int result = 0;
            var info = await processWorker.Producer.EnqueueAsync(async (svc, cancellation) =>
            {
                await Task.Delay(500);
                result = 42;
            });

            await info.Completion;

            processWorker.Dispose();

            await processWorker.Consumer.Completion;

            result.Should().Be(42);
            processWorker.IsOperational.Should().BeFalse();
        }

        [Fact(DisplayName = $"T05: Cancel queued {nameof(Service.ProcessWorker)} item")]
        public async Task T05()
        {
            using var processWorker = Service.ProcessWorker.Create(_serviceScopeFactory, new ProcessWorkerConfiguration
            {
                Concurrency = 1
            });

            // Occupies the only slot, so the next item stays queued.
            var release = new TaskCompletionSource();
            var blocker = await processWorker.Producer.EnqueueAsync(async (svc, cancellation) =>
            {
                await release.Task.WaitAsync(cancellation);
            });

            var queuedItemRan = false;
            var statuses = new ConcurrentQueue<ProcessStatus>();

            var queued = await processWorker.Producer.EnqueueAsync(
                async (svc, cancellation) =>
                {
                    queuedItemRan = true;
                    await Task.CompletedTask;
                },
                async (processId, status, svc, exception) =>
                {
                    // Slow reporting of the final status, so completing before it is reported would be visible below.
                    if (status is ProcessStatus.Canceled)
                        await Task.Delay(300);

                    statuses.Enqueue(status);
                }
            );

            var cancelTask = processWorker.Producer.CancelWorkItemAsync(queued.ProcessId);

            Func<Task> awaitQueued = () => queued.Completion;

            await awaitQueued.Should().ThrowAsync<OperationCanceledException>();

            // The completion is released only after the final status has been reported.
            statuses.Should().Contain(ProcessStatus.Canceled);

            await cancelTask;

            release.SetResult();
            await blocker.Completion;

            // Give the consumer the chance to pick up the canceled item, it must skip it.
            await Task.Delay(250, TestContext.Current.CancellationToken);

            queuedItemRan.Should().BeFalse();
            statuses.Should().NotContain(ProcessStatus.Running);
            processWorker.IsOperational.Should().BeTrue();
        }

        private async Task<int> SumFirstNIntegers(int numOfIntegers, bool useParallel)
        {
            var taskList = new List<Task>();

            int totalSum = 0;

            foreach (var item in Enumerable.Range(1, numOfIntegers))
            {
                var info = await (useParallel ? _processWorkerMT : _processWorkerST).Producer.EnqueueAsync(async (svc, cancellation) =>
                {
                    await Task.Yield();

                    totalSum += item;
                });

                taskList.Add(info.Completion);
            }

            await Task.WhenAll(taskList);

            (useParallel ? _processWorkerMT : _processWorkerST).IsOperational.Should().BeTrue();

            return totalSum;
        }
    }
}