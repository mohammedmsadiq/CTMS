using System.Net;
using CTMS.Api.IntegrationTests.Support;

namespace CTMS.Api.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class HealthEndpointsTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_are_200_for_anonymous_callers(string path)
    {
        using var client = Factory.AnonymousClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_reports_the_mongo_check_healthy()
    {
        using var client = Factory.AnonymousClient();

        using var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body);
    }
}
