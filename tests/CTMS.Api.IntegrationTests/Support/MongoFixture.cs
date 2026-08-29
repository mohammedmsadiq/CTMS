using EphemeralMongo;
using Testcontainers.MongoDb;

namespace CTMS.Api.IntegrationTests.Support;

/// <summary>
/// Starts one MongoDB for the whole assembly. Prefers a real <c>mongo:7</c> via
/// <c>Testcontainers.MongoDb</c> when a Docker daemon is reachable; otherwise falls back to
/// <c>EphemeralMongo</c> (an in-process <c>mongod</c>, the same package the unit tests use).
/// If neither can start it throws with both failure reasons rather than skipping silently.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    private MongoDbContainer? _container;
    private IMongoRunner? _runner;

    /// <summary>Connection string every per-test <see cref="CtmsApiFactory"/> points at.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Which backend actually started — surfaced by <see cref="BackendReportTests"/>.</summary>
    public string Backend { get; private set; } = "none";

    public async Task InitializeAsync()
    {
        string? testcontainersError = null;

        try
        {
            var container = new MongoDbBuilder()
                .WithImage("mongo:7")
                .Build();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await container.StartAsync(cts.Token);

            _container = container;
            ConnectionString = container.GetConnectionString();
            Backend = "Testcontainers.MongoDb (mongo:7)";
            return;
        }
        catch (Exception ex)
        {
            testcontainersError = ex.Message;
            if (_container is not null)
            {
                try
                {
                    await _container.DisposeAsync();
                }
                catch
                {
                    // best-effort teardown of a container that never came up
                }

                _container = null;
            }
        }

        try
        {
            _runner = await MongoRunner.RunAsync();
            ConnectionString = _runner.ConnectionString;
            Backend = "EphemeralMongo (in-process mongod; no Docker daemon)";
        }
        catch (Exception ephemeralError)
        {
            throw new InvalidOperationException(
                "The API integration suite requires a MongoDB and neither backend could start.\n" +
                $"  Testcontainers.MongoDb: {testcontainersError}\n" +
                $"  EphemeralMongo:         {ephemeralError.Message}",
                ephemeralError);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        _runner?.Dispose();
    }
}

/// <summary>Assembly-wide collection so the MongoDB backend is started exactly once.</summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<MongoFixture>
{
    public const string Name = "ctms-api-integration";
}
