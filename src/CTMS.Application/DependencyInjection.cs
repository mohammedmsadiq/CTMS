using CTMS.Application.Audit;
using CTMS.Application.Locales;
using CTMS.Application.Projects;
using CTMS.Application.Translations;
using Microsoft.Extensions.DependencyInjection;

namespace CTMS.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services (use-case orchestrators).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ProjectService>();
        services.AddScoped<LocaleService>();
        services.AddScoped<TranslationKeyService>();
        services.AddScoped<TranslationStringService>();
        services.AddScoped<AuditService>();
        return services;
    }
}
