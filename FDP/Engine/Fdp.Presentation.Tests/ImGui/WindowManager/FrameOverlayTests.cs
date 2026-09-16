using System;
using System.Collections.Generic;
using Fdp.Presentation.Icons;
using Xunit;

using ImGuiApi = ImGuiNET.ImGui;
using WM = Fdp.Presentation.WindowManager.WindowManager;
using Fdp.Presentation.WindowManager;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Batch 89 (89a) — the per-frame overlay slot.
///
/// <para>
/// WHY THIS SLOT EXISTS, stated once so nobody re-derives it. A modal cannot be drawn from a
/// window's client area: <c>ManagedWindow.Render</c> returns early when the window is closed and
/// again when it belongs to another perspective, so the dialog would vanish with its host window.
/// Nor can it be a line in the composition root: there are three perspective registrars, which is
/// three lines to forget — the exact shape this programme keeps finding. The final slot after all
/// windows and the status bar is documented for that purpose ("so the modal overlays all other
/// windows") and until now it had exactly one occupant, hard-wired.
/// </para>
///
/// <para>
/// WHAT THESE RAILS ASK: the CONSTRUCTED manager, driven through a real ImGui frame. They prove the
/// slot invokes what is registered in it. They deliberately do NOT prove that any particular caller
/// registers anything — that half belongs to the caller and is railed in Hrot.Editor.AiShared.Tests
/// (<c>TheEditDialogReachesTheFrameTests</c>). Neither half alone is worth anything: a slot nobody
/// fills draws nothing, and a registration into a slot nobody invokes draws nothing.
/// </para>
/// </summary>
[Collection("ImGui Sequential")]
public class FrameOverlayTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    public void Dispose() => _atlas.Dispose();

    private WM CreateManager() => new(_atlas);

    /// <summary>Counts its own invocations, and records the frame order relative to a window.</summary>
    private sealed class SpyWindow : ManagedWindow
    {
        private readonly List<string> _log;
        public SpyWindow(List<string> log) : base("spy_win", "spy_win", "any", WindowScope.Global)
        {
            _log = log;
            IsOpen = true;
        }
        protected override void DrawClientArea()
        {
            _log.Add("window");
            ImGuiApi.Text("content");
        }
    }

    // ══ the slot invokes what is in it ══════════════════════════════════════

    /// <summary>
    /// THE rail. A registered overlay is invoked once per frame, by the real <c>Render</c> path.
    /// </summary>
    [Fact]
    public void ARegisteredOverlayIsDrawnEachFrame()
    {
        using var fixture = new ImGuiTestFixture();
        var wm    = CreateManager();
        int calls = 0;

        wm.RegisterFrameOverlay(() => calls++);

        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Equal(1, calls);

        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Equal(2, calls);
    }

    /// <summary>
    /// The negative control. Without it the rail above could pass against a Render that invokes
    /// something else entirely — and this is the state the editor actually shipped in.
    /// </summary>
    [Fact]
    public void AnUnregisteredOverlayIsNeverDrawn()
    {
        using var fixture = new ImGuiTestFixture();
        var wm    = CreateManager();
        int calls = 0;
        Action overlay = () => calls++;   // built, never registered — Batch 87's defect in miniature

        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Equal(0, calls);
        Assert.DoesNotContain(overlay, wm.FrameOverlays);
    }

    /// <summary>
    /// The overlay draws AFTER every window. That ordering is the reason this slot was chosen: a
    /// modal must overlay the windows, and ImGui draws in call order.
    /// </summary>
    [Fact]
    public void OverlaysAreDrawnAfterAllWindows()
    {
        using var fixture = new ImGuiTestFixture();
        var wm  = CreateManager();
        var log = new List<string>();

        wm.RegisterWindow(new SpyWindow(log));
        wm.RegisterFrameOverlay(() => log.Add("overlay"));

        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Equal(new[] { "window", "overlay" }, log);
    }

    /// <summary>
    /// An overlay is drawn even when every window is closed. This is the property a modal needs and
    /// a window cannot give it: ManagedWindow.Render returns early on !IsOpen, so a dialog hosted in
    /// a client area disappears exactly when the user closes the panel they opened it from.
    /// </summary>
    [Fact]
    public void AnOverlayIsDrawnEvenWhenEveryWindowIsClosed()
    {
        using var fixture = new ImGuiTestFixture();
        var wm  = CreateManager();
        var log = new List<string>();

        var win = new SpyWindow(log) { IsOpen = false };
        wm.RegisterWindow(win);
        wm.RegisterFrameOverlay(() => log.Add("overlay"));

        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Equal(new[] { "overlay" }, log);
    }

    /// <summary>
    /// And even when the current perspective is not the window's. A modal that survives a perspective
    /// switch is correct — its own open-state is what gates it, not the panel it was opened from.
    /// </summary>
    [Fact]
    public void AnOverlayIsDrawnAcrossAPerspectiveSwitch()
    {
        using var fixture = new ImGuiTestFixture();
        var wm  = CreateManager();
        int calls = 0;

        var win = new SpyWindow(new List<string>());
        wm.RegisterWindow(win);
        wm.RegisterFrameOverlay(() => calls++);
        wm.SwitchPerspective("some_other_perspective");

        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Equal(1, calls);
    }

    // ══ registration behaviour ══════════════════════════════════════════════

    /// <summary>
    /// Idempotent by delegate equality. A registrar may be registered against one manager more than
    /// once; a second subscription would draw the same modal twice per frame, which for a popup means
    /// opening it under its own id twice.
    /// </summary>
    [Fact]
    public void RegisteringTheSameMethodGroupTwiceDrawsItOnce()
    {
        using var fixture = new ImGuiTestFixture();
        var wm     = CreateManager();
        var target = new Counter();

        wm.RegisterFrameOverlay(target.Draw);
        wm.RegisterFrameOverlay(target.Draw);   // same target, same method

        Assert.Single(wm.FrameOverlays);

        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Equal(1, target.Calls);
    }

    private sealed class Counter
    {
        public int Calls { get; private set; }
        public void Draw() => Calls++;
    }

    /// <summary>Two distinct targets are two overlays — idempotence must not collapse them.</summary>
    [Fact]
    public void TwoDifferentTargetsAreTwoOverlays()
    {
        var wm = CreateManager();
        var a  = new Counter();
        var b  = new Counter();

        wm.RegisterFrameOverlay(a.Draw);
        wm.RegisterFrameOverlay(b.Draw);

        Assert.Equal(2, wm.FrameOverlays.Count);
    }

    /// <summary>Overlays are drawn in registration order — the same rule the window list follows.</summary>
    [Fact]
    public void OverlaysAreDrawnInRegistrationOrder()
    {
        using var fixture = new ImGuiTestFixture();
        var wm  = CreateManager();
        var log = new List<string>();

        wm.RegisterFrameOverlay(() => log.Add("first"));
        wm.RegisterFrameOverlay(() => log.Add("second"));

        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Equal(new[] { "first", "second" }, log);
    }

    /// <summary>A null overlay throws rather than being silently dropped.</summary>
    [Fact]
    public void ANullOverlayThrows()
        => Assert.Throws<ArgumentNullException>(() => CreateManager().RegisterFrameOverlay(null!));

    /// <summary>
    /// An overlay that registers another one mid-frame does not corrupt the current frame's
    /// enumeration. Cheap to guarantee (iterate a copy) and expensive to discover.
    /// </summary>
    [Fact]
    public void AnOverlayMayRegisterAnotherWithoutBreakingTheFrame()
    {
        using var fixture = new ImGuiTestFixture();
        var wm  = CreateManager();
        int late = 0;

        wm.RegisterFrameOverlay(() => wm.RegisterFrameOverlay(() => late++));

        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Equal(0, late);              // registered during the frame — drawn from the next one
        Assert.Equal(2, wm.FrameOverlays.Count);

        fixture.NewFrame();
        wm.Render();
        fixture.Render();

        Assert.Equal(1, late);
    }
}
