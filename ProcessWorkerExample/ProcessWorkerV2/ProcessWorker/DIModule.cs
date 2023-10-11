using Autofac;

namespace ProcessWorkerV2
{
    public class DIModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<ProcessWorkerProvider>().As<IProcessWorkerProvider>().SingleInstance();
        }
    }
}