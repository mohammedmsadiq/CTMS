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
    public async Task CreateAsync_persists_the_application_and_derives_a_code_from_the_name()
    {
        var created = await Service.CreateAsync(new CreateProjectRequest("Acme Web", "en-GB"));

        Assert.Equal("acme-web", created.Code);
        Assert.False(created.IsCommon);
        Assert.True(created.Active);
        Assert.NotEqual(default, created.CreatedAt);

        var persisted = Assert.Single(await _harness.Projects.ListAsync(includeInactive: true));
        Assert.Equal("Acme Web", persisted.Name);
        Assert.Equal("en-GB", persisted.BaseLanguageCode);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_code()
    {
        await Service.CreateAsync(new CreateProjectRequest("Acme Web", "en-GB"));

        var exception = await Assert.ThrowsAsync<SlugAlreadyInUseException>(
            () => Service.CreateAsync(new CreateProjectRequest("  ACME   Web  ", "fr-FR")));

        Assert.Equal("acme-web", exception.Slug);
        Assert.Single(await _harness.Projects.ListAsync(includeInactive: true));
    }

    [Fact]
    public async Task CreateAsync_can_mark_an_application_shared()
    {
        var created = await Service.CreateAsync(
            new CreateProjectRequest("Common", "en-GB", IsCommon: true));

        Assert.True(created.IsCommon);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_blank_name()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => Service.CreateAsync(new CreateProjectRequest("   ", "en-GB")));
    }

    [Fact]
    public async Task CreateAsync_with_enabled_languages_rejects_an_unknown_language()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => Service.CreateAsync(new CreateProjectRequest(
                "Acme", "en-GB", EnabledLanguageCodes: ["en-GB"])));
    }

    [Fact]
    public async Task EnableLanguageAsync_adds_an_active_language_to_the_enabled_set()
    {
        await Seed.LanguageAsync(_harness, "en-GB");
        await Seed.LanguageAsync(_harness, "fr-FR");
        await Service.CreateAsync(new CreateProjectRequest("Acme", "en-GB", EnabledLanguageCodes: ["en-GB"]));

        var updated = await Service.EnableLanguageAsync("acme", "fr-FR");

        Assert.NotNull(updated);
        Assert.Equal(["en-GB", "fr-FR"], updated!.EnabledLanguageCodes);
    }

    [Fact]
    public async Task EnableLanguageAsync_rejects_an_inactive_language()
    {
        await Seed.LanguageAsync(_harness, "en-GB");
        await Seed.LanguageAsync(_harness, "de-DE", active: false);
        await Service.CreateAsync(new CreateProjectRequest("Acme", "en-GB"));

        await Assert.ThrowsAsync<ValidationException>(() => Service.EnableLanguageAsync("acme", "de-DE"));
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_code()
    {
        Assert.Null(await Service.GetAsync("nope"));
    }

    public void Dispose() => _harness.Dispose();
}
