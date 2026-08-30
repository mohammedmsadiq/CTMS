using Microsoft.Extensions.Primitives;

namespace CTMS.Api.Infrastructure;

/// <summary>
/// Evaluates an HTTP <c>If-None-Match</c> request header against a strong ETag to decide whether
/// <c>GET /api/translations/{application}/{language}</c> should answer <c>304 Not Modified</c>.
/// </summary>
public static class ConditionalRequest
{
    /// <summary>
    /// <c>true</c> when <paramref name="ifNoneMatch"/> contains <c>*</c> or an entity-tag that
    /// matches <paramref name="rawETag"/> (the raw, unquoted lowercase-hex content hash).
    /// </summary>
    /// <remarks>
    /// Accepts the header split across multiple values or as one comma-separated list, each tag
    /// optionally wrapped in double quotes and optionally prefixed with the weak marker
    /// <c>W/</c>. Splitting on <c>,</c> is safe because a SHA-256 hex hash never contains a comma.
    /// </remarks>
    public static bool IsNotModified(StringValues ifNoneMatch, string? rawETag)
    {
        if (StringValues.IsNullOrEmpty(ifNoneMatch) || string.IsNullOrEmpty(rawETag))
        {
            return false;
        }

        foreach (var headerValue in ifNoneMatch)
        {
            if (headerValue is null)
            {
                continue;
            }

            foreach (var rawToken in headerValue.Split(','))
            {
                var token = rawToken.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                if (token == "*")
                {
                    return true;
                }

                if (token.StartsWith("W/", StringComparison.Ordinal))
                {
                    token = token[2..].Trim();
                }

                token = token.Trim('"');

                if (string.Equals(token, rawETag, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
