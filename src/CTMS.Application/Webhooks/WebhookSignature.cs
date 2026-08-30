using System.Security.Cryptography;
using System.Text;

namespace CTMS.Application.Webhooks;

/// <summary>
/// Computes the <c>X-CTMS-Signature</c> header value for a webhook delivery:
/// <c>sha256=&lt;lowercase-hex HMAC-SHA256(secret, rawBody)&gt;</c>. Consumers verify by recomputing
/// the HMAC over the exact bytes they received.
/// </summary>
public static class WebhookSignature
{
    public const string HeaderName = "X-CTMS-Signature";

    public static string Compute(string secret, string rawBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentNullException.ThrowIfNull(rawBody);

        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody));
        return "sha256=" + Convert.ToHexStringLower(mac);
    }
}
