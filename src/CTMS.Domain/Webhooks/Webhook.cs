using CTMS.Domain.Common;

namespace CTMS.Domain.Webhooks;

/// <summary>
/// A registered HTTP endpoint CTMS calls when translations are published, so a consumer can
/// refresh instead of polling. The delivery body is signed with <see cref="Secret"/> using
/// HMAC-SHA256 (header <c>X-CTMS-Signature: sha256=&lt;hex&gt;</c>).
/// </summary>
public sealed class Webhook : Entity
{
    /// <summary>The only event that currently fires. <see cref="Events"/> is modelled as a list for forward room.</summary>
    public const string PublishedEvent = "published";

    private Webhook()
    {
        // Materialization constructor for the persistence layer.
    }

    public Webhook(string url, string secret, string createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("A webhook URL must be an absolute http or https URL.", nameof(url));
        }

        Url = parsed.ToString();
        Secret = secret.Trim();
        CreatedBy = createdBy.Trim();
    }

    /// <summary>Absolute http/https URL CTMS POSTs the delivery to.</summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>Shared secret for the HMAC-SHA256 body signature. Returned once at creation, then hidden.</summary>
    public string Secret { get; private set; } = string.Empty;

    /// <summary>Event names this webhook subscribes to. Only <see cref="PublishedEvent"/> is emitted today.</summary>
    public IReadOnlyList<string> Events { get; private set; } = [PublishedEvent];

    public bool Active { get; private set; } = true;

    public string CreatedBy { get; private set; } = string.Empty;

    public void Deactivate() => Active = false;

    /// <summary>
    /// Replaces the subscribed-event set, trimming and de-duplicating (ordinal-ignore-case). A
    /// null or empty set falls back to <see cref="PublishedEvent"/>.
    /// </summary>
    public void SetEvents(IEnumerable<string>? events)
    {
        if (events is null)
        {
            Events = [PublishedEvent];
            return;
        }

        var result = new List<string>();
        foreach (var raw in events)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var value = raw.Trim();
            if (!result.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(value);
            }
        }

        Events = result.Count == 0 ? [PublishedEvent] : result;
    }
}
