using System.Diagnostics;
using Microsoft.AspNetCore.HttpLogging;

namespace CTMS.Api.Infrastructure;

/// <summary>
/// Console logging shape and lightweight HTTP request logging.
/// <list type="bullet">
///   <item>Development gets the human-readable console; every other environment gets the
///   built-in JSON console formatter (scopes included, UTC timestamps) so a log shipper can
///   parse it. No third-party logging package — <c>AddJsonConsole</c> covers this.</item>
///   <item>Every log scope carries the current trace id (<see cref="ActivityTrackingOptions"/>),
///   which lines the logs up with the <c>traceId</c> that <c>AddProblemDetails</c> puts on
///   error responses.</item>
///   <item><see cref="UseCtmsHttpLogging"/> logs one line per request — method, path, status,
///   elapsed — and nothing else (no headers, no bodies). <c>/health</c> is excluded.</item>
/// </list>
/// </summary>
internal static class LoggingSetup
{
    public static WebApplicationBuilder AddCtmsLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddSimpleConsole(options => options.SingleLine = false);
        }
        else
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
            });
        }

        builder.Logging.Configure(options =>
            options.ActivityTrackingOptions =
                ActivityTrackingOptions.TraceId
                | ActivityTrackingOptions.SpanId
                | ActivityTrackingOptions.ParentId);

        builder.Services.AddHttpLogging(options =>
            options.LoggingFields =
                HttpLoggingFields.RequestMethod
                | HttpLoggingFields.RequestPath
                | HttpLoggingFields.ResponseStatusCode
                | HttpLoggingFields.Duration);

        return builder;
    }

    /// <summary>Request logging for everything except the health probes.</summary>
    public static IApplicationBuilder UseCtmsHttpLogging(this WebApplication app)
    {
        return app.UseWhen(
            context => !context.Request.Path.StartsWithSegments("/health"),
            branch => branch.UseHttpLogging());
    }
}
