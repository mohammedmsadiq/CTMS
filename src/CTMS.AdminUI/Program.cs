using CTMS.AdminUI.Auth;
using CTMS.AdminUI.Components;
using CTMS.AdminUI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// Blazor Web App — InteractiveServer render mode only (internal admin tool).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- Authentication / authorization (WS7) -------------------------------------
// Auth:Enabled (default true) selects the mode. false => a permissive all-roles bypass so the
// UI runs locally without Entra ID; refused outright under Production.
var authEnabled = builder.Configuration.GetValue("Auth:Enabled", true);
if (!authEnabled && builder.Environment.IsProduction())
{
    throw new InvalidOperationException(
        "Auth:Enabled=false is not permitted when ASPNETCORE_ENVIRONMENT=Production.");
}

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization(AuthorizationPolicies.Configure);

if (authEnabled)
{
    var apiScope = builder.Configuration["Ctms:ApiScope"]
        ?? throw new InvalidOperationException("Configuration key 'Ctms:ApiScope' is required when Auth:Enabled is true.");

    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
        .EnableTokenAcquisitionToCallDownstreamApi([apiScope])
        .AddInMemoryTokenCaches();

    builder.Services.AddControllersWithViews()
        .AddMicrosoftIdentityUI();
}
else
{
    builder.Services.AddAuthentication(DevBypassAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevBypassAuthHandler>(DevBypassAuthHandler.SchemeName, _ => { });
}

// Typed CTMS API client. Base address comes from configuration key "Ctms:ApiBaseUrl"
// (env override: Ctms__ApiBaseUrl); it defaults to the compose-network address.
var apiBaseUrl = builder.Configuration["Ctms:ApiBaseUrl"] ?? "http://localhost:8080";
var apiClient = builder.Services.AddHttpClient(CtmsApiClient.HttpClientName, client =>
{
    client.BaseAddress = new Uri(apiBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(30);
});
if (authEnabled)
{
    // Acquires a bearer token on behalf of the signed-in user for every API call.
    builder.Services.AddScoped<CtmsApiTokenHandler>();
    apiClient.AddHttpMessageHandler<CtmsApiTokenHandler>();
}
builder.Services.AddScoped<CtmsApiClient>(sp =>
    new CtmsApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(CtmsApiClient.HttpClientName)));

builder.Services.AddScoped<CurrentUser>();

var app = builder.Build();

if (!authEnabled)
{
    app.Logger.LogWarning(
        "================ ADMIN UI AUTH IS DISABLED (Auth:Enabled=false) ================\n" +
        "Every visitor is treated as '{User}' with ALL roles and the API is called without a " +
        "bearer token. Local development only; refused under Production.",
        DevBypassAuthHandler.SyntheticUserName);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
if (authEnabled)
{
    // /MicrosoftIdentity/Account/SignIn|SignOut endpoints.
    app.MapControllers();
}
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
