using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;

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
        Assert.Equal("acme-web", created.Application);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_blank_category()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => Service.CreateAsync("acme-web", new CreateTranslationKeyRequest("home.title", "  "), "alice"));
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

    public void Dispose() => _harness.Dispose();
}
