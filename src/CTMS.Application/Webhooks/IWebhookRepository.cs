using CTMS.Domain.Webhooks;

namespace CTMS.Application.Webhooks;

/// <summary>Persistence for the <see cref="Webhook"/> aggregate (collection <c>webhooks</c>).</summary>
public interface IWebhookRepository
{
    /// <summary>Every active webhook. Backs delivery fan-out.</summary>
    Task<IReadOnlyList<Webhook>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Every webhook, newest first. Backs the management list.</summary>
    Task<IReadOnlyList<Webhook>> ListAsync(CancellationToken cancellationToken = default);

    Task<Webhook?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task InsertAsync(Webhook webhook, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes the webhook. Returns <c>true</c> when a row was removed.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
