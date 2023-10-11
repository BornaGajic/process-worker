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

            _processWorkerST = processWorkerProvider.GetOrCreate("ProcessWorkerTest_Sync", new ProcessWorkerConfiguration
            {
                Concurrency = 1
            });

            _processWorkerMT = processWorkerProvider.GetOrCreate("ProcessWorkerTest_Async", new ProcessWorkerConfiguration
            {
                Concurrency = 3
            });
        }

        private IContainer Container { get; }

        [Fact(DisplayName = "T01: Sum integers")]
        public async Task T01_SumFirstNIntegers()
        {
            int numOfIntegers = 10;
            var expectedResult = Enumerable.Range(1, numOfIntegers).Sum(i => i); // 55

            _logger.Information($"Start {nameof(SumFirstNIntegers)} with {nameof(numOfIntegers)} set to {numOfIntegers}.");

            var sumIntegersMtTest = await SumFirstNIntegers(numOfIntegers, useParallel: true);
            var sumIntegersStTest = await SumFirstNIntegers(numOfIntegers, useParallel: false);

            _logger.Information($"End {nameof(SumFirstNIntegers)}");

            sumIntegersMtTest.Should().Be(expectedResult);
            sumIntegersStTest.Should().Be(expectedResult);
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

                    if (newStatus is ProcessStatus.Queued)
                        state.StartTime = DateTime.Now;
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