using CTMS.Application.Common;
using CTMS.Application.Projects;
using CTMS.Domain.Translations;

namespace CTMS.Application.Translations.Export;

/// <summary>
/// Optional filters for <see cref="TranslationExportService.ExportAsync"/>.
/// <list type="bullet">
///   <item><see cref="Format"/> — <c>csv</c> or <c>xlsx</c> (required);</item>
///   <item><see cref="Language"/> — restrict the output to a single language column; when
///   <c>null</c> every one of the project's enabled languages is a column;</item>
///   <item><see cref="Category"/> — only keys in this category;</item>
///   <item><see cref="IncludeInactiveKeys"/> — include keys that are not <see cref="TranslationKey.Active"/>;</item>
///   <item><see cref="Status"/> — only keys that have at least one value in this review state
///   (same semantics as the management grid's <c>status</c> filter).</item>
/// </list>
/// </summary>
public sealed record TranslationExportQuery(
    string Format,
    string? Language = null,
    string? Category = null,
    bool IncludeInactiveKeys = false,
    string? Status = null);

/// <summary>
/// Assembles a translator work file for one project: one row per key the project <b>owns</b> (the
/// merged <c>common</c> keys are exported from the <c>common</c> project itself), one column per
/// language, each cell the key's current value in that language regardless of review state.
/// Returns <c>null</c> for an unknown or inactive project (→ <c>404</c>).
/// </summary>
public sealed class TranslationExportService
{
    private readonly IProjectRepository _projects;
    private readonly ITranslationKeyRepository _keys;
    private readonly ITranslationStringRepository _strings;

    public TranslationExportService(
        IProjectRepository projects,
        ITranslationKeyRepository keys,
        ITranslationStringRepository strings)
    {
        _projects = projects;
        _keys = keys;
        _strings = strings;
    }

    public async Task<ExportedFile?> ExportAsync(
        string projectCode,
        TranslationExportQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Reject a bad ?format= as 400 before touching the store.
        var format = TranslationExporter.NormalizeFormat(query.Format);
        var statusFilter = ParseStatus(query.Status);

        var project = await _projects.GetBySlugAsync(Slug.From(projectCode ?? string.Empty), cancellationToken);
        if (project is null || !project.Active)
        {
            return null;
        }

        var columns = string.IsNullOrWhiteSpace(query.Language)
            ? project.EnabledLanguageCodes.ToList()
            : [query.Language.Trim()];

        var normalizedCategory = string.IsNullOrWhiteSpace(query.Category) ? null : query.Category.Trim();

        var keys = (await _keys.ListByProjectsAsync([project.Id], cancellationToken))
            .Where(k => query.IncludeInactiveKeys || k.Active)
            .Where(k => normalizedCategory is null
                || string.Equals(k.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k.KeyName, StringComparer.Ordinal)
            .ToList();

        var stringsByKey = (await _strings.ListByKeyIdsAsync(keys.Select(k => k.Id).ToList(), cancellationToken))
            .GroupBy(s => s.TranslationKeyId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<TranslationExportRow>(keys.Count);
        foreach (var key in keys)
        {
            var cells = stringsByKey.TryGetValue(key.Id, out var list)
                ? list
                : [];

            if (statusFilter is { } wanted && !cells.Any(s => s.ReviewState == wanted))
            {
                continue;
            }

            var byLanguage = cells.ToDictionary(s => s.LanguageCode, StringComparer.OrdinalIgnoreCase);
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var code in columns)
            {
                values[code] = byLanguage.TryGetValue(code, out var s) ? s.Value : null;
            }

            rows.Add(new TranslationExportRow(key.KeyName, key.Category, key.Description, values));
        }

        var data = new TranslationExportData(project.Slug, columns, rows);
        return TranslationExporter.Export(format, data);
    }

    private static ReviewState? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var candidate = status.Trim();
        if (!int.TryParse(candidate, out _)
            && Enum.TryParse<ReviewState>(candidate, ignoreCase: false, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ValidationException(
            $"'{status}' is not a valid review state. Expected one of: {string.Join(", ", Enum.GetNames<ReviewState>())}.");
    }
}
