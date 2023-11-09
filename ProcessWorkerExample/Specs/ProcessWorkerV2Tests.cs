using Autofac;
using FluentAssertions;
using ProcessWorkerV2;
using Serilog;
using Xunit;

namespace Specs
{
    public class ProcessWorkerV2Tests
    {
        private readonly ILogger _logger;
        private readonly IProcessWorkerProducer _processWorkerMT;
        private readonly IProcessWorkerProducer _processWorkerST;

        public ProcessWorkerV2Tests()
        {
            var builder = new ContainerBuilder();
            builder.RegisterModule<DIModule>();
            Container = builder.Build();

            var processWorkerProvider = Container.Resolve<IProcessWorkerProvider>();

            _logger = Container.Resolve<ILogger>();

            _processWorkerST = processWorkerProvider.GetOrCreate($"ST-{nameof(ProcessWorkerV2Tests)}", new ProcessWorkerConfiguration
            {
                Concurrency = 1
            });

            _processWorkerMT = processWorkerProvider.GetOrCreate($"MT-{nameof(ProcessWorkerV2Tests)}");
        }

        private IContainer Container { get; }

        [Fact(DisplayName = $"T01: MT-{nameof(ProcessWorker)}")]
        public async Task T01()
        {
            int numOfIntegers = 10;
            var expectedResult = Enumerable.Range(1, numOfIntegers).Sum(i => i); // 55

            var sumIntegersMtTest = await SumFirstNIntegers(numOfIntegers, useParallel: true);

            sumIntegersMtTest.Should().Be(expectedResult);
        }

        [Fact(DisplayName = $"T02: ST-{nameof(ProcessWorker)}")]
        public async Task T02()
        {
            int numOfIntegers = 10;
            var expectedResult = Enumerable.Range(1, numOfIntegers).Sum(i => i); // 55

            var sumIntegersStTest = await SumFirstNIntegers(numOfIntegers, useParallel: false);

            sumIntegersStTest.Should().Be(expectedResult);
        }

        [Fact(DisplayName = $"T03: Cancel {nameof(ProcessWorker)}")]
        public async Task T03()
        {
            var info = await _processWorkerST.EnqueueWorkItemAsync(async cancel =>
            {
                foreach (var item in Enumerable.Range(1, 20))
                {
                    cancel.ThrowIfCancellationRequested();
                    await Task.Delay(500);
                }
            });

            await Task.Delay(1_000);

            _processWorkerST.CancelWorkItemAsync(info.ProcessId, 250);

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
        }

        private async Task<int> SumFirstNIntegers(int numOfIntegers, bool useParallel)
        {
            var taskList = new List<Task>();

            int totalSum = 0;

            foreach (var item in Enumerable.Range(1, numOfIntegers))
            {
                var state = new ProcessWorkerState();

                var statusCallback = (Guid processId, ProcessStatus newStatus) =>
                {
                    _logger.Information($"ProcessId: {processId}, Status: {newStatus}");

                    state.Status = newStatus;

                    if (newStatus is ProcessStatus.Running)
                        state.StartTime = DateTime.Now;
                    else if (newStatus is ProcessStatus.Queued)
                        state.QueuedAt = DateTime.Now;
                    else if (newStatus is ProcessStatus.Done or ProcessStatus.Canceled or ProcessStatus.Failed)
                        state.EndTime = DateTime.Now;

                    return Task.FromResult(state with
                    {
                        ProcessId = processId
                    });
                };

                var info = await (useParallel ? _processWorkerMT : _processWorkerST).EnqueueWorkItemAsync(async (cancelToken) =>
                {
                    await Task.Yield();

                    totalSum += item;
                }, statusCallback);

                taskList.Add(info.Completion);
            }

            await Task.WhenAll(taskList);

            return totalSum;
        }
    }
}