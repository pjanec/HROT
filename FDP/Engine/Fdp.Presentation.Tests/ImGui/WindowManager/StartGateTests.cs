using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Orchestration;
using Xunit;

using ImGuiApi = ImGuiNET.ImGui;
using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// ⭐⭐ <c>CE-4096</c> (AQ108 §2.6 / D16) — the start gate, the Idle-only menu and the start surface. Backend's CE-4094 rails (remember /
/// refuse / leave) sit in <c>WindowManagerTests</c>; these are the layer on top: WHEN Start is entered and left, what the bar and the
/// surface offer while it shows.
/// </summary>
[Collection("ImGui Sequential")]
public class StartGateTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    public void Dispose() => _atlas.Dispose();

    private sealed class CountingWindow : ManagedWindow
    {
        public int DrawCount { get; private set; }
        public CountingWindow(string id, string perspective, WindowScope scope) : base(id, id, perspective, scope) { IsOpen = true; }
        protected override void DrawClientArea() { DrawCount++; ImGuiApi.Text("content"); }
    }

    private static ClusterRest Rest(ClusterState? state, bool gone = false, bool can = true) => new(state, gone, can);

    /// <summary>A manager with the real start surface and its gate, the way the runner composes them; current perspective = SimHost.</summary>
    private (WM wm, StartGate gate, StartSurfaceWindow surface) Compose()
    {
        var wm = new WM(_atlas);
        wm.RegisterWindow(new CountingWindow("w-ig", "IG", WindowScope.PerspectiveBound));
        wm.RegisterWindow(new CountingWindow("w-sim", "SimHost", WindowScope.PerspectiveBound));
        var gate = new StartGate(wm);
        var surface = new StartSurfaceWindow(wm.GlobalMenu, () => gate.Variant);
        wm.RegisterWindow(surface);
        wm.SwitchPerspective("SimHost");
        return (wm, gate, surface);
    }

    // ── 1. the gate ────────────────────────────────────────────────────────────

    [Fact]
    public void Gate_IdleHeldLessThanTheSettle_DoesNotEnterStart()
    {
        var (wm, gate, _) = Compose();
        gate.Tick(10.0, Rest(ClusterState.Idle));
        gate.Tick(10.0 + StartGate.SettleSeconds - 0.01, Rest(ClusterState.Idle));
        Assert.False(wm.IsStartShowing);
        Assert.Equal("SimHost", wm.CurrentPerspective);
    }

    [Fact]
    public void Gate_IdleHeldForTheSettle_EntersStart_RememberingThePreviousPerspective()
    {
        var (wm, gate, _) = Compose();
        gate.Tick(10.0, Rest(ClusterState.Idle));
        gate.Tick(10.0 + StartGate.SettleSeconds, Rest(ClusterState.Idle));
        Assert.True(wm.IsStartShowing);
        Assert.Equal("SimHost", wm.RememberedPerspective);
    }

    [Fact]
    public void Gate_TheTwoRequestLoad_IdleThenLoadingWithinTheSettle_NeverEntersStart()
    {
        // CE-295: the ordinary load is Idle then the load — every host sees a real Idle in between (≈0.1–0.3 s).
        var (wm, gate, _) = Compose();
        int changes = 0;
        wm.OnPerspectiveChanged += (_, _) => changes++;

        gate.Tick(5.00, Rest(ClusterState.OperatingEdit));
        gate.Tick(5.05, Rest(ClusterState.Idle));          // the pass-through Idle …
        gate.Tick(5.20, Rest(ClusterState.Idle));
        gate.Tick(5.30, Rest(ClusterState.LoadingEdit));   // … the load arrives within 0.3 s
        gate.Tick(5.60, Rest(ClusterState.LoadingEdit));
        gate.Tick(5.90, Rest(ClusterState.OperatingEdit));

        Assert.Equal(0, changes);
        Assert.False(wm.IsStartShowing);
        // ⭐ and the settle clock restarted: a later Idle must be held a FULL settle again
        gate.Tick(6.0, Rest(ClusterState.Idle));
        gate.Tick(6.3, Rest(ClusterState.Idle));
        Assert.False(wm.IsStartShowing);
    }

    [Fact]
    public void Gate_AnyNonIdleStateWhileStartShows_LeavesAndRestoresTheRememberedPerspective()
    {
        var (wm, gate, _) = Compose();
        gate.Tick(1.0, Rest(ClusterState.Idle));
        gate.Tick(1.6, Rest(ClusterState.Idle));
        Assert.True(wm.IsStartShowing);

        gate.Tick(2.0, Rest(ClusterState.LoadingEdit));

        Assert.False(wm.IsStartShowing);
        Assert.Equal("SimHost", wm.CurrentPerspective);
        Assert.Null(wm.RememberedPerspective);
        Assert.True(wm.TrySwitchPerspective("IG", out _));   // the full switcher is back
    }

    [Fact]
    public void Gate_MasterGone_ShowsStartWithTheServerLostVariant()
    {
        var (wm, gate, _) = Compose();
        gate.Tick(1.0, Rest(ClusterState.OperatingLive, gone: true));
        gate.Tick(1.5, Rest(ClusterState.OperatingLive, gone: true));   // MasterGone rests regardless of the last state
        Assert.True(wm.IsStartShowing);
        Assert.Equal(StartVariant.ServerLost, gate.Variant);
    }

    [Fact]
    public void Gate_NoStateYet_ShowsStart_WaitingWhenItCannotOperate_ActionsWhenItCan()
    {
        var (wm, gate, _) = Compose();
        gate.Tick(1.0, Rest(null, can: false));
        gate.Tick(1.5, Rest(null, can: false));
        Assert.True(wm.IsStartShowing);
        Assert.Equal(StartVariant.WaitingForServer, gate.Variant);

        gate.Tick(1.6, Rest(null, can: true));
        Assert.Equal(StartVariant.Actions, gate.Variant);
        Assert.True(wm.IsStartShowing);                      // still resting: only the surface's content changed
    }

    // ── 2. while Start shows: the switcher refuses, the bar holds only the Idle-allowed items ────────────

    private static void RegisterMenu(WM wm, Action? onOpen = null)
    {
        wm.GlobalMenu.RegisterItem("File/Open Scenario", () => onOpen?.Invoke(), allowedInIdle: true);
        wm.GlobalMenu.RegisterItem("File/Save Scenario", () => { });                       // not allowed in Idle
        wm.GlobalMenu.RegisterItem("Edit/Undo", () => { });
    }

    private static List<string> VisibleItems(WM wm)
        => wm.GlobalMenu.BuildViewModel(wm.CurrentPerspective).Items.Where(i => i.Visible && i.Kind == "item").Select(i => i.Path).ToList();

    [Fact]
    public void WhileStartShows_TheSwitcherRefuses_AndTheBarModelHoldsOnlyAllowedInIdleItems_ThenTheFullBarIsBack()
    {
        var (wm, gate, _) = Compose();
        RegisterMenu(wm);
        Assert.Contains("File/Save Scenario", VisibleItems(wm));
        Assert.Contains("Edit/Undo", VisibleItems(wm));

        gate.Tick(1.0, Rest(ClusterState.Idle));
        gate.Tick(1.5, Rest(ClusterState.Idle));
        Assert.True(wm.IsStartShowing);

        Assert.False(wm.TrySwitchPerspective("IG", out var reason));       // backend's refusal, through the gate's Start
        Assert.NotNull(reason);
        var idle = VisibleItems(wm);
        Assert.Contains("File/Open Scenario", idle);
        Assert.Contains("Help/About", idle);                                // Help stays (a contributed DTO carries the flag)
        Assert.DoesNotContain("File/Save Scenario", idle);
        Assert.DoesNotContain("Edit/Undo", idle);
        Assert.DoesNotContain(idle, p => p.StartsWith("View/Windows") || p.StartsWith("Windows/") || p.StartsWith("View/Perspective"));   // the shell's own contributors are pruned too

        gate.Tick(2.0, Rest(ClusterState.OperatingEdit));
        var full = VisibleItems(wm);
        Assert.Contains("File/Save Scenario", full);
        Assert.Contains("Edit/Undo", full);
    }

    [Fact]
    public void StartIsNeverOfferedToAUser_ButStaysInTheClaimedSetForTheGate()
    {
        var (wm, _, _) = Compose();
        Assert.Contains(WM.StartPerspectiveName, wm.GetPerspectives());               // A0 needs it claimed
        Assert.DoesNotContain(WM.StartPerspectiveName, wm.GetSelectablePerspectives());
        Assert.DoesNotContain(wm.BuildPerspectiveMenuModel(), e => e.Perspective == WM.StartPerspectiveName);
    }

    // ── 3. the start surface owns no action ────────────────────────────────────

    [Fact]
    public void TheSurface_InvokesTheSameBindingTheMenuItemInvokes_Once()
    {
        var (wm, _, _) = Compose();
        int ran = 0;
        RegisterMenu(wm, () => ran++);
        var surface = new StartSurfaceWindow(wm.GlobalMenu, () => StartVariant.Actions);

        var model = surface.Model();
        Assert.Equal(StartVariant.Actions, model.Variant);
        var button = Assert.Single(model.Buttons, b => b.Label == "Open Scenario");
        Assert.DoesNotContain(model.Buttons, b => b.Label == "Save Scenario");   // only what the menu marks allowed in Idle
        button.Invoke();

        Assert.Equal(1, ran);
        // and the menu's own node is the one that ran: the click path IS the binding
        wm.GlobalMenu.Root.Children["File"].Children["Open Scenario"].OnClick!();
        Assert.Equal(2, ran);
    }

    [Fact]
    public void TheSurface_WaitingVariants_ShowOnlyTheirSentence()
    {
        var (wm, _, _) = Compose();
        RegisterMenu(wm);
        var waiting = new StartSurfaceWindow(wm.GlobalMenu, () => StartVariant.WaitingForServer).Model();
        var lost = new StartSurfaceWindow(wm.GlobalMenu, () => StartVariant.ServerLost).Model();
        Assert.Equal("Waiting for the server to load a scenario", waiting.Text);
        Assert.Empty(waiting.Buttons);
        Assert.Equal("Server lost — waiting", lost.Text);
        Assert.Empty(lost.Buttons);

        // a host that registers nothing of its own still offers the shell's Help (allowed in Idle) — and nothing else
        var bare = new WM(_atlas);
        var onlyShell = new StartSurfaceWindow(bare.GlobalMenu, () => StartVariant.Actions).Model();
        Assert.Equal(new[] { "About" }, onlyShell.Buttons.Select(b => b.Label).ToArray());
    }

    [Fact]
    public void TheSurface_ContributedItemsCarryTheFlagOnTheirDto_AndInvokeThroughTheContributor()
    {
        var wm = new WM(_atlas);
        var seen = new List<int>();
        wm.GlobalMenu.RegisterContributor("host/files", () => new[]
        {
            new ContextMenuItemDto { Label = "File", Children = new[]
            {
                new ContextMenuItemDto { Id = 7, Label = "Open Recording", AllowedInIdle = true },
                new ContextMenuItemDto { Id = 8, Label = "Close" },
            } },
        }, seen.Add);

        var buttons = new StartSurfaceWindow(wm.GlobalMenu, () => StartVariant.Actions).Model().Buttons;
        var open = Assert.Single(buttons, b => b.Label == "Open Recording");
        Assert.DoesNotContain(buttons, b => b.Label == "Close");
        open.Invoke();
        Assert.Equal(new[] { 7 }, seen);
    }

    /// <summary>
    /// ⭐ CE-1441 (audit #20) — ONE rule decides "allowed in Idle" (<c>GlobalMenuRegistry.IsAllowedInIdle</c>): the bar (tree leaves via <c>Offers</c>, contributed
    /// items via <c>PruneToIdle</c>) and the start surface (<c>AllowedInIdleItems</c>) can never disagree — an item is on the Idle bar iff the surface offers it.
    /// A contributed item with the flag UNSET is not allowed (only an explicit <c>true</c> is).
    /// 🔴 Red-proof: make <c>IsAllowedInIdle</c> return <c>allowedInIdle != false</c> ⇒ the unset contributed item shows on the bar and the truth-table row fails.
    /// </summary>
    [Fact]
    public void CE1441_OneIdlePredicate_TheBarAndTheSurfaceAgree_ForTreeAndContributedItems()
    {
        Assert.True(GlobalMenuRegistry.IsAllowedInIdle(true));
        Assert.False(GlobalMenuRegistry.IsAllowedInIdle(false));
        Assert.False(GlobalMenuRegistry.IsAllowedInIdle(null));

        var wm = new WM(_atlas);
        RegisterMenu(wm);
        wm.GlobalMenu.RegisterContributor("host/files", () => new[]
        {
            new ContextMenuItemDto { Label = "Host", Children = new[]
            {
                new ContextMenuItemDto { Id = 1, Label = "Yes", AllowedInIdle = true },
                new ContextMenuItemDto { Id = 2, Label = "No", AllowedInIdle = false },
                new ContextMenuItemDto { Id = 3, Label = "Unset" },
            } },
        }, _ => { });
        wm.GlobalMenu.IdleOnlyWhen = () => true;

        var open = wm.GlobalMenu.Root.Children["File"].Children["Open Scenario"];
        var save = wm.GlobalMenu.Root.Children["File"].Children["Save Scenario"];
        Assert.True(wm.GlobalMenu.Offers(open, wm.CurrentPerspective));
        Assert.False(wm.GlobalMenu.Offers(save, wm.CurrentPerspective));

        var bar = VisibleItems(wm);
        var surface = wm.GlobalMenu.AllowedInIdleItems().Select(a => a.Path).ToList();
        foreach (var label in new[] { "Open Scenario", "Yes", "No", "Unset", "Save Scenario" })
            Assert.Equal(surface.Any(p => p.EndsWith(label)), bar.Any(p => p.EndsWith(label)));   // the bar shows it iff the surface offers it
        Assert.Contains(bar, p => p.EndsWith("Yes"));
        Assert.DoesNotContain(bar, p => p.EndsWith("No") || p.EndsWith("Unset"));
    }

    // ── 4. no other panel shows ────────────────────────────────────────────────

    [Fact]
    public void WhileStartShows_GlobalAndPinnedWindowsAreHidden_AndTheSurfaceDraws_ThenAllReturn()
    {
        using var fixture = new ImGuiTestFixture();
        var (wm, gate, surface) = Compose();
        var global = new CountingWindow("g", "any", WindowScope.Global);
        var pinned = new CountingWindow("p", "IG", WindowScope.PerspectiveBound) { IsPinned = true };
        wm.RegisterWindow(global);
        wm.RegisterWindow(pinned);

        fixture.NewFrame(); wm.Render(); fixture.Render();
        Assert.Equal(1, global.DrawCount);
        Assert.Equal(1, pinned.DrawCount);

        gate.Tick(1.0, Rest(ClusterState.Idle));
        gate.Tick(1.5, Rest(ClusterState.Idle));
        Assert.True(wm.IsStartShowing);
        fixture.NewFrame(); wm.Render(); fixture.Render();
        Assert.Equal(1, global.DrawCount);            // ⭐ Global-scope draws in EVERY perspective by default — Start hides it
        Assert.Equal(1, pinned.DrawCount);

        gate.Tick(2.0, Rest(ClusterState.OperatingEdit));
        fixture.NewFrame(); wm.Render(); fixture.Render();
        Assert.Equal(2, global.DrawCount);
        Assert.Equal(2, pinned.DrawCount);
    }

    [Fact]
    public void ExactlyOneWindowClaimsStart()
    {
        var (wm, _, surface) = Compose();
        var claimants = wm.RegisteredWindowIds
            .Select(id => { wm.TryGetWindow(id, out var w); return w!; })
            .Where(w => w.OwningPerspective == WM.StartPerspectiveName).ToList();
        Assert.Same(surface, Assert.Single(claimants));
    }
}
