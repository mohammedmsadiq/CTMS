namespace CTMS.Application.Translations;

/// <summary>
/// The one translation engine, exposed for direct in-process use by internal .NET microservices
/// (no HTTP). The REST API is a thin adapter over this same abstraction, so both consumption
/// paths produce an identical <see cref="TranslationBundle"/>.
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// Resolves the published translation bundle for <paramref name="project"/> in
    /// <paramref name="language"/>: the project's own published strings merged with every
    /// <c>common</c> project's published strings (project value wins on a key collision), with
    /// missing values filled from the language fallback chain. Only <see cref="ReviewState"/>
    /// <c>Published</c> strings are included; <c>Archived</c> is never served.
    /// </summary>
    /// <exception cref="Common.NotFoundException">
    /// The project or language is unknown/inactive, or the language is not enabled for the project.
    /// </exception>
    Task<TranslationBundle> GetTranslationsAsync(
        string project,
        string language,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable resolved translation bundle — the same conceptual result the REST API returns.
/// <see cref="ETag"/> is the content hash of <see cref="Translations"/>.
/// </summary>
public sealed record TranslationBundle(
    string Project,
    string Language,
    IReadOnlyDictionary<string, string> Translations,
    string ETag);
