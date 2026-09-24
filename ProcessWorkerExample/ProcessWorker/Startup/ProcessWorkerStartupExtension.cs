using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProcessWorker.Common;
using ProcessWorker.Service;

namespace ProcessWorker.Startup;

public static class ProcessWorkerStartupExtension
{
    /// <summary>
    /// 1. Registers <see cref="IProcessWorkerProvider"/> with <see cref="ProcessWorkerProvider"/>.
    /// </summary>
    public static IServiceCollection RegisterProcessWorker(this IServiceCollection services)
    {
        services.RegisterProcessWorkerServices();
        return services;
    }

    private static IServiceCollection RegisterProcessWorkerServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IProcessWorkerProvider, ProcessWorkerProvider>();

        return services;
    }
}