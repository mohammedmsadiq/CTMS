namespace CTMS.Api.IntegrationTests.Support;

/// <summary>
/// Base for the API test classes: gives each one its own <see cref="CtmsApiFactory"/> (a fresh
/// database on the shared server) and drops it on disposal. Concrete classes still carry
/// <c>[Collection(IntegrationCollection.Name)]</c> so xUnit injects the <see cref="MongoFixture"/>.
/// Override <see cref="InitializeAsync"/> for per-class setup and call <c>base.InitializeAsync()</c> first.
/// </summary>
public abstract class IntegrationTest(MongoFixture mongo) : IAsyncLifetime
{
    protected CtmsApiFactory Factory { get; private set; } = null!;

    public virtual Task InitializeAsync()
    {
        Factory = new CtmsApiFactory(mongo.ConnectionString);
        return Task.CompletedTask;
    }

    public virtual async Task DisposeAsync() => await Factory.DisposeAsync();
}
