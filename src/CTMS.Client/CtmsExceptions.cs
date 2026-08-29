using System;

namespace CTMS.Client;

/// <summary>Base type for every error surfaced by the CTMS client SDK.</summary>
public class CtmsException : Exception
{
    public CtmsException(string message)
        : base(message)
    {
    }

    public CtmsException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The CTMS API returned an error response (typically an <c>application/problem+json</c> body).
/// </summary>
public sealed class CtmsApiException : CtmsException
{
    public CtmsApiException(int statusCode, string? title, string? detail)
        : base(BuildMessage(statusCode, title, detail))
    {
        StatusCode = statusCode;
        Title = title;
        Detail = detail;
    }

    /// <summary>HTTP status code of the failing response.</summary>
    public int StatusCode { get; }

    /// <summary>ProblemDetails <c>title</c>, when present.</summary>
    public string? Title { get; }

    /// <summary>ProblemDetails <c>detail</c>, when present.</summary>
    public string? Detail { get; }

    private static string BuildMessage(int statusCode, string? title, string? detail)
    {
        var headline = string.IsNullOrWhiteSpace(title) ? "CTMS API request failed" : title!;
        return string.IsNullOrWhiteSpace(detail)
            ? $"{headline} (HTTP {statusCode})."
            : $"{headline} (HTTP {statusCode}): {detail}";
    }
}

/// <summary>
/// A bundle was requested that is not in the local cache and the CTMS API could not be reached.
/// The SDK never blocks an application on a translation fetch; callers should treat this as
/// "translations unavailable" and fall back to their own defaults.
/// </summary>
public sealed class CtmsOfflineException : CtmsException
{
    public CtmsOfflineException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
