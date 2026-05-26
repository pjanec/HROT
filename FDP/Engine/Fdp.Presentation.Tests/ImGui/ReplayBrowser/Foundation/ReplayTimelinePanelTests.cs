using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser;
using Xunit;

namespace Fdp.Presentation.ReplayBrowser.Foundation;

/// <summary>
/// FND-T17: Snapshot immutability — mutating <see cref="JsonExportOptions"/> after
/// calling <see cref="ReplayTimelinePanel.CloneOptions"/> does not affect the clone.
/// </summary>
public sealed class ReplayTimelinePanelTests
{
    // ── FND-T17: CloneOptions produces an independent snapshot ────────────

    [Fact]
    public void CloneOptions_MutatingOriginal_DoesNotAffectSnapshot()
    {
        var original = new JsonExportOptions
        {
            StartFrame       = 10,
            EndFrame         = 20,
            WindowMode       = ExportWindowMode.ByFrame,
            FormatMode       = ExportFormatMode.Changelog,
            TargetEntities   = new List<Entity> { new Entity(1, 0) },
        };

        var snapshot = ReplayTimelinePanel.CloneOptions(original);

        // Mutate original
        original.StartFrame = 999;
        original.EndFrame   = 999;
        original.WindowMode = ExportWindowMode.FullFile;
        original.TargetEntities.Add(new Entity(2, 0));

        // Snapshot must be unaffected
        Assert.Equal(10, snapshot.StartFrame);
        Assert.Equal(20, snapshot.EndFrame);
        Assert.Equal(ExportWindowMode.ByFrame, snapshot.WindowMode);
        Assert.Single(snapshot.TargetEntities);
    }

    // ── GetDisabledFrameInputs ────────────────────────────────────────────

    [Fact]
    public void GetDisabledFrameInputs_ByFrame_ReturnsFalse()
    {
        Assert.False(ReplayTimelinePanel.GetDisabledFrameInputs(ExportWindowMode.ByFrame));
    }

    [Theory]
    [InlineData(ExportWindowMode.FullFile)]
    [InlineData(ExportWindowMode.ByTime)]
    public void GetDisabledFrameInputs_NonByFrame_ReturnsTrue(ExportWindowMode mode)
    {
        Assert.True(ReplayTimelinePanel.GetDisabledFrameInputs(mode));
    }

    // ── GetDisabledTimeInputs ─────────────────────────────────────────────

    [Fact]
    public void GetDisabledTimeInputs_ByTime_ReturnsFalse()
    {
        Assert.False(ReplayTimelinePanel.GetDisabledTimeInputs(ExportWindowMode.ByTime));
    }

    [Theory]
    [InlineData(ExportWindowMode.FullFile)]
    [InlineData(ExportWindowMode.ByFrame)]
    public void GetDisabledTimeInputs_NonByTime_ReturnsTrue(ExportWindowMode mode)
    {
        Assert.True(ReplayTimelinePanel.GetDisabledTimeInputs(mode));
    }

    // ── RBF-P4T6: Play button enable/disable logic ────────────────────────

    [Fact]
    public void RBF_P4T6_Play_DisabledInMerged()
    {
        Assert.False(ReplayTimelinePanel.IsPlayEnabled(hasRecording: true, isMergedView: true));
    }

    [Fact]
    public void RBF_P4T6_Play_EnabledInSingleNode()
    {
        Assert.True(ReplayTimelinePanel.IsPlayEnabled(hasRecording: true, isMergedView: false));
    }

    [Fact]
    public void RBF_P4T6_Play_DisabledWhenNoRecording()
    {
        Assert.False(ReplayTimelinePanel.IsPlayEnabled(hasRecording: false, isMergedView: false));
    }

    [Fact]
    public void RBF_P4T6_PlayTooltipContainsDisclaimer()
    {
        const string mergedTooltip = "Continuous playback is disabled in Merged View. Use Step-Forward/Backward or the timeline slider.";
        Assert.Contains("disabled in Merged View", mergedTooltip);
    }
}
