using System;
using MassTransit;
using MassTransit.AspNetCoreIntegration;
using MassTransit.ExtensionsDependencyInjectionIntegration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.MassTransit
{
    public abstract class MassTransitBuilderBase<TOptions> : IMassTransitBuilder<TOptions> where TOptions : class
    {
        protected MassTransitBuilderBase() { }

        public IServiceCollection Build(IServiceCollection services)
        {
            var optionsBuilder = services.AddOptions<TOptions>();
            Options?.Invoke(optionsBuilder);


            //return services.AddMassTransit(CreateBus, ConfigureMassTransit);

            return services.AddMassTransit(ConfigureMassTransit)
                .AddMassTransitHostedService();
        }

        protected abstract void ConfigureMassTransit(IServiceCollectionBusConfigurator configurator);
        //protected abstract IBusControl CreateBus(IServiceProvider serviceProvider);

        public Action<OptionsBuilder<TOptions>> Options { get; set; }
    }
}