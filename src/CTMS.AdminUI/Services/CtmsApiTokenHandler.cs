using System.Net;
using System.Net.Http.Headers;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace CTMS.AdminUI.Services;

/// <summary>
/// Attaches an Entra ID bearer token (acquired on behalf of the signed-in user) to every
/// outgoing <see cref="CtmsApiClient"/> request. Registered with
/// <c>AddHttpMessageHandler&lt;CtmsApiTokenHandler&gt;()</c> only when <c>Auth:Enabled</c> is
/// true; in the dev-bypass mode the API accepts unauthenticated calls, so no handler is added.
/// </summary>
/// <remarks>
/// The downstream API scope comes from configuration key <c>Ctms:ApiScope</c>
/// (e.g. <c>api://&lt;api-client-id&gt;/access_as_user</c>). If interactive sign-in is required
/// again (<see cref="MicrosoftIdentityWebChallengeUserException"/> /
/// <see cref="MsalUiRequiredException"/>) the request is short-circuited with a synthetic
/// <c>401</c> so <see cref="CtmsApiClient"/>'s existing error path surfaces "Not authenticated"
/// rather than throwing; the next full-page navigation re-runs the OIDC challenge.
/// </remarks>
public sealed class CtmsApiTokenHandler(
    ITokenAcquisition tokenAcquisition,
    IConfiguration configuration,
    ILogger<CtmsApiTokenHandler> logger)
    : DelegatingHandler
{
    private readonly string _apiScope = configuration["Ctms:ApiScope"]
        ?? throw new InvalidOperationException(
            "Configuration key 'Ctms:ApiScope' is required when Auth:Enabled is true.");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = await tokenAcquisition.GetAccessTokenForUserAsync([_apiScope]);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        catch (Exception ex) when (ex is MsalUiRequiredException or MicrosoftIdentityWebChallengeUserException)
        {
            logger.LogInformation(ex, "Interactive sign-in required to call the CTMS API; returning 401.");
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "Interactive sign-in required",
                RequestMessage = request,
            };
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
