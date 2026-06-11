using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.WindowManager;
using Xunit;
using ImGuiNET;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Tests for <see cref="MainToolbarManager"/> — MTB-P1-T1 success conditions.
/// Pure-logic tests (Height, GetVisibleItemPlan) require no ImGui frame.
/// Render-path tests create an inline <see cref="ImGuiTestFixture"/> headless context
/// and use recording delegates to verify invocation order.
/// All tests run sequentially via the "ImGui Sequential" collection.
/// </summary>
[Collection("ImGui Sequential")]
public class MainToolbarManagerTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // MTB-P1-T1.C1: Duplicate entry id — last-write-wins
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterEntry_DuplicateId_LastWriteWins()
    {
        var mgr = new MainToolbarManager();
        var callLog = new List<string>();

        mgr.RegisterEntry("btn", 0, 64f, () => callLog.Add("first"));
        mgr.RegisterEntry("btn", 0, 64f, () => callLog.Add("second"));

        // BATCH-25: RenderInline is called inside BeginMainMenuBar (production path).
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        ImGuiNET.ImGui.BeginMainMenuBar();
        mgr.RenderInline();
        ImGuiNET.ImGui.EndMainMenuBar();
        fixture.Render();

        Assert.Single(callLog);
        Assert.Equal("second", callLog[0]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MTB-P1-T1.C2: Entries render in ascending sort order
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Entries_RenderInAscendingSortOrder()
    {
        var mgr = new MainToolbarManager();
        var callLog = new List<string>();

        mgr.RegisterEntry("C", 30, 64f, () => callLog.Add("C"));
        mgr.RegisterEntry("A", 10, 64f, () => callLog.Add("A"));
        mgr.RegisterEntry("B", 20, 64f, () => callLog.Add("B"));

        // BATCH-25: RenderInline inside the menu bar (production path).
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        ImGuiNET.ImGui.BeginMainMenuBar();
        mgr.RenderInline();
        ImGuiNET.ImGui.EndMainMenuBar();
        fixture.Render();

        Assert.Equal(new[] { "A", "B", "C" }, callLog);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MTB-P1-T1.C3: Perspective filter — null = global, named = only when match
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PerspectiveFilter_NullIsGlobal_NamedOnlyWhenMatch()
    {
        var mgr = new MainToolbarManager();
        var callLog = new List<string>();

        // Global entry (null perspective)
        mgr.RegisterEntry("global", 0, 64f, () => callLog.Add("global"), perspective: null);
        // Combat-only entry
        mgr.RegisterEntry("combat", 1, 64f, () => callLog.Add("combat"), perspective: "combat");

        // Rendering "combat" — both should fire.
        // BATCH-25: RenderInline inside the menu bar.
        using (var fixture = new ImGuiTestFixture())
        {
            fixture.NewFrame();
            ImGuiNET.ImGui.BeginMainMenuBar();
            mgr.RenderInline("combat");
            ImGuiNET.ImGui.EndMainMenuBar();
            fixture.Render();
        }

        Assert.Equal(new[] { "global", "combat" }, callLog);

        // Rendering "strategic" — only global should fire.
        callLog.Clear();
        using (var fixture = new ImGuiTestFixture())
        {
            fixture.NewFrame();
            ImGuiNET.ImGui.BeginMainMenuBar();
            mgr.RenderInline("strategic");
            ImGuiNET.ImGui.EndMainMenuBar();
            fixture.Render();
        }

        Assert.Equal(new[] { "global" }, callLog);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MTB-P1-T1.C4: Height = max declaredHeight over ALL registered entries
    //                (NOT just visible ones), regardless of perspective
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Height_IsMaxDeclaredOverAllRegistered_RegardlessOfCurrentPerspective()
    {
        var mgr = new MainToolbarManager();

        // 64 px global entry
        mgr.RegisterEntry("btn1", 0, 64f, () => { }, perspective: null);
        Assert.Equal(64f, mgr.Height);

        // 80 px entry bound to perspective "X"
        mgr.RegisterEntry("btn2", 1, 80f, () => { }, perspective: "X");
        Assert.Equal(80f, mgr.Height); // max over all — not just one perspective

        // Even when rendering a completely unrelated perspective "Y",
        // the height must still be 80 (jitter-free guarantee).
        // BATCH-25: RenderInline inside the menu bar.
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        ImGuiNET.ImGui.BeginMainMenuBar();
        mgr.RenderInline("Y");
        ImGuiNET.ImGui.EndMainMenuBar();
        fixture.Render();

        Assert.Equal(80f, mgr.Height);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MTB-P1-T1.C5: Separator is registered and participates in ordering
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Separator_RegisteredAndOrdered()
    {
        var mgr = new MainToolbarManager();

        mgr.RegisterEntry("A", 10, 64f, () => { });
        mgr.RegisterSeparator("sep1", 20, perspective: null);
        mgr.RegisterEntry("B", 30, 64f, () => { });

        // Use the headless-test seam — no ImGui needed for ordering verification.
        var plan = mgr.GetVisibleItemPlan("");

        Assert.Equal(3, plan.Count);
        Assert.Equal("A", plan[0].Id);
        Assert.False(plan[0].IsSeparator);

        Assert.Equal("sep1", plan[1].Id);
        Assert.True(plan[1].IsSeparator);

        Assert.Equal("B", plan[2].Id);
        Assert.False(plan[2].IsSeparator);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Additional: Verify separator participates inline in render via recording
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Separator_RenderPlan_RespectsSortOrder()
    {
        var mgr = new MainToolbarManager();

        mgr.RegisterSeparator("s", 50, perspective: null);
        mgr.RegisterEntry("first", 10, 64f, () => { });
        mgr.RegisterEntry("last", 100, 64f, () => { });

        var plan = mgr.GetVisibleItemPlan("");

        Assert.Equal(3, plan.Count);
        // Sorted by SortOrder regardless of registration order
        Assert.Equal("first", plan[0].Id);
        Assert.False(plan[0].IsSeparator);
        Assert.Equal(10, plan[0].SortOrder);

        Assert.Equal("s", plan[1].Id);
        Assert.True(plan[1].IsSeparator);
        Assert.Equal(50, plan[1].SortOrder);

        Assert.Equal("last", plan[2].Id);
        Assert.False(plan[2].IsSeparator);
        Assert.Equal(100, plan[2].SortOrder);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Additional: Separators are perspective-filtered like entries
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Separator_PerspectiveFiltered_LikeEntries()
    {
        var mgr = new MainToolbarManager();

        mgr.RegisterEntry("global", 10, 64f, () => { }, perspective: null);
        mgr.RegisterSeparator("sep_combat", 20, perspective: "combat");
        mgr.RegisterEntry("combat", 30, 64f, () => { }, perspective: "combat");

        // When rendering "strategic": only the global entry, no separator
        var planStrategic = mgr.GetVisibleItemPlan("strategic");
        Assert.Single(planStrategic);
        Assert.Equal("global", planStrategic[0].Id);

        // When rendering "combat": all three
        var planCombat = mgr.GetVisibleItemPlan("combat");
        Assert.Equal(3, planCombat.Count);
        Assert.Equal("global", planCombat[0].Id);
        Assert.Equal("sep_combat", planCombat[1].Id);
        Assert.True(planCombat[1].IsSeparator);
        Assert.Equal("combat", planCombat[2].Id);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Additional: Null delegate throws ArgumentNullException
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterEntry_NullDelegate_ThrowsArgumentNullException()
    {
        var mgr = new MainToolbarManager();

        Assert.Throws<ArgumentNullException>(() =>
            mgr.RegisterEntry("x", 0, 64f, null!));
    }
}
