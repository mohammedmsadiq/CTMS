using CTMS.Api.IntegrationTests.Support;
using Xunit.Abstractions;

namespace CTMS.Api.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class BackendReportTests(MongoFixture mongo, ITestOutputHelper output)
{
    [Fact]
    public void A_mongo_backend_was_selected()
    {
        output.WriteLine($"Integration MongoDB backend: {mongo.Backend}");

        Assert.NotEqual("none", mongo.Backend);
        Assert.False(string.IsNullOrWhiteSpace(mongo.ConnectionString));
    }
}
