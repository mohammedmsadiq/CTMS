using EphemeralMongo;

namespace CTMS.Application.Tests.Infrastructure;

/// <summary>
/// Boots a single throwaway <c>mongod</c> for the whole test run. Test classes share it via
/// the <c>[Collection("mongo")]</c> attribute and each takes an isolated database from
/// <see cref="CtmsTestHarness"/>.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    private IMongoRunner? _runner;

    public string ConnectionString => _runner?.ConnectionString
        ?? throw new InvalidOperationException("The MongoDB runner has not been started.");

    public async Task InitializeAsync()
        => _runner = await MongoRunner.RunAsync();

    public Task DisposeAsync()
    {
        _runner?.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>;
