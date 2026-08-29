using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class TranslationBundleRepositoryTests : IDisposable
{
    private readonly CtmsTestHarness _harness;
    private readonly Guid _projectId = Guid.NewGuid();

    public TranslationBundleRepositoryTests(MongoFixture fixture)
        => _harness = new CtmsTestHarness(fixture.ConnectionString);

    private static Dictionary<string, string> Entries(params (string Key, string Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public async Task InsertAsync_then_get_by_version_round_trips_the_snapshot()
    {
        var bundle = new TranslationBundle(
            _projectId,
            "fr",
            1,
            Entries(("home.title", "Bonjour"), ("home.cta", "Commencer")),
            "release-bot");

        await _harness.Bundles.InsertAsync(bundle);

        var loaded = await _harness.Bundles.GetByVersionAsync(_projectId, "fr", 1);

        Assert.NotNull(loaded);
        Assert.Equal(bundle.Id, loaded!.Id);
        Assert.Equal("release-bot", loaded.CreatedBy);
        Assert.Equal(bundle.ETag, loaded.ETag);
        Assert.Equal("Bonjour", loaded.Entries["home.title"]);
        Assert.Equal("Commencer", loaded.Entries["home.cta"]);
        Assert.NotEqual(default, loaded.CreatedAt);
    }

    [Fact]
    public async Task GetLatestAsync_returns_the_highest_version()
    {
        await _harness.Bundles.InsertAsync(new TranslationBundle(_projectId, "fr", 1, Entries(("k", "v1")), "bot"));
        await _harness.Bundles.InsertAsync(new TranslationBundle(_projectId, "fr", 2, Entries(("k", "v2")), "bot"));
        await _harness.Bundles.InsertAsync(new TranslationBundle(_projectId, "de", 1, Entries(("k", "d1")), "bot"));

        var latest = await _harness.Bundles.GetLatestAsync(_projectId, "fr");

        Assert.NotNull(latest);
        Assert.Equal(2, latest!.Version);
        Assert.Equal("v2", latest.Entries["k"]);
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_when_nothing_is_published()
    {
        Assert.Null(await _harness.Bundles.GetLatestAsync(_projectId, "fr"));
    }

    [Fact]
    public async Task InsertAsync_rejects_a_duplicate_identity_via_the_unique_index()
    {
        await _harness.Bundles.InsertAsync(new TranslationBundle(_projectId, "fr", 1, Entries(("k", "v")), "bot"));

        await Assert.ThrowsAsync<ConflictException>(
            () => _harness.Bundles.InsertAsync(new TranslationBundle(_projectId, "fr", 1, Entries(("k", "other")), "bot")));
    }

    [Fact]
    public void ComputeETag_is_stable_regardless_of_entry_order()
    {
        var a = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1", ["c"] = "3" };
        var b = new Dictionary<string, string> { ["c"] = "3", ["a"] = "1", ["b"] = "2" };

        Assert.Equal(TranslationBundle.ComputeETag(a), TranslationBundle.ComputeETag(b));
        Assert.NotEqual(
            TranslationBundle.ComputeETag(a),
            TranslationBundle.ComputeETag(new Dictionary<string, string> { ["a"] = "1", ["b"] = "changed", ["c"] = "3" }));
    }

    public void Dispose() => _harness.Dispose();
}
