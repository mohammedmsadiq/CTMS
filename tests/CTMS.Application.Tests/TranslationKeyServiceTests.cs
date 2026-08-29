using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Projects;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class TranslationKeyServiceTests : IDisposable
{
    private readonly CtmsTestHarness _harness;
    private readonly Guid _projectId;

    public TranslationKeyServiceTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);

        var project = new Project("Acme Web", "acme-web", "en");
        _harness.Projects.AddAsync(project).GetAwaiter().GetResult();
        _projectId = project.Id;
    }

    private TranslationKeyService Service => _harness.TranslationKeyService;

    [Fact]
    public async Task CreateAsync_persists_the_key()
    {
        var created = await Service.CreateAsync(
            _projectId,
            new CreateTranslationKeyRequest("checkout.button.submit", "Primary CTA"));

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("checkout.button.submit", created.KeyName);

        var persisted = Assert.Single(await _harness.Keys.ListByProjectAsync(_projectId, 0, 50));
        Assert.Equal("checkout.button.submit", persisted.KeyName);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_key_name()
    {
        await Service.CreateAsync(_projectId, new CreateTranslationKeyRequest("home.title"));

        await Assert.ThrowsAsync<ConflictException>(
            () => Service.CreateAsync(_projectId, new CreateTranslationKeyRequest("home.title")));

        Assert.Equal(1, await _harness.Keys.CountByProjectAsync(_projectId));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_invalid_character_set()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => Service.CreateAsync(_projectId, new CreateTranslationKeyRequest("home title!")));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_project()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service.CreateAsync(Guid.NewGuid(), new CreateTranslationKeyRequest("home.title")));
    }

    [Fact]
    public async Task ListAsync_pages_results_and_reports_the_total()
    {
        for (var i = 0; i < 5; i++)
        {
            await Service.CreateAsync(_projectId, new CreateTranslationKeyRequest($"key.{i:D2}"));
        }

        var page = await Service.ListAsync(_projectId, skip: 1, take: 2);

        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(["key.01", "key.02"], page.Items.Select(k => k.KeyName));
    }

    [Fact]
    public async Task ListAsync_clamps_take_and_normalises_a_negative_skip()
    {
        for (var i = 0; i < 3; i++)
        {
            await Service.CreateAsync(_projectId, new CreateTranslationKeyRequest($"key.{i:D2}"));
        }

        var page = await Service.ListAsync(_projectId, skip: -10, take: 10_000);

        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task UpdateAsync_persists_the_description()
    {
        var created = await Service.CreateAsync(_projectId, new CreateTranslationKeyRequest("home.title"));

        var updated = await Service.UpdateAsync(
            _projectId,
            created.Id,
            new UpdateTranslationKeyRequest("The landing headline"));

        Assert.Equal("The landing headline", updated!.Description);
        Assert.Equal("The landing headline", (await Service.GetAsync(_projectId, created.Id))!.Description);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_key()
    {
        var created = await Service.CreateAsync(_projectId, new CreateTranslationKeyRequest("home.title"));

        Assert.True(await Service.DeleteAsync(_projectId, created.Id));
        Assert.Equal(0, await _harness.Keys.CountByProjectAsync(_projectId));
    }

    public void Dispose() => _harness.Dispose();
}
