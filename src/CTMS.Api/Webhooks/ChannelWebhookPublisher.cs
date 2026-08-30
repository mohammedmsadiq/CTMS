using CTMS.Application.Webhooks;

namespace CTMS.Api.Webhooks;

/// <summary>
/// <see cref="IWebhookPublisher"/> that drops one <see cref="WebhookDelivery"/> per affected
/// language onto the <see cref="WebhookChannel"/> and returns immediately. Registered only when
/// <c>Webhooks:Enabled</c> is <c>true</c>.
/// </summary>
public sealed class ChannelWebhookPublisher : IWebhookPublisher
{
    private readonly WebhookChannel _channel;
    private readonly ILogger<ChannelWebhookPublisher> _logger;

    public ChannelWebhookPublisher(WebhookChannel channel, ILogger<ChannelWebhookPublisher> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public void Enqueue(string application, IEnumerable<string> languages)
    {
        if (string.IsNullOrWhiteSpace(application) || languages is null)
        {
            return;
        }

        var publishedAt = DateTimeOffset.UtcNow;
        foreach (var language in languages
                     .Where(l => !string.IsNullOrWhiteSpace(l))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_channel.TryWrite(new WebhookDelivery(application, language, publishedAt)))
            {
                _logger.LogWarning(
                    "Webhook delivery for {Application}/{Language} was not queued (channel closed).",
                    application, language);
            }
        }
    }
}
