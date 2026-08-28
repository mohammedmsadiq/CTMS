using CTMS.Application.Locales;
using CTMS.Domain.Locales;
using Microsoft.EntityFrameworkCore;

namespace CTMS.Infrastructure.Persistence.Repositories;

public sealed class LocaleRepository : ILocaleRepository
{
    private readonly CtmsDbContext _db;

    public LocaleRepository(CtmsDbContext db) => _db = db;

    public async Task<IReadOnlyList<Locale>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => await _db.Locales.AsNoTracking()
            .Where(l => l.ProjectId == projectId)
            .OrderBy(l => l.Code)
            .ToListAsync(cancellationToken);

    public Task<Locale?> GetAsync(Guid projectId, Guid localeId, CancellationToken cancellationToken = default)
        => _db.Locales.FirstOrDefaultAsync(l => l.Id == localeId && l.ProjectId == projectId, cancellationToken);

    public Task<bool> CodeExistsAsync(Guid projectId, string code, CancellationToken cancellationToken = default)
        => _db.Locales.AnyAsync(l => l.ProjectId == projectId && l.Code == code, cancellationToken);

    public Task AddAsync(Locale locale, CancellationToken cancellationToken = default)
    {
        _db.Locales.Add(locale);
        return Task.CompletedTask;
    }

    public async Task RemoveAsync(Locale locale, CancellationToken cancellationToken = default)
    {
        // The FK is configured OnDelete(DeleteBehavior.Cascade); delete the dependent strings
        // explicitly as well so the behaviour holds on providers that do not enforce FKs.
        var strings = await _db.TranslationStrings
            .Where(s => s.LocaleId == locale.Id)
            .ToListAsync(cancellationToken);

        _db.TranslationStrings.RemoveRange(strings);
        _db.Locales.Remove(locale);
    }
}
