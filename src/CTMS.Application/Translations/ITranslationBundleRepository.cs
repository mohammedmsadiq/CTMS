using CTMS.Domain.Translations;

namespace CTMS.Application.Translations;

/// <summary>Persistence for the append-only <see cref="TranslationBundle"/> snapshots.</summary>
public interface ITranslationBundleRepository
{
    /// <summary>The highest-version bundle for a project's locale, or <c>null</c> if none has been published.</summary>
    Task<TranslationBundle?> GetLatestAsync(
        Guid projectId,
        string localeCode,
        CancellationToken cancellationToken = default);

    /// <summary>A specific bundle version, or <c>null</c> if it does not exist.</summary>
    Task<TranslationBundle?> GetByVersionAsync(
        Guid projectId,
        string localeCode,
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new bundle. Throws <see cref="Common.ConflictException"/> if
    /// <c>(ProjectId, LocaleCode, Version)</c> is already taken.
    /// </summary>
    Task InsertAsync(TranslationBundle bundle, CancellationToken cancellationToken = default);
}
