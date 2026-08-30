using System.Text.RegularExpressions;
using CTMS.Application.ApiKeys;
using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;

namespace CTMS.Application.Tests;

/// <summary>
/// <see cref="ApiKeyService"/> minting: the raw key format, that only a hash + prefix are
/// persisted, and that list / delete never expose secret material.
/// </summary>
[Collection("mongo")]
public sealed partial class ApiKeyServiceTests : IDisposable
{
    private readonly CtmsTestHarness _harness;

    public ApiKeyServiceTests(MongoFixture fixture)
        => _harness = new CtmsTestHarness(fixture.ConnectionString);

    private ApiKeyService Service => _harness.ApiKeyService;

    [Fact]
    public async Task Create_mints_a_ctms_prefixed_key_and_stores_only_its_hash()
    {
        var created = await Service.CreateAsync(new CreateApiKeyRequest("ci-bot"), "admin");

        Assert.Matches(RawKeyPattern(), created.Key);
        Assert.Equal(created.Key[..8], created.Prefix);
        Assert.Equal("ci-bot", created.Name);
        Assert.Equal("admin", created.CreatedBy);
        Assert.True(created.Active);

        var stored = await _harness.ApiKeys.GetAsync(created.Id);
        Assert.NotNull(stored);
        Assert.Equal(ApiKeySecret.Hash(created.Key), stored!.Hash);
        Assert.NotEqual(created.Key, stored.Hash);
        Assert.Null(stored.LastUsedAt);

        // The stored hash is what authentication looks up.
        var found = await _harness.ApiKeys.FindByHashAsync(ApiKeySecret.Hash(created.Key));
        Assert.Equal(created.Id, found!.Id);
    }

    [Fact]
    public async Task Two_minted_keys_differ()
    {
        var a = await Service.CreateAsync(new CreateApiKeyRequest("a"), "admin");
        var b = await Service.CreateAsync(new CreateApiKeyRequest("b"), "admin");

        Assert.NotEqual(a.Key, b.Key);
    }

    [Fact]
    public async Task Create_rejects_a_blank_name()
        => await Assert.ThrowsAsync<ValidationException>(
            () => Service.CreateAsync(new CreateApiKeyRequest("  "), "admin"));

    [Fact]
    public async Task List_returns_no_secret_material()
    {
        var created = await Service.CreateAsync(new CreateApiKeyRequest("ci-bot"), "admin");

        var listed = Assert.Single(await Service.ListAsync());

        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(created.Prefix, listed.Prefix);
        Assert.Equal("ci-bot", listed.Name);
        // ApiKeyDto has neither a Hash nor a Key member — the type itself guarantees no leak.
        Assert.Equal(8, listed.Prefix.Length);
    }

    [Fact]
    public async Task Delete_reports_whether_a_row_was_removed()
    {
        var created = await Service.CreateAsync(new CreateApiKeyRequest("ci-bot"), "admin");

        Assert.True(await Service.DeleteAsync(created.Id));
        Assert.False(await Service.DeleteAsync(created.Id));
        Assert.Empty(await Service.ListAsync());
    }

    public void Dispose() => _harness.Dispose();

    [GeneratedRegex(@"^ctms_[A-Za-z0-9_-]{40}$")]
    private static partial Regex RawKeyPattern();
}
