using CTMS.Application.Common;
using CTMS.Application.Locales;
using CTMS.Application.Projects;
using CTMS.Application.Translations;
using CTMS.Infrastructure.Persistence;
using CTMS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CTMS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Name of the PostgreSQL connection string in configuration.</summary>
    public const string ConnectionStringName = "CtmsDatabase";

    /// <summary>Registers the EF Core context, unit of work and repository implementations.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set it in appsettings.json " +
                $"or via the ConnectionStrings__{ConnectionStringName} environment variable.");

        services.AddDbContext<CtmsDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CtmsDbContext>());
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ILocaleRepository, LocaleRepository>();
        services.AddScoped<ITranslationKeyRepository, TranslationKeyRepository>();
        services.AddScoped<ITranslationStringRepository, TranslationStringRepository>();

        return services;
    }
}
