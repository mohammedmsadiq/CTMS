using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class ReviewBulkTests : IDisposable
{
    private const string App = "acme-web";

    private readonly CtmsTestHarness _harness;
    private Guid _appId;

    public ReviewBulkTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);
        Seed.LanguageAsync(_harness, "en-GB").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "fr-FR", fallbackCode: "en-GB").GetAwaiter().GetResult();
        _appId = Seed.ApplicationAsync(_harness, App, "en-GB", ["en-GB", "fr-FR"]).GetAwaiter().GetResult().Id;
    }

    private TranslationStringService Service => _harness.TranslationStringService;

    [Fact]
    public async Task Requires_at_least_one_filter()
        => await Assert.ThrowsAsync<ValidationException>(
            () => Service.ReviewBulkAsync(App, new ReviewBulkRequest("approve"), "lead"));

    [Fact]
    public async Task Applies_the_action_where_legal_and_skips_the_rest()
    {
        var k1 = await Seed.KeyAsync(_harness, _appId, "a.one", "Course");
        var k2 = await Seed.KeyAsync(_harness, _appId, "b.two", "Course");
        var k3 = await Seed.KeyAsync(_harness, _appId, "c.three", "Course");
        await Seed.StringAsync(_harness, k1.Id, "fr-FR", "un", ReviewState.NeedsReview);   // approve -> legal
        await Seed.StringAsync(_harness, k2.Id, "fr-FR", "deux", ReviewState.NeedsReview); // approve -> legal
        await Seed.StringAsync(_harness, k3.Id, "fr-FR", "trois", ReviewState.Draft);      // approve -> illegal

        var result = await Service.ReviewBulkAsync(
            App, new ReviewBulkRequest("approve", Category: "Course"), "lead");

        Assert.Equal(2, result.Transitioned);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(ReviewState.Approved, (await _harness.Strings.GetAsync(k1.Id, "fr-FR"))!.ReviewState);
        Assert.Equal(ReviewState.Draft, (await _harness.Strings.GetAsync(k3.Id, "fr-FR"))!.ReviewState);
    }

    [Fact]
    public async Task Filters_by_language()
    {
        var key = await Seed.KeyAsync(_harness, _appId, "a.one", "Course");
        await Seed.StringAsync(_harness, key.Id, "en-GB", "one", ReviewState.NeedsReview);
        await Seed.StringAsync(_harness, key.Id, "fr-FR", "un", ReviewState.NeedsReview);

        var result = await Service.ReviewBulkAsync(
            App, new ReviewBulkRequest("approve", Language: "fr-FR"), "lead");

        Assert.Equal(1, result.Transitioned);
        Assert.Equal(ReviewState.NeedsReview, (await _harness.Strings.GetAsync(key.Id, "en-GB"))!.ReviewState);
        Assert.Equal(ReviewState.Approved, (await _harness.Strings.GetAsync(key.Id, "fr-FR"))!.ReviewState);
    }

    [Fact]
    public async Task Filters_by_key_ids()
    {
        var k1 = await Seed.KeyAsync(_harness, _appId, "a.one", "Course");
        var k2 = await Seed.KeyAsync(_harness, _appId, "b.two", "Course");
        await Seed.StringAsync(_harness, k1.Id, "fr-FR", "un", ReviewState.NeedsReview);
        await Seed.StringAsync(_harness, k2.Id, "fr-FR", "deux", ReviewState.NeedsReview);

        var result = await Service.ReviewBulkAsync(
            App, new ReviewBulkRequest("approve", KeyIds: [k1.Id]), "lead");

        Assert.Equal(1, result.Transitioned);
        Assert.Equal(ReviewState.Approved, (await _harness.Strings.GetAsync(k1.Id, "fr-FR"))!.ReviewState);
        Assert.Equal(ReviewState.NeedsReview, (await _harness.Strings.GetAsync(k2.Id, "fr-FR"))!.ReviewState);
    }

    [Fact]
    public async Task Publish_action_invalidates_the_delivery_cache()
    {
        var key = await Seed.KeyAsync(_harness, _appId, "course.start", "Course");
        await Seed.StringAsync(_harness, key.Id, "fr-FR", "Commencer", ReviewState.Approved);

        // Prime the fr-FR cache entry.
        await _harness.PublishedTranslationsService.GetPublishedAsync(App, "fr-FR");
        Assert.NotNull(await _harness.DistributedCache.GetAsync(
            CTMS.Infrastructure.Persistence.Caching.PublishedTranslationsCache.KeyFor(App, "fr-FR")));

        var result = await Service.ReviewBulkAsync(
            App, new ReviewBulkRequest("publish", Language: "fr-FR"), "lead");

        Assert.Equal(1, result.Transitioned);
        Assert.Null(await _harness.DistributedCache.GetAsync(
            CTMS.Infrastructure.Persistence.Caching.PublishedTranslationsCache.KeyFor(App, "fr-FR")));
    }

    [Fact]
    public async Task Unknown_action_is_a_validation_error()
        => await Assert.ThrowsAsync<ValidationException>(
            () => Service.ReviewBulkAsync(App, new ReviewBulkRequest("frobnicate", Language: "fr-FR"), "lead"));

    [Fact]
    public async Task Unknown_application_is_a_not_found()
        => await Assert.ThrowsAsync<NotFoundException>(
            () => Service.ReviewBulkAsync("nope", new ReviewBulkRequest("approve", Language: "fr-FR"), "lead"));

    public void Dispose() => _harness.Dispose();
}
