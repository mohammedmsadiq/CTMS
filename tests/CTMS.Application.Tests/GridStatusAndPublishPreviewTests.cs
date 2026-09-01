using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

/// <summary>
/// The grid <c>status</c> filter and shared-value <c>source</c> provenance, plus the publish
/// preview diff.
/// </summary>
[Collection("mongo")]
public sealed class GridStatusAndPublishPreviewTests : IDisposable
{
    private readonly CtmsTestHarness _harness;

    public GridStatusAndPublishPreviewTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);
        Seed.LanguageAsync(_harness, "en-GB").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "fr-FR", fallbackCode: "en-GB").GetAwaiter().GetResult();
    }

    private async Task<Guid> AppAsync(string slug, bool shared = false)
        => (await Seed.ApplicationAsync(_harness, slug, "en-GB", ["en-GB", "fr-FR"], isCommon: shared)).Id;

    [Fact]
    public async Task Grid_status_filter_keeps_rows_with_at_least_one_matching_cell_but_returns_all_cells()
    {
        var app = await AppAsync("nimbus");
        var k1 = await Seed.KeyAsync(_harness, app, "a.one", "Course");
        var k2 = await Seed.KeyAsync(_harness, app, "b.two", "Course");
        await Seed.StringAsync(_harness, k1.Id, "en-GB", "One EN", ReviewState.Approved);
        await Seed.StringAsync(_harness, k1.Id, "fr-FR", "Un FR", ReviewState.Draft);
        await Seed.StringAsync(_harness, k2.Id, "en-GB", "Two EN", ReviewState.Draft);

        var approved = await _harness.PublishedTranslationsService.GetGridAsync(
            "nimbus", null, null, null, 0, 50, "Approved");

        var row = Assert.Single(approved!.Items);
        Assert.Equal("a.one", row.Key);
        // The row still carries every cell, not just the Approved one.
        Assert.Equal("Approved", row.Values["en-GB"].Status);
        Assert.Equal("Draft", row.Values["fr-FR"].Status);
    }

    [Fact]
    public async Task Grid_status_filter_rejects_an_invalid_value()
        => await Assert.ThrowsAsync<ValidationException>(() => _harness.PublishedTranslationsService.GetGridAsync(
            "nimbus", null, null, null, 0, 50, "Bogus"));

    [Fact]
    public async Task Grid_tags_the_source_of_each_cell_app_vs_shared()
    {
        var common = await AppAsync("common", shared: true);
        var sharedKey = await Seed.KeyAsync(_harness, common, "common.save", "Common");
        await Seed.StringAsync(_harness, sharedKey.Id, "fr-FR", "Enregistrer", ReviewState.Published);

        var app = await AppAsync("nimbus");
        var ownKey = await Seed.KeyAsync(_harness, app, "course.start", "Course");
        await Seed.StringAsync(_harness, ownKey.Id, "fr-FR", "Commencer", ReviewState.Draft);

        var grid = await _harness.PublishedTranslationsService.GetGridAsync("nimbus", null, null, null, 0, 50);

        var own = grid!.Items.Single(r => r.Key == "course.start");
        Assert.Equal("app", own.Values["fr-FR"].Source);

        var shared = grid.Items.Single(r => r.Key == "common.save");
        Assert.Equal("shared:common", shared.Values["fr-FR"].Source);
    }

    [Fact]
    public async Task Publish_preview_classifies_added_and_changed_and_requires_a_language()
    {
        var app = await AppAsync("nimbus");
        var kAdded = await Seed.KeyAsync(_harness, app, "a.added", "Course");
        var kChanged = await Seed.KeyAsync(_harness, app, "b.changed", "Course");
        var kSame = await Seed.KeyAsync(_harness, app, "c.same", "Course");

        // "added": an Approved fr-FR value, and nothing published for the key in any language.
        await Seed.StringAsync(_harness, kAdded.Id, "fr-FR", "Nouveau", ReviewState.Approved);

        // "changed": today fr-FR is served from the published en-GB fallback; publishing the
        // Approved fr-FR value would replace it.
        await Seed.StringAsync(_harness, kChanged.Id, "en-GB", "Old EN", ReviewState.Published);
        await Seed.StringAsync(_harness, kChanged.Id, "fr-FR", "New FR", ReviewState.Approved);

        // no change: the Approved fr-FR value equals the published en-GB fallback.
        await Seed.StringAsync(_harness, kSame.Id, "en-GB", "Same", ReviewState.Published);
        await Seed.StringAsync(_harness, kSame.Id, "fr-FR", "Same", ReviewState.Approved);

        var preview = await _harness.PublishedTranslationsService.GetPublishPreviewAsync("nimbus", "fr-FR");

        Assert.Equal(1, preview!.AddedCount);
        Assert.Equal(1, preview.ChangedCount);
        Assert.Contains(preview.Changes, c => c is { Key: "a.added", Kind: "added", CurrentValue: null, NewValue: "Nouveau" });
        Assert.Contains(preview.Changes, c => c is { Key: "b.changed", Kind: "changed", CurrentValue: "Old EN", NewValue: "New FR" });
        Assert.DoesNotContain(preview.Changes, c => c.Key == "c.same");

        await Assert.ThrowsAsync<ValidationException>(
            () => _harness.PublishedTranslationsService.GetPublishPreviewAsync("nimbus", null));
    }

    public void Dispose() => _harness.Dispose();
}
