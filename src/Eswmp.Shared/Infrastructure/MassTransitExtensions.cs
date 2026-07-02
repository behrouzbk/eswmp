using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Eswmp.Shared.Infrastructure;

public static class MassTransitExtensions
{
    /// <summary>
    /// Registers MassTransit with RabbitMQ (local) or Azure Service Bus (production).
    /// Transport is selected by MessageBus:Transport config key ("RabbitMQ" | "AzureServiceBus").
    /// </summary>
    public static IServiceCollection AddEswmpMessageBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        var transport = configuration["MessageBus:Transport"] ?? "RabbitMQ";

        services.AddMassTransit(cfg =>
        {
            configureConsumers?.Invoke(cfg);

            if (transport.Equals("AzureServiceBus", StringComparison.OrdinalIgnoreCase))
            {
                cfg.UsingAzureServiceBus((ctx, bus) =>
                {
                    bus.Host(configuration["MessageBus:ServiceBusConnectionString"]);
                    bus.ConfigureEndpoints(ctx);
                });
            }
            else
            {
                cfg.UsingRabbitMq((ctx, bus) =>
                {
                    bus.Host(
                        configuration["MessageBus:Host"] ?? "localhost",
                        configuration.GetValue<ushort>("MessageBus:Port", 5672),
                        "/",
                        h =>
                        {
                            h.Username(configuration["MessageBus:Username"] ?? "guest");
                            h.Password(configuration["MessageBus:Password"] ?? "guest");
                        });

                    bus.ConfigureEndpoints(ctx);
                });
            }
        });

        return services;
    }
}
