# Ctms.MauiSample

A .NET MAUI sample that consumes the [`CTMS.Client`](../../src/CTMS.Client) SDK:
it loads an assembled-on-demand translation set, resolves keys through the
language fallback chain, shows how fresh the data is, and lets the user force a
refresh.

> **There is no project here yet.** The MAUI workload
> (`dotnet workload install maui`) was not available in the environment that
> produced this repo, so the sample could not be built or committed. The
> runnable SDK demo is **[`samples/Ctms.ConsoleSample`](../Ctms.ConsoleSample)**
> (`dotnet run --project samples/Ctms.ConsoleSample`), which exercises prefetch,
> `304` revalidation, offline replay and fallback-chain resolution against an
> in-process fake API.
>
> This file is the scaffold to drop in once the workload is available. Nothing
> below is CTMS-specific beyond the `AddCtmsClient` call and the page code.

---

## 1. Create the project

```bash
dotnet workload install maui
dotnet new maui -n Ctms.MauiSample -o samples/Ctms.MauiSample
dotnet sln CTMS.sln add samples/Ctms.MauiSample/Ctms.MauiSample.csproj
dotnet add samples/Ctms.MauiSample reference src/CTMS.Client/CTMS.Client.csproj
```

`CTMS.Client` multi-targets `netstandard2.0;net10.0`; MAUI heads resolve the
`netstandard2.0` assembly, so `CtmsClient` is constructed directly (there is no
`AddCtmsClient` on `netstandard2.0` — but the MAUI host targets
`net10.0-android` / `net10.0-ios` / ... which **do** have it, so the DI form
below works).

## 2. `Ctms.MauiSample.csproj` (relevant bits)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net10.0-android;net10.0-ios;net10.0-maccatalyst</TargetFrameworks>
    <TargetFrameworks Condition="$([MSBuild]::IsOSPlatform('windows'))">$(TargetFrameworks);net10.0-windows10.0.19041.0</TargetFrameworks>
    <OutputType>Exe</OutputType>
    <UseMaui>true</UseMaui>
    <SingleProject>true</SingleProject>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CTMS.Client\CTMS.Client.csproj" />
  </ItemGroup>

</Project>
```

The repo-root `Directory.Build.props` sets a single
`<TargetFramework>net10.0</TargetFramework>`; clear it here (set
`<TargetFramework></TargetFramework>` before `<TargetFrameworks>`) exactly as
`CTMS.Client.csproj` does, or the MAUI TFMs will not apply.

## 3. `MauiProgram.cs`

```csharp
using CTMS.Client;
using CTMS.Client.Caching;
using Microsoft.Extensions.Logging;

namespace Ctms.MauiSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

        builder.Services.AddCtmsClient(options =>
        {
            options.BaseAddress     = new Uri("https://ctms.example.com");
            options.Application     = "icoach";   // the application code (Project slug)
            options.DefaultLanguage = "en-GB";

            // FileSystem.AppDataDirectory is the per-user, per-app writable
            // sandbox on every platform and survives app restarts.
            options.CacheDirectory  = Path.Combine(FileSystem.AppDataDirectory, "ctms-translations");

            // Mobile: revalidate sparingly; the offline-stale path covers gaps.
            options.StalenessTtl    = TimeSpan.FromHours(6);
            options.RequestTimeout  = TimeSpan.FromSeconds(10);
            options.DiagnosticsLogger = m => System.Diagnostics.Debug.WriteLine(m);

            // Only needed if the deployment sets Auth:PublicBundleReads=false:
            // options.AuthTokenProvider = async ct => (await AcquireTokenAsync(ct)).AccessToken;
        });

        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
```

`CacheDirectory` gives you a `FileTranslationStore` rooted there; pass
`options.TranslationStore` instead to supply your own `ITranslationStore`.

## 4. A page: language, retrieved-at, stale indicator, refresh

`MainPage.xaml`:

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="Ctms.MauiSample.MainPage">
  <VerticalStackLayout Padding="24" Spacing="12">
    <Label x:Name="Greeting" FontSize="24" />
    <Label x:Name="Meta" FontSize="12" TextColor="Gray" />
    <Label x:Name="StaleBanner" Text="⚠ showing an offline copy" TextColor="OrangeRed" IsVisible="False" />
    <Button x:Name="RefreshButton" Text="Refresh" />
  </VerticalStackLayout>
</ContentPage>
```

`MainPage.xaml.cs`:

```csharp
using System.Globalization;
using CTMS.Client;

namespace Ctms.MauiSample;

public partial class MainPage : ContentPage
{
    private readonly ICtmsClient _ctms;
    private string Language => CultureInfo.CurrentUICulture.Name; // e.g. "fr-CA"

    public MainPage(ICtmsClient ctms)
    {
        InitializeComponent();
        _ctms = ctms;
        RefreshButton.Clicked += async (_, _) => await LoadAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Warm the chain so Get(...) resolves offline from the first frame. The
        // server already fills gaps from each language's FallbackCode chain; the
        // client list is a secondary safety net across loaded languages.
        await _ctms.PrefetchAsync(new[] { Language, "en-GB" });
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            TranslationSet set = await _ctms.GetTranslationsAsync(Language);

            Greeting.Text         = _ctms.Get("home.greeting", Language, "en-GB");
            Meta.Text             = $"{set.Application}/{set.Language} · {set.Entries.Count} keys · " +
                                    $"retrieved {set.RetrievedAt.LocalDateTime:g}";
            StaleBanner.IsVisible = set.IsStale;
        }
        catch (CtmsOfflineException)
        {
            Meta.Text = "translations unavailable (offline, no cached copy)";
            Greeting.Text = "Hello"; // your own hard-coded default
        }
        catch (CtmsApiException ex)
        {
            Meta.Text = $"CTMS error {ex.StatusCode}: {ex.Title}";
        }
    }
}
```

### A language picker

`GetLanguagesAsync` / `GetApplicationsAsync` are thin passthroughs over the
anonymous catalogue endpoints:

```csharp
foreach (LanguageInfo lang in await _ctms.GetLanguagesAsync())
    LanguagePicker.Items.Add($"{lang.Name} ({lang.Code})");
```

### What to look for when running it

- First launch online: `Meta` shows the language and a fresh timestamp;
  `StaleBanner` hidden.
- Tap **Refresh** immediately: the SDK sends `If-None-Match` with the stored
  `ETag`, the API returns `304`, `RetrievedAt` is unchanged and the set is still
  `IsStale = false`.
- Kill the network and relaunch: the set loads from
  `FileSystem.AppDataDirectory`, `StaleBanner` appears (`IsStale = true`).
- Clear app data, stay offline, relaunch: `CtmsOfflineException` — the page
  falls back to its own defaults.
