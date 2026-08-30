using System.Text.Json;
using System.Text.Json.Serialization;

namespace CTMS.Api.Webhooks;

/// <summary>
/// The JSON body POSTed to a webhook on <c>published</c>. Property order is fixed so the exact
/// bytes signed by <see cref="Application.Webhooks.WebhookSignature"/> are reproducible.
/// </summary>
public sealed record WebhookPayload(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("application")] string Application,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("etag")] string Etag,
    [property: JsonPropertyName("publishedAt")] string PublishedAt)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>The canonical raw body — serialise once, sign that string, send that string.</summary>
    public static string Serialize(WebhookPayload payload)
        => JsonSerializer.Serialize(payload, SerializerOptions);
}
