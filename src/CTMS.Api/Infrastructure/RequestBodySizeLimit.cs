using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CTMS.Api.Infrastructure;

/// <summary>
/// Caps the request body size for the whole API. Every CTMS write is small JSON (the largest
/// realistic body is a single translation-string upsert), so a low ceiling costs nothing and
/// closes off oversized-payload abuse.
/// </summary>
/// <remarks>
/// The limit is <c>Limits:MaxRequestBodyBytes</c> (default 262144 = 256&nbsp;KB). It is applied
/// two ways: on Kestrel's global <see cref="Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerLimits.MaxRequestBodySize"/>
/// for real deployments, and by <see cref="Middleware"/> which rejects an over-limit request
/// with <c>413</c> + an RFC 7807 body (this also covers hosting models — e.g. the test server —
/// that do not honour the Kestrel limit).
/// </remarks>
internal static class RequestBodySizeLimit
{
    public const string MaxRequestBodyBytesKey = "Limits:MaxRequestBodyBytes";

    public const long DefaultMaxRequestBodyBytes = 262144;

    /// <summary>
    /// The ceiling for endpoints marked with <see cref="LargeImportBody"/> (the bulk import). Import
    /// files routinely exceed the 256&nbsp;KB host default, so they are allowed up to this instead
    /// (<c>Limits:MaxImportBodyBytes</c>, default 5&nbsp;MB).
    /// </summary>
    public const string MaxImportBodyBytesKey = "Limits:MaxImportBodyBytes";

    public const long DefaultMaxImportBodyBytes = 5 * 1024 * 1024;

    public static long MaxRequestBodyBytes(this IConfiguration configuration)
    {
        var configured = configuration.GetValue(MaxRequestBodyBytesKey, DefaultMaxRequestBodyBytes);
        return configured > 0 ? configured : DefaultMaxRequestBodyBytes;
    }

    public static long MaxImportBodyBytes(this IConfiguration configuration)
    {
        var configured = configuration.GetValue(MaxImportBodyBytesKey, DefaultMaxImportBodyBytes);
        return configured > 0 ? configured : DefaultMaxImportBodyBytes;
    }

    /// <summary>
    /// Endpoint metadata marker: the body-size middleware lets a request to this endpoint run up to
    /// <see cref="MaxImportBodyBytesKey"/> instead of the global <see cref="MaxRequestBodyBytesKey"/>.
    /// </summary>
    public sealed class LargeImportBody;

    /// <summary>
    /// Short-circuits a request whose declared or streamed body exceeds <paramref name="maxBytes"/>
    /// with <c>413 Payload Too Large</c>.
    /// </summary>
    public static IApplicationBuilder UseCtmsRequestBodySizeLimit(
        this IApplicationBuilder app,
        long maxBytes,
        long importMaxBytes)
    {
        return app.Use(async (context, next) =>
        {
            // The bulk-import endpoint opts in to a larger ceiling via endpoint metadata.
            var limit = context.GetEndpoint()?.Metadata.GetMetadata<LargeImportBody>() is not null
                ? importMaxBytes
                : maxBytes;

            // Tighten the per-request limit too, so a chunked body with no Content-Length is
            // still capped when it is actually read.
            var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false })
            {
                sizeFeature.MaxRequestBodySize = limit;
            }

            if (context.Request.ContentLength is { } declared && declared > limit)
            {
                await WriteTooLargeAsync(context, limit);
                return;
            }

            try
            {
                await next(context);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                await WriteTooLargeAsync(context, limit);
            }
        });
    }

    private static async Task WriteTooLargeAsync(HttpContext context, long maxBytes)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;

        var problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "Request body too large",
                Detail = $"The request body exceeds the {maxBytes}-byte limit.",
            },
        });
    }
}
