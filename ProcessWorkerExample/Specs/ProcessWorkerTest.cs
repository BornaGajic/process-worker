using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
            _processWorkerMT = _provider.GetOrCreateCached($"MT-{nameof(ProcessWorkerTest)}", new ProcessWorkerConfiguration
            {
                Concurrency = 3
            });
            _processWorkerST = _provider.GetOrCreateCached($"ST-{nameof(ProcessWorkerTest)}", new ProcessWorkerConfiguration
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

            await Task.Delay(1_000);

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