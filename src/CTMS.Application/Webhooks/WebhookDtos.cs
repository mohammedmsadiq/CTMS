namespace CTMS.Application.Webhooks;

/// <summary>Body for <c>POST /api/webhooks</c>. A <see cref="Secret"/> is generated when omitted.</summary>
public sealed record CreateWebhookRequest(
    string Url,
    string? Secret = null,
    IReadOnlyList<string>? Events = null);

/// <summary>Read model for a listed webhook. The <see cref="Secret"/> is never included.</summary>
public sealed record WebhookDto(
    Guid Id,
    string Url,
    bool Active,
    IReadOnlyList<string> Events,
    string CreatedBy,
    DateTime CreatedAt);

/// <summary>
/// Response for <c>POST /api/webhooks</c> — the same fields as <see cref="WebhookDto"/> plus the
/// one and only disclosure of the signing <see cref="Secret"/>.
/// </summary>
public sealed record CreatedWebhookDto(
    Guid Id,
    string Url,
    bool Active,
    IReadOnlyList<string> Events,
    string CreatedBy,
    DateTime CreatedAt,
    string Secret);
