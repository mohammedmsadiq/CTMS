using CTMS.Application.Common;
using CTMS.Application.Locales;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence;
using CTMS.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CTMS.Application.Tests;

public sealed class LocaleServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CtmsDbContext _context;
    private readonly LocaleService _service;
    private readonly Guid _projectId;

    public LocaleServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new CtmsDbContext(options);
        _context.Database.EnsureCreated();

        var project = new Project("Acme Web", "acme-web", "en");
        _context.Projects.Add(project);
        _context.SaveChanges();
        _projectId = project.Id;

        _service = new LocaleService(new LocaleRepository(_context), new ProjectRepository(_context), _context);
    }

    [Fact]
    public async Task CreateAsync_persists_the_locale()
    {
        var created = await _service.CreateAsync(_projectId, new CreateLocaleRequest("  fr-FR  ", "French", IsRtl: false));

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("fr-FR", created.Code);
        Assert.Equal("French", created.DisplayName);

        var persisted = await _context.Locales.AsNoTracking().SingleAsync();
        Assert.Equal("fr-FR", persisted.Code);
        Assert.Equal(_projectId, persisted.ProjectId);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_code_in_the_same_project()
    {
        await _service.CreateAsync(_projectId, new CreateLocaleRequest("de", "German"));

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAsync(_projectId, new CreateLocaleRequest("de", "Deutsch")));

        Assert.Equal(1, await _context.Locales.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_project()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateAsync(Guid.NewGuid(), new CreateLocaleRequest("es", "Spanish")));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_blank_code()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAsync(_projectId, new CreateLocaleRequest("   ", "Spanish")));
    }

    [Fact]
    public async Task DeleteAsync_removes_the_locale_and_cascades_to_its_translation_strings()
    {
        var locale = new Locale(_projectId, "it", "Italian");
        var key = new TranslationKey(_projectId, "checkout.title");
        _context.Locales.Add(locale);
        _context.TranslationKeys.Add(key);
        _context.SaveChanges();

        _context.TranslationStrings.Add(new TranslationString(key.Id, locale.Id, "Cassa", "author"));
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var deleted = await _service.DeleteAsync(_projectId, locale.Id);

        Assert.True(deleted);
        Assert.False(await _context.Locales.AnyAsync(l => l.Id == locale.Id));
        Assert.Equal(0, await _context.TranslationStrings.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_returns_false_when_the_locale_is_missing()
    {
        Assert.False(await _service.DeleteAsync(_projectId, Guid.NewGuid()));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
