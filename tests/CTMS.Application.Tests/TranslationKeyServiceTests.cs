using CTMS.Application.Common;
using CTMS.Application.Translations;
using CTMS.Domain.Projects;
using CTMS.Infrastructure.Persistence;
using CTMS.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CTMS.Application.Tests;

public sealed class TranslationKeyServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CtmsDbContext _context;
    private readonly TranslationKeyService _service;
    private readonly Guid _projectId;

    public TranslationKeyServiceTests()
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

        _service = new TranslationKeyService(
            new TranslationKeyRepository(_context),
            new ProjectRepository(_context),
            _context);
    }

    [Fact]
    public async Task CreateAsync_persists_the_key()
    {
        var created = await _service.CreateAsync(
            _projectId,
            new CreateTranslationKeyRequest("checkout.button.submit", "Primary CTA"));

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("checkout.button.submit", created.KeyName);

        var persisted = await _context.TranslationKeys.AsNoTracking().SingleAsync();
        Assert.Equal("checkout.button.submit", persisted.KeyName);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_key_name()
    {
        await _service.CreateAsync(_projectId, new CreateTranslationKeyRequest("home.title"));

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAsync(_projectId, new CreateTranslationKeyRequest("home.title")));

        Assert.Equal(1, await _context.TranslationKeys.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_an_invalid_character_set()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAsync(_projectId, new CreateTranslationKeyRequest("home title!")));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_project()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateAsync(Guid.NewGuid(), new CreateTranslationKeyRequest("home.title")));
    }

    [Fact]
    public async Task ListAsync_pages_results_and_reports_the_total()
    {
        for (var i = 0; i < 5; i++)
        {
            await _service.CreateAsync(_projectId, new CreateTranslationKeyRequest($"key.{i:D2}"));
        }

        var page = await _service.ListAsync(_projectId, skip: 1, take: 2);

        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(["key.01", "key.02"], page.Items.Select(k => k.KeyName));
    }

    [Fact]
    public async Task ListAsync_clamps_take_and_normalises_a_negative_skip()
    {
        for (var i = 0; i < 3; i++)
        {
            await _service.CreateAsync(_projectId, new CreateTranslationKeyRequest($"key.{i:D2}"));
        }

        var page = await _service.ListAsync(_projectId, skip: -10, take: 10_000);

        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.Items.Count);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
