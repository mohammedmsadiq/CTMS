using CTMS.Api.Auth;

namespace CTMS.Api.Endpoints;

internal static class EndpointConventions
{
    /// <summary>
    /// The client delivery reads (translations, languages, applications) are anonymous while
    /// <c>Auth:PublicBundleReads</c> is <c>true</c> (the default) and require <c>CanRead</c>
    /// otherwise.
    /// </summary>
    public static TBuilder GatePublicRead<TBuilder>(this TBuilder builder, bool publicReads)
        where TBuilder : IEndpointConventionBuilder
    {
        if (publicReads)
        {
            builder.AllowAnonymous();
        }
        else
        {
            builder.RequireAuthorization(AuthorizationPolicies.CanRead);
        }

        return builder;
    }
}
