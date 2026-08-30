using CTMS.Application.Translations;

namespace CTMS.Application.Tests;

/// <summary>Pure-unit coverage for the key-name → category derivation rule.</summary>
public sealed class CategorySuggestionTests
{
    [Theory]
    [InlineData("course.start", "Course")]
    [InlineData("nav.home.link", "Nav")]
    [InlineData("COURSE.start", "Course")]
    [InlineData("checkout.button.submit", "Checkout")]
    [InlineData("a.b", "A")]
    public void Derives_the_title_cased_prefix_before_the_first_dot(string keyName, string expected)
        => Assert.Equal(expected, CategorySuggestion.FromKeyName(keyName));

    [Theory]
    [InlineData("standalone")]
    [InlineData("no-dots-here")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData(".leadingdot")]
    public void Falls_back_to_General_without_a_usable_prefix(string? keyName)
        => Assert.Equal("General", CategorySuggestion.FromKeyName(keyName));
}
