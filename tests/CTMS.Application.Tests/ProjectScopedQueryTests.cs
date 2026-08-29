using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

/// <summary>
/// Covers the application-wide review-state string listing
/// (<see cref="TranslationStringService.ListByProjectAsync"/>) and the audit-history reads that
/// back the history endpoints.
/// </summary>
[Collection("mongo")]
public sealed class ProjectScopedQueryTests : IDisposable
{
    private readonly CtmsTestHarness _harness;
    private Guid _projectId;

    public ProjectScopedQueryTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);

        Seed.LanguageAsync(_harness, "en-GB").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "fr-FR", fallbackCode: "en-GB").GetAwaiter().GetResult();
        _projectId = Seed.ApplicationAsync(_harness, "acme-web", "en-GB", ["fr-FR"]).GetAwaiter().GetResult().Id;
    }

    private async Task<Guid> AddKeyAsync(string name)
        => (await Seed.KeyAsync(_harness, _projectId, name, "Common")).Id;

    private async Task SeedStringAsync(Guid keyId, string value, ReviewState state)
    {
        await _harness.TranslationStringService.UpsertAsync(
            "acme-web", keyId, "fr-FR", new UpsertTranslationStringRequest(value, UpdatedBy: "alice"));

        if (state is ReviewState.NeedsReview or ReviewState.Approved)
        {
            await _harness.TranslationStringService.ReviewAsync("acme-web", keyId, "fr-FR", "submit", "alice");
        }

        if (state is ReviewState.Approved)
        {
            await _harness.TranslationStringService.ReviewAsync("acme-web", keyId, "fr-FR", "approve", "lead");
        }
    }

    [Fact]
    public async Task ListByProjectAsync_filters_by_review_state()
    {
        await SeedStringAsync(await AddKeyAsync("a"), "va", ReviewState.NeedsReview);
        await SeedStringAsync(await AddKeyAsync("b"), "vb", ReviewState.NeedsReview);
        await SeedStringAsync(await AddKeyAsync("c"), "vc", ReviewState.Approved);
        await SeedStringAsync(await AddKeyAsync("d"), "vd", ReviewState.Draft);

        var page = await _harness.TranslationStringService.ListByProjectAsync("acme-web", "NeedsReview", 0, 50);

        Assert.NotNull(page);
        Assert.Equal(2, page!.Total);
        Assert.All(page.Items, s => Assert.Equal("NeedsReview", s.Status));
        Assert.All(page.Items, s => Assert.Equal("fr-FR", s.LanguageCode));
    }

    [Fact]
    public async Task ListByProjectAsync_without_a_filter_returns_all_states_paged_with_total()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedStringAsync(await AddKeyAsync($"k{i}"), $"v{i}", ReviewState.Draft);
        }

        var page = await _harness.TranslationStringService.ListByProjectAsync("acme-web", reviewState: null, skip: 0, take: 2);

        Assert.Equal(5, page!.Total);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task ListByProjectAsync_rejects_an_invalid_review_state()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _harness.TranslationStringService.ListByProjectAsync("acme-web", "Bogus", 0, 50));
    }

    [Fact]
    public async Task ListByProjectAsync_returns_null_for_an_unknown_application()
        => Assert.Null(await _harness.TranslationStringService.ListByProjectAsync("nope", null, 0, 50));

    [Fact]
    public async Task ListByProjectAsync_excludes_strings_from_other_applications()
    {
        await SeedStringAsync(await AddKeyAsync("mine"), "mine", ReviewState.Draft);

        var other = await Seed.ApplicationAsync(_harness, "other", "en-GB", ["fr-FR"]);
        var otherKey = await Seed.KeyAsync(_harness, other.Id, "theirs", "Common");
        await _harness.TranslationStringService.UpsertAsync(
            "other", otherKey.Id, "fr-FR", new UpsertTranslationStringRequest("theirs", UpdatedBy: "bob"));

        var page = await _harness.TranslationStringService.ListByProjectAsync("acme-web", null, 0, 50);

        Assert.Single(page!.Items);
        Assert.Equal("mine", page.Items[0].Value);
    }

    [Fact]
    public async Task ApplicationHistory_pages_newest_first_with_a_correct_total()
    {
        var keyId = await AddKeyAsync("checkout.title");
        await SeedStringAsync(keyId, "v1", ReviewState.Approved); // Created + Submitted + Approved
        await _harness.TranslationStringService.ReviewAsync("acme-web", keyId, "fr-FR", "publish", "lead"); // Published

        var page = await _harness.AuditService.ListByApplicationAsync("acme-web", skip: 0, take: 2);

        Assert.NotNull(page);
        Assert.Equal(4, page!.Total);
        Assert.Equal("Published", page.Items[0].Action);
        Assert.Equal("Approved", page.Items[1].Action);
    }

    public void Dispose() => _harness.Dispose();
}
