using CTMS.Application.Common;
using CTMS.Application.Languages;
using CTMS.Application.Tests.Infrastructure;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class LanguageServiceTests : IDisposable
{
    private readonly CtmsTestHarness _harness;

    public LanguageServiceTests(MongoFixture fixture) => _harness = new CtmsTestHarness(fixture.ConnectionString);

    private LanguageService Service => _harness.LanguageService;

    [Fact]
    public async Task CreateAsync_persists_the_language()
    {
        var created = await Service.CreateAsync(new CreateLanguageRequest("  fr-FR  ", "French", FallbackCode: "en-GB"));

        Assert.Equal("fr-FR", created.Code);
        Assert.Equal("en-GB", created.FallbackCode);
        Assert.True(created.Active);

        var persisted = Assert.Single(await _harness.Languages.ListAllAsync());
        Assert.Equal("fr-FR", persisted.Code);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_code()
    {
        await Service.CreateAsync(new CreateLanguageRequest("de-DE", "German"));

        await Assert.ThrowsAsync<ConflictException>(
            () => Service.CreateAsync(new CreateLanguageRequest("de-DE", "Deutsch")));

        Assert.Single(await _harness.Languages.ListAllAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_a_self_referential_fallback()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Service.CreateAsync(new CreateLanguageRequest("fr-FR", "French", FallbackCode: "fr-fr")));
    }

    [Fact]
    public async Task ListAsync_hides_inactive_languages_unless_asked()
    {
        await Service.CreateAsync(new CreateLanguageRequest("en-GB", "English"));
        await Service.CreateAsync(new CreateLanguageRequest("it-IT", "Italian", Active: false));

        var active = await Service.ListAsync(includeInactive: false);
        Assert.Equal(["en-GB"], active.Select(l => l.Code));

        var all = await Service.ListAsync(includeInactive: true);
        Assert.Equal(["en-GB", "it-IT"], all.Select(l => l.Code));
    }

    [Fact]
    public async Task UpdateAsync_persists_name_fallback_rtl_and_active_changes()
    {
        await Service.CreateAsync(new CreateLanguageRequest("ar-AE", "Arabic"));

        var updated = await Service.UpdateAsync(
            "ar-AE",
            new UpdateLanguageRequest(Name: "العربية", FallbackCode: "en-GB", IsRtl: true, Active: false));

        Assert.NotNull(updated);
        Assert.Equal("العربية", updated!.Name);
        Assert.Equal("en-GB", updated.FallbackCode);
        Assert.True(updated.IsRtl);
        Assert.False(updated.Active);
    }

    [Fact]
    public async Task UpdateAsync_returns_null_for_an_unknown_code()
    {
        Assert.Null(await Service.UpdateAsync("zz-ZZ", new UpdateLanguageRequest(Name: "x")));
    }

    public void Dispose() => _harness.Dispose();
}
