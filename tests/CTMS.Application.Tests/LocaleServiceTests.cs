using CTMS.Application.Common;
using CTMS.Application.Locales;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class LocaleServiceTests : IDisposable
{
    private readonly CtmsTestHarness _harness;
    private readonly Guid _projectId;

    public LocaleServiceTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);

        var project = new Project("Acme Web", "acme-web", "en");
        _harness.Projects.AddAsync(project).GetAwaiter().GetResult();
        _projectId = project.Id;
    }

    private LocaleService Service => _harness.LocaleService;

    [Fact]
    public async Task CreateAsync_persists_the_locale()
    {
        var created = await Service.CreateAsync(_projectId, new CreateLocaleRequest("  fr-FR  ", "French", IsRtl: false));

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("fr-FR", created.Code);
        Assert.Equal("French", created.DisplayName);

        var persisted = Assert.Single(await _harness.Locales.ListByProjectAsync(_projectId));
        Assert.Equal("fr-FR", persisted.Code);
        Assert.Equal(_projectId, persisted.ProjectId);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_code_in_the_same_project()
    {
        await Service.CreateAsync(_projectId, new CreateLocaleRequest("de", "German"));

        await Assert.ThrowsAsync<ConflictException>(
            () => Service.CreateAsync(_projectId, new CreateLocaleRequest("de", "Deutsch")));

        Assert.Single(await _harness.Locales.ListByProjectAsync(_projectId));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_project()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service.CreateAsync(Guid.NewGuid(), new CreateLocaleRequest("es", "Spanish")));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_blank_code()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => Service.CreateAsync(_projectId, new CreateLocaleRequest("   ", "Spanish")));
    }

    [Fact]
    public async Task UpdateAsync_persists_display_name_and_rtl_changes()
    {
        var created = await Service.CreateAsync(_projectId, new CreateLocaleRequest("ar", "Arabic"));

        var updated = await Service.UpdateAsync(
            _projectId,
            created.Id,
            new UpdateLocaleRequest(DisplayName: "العربية", IsRtl: true));

        Assert.NotNull(updated);
        Assert.Equal("العربية", updated!.DisplayName);
        Assert.True(updated.IsRtl);

        var reloaded = await Service.GetAsync(_projectId, created.Id);
        Assert.Equal("العربية", reloaded!.DisplayName);
        Assert.True(reloaded.IsRtl);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_locale_and_cascades_to_its_translation_strings()
    {
        var locale = new Locale(_projectId, "it", "Italian");
        var key = new TranslationKey(_projectId, "checkout.title");
        await _harness.Locales.AddAsync(locale);
        await _harness.Keys.AddAsync(key);
        await _harness.Strings.AddAsync(new TranslationString(key.Id, locale.Id, "Cassa", "author"));

        var deleted = await Service.DeleteAsync(_projectId, locale.Id);

        Assert.True(deleted);
        Assert.Null(await _harness.Locales.GetAsync(_projectId, locale.Id));
        Assert.Empty(await _harness.Strings.ListByKeyAsync(key.Id));
    }

    [Fact]
    public async Task DeleteAsync_returns_false_when_the_locale_is_missing()
    {
        Assert.False(await Service.DeleteAsync(_projectId, Guid.NewGuid()));
    }

    public void Dispose() => _harness.Dispose();
}
