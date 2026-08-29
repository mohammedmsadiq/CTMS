using CTMS.Api.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace CTMS.Application.Tests;

/// <summary>
/// Unit coverage for the <c>If-None-Match</c> -> <c>304</c> decision on
/// <c>GET .../bundles/{localeCode}</c>. Exercised directly rather than through a
/// <c>WebApplicationFactory</c>: there is no API test project, and standing one up (plus
/// pointing the whole host at EphemeralMongo and disabling the startup hosted services) is out
/// of proportion for one header check. The endpoint itself is a two-line call into this helper.
/// </summary>
public sealed class BundleConditionalRequestTests
{
    private const string ETag = "a1b2c3d4e5f6";

    [Fact]
    public void No_header_is_not_a_match()
        => Assert.False(BundleConditionalRequest.IsNotModified(StringValues.Empty, ETag));

    [Fact]
    public void Quoted_exact_tag_matches()
        => Assert.True(BundleConditionalRequest.IsNotModified(new StringValues($"\"{ETag}\""), ETag));

    [Fact]
    public void Unquoted_tag_matches()
        => Assert.True(BundleConditionalRequest.IsNotModified(new StringValues(ETag), ETag));

    [Fact]
    public void Weak_validator_form_matches()
        => Assert.True(BundleConditionalRequest.IsNotModified(new StringValues($"W/\"{ETag}\""), ETag));

    [Fact]
    public void Star_matches_any_current_representation()
        => Assert.True(BundleConditionalRequest.IsNotModified(new StringValues("*"), ETag));

    [Fact]
    public void Comma_separated_list_containing_the_tag_matches()
        => Assert.True(BundleConditionalRequest.IsNotModified(
            new StringValues($"\"00000000\", \"{ETag}\", \"ffffffff\""), ETag));

    [Fact]
    public void Multi_value_header_containing_the_tag_matches()
        => Assert.True(BundleConditionalRequest.IsNotModified(
            new StringValues(new[] { "\"00000000\"", $"\"{ETag}\"" }), ETag));

    [Fact]
    public void Non_matching_tag_is_not_a_match()
        => Assert.False(BundleConditionalRequest.IsNotModified(new StringValues("\"deadbeef\""), ETag));

    [Fact]
    public void Empty_current_etag_is_never_a_match_even_for_star()
        => Assert.False(BundleConditionalRequest.IsNotModified(new StringValues("*"), null));
}
