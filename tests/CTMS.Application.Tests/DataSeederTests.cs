using CTMS.Application.Tests.Infrastructure;
using CTMS.Infrastructure.Persistence.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CTMS.Application.Tests;

/// <summary>
/// The dev data seeder must only ever run in the Development environment, regardless of
/// <c>Seed:Enabled</c>. Guards the production posture: a Production host never gets sample data.
/// </summary>
[Collection("mongo")]
public sealed class DataSeederTests : IDisposable
{
    private readonly CtmsTestHarness _harness;

    public DataSeederTests(MongoFixture fixture) => _harness = new CtmsTestHarness(fixture.ConnectionString);

    private static IConfiguration SeedEnabledConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:Enabled"] = "true" })
        .Build();

    private DataSeeder Seeder(string environmentName) => new(
        _harness.Context,
        SeedEnabledConfig(),
        new StubHostEnvironment(environmentName),
        NullLogger<DataSeeder>.Instance);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task StartAsync_does_nothing_outside_Development_even_with_Seed_Enabled(string environmentName)
    {
        await Seeder(environmentName).StartAsync(CancellationToken.None);

        Assert.Empty(await _harness.Projects.ListAsync());
    }

    [Fact]
    public async Task StartAsync_seeds_the_sample_project_in_Development_when_Seed_Enabled()
    {
        await Seeder(Environments.Development).StartAsync(CancellationToken.None);

        var projects = await _harness.Projects.ListAsync();
        Assert.Contains(projects, p => p.Slug == "marketing-site");
    }

    public void Dispose() => _harness.Dispose();

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "CTMS.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
