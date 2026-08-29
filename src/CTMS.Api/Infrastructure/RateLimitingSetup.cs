using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace CTMS.Api.Infrastructure;

/// <summary>
/// Global request rate limiting. One partitioned fixed-window limiter fronts the whole API:
/// <list type="bullet">
///   <item>authenticated callers are partitioned by their stable user id (<c>oid</c> →
///   name-identifier → <c>preferred_username</c> → name);</item>
///   <item>anonymous callers fall back to the remote IP;</item>
///   <item>the anonymous bundle <b>delivery</b> GET path (the SDK/CDN caller) gets its own,
///   looser IP-keyed partition so a busy CDN edge does not exhaust a translator's budget.</item>
/// </list>
/// A rejected request gets <c>429</c> with an RFC 7807 body and a <c>Retry-After</c> header.
/// <c>/health</c> and <c>/health/ready</c> opt out via <c>.DisableRateLimiting()</c>.
/// The whole feature is switched off when <c>RateLimit:Enabled</c> is <c>false</c> (tests).
/// </summary>
internal static class RateLimitingSetup
{
    public const string EnabledKey = "RateLimit:Enabled";
    public const string PermitPerWindowKey = "RateLimit:PermitPerWindow";
    public const string WindowSecondsKey = "RateLimit:WindowSeconds";
    public const string QueueLimitKey = "RateLimit:QueueLimit";
    public const string BundlePermitPerWindowKey = "RateLimit:BundlePermitPerWindow";

    private const int DefaultPermitPerWindow = 120;
    private const int DefaultWindowSeconds = 60;
    private const int DefaultQueueLimit = 0;

    public static bool RateLimitingEnabled(this IConfiguration configuration) =>
        configuration.GetValue(EnabledKey, true);

    public static IServiceCollection AddCtmsRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.RateLimitingEnabled())
        {
            return services;
        }

        var permit = Positive(configuration.GetValue(PermitPerWindowKey, DefaultPermitPerWindow), DefaultPermitPerWindow);
        var windowSeconds = Positive(configuration.GetValue(WindowSecondsKey, DefaultWindowSeconds), DefaultWindowSeconds);
        var queueLimit = Math.Max(0, configuration.GetValue(QueueLimitKey, DefaultQueueLimit));
        var bundlePermit = Positive(configuration.GetValue(BundlePermitPerWindowKey, permit * 5), permit * 5);
        var window = TimeSpan.FromSeconds(windowSeconds);
        var retryAfterSeconds = windowSeconds.ToString(CultureInfo.InvariantCulture);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (IsBundleDelivery(context.Request))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"bundle:{RemoteIp(context)}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = bundlePermit,
                            Window = window,
                            QueueLimit = queueLimit,
                        });
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permit,
                        Window = window,
                        QueueLimit = queueLimit,
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.Headers.RetryAfter =
                    context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                        ? ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture)
                        : retryAfterSeconds;

                response.StatusCode = StatusCodes.Status429TooManyRequests;

                var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetails.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Rate limit exceeded. Retry after the interval given in the Retry-After header.",
                    },
                });
            };
        });

        return services;
    }

    private static bool IsBundleDelivery(HttpRequest request) =>
        HttpMethods.IsGet(request.Method)
        && request.Path.HasValue
        && request.Path.Value!.Contains("/bundles/", StringComparison.OrdinalIgnoreCase);

    private static string PartitionKey(HttpContext context)
    {
        if (context.User.Identity is { IsAuthenticated: true })
        {
            var id = context.User.FindFirstValue("oid")
                ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("preferred_username")
                ?? context.User.FindFirstValue(ClaimTypes.Name);

            if (!string.IsNullOrWhiteSpace(id))
            {
                return $"user:{id}";
            }
        }

        return $"ip:{RemoteIp(context)}";
    }

    private static string RemoteIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static int Positive(int value, int fallback) => value > 0 ? value : fallback;
}
