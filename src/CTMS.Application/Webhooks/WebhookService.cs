using System.Security.Cryptography;
using CTMS.Application.Common;
using CTMS.Domain.Webhooks;

namespace CTMS.Application.Webhooks;

/// <summary>Use-case orchestration for webhook registrations (create / list / delete).</summary>
public sealed class WebhookService
{
    private readonly IWebhookRepository _webhooks;

    public WebhookService(IWebhookRepository webhooks) => _webhooks = webhooks;

    /// <summary>
    /// Registers a webhook. When <see cref="CreateWebhookRequest.Secret"/> is omitted a random one
    /// is generated; the secret is returned <b>once</b> in <see cref="CreatedWebhookDto.Secret"/>.
    /// </summary>
    public async Task<CreatedWebhookDto> CreateAsync(
        CreateWebhookRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            throw new ValidationException("A webhook URL is required.");
        }

        var actor = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim();
        var secret = string.IsNullOrWhiteSpace(request.Secret) ? NewSecret() : request.Secret.Trim();

        Webhook webhook;
        try
        {
            webhook = new Webhook(request.Url, secret, actor);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ex.Message);
        }

        webhook.SetEvents(request.Events);

        await _webhooks.InsertAsync(webhook, cancellationToken);

        return new CreatedWebhookDto(
            webhook.Id,
            webhook.Url,
            webhook.Active,
            webhook.Events,
            webhook.CreatedBy,
            webhook.CreatedAt,
            secret);
    }

    public async Task<IReadOnlyList<WebhookDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var webhooks = await _webhooks.ListAsync(cancellationToken);
        return webhooks.Select(ToDto).ToList();
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => _webhooks.DeleteAsync(id, cancellationToken);

    private static string NewSecret()
        => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static WebhookDto ToDto(Webhook webhook) => new(
        webhook.Id,
        webhook.Url,
        webhook.Active,
        webhook.Events,
        webhook.CreatedBy,
        webhook.CreatedAt);
}
