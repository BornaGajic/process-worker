using Autofac;
using Autofac.Core;
using ProcessWorker.Model;
using Serilog;
using System.Collections.Concurrent;

namespace ProcessWorker.Service;

public class ProcessWorkerProvider : IProcessWorkerProvider
{
    private static readonly ConcurrentDictionary<string, IProcessWorker> _processWorkerMux = new();
    private readonly ILifetimeScope _lifetimeScope;

    public ProcessWorkerProvider(ILifetimeScope lifetimeScope)
    {
        _lifetimeScope = lifetimeScope;
    }

    public void Dispose()
    {
        foreach (var (_, value) in _processWorkerMux)
        {
            value.Dispose();
        }
    }

    public IProcessWorker GetOrCreate(string key) => _processWorkerMux.GetOrAdd(key, _lifetimeScope.Resolve<IProcessWorker>());

    public IProcessWorker GetOrCreate(string key, ProcessWorkerConfiguration configuration)
    {
        var parameters = new Parameter[]
        {
            new TypedParameter(typeof(ILogger), _lifetimeScope.Resolve<ILogger>()),
            new TypedParameter(typeof(ProcessWorkerConfiguration), configuration)
        };

        return _processWorkerMux.GetOrAdd(key, _lifetimeScope.Resolve<IProcessWorker>(parameters));
    }
}
