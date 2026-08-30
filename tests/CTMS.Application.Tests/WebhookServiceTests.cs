using System.Security.Cryptography;
using System.Text;
using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Webhooks;

namespace CTMS.Application.Tests;

/// <summary>
/// <see cref="WebhookService"/> registration behaviour and the <see cref="WebhookSignature"/>
/// contract consumers verify against.
/// </summary>
[Collection("mongo")]
public sealed class WebhookServiceTests : IDisposable
{
    private readonly CtmsTestHarness _harness;

    public WebhookServiceTests(MongoFixture fixture)
        => _harness = new CtmsTestHarness(fixture.ConnectionString);

    private WebhookService Service => _harness.WebhookService;

    [Fact]
    public async Task Create_generates_a_secret_when_none_is_supplied_and_returns_it_once()
    {
        var created = await Service.CreateAsync(
            new CreateWebhookRequest("https://example.test/hook"), "admin");

        Assert.False(string.IsNullOrWhiteSpace(created.Secret));
        Assert.True(created.Active);
        Assert.Equal(["published"], created.Events);

        var listed = Assert.Single(await Service.ListAsync());
        Assert.Equal(created.Id, listed.Id);
        // The list DTO has no Secret member at all — nothing to leak.
        Assert.Equal("https://example.test/hook", listed.Url);
    }

    [Fact]
    public async Task Create_keeps_a_caller_supplied_secret()
    {
        var created = await Service.CreateAsync(
            new CreateWebhookRequest("https://example.test/hook", "my-shared-secret"), "admin");

        Assert.Equal("my-shared-secret", created.Secret);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.test/hook")]
    [InlineData("/relative/only")]
    public async Task Create_rejects_a_non_http_url(string url)
        => await Assert.ThrowsAsync<ValidationException>(
            () => Service.CreateAsync(new CreateWebhookRequest(url), "admin"));

    [Fact]
    public async Task Delete_reports_whether_a_row_was_removed()
    {
        var created = await Service.CreateAsync(
            new CreateWebhookRequest("https://example.test/hook"), "admin");

        Assert.True(await Service.DeleteAsync(created.Id));
        Assert.False(await Service.DeleteAsync(created.Id));
        Assert.Empty(await Service.ListAsync());
    }

    [Fact]
    public void Signature_is_sha256_prefixed_lowercase_hex_hmac_over_the_raw_body()
    {
        const string secret = "top-secret";
        const string body = """{"event":"published","application":"icoach","language":"fr-FR","etag":"abc","publishedAt":"2026-08-30T00:00:00.0000000Z"}""";

        var signature = WebhookSignature.Compute(secret, body);

        var expected = "sha256=" + Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
        Assert.Equal(expected, signature);
        Assert.StartsWith("sha256=", signature);
        Assert.NotEqual(signature, WebhookSignature.Compute("different-secret", body));
    }

    public void Dispose() => _harness.Dispose();
}
