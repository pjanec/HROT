using System.Collections.Generic;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Tests for the <see cref="WM.MainToolbar"/> property and its integration
/// with <see cref="WM.Render"/> — MTB-P1-T3 success conditions.
/// </summary>
[Collection("ImGui Sequential")]
public class WindowManagerMainToolbarTests
{
    private readonly IconAtlas _atlas = new(new System.IntPtr(1), 256f, 256f, 16f);

    private WM CreateManager() => new(_atlas);

    // ═══════════════════════════════════════════════════════════════════════════
    // MTB-P1-T3.C1: MainToolbar property resolves — non-null and stable
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MainToolbar_PropertyResolves()
    {
        var wm = CreateManager();

        var tb1 = wm.MainToolbar;
        var tb2 = wm.MainToolbar;

        Assert.NotNull(tb1);
        Assert.Same(tb1, tb2);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MTB-P1-T3.C2: WindowManager.Render invokes MainToolbar render delegates
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_InvokesMainToolbar()
    {
        var wm = CreateManager();
        var callLog = new List<string>();

        // Register a recording entry on the toolbar (mirrors how StatusBar tests
        // verify rendering via recording delegates).
        wm.MainToolbar.RegisterEntry("test_entry", 0, 64f, () => callLog.Add("rendered"));

        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Single(callLog);
        Assert.Equal("rendered", callLog[0]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MTB-P1-T3.C3: MainToolbar renders with the current perspective
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_InvokesMainToolbar_WithCurrentPerspective()
    {
        var wm = CreateManager();
        wm.SwitchPerspective("Combat");
        var callLog = new List<string>();

        // Global entry (always rendered) + perspective-bound entry
        wm.MainToolbar.RegisterEntry("global", 0, 64f, () => callLog.Add("global"), perspective: null);
        wm.MainToolbar.RegisterEntry("combat", 1, 64f, () => callLog.Add("combat"), perspective: "Combat");
        wm.MainToolbar.RegisterEntry("strategic", 2, 64f, () => callLog.Add("strategic"), perspective: "Strategic");

        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        // "Combat" is current — global + combat should render, strategic should not.
        Assert.Equal(2, callLog.Count);
        Assert.Contains("global", callLog);
        Assert.Contains("combat", callLog);
        Assert.DoesNotContain("strategic", callLog);
    }
}
