using CTMS.Api.Infrastructure;
using CTMS.Api.Webhooks;
using CTMS.Application.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CTMS.Api.IntegrationTests;

/// <summary><c>AddCtmsWebhooks</c> honours the <c>Webhooks:Enabled</c> switch.</summary>
public sealed class WebhookSetupTests
{
    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCtmsWebhooks(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Enabled_by_default_wires_the_channel_publisher_and_the_dispatch_service()
    {
        using var provider = Build();

        Assert.IsType<ChannelWebhookPublisher>(provider.GetRequiredService<IWebhookPublisher>());
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is WebhookDispatchService);
    }

    [Fact]
    public void Disabled_wires_the_no_op_publisher_and_no_dispatch_service()
    {
        using var provider = Build(("Webhooks:Enabled", "false"));

        Assert.IsType<NoOpWebhookPublisher>(provider.GetRequiredService<IWebhookPublisher>());
        Assert.DoesNotContain(provider.GetServices<IHostedService>(), s => s is WebhookDispatchService);
    }
}
