using Autofac;
using ProcessWorker.Service;
using Serilog;

namespace ProcessWorker;

public class DIModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.Register(ctx => 
        {
            var projectDirectory = Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.FullName;
            var logsPath = Path.Combine(projectDirectory, @$"Logs/log - {DateTime.Now:yyyyMMdd}.txt");

            var config = new LoggerConfiguration()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(logsPath, outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}");

            config = config.MinimumLevel.Information();

            var logger = Log.Logger = config.CreateLogger();

            return logger;
        })
        .As<ILogger>()
        .SingleInstance();

        builder.RegisterType<Service.ProcessWorker>().As<IProcessWorker>();
        builder.RegisterType<ProcessWorkerProvider>().As<IProcessWorkerProvider>().SingleInstance();
    }
}