using CTMS.Api.Auth;
using CTMS.Api.Endpoints;
using CTMS.Api.Infrastructure;
using CTMS.Application;
using CTMS.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

// Entra ID JWT bearer + the CTMS authorization policies (see CTMS.Api/Auth). When
// Auth:Enabled=false (local dev / tests) a permissive all-roles bypass scheme is used instead.
builder.AddCtmsAuth();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Lets /swagger call secured endpoints: paste an Entra access token into "Authorize".
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Entra ID access token for the CTMS API. Enter the raw JWT — Swagger adds the \"Bearer \" prefix.",
    });
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", hostDocument: null)] = new List<string>(),
    });
});

// The MongoDB readiness check (name "database", tag "ready") is registered by AddInfrastructure.

var app = builder.Build();

app.WarnIfAuthDisabled();

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

// Auth sits between the guarded UseHttpsRedirection block and the health checks. Registration
// happened in builder.AddCtmsAuth(); the /api/* groups carry .RequireAuthorization("<policy>").
// /health, /health/ready and Swagger are outside any guarded group and stay anonymous.
app.UseAuthentication();
app.UseAuthorization();

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
