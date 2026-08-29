using System.Net;
using System.Net.Http.Headers;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.Locales;
using CTMS.Application.Projects;

namespace CTMS.Api.IntegrationTests;

/// <summary>
/// <c>GET .../bundles/{localeCode}</c> conditional-request behaviour: a strong <c>ETag</c>,
/// <c>If-None-Match</c> ⇒ <c>304</c> with no body, a bogus validator ⇒ <c>200</c>, and a fresh
/// <c>ETag</c> once a new version with different content is published.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class BundleETagTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    private HttpClient _client = null!;
    private ProjectDto _project = null!;
    private LocaleDto _en = null!;
    private Guid _keyId;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _client = Factory.ClientAs(AuthRoles.Admin);
        _project = await _client.CreateProjectAsync(slug: ApiHelpers.UniqueName("etag"));
        _en = await _client.CreateLocaleAsync(_project.Id, "en", "English");
        var key = await _client.PublishStringAsync(_project.Id, _en.Id, "greeting", "hello");
        _keyId = key.Id;
        await _client.PublishBundleAsync(_project.Id, "en");
    }

    private Uri BundleUri => new($"/api/projects/{_project.Id}/bundles/en", UriKind.Relative);

    [Fact]
    public async Task Get_returns_a_strong_etag()
    {
        using var response = await _client.GetAsync(BundleUri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.False(response.Headers.ETag!.IsWeak);
        Assert.StartsWith("\"", response.Headers.ETag.Tag, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Matching_If_None_Match_yields_304_with_no_body()
    {
        var etag = (await _client.GetAsync(BundleUri)).Headers.ETag!;

        using var request = new HttpRequestMessage(HttpMethod.Get, BundleUri);
        request.Headers.IfNoneMatch.Add(etag);
        using var conditional = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, conditional.StatusCode);
        Assert.True(string.IsNullOrEmpty(await conditional.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Bogus_If_None_Match_yields_200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BundleUri);
        request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse("\"not-the-etag\""));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task New_version_with_changed_content_changes_the_etag_and_stale_validator_gets_200()
    {
        var firstEtag = (await _client.GetAsync(BundleUri)).Headers.ETag!;

        // Change the string's content, run it back through review to Published, cut v2.
        await _client.UpsertStringAsync(_project.Id, _keyId, _en.Id, "hello again");
        await _client.ReviewAsync(_project.Id, _keyId, _en.Id, "approve");
        await _client.ReviewAsync(_project.Id, _keyId, _en.Id, "publish");
        await _client.PublishBundleAsync(_project.Id, "en");

        using var response = await _client.GetAsync(BundleUri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(firstEtag.Tag, response.Headers.ETag!.Tag);

        using var staleRequest = new HttpRequestMessage(HttpMethod.Get, BundleUri);
        staleRequest.Headers.IfNoneMatch.Add(firstEtag);
        using var staleResponse = await _client.SendAsync(staleRequest);
        Assert.Equal(HttpStatusCode.OK, staleResponse.StatusCode);
    }
}
