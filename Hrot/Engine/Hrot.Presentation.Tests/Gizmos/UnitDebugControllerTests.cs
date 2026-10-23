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
/// ⭐⭐ <c>CE-1257</c> (BV-26) — "Debug this unit": one switch, one debugged unit; on = traces armed + every gizmo family pinned + the
/// graph overlay follows it; off = all undone; moving it to another unit undoes the first. 📄 docs/DESIGN_Entity_Brain_View.md §10.
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

    /// <summary>🔴 Red-proof: drop the <c>Apply(world, previous, false)</c> line in <c>Set</c> ⇒ red (the first unit stays armed).</summary>
    [Fact]
    public void OnArmsAndPins_MovingUndoesTheFirst_OffUndoesAll_AndTheOverlayFollows()
    {
        var a = Unit();
        var b = Unit();
        var c = new UnitDebugController();

        c.Set(_repo, a, true);
        Apply();
        Assert.True((Debug(a).Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0);
        foreach (var f in GizmoFamilies.All) Assert.True((Debug(a).Ai & f) != 0, $"{f} pinned");
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
}
