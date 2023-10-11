﻿using Autofac;

namespace Specs
{
    public class DIModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterModule<ProcessWorker.DIModule>();
            builder.RegisterModule<ProcessWorkerV2.DIModule>();
        }
    }
}