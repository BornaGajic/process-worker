using Microsoft.Extensions.DependencyInjection;

namespace Specs
{
    public abstract class TestSetup
    {
        public static IServiceProvider SetupContainer(Action<IServiceCollection> callback)
        {
            var services = new ServiceCollection();
            callback(services);
            return services.BuildServiceProvider();
        }
    }
}