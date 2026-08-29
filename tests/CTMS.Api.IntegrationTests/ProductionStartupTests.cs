using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CTMS.Api.IntegrationTests;

/// <summary>
/// Production posture: the permissive <c>Auth:Enabled=false</c> dev bypass must never come up
/// under <c>ASPNETCORE_ENVIRONMENT=Production</c> — the host fails fast at startup instead.
/// </summary>
public sealed class ProductionStartupTests
{
    [Fact]
    public void Auth_disabled_under_Production_fails_startup()
    {
        using var factory = new ProductionAuthOffFactory();

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains(
            "Auth:Enabled=false is not permitted",
            Flatten(exception!),
            StringComparison.Ordinal);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private sealed class ProductionAuthOffFactory : WebApplicationFactory<DevBypassAuthHandler>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Enabled", "false");
            builder.UseSetting("ConnectionStrings:CtmsDatabase", "mongodb://localhost:27017");
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("RateLimit:Enabled", "false");
        }
    }
}
