using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace CTMS.Client;

/// <summary>
/// <see cref="DelegatingHandler"/> that adds <c>Authorization: Bearer &lt;token&gt;</c> to every
/// outgoing request that does not already carry one, taking the token from
/// <see cref="CtmsClientOptions.AuthTokenProvider"/> (preferred) or
/// <see cref="CtmsClientOptions.AuthToken"/>. Registered by <c>AddCtmsClient</c>; net10.0 only.
/// </summary>
internal sealed class CtmsAuthTokenHandler : DelegatingHandler
{
    private readonly CtmsClientOptions _options;

    public CtmsAuthTokenHandler(CtmsClientOptions options) => _options = options;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var token = _options.AuthTokenProvider is not null
                ? await _options.AuthTokenProvider(cancellationToken).ConfigureAwait(false)
                : _options.AuthToken;

            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
