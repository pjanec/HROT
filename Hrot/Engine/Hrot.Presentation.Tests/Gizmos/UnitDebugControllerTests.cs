using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Hrot.Common.Constants;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.ScenarioEditor.Map;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐⭐ <c>CE-1257</c> (BV-26) — "Debug this unit": one switch, one debugged unit; on = traces armed + the graph overlay follows it; off =
/// all undone; moving it to another unit undoes the first. ⭐ <c>CE-1317</c> (BV-48, BV-49): ⛔ it no longer pins every gizmo family, and
/// VIEWING a unit's Brain tab arms its traces (<see cref="BrainObservation"/>). 📄 docs/DESIGN_Entity_Brain_View.md §10, §12c.
/// </summary>
public sealed class UnitDebugControllerTests : IDisposable
{
    private readonly EntityRepository _repo = new();

    public UnitDebugControllerTests()
    {
        _repo.RegisterComponent<BehaviorState>();
        _repo.RegisterComponent<DebugState>();
        _repo.RegisterManagedEvent<PatchDebugStateCommand>();
    }

    public void Dispose() => _repo.Dispose();

    private Entity Unit()
    {
        var e = _repo.CreateEntity();
        _repo.AddComponent(e, new BehaviorState());
        return e;
    }

    private void Apply()
    {
        _repo.Bus.SwapBuffers();
        new DebugStatePatchSystem().Execute(_repo, 0.016f);
    }

    private DebugState Debug(Entity e) => _repo.HasComponent<DebugState>(e) ? _repo.GetComponentRO<DebugState>(e) : default;

    /// <summary>🔴 Red-proof: drop the <c>Apply(world, previous, false)</c> line in <c>Set</c> ⇒ red (the first unit stays armed).
    /// ⭐ <c>CE-1317</c> (BV-49) — and NO gizmo family is pinned by it (🔒 user: "turning all gizmos at once would be overwhelming").</summary>
    [Fact]
    public void CE1317_OnArmsTracingOnly_MovingUndoesTheFirst_OffUndoesAll_AndTheOverlayFollows()
    {
        var a = Unit();
        var b = Unit();
        var c = new UnitDebugController();

        c.Set(_repo, a, true);
        Apply();
        Assert.True((Debug(a).Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0);
        Assert.Equal(0, (int)Debug(a).Ai);                            // ⛔ BV-49: no family pinned
        Assert.Equal(a, c.Follow(_repo, b));                          // the overlay follows the debugged unit, not the selection

        c.Set(_repo, b, true);
        Apply();
        Assert.Equal(b, c.Debugged);
        Assert.Equal(0, (int)(Debug(a).Behavior & BehaviorDebugFlags.EnableTraceBuffer));
        Assert.Equal(0, (int)Debug(a).Ai);
        Assert.True((Debug(b).Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0);

        c.Set(_repo, b, false);
        Apply();
        Assert.Null(c.Debugged);
        Assert.Equal(0, (int)Debug(b).Ai);
        Assert.Equal(a, c.Follow(_repo, a));                          // nothing debugged: the selection again
    }

    /// <summary>⭐ The map's AI-trace submenu offers it only where a host composes the controller, and the pack registers the action that
    /// toggles it. 🔴 Red-proof: build the submenu without the item (<c>Children = false ? …</c> in <c>AiTraceActions.Submenu</c>) ⇒ red.
    /// ⚠ Dropping the cache key's 128 flag stayed GREEN: the unit-menu cache is rebuilt when the order vocabulary changes, which it does
    /// between these calls — the flag is a guard for a stable vocabulary this rail cannot reach.</summary>
    [Fact]
    public void TheMapMenu_OffersIt_WhereAControllerIsComposed_AndItsActionToggles()
    {
        var unit = Unit();
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _repo, StartEnabled = true });
        string id = $"\"id\":{GlobalActionIds.DebugThisUnit},";

        var previous = UnitDebugController.Current;
        try
        {
            UnitDebugController.Current = null;
            Assert.DoesNotContain(id, ContextMenuProjectorGizmo.MenuJsonFor(_repo, unit));

            var c = new UnitDebugController();
            UnitDebugController.Current = c;
            Assert.Contains(id, ContextMenuProjectorGizmo.MenuJsonFor(_repo, unit));
            Assert.True(mi.Actions.TryGetHandler(GlobalActionIds.DebugThisUnit, out var handler));
            handler!(_repo, unit);
            Assert.Equal(unit, c.Debugged);
        }
        finally { UnitDebugController.Current = previous; }
    }

    /// <summary>⭐⭐ <c>CE-1317</c> (Brain View §12c BV-48) — VIEWING IS DEBUGGING: a tab drawing a unit arms its traces; a unit no tab drew for
    /// the linger is disarmed; a trace someone else armed (the menu, the debug switch) is left alone. ✅ Red-proof: the <c>_armedHere</c>
    /// guard removed ⇒ the menu-armed unit is disarmed.</summary>
    [Fact]
    public void CE1317_ATabViewingAUnit_ArmsItsTraces_AndReleasesThemOnlyWhenItArmedThem()
    {
        long now = 0;
        var o = new BrainObservation(() => now) { Linger = TimeSpan.FromSeconds(1) };
        var viewed = Unit();
        var menuArmed = Unit();
        AiTraceActions.Set(_repo, menuArmed, BehaviorDebugFlags.EnableTraceBuffer, true);
        Apply();

        o.Viewed(_repo, viewed);
        o.Viewed(_repo, menuArmed);
        Apply();
        Assert.True((Debug(viewed).Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0);
        Assert.Equal(0, (int)Debug(viewed).Ai);                       // tracing only — no family pinned

        now = 500; o.Viewed(_repo, viewed); o.Tick(_repo); Apply();   // still drawn: stays armed
        Assert.True((Debug(viewed).Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0);

        now = 2_000; o.Tick(_repo); Apply();                          // no tab drew either for > 1 s
        Assert.Equal(0, (int)(Debug(viewed).Behavior & BehaviorDebugFlags.EnableTraceBuffer));
        Assert.True((Debug(menuArmed).Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0, "the menu armed it — not ours to disarm");
        Assert.Empty(o.Observed);
    }
}
