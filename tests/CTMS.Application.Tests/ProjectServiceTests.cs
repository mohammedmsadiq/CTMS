using CTMS.Application.Common;
using CTMS.Application.Projects;
using CTMS.Application.Tests.Infrastructure;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class ProjectServiceTests : IDisposable
{
    private readonly CtmsTestHarness _harness;

    public ProjectServiceTests(MongoFixture fixture) => _harness = new CtmsTestHarness(fixture.ConnectionString);

    private ProjectService Service => _harness.ProjectService;

    [Fact]
    public async Task CreateAsync_persists_the_project_and_derives_a_slug_from_the_name()
    {
        var created = await Service.CreateAsync(new CreateProjectRequest("Acme Web", "en"));

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("acme-web", created.Slug);
        Assert.NotEqual(default, created.CreatedAt);

        var persisted = Assert.Single(await _harness.Projects.ListAsync());
        Assert.Equal("Acme Web", persisted.Name);
        Assert.Equal("en", persisted.BaseLocaleCode);
        Assert.Equal(created.Id, persisted.Id);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_slug()
    {
        await Service.CreateAsync(new CreateProjectRequest("Acme Web", "en"));

        var exception = await Assert.ThrowsAsync<SlugAlreadyInUseException>(
            () => Service.CreateAsync(new CreateProjectRequest("  ACME   Web  ", "fr")));

        Assert.Equal("acme-web", exception.Slug);
        Assert.Single(await _harness.Projects.ListAsync());
    }

    [Fact]
    public async Task CreateAsync_honours_an_explicit_slug()
    {
        var created = await Service.CreateAsync(
            new CreateProjectRequest("Acme Web", "en", Slug: "Marketing Site"));

        Assert.Equal("marketing-site", created.Slug);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_blank_name()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => Service.CreateAsync(new CreateProjectRequest("   ", "en")));
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_id()
    {
        Assert.Null(await Service.GetAsync(Guid.NewGuid()));
    }

    public void Dispose() => _harness.Dispose();
}
