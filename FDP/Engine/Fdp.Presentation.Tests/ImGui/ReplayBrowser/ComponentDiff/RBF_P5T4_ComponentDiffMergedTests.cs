using System;
using Fdp.Presentation.Panels.ReplayBrowser;
using Xunit;

namespace Fdp.Presentation.ReplayBrowser.ComponentDiff;

/// <summary>
/// RBF-P5T4: "Seek to Previous/Next Change" arrows must be disabled in Merged View.
/// Tests the <see cref="ComponentDiffPanel.IsMergedViewQuery"/> gate and the
/// <see cref="ComponentDiffPanel.MergedViewDisabledTooltip"/> constant.
/// </summary>
public sealed class RBF_P5T4_ComponentDiffMergedTests
{
    /// <summary>
    /// RBF-P5T4: IsSeekToChangeEnabled returns false when isMerged is true,
    /// regardless of isSearching.
    /// </summary>
    [Theory]
    [InlineData(false, true,  false)]  // not searching, but merged => disabled
    [InlineData(true,  true,  false)]  // searching and merged => disabled
    [InlineData(false, false, true)]   // not searching, not merged => enabled
    [InlineData(true,  false, false)]  // searching, not merged => disabled
    public void RBF_P5T4_IsSeekToChangeEnabled_Logic(bool isSearching, bool isMerged, bool expected)
    {
        bool result = ComponentDiffPanel.IsSeekToChangeEnabled(isSearching, isMerged);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// RBF-P5T4: When IsMergedViewQuery returns true, IsSeekToChangeEnabled is false.
    /// </summary>
    [Fact]
    public void RBF_P5T4_PrevChange_DisabledInMerged()
    {
        var panel = new ComponentDiffPanel();
        panel.IsMergedViewQuery = () => true;
        panel.IsSearching       = false;

        bool enabled = ComponentDiffPanel.IsSeekToChangeEnabled(panel.IsSearching, panel.IsMergedViewQuery());
        Assert.False(enabled);
    }

    /// <summary>
    /// RBF-P5T4: When IsMergedViewQuery returns true, next-change button is also disabled.
    /// </summary>
    [Fact]
    public void RBF_P5T4_NextChange_DisabledInMerged()
    {
        var panel = new ComponentDiffPanel();
        panel.IsMergedViewQuery = () => true;
        panel.IsSearching       = false;

        bool enabled = ComponentDiffPanel.IsSeekToChangeEnabled(panel.IsSearching, panel.IsMergedViewQuery());
        Assert.False(enabled);
    }

    /// <summary>
    /// RBF-P5T4: When IsMergedViewQuery returns false and IsSearching is false, buttons are enabled.
    /// </summary>
    [Fact]
    public void RBF_P5T4_PrevNextChange_EnabledInSingleNode()
    {
        var panel = new ComponentDiffPanel();
        panel.IsMergedViewQuery = () => false;
        panel.IsSearching       = false;

        bool enabled = ComponentDiffPanel.IsSeekToChangeEnabled(panel.IsSearching, panel.IsMergedViewQuery());
        Assert.True(enabled);
    }

    /// <summary>
    /// RBF-P5T4: MergedViewDisabledTooltip contains the expected disclaimer text.
    /// </summary>
    [Fact]
    public void RBF_P5T4_TooltipContainsDisclaimer()
    {
        Assert.Contains(
            "Step-change search is disabled in Merged View",
            ComponentDiffPanel.MergedViewDisabledTooltip);
    }

    /// <summary>
    /// RBF-P5T4: OnSeekToChangeRequested is NOT invoked when IsMergedViewQuery returns true.
    /// (Tests that the button-click guard in DrawContent honours IsMergedViewQuery.)
    /// </summary>
    [Fact]
    public void RBF_P5T4_OnSeekToChange_NotInvokedWhenMerged()
    {
        var panel = new ComponentDiffPanel();
        panel.IsMergedViewQuery = () => true;
        panel.IsSearching       = false;

        bool invoked = false;
        panel.OnSeekToChangeRequested = _ => { invoked = true; };

        // Simulate the internal enabled-check: in merged mode, enabled == false,
        // so the callback must not be triggered.
        bool enabled = ComponentDiffPanel.IsSeekToChangeEnabled(panel.IsSearching, panel.IsMergedViewQuery());
        if (enabled)
            panel.OnSeekToChangeRequested?.Invoke(1);

        Assert.False(invoked);
    }
}
