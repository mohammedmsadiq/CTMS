using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class TranslationStringServiceTests : IDisposable
{
    private readonly CtmsTestHarness _harness;
    private readonly Guid _keyId;

    public TranslationStringServiceTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);

        Seed.LanguageAsync(_harness, "en-GB").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "fr-FR", fallbackCode: "en-GB").GetAwaiter().GetResult();
        var project = Seed.ApplicationAsync(_harness, "acme-web", "en-GB", ["en-GB", "fr-FR"]).GetAwaiter().GetResult();
        var key = Seed.KeyAsync(_harness, project.Id, "checkout.title").GetAwaiter().GetResult();
        _keyId = key.Id;
    }

    private TranslationStringService Service => _harness.TranslationStringService;

    [Fact]
    public async Task UpsertAsync_creates_a_draft_row_when_none_exists()
    {
        var result = await Service.UpsertAsync(
            "acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("Paiement", UpdatedBy: "alice"));

        Assert.True(result.Created);
        Assert.Equal("Draft", result.String.Status);
        Assert.Equal("fr-FR", result.String.LanguageCode);
        Assert.Equal("alice", result.String.UpdatedBy);
        Assert.NotNull(await _harness.Strings.GetAsync(_keyId, "fr-FR"));
    }

    [Fact]
    public async Task UpsertAsync_updates_the_existing_row_last_write_wins()
    {
        await Service.UpsertAsync("acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("v1"));

        var result = await Service.UpsertAsync(
            "acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("v2", UpdatedBy: "bob"));

        Assert.False(result.Created);
        Assert.Equal("v2", result.String.Value);

        var stored = Assert.Single(await _harness.Strings.ListByKeyAsync(_keyId));
        Assert.Equal("v2", stored.Value);
    }

    [Fact]
    public async Task UpsertAsync_with_an_unchanged_value_is_a_no_op()
    {
        await Service.UpsertAsync("acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("same"));
        await Service.ReviewAsync("acme-web", _keyId, "fr-FR", "submit", "alice");
        await Service.ReviewAsync("acme-web", _keyId, "fr-FR", "approve", "lead");

        var result = await Service.UpsertAsync("acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("same"));

        Assert.Equal("Approved", result.String.Status); // not knocked back to NeedsReview
    }

    [Fact]
    public async Task UpsertAsync_moves_an_approved_string_back_to_needs_review_when_edited()
    {
        await Service.UpsertAsync("acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("v1"));
        await Service.ReviewAsync("acme-web", _keyId, "fr-FR", "submit", "alice");
        await Service.ReviewAsync("acme-web", _keyId, "fr-FR", "approve", "lead");

        var result = await Service.UpsertAsync(
            "acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("v2", UpdatedBy: "alice"));

        Assert.Equal("NeedsReview", result.String.Status);
    }

    [Fact]
    public async Task UpsertAsync_leaves_a_draft_string_as_draft_when_edited()
    {
        await Service.UpsertAsync("acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("v1"));

        var result = await Service.UpsertAsync("acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("v2"));

        Assert.Equal("Draft", result.String.Status);
    }

    [Fact]
    public async Task UpsertAsync_rejects_a_language_not_enabled_for_the_application()
    {
        await Seed.LanguageAsync(_harness, "de-DE");

        await Assert.ThrowsAsync<NotFoundException>(
            () => Service.UpsertAsync("acme-web", _keyId, "de-DE", new UpsertTranslationStringRequest("v1")));
    }

    [Fact]
    public async Task UpsertAsync_rejects_an_unregistered_language()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service.UpsertAsync("acme-web", _keyId, "zz-ZZ", new UpsertTranslationStringRequest("v1")));
    }

    [Fact]
    public async Task ReviewAsync_publishes_an_approved_string()
    {
        await Service.UpsertAsync("acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("v1"));
        await Service.ReviewAsync("acme-web", _keyId, "fr-FR", "submit", "alice");
        await Service.ReviewAsync("acme-web", _keyId, "fr-FR", "approve", "lead");

        var published = await Service.ReviewAsync("acme-web", _keyId, "fr-FR", "publish", "release-bot");

        Assert.Equal("Published", published!.Status);
        Assert.Equal("release-bot", published.UpdatedBy);
    }

    [Fact]
    public async Task ReviewAsync_rejects_publishing_a_draft_string()
    {
        await Service.UpsertAsync("acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("v1"));

        await Assert.ThrowsAsync<InvalidReviewTransitionException>(
            () => Service.ReviewAsync("acme-web", _keyId, "fr-FR", "publish", "release-bot"));
    }

    [Fact]
    public async Task ReviewAsync_returns_null_when_no_string_exists_for_the_language()
    {
        Assert.Null(await Service.ReviewAsync("acme-web", _keyId, "fr-FR", "submit", "alice"));
    }

    public void Dispose() => _harness.Dispose();
}
