using NodeEditor.Core.Search;
using FluentAssertions;
using Xunit;

namespace NodeEditor.Core.Tests.Search;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("",      "Multiply", 1)]        // empty query: match-all
    [InlineData("mult",  "Multiply", 5000)]     // prefix
    [InlineData("vm",    "VectorMultiply", 2500)] // camelCase
    [InlineData("vec",   "VectorMultiply", 5000)] // prefix
    [InlineData("ltp",   "VectorMultiply", 500)]  // fuzzy char-order
    [InlineData("xyz",   "VectorMultiply", 0)]    // no match
    public void Score_TierBehavior(string query, string candidate, int expectedMin)
    {
        var r = FuzzyMatcher.Score(query, candidate);
        if (expectedMin == 0) r.HasMatch.Should().BeFalse();
        else r.Score.Should().BeGreaterThanOrEqualTo(expectedMin - 100); // tolerance
    }

    [Fact]
    public void ExactMatch_BeatsAllOthers()
    {
        var exact   = FuzzyMatcher.Score("multiply", "multiply");
        var prefix  = FuzzyMatcher.Score("multiply", "multiplyVector");
        exact.Score.Should().BeGreaterThan(prefix.Score);
    }

    [Fact]
    public void Prefix_BeatsSubstring()
    {
        var prefix    = FuzzyMatcher.Score("mult", "multiply");
        var substring = FuzzyMatcher.Score("mult", "submultiplex");
        prefix.Score.Should().BeGreaterThan(substring.Score);
    }

    [Fact]
    public void Keywords_ProvideMatch()
    {
        var noKey = FuzzyMatcher.Score("multiply", "Mul");
        var withKey = FuzzyMatcher.Score("multiply", "Mul", new[] { "multiply" });
        withKey.Score.Should().BeGreaterThan(noKey.Score);
    }
}
