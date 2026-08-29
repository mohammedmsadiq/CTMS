using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Locales;
using CTMS.Application.Projects;
using CTMS.Application.Translations;
using CTMS.Infrastructure.Persistence.Health;
using CTMS.Infrastructure.Persistence.Mongo;
using CTMS.Infrastructure.Persistence.Repositories;
using CTMS.Infrastructure.Persistence.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace CTMS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Name of the MongoDB connection string in configuration.</summary>
    public const string ConnectionStringName = "CtmsDatabase";

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
        services.AddScoped<ILocaleRepository, LocaleRepository>();
        services.AddScoped<ITranslationKeyRepository, TranslationKeyRepository>();
        services.AddScoped<ITranslationStringRepository, TranslationStringRepository>();
        services.AddScoped<ITranslationBundleRepository, TranslationBundleRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();

        services.AddHealthChecks()
            .AddCheck<MongoHealthCheck>("database", tags: ["ready"]);

        services.AddHostedService<MongoIndexInitializer>();
        services.AddHostedService<DataSeeder>();

        return services;
    }
}
