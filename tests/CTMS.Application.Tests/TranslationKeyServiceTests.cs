using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence.Caching;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class TranslationKeyServiceTests : IDisposable
{
    private readonly CtmsTestHarness _harness;

    public TranslationKeyServiceTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);
        Seed.ApplicationAsync(_harness, "acme-web", "en-GB").GetAwaiter().GetResult();
    }

    private TranslationKeyService Service => _harness.TranslationKeyService;

    [Fact]
    public async Task CreateAsync_persists_the_key_with_category_and_creator()
    {
        var created = await Service.CreateAsync(
            "acme-web",
            new CreateTranslationKeyRequest("checkout.button.submit", "Navigation", "Primary CTA"),
            actor: "alice");

        Assert.Equal("checkout.button.submit", created.KeyName);
        Assert.Equal("Navigation", created.Category);
        Assert.Equal("alice", created.CreatedBy);
        Assert.True(created.Active);
        Assert.Equal("acme-web", created.Project);
    }

    [Fact]
    public async Task CreateAsync_derives_the_category_from_the_key_name_prefix_when_blank()
    {
        var fromPrefix = await Service.CreateAsync(
            "acme-web", new CreateTranslationKeyRequest("course.start", Category: "  "), "alice");
        Assert.Equal("Course", fromPrefix.Category);

        var noDot = await Service.CreateAsync(
            "acme-web", new CreateTranslationKeyRequest("standalone", Category: null), "alice");
        Assert.Equal("General", noDot.Category);
    }

    [Fact]
    public async Task UpdateAsync_still_rejects_an_explicitly_blank_category()
    {
        var created = await Service.CreateAsync(
            "acme-web", new CreateTranslationKeyRequest("home.title", "Common"), "alice");

        await Assert.ThrowsAsync<ValidationException>(
            () => Service.UpdateAsync("acme-web", created.Id, new UpdateTranslationKeyRequest(Category: "   ")));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_key_name()
    {
        await Service.CreateAsync("acme-web", new CreateTranslationKeyRequest("home.title", "Common"), "alice");

        await Assert.ThrowsAsync<ConflictException>(
            () => Service.CreateAsync("acme-web", new CreateTranslationKeyRequest("home.title", "Common"), "alice"));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_invalid_character_set()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => Service.CreateAsync("acme-web", new CreateTranslationKeyRequest("home title!", "Common"), "alice"));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_application()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service.CreateAsync("nope", new CreateTranslationKeyRequest("home.title", "Common"), "alice"));
    }

    [Fact]
    public async Task ListAsync_pages_results_filters_by_category_and_reports_the_total()
    {
        for (var i = 0; i < 4; i++)
        {
            await Service.CreateAsync("acme-web", new CreateTranslationKeyRequest($"nav.{i:D2}", "Navigation"), "a");
        }

        await Service.CreateAsync("acme-web", new CreateTranslationKeyRequest("common.save", "Common"), "a");

        var nav = await Service.ListAsync("acme-web", "Navigation", skip: 1, take: 2);
        Assert.NotNull(nav);
        Assert.Equal(4, nav!.Total);
        Assert.Equal(["nav.01", "nav.02"], nav.Items.Select(k => k.KeyName));

        var all = await Service.ListAsync("acme-web", category: null, skip: 0, take: 50);
        Assert.Equal(5, all!.Total);
    }

    [Fact]
    public async Task ListAsync_returns_null_for_an_unknown_application()
    {
        Assert.Null(await Service.ListAsync("nope", null, 0, 50));
    }

    [Fact]
    public async Task UpdateAsync_persists_category_description_and_active()
    {
        var created = await Service.CreateAsync(
            "acme-web", new CreateTranslationKeyRequest("home.title", "Common"), "a");

        var updated = await Service.UpdateAsync(
            "acme-web",
            created.Id,
            new UpdateTranslationKeyRequest(Category: "Content", Description: "The landing headline", Active: false));

        Assert.Equal("Content", updated!.Category);
        Assert.Equal("The landing headline", updated.Description);
        Assert.False(updated.Active);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_key()
    {
        var created = await Service.CreateAsync(
            "acme-web", new CreateTranslationKeyRequest("home.title", "Common"), "a");

        Assert.True(await Service.DeleteAsync("acme-web", created.Id));
        Assert.Null(await Service.GetAsync("acme-web", created.Id));
    }

    [Fact]
    public async Task DeleteAsync_returns_null_for_an_unknown_application()
    {
        Assert.Null(await Service.DeleteAsync("nope", Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAsync_invalidates_the_delivery_cache_and_audits_when_a_published_string_is_removed()
    {
        await Seed.LanguageAsync(_harness, "en-GB");
        var project = await Seed.ApplicationAsync(_harness, "shop", "en-GB", enabledLanguages: ["en-GB"]);
        var key = await Seed.KeyAsync(_harness, project.Id, "checkout.pay", "Checkout");
        await Seed.StringAsync(_harness, key.Id, "en-GB", "Pay now", ReviewState.Published);

        // Prime the delivery cache for shop/en-GB.
        await _harness.PublishedTranslationsService.GetPublishedAsync("shop", "en-GB");
        Assert.NotNull(await _harness.DistributedCache.GetAsync(
            PublishedTranslationsCache.KeyFor("shop", "en-GB")));

        Assert.True(await Service.DeleteAsync("shop", key.Id, actor: "carol"));

        // Cache entry dropped so the next fetch re-assembles without the deleted key.
        Assert.Null(await _harness.DistributedCache.GetAsync(
            PublishedTranslationsCache.KeyFor("shop", "en-GB")));

        var audit = await _harness.Audit.ListByEntityAsync("TranslationKey", key.Id);
        Assert.Contains(audit, e => e.Action == Domain.Audit.AuditAction.Deleted && e.Actor == "carol");
    }

    [Fact]
    public async Task UpdateAsync_deactivating_a_key_invalidates_the_delivery_cache()
    {
        await Seed.LanguageAsync(_harness, "en-GB");
        var project = await Seed.ApplicationAsync(_harness, "portal", "en-GB", enabledLanguages: ["en-GB"]);
        var key = await Seed.KeyAsync(_harness, project.Id, "nav.home", "Navigation");
        await Seed.StringAsync(_harness, key.Id, "en-GB", "Home", ReviewState.Published);

        await _harness.PublishedTranslationsService.GetPublishedAsync("portal", "en-GB");
        Assert.NotNull(await _harness.DistributedCache.GetAsync(
            PublishedTranslationsCache.KeyFor("portal", "en-GB")));

        await Service.UpdateAsync("portal", key.Id, new UpdateTranslationKeyRequest(Active: false), actor: "dave");

        Assert.Null(await _harness.DistributedCache.GetAsync(
            PublishedTranslationsCache.KeyFor("portal", "en-GB")));
    }

    public void Dispose() => _harness.Dispose();
}
