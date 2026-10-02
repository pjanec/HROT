using Hrot.SimHost.UI;
using Hrot.UI.Common.Facades;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// `T5` — the SimHost simulation-controls panel was INERT: Pause toggled a private field, Step set a
/// private flag, and <c>ConsumeStepRequest</c> had no caller anywhere in the repo. Pressing Pause did
/// nothing, silently, which is `AS-9`'s shape exactly.
///
/// <para>These rails pin the routing, not the rendering: ImGui cannot be driven headlessly here, so
/// they assert what the panel READS and what it FORWARDS — which is the half that was broken.</para>
/// </summary>
public class TheSimControlsActuallyControlTimeTests
{
    private sealed class SpyFacade : ITimeTransportFacade
    {
        public bool  IsPaused  { get; set; }
        public float TimeScale { get; set; } = 1f;
        public double TotalTime => 0.0;

        public bool IsPlayPauseEnabled => true;
        public bool IsStepEnabled      => true;
        public bool IsStopEnabled      => true;

        public int   ToggleCount { get; private set; }
        public int   StepCount   { get; private set; }
        public int   StopCount   { get; private set; }
        public float LastScaleSet { get; private set; } = float.NaN;

        public void TogglePlayPause()      => ToggleCount++;
        public void Step()                 => StepCount++;
        public void Stop()                 => StopCount++;
        public void SetTimeScale(float s)  { LastScaleSet = s; TimeScale = s; }
    }

    /// <summary>
    /// The panel must not hold a pause state of its own. SimHost is a SLAVE node: it cannot pause
    /// itself, so its only truthful answer is the cluster's.
    /// </summary>
    [Fact]
    public void ThePanelReportsTheClustersPauseState_NotItsOwn()
    {
        var panel  = new SimHostSimulationControlsPanel();
        var facade = new SpyFacade { IsPaused = true, TimeScale = 3f };

        Assert.False(panel.IsPaused);          // no transport yet
        Assert.Equal(1f, panel.TimeScale);

        panel.TimeFacade = facade;

        Assert.True(panel.IsPaused);
        Assert.Equal(3f, panel.TimeScale);

        facade.IsPaused = false;
        Assert.False(panel.IsPaused);          // derived, never latched (R-126)
    }

    /// <summary>
    /// The forwarding rail (`SILENT-DEFAULT` control): the aggregate UI must hand the transport
    /// through to the panel that renders the buttons, not keep it to itself.
    /// </summary>
    [Fact]
    public void TheMainUiForwardsTheTransportToTheControlsPanel()
    {
        var ui     = new SimHostMainUI();
        var facade = new SpyFacade { IsPaused = true };

        Assert.False(ui.IsPaused);

        ui.TimeFacade = facade;

        Assert.Same(facade, ui.TimeFacade);
        Assert.True(ui.IsPaused);              // reached the inner panel, not just the wrapper
    }

    /// <summary>
    /// The composition root has the transport and must pass it — the defect shape named in
    /// CLAUDE.md's silent-default section. Asserted on the CONSTRUCTED object, not on source.
    /// </summary>
    [Fact]
    public void AnUnwiredPanelAnswersHonestly_RatherThanPretendingToBeRunning()
    {
        var panel = new SimHostSimulationControlsPanel();

        Assert.Null(panel.TimeFacade);
        Assert.False(panel.IsPaused);
        Assert.Equal(1f, panel.TimeScale);
    }
}
