using System.Net;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;

namespace CTMS.Api.IntegrationTests;

/// <summary>
/// With <c>RateLimit:Enabled=true</c> the global limiter rejects the caller past the window
/// budget with <c>429</c> + <c>Retry-After</c>; the health probes are never limited.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RateLimitingTests(MongoFixture mongo) : IAsyncLifetime
{
    private const int Permit = 3;

    private CtmsApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new CtmsApiFactory(mongo.ConnectionString, new Dictionary<string, string?>
        {
            ["RateLimit:Enabled"] = "true",
            ["RateLimit:PermitPerWindow"] = Permit.ToString(),
            ["RateLimit:WindowSeconds"] = "60",
            ["RateLimit:QueueLimit"] = "0",
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Caller_is_throttled_with_429_and_Retry_After_after_the_window_budget()
    {
        using var client = _factory.ClientAs(AuthRoles.Admin);

        for (var i = 0; i < Permit; i++)
        {
            using var ok = await client.GetAsync("/api/applications");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var throttled = await client.GetAsync("/api/applications");

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.True(throttled.Headers.Contains("Retry-After"), "429 response must carry a Retry-After header.");
        Assert.Contains("problem+json", throttled.Content.Headers.ContentType?.MediaType ?? string.Empty);
    }

    [Fact]
    public async Task Health_endpoints_are_never_rate_limited()
    {
        using var client = _factory.AnonymousClient();

        for (var i = 0; i < Permit * 3; i++)
        {
            using var response = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }
}
