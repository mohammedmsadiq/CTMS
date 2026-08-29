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
