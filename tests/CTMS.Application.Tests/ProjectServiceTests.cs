using CTMS.Application.Common;
using CTMS.Application.Projects;
using CTMS.Infrastructure.Persistence;
using CTMS.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CTMS.Application.Tests;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CtmsDbContext _context;
    private readonly ProjectService _service;

    public ProjectServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new CtmsDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ProjectService(new ProjectRepository(_context), _context);
    }

    [Fact]
    public async Task CreateAsync_persists_the_project_and_derives_a_slug_from_the_name()
    {
        var created = await _service.CreateAsync(new CreateProjectRequest("Acme Web", "en"));

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("acme-web", created.Slug);
        Assert.NotEqual(default, created.CreatedAt);

        var persisted = await _context.Projects.AsNoTracking().SingleAsync();
        Assert.Equal("Acme Web", persisted.Name);
        Assert.Equal("en", persisted.BaseLocaleCode);
        Assert.Equal(created.Id, persisted.Id);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_slug()
    {
        await _service.CreateAsync(new CreateProjectRequest("Acme Web", "en"));

        var exception = await Assert.ThrowsAsync<SlugAlreadyInUseException>(
            () => _service.CreateAsync(new CreateProjectRequest("  ACME   Web  ", "fr")));

        Assert.Equal("acme-web", exception.Slug);
        Assert.Equal(1, await _context.Projects.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_honours_an_explicit_slug()
    {
        var created = await _service.CreateAsync(
            new CreateProjectRequest("Acme Web", "en", Slug: "Marketing Site"));

        Assert.Equal("marketing-site", created.Slug);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_blank_name()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAsync(new CreateProjectRequest("   ", "en")));
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_id()
    {
        Assert.Null(await _service.GetAsync(Guid.NewGuid()));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
