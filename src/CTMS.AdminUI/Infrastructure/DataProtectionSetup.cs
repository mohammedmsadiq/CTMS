using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

namespace CTMS.AdminUI.Infrastructure;

/// <summary>
/// Persists the ASP.NET Core Data Protection key ring to Redis so every Admin UI replica shares
/// one set of keys (antiforgery tokens, auth-cookie protection, anything else that calls
/// <c>IDataProtector</c>). Without this each container keeps its keys on a local, often
/// non-persistent volume and a multi-replica deployment fails to round-trip protected payloads —
/// e.g. an antiforgery token minted by one replica is rejected by another.
/// </summary>
/// <remarks>
/// A byte-for-byte mirror of <c>CTMS.Api/Infrastructure/DataProtectionSetup.cs</c>: same
/// <c>ConnectionStrings:Redis</c> key, same <c>SetApplicationName("CTMS")</c>, same
/// <c>DataProtection-Keys</c> Redis key. When Redis is not configured it falls back to the
/// framework default (keys in a local directory, ephemeral) and logs an info line — the same
/// degrade-quietly pattern the API uses.
/// </remarks>
internal static class DataProtectionSetup
{
    private const string ApplicationName = "CTMS";
    private const string RedisKeyName = "DataProtection-Keys";

    public static WebApplicationBuilder AddCtmsDataProtection(this WebApplicationBuilder builder)
    {
        var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
        var usingRedis = !string.IsNullOrWhiteSpace(redisConnectionString);

        var dataProtection = builder.Services
            .AddDataProtection()
            .SetApplicationName(ApplicationName);

        if (usingRedis)
        {
            // One lazily-established multiplexer for the key ring; long-lived and thread-safe.
            // Lazy + the Func<IDatabase> overload keeps a briefly-unavailable Redis from
            // blocking startup — the key ring connects on first protect/unprotect instead.
            var multiplexer = new Lazy<IConnectionMultiplexer>(
                () => ConnectionMultiplexer.Connect(redisConnectionString!));
            dataProtection.PersistKeysToStackExchangeRedis(() => multiplexer.Value.GetDatabase(), RedisKeyName);

            // TODO: at-rest key encryption (ProtectKeysWithCertificate / Azure Key Vault via
            // ProtectKeysWithAzureKeyVault) is a platform/Key Vault concern and is wired here
            // once a certificate or Key Vault key URL is provisioned for the environment.
        }

        builder.Services.AddHostedService(provider =>
            new DataProtectionModeLogger(
                provider.GetRequiredService<ILogger<DataProtectionModeLogger>>(),
                usingRedis));

        return builder;
    }
}
