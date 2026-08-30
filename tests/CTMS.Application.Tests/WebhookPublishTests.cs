using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Application.Webhooks;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

/// <summary>
/// The three publish paths (bulk publish, <c>review-bulk</c> with <c>action=publish</c>, and a
/// per-string <c>review</c> <c>publish</c>) each enqueue one webhook signal per affected
/// language; non-publish transitions enqueue nothing.
/// </summary>
[Collection("mongo")]
public sealed class WebhookPublishTests : IDisposable
{
    private const string App = "icoach";

    private readonly CtmsTestHarness _harness;
    private Guid _appId;

    public WebhookPublishTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);
        Seed.LanguageAsync(_harness, "en-GB").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "fr-FR", fallbackCode: "en-GB").GetAwaiter().GetResult();
        _appId = Seed.ApplicationAsync(_harness, App, "en-GB", ["en-GB", "fr-FR"]).GetAwaiter().GetResult().Id;
    }

    private RecordingWebhookPublisher Enqueued => _harness.WebhookPublisher;

    [Fact]
    public async Task Bulk_publish_enqueues_one_signal_per_affected_language()
    {
        var k1 = await Seed.KeyAsync(_harness, _appId, "a.one", "Course");
        var k2 = await Seed.KeyAsync(_harness, _appId, "b.two", "Course");
        await Seed.StringAsync(_harness, k1.Id, "en-GB", "one", ReviewState.Approved);
        await Seed.StringAsync(_harness, k2.Id, "fr-FR", "deux", ReviewState.Approved);

        await _harness.PublishedTranslationsService.BulkPublishAsync(
            new PublishTranslationsRequest(App), "release-bot");

        Assert.Equal(
            [(App, "en-GB"), (App, "fr-FR")],
            Enqueued.Enqueued.OrderBy(e => e.Language).ToArray());
    }

    [Fact]
    public async Task Bulk_publish_with_nothing_approved_enqueues_nothing()
    {
        await Seed.KeyAsync(_harness, _appId, "a.one", "Course");

        await _harness.PublishedTranslationsService.BulkPublishAsync(
            new PublishTranslationsRequest(App), "release-bot");

        Assert.Empty(Enqueued.Enqueued);
    }

    [Fact]
    public async Task Review_bulk_publish_enqueues_the_affected_language()
    {
        var key = await Seed.KeyAsync(_harness, _appId, "course.start", "Course");
        await Seed.StringAsync(_harness, key.Id, "fr-FR", "Commencer", ReviewState.Approved);

        await _harness.TranslationStringService.ReviewBulkAsync(
            App, new ReviewBulkRequest("publish", Language: "fr-FR"), "lead");

        Assert.Equal([(App, "fr-FR")], Enqueued.Enqueued.ToArray());
    }

    [Fact]
    public async Task Review_bulk_approve_enqueues_nothing()
    {
        var key = await Seed.KeyAsync(_harness, _appId, "course.start", "Course");
        await Seed.StringAsync(_harness, key.Id, "fr-FR", "Commencer", ReviewState.NeedsReview);

        await _harness.TranslationStringService.ReviewBulkAsync(
            App, new ReviewBulkRequest("approve", Language: "fr-FR"), "lead");

        Assert.Empty(Enqueued.Enqueued);
    }

    [Fact]
    public async Task Per_string_review_publish_enqueues_the_language()
    {
        var key = await Seed.KeyAsync(_harness, _appId, "course.start", "Course");
        await Seed.StringAsync(_harness, key.Id, "fr-FR", "Commencer", ReviewState.Approved);

        await _harness.TranslationStringService.ReviewAsync(App, key.Id, "fr-FR", "publish", "lead");

        Assert.Equal([(App, "fr-FR")], Enqueued.Enqueued.ToArray());
    }

    [Fact]
    public async Task Per_string_review_approve_enqueues_nothing()
    {
        var key = await Seed.KeyAsync(_harness, _appId, "course.start", "Course");
        await Seed.StringAsync(_harness, key.Id, "fr-FR", "Commencer", ReviewState.NeedsReview);

        await _harness.TranslationStringService.ReviewAsync(App, key.Id, "fr-FR", "approve", "lead");

        Assert.Empty(Enqueued.Enqueued);
    }

    [Fact]
    public void The_no_op_publisher_drops_every_signal()
    {
        var publisher = new NoOpWebhookPublisher();

        var exception = Record.Exception(() => publisher.Enqueue(App, ["en-GB", "fr-FR"]));

        Assert.Null(exception);
    }

    public void Dispose() => _harness.Dispose();
}
