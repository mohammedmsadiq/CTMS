using CTMS.Application.Common;
using CTMS.Domain.Languages;

namespace CTMS.Application.Languages;

/// <summary>Use-case orchestration for the global language catalogue.</summary>
public sealed class LanguageService
{
    private readonly ILanguageRepository _languages;
    private readonly IUnitOfWork _unitOfWork;

    public LanguageService(ILanguageRepository languages, IUnitOfWork unitOfWork)
    {
        _languages = languages;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<LanguageDto>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var languages = await _languages.ListAsync(includeInactive, cancellationToken);
        return languages.Select(ToDto).ToList();
    }

    public async Task<LanguageDto?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        var language = await _languages.GetByCodeAsync(NormalizeCode(code), cancellationToken);
        return language is null ? null : ToDto(language);
    }

    public async Task<LanguageDto> CreateAsync(
        CreateLanguageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = NormalizeCode(request.Code);
        if (code.Length == 0)
        {
            throw new ValidationException("A language code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("A language name is required.");
        }

        if (await _languages.CodeExistsAsync(code, cancellationToken))
        {
            throw new ConflictException($"A language with the code '{code}' already exists.");
        }

        var language = new Language(code, request.Name, request.FallbackCode, request.IsRtl, request.Active);

        await _languages.AddAsync(language, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(language);
    }

    /// <summary>
    /// Registers every language in <paramref name="request"/> that does not already exist. Existing
    /// codes are skipped, not errored, so the call is idempotent. A single item with a blank code
    /// or name is a <see cref="ValidationException"/>.
    /// </summary>
    public async Task<BulkCreateLanguagesResult> BulkCreateAsync(
        BulkCreateLanguagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Languages is null || request.Languages.Count == 0)
        {
            throw new ValidationException("At least one language is required.");
        }

        var existing = (await _languages.ListAllAsync(cancellationToken))
            .Select(l => l.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<string>();
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in request.Languages)
        {
            var code = NormalizeCode(item?.Code);
            if (code.Length == 0)
            {
                throw new ValidationException("A language code is required for every entry.");
            }

            if (string.IsNullOrWhiteSpace(item!.Name))
            {
                throw new ValidationException($"A name is required for language '{code}'.");
            }

            if (!seen.Add(code))
            {
                continue; // duplicate within the same request
            }

            if (existing.Contains(code))
            {
                skipped.Add(code);
                continue;
            }

            var language = new Language(code, item.Name, item.FallbackCode, item.IsRtl ?? false);

            try
            {
                await _languages.AddAsync(language, cancellationToken);
                created.Add(code);
            }
            catch (ConflictException)
            {
                // Lost a race with a concurrent create — treat as already-present.
                skipped.Add(code);
            }
        }

        if (created.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new BulkCreateLanguagesResult(created, skipped);
    }

    public async Task<LanguageDto?> UpdateAsync(
        string code,
        UpdateLanguageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var language = await _languages.GetByCodeAsync(NormalizeCode(code), cancellationToken);
        if (language is null)
        {
            return null;
        }

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ValidationException("A language name cannot be blank.");
            }

            language.SetName(request.Name);
        }

        if (request.FallbackCode is not null)
        {
            language.SetFallbackCode(request.FallbackCode.Length == 0 ? null : request.FallbackCode);
        }

        if (request.IsRtl is { } isRtl)
        {
            language.SetRightToLeft(isRtl);
        }

        if (request.Active is { } active)
        {
            language.SetActive(active);
        }

        await _languages.UpdateAsync(language, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(language);
    }

    private static string NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : string.Join(' ', code.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static LanguageDto ToDto(Language language) => new(
        language.Code,
        language.Name,
        language.FallbackCode,
        language.IsRtl,
        language.Active,
        language.CreatedAt,
        language.UpdatedAt);
}
