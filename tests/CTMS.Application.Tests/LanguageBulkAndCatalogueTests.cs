using CTMS.Application.Common;
using CTMS.Application.Languages;
using CTMS.Application.Tests.Infrastructure;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class LanguageBulkAndCatalogueTests : IDisposable
{
    private readonly CtmsTestHarness _harness;

    public LanguageBulkAndCatalogueTests(MongoFixture fixture)
        => _harness = new CtmsTestHarness(fixture.ConnectionString);

    private LanguageService Service => _harness.LanguageService;

    [Fact]
    public void Suggestions_are_a_static_non_persisted_catalogue_with_rtl_flags()
    {
        var suggestions = Service.Suggestions();

        Assert.True(suggestions.Count >= 35);
        Assert.Contains(suggestions, s => s.Code == "en-GB" && !s.IsRtl);
        Assert.Contains(suggestions, s => s.Code == "ar-SA" && s.IsRtl);
        Assert.Contains(suggestions, s => s.Code == "he-IL" && s.IsRtl);
        Assert.All(suggestions.Where(s => s.Code.StartsWith("ar-", StringComparison.Ordinal)), s => Assert.True(s.IsRtl));
    }

    [Fact]
    public async Task BulkCreateAsync_creates_the_new_ones_and_skips_the_existing_ones()
    {
        await Service.CreateAsync(new CreateLanguageRequest("en-GB", "English"));

        var result = await Service.BulkCreateAsync(new BulkCreateLanguagesRequest(
        [
            new BulkCreateLanguageItem("en-GB", "English"),          // already exists
            new BulkCreateLanguageItem("fr-FR", "French", "en-GB"),
            new BulkCreateLanguageItem("ar-AE", "Arabic (UAE)", IsRtl: true),
        ]));

        Assert.Equal(["fr-FR", "ar-AE"], result.Created);
        Assert.Equal(["en-GB"], result.Skipped);

        var all = await _harness.Languages.ListAllAsync();
        Assert.Equal(["ar-AE", "en-GB", "fr-FR"], all.Select(l => l.Code).OrderBy(c => c, StringComparer.Ordinal));
        Assert.True(all.Single(l => l.Code == "ar-AE").IsRtl);
        Assert.Equal("en-GB", all.Single(l => l.Code == "fr-FR").FallbackCode);
    }

    [Fact]
    public async Task BulkCreateAsync_is_idempotent_when_run_twice()
    {
        var request = new BulkCreateLanguagesRequest(
        [
            new BulkCreateLanguageItem("de-DE", "German"),
            new BulkCreateLanguageItem("es-ES", "Spanish"),
        ]);

        var first = await Service.BulkCreateAsync(request);
        var second = await Service.BulkCreateAsync(request);

        Assert.Equal(["de-DE", "es-ES"], first.Created);
        Assert.Empty(second.Created);
        Assert.Equal(["de-DE", "es-ES"], second.Skipped);
        Assert.Equal(2, (await _harness.Languages.ListAllAsync()).Count);
    }

    [Fact]
    public async Task BulkCreateAsync_rejects_an_entry_with_a_blank_code_or_name()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service.BulkCreateAsync(
            new BulkCreateLanguagesRequest([new BulkCreateLanguageItem("  ", "Nameless")])));

        await Assert.ThrowsAsync<ValidationException>(() => Service.BulkCreateAsync(
            new BulkCreateLanguagesRequest([new BulkCreateLanguageItem("nb-NO", "  ")])));
    }

    public void Dispose() => _harness.Dispose();
}
