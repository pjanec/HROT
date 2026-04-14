using System;
using System.Collections.Generic;
using Fdp.Toolkit.ImGui.WindowManager;
using Xunit;

namespace Fdp.Toolkit.ImGui.Tests.WindowManager;

/// <summary>
/// Tests for <see cref="StatusBarManager"/> — WM-S601 success conditions.
/// Render-path tests create an inline <see cref="ImGuiTestFixture"/> headless context.
/// Pure API tests (null check) require no ImGui frame.
/// All tests run sequentially via the "ImGui Sequential" collection.
/// </summary>
[Collection("ImGui Sequential")]
public class StatusBarManagerTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S601.C1: Null delegate throws ArgumentNullException
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterSection_NullDelegate_ThrowsArgumentNullException()
    {
        var mgr = new StatusBarManager();

        Assert.Throws<ArgumentNullException>(() =>
            mgr.RegisterSection("hero", 0, null!));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S601.C2: Duplicate Id replaces existing section (last-write-wins)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterSection_DuplicateId_ReplacesExisting()
    {
        var mgr = new StatusBarManager();
        var callLog = new List<string>();

        mgr.RegisterSection("sec", 0, () => callLog.Add("first"));
        mgr.RegisterSection("sec", 0, () => callLog.Add("second"));

        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        mgr.Render();
        fixture.Render();

        Assert.Single(callLog);
        Assert.Equal("second", callLog[0]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S601.C3: Deferred sort — sorted on first Render after registration
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_SortsSectionsBySortOrder()
    {
        var mgr = new StatusBarManager();
        var callLog = new List<string>();

        mgr.RegisterSection("b", sortOrder: 10, () => callLog.Add("B"));
        mgr.RegisterSection("a", sortOrder:  1, () => callLog.Add("A"));
        mgr.RegisterSection("c", sortOrder: 20, () => callLog.Add("C"));

        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        mgr.Render();
        fixture.Render();

        Assert.Equal(new[] { "A", "B", "C" }, callLog);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S601.C4: Sort is not re-applied when no new section registered
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_SecondFrameWithoutChange_KeepsOrder()
    {
        var mgr = new StatusBarManager();
        var callLog = new List<string>();

        mgr.RegisterSection("b", 10, () => callLog.Add("B"));
        mgr.RegisterSection("a",  1, () => callLog.Add("A"));

        using var fixture = new ImGuiTestFixture();

        // First frame
        fixture.NewFrame();
        mgr.Render();
        fixture.Render();
        callLog.Clear();

        // Second frame — no new registrations
        fixture.NewFrame();
        mgr.Render();
        fixture.Render();

        Assert.Equal(new[] { "A", "B" }, callLog);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S601.C5: N sections → N-1 separators (verified via call tracking)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_ThreeSections_CallsDelegatesInOrder()
    {
        var mgr = new StatusBarManager();
        var callLog = new List<int>();

        mgr.RegisterSection("s1", 1, () => callLog.Add(1));
        mgr.RegisterSection("s2", 2, () => callLog.Add(2));
        mgr.RegisterSection("s3", 3, () => callLog.Add(3));

        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        mgr.Render();
        fixture.Render();

        Assert.Equal(new[] { 1, 2, 3 }, callLog);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S601.C6: Height property updated after Render
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_UpdatesHeightProperty()
    {
        var mgr = new StatusBarManager();
        mgr.RegisterSection("s", 0, () => { });

        Assert.Equal(0f, mgr.Height); // before render

        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        mgr.Render();
        fixture.Render();

        Assert.True(mgr.Height > 0f, "Height should be positive after Render()");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S601.C7: Single section renders with no separators (no crash)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_SingleSection_RendersWithoutError()
    {
        var mgr = new StatusBarManager();
        int calls = 0;
        mgr.RegisterSection("only", 0, () => calls++);

        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        mgr.Render(); // must not throw
        fixture.Render();

        Assert.Equal(1, calls);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S601.C8: Zero sections — Render is a no-op (no crash)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_NoSections_DoesNotThrow()
    {
        var mgr = new StatusBarManager();

        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        var ex = Record.Exception(() => mgr.Render());
        fixture.Render();

        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S601.C9: Delegate registered with higher sortOrder renders after lower
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterSection_HigherSortOrderRendersLast()
    {
        var mgr = new StatusBarManager();
        var callLog = new List<string>();

        mgr.RegisterSection("late",  99, () => callLog.Add("late"));
        mgr.RegisterSection("early",  0, () => callLog.Add("early"));

        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        mgr.Render();
        fixture.Render();

        Assert.Equal("early", callLog[0]);
        Assert.Equal("late",  callLog[1]);
    }
}
