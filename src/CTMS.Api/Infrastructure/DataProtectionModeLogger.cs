namespace CTMS.Api.Infrastructure;

/// <summary>
/// Logs once, at startup, whether the Data Protection key ring is persisted to Redis (shared
/// across replicas) or left on the framework default local, ephemeral store. Mirrors
/// <c>CacheModeLogger</c> in the infrastructure project.
/// </summary>
internal sealed class DataProtectionModeLogger : IHostedService
{
    private readonly ILogger<DataProtectionModeLogger> _logger;
    private readonly bool _usingRedis;

    public DataProtectionModeLogger(ILogger<DataProtectionModeLogger> logger, bool usingRedis)
    {
        _logger = logger;
        _usingRedis = usingRedis;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_usingRedis)
        {
            _logger.LogInformation(
                "Data Protection key ring: Redis (ConnectionStrings:Redis is set); keys are shared across replicas.");
        }
        else
        {
            _logger.LogInformation(
                "Data Protection key ring: local ephemeral store (ConnectionStrings:Redis is not set). " +
                "Fine for a single instance or local runs; configure Redis for a multi-replica deployment.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
