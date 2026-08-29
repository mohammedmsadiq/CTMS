using CTMS.Infrastructure.Persistence.Mongo;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;

namespace CTMS.Infrastructure.Persistence.Health;

/// <summary>Readiness probe: runs <c>{ ping: 1 }</c> against the configured database.</summary>
public sealed class MongoHealthCheck : IHealthCheck
{
    private readonly IMongoContext _context;

    public MongoHealthCheck(IMongoContext context) => _context = context;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB ping failed.", ex);
        }
    }
}
