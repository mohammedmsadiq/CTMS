using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CTMS.Infrastructure.Persistence.Caching;

/// <summary>
/// Logs once, at startup, whether the bundle cache is backed by Redis or by the in-process
/// distributed-memory fallback. Registered by <c>AddInfrastructure</c>.
/// </summary>
internal sealed class CacheModeLogger : IHostedService
{
    private readonly ILogger<CacheModeLogger> _logger;
    private readonly bool _usingRedis;

    public CacheModeLogger(ILogger<CacheModeLogger> logger, bool usingRedis)
    {
        _logger = logger;
        _usingRedis = usingRedis;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_usingRedis)
        {
            _logger.LogInformation(
                "Bundle cache backend: Redis (ConnectionStrings:Redis is set).");
        }
        else
        {
            _logger.LogInformation(
                "Bundle cache backend: in-process distributed memory " +
                "(ConnectionStrings:Redis is not set).");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
