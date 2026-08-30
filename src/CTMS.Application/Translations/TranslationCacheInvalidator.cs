using CTMS.Application.Projects;
using CTMS.Domain.Projects;

namespace CTMS.Application.Translations;

/// <summary>
/// Invalidates the assembled-translations cache for an application and a set of languages.
/// A <see cref="Project.IsCommon"/> project contributes to every project's bundle, so
/// invalidating it fans out across every application's cache for those languages.
/// </summary>
public sealed class TranslationCacheInvalidator
{
    private readonly IProjectRepository _projects;
    private readonly IPublishedTranslationsCache _cache;

    public TranslationCacheInvalidator(IProjectRepository projects, IPublishedTranslationsCache cache)
    {
        _projects = projects;
        _cache = cache;
    }

    public async Task InvalidateAsync(
        Project application,
        IReadOnlyCollection<string> languageCodes,
        CancellationToken cancellationToken = default)
    {
        var languages = languageCodes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (languages.Count == 0)
        {
            return;
        }

        var applicationCodes = application.IsCommon
            ? (await _projects.ListAsync(includeInactive: true, cancellationToken)).Select(p => p.Slug).ToList()
            : [application.Slug];

        foreach (var code in applicationCodes)
        {
            foreach (var language in languages)
            {
                await _cache.InvalidateAsync(code, language, cancellationToken);
            }
        }
    }
}
