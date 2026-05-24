using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Presentation.Panels.Breakpoints;
using Hrot.Presentation.Windows;

namespace Hrot.Diagnostics.Breakpoints.Tests;

[Collection("ComponentRegistry")]
public sealed class ManagerWindowTests
{
    private readonly DataBreakpointManager _manager;

    public ManagerWindowTests()
    {
        ComponentTypeRegistry.Clear();
        var (mgr, _, _, _) = ManagerFactory.Create();
        _manager = mgr;
    }

    // ── P8T1 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ManagerWindow_PerspectiveBound_WindowHasCorrectScopeAndPerspective()
    {
        // Instantiate the window and verify it has the correct ManagedWindow properties.
        var panel = new DataBreakpointManagerPanel(
            _manager,
            new TemporalStatusBannerState());
        var window = new DataBreakpointManagerWindow(
            "dbm_test",
            "SimHost",
            panel);

        Assert.Equal(WindowScope.PerspectiveBound, window.Scope);
        Assert.Equal("SimHost", window.OwningPerspective);
        Assert.False(window.IsOpen); // starts closed
    }

    [Fact]
    public void ManagerWindow_AddRow_AppendsBreakpointToManager()
    {
        var panel = new DataBreakpointManagerPanel(
            _manager,
            new TemporalStatusBannerState());

        Assert.Empty(_manager.AllBreakpoints);
        panel.AddBreakpoint();  // internal seam
        Assert.Single(_manager.AllBreakpoints);
    }

    [Fact]
    public void ManagerWindow_EnableCheckbox_TogglesManagerSetEnabled()
    {
        var panel = new DataBreakpointManagerPanel(
            _manager,
            new TemporalStatusBannerState());

        // Add a breakpoint (starts enabled by default)
        var id = _manager.AddBreakpoint(new PropertyMatchDto(), displayName: "Test");
        var initial = _manager.AllBreakpoints[0];
        Assert.True(initial.Enabled);

        // Toggle off
        panel.ToggleEnabled(id);
        Assert.False(_manager.AllBreakpoints[0].Enabled);

        // Toggle on again
        panel.ToggleEnabled(id);
        Assert.True(_manager.AllBreakpoints[0].Enabled);
    }

    [Fact]
    public void ManagerWindow_EnableAll_EnablesAllBreakpoints()
    {
        var panel = new DataBreakpointManagerPanel(
            _manager,
            new TemporalStatusBannerState());

        var id1 = _manager.AddBreakpoint(new PropertyMatchDto());
        var id2 = _manager.AddBreakpoint(new PropertyMatchDto());
        _manager.SetEnabled(id1, false);
        _manager.SetEnabled(id2, false);

        panel.EnableAll();

        Assert.All(_manager.AllBreakpoints, bp => Assert.True(bp.Enabled));
    }

    [Fact]
    public void ManagerWindow_DisableAll_DisablesAllBreakpoints()
    {
        var panel = new DataBreakpointManagerPanel(
            _manager,
            new TemporalStatusBannerState());

        _manager.AddBreakpoint(new PropertyMatchDto());
        _manager.AddBreakpoint(new PropertyMatchDto());

        panel.DisableAll();

        Assert.All(_manager.AllBreakpoints, bp => Assert.False(bp.Enabled));
    }

    // ── P8T1 — BreakpointConditionSummarizer ─────────────────────────────────

    [Fact]
    public void ConditionSummarizer_Null_ReturnsNone()
    {
        Assert.Equal("(none)", BreakpointConditionSummarizer.Summarize(null));
    }

    [Fact]
    public void ConditionSummarizer_PropertyMatch_ContainsComponentName()
    {
        var dto = new PropertyMatchDto { ComponentType = typeof(StubComponent) };
        var summary = BreakpointConditionSummarizer.Summarize(dto);
        Assert.Contains("StubComponent", summary);
    }

    [Fact]
    public void ConditionSummarizer_Compound_ContainsOperatorAndCount()
    {
        var dto = new CompoundPredicateDto
        {
            Operator = LogicalOperator.And,
            Conditions = new System.Collections.Generic.List<SearchPredicateDto>
            {
                new PropertyMatchDto(),
                new PropertyMatchDto(),
            },
        };
        var summary = BreakpointConditionSummarizer.Summarize(dto);
        Assert.Contains("Compound", summary);
        Assert.Contains("And", summary);
        Assert.Contains("2", summary);
    }
}

// Test-only unmanaged component
[ComponentId(221)]
file struct StubComponent { public int X; }
