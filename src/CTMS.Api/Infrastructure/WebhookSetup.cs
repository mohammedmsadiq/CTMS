using CTMS.Api.Webhooks;
using CTMS.Application.Webhooks;

namespace CTMS.Api.Infrastructure;

/// <summary>
/// Wires publish webhooks. When <c>Webhooks:Enabled</c> is <c>true</c> (default) an
/// <see cref="ChannelWebhookPublisher"/> feeds a bounded channel drained by
/// <see cref="WebhookDispatchService"/> through a named <see cref="HttpClient"/>. When
/// <c>false</c>, <see cref="NoOpWebhookPublisher"/> is registered and nothing is enqueued.
/// </summary>
public static class WebhookSetup
{
    public static IServiceCollection AddCtmsWebhooks(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new WebhookOptions();
        configuration.GetSection(WebhookOptions.SectionName).Bind(options);
        services.Configure<WebhookOptions>(configuration.GetSection(WebhookOptions.SectionName));

        if (!options.Enabled)
        {
            services.AddSingleton<IWebhookPublisher, NoOpWebhookPublisher>();
            return services;
        }

        services.AddSingleton(new WebhookChannel(options.ChannelCapacity));
        services.AddSingleton<IWebhookPublisher, ChannelWebhookPublisher>();

        services.AddHttpClient<WebhookSender>(client =>
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));

        services.AddHostedService<WebhookDispatchService>();

        return services;
    }
}
