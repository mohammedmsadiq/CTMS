using CTMS.Application.Common;
using CTMS.Application.Translations;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence;
using CTMS.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CTMS.Application.Tests;

public sealed class TranslationStringServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CtmsDbContext _context;
    private readonly TranslationStringService _service;
    private readonly Guid _projectId;
    private readonly Guid _keyId;
    private readonly Guid _localeId;

    public TranslationStringServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new CtmsDbContext(options);
        _context.Database.EnsureCreated();

        var project = new Project("Acme Web", "acme-web", "en");
        var key = new TranslationKey(project.Id, "checkout.title");
        var locale = new Locale(project.Id, "fr", "French");
        _context.Projects.Add(project);
        _context.TranslationKeys.Add(key);
        _context.Locales.Add(locale);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        _projectId = project.Id;
        _keyId = key.Id;
        _localeId = locale.Id;

        _service = new TranslationStringService(
            new TranslationStringRepository(_context),
            new TranslationKeyRepository(_context),
            new LocaleRepository(_context),
            _context);
    }

    [Fact]
    public async Task UpsertAsync_creates_a_draft_row_when_none_exists()
    {
        var result = await _service.UpsertAsync(
            _projectId,
            _keyId,
            _localeId,
            new UpsertTranslationStringRequest("Paiement", UpdatedBy: "alice"));

        Assert.True(result.Created);
        Assert.Equal("Draft", result.String.ReviewState);
        Assert.Equal("fr", result.String.LocaleCode);
        Assert.Equal("alice", result.String.UpdatedBy);
        Assert.Equal(1, await _context.TranslationStrings.CountAsync());
    }

    [Fact]
    public async Task UpsertAsync_updates_the_existing_row_on_the_second_call()
    {
        await _service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));

        var result = await _service.UpsertAsync(
            _projectId,
            _keyId,
            _localeId,
            new UpsertTranslationStringRequest("v2", UpdatedBy: "bob"));

        Assert.False(result.Created);
        Assert.Equal("v2", result.String.Value);
        Assert.Equal(1, await _context.TranslationStrings.CountAsync());
    }

    [Fact]
    public async Task UpsertAsync_moves_an_approved_string_back_to_needs_review_when_edited()
    {
        await _service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));
        await _service.ReviewAsync(_projectId, _keyId, _localeId, "submit", "alice");
        await _service.ReviewAsync(_projectId, _keyId, _localeId, "approve", "lead");

        var result = await _service.UpsertAsync(
            _projectId,
            _keyId,
            _localeId,
            new UpsertTranslationStringRequest("v2", UpdatedBy: "alice"));

        Assert.Equal("NeedsReview", result.String.ReviewState);
    }

    [Fact]
    public async Task UpsertAsync_leaves_a_draft_string_as_draft_when_edited()
    {
        await _service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));

        var result = await _service.UpsertAsync(
            _projectId,
            _keyId,
            _localeId,
            new UpsertTranslationStringRequest("v2"));

        Assert.Equal("Draft", result.String.ReviewState);
    }

    [Fact]
    public async Task UpsertAsync_rejects_a_stale_expected_version()
    {
        await _service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));

        var exception = await Assert.ThrowsAsync<ConcurrencyException>(
            () => _service.UpsertAsync(
                _projectId,
                _keyId,
                _localeId,
                new UpsertTranslationStringRequest("v2", ExpectedVersion: 999u)));

        Assert.Equal(0u, exception.CurrentVersion);
        var persisted = await _context.TranslationStrings.AsNoTracking().SingleAsync();
        Assert.Equal("v1", persisted.Value);
    }

    [Fact]
    public async Task UpsertAsync_rejects_a_locale_from_another_project()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UpsertAsync(_projectId, _keyId, Guid.NewGuid(), new UpsertTranslationStringRequest("v1")));
    }

    [Fact]
    public async Task ReviewAsync_returns_null_when_no_string_exists_for_the_locale()
    {
        Assert.Null(await _service.ReviewAsync(_projectId, _keyId, _localeId, "submit", "alice"));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
