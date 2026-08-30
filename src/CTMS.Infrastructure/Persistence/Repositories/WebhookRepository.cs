using CTMS.Application.Webhooks;
using CTMS.Domain.Webhooks;
using CTMS.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class WebhookRepository : IWebhookRepository
{
    private readonly IMongoCollection<Webhook> _webhooks;

    public WebhookRepository(IMongoContext context) => _webhooks = context.Webhooks;

    public async Task<IReadOnlyList<Webhook>> ListActiveAsync(CancellationToken cancellationToken = default)
        => await _webhooks.Find(w => w.Active)
            .SortByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Webhook>> ListAsync(CancellationToken cancellationToken = default)
        => await _webhooks.Find(FilterDefinition<Webhook>.Empty)
            .SortByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<Webhook?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _webhooks.Find(w => w.Id == id).FirstOrDefaultAsync(cancellationToken);

    public async Task InsertAsync(Webhook webhook, CancellationToken cancellationToken = default)
        => await _webhooks.InsertOneAsync(webhook.StampCreated(), cancellationToken: cancellationToken);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _webhooks.DeleteOneAsync(w => w.Id == id, cancellationToken);
        return result.DeletedCount > 0;
    }
}
