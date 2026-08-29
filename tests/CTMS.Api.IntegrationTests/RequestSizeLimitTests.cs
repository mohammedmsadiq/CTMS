using System.Net;
using System.Text;
using System.Text.Json;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;

namespace CTMS.Api.IntegrationTests;

/// <summary>
/// The host caps request bodies at <c>Limits:MaxRequestBodyBytes</c> (256&nbsp;KB by default).
/// An over-cap body is rejected with <c>413</c> before it is bound.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RequestSizeLimitTests(MongoFixture mongo) : IAsyncLifetime
{
    private const int CapBytes = 4096;

    private CtmsApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new CtmsApiFactory(mongo.ConnectionString, new Dictionary<string, string?>
        {
            ["Limits:MaxRequestBodyBytes"] = CapBytes.ToString(),
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Body_over_the_cap_is_rejected_with_413()
    {
        using var client = _factory.ClientAs(AuthRoles.Admin);

        var payload = JsonSerializer.Serialize(new
        {
            name = "Big",
            baseLanguageCode = "en",
            description = new string('x', CapBytes * 4),
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/applications", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Body_under_the_cap_is_accepted()
    {
        using var client = _factory.ClientAs(AuthRoles.Admin);

        var payload = JsonSerializer.Serialize(new
        {
            name = ApiHelpers.UniqueName("Small"),
            baseLanguageCode = "en",
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/applications", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
