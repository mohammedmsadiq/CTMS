using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class TranslationStringServiceTests : IDisposable
{
    private readonly CtmsTestHarness _harness;
    private readonly Guid _projectId;
    private readonly Guid _keyId;
    private readonly Guid _localeId;

    public TranslationStringServiceTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);

        var project = new Project("Acme Web", "acme-web", "en");
        var key = new TranslationKey(project.Id, "checkout.title");
        var locale = new Locale(project.Id, "fr", "French");
        _harness.Projects.AddAsync(project).GetAwaiter().GetResult();
        _harness.Keys.AddAsync(key).GetAwaiter().GetResult();
        _harness.Locales.AddAsync(locale).GetAwaiter().GetResult();

        _projectId = project.Id;
        _keyId = key.Id;
        _localeId = locale.Id;
    }

    private TranslationStringService Service => _harness.TranslationStringService;

    [Fact]
    public async Task UpsertAsync_creates_a_draft_row_when_none_exists()
    {
        var result = await Service.UpsertAsync(
            _projectId,
            _keyId,
            _localeId,
            new UpsertTranslationStringRequest("Paiement", UpdatedBy: "alice"));

        Assert.True(result.Created);
        Assert.Equal("Draft", result.String.ReviewState);
        Assert.Equal("fr", result.String.LocaleCode);
        Assert.Equal("alice", result.String.UpdatedBy);
        Assert.Equal(0, result.String.Version);
        Assert.NotNull(await _harness.Strings.GetAsync(_keyId, _localeId));
    }

    [Fact]
    public async Task UpsertAsync_updates_the_existing_row_and_bumps_the_version()
    {
        await Service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));

        var result = await Service.UpsertAsync(
            _projectId,
            _keyId,
            _localeId,
            new UpsertTranslationStringRequest("v2", UpdatedBy: "bob"));

        Assert.False(result.Created);
        Assert.Equal("v2", result.String.Value);
        Assert.Equal(1, result.String.Version);

        var stored = Assert.Single(await _harness.Strings.ListByKeyAsync(_keyId));
        Assert.Equal("v2", stored.Value);
    }

    [Fact]
    public async Task UpsertAsync_moves_an_approved_string_back_to_needs_review_when_edited()
    {
        await Service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));
        await Service.ReviewAsync(_projectId, _keyId, _localeId, "submit", "alice");
        await Service.ReviewAsync(_projectId, _keyId, _localeId, "approve", "lead");

        var result = await Service.UpsertAsync(
            _projectId,
            _keyId,
            _localeId,
            new UpsertTranslationStringRequest("v2", UpdatedBy: "alice"));

        Assert.Equal("NeedsReview", result.String.ReviewState);
    }

    [Fact]
    public async Task UpsertAsync_leaves_a_draft_string_as_draft_when_edited()
    {
        await Service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));

        var result = await Service.UpsertAsync(
            _projectId,
            _keyId,
            _localeId,
            new UpsertTranslationStringRequest("v2"));

        Assert.Equal("Draft", result.String.ReviewState);
    }

    [Fact]
    public async Task UpsertAsync_rejects_a_stale_expected_version()
    {
        await Service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));

        var exception = await Assert.ThrowsAsync<ConcurrencyException>(
            () => Service.UpsertAsync(
                _projectId,
                _keyId,
                _localeId,
                new UpsertTranslationStringRequest("v2", ExpectedVersion: 999L)));

        Assert.Equal(0L, exception.CurrentVersion);
        var persisted = Assert.Single(await _harness.Strings.ListByKeyAsync(_keyId));
        Assert.Equal("v1", persisted.Value);
    }

    [Fact]
    public async Task Repository_update_detects_a_concurrent_change_via_the_version_guard()
    {
        await Service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));

        var stale = await _harness.Strings.GetAsync(_keyId, _localeId);
        Assert.NotNull(stale);

        // A different caller updates the row first, advancing the stored version to 1.
        await Service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v2"));

        stale!.Edit("v3", "carol");
        var exception = await Assert.ThrowsAsync<ConcurrencyException>(() => _harness.Strings.UpdateAsync(stale));
        Assert.Equal(1L, exception.CurrentVersion);
    }

    [Fact]
    public async Task ReviewAsync_publishes_an_approved_string()
    {
        await Service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));
        await Service.ReviewAsync(_projectId, _keyId, _localeId, "submit", "alice");
        await Service.ReviewAsync(_projectId, _keyId, _localeId, "approve", "lead");

        var published = await Service.ReviewAsync(_projectId, _keyId, _localeId, "publish", "release-bot");

        Assert.NotNull(published);
        Assert.Equal("Published", published!.ReviewState);
        Assert.Equal("release-bot", published.UpdatedBy);
    }

    [Fact]
    public async Task ReviewAsync_rejects_publishing_a_draft_string()
    {
        await Service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));

        await Assert.ThrowsAsync<InvalidReviewTransitionException>(
            () => Service.ReviewAsync(_projectId, _keyId, _localeId, "publish", "release-bot"));
    }

    [Fact]
    public async Task ReviewAsync_reopens_a_published_string_for_more_review()
    {
        await Service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));
        await Service.ReviewAsync(_projectId, _keyId, _localeId, "submit", "alice");
        await Service.ReviewAsync(_projectId, _keyId, _localeId, "approve", "lead");
        await Service.ReviewAsync(_projectId, _keyId, _localeId, "publish", "release-bot");

        var reopened = await Service.ReviewAsync(_projectId, _keyId, _localeId, "reopen", "alice");

        Assert.Equal("NeedsReview", reopened!.ReviewState);
    }

    [Fact]
    public async Task UpsertAsync_moves_a_published_string_back_to_needs_review_when_edited()
    {
        await Service.UpsertAsync(_projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1"));
        await Service.ReviewAsync(_projectId, _keyId, _localeId, "submit", "alice");
        await Service.ReviewAsync(_projectId, _keyId, _localeId, "approve", "lead");
        await Service.ReviewAsync(_projectId, _keyId, _localeId, "publish", "release-bot");

        var result = await Service.UpsertAsync(
            _projectId,
            _keyId,
            _localeId,
            new UpsertTranslationStringRequest("v2", UpdatedBy: "alice"));

        Assert.Equal("NeedsReview", result.String.ReviewState);
    }

    [Fact]
    public async Task UpsertAsync_rejects_a_locale_from_another_project()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service.UpsertAsync(_projectId, _keyId, Guid.NewGuid(), new UpsertTranslationStringRequest("v1")));
    }

    [Fact]
    public async Task ReviewAsync_returns_null_when_no_string_exists_for_the_locale()
    {
        Assert.Null(await Service.ReviewAsync(_projectId, _keyId, _localeId, "submit", "alice"));
    }

    public void Dispose() => _harness.Dispose();
}
