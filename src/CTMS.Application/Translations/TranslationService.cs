using CTMS.Application.Common;

namespace CTMS.Application.Translations;

/// <summary>
/// Default <see cref="ITranslationService"/>. Delegates to <see cref="PublishedTranslationsService"/>
/// so the resolve / common-merge / fallback / hash / Redis read-through logic lives in exactly one
/// place and is shared byte-for-byte with the REST API.
/// </summary>
public sealed class TranslationService : ITranslationService
{
    private readonly PublishedTranslationsService _published;

    public TranslationService(PublishedTranslationsService published) => _published = published;

    public async Task<TranslationBundle> GetTranslationsAsync(
        string project,
        string language,
        CancellationToken cancellationToken = default)
    {
        var view = await _published.GetPublishedAsync(project, language, cancellationToken)
            ?? throw new NotFoundException(
                $"No published translations for project '{project}' in language '{language}'.");

        return new TranslationBundle(view.Project, view.Language, view.Translations, view.Hash);
    }
}
