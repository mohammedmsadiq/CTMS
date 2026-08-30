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
    /// client — base address, a JSON <c>Accept</c> header and an auth-token
    /// <see cref="System.Net.Http.DelegatingHandler"/> (adds <c>Authorization: Bearer</c> from
    /// <see cref="CtmsClientOptions.AuthToken"/> / <see cref="CtmsClientOptions.AuthTokenProvider"/>
    /// when present) — and an <see cref="ITranslationStore"/> chosen from
    /// <see cref="CtmsClientOptions.CacheDirectory"/> / <see cref="CtmsClientOptions.TranslationStore"/>.
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
        })
        .AddHttpMessageHandler(() => new CtmsAuthTokenHandler(options));

        services.TryAddSingleton<ITranslationStore>(_ =>
            options.TranslationStore
            ?? (string.IsNullOrWhiteSpace(options.CacheDirectory)
                ? new InMemoryTranslationStore()
                : new FileTranslationStore(options.CacheDirectory!)));

        services.TryAddSingleton<ICtmsClient>(sp =>
        {
            var http = options.HttpClient
                       ?? sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var store = sp.GetRequiredService<ITranslationStore>();
            return new CtmsClient(options, http, store);
        });

        return services;
    }
}
