using CTMS.Api.Auth;
using CTMS.Api.Endpoints;
using CTMS.Api.Infrastructure;
using CTMS.Application;
using CTMS.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Human-readable console in Development, built-in JSON console elsewhere; trace id on every scope.
builder.AddCtmsLogging();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

// Entra ID JWT bearer + the CTMS authorization policies (see CTMS.Api/Auth). When
// Auth:Enabled=false (local dev / tests) a permissive all-roles bypass scheme is used instead.
builder.AddCtmsAuth();

// Cross-origin policy "ctms" (empty Cors:AllowedOrigins ⇒ no cross-origin access), a global
// partitioned rate limiter (off when RateLimit:Enabled=false), and a Redis-backed Data
// Protection key ring (local ephemeral fallback when ConnectionStrings:Redis is unset).
builder.Services.AddCtmsCors(builder.Configuration);
builder.Services.AddCtmsRateLimiting(builder.Configuration);
builder.AddCtmsDataProtection();

// Cap the request body for the whole host. The middleware (added below) enforces the same
// ceiling on hosting models that ignore this Kestrel limit, e.g. the integration test server.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = builder.Configuration.MaxRequestBodyBytes());

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

// Reject oversized bodies with 413 before anything reads them.
app.UseCtmsRequestBodySizeLimit(app.Configuration.MaxRequestBodyBytes());

// One structured line per request (method, path, status, elapsed); /health* excluded.
app.UseCtmsHttpLogging();

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

// CORS runs before auth so an unauthenticated preflight is answered correctly.
app.UseCors(CorsSetup.PolicyName);

// Auth sits between the guarded UseHttpsRedirection block and the health checks. Registration
// happened in builder.AddCtmsAuth(); the /api/* groups carry .RequireAuthorization("<policy>").
// /health, /health/ready and Swagger are outside any guarded group and stay anonymous.
app.UseAuthentication();
app.UseAuthorization();

// After auth so the limiter can partition by the authenticated user id (remote IP otherwise).
if (app.Configuration.RateLimitingEnabled())
{
    app.UseRateLimiter();
}

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
    .DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
}).DisableRateLimiting();

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
