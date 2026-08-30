using CTMS.Application.ApiKeys;
using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Languages;
using CTMS.Application.Projects;
using CTMS.Application.Translations;
using CTMS.Application.Webhooks;
using CTMS.Infrastructure.Persistence.Caching;
using CTMS.Infrastructure.Persistence.Health;
using CTMS.Infrastructure.Persistence.Mongo;
using CTMS.Infrastructure.Persistence.Repositories;
using CTMS.Infrastructure.Persistence.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace CTMS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Name of the MongoDB connection string in configuration.</summary>
    public const string ConnectionStringName = "CtmsDatabase";

    /// <summary>
    /// Name of the Redis connection string in configuration (StackExchange.Redis format
    /// <c>host:port[,options]</c>). When absent, the translations cache falls back to an
    /// in-process distributed-memory cache so a local <c>dotnet run</c> needs no Redis.
    /// </summary>
    public const string RedisConnectionStringName = "Redis";

    /// <summary>
    /// Wires the MongoDB client and context, the (no-op) unit of work, repository
    /// implementations, the readiness health check, and the startup index initializer and
    /// dev-only data seeder.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set it in appsettings.json " +
                $"or via the ConnectionStrings__{ConnectionStringName} environment variable.");

        var databaseName = configuration.GetValue<string?>($"{MongoOptions.SectionName}:Database")
            is { Length: > 0 } configured
            ? configured
            : new MongoOptions().Database;

        MongoMappingRegistration.Register();

        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
        services.AddSingleton<IMongoContext>(provider =>
            new CtmsMongoContext(provider.GetRequiredService<IMongoClient>(), databaseName));

        services.AddSingleton<IUnitOfWork, NoOpUnitOfWork>();

        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ILanguageRepository, LanguageRepository>();
        services.AddScoped<ITranslationKeyRepository, TranslationKeyRepository>();
        services.AddScoped<ITranslationStringRepository, TranslationStringRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IWebhookRepository, WebhookRepository>();

        services.AddHealthChecks()
            .AddCheck<MongoHealthCheck>("database", tags: ["ready"]);

        AddTranslationsCache(services, configuration);

        services.AddHostedService<MongoIndexInitializer>();
        services.AddHostedService<DataSeeder>();

        return services;
    }

    /// <summary>
    /// Registers the distributed cache that fronts <c>GET /api/translations/{application}/{language}</c>:
    /// StackExchange.Redis when <c>ConnectionStrings:Redis</c> is set, otherwise an in-process
    /// distributed-memory cache. The active backend is logged once at startup by
    /// <see cref="CacheModeLogger"/>.
    /// </summary>
    private static void AddTranslationsCache(IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString(RedisConnectionStringName);
        var usingRedis = !string.IsNullOrWhiteSpace(redisConnectionString);

        if (usingRedis)
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.Configure<TranslationsCacheOptions>(options => options.TranslationsTtlMinutes =
            configuration.GetValue(
                $"{TranslationsCacheOptions.SectionName}:TranslationsTtlMinutes",
                TranslationsCacheOptions.DefaultTtlMinutes));
        services.AddSingleton<IPublishedTranslationsCache, PublishedTranslationsCache>();
        services.AddHostedService(provider =>
            new CacheModeLogger(provider.GetRequiredService<ILogger<CacheModeLogger>>(), usingRedis));
    }
}
