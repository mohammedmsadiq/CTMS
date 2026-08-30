using CTMS.Application.Audit;
using CTMS.Application.Languages;
using CTMS.Application.Projects;
using CTMS.Application.Translations;
using CTMS.Application.Translations.Import;
using Microsoft.Extensions.DependencyInjection;

namespace CTMS.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services (use-case orchestrators).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ProjectService>();
        services.AddScoped<LanguageService>();
        services.AddScoped<TranslationKeyService>();
        services.AddScoped<TranslationStringService>();
        services.AddScoped<TranslationCacheInvalidator>();
        services.AddScoped<PublishedTranslationsService>();
        services.AddScoped<ITranslationService, TranslationService>();
        services.AddScoped<TranslationImportService>();
        services.AddScoped<AuditService>();
        return services;
    }
}
