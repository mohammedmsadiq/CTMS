using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

/// <summary>
/// Covers the project-wide review-state string listing (<see cref="TranslationStringService.ListByProjectAsync"/>)
/// and the audit-history reads that back the history endpoints.
/// </summary>
[Collection("mongo")]
public sealed class ProjectScopedQueryTests : IDisposable
{
    private readonly CtmsTestHarness _harness;
    private readonly Guid _projectId;
    private readonly Guid _localeId;

    public ProjectScopedQueryTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);

        var project = new Project("Acme Web", "acme-web", "en");
        var locale = new Locale(project.Id, "fr", "French");
        _harness.Projects.AddAsync(project).GetAwaiter().GetResult();
        _harness.Locales.AddAsync(locale).GetAwaiter().GetResult();

        _projectId = project.Id;
        _localeId = locale.Id;
    }

    private async Task<Guid> AddKeyAsync(string name)
    {
        var key = new TranslationKey(_projectId, name);
        await _harness.Keys.AddAsync(key);
        return key.Id;
    }

    private async Task SeedStringAsync(Guid keyId, string value, ReviewState state)
    {
        await _harness.TranslationStringService.UpsertAsync(
            _projectId, keyId, _localeId, new UpsertTranslationStringRequest(value, UpdatedBy: "alice"));

        if (state is ReviewState.NeedsReview or ReviewState.Approved)
        {
            await _harness.TranslationStringService.ReviewAsync(_projectId, keyId, _localeId, "submit", "alice");
        }

        if (state is ReviewState.Approved)
        {
            await _harness.TranslationStringService.ReviewAsync(_projectId, keyId, _localeId, "approve", "lead");
        }
    }

    [Fact]
    public async Task ListByProjectAsync_filters_by_review_state()
    {
        await SeedStringAsync(await AddKeyAsync("a"), "va", ReviewState.NeedsReview);
        await SeedStringAsync(await AddKeyAsync("b"), "vb", ReviewState.NeedsReview);
        await SeedStringAsync(await AddKeyAsync("c"), "vc", ReviewState.Approved);
        await SeedStringAsync(await AddKeyAsync("d"), "vd", ReviewState.Draft);

        var page = await _harness.TranslationStringService.ListByProjectAsync(_projectId, "NeedsReview", 0, 50);

        Assert.NotNull(page);
        Assert.Equal(2, page!.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, s => Assert.Equal("NeedsReview", s.ReviewState));
        Assert.All(page.Items, s => Assert.Equal("fr", s.LocaleCode));
    }

    [Fact]
    public async Task ListByProjectAsync_without_a_filter_returns_all_states_paged_with_total()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedStringAsync(await AddKeyAsync($"k{i}"), $"v{i}", ReviewState.Draft);
        }

        var page = await _harness.TranslationStringService.ListByProjectAsync(_projectId, reviewState: null, skip: 0, take: 2);

        Assert.NotNull(page);
        Assert.Equal(5, page!.Total);
        Assert.Equal(2, page.Items.Count);

        var second = await _harness.TranslationStringService.ListByProjectAsync(_projectId, null, skip: 4, take: 2);
        Assert.Single(second!.Items);
    }

    [Fact]
    public async Task ListByProjectAsync_orders_newest_updated_first()
    {
        var first = await AddKeyAsync("first");
        var second = await AddKeyAsync("second");
        await SeedStringAsync(first, "1", ReviewState.Draft);
        await SeedStringAsync(second, "2", ReviewState.Draft);

        // Touch the first string again so it becomes the most recently updated.
        await _harness.TranslationStringService.UpsertAsync(
            _projectId, first, _localeId, new UpsertTranslationStringRequest("1b", UpdatedBy: "alice"));

        var page = await _harness.TranslationStringService.ListByProjectAsync(_projectId, null, 0, 50);

        Assert.Equal(first, page!.Items[0].TranslationKeyId);
    }

    [Fact]
    public async Task ListByProjectAsync_rejects_an_invalid_review_state()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _harness.TranslationStringService.ListByProjectAsync(_projectId, "Bogus", 0, 50));

        await Assert.ThrowsAsync<ValidationException>(
            () => _harness.TranslationStringService.ListByProjectAsync(_projectId, "1", 0, 50));
    }

    [Fact]
    public async Task ListByProjectAsync_returns_null_for_an_unknown_project()
    {
        Assert.Null(await _harness.TranslationStringService.ListByProjectAsync(Guid.NewGuid(), null, 0, 50));
    }

    [Fact]
    public async Task ListByProjectAsync_excludes_strings_from_other_projects()
    {
        await SeedStringAsync(await AddKeyAsync("mine"), "mine", ReviewState.Draft);

        var otherProject = new Project("Other", "other", "en");
        var otherLocale = new Locale(otherProject.Id, "fr", "French");
        var otherKey = new TranslationKey(otherProject.Id, "theirs");
        await _harness.Projects.AddAsync(otherProject);
        await _harness.Locales.AddAsync(otherLocale);
        await _harness.Keys.AddAsync(otherKey);
        await _harness.TranslationStringService.UpsertAsync(
            otherProject.Id, otherKey.Id, otherLocale.Id, new UpsertTranslationStringRequest("theirs", UpdatedBy: "bob"));

        var page = await _harness.TranslationStringService.ListByProjectAsync(_projectId, null, 0, 50);

        Assert.NotNull(page);
        Assert.Single(page!.Items);
        Assert.Equal("mine", page.Items[0].Value);
    }

    [Fact]
    public async Task ProjectHistory_pages_newest_first_with_a_correct_total()
    {
        var keyId = await AddKeyAsync("checkout.title");
        await SeedStringAsync(keyId, "v1", ReviewState.Approved); // Created + Submitted + Approved
        await _harness.TranslationStringService.ReviewAsync(_projectId, keyId, _localeId, "publish", "lead"); // Published

        var page = await _harness.AuditService.ListByProjectAsync(_projectId, skip: 0, take: 2);

        Assert.Equal(4, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("Published", page.Items[0].Action);
        Assert.Equal("Approved", page.Items[1].Action);
    }

    [Fact]
    public async Task PerStringHistory_returns_that_strings_entries_newest_first()
    {
        var keyId = await AddKeyAsync("checkout.title");
        await SeedStringAsync(keyId, "v1", ReviewState.NeedsReview); // Created + Submitted

        var created = await _harness.TranslationStringService.GetAsync(_projectId, keyId, _localeId);
        Assert.NotNull(created);

        var entries = await _harness.AuditService.ListByEntityAsync("TranslationString", created!.Id);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Submitted", entries[0].Action);
        Assert.Equal("Created", entries[1].Action);
    }

    public void Dispose() => _harness.Dispose();
}
