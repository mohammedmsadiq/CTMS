using System.Globalization;
using CTMS.Application.Translations;
using CTMS.Application.Webhooks;
using CTMS.Domain.Webhooks;
using Microsoft.Extensions.DependencyInjection;

namespace CTMS.Api.Webhooks;

/// <summary>
/// Drains the <see cref="WebhookChannel"/> off the request path. For each queued
/// <see cref="WebhookDelivery"/> it resolves the current delivery ETag for
/// <c>(application, language)</c>, builds and signs the body once, and POSTs it to every active
/// webhook subscribed to <c>published</c>. One bad delivery never stops the loop.
/// </summary>
public sealed class WebhookDispatchService : BackgroundService
{
    private readonly WebhookChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookDispatchService> _logger;

    public WebhookDispatchService(
        WebhookChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<WebhookDispatchService> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var delivery in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await DispatchAsync(delivery, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        ex,
                        "Webhook dispatch for {Application}/{Language} threw.",
                        delivery.Application, delivery.Language);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // host is shutting down
        }
    }

    private async Task DispatchAsync(WebhookDelivery delivery, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        var webhooks = await services.GetRequiredService<IWebhookRepository>()
            .ListActiveAsync(cancellationToken);
        var subscribed = webhooks
            .Where(w => w.Events.Contains(Webhook.PublishedEvent, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (subscribed.Count == 0)
        {
            return;
        }

        var etag = string.Empty;
        try
        {
            var view = await services.GetRequiredService<PublishedTranslationsService>()
                .GetPublishedAsync(delivery.Application, delivery.Language, cancellationToken);
            if (view is null)
            {
                _logger.LogWarning(
                    "Webhook ETag lookup found no delivery view for {Application}/{Language}; sending empty etag.",
                    delivery.Application, delivery.Language);
            }
            else
            {
                etag = view.Hash;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Webhook ETag lookup failed for {Application}/{Language}; sending empty etag.",
                delivery.Application, delivery.Language);
        }

        var payload = new WebhookPayload(
            Webhook.PublishedEvent,
            delivery.Application,
            delivery.Language,
            etag,
            delivery.PublishedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var rawBody = WebhookPayload.Serialize(payload);

        var sender = services.GetRequiredService<WebhookSender>();
        foreach (var webhook in subscribed)
        {
            await sender.SendAsync(webhook.Id, webhook.Url, webhook.Secret, rawBody, cancellationToken);
        }
    }
}
