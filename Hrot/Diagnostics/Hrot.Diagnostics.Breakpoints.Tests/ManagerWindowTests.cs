using System.Linq;
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
    // ── CE-1285 (Behaviour Debugging DBG-6, Q44 R-97): the one list, readable ─────────────────────────────────────────────

    /// <summary>⭐⭐ <c>CE-1285</c> — a "changed" condition reads as words, ⛔ never its unbounded range (🔒 user: <i>"condition shows just some
    /// weird large numbers"</i> — they were ±1.7976931348623157E+308); a bounded range reads as people write it; a compound joins its parts.
    /// 🔴 Red-proof: the old <c>[min, max]</c> back in <c>Range</c> ⇒ the E+308 text returns.</summary>
    [Fact]
    public void CE1285_AConditionReadsAsWords_NeverItsInfiniteRange()
    {
        var changed = new PropertyMatchDto
        {
            ComponentType = typeof(StubComponent), PropertyPath = "X", Operator = SearchOperator.Changed, Predicate = new NumericPredicateDto(),
        };
        string text = BreakpointConditionSummarizer.Summarize(changed);
        Assert.Equal($"Component: {typeof(StubComponent).Name}.X changed", text);
        Assert.DoesNotContain("E+308", text);
        Assert.Contains("<= 20", BreakpointConditionSummarizer.Summarize(new PropertyMatchDto
            { ComponentType = typeof(StubComponent), PropertyPath = "X", Predicate = new NumericPredicateDto { MaxValue = 20 } }));
        string both = BreakpointConditionSummarizer.Summarize(new CompoundPredicateDto
            { Operator = LogicalOperator.And, Conditions = new System.Collections.Generic.List<SearchPredicateDto> { changed, changed } });
        Assert.Contains($"{typeof(StubComponent).Name}.X changed AND Component: {typeof(StubComponent).Name}.X changed", both);
    }

    /// <summary>⭐ <c>CE-1285</c> (Q44-A/C) — one list, every kind told apart (Node / Journal / Data); a row carries its kind, its name
    /// (the display name, else the readable condition) and its unit as the host names it.</summary>
    [Fact]
    public void CE1285_OneList_EveryKind_NamedRows_TheUnitAsTheHostNamesIt()
    {
        var unit = new Entity(7, 1);
        _manager.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = "btree:1:abc" }, displayName: "Node Reload in FightFromCover");
        _manager.AddBreakpoint(new PropertyMatchDto
            { ComponentType = typeof(StubComponent), PropertyPath = "X", Operator = SearchOperator.Changed, Predicate = new NumericPredicateDto() }, unit);
        var panel = new DataBreakpointManagerPanel(_manager, new TemporalStatusBannerState(), entityLabel: e => $"#{1000 + e.Index}");

        var rows = panel.BuildViewModel("p", "k").Breakpoints;
        Assert.Equal(new[] { "Node", "Data" }, rows.Select(r => r.Kind));
        Assert.Equal("Node Reload in FightFromCover", rows[0].Name);
        Assert.Equal($"Component: {typeof(StubComponent).Name}.X changed", rows[1].Name);
        Assert.Equal("#1007", rows[1].Scope);
    }
}

// Test-only unmanaged component
[ComponentId(221)]
file struct StubComponent { public int X; 
}
