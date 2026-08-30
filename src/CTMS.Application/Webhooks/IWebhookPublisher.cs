namespace CTMS.Application.Webhooks;

/// <summary>
/// One <c>published</c> signal for an <c>(application, language)</c> pair, produced the moment a
/// publish completes. The background dispatcher fans this out to every active webhook, resolves
/// the current delivery ETag and POSTs the signed body.
/// </summary>
public sealed record WebhookDelivery(string Application, string Language, DateTimeOffset PublishedAt);

/// <summary>
/// Non-blocking hand-off from a publish use case to webhook delivery. Implementations enqueue and
/// return immediately; a webhook failure must never affect the publish result. Registered as a
/// no-op when <c>Webhooks:Enabled</c> is <c>false</c>.
/// </summary>
public interface IWebhookPublisher
{
    /// <summary>Enqueues one <c>published</c> signal per language. Returns at once; never throws.</summary>
    void Enqueue(string application, IEnumerable<string> languages);
}

/// <summary>Drops every signal. Used when <c>Webhooks:Enabled=false</c>.</summary>
public sealed class NoOpWebhookPublisher : IWebhookPublisher
{
    public void Enqueue(string application, IEnumerable<string> languages)
    {
        // intentionally nothing
    }
}
