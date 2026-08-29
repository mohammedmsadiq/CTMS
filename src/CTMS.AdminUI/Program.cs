using CTMS.AdminUI.Components;
using CTMS.AdminUI.Services;

var builder = WebApplication.CreateBuilder(args);

// Blazor Web App — InteractiveServer render mode only (internal admin tool).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Typed CTMS API client. Base address comes from configuration key "Ctms:ApiBaseUrl"
// (env override: Ctms__ApiBaseUrl); it defaults to the compose-network address.
var apiBaseUrl = builder.Configuration["Ctms:ApiBaseUrl"] ?? "http://localhost:8080";
builder.Services.AddHttpClient(CtmsApiClient.HttpClientName, client =>
{
    client.BaseAddress = new Uri(apiBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<CtmsApiClient>(sp =>
    new CtmsApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(CtmsApiClient.HttpClientName)));

// TODO: WS7 auth — register AddAuthentication()/AddAuthorization() here and expose an
// AuthenticationStateProvider; CurrentUser is a stub until then.
builder.Services.AddScoped<CurrentUser>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
