using System.Net;
using System.Net.Http.Headers;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.Projects;

namespace CTMS.Api.IntegrationTests;

/// <summary>
/// <c>GET /api/translations/{application}/{language}</c> conditional-request behaviour: a strong
/// <c>ETag</c>, <c>If-None-Match</c> ⇒ <c>304</c> with no body, a bogus validator ⇒ <c>200</c>,
/// and a fresh <c>ETag</c> once the published content changes.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class TranslationDeliveryETagTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    private HttpClient _client = null!;
    private ProjectDto _app = null!;
    private Guid _keyId;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _client = Factory.ClientAs(AuthRoles.Admin);
        await _client.CreateLanguageAsync("en-GB", "English");
        _app = await _client.CreateApplicationAsync(
            code: ApiHelpers.UniqueName("etag"), enabledLanguageCodes: ["en-GB"]);
        var key = await _client.PublishStringAsync(_app.Code, "en-GB", "greeting", "hello");
        _keyId = key.Id;
    }

    private Uri DeliveryUri => new($"/api/translations/{_app.Code}/en-GB", UriKind.Relative);

    [Fact]
    public async Task Get_returns_a_strong_etag_and_no_cache()
    {
        using var response = await _client.GetAsync(DeliveryUri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag!.IsWeak);
        Assert.StartsWith("\"", response.Headers.ETag.Tag, StringComparison.Ordinal);
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Matching_If_None_Match_yields_304_with_no_body()
    {
        var etag = (await _client.GetAsync(DeliveryUri)).Headers.ETag!;

        using var request = new HttpRequestMessage(HttpMethod.Get, DeliveryUri);
        request.Headers.IfNoneMatch.Add(etag);
        using var conditional = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, conditional.StatusCode);
        Assert.True(string.IsNullOrEmpty(await conditional.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Bogus_If_None_Match_yields_200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, DeliveryUri);
        request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse("\"not-the-etag\""));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Changed_published_content_changes_the_etag_and_a_stale_validator_gets_200()
    {
        var firstEtag = (await _client.GetAsync(DeliveryUri)).Headers.ETag!;

        await _client.UpsertStringAsync(_app.Code, _keyId, "en-GB", "hello again");
        await _client.ReviewAsync(_app.Code, _keyId, "en-GB", "approve");
        await _client.ReviewAsync(_app.Code, _keyId, "en-GB", "publish");
        await _client.BulkPublishAsync(_app.Code); // no-op, but also invalidates the cache

        using var response = await _client.GetAsync(DeliveryUri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(firstEtag.Tag, response.Headers.ETag!.Tag);

        using var staleRequest = new HttpRequestMessage(HttpMethod.Get, DeliveryUri);
        staleRequest.Headers.IfNoneMatch.Add(firstEtag);
        using var staleResponse = await _client.SendAsync(staleRequest);
        Assert.Equal(HttpStatusCode.OK, staleResponse.StatusCode);
    }
}
