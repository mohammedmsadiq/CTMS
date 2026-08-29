using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class TranslationBundleServiceTests : IDisposable
{
    private readonly CtmsTestHarness _harness;
    private readonly Guid _projectId;
    private readonly Guid _localeId;
    private readonly Dictionary<string, Guid> _keyIds = new(StringComparer.Ordinal);

    public TranslationBundleServiceTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);

        var project = new Project("Acme Web", "acme-web", "en");
        var locale = new Locale(project.Id, "fr", "French");
        _harness.Projects.AddAsync(project).GetAwaiter().GetResult();
        _harness.Locales.AddAsync(locale).GetAwaiter().GetResult();

        _projectId = project.Id;
        _localeId = locale.Id;

        foreach (var name in new[] { "home.title", "home.cta" })
        {
            var key = new TranslationKey(project.Id, name);
            _harness.Keys.AddAsync(key).GetAwaiter().GetResult();
            _keyIds[name] = key.Id;
        }
    }

    private async Task PublishStringAsync(string keyName, string value)
    {
        var keyId = _keyIds[keyName];
        await _harness.TranslationStringService.UpsertAsync(
            _projectId, keyId, _localeId, new UpsertTranslationStringRequest(value, UpdatedBy: "alice"));

        var current = await _harness.Strings.GetAsync(keyId, _localeId);
        if (current!.ReviewState == ReviewState.Draft)
        {
            await _harness.TranslationStringService.ReviewAsync(_projectId, keyId, _localeId, "submit", "alice");
        }

        await _harness.TranslationStringService.ReviewAsync(_projectId, keyId, _localeId, "approve", "lead");
        await _harness.TranslationStringService.ReviewAsync(_projectId, keyId, _localeId, "publish", "lead");
    }

    [Fact]
    public async Task PublishAsync_snapshots_published_strings_into_version_1_then_2()
    {
        await PublishStringAsync("home.title", "Bonjour");
        await PublishStringAsync("home.cta", "Commencer");

        var v1 = await _harness.TranslationBundleService.PublishAsync(_projectId, "fr", "release-bot");

        Assert.Equal(1, v1.Version);
        Assert.Equal("Bonjour", v1.Entries["home.title"]);
        Assert.Equal("Commencer", v1.Entries["home.cta"]);
        Assert.Equal("release-bot", v1.CreatedBy);
        Assert.NotEqual(default, v1.CreatedAt);

        var v2 = await _harness.TranslationBundleService.PublishAsync(_projectId, "fr", "release-bot");

        Assert.Equal(2, v2.Version);
        Assert.Equal(v1.ETag, v2.ETag); // identical content ⇒ identical ETag
    }

    [Fact]
    public async Task PublishAsync_does_not_change_string_review_state()
    {
        await PublishStringAsync("home.title", "Bonjour");

        await _harness.TranslationBundleService.PublishAsync(_projectId, "fr", "release-bot");

        var stored = await _harness.Strings.GetAsync(_keyIds["home.title"], _localeId);
        Assert.NotNull(stored);
        Assert.Equal(ReviewState.Published, stored!.ReviewState);
    }

    [Fact]
    public async Task PublishAsync_etag_changes_when_a_value_changes()
    {
        await PublishStringAsync("home.title", "Bonjour");
        var v1 = await _harness.TranslationBundleService.PublishAsync(_projectId, "fr", "bot");

        // Re-edit and re-publish the string, then snapshot again.
        await PublishStringAsync("home.title", "Salut");
        var v2 = await _harness.TranslationBundleService.PublishAsync(_projectId, "fr", "bot");

        Assert.NotEqual(v1.ETag, v2.ETag);
        Assert.Equal("Salut", v2.Entries["home.title"]);
    }

    [Fact]
    public async Task PublishAsync_rejects_an_empty_publish()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _harness.TranslationBundleService.PublishAsync(_projectId, "fr", "bot"));

        Assert.Null(await _harness.Bundles.GetLatestAsync(_projectId, "fr"));
    }

    [Fact]
    public async Task PublishAsync_rejects_an_unknown_project()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _harness.TranslationBundleService.PublishAsync(Guid.NewGuid(), "fr", "bot"));
    }

    [Fact]
    public async Task PublishAsync_rejects_a_locale_not_in_the_project()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _harness.TranslationBundleService.PublishAsync(_projectId, "de", "bot"));
    }

    [Fact]
    public async Task PublishAsync_writes_a_published_audit_entry()
    {
        await PublishStringAsync("home.title", "Bonjour");
        var bundle = await _harness.TranslationBundleService.PublishAsync(_projectId, "fr", "release-bot");

        var trail = await _harness.Audit.ListByEntityAsync("TranslationBundle", bundle.Id);

        var entry = Assert.Single(trail);
        Assert.Equal(Domain.Audit.AuditAction.Published, entry.Action);
        Assert.Equal("release-bot", entry.Actor);
        Assert.Equal(_projectId, entry.ProjectId);
        Assert.Equal("fr v1, 1 strings", entry.Detail);
    }

    [Fact]
    public async Task GetLatest_and_GetByVersion_and_ListVersions_read_bundles_back()
    {
        await PublishStringAsync("home.title", "Bonjour");
        await _harness.TranslationBundleService.PublishAsync(_projectId, "fr", "bot");
        await _harness.TranslationBundleService.PublishAsync(_projectId, "fr", "bot");

        var latest = await _harness.TranslationBundleService.GetLatestAsync(_projectId, "fr");
        Assert.NotNull(latest);
        Assert.Equal(2, latest!.Version);

        var v1 = await _harness.TranslationBundleService.GetByVersionAsync(_projectId, "fr", 1);
        Assert.NotNull(v1);
        Assert.Equal(1, v1!.Version);

        var versions = await _harness.TranslationBundleService.ListVersionsAsync(_projectId, "fr");
        Assert.NotNull(versions);
        Assert.Equal(new[] { 1, 2 }, versions!.Select(v => v.Version).ToArray());
        Assert.All(versions, v => Assert.Equal(1, v.EntryCount));
    }

    [Fact]
    public async Task Bundle_reads_return_null_for_unknown_targets()
    {
        Assert.Null(await _harness.TranslationBundleService.GetLatestAsync(_projectId, "fr"));            // locale exists, nothing published
        Assert.Null(await _harness.TranslationBundleService.GetLatestAsync(_projectId, "de"));            // unknown locale
        Assert.Null(await _harness.TranslationBundleService.GetLatestAsync(Guid.NewGuid(), "fr"));        // unknown project
        Assert.Null(await _harness.TranslationBundleService.GetByVersionAsync(_projectId, "fr", 99));
        Assert.Null(await _harness.TranslationBundleService.ListVersionsAsync(_projectId, "de"));
    }

    public void Dispose() => _harness.Dispose();
}
