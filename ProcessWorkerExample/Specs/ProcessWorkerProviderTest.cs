using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using ProcessWorker.Common;
using ProcessWorker.Model;
using ProcessWorker.Startup;
using Xunit;

namespace Specs
{
    public class ProcessWorkerProviderTest : TestSetup
    {
        private readonly IProcessWorkerProvider _provider;

        public ProcessWorkerProviderTest()
        {
            var container = SetupContainer(builder =>
            {
                builder.RegisterProcessWorker();
            });
            _provider = container.GetRequiredService<IProcessWorkerProvider>();
        }

        // The provider keeps its workers in a static store, so every test uses its own key.
        private static string NewKey() => $"{nameof(ProcessWorkerProviderTest)}-{Guid.NewGuid()}";

        [Fact(DisplayName = "P01: Same key returns the same worker")]
        public void P01()
        {
            var key = NewKey();

            try
            {
                var first = _provider.GetOrCreate(key);
                var second = _provider.GetOrCreate(key, new ProcessWorkerConfiguration { Concurrency = 7 });

                second.Should().BeSameAs(first);
            }
            finally
            {
                _provider.Remove(key);
            }
        }

        [Fact(DisplayName = "P02: Different keys return different workers")]
        public void P02()
        {
            var keyA = NewKey();
            var keyB = NewKey();

            try
            {
                _provider.GetOrCreate(keyA).Should().NotBeSameAs(_provider.GetOrCreate(keyB));
            }
            finally
            {
                _provider.Remove(keyA);
                _provider.Remove(keyB);
            }
        }

        [Fact(DisplayName = "P03: Concurrent callers all get the same worker")]
        public async Task P03()
        {
            var key = NewKey();

            try
            {
                var workers = await Task.WhenAll(Enumerable.Range(1, 32).Select(_ => Task.Run(() => _provider.GetOrCreate(key))));

                workers.Distinct().Should().ContainSingle();
            }
            finally
            {
                _provider.Remove(key);
            }
        }

        [Fact(DisplayName = "P04: Remove disposes the worker and the key can be created again")]
        public void P04()
        {
            var key = NewKey();

            var worker = _provider.GetOrCreate(key);

            worker.IsOperational.Should().BeTrue();

            _provider.Remove(key).Should().BeTrue();

            worker.IsOperational.Should().BeFalse();

            var recreated = _provider.GetOrCreate(key);

            try
            {
                recreated.Should().NotBeSameAs(worker);
                recreated.IsOperational.Should().BeTrue();
            }
            finally
            {
                _provider.Remove(key);
            }
        }

        [Fact(DisplayName = "P05: Removing an unknown key returns false")]
        public void P05()
        {
            _provider.Remove(NewKey()).Should().BeFalse();
        }

        [Fact(DisplayName = "P06: Removing the same key twice only succeeds once")]
        public void P06()
        {
            var key = NewKey();

            _provider.GetOrCreate(key);

            _provider.Remove(key).Should().BeTrue();
            _provider.Remove(key).Should().BeFalse();
        }
    }
}
