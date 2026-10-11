using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Constants;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Common.Orders;
using Hrot.Core.Mission;
using Hrot.ScenarioEditor.Map;
using Hrot.UI.Common.Facades;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐⭐ <c>CE-3169</c> (docs/DESIGN_Entity_Control.md §4.2, §7 S2) — the unit menu's Move Here · Engage · Stop, registered ONCE by the
/// map pack on every live host: Move and Engage take a follow-up pick and become the same order the HTTP route publishes; a
/// cancelled pick orders nothing; a replay offers and registers none of them.
/// </summary>
public sealed class MenuOrdersTests : IDisposable
{
    private const long UnitId = 77, EnemyId = 88;
    private readonly EntityRepository _w = new();
    private readonly NetworkEntityMap _map = new();
    private readonly Entity _unit;

    public MenuOrdersTests()
    {
        _w.RegisterComponent<SimTransform>();
        _w.RegisterComponent<NetworkIdentity>();
        _w.SetSingletonManaged(_map);
        _unit = Add(UnitId);
        Add(EnemyId);
    }

    public void Dispose() => _w.Dispose();

    private Entity Add(long id)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new SimTransform { Position = new Vector3(5, 5, 0) });
        _w.AddComponent(e, new NetworkIdentity(id));
        _map.Register(id, e);
        return e;
    }

    private sealed class FakePick : IMapPickService
    {
        public TaskCompletionSource<GeoPoint>? Location;
        public TaskCompletionSource<int>? Enemy;
        public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default) => (Location = new()).Task;
        public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default) => (Enemy = new()).Task;
        public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());
    }

    private (MapInteraction mi, FakePick pick) Live()
    {
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w });
        var pick = new FakePick();
        mi.MenuOrders.PickService = () => pick;
        return (mi, pick);
    }

    private void Click(MapInteraction mi, int actionId)
    {
        Assert.True(mi.Actions.TryGetHandler(actionId, out var handler), $"action {actionId} registered");
        handler!(_w, _unit);
    }

    private AssignTacticalIntentEvent[] Published(MapInteraction mi)
    {
        mi.MenuOrders.Execute(_w, 0.016f);
        _w.Bus.SwapBuffers();
        return _w.Bus.ReadManaged<AssignTacticalIntentEvent>().ToArray();
    }

    [Fact]
    public void CE3169_MoveHere_PicksAPoint_ThenOrdersTheMove()
    {
        var (mi, pick) = Live();
        Click(mi, GlobalActionIds.MoveHere);
        Assert.True(mi.MenuOrders.IsPicking);
        Assert.Empty(Published(mi));                                  // nothing until the operator picks

        pick.Location!.SetResult(new GeoPoint(120, -40, 0));          // no geo transform here: world metres
        var order = Assert.Single(Published(mi));
        Assert.Equal(_unit, order.Entity);
        Assert.Equal("MoveToLocation", order.IntentId);
        Assert.Equal(Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Operator, order.Origin);
        Assert.Contains("\"x\":120", order.JsonParams);
    }

    [Fact]
    public void CE3169_Engage_PicksAnEnemy_ThenOrdersTheAttack()
    {
        var (mi, pick) = Live();
        Click(mi, GlobalActionIds.Engage);
        pick.Enemy!.SetResult((int)EnemyId);
        var order = Assert.Single(Published(mi));
        Assert.Equal("CombatPosture", order.IntentId);
        Assert.Contains($"\"target\":{EnemyId}", order.JsonParams);
        Assert.Contains("\"assault\":true", order.JsonParams);
    }

    [Fact]
    public void CE3169_ACancelledPick_OrdersNothing()
    {
        var (mi, pick) = Live();
        Click(mi, GlobalActionIds.MoveHere);
        pick.Location!.SetCanceled();
        Assert.Empty(Published(mi));
        Assert.False(mi.MenuOrders.IsPicking);

        Click(mi, GlobalActionIds.Engage);
        pick.Enemy!.SetResult(-1);                                     // the adapter's "nothing valid picked"
        Assert.Empty(Published(mi));
    }

    [Fact]
    public void CE3169_Stop_HoldsAtOnce()
    {
        var (mi, _) = Live();
        Click(mi, GlobalActionIds.Stop);
        _w.Bus.SwapBuffers();
        var order = Assert.Single(_w.Bus.ReadManaged<AssignTacticalIntentEvent>().ToArray());
        Assert.Equal("Idle", order.IntentId);
    }

    [Fact]
    public void CE3169_AHostWithNoPick_IssuesNothing_SoARelayedMenuActionIsNotOrderedTwice()
    {
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w });   // headless: nobody set PickService
        Click(mi, GlobalActionIds.Stop);
        Click(mi, GlobalActionIds.MoveHere);
        Assert.Empty(Published(mi));
    }

    [Fact]
    public void CE3169_TheUnitMenu_OffersOrders_OnALiveMap_AndNotInAReplay()
    {
        var unit = _unit;
        Assert.DoesNotContain("Move Here", ContextMenuProjectorGizmo.MenuJsonFor(_w, unit));   // no map built yet

        using var replayWorld = new EntityRepository();
        replayWorld.RegisterComponent<SimTransform>();
        replayWorld.RegisterComponent<NetworkIdentity>();
        var replay = MapInteractionPack.Build(new MapInteractionContext { World = replayWorld, IsReplayActive = static () => true });
        Assert.False(replay.Actions.TryGetHandler(GlobalActionIds.MoveHere, out _), "a replay registers no orders");
        var ghost = replayWorld.CreateEntity();
        replayWorld.AddComponent(ghost, new NetworkIdentity(5));
        Assert.DoesNotContain("Move Here", ContextMenuProjectorGizmo.MenuJsonFor(replayWorld, ghost));

        var (live, _) = Live();
        var menu = ContextMenuProjectorGizmo.MenuJsonFor(_w, unit);
        Assert.Contains("Move Here", menu);
        Assert.Contains("Engage", menu);
        Assert.Contains($"\"id\":{GlobalActionIds.Stop},", menu);
        Assert.Contains(typeof(MenuOrders), live.RequiredSystems);
        Assert.Contains(live.MenuOrders, live.InteractionSystems);   // scheduled wherever the map's interaction runs
    }

    /// <summary>⭐ CE-3169 — the location picker's Esc and right-click CANCEL (the adapter turns that into a cancelled task); a pick
    /// cancelled before used to leave <c>PickLocationAsync</c> pending for ever, so a menu's follow-up pick would hang.</summary>
    [Fact]
    public void CE3169_TheLocationPicker_ReportsACancel_OnEscapeAndRightClick()
    {
        int cancelled = 0, removed = 0, picked = 0;
        var esc = new Fdp.Toolkit.Vis2D.Gizmos.FdpLocationPickerGizmo(_ => picked++, () => removed++, () => cancelled++);
        esc.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Escape, isPressed: true);
        var right = new Fdp.Toolkit.Vis2D.Gizmos.FdpLocationPickerGizmo(_ => picked++, () => removed++, () => cancelled++);
        right.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Right, isPressed: true, Vector3.Zero);
        Assert.Equal((2, 2, 0), (cancelled, removed, picked));

        var click = new Fdp.Toolkit.Vis2D.Gizmos.FdpLocationPickerGizmo(_ => picked++, () => removed++, () => cancelled++);
        click.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, isPressed: false, Vector3.One);
        Assert.Equal((2, 3, 1), (cancelled, removed, picked));
    }
}
