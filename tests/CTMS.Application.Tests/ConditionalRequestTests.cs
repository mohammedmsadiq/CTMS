using CTMS.Api.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace CTMS.Application.Tests;

/// <summary>
/// Unit coverage for the <c>If-None-Match</c> → <c>304</c> decision on
/// <c>GET /api/translations/{application}/{language}</c>. Exercised directly: the endpoint is a
/// two-line call into this helper.
/// </summary>
public sealed class ConditionalRequestTests
{
    private const string ETag = "a1b2c3d4e5f6";

    [Fact]
    public void No_header_is_not_a_match()
        => Assert.False(ConditionalRequest.IsNotModified(StringValues.Empty, ETag));

    [Fact]
    public void Quoted_exact_tag_matches()
        => Assert.True(ConditionalRequest.IsNotModified(new StringValues($"\"{ETag}\""), ETag));

    [Fact]
    public void Unquoted_tag_matches()
        => Assert.True(ConditionalRequest.IsNotModified(new StringValues(ETag), ETag));

    [Fact]
    public void Weak_validator_form_matches()
        => Assert.True(ConditionalRequest.IsNotModified(new StringValues($"W/\"{ETag}\""), ETag));

    [Fact]
    public void Star_matches_any_current_representation()
        => Assert.True(ConditionalRequest.IsNotModified(new StringValues("*"), ETag));

    [Fact]
    public void Comma_separated_list_containing_the_tag_matches()
        => Assert.True(ConditionalRequest.IsNotModified(
            new StringValues($"\"00000000\", \"{ETag}\", \"ffffffff\""), ETag));

    [Fact]
    public void Multi_value_header_containing_the_tag_matches()
        => Assert.True(ConditionalRequest.IsNotModified(
            new StringValues(new[] { "\"00000000\"", $"\"{ETag}\"" }), ETag));

    [Fact]
    public void Non_matching_tag_is_not_a_match()
        => Assert.False(ConditionalRequest.IsNotModified(new StringValues("\"deadbeef\""), ETag));

    [Fact]
    public void Empty_current_etag_is_never_a_match_even_for_star()
        => Assert.False(ConditionalRequest.IsNotModified(new StringValues("*"), null));
}
