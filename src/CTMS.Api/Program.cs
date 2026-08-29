using CTMS.Api.Endpoints;
using CTMS.Api.Infrastructure;
using CTMS.Application;
using CTMS.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// The MongoDB readiness check (name "database", tag "ready") is registered by AddInfrastructure.

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// UseHttpsRedirection logs "Failed to determine the https port for redirect" on every request
// when it can find no HTTPS port to redirect to. The container listens HTTP-only on :8080 (TLS
// terminates upstream), and a bare `dotnet run` without launchSettings has no https URL either.
// Only enable the middleware when an HTTPS port/URL is actually configured; real deployments
// that set one keep the redirect.
if (HttpsRedirectConfigured(builder.Configuration))
{
    app.UseHttpsRedirection();
}

// TODO: auth — add authentication/authorization here. Expected: JWT bearer.
// Register the scheme(s) above with builder.Services.AddAuthentication(...).AddJwtBearer(...)
// and builder.Services.AddAuthorization(), then call app.UseAuthentication() and
// app.UseAuthorization() at this point in the pipeline, and protect the /api/* endpoints.

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

app.MapProjectEndpoints();
app.MapLocaleEndpoints();
app.MapTranslationKeyEndpoints();
app.MapTranslationStringEndpoints();
app.MapReviewEndpoints();
app.MapBundleEndpoints();
app.MapAuditEndpoints();

app.Run();

// True when an HTTPS endpoint is configured for this host, via the `https_port` config key,
// the `HTTPS_PORTS` / `ASPNETCORE_HTTPS_PORTS` variables, the `ASPNETCORE_HTTPS_PORT` env var
// that UseHttpsRedirection itself reads, or an `https://` entry in the configured URLs.
static bool HttpsRedirectConfigured(IConfiguration config)
{
    if (!string.IsNullOrEmpty(config["https_port"])
        || !string.IsNullOrEmpty(config["HTTPS_PORTS"])
        || !string.IsNullOrEmpty(config["ASPNETCORE_HTTPS_PORTS"])
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORT")))
    {
        return true;
    }

    var urls = config["ASPNETCORE_URLS"] ?? config["urls"];
    return urls is not null && urls.Contains("https://", StringComparison.OrdinalIgnoreCase);
}
