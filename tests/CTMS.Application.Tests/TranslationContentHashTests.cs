using CTMS.Application.Translations;

namespace CTMS.Application.Tests;

public sealed class TranslationContentHashTests
{
    [Fact]
    public void Compute_is_stable_regardless_of_entry_order()
    {
        var a = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1", ["c"] = "3" };
        var b = new Dictionary<string, string> { ["c"] = "3", ["a"] = "1", ["b"] = "2" };

        Assert.Equal(TranslationContentHash.Compute(a), TranslationContentHash.Compute(b));
    }

    [Fact]
    public void Compute_changes_when_a_value_changes()
    {
        var a = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var b = new Dictionary<string, string> { ["a"] = "1", ["b"] = "changed" };

        Assert.NotEqual(TranslationContentHash.Compute(a), TranslationContentHash.Compute(b));
    }

    [Fact]
    public void Compute_is_lowercase_hex_sha256()
    {
        var hash = TranslationContentHash.Compute(new Dictionary<string, string> { ["k"] = "v" });

        Assert.Equal(64, hash.Length);
        Assert.All(hash, c => Assert.Contains(c, "0123456789abcdef"));
    }
}
