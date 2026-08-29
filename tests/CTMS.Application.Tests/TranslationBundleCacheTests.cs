using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CTMS.Application.Tests;

/// <summary>
/// Read-through / invalidation behaviour of <see cref="IBundleCache"/> as wired into
/// <see cref="TranslationBundleService"/>. Uses a real <see cref="BundleCache"/> over an
/// in-memory <see cref="IDistributedCache"/> plus a call-counting repository decorator so a
/// cache hit can be shown to skip MongoDB.
/// </summary>
[Collection("mongo")]
public sealed class TranslationBundleCacheTests : IDisposable
{
    private readonly CtmsTestHarness _harness;
    private readonly CountingBundleRepository _repo;
    private readonly MemoryDistributedCache _distributed;
    private readonly TranslationBundleService _sut;

    private readonly Guid _projectId;
    private readonly Guid _localeId;
    private readonly Dictionary<string, Guid> _keyIds = new(StringComparer.Ordinal);

    public TranslationBundleCacheTests(MongoFixture fixture)
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

        _repo = new CountingBundleRepository(_harness.Bundles);
        _distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cache = new BundleCache(
            _distributed, Options.Create(new BundleCacheOptions()), NullLogger<BundleCache>.Instance);

        _sut = new TranslationBundleService(
            _repo,
            _harness.Strings,
            _harness.Keys,
            _harness.Locales,
            _harness.Projects,
            _harness.Audit,
            cache,
            _harness.UnitOfWork);
    }

    [Fact]
    public async Task GetLatestAsync_populates_the_cache_on_a_miss_then_serves_the_next_call_from_it()
    {
        await PublishStringAsync("home.title", "Bonjour");
        await _sut.PublishAsync(_projectId, "fr", "bot"); // v1

        var callsBefore = _repo.GetLatestCalls;

        var first = await _sut.GetLatestAsync(_projectId, "fr");
        Assert.Equal(1, first!.Version);
        Assert.Equal(callsBefore + 1, _repo.GetLatestCalls); // miss -> one repo read

        var second = await _sut.GetLatestAsync(_projectId, "fr");
        Assert.Equal(callsBefore + 1, _repo.GetLatestCalls); // hit -> no further repo read
        Assert.Equal(first.ETag, second!.ETag);

        // A differently-cased locale code hits the same normalised key without a locale lookup.
        var thirdCased = await _sut.GetLatestAsync(_projectId, "FR");
        Assert.Equal(callsBefore + 1, _repo.GetLatestCalls);
        Assert.Equal(first.ETag, thirdCased!.ETag);

        Assert.NotNull(await _distributed.GetAsync(BundleCache.KeyFor(_projectId, "fr")));
    }

    [Fact]
    public async Task PublishAsync_invalidates_so_the_next_GetLatestAsync_returns_the_new_version()
    {
        await PublishStringAsync("home.title", "Bonjour");
        await _sut.PublishAsync(_projectId, "fr", "bot"); // v1

        var v1 = await _sut.GetLatestAsync(_projectId, "fr"); // caches v1
        Assert.Equal(1, v1!.Version);

        await PublishStringAsync("home.title", "Salut");
        var v2Published = await _sut.PublishAsync(_projectId, "fr", "bot"); // v2 + invalidate
        Assert.Equal(2, v2Published.Version);

        var latest = await _sut.GetLatestAsync(_projectId, "fr");
        Assert.Equal(2, latest!.Version);
        Assert.Equal(v2Published.ETag, latest.ETag);
        Assert.NotEqual(v1.ETag, latest.ETag);
    }

    [Fact]
    public async Task Cached_dto_etag_equals_the_database_path_etag()
    {
        await PublishStringAsync("home.title", "Bonjour");
        await _sut.PublishAsync(_projectId, "fr", "bot");

        var fromDb = await _sut.GetLatestAsync(_projectId, "fr");   // repo path, primes cache
        var fromCache = await _sut.GetLatestAsync(_projectId, "fr"); // cache path
        var repoDirect = await _harness.Bundles.GetLatestAsync(_projectId, "fr");

        Assert.Equal(fromDb!.ETag, fromCache!.ETag);
        Assert.Equal(repoDirect!.ETag, fromCache.ETag);
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

    public void Dispose() => _harness.Dispose();

    private sealed class CountingBundleRepository : ITranslationBundleRepository
    {
        private readonly ITranslationBundleRepository _inner;

        public CountingBundleRepository(ITranslationBundleRepository inner) => _inner = inner;

        public int GetLatestCalls { get; private set; }

        public Task<TranslationBundle?> GetLatestAsync(
            Guid projectId, string localeCode, CancellationToken cancellationToken = default)
        {
            GetLatestCalls++;
            return _inner.GetLatestAsync(projectId, localeCode, cancellationToken);
        }

        public Task<TranslationBundle?> GetByVersionAsync(
            Guid projectId, string localeCode, int version, CancellationToken cancellationToken = default)
            => _inner.GetByVersionAsync(projectId, localeCode, version, cancellationToken);

        public Task<IReadOnlyList<TranslationBundle>> ListByProjectAndLocaleAsync(
            Guid projectId, string localeCode, CancellationToken cancellationToken = default)
            => _inner.ListByProjectAndLocaleAsync(projectId, localeCode, cancellationToken);

        public Task InsertAsync(TranslationBundle bundle, CancellationToken cancellationToken = default)
            => _inner.InsertAsync(bundle, cancellationToken);
    }
}
