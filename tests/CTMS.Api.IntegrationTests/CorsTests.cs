using System.Net;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;

namespace CTMS.Api.IntegrationTests;

/// <summary>
/// The "ctms" CORS policy: configured origins get an <c>Access-Control-Allow-Origin</c> on a
/// preflight; everyone else gets nothing (the browser then blocks the call).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CorsTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string AllowedOrigin = "https://sdk.example.com";

    private CtmsApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new CtmsApiFactory(mongo.ConnectionString, new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = AllowedOrigin,
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Preflight_from_a_configured_origin_is_allowed()
    {
        using var client = _factory.AnonymousClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/projects");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NoContent });
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Preflight_from_an_unconfigured_origin_gets_no_allow_origin_header()
    {
        using var client = _factory.AnonymousClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/projects");
        request.Headers.Add("Origin", "https://evil.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Actual_request_from_a_configured_origin_echoes_the_origin_and_exposes_ETag_and_Location()
    {
        using var client = _factory.ClientAs(AuthRoles.Admin);
        client.DefaultRequestHeaders.Add("Origin", AllowedOrigin);

        using var response = await client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));

        var exposed = response.Headers.TryGetValues("Access-Control-Expose-Headers", out var values)
            ? string.Join(",", values)
            : string.Empty;
        Assert.Contains("ETag", exposed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Location", exposed, StringComparison.OrdinalIgnoreCase);
    }
}
