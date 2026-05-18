using System;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using ImGuiNET;
using Xunit;

using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Tests for <see cref="ManagedWindow"/> — WM-S201, WM-S202, WM-S203 success conditions.
/// Tests that require ImGui calls (Begin/End, draw-list) run inside a
/// <see cref="ImGuiTestFixture"/> headless context placed in the shared sequential collection.
/// Tests that verify early-exit logic (no ImGui calls) do not need the fixture.
/// </summary>
[Collection("ImGui Sequential")]
public class ManagedWindowTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static IconAtlas CreateAtlas() =>
        new IconAtlas(new IntPtr(1), 256f, 256f, 16f);

    /// <summary>
    /// Minimal concrete subclass for testing the abstract <see cref="ManagedWindow"/> base.
    /// Tracks whether <see cref="DrawClientArea"/> and <see cref="DrawLocalMenuBar"/> were called.
    /// </summary>
    private class TestWindow : ManagedWindow
    {
        private readonly bool _hasMenuBar;

        public bool DrawClientAreaCalled { get; private set; }
        public bool DrawLocalMenuBarCalled { get; private set; }

        protected override bool HasMenuBar => _hasMenuBar;

        public TestWindow(
            string id,
            string owningPerspective,
            WindowScope scope,
            bool hasMenuBar = false,
            string title = "Test Window")
            : base(id, title, owningPerspective, scope)
        {
            _hasMenuBar = hasMenuBar;
        }

        public void ResetCallFlags()
        {
            DrawClientAreaCalled = false;
            DrawLocalMenuBarCalled = false;
        }

        protected override void DrawClientArea()
        {
            DrawClientAreaCalled = true;
            ImGuiApi.Text("test content");
        }

        protected override void DrawLocalMenuBar()
        {
            DrawLocalMenuBarCalled = true;
        }
    }

    /// <summary>
    /// Subclass that exposes <see cref="HasMenuBar"/> override but uses base
    /// <see cref="DrawLocalMenuBar"/> (default empty) — verifies WM-S203.4.
    /// </summary>
    private class TestWindowDefaultMenuBar : ManagedWindow
    {
        protected override bool HasMenuBar => true;

        public TestWindowDefaultMenuBar(string id) : base(id, "Menu Window", "IG", WindowScope.Global) { }

        protected override void DrawClientArea() => ImGuiApi.Text("content");
    }

    // ── WM-S201: Visibility logic ──────────────────────────────────────────────

    /// <summary>WM-S201 condition 1: Global window, IsOpen=true → DrawClientArea is called.</summary>
    [Fact]
    public void Render_GlobalWindow_Open_DrawsClientArea()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var window = new TestWindow("s201c1", "IG", WindowScope.Global) { IsOpen = true };

        fixture.NewFrame();
        window.Render("ExCon", atlas);
        fixture.Render();

        Assert.True(window.DrawClientAreaCalled);
    }

    /// <summary>WM-S201 condition 2: Global window, IsOpen=false → Render exits early, no ImGui calls.</summary>
    [Fact]
    public void Render_GlobalWindow_Closed_SkipsClientArea()
    {
        // No ImGui context needed — method returns before any Gui call.
        using var atlas = CreateAtlas();
        var window = new TestWindow("s201c2", "IG", WindowScope.Global) { IsOpen = false };

        window.Render("IG", atlas);

        Assert.False(window.DrawClientAreaCalled);
    }

    /// <summary>WM-S201 condition 3: PerspectiveBound, matching perspective, not pinned → visible.</summary>
    [Fact]
    public void Render_PerspectiveBound_MatchingPerspective_NotPinned_DrawsClientArea()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var window = new TestWindow("s201c3", "IG", WindowScope.PerspectiveBound)
        {
            IsOpen = true,
            IsPinned = false
        };

        fixture.NewFrame();
        window.Render("IG", atlas);
        fixture.Render();

        Assert.True(window.DrawClientAreaCalled);
    }

    /// <summary>WM-S201 condition 4: PerspectiveBound, wrong perspective, not pinned → not visible.</summary>
    [Fact]
    public void Render_PerspectiveBound_WrongPerspective_NotPinned_SkipsClientArea()
    {
        // No ImGui context needed — method returns before any Gui call.
        using var atlas = CreateAtlas();
        var window = new TestWindow("s201c4", "IG", WindowScope.PerspectiveBound)
        {
            IsOpen = true,
            IsPinned = false
        };

        window.Render("ExCon", atlas);

        Assert.False(window.DrawClientAreaCalled);
    }

    /// <summary>WM-S201 condition 5: PerspectiveBound, wrong perspective, pinned → visible.</summary>
    [Fact]
    public void Render_PerspectiveBound_WrongPerspective_Pinned_DrawsClientArea()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var window = new TestWindow("s201c5", "IG", WindowScope.PerspectiveBound)
        {
            IsOpen = true,
            IsPinned = true
        };

        fixture.NewFrame();
        window.Render("ExCon", atlas);
        fixture.Render();

        Assert.True(window.DrawClientAreaCalled);
    }

    /// <summary>WM-S201 condition 6: PerspectiveBound, closed → not visible regardless of pin state.</summary>
    [Fact]
    public void Render_PerspectiveBound_Closed_SkipsClientArea()
    {
        // No ImGui context needed — method returns at the IsOpen gate.
        using var atlas = CreateAtlas();
        var window = new TestWindow("s201c6", "IG", WindowScope.PerspectiveBound)
        {
            IsOpen = false,
            IsPinned = true
        };

        window.Render("IG", atlas);

        Assert.False(window.DrawClientAreaCalled);
    }

    /// <summary>WM-S201 condition 7: _focusRequested is cleared after Render() consumes it.</summary>
    [Fact]
    public void Render_AfterRequestFocus_FocusFlagIsCleared()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var window = new TestWindow("s201c7", "IG", WindowScope.Global) { IsOpen = true };
        window.RequestFocus();
        Assert.True(window.FocusRequested); // flag set before render

        fixture.NewFrame();
        window.Render("IG", atlas);
        fixture.Render();

        Assert.False(window.FocusRequested); // flag cleared after render
    }

    /// <summary>WM-S201 condition 8: _focusRequested is false immediately after construction.</summary>
    [Fact]
    public void Construction_FocusFlagIsFalseByDefault()
    {
        var window = new TestWindow("s201c8", "IG", WindowScope.Global);
        Assert.False(window.FocusRequested);
    }

    /// <summary>WM-S201 condition 9: RequestFocus() sets _focusRequested to true.</summary>
    [Fact]
    public void RequestFocus_SetsFocusFlag()
    {
        var window = new TestWindow("s201c9", "IG", WindowScope.Global);
        window.RequestFocus();
        Assert.True(window.FocusRequested);
    }

    /// <summary>WM-S201 condition 10: WindowInternalName follows "{Title}###{Id}" format.</summary>
    [Fact]
    public void WindowInternalName_FollowsExpectedFormat()
    {
        var window = new TestWindow("myUniqueId", "IG", WindowScope.Global, title: "My Panel");
        Assert.Equal("My Panel###myUniqueId", window.WindowInternalName);
    }

    // ── WM-S202: Custom title bar controls ────────────────────────────────────

    /// <summary>WM-S202 condition 1: Global window renders without crash (no pin icon section).</summary>
    [Fact]
    public void Render_GlobalWindow_NoPinIcon_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var window = new TestWindow("s202c1", "IG", WindowScope.Global) { IsOpen = true };

        fixture.NewFrame();
        var ex = Record.Exception(() => window.Render("ExCon", atlas));
        fixture.Render();

        Assert.Null(ex);
    }

    /// <summary>
    /// WM-S202 conditions 5 and 7: Close icon is rendered for both Global and PerspectiveBound
    /// windows without throwing.
    /// </summary>
    [Fact]
    public void Render_BothScopeTypes_CloseIconRendered_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var pbWindow = new TestWindow("s202c7pb", "IG", WindowScope.PerspectiveBound) { IsOpen = true };
        var glWindow = new TestWindow("s202c7gl", "IG", WindowScope.Global) { IsOpen = true };

        fixture.NewFrame();
        var ex1 = Record.Exception(() => pbWindow.Render("IG", atlas));
        var ex2 = Record.Exception(() => glWindow.Render("IG", atlas));
        fixture.Render();

        Assert.Null(ex1);
        Assert.Null(ex2);
    }

    /// <summary>
    /// WM-S202 condition 2: IsPinned unchanged when no click occurs (no side effects on pin state).
    /// In headless mode no actual click is generated, so IsPinned must remain at its initial value.
    /// </summary>
    [Fact]
    public void Render_PerspectiveBound_NoPinClickOccurs_IsPinnedUnchanged()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var window = new TestWindow("s202c2", "IG", WindowScope.PerspectiveBound)
        {
            IsOpen = true,
            IsPinned = true
        };

        fixture.NewFrame();
        window.Render("IG", atlas);
        fixture.Render();

        // No click simulated in headless mode → pin state must remain unchanged.
        Assert.True(window.IsPinned);
    }

    /// <summary>
    /// WM-S202 condition 5: IsOpen unchanged when close button is not clicked.
    /// In headless mode no actual click is generated, so IsOpen must remain true.
    /// </summary>
    [Fact]
    public void Render_OpenWindow_NoCloseClickOccurs_IsOpenUnchanged()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var window = new TestWindow("s202c5", "IG", WindowScope.Global) { IsOpen = true };

        fixture.NewFrame();
        window.Render("IG", atlas);
        fixture.Render();

        Assert.True(window.IsOpen);
    }

    // ── WM-S203: Optional local menu bar ─────────────────────────────────────

    /// <summary>
    /// WM-S203 condition 1: HasMenuBar=false (default) — DrawLocalMenuBar is never called.
    /// </summary>
    [Fact]
    public void Render_WithoutMenuBar_DoesNotCallDrawLocalMenuBar()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var window = new TestWindow("s203c1", "IG", WindowScope.Global, hasMenuBar: false)
        {
            IsOpen = true
        };

        fixture.NewFrame();
        window.Render("IG", atlas);
        fixture.Render();

        Assert.False(window.DrawLocalMenuBarCalled);
    }

    /// <summary>
    /// WM-S203 conditions 2 and 3: HasMenuBar=true — DrawLocalMenuBar is called inside
    /// the BeginMenuBar/EndMenuBar block.
    /// </summary>
    [Fact]
    public void Render_WithMenuBar_CallsDrawLocalMenuBar()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var window = new TestWindow("s203c2", "IG", WindowScope.Global, hasMenuBar: true)
        {
            IsOpen = true
        };

        fixture.NewFrame();
        window.Render("IG", atlas);
        fixture.Render();

        Assert.True(window.DrawLocalMenuBarCalled);
    }

    /// <summary>
    /// WM-S203 condition 4: Default DrawLocalMenuBar implementation does nothing and does not
    /// throw (verified via TestWindowDefaultMenuBar which does not override DrawLocalMenuBar).
    /// </summary>
    [Fact]
    public void Render_DefaultDrawLocalMenuBar_DoesNotThrow()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        var window = new TestWindowDefaultMenuBar("s203c4") { IsOpen = true };

        fixture.NewFrame();
        var ex = Record.Exception(() => window.Render("IG", atlas));
        fixture.Render();

        Assert.Null(ex);
    }

    /// <summary>
    /// WM-S203 condition 5: Subclass override of HasMenuBar=true and DrawLocalMenuBar works;
    /// verified by asserting DrawLocalMenuBarCalled=true (same as condition 2/3 test above,
    /// here using HasMenuBar from the same TestWindow subclass for completeness).
    /// </summary>
    [Fact]
    public void Render_SubclassOverridesMenuBar_DrawLocalMenuBarIsInvoked()
    {
        using var fixture = new ImGuiTestFixture();
        using var atlas = CreateAtlas();
        // TestWindow with hasMenuBar=true overrides HasMenuBar=>true AND DrawLocalMenuBar().
        var window = new TestWindow("s203c5", "IG", WindowScope.PerspectiveBound, hasMenuBar: true)
        {
            IsOpen = true,
            IsPinned = false
        };

        fixture.NewFrame();
        window.Render("IG", atlas);
        fixture.Render();

        Assert.True(window.DrawClientAreaCalled);
        Assert.True(window.DrawLocalMenuBarCalled);
    }
}
