using FluentAssertions;
using NodeEditor.UI.Picker;
using Xunit;

namespace NodeEditor.UI.Tests.Picker;

public sealed class PickerTextHighlighterTests
{
    [Fact]
    public void SplitRuns_HighlightsMatchedRanges_ForGrdOverGuard()
    {
        // Fuzzy "grd" over "Guard" → positions {0,3,4}
        var runs = PickerTextHighlighter.SplitRuns("Guard", new[] { 0, 3, 4 });

        runs.Should().HaveCount(3);
        runs[0].Text.Should().Be("G");
        runs[0].IsMatch.Should().BeTrue();
        runs[1].Text.Should().Be("ua");
        runs[1].IsMatch.Should().BeFalse();
        runs[2].Text.Should().Be("rd");
        runs[2].IsMatch.Should().BeTrue();
    }

    [Fact]
    public void SplitRuns_NoMatchPositions_YieldsSinglePlainRun()
    {
        // Null matchPositions.
        var runs1 = PickerTextHighlighter.SplitRuns("Guard", null);
        runs1.Should().HaveCount(1);
        runs1[0].Text.Should().Be("Guard");
        runs1[0].IsMatch.Should().BeFalse();

        // Empty matchPositions.
        var runs2 = PickerTextHighlighter.SplitRuns("Guard", Array.Empty<int>());
        runs2.Should().HaveCount(1);
        runs2[0].Text.Should().Be("Guard");
        runs2[0].IsMatch.Should().BeFalse();
    }

    [Fact]
    public void SplitRuns_AllMatched_YieldsSingleHighlightedRun()
    {
        var runs = PickerTextHighlighter.SplitRuns("Guard", new[] { 0, 1, 2, 3, 4 });

        runs.Should().HaveCount(1);
        runs[0].Text.Should().Be("Guard");
        runs[0].IsMatch.Should().BeTrue();
    }

    [Fact]
    public void SplitRuns_ConcatenationReproducesName()
    {
        var runs = PickerTextHighlighter.SplitRuns("Blueprint", new[] { 0, 1, 2, 5, 6, 7 });

        string reconstructed = string.Concat(runs.Select(r => r.Text));
        reconstructed.Should().Be("Blueprint");
    }

    [Fact]
    public void SplitRuns_EmptyName_YieldsEmpty()
    {
        var runs = PickerTextHighlighter.SplitRuns("", new[] { 0 });
        runs.Should().BeEmpty();
    }
}
