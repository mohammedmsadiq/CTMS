using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CTMS.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> construct the context at design time without booting the API host.
/// The connection string only selects the provider; migration commands never open it.
/// </summary>
public sealed class CtmsDbContextFactory : IDesignTimeDbContextFactory<CtmsDbContext>
{
    public CtmsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__CtmsDatabase")
            ?? "Host=localhost;Port=5432;Database=ctms;Username=ctms;Password=ctms";

        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CtmsDbContext(options);
    }
}
