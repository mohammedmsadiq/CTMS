using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence.Caching;

namespace CTMS.Application.Tests;

/// <summary>
/// Assemble-on-demand delivery: shared-merge, fallback fill, ordering, the read-through cache,
/// and the management grid / dashboard / missing / bulk-publish surface.
/// </summary>
[Collection("mongo")]
public sealed class PublishedTranslationsServiceTests : IDisposable
{
    private readonly CtmsTestHarness _harness;

    public PublishedTranslationsServiceTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);

        Seed.LanguageAsync(_harness, "en-GB").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "fr-FR", fallbackCode: "en-GB").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "fr-CA", fallbackCode: "fr-FR").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "de-DE", fallbackCode: "en-GB").GetAwaiter().GetResult();
    }

    private PublishedTranslationsService Service => _harness.PublishedTranslationsService;

    private async Task<Guid> AppAsync(string slug, bool shared, params string[] languages)
        => (await Seed.ApplicationAsync(_harness, slug, "en-GB", languages, isCommon: shared)).Id;

    [Fact]
    public async Task GetPublishedAsync_assembles_published_values_ordered_by_key()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB", "fr-FR");
        var start = await Seed.KeyAsync(_harness, app, "course.start", "Course");
        var resume = await Seed.KeyAsync(_harness, app, "course.resume", "Course");
        await Seed.StringAsync(_harness, start.Id, "fr-FR", "Commencer", ReviewState.Published);
        await Seed.StringAsync(_harness, resume.Id, "fr-FR", "Reprendre", ReviewState.Published);
        await Seed.StringAsync(_harness, resume.Id, "en-GB", "ignored-draft-en", ReviewState.Draft); // different language, not surfaced

        var view = await Service.GetPublishedAsync("icoach", "fr-FR");

        Assert.NotNull(view);
        Assert.Equal(["course.resume", "course.start"], view!.Translations.Keys);
        Assert.Equal("Commencer", view.Translations["course.start"]);
        Assert.False(string.IsNullOrWhiteSpace(view.Hash));
    }

    [Fact]
    public async Task GetPublishedAsync_merges_shared_application_keys()
    {
        var common = await AppAsync("common", shared: true, "en-GB", "fr-FR");
        var save = await Seed.KeyAsync(_harness, common, "common.save", "Common");
        await Seed.StringAsync(_harness, save.Id, "fr-FR", "Enregistrer", ReviewState.Published);

        var icoach = await AppAsync("icoach", shared: false, "en-GB", "fr-FR");
        var start = await Seed.KeyAsync(_harness, icoach, "course.start", "Course");
        await Seed.StringAsync(_harness, start.Id, "fr-FR", "Commencer", ReviewState.Published);

        var view = await Service.GetPublishedAsync("icoach", "fr-FR");

        Assert.Equal("Enregistrer", view!.Translations["common.save"]);
        Assert.Equal("Commencer", view.Translations["course.start"]);
    }

    [Fact]
    public async Task GetPublishedAsync_application_value_wins_over_shared_on_a_key_name_collision()
    {
        var common = await AppAsync("common", shared: true, "en-GB", "fr-FR");
        var sharedBrand = await Seed.KeyAsync(_harness, common, "brand.name", "Common");
        await Seed.StringAsync(_harness, sharedBrand.Id, "fr-FR", "Shared Brand", ReviewState.Published);

        var icoach = await AppAsync("icoach", shared: false, "en-GB", "fr-FR");
        var appBrand = await Seed.KeyAsync(_harness, icoach, "brand.name", "Common");
        await Seed.StringAsync(_harness, appBrand.Id, "fr-FR", "iCoach Brand", ReviewState.Published);

        var view = await Service.GetPublishedAsync("icoach", "fr-FR");

        Assert.Equal("iCoach Brand", view!.Translations["brand.name"]);
    }

    [Fact]
    public async Task GetPublishedAsync_fills_from_the_fallback_chain()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB", "fr-FR", "fr-CA");
        var onlyFr = await Seed.KeyAsync(_harness, app, "a.only-fr", "Course");
        var onlyEn = await Seed.KeyAsync(_harness, app, "b.only-en", "Course");
        await Seed.StringAsync(_harness, onlyFr.Id, "fr-FR", "Valeur FR", ReviewState.Published);
        await Seed.StringAsync(_harness, onlyEn.Id, "en-GB", "English value", ReviewState.Published);

        // Request fr-CA: chain is fr-CA -> fr-FR -> en-GB.
        var view = await Service.GetPublishedAsync("icoach", "fr-CA");

        Assert.Equal("Valeur FR", view!.Translations["a.only-fr"]);
        Assert.Equal("English value", view.Translations["b.only-en"]);
    }

    [Fact]
    public async Task GetPublishedAsync_omits_keys_with_no_published_value_anywhere()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB", "fr-FR");
        var draftOnly = await Seed.KeyAsync(_harness, app, "c.draft-only", "Course");
        await Seed.StringAsync(_harness, draftOnly.Id, "fr-FR", "brouillon", ReviewState.Approved); // not Published

        var view = await Service.GetPublishedAsync("icoach", "fr-FR");

        Assert.Empty(view!.Translations);
    }

    [Fact]
    public async Task GetPublishedAsync_never_serves_an_archived_string()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB", "fr-FR");
        var live = await Seed.KeyAsync(_harness, app, "a.live", "Course");
        var dead = await Seed.KeyAsync(_harness, app, "b.dead", "Course");
        await Seed.StringAsync(_harness, live.Id, "fr-FR", "Vivant", ReviewState.Published);
        await Seed.StringAsync(_harness, dead.Id, "fr-FR", "Mort", ReviewState.Archived);

        var view = await Service.GetPublishedAsync("icoach", "fr-FR");

        Assert.Equal(["a.live"], view!.Translations.Keys);
    }

    [Fact]
    public async Task GetGridAsync_hides_archived_cells_unless_status_is_Archived()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB");
        var key = await Seed.KeyAsync(_harness, app, "a.one", "Course");
        await Seed.StringAsync(_harness, key.Id, "en-GB", "Retired", ReviewState.Archived);

        var normal = await Service.GetGridAsync("icoach", null, null, null, 0, 50);
        Assert.DoesNotContain("en-GB", normal!.Items.Single().Values.Keys);

        var archived = await Service.GetGridAsync("icoach", null, null, null, 0, 50, "Archived");
        Assert.Equal("Archived", archived!.Items.Single().Values["en-GB"].Status);
    }

    [Fact]
    public async Task GetPublishedAsync_returns_null_for_unknown_disabled_or_not_enabled_targets()
    {
        await AppAsync("icoach", shared: false, "en-GB");
        await Seed.LanguageAsync(_harness, "it-IT");

        Assert.Null(await Service.GetPublishedAsync("nope", "en-GB"));       // unknown application
        Assert.Null(await Service.GetPublishedAsync("icoach", "zz-ZZ"));     // unknown language
        Assert.Null(await Service.GetPublishedAsync("icoach", "it-IT"));     // language not enabled for the app
    }

    [Fact]
    public async Task GetPublishedAsync_is_served_through_the_cache_on_the_second_call()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB", "fr-FR");
        var start = await Seed.KeyAsync(_harness, app, "course.start", "Course");
        await Seed.StringAsync(_harness, start.Id, "fr-FR", "Commencer", ReviewState.Published);

        var first = await Service.GetPublishedAsync("icoach", "fr-FR");
        Assert.NotNull(await _harness.DistributedCache.GetAsync(PublishedTranslationsCache.KeyFor("icoach", "fr-FR")));

        var second = await Service.GetPublishedAsync("icoach", "FR-fr"); // normalised to the same key
        Assert.Equal(first!.Hash, second!.Hash);
    }

    [Fact]
    public async Task GetGridAsync_returns_one_row_per_key_with_a_cell_per_enabled_language()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB", "fr-FR");
        var start = await Seed.KeyAsync(_harness, app, "course.start", "Course");
        await Seed.StringAsync(_harness, start.Id, "en-GB", "Start", ReviewState.Published);
        await Seed.StringAsync(_harness, start.Id, "fr-FR", "Commencer", ReviewState.InReview);
        await Seed.KeyAsync(_harness, app, "course.resume", "Course"); // no values at all

        var page = await Service.GetGridAsync("icoach", null, null, null, 0, 50);

        Assert.NotNull(page);
        Assert.Equal(2, page!.Total);
        var row = page.Items.Single(r => r.Key == "course.start");
        Assert.Equal("Start", row.Values["en-GB"].Value);
        Assert.Equal("Published", row.Values["en-GB"].Status);
        Assert.Equal("InReview", row.Values["fr-FR"].Status);
        Assert.DoesNotContain("course.resume", page.Items.Single(r => r.Key == "course.resume").Values.Keys);
    }

    [Fact]
    public async Task GetGridAsync_search_matches_key_name_or_any_value()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB");
        var start = await Seed.KeyAsync(_harness, app, "course.start", "Course");
        var nav = await Seed.KeyAsync(_harness, app, "nav.home", "Navigation");
        await Seed.StringAsync(_harness, start.Id, "en-GB", "Begin the lesson", ReviewState.Draft);
        await Seed.StringAsync(_harness, nav.Id, "en-GB", "Home", ReviewState.Draft);

        var byValue = await Service.GetGridAsync("icoach", null, null, "lesson", 0, 50);
        Assert.Equal(["course.start"], byValue!.Items.Select(r => r.Key));

        var byKey = await Service.GetGridAsync("icoach", null, null, "nav.", 0, 50);
        Assert.Equal(["nav.home"], byKey!.Items.Select(r => r.Key));
    }

    [Fact]
    public async Task GetCategoriesAsync_returns_distinct_non_empty_categories()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB");
        await Seed.KeyAsync(_harness, app, "a", "Course");
        await Seed.KeyAsync(_harness, app, "b", "Course");
        await Seed.KeyAsync(_harness, app, "c", "Navigation");

        var categories = await Service.GetCategoriesAsync("icoach");

        Assert.Equal(["Course", "Navigation"], categories);
    }

    [Fact]
    public async Task GetDashboardAsync_counts_translated_as_any_non_draft_value()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB", "fr-FR");
        var k1 = await Seed.KeyAsync(_harness, app, "k1", "Course");
        var k2 = await Seed.KeyAsync(_harness, app, "k2", "Course");
        await Seed.StringAsync(_harness, k1.Id, "en-GB", "one", ReviewState.Published);
        await Seed.StringAsync(_harness, k2.Id, "en-GB", "two", ReviewState.Approved);
        await Seed.StringAsync(_harness, k1.Id, "fr-FR", "un", ReviewState.Draft); // draft does not count

        var dashboard = await Service.GetDashboardAsync("icoach");

        Assert.NotNull(dashboard);
        Assert.Equal(1, dashboard!.ProjectCount);
        Assert.Equal(2, dashboard.LanguageCount);
        Assert.Equal(2, dashboard.KeyCount);

        var en = dashboard.Coverage.Single(c => c.LanguageCode == "en-GB");
        Assert.Equal(2, en.TranslatedCount);
        Assert.Equal(100d, en.Percent);

        var fr = dashboard.Coverage.Single(c => c.LanguageCode == "fr-FR");
        Assert.Equal(0, fr.TranslatedCount);
        Assert.Equal(2, fr.MissingCount);
        Assert.Equal(2, dashboard.TotalMissing);
    }

    [Fact]
    public async Task GetMissingAsync_lists_keys_missing_a_non_draft_value_per_language()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB", "fr-FR");
        var k1 = await Seed.KeyAsync(_harness, app, "k1", "Course");
        var k2 = await Seed.KeyAsync(_harness, app, "k2", "Course");
        await Seed.StringAsync(_harness, k1.Id, "en-GB", "one", ReviewState.Published);
        await Seed.StringAsync(_harness, k1.Id, "fr-FR", "un", ReviewState.Approved);
        await Seed.StringAsync(_harness, k2.Id, "en-GB", "two", ReviewState.Approved);

        var page = await Service.GetMissingAsync("icoach", languageCode: null, 0, 50);

        var row = Assert.Single(page!.Items);
        Assert.Equal("k2", row.Key);
        Assert.Equal(["fr-FR"], row.MissingLanguages);
    }

    [Fact]
    public async Task BulkPublishAsync_publishes_every_approved_string_and_returns_the_count()
    {
        var app = await AppAsync("icoach", shared: false, "en-GB", "fr-FR");
        var k1 = await Seed.KeyAsync(_harness, app, "k1", "Course");
        var k2 = await Seed.KeyAsync(_harness, app, "k2", "Course");
        await Seed.StringAsync(_harness, k1.Id, "en-GB", "one", ReviewState.Approved);
        await Seed.StringAsync(_harness, k2.Id, "fr-FR", "deux", ReviewState.Approved);
        await Seed.StringAsync(_harness, k2.Id, "en-GB", "two", ReviewState.Draft); // not approved

        var result = await Service.BulkPublishAsync(new PublishTranslationsRequest("icoach"), "release-bot");

        Assert.Equal(2, result.Published);
        Assert.Equal(ReviewState.Published, (await _harness.Strings.GetAsync(k1.Id, "en-GB"))!.ReviewState);
        Assert.Equal(ReviewState.Draft, (await _harness.Strings.GetAsync(k2.Id, "en-GB"))!.ReviewState);

        var audit = await _harness.Audit.ListByEntityAsync("TranslationString", (await _harness.Strings.GetAsync(k1.Id, "en-GB"))!.Id);
        Assert.Contains(audit, e => e.Action == Domain.Audit.AuditAction.Published && e.Actor == "release-bot");
    }

    [Fact]
    public async Task BulkPublishAsync_for_a_shared_application_invalidates_every_applications_cache_for_the_language()
    {
        var common = await AppAsync("common", shared: true, "en-GB", "fr-FR");
        var save = await Seed.KeyAsync(_harness, common, "common.save", "Common");
        await Seed.StringAsync(_harness, save.Id, "fr-FR", "Enregistrer", ReviewState.Published);

        var icoach = await AppAsync("icoach", shared: false, "en-GB", "fr-FR");
        var start = await Seed.KeyAsync(_harness, icoach, "course.start", "Course");
        await Seed.StringAsync(_harness, start.Id, "fr-FR", "Commencer", ReviewState.Published);

        // Prime icoach/fr-FR (which merges the shared common.save).
        await Service.GetPublishedAsync("icoach", "fr-FR");
        Assert.NotNull(await _harness.DistributedCache.GetAsync(PublishedTranslationsCache.KeyFor("icoach", "fr-FR")));

        // Approve a new shared value and bulk-publish the shared app for fr-FR.
        var cancel = await Seed.KeyAsync(_harness, common, "common.cancel", "Common");
        await Seed.StringAsync(_harness, cancel.Id, "fr-FR", "Annuler", ReviewState.Approved);

        await Service.BulkPublishAsync(new PublishTranslationsRequest("common", "fr-FR"), "release-bot");

        // icoach's cache entry for fr-FR was invalidated by the shared-app fan-out.
        Assert.Null(await _harness.DistributedCache.GetAsync(PublishedTranslationsCache.KeyFor("icoach", "fr-FR")));
    }

    public void Dispose() => _harness.Dispose();
}
