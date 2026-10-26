using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Constants;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.ScenarioEditor.Map;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐⭐ <c>CE-1329</c> (docs/DESIGN_Entity_Brain_View.md §12r BV-65…BV-72) — ONE store of every per-unit debug switch (<see cref="UnitDebugSettings"/>,
/// keyed by network id): the Brain tab's ticks, the map's right-click menu and the HTTP route write it; the unit's <see cref="DebugState"/> is
/// its per-tick copy (rewritten every frame, so a preview's rewind cannot lose a tick); brain gizmo families draw for pinned units only;
/// remembered per scenario. ⛔ SUPERSEDED: <c>UnitDebugControllerTests</c> ("Debug this unit", one debugged unit).
/// </summary>
public sealed class UnitDebugSettingsTests : IDisposable
{
    private readonly EntityRepository _repo = new();

    public UnitDebugSettingsTests()
    {
        _repo.RegisterComponent<BehaviorState>();
        _repo.RegisterComponent<DebugState>();
        _repo.RegisterComponent<NetworkIdentity>();
        _repo.RegisterManagedEvent<PatchDebugStateCommand>();
    }

    public void Dispose() => _repo.Dispose();

    private Entity Unit(long net)
    {
        var e = _repo.CreateEntity();
        _repo.AddComponent(e, new BehaviorState());
        _repo.AddComponent(e, new NetworkIdentity(net));
        return e;
    }

    private void Frame()
    {
        _repo.Bus.SwapBuffers();
        new DebugStatePatchSystem().Execute(_repo, 0.016f);
    }

    private DebugState Debug(Entity e) => _repo.HasComponent<DebugState>(e) ? _repo.GetComponentRO<DebugState>(e) : default;

    /// <summary>⭐ The ticks land in the store and the unit's copy follows it every frame — a rewind that put the copy back (a preview's
    /// Stop restores the captured DebugState) is undone on the next frame. ✅ Red-proof: <c>Apply</c> not called by the patch system ⇒ red.</summary>
    [Fact]
    public void CE1329_TheStoreIsTheOneSwitch_TheUnitsCopyFollowsIt_EvenAfterARewind()
    {
        var a = Unit(1001);
        GizmoPins.Set(_repo, a, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Perception, true);
        AiTraceActions.Set(_repo, a, BehaviorDebugFlags.EnableTraceBuffer, true);
        Frame();
        Assert.True(GizmoPins.IsPinned(_repo, a, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Perception));
        Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Perception, Debug(a).Ai);
        Assert.True((Debug(a).Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0);

        _repo.SetComponent(a, new DebugState());   // what a preview's Stop does: the copy captured at its start comes back
        Frame();
        Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Perception, Debug(a).Ai);
        Assert.True((Debug(a).Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0);

        GizmoPins.Set(_repo, a, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Perception, false);
        AiTraceActions.Set(_repo, a, BehaviorDebugFlags.EnableTraceBuffer, false);
        Frame();
        Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.None, Debug(a).Ai);
        Assert.Equal(0, (int)(Debug(a).Behavior & BehaviorDebugFlags.EnableTraceBuffer));
    }

    /// <summary>⭐ The trace runs while ANY requester asks — the user's switch (remembered), a Brain tab showing the unit, an observe /
    /// breakpoint arming (both temporary); the tab going away never clears what the user switched on. ✅ Red-proof: the viewed request
    /// written as the user's (BrainObservation) ⇒ red.</summary>
    [Fact]
    public void CE1329_TheTraceRunsWhileAnyAsks_ATabClosingNeverClearsTheUsersSwitch()
    {
        long now = 0;
        var o = new BrainObservation(() => now) { Linger = TimeSpan.FromSeconds(1) };
        var viewed = Unit(1001);
        var users = Unit(1002);
        AiTraceActions.Set(_repo, users, BehaviorDebugFlags.EnableTraceBuffer, true);
        o.Viewed(_repo, viewed);
        o.Viewed(_repo, users);
        Frame();
        Assert.True((Debug(viewed).Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0);
        var store = UnitDebugSettings.Of(_repo);
        Assert.False(store.IsTraceSet(1001));                      // the tab's request is not remembered

        now = 2_000; o.Tick(_repo); Frame();                       // no tab drew either for > 1 s
        Assert.Equal(0, (int)(Debug(viewed).Behavior & BehaviorDebugFlags.EnableTraceBuffer));
        Assert.True((Debug(users).Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0, "the user's switch stays");

        store.SetArmed(1001, true); Frame();
        Assert.True(store.TraceOn(1001));
        Assert.True((Debug(viewed).Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0);
        Assert.Empty(store.Remembered.Keys.Where(k => k == 1001));
    }

    /// <summary>⭐ BV-68 — the right-click menu checks the pinned families and the trace, read from the same store; "Debug this unit" is
    /// gone. ✅ Red-proof: the menu built without the pins (Submenu() with no argument) ⇒ red.</summary>
    [Fact]
    public void CE1329_TheMapMenu_ChecksWhatIsPinnedAndTraced_AndOffersNoDebugThisUnit()
    {
        var unit = Unit(1003);
        GizmoPins.Set(_repo, unit, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Eqs, true);
        AiTraceActions.Set(_repo, unit, BehaviorDebugFlags.EnableTraceBuffer, true);
        string json = ContextMenuProjectorGizmo.MenuJsonFor(_repo, unit);
        var items = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsArray();
        var pin = items.Single(i => (string?)i!["label"] == "Pin gizmos")!["children"]!.AsArray();
        Assert.True((bool)pin.Single(c => (string?)c!["label"] == "EQS")!["checked"]!);
        Assert.False((bool?)pin.Single(c => (string?)c!["label"] == "Perception")!["checked"] ?? false);
        var trace = items.Single(i => (string?)i!["label"] == "AI trace")!["children"]!.AsArray();
        Assert.True((bool)trace.Single(c => ((string?)c!["label"])!.StartsWith("Trace"))!["checked"]!);
        Assert.DoesNotContain($"\"id\":{GlobalActionIds.DebugThisUnit},", json);
    }

    /// <summary>⭐ BV-66 — a BRAIN family draws for a pinned unit, selected or not; selecting a unit draws none of it; Path keeps
    /// selected-or-pinned. ✅ Red-proof: the PinnedOnly guard dropped from the policy ⇒ red (the selected unit draws).</summary>
    [Fact]
    public void CE1329_ABrainFamilyDrawsForPinnedUnitsOnly_SelectionDrawsNone()
    {
        _repo.RegisterComponent<Hrot.IG.Components.SelectionState>();
        var selected = Unit(1004);
        var pinned = Unit(1005);
        _repo.AddComponent(selected, new Hrot.IG.Components.SelectionState { IsSelected = true });
        GizmoPins.Set(_repo, pinned, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Perception, true);
        Frame();
        var settings = new Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry();
        var cones = new GizmoFamilyVisibilityPolicy(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Perception);
        Assert.False(cones.IsEntityVisible(_repo, selected));
        Assert.True(cones.IsEntityVisible(_repo, pinned));
        var path = new GizmoFamilyVisibilityPolicy(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path);
        Assert.True(path.IsEntityVisible(_repo, selected));
        Assert.Equal(GizmoScope.PinnedOnly, GizmoFamilies.ScopeOf(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Eqs));
    }

    /// <summary>⭐ BV-70 / BV-71 — remembered per scenario: written on change, cleared when another scenario loads, back when it loads again;
    /// the Settings menu's reset forgets them. ✅ Red-proof: Tick not loading the new scenario's file ⇒ red.</summary>
    [Fact]
    public void CE1329_RememberedPerScenario_BackOnItsNextLoad_AndResetForgets()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hrot-debug-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var memory = new UnitDebugSettingsMemory(() => dir);
            var store = UnitDebugSettings.Of(_repo);
            memory.Tick(_repo, "hill-attack");
            store.SetPin(1000, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory, true);
            store.SetTrace(1000, true);
            store.SetFollowed(1000);
            memory.Tick(_repo, "hill-attack");                         // saved on change
            Assert.True(File.Exists(memory.PathOf("hill-attack")));

            memory.Tick(_repo, "other");
            Assert.Empty(store.Remembered);
            Assert.Null(store.Followed);

            memory.Tick(_repo, "hill-attack");
            Assert.True(store.IsPinned(1000, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory));
            Assert.True(store.IsTraceSet(1000));
            Assert.Equal(1000, store.Followed);

            store.ResetAll();
            memory.Tick(_repo, "hill-attack");
            memory.Tick(_repo, "other");
            memory.Tick(_repo, "hill-attack");
            Assert.Empty(store.Remembered);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
