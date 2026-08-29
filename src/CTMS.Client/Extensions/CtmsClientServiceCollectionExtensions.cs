using System;
using System.Net.Http.Headers;
using CTMS.Client.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CTMS.Client;

/// <summary>
/// DI wiring for the CTMS client SDK. net10.0 only (the SDK also targets netstandard2.0, where
/// callers construct <see cref="CtmsClient"/> directly).
/// </summary>
public static class CtmsClientServiceCollectionExtensions
{
    /// <summary>Named <see cref="System.Net.Http.HttpClient"/> registered for the SDK.</summary>
    public const string HttpClientName = "CTMS.Client";

    /// <summary>
    /// Registers <see cref="ICtmsClient"/> (singleton) backed by an <see cref="IHttpClientFactory"/>
    /// client and an <see cref="IBundleStore"/> chosen from
    /// <see cref="CtmsClientOptions.CacheDirectory"/>/<see cref="CtmsClientOptions.BundleStore"/>.
    /// </summary>
    public static IServiceCollection AddCtmsClient(this IServiceCollection services, Action<CtmsClientOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = new CtmsClientOptions();
        configure(options);

        services.TryAddSingleton(options);

        services.AddHttpClient(HttpClientName, http =>
        {
            if (options.BaseAddress is not null)
            {
                var text = options.BaseAddress.AbsoluteUri;
                http.BaseAddress = text.EndsWith("/", StringComparison.Ordinal)
                    ? options.BaseAddress
                    : new Uri(text + "/");
            }

            if (options.RequestTimeout > TimeSpan.Zero)
            {
                http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            }

            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.TryAddSingleton<IBundleStore>(_ =>
            options.BundleStore
            ?? (string.IsNullOrWhiteSpace(options.CacheDirectory)
                ? new InMemoryBundleStore()
                : new FileBundleStore(options.CacheDirectory!)));

        services.TryAddSingleton<ICtmsClient>(sp =>
        {
            var http = options.HttpClient
                       ?? sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var store = sp.GetRequiredService<IBundleStore>();
            return new CtmsClient(options, http, store);
        });

        return services;
    }
}
