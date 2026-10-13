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
/// ⭐⭐ <c>CE-3169</c> (docs/DESIGN_Entity_Control.md §4.2, §7 S2) — the unit menu's orders (⭐ CE-2143: the ones the unit's class fulfils), registered ONCE by the
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
        // ⭐ CE-2143 — a SOLDIER (DIS 3.1.225.1: class Person → Infantry): its class fulfils Move / Attack / Hold (PersonMove …).
        _w.SetDisType(_unit, new DISEntityType { Value = (3UL << 56) | (1UL << 48) | (225UL << 32) | (1UL << 24) });
    }

    /// <summary>⭐ CE-2143 — the unit-menu action id of an order (the menu offers orders, not a fixed three).</summary>
    private static int Id(string order) { Assert.True(OrderMenu.TryGetByName(order, out var o), order); return o.ActionId; }

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
        public string[]? Presets;
        public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default) { Presets = filterPresets; return (Enemy = new()).Task; }
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
        Click(mi, Id("Move"));
        Assert.True(mi.MenuOrders.IsPicking);
        Assert.Empty(Published(mi));                                  // nothing until the operator picks

        pick.Location!.SetResult(new GeoPoint(120, -40, 0));          // no geo transform here: world metres
        var order = Assert.Single(Published(mi));
        Assert.Equal(_unit, order.Entity);
        Assert.Equal("Order.Move", order.IntentId);                    // ⭐ CE-2143 — the ORDER; the unit picks how
        Assert.Equal(Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Operator, order.Origin);
        Assert.Contains("\"Location\":[-40,120]", order.JsonParams);    // no transform: [y, x] (OrderGeo's fallback)
    }

    [Fact]
    public void CE3169_Engage_PicksAnEnemy_ThenOrdersTheAttack()
    {
        var (mi, pick) = Live();
        Click(mi, Id("Attack"));
        pick.Enemy!.SetResult((int)EnemyId);
        var order = Assert.Single(Published(mi));
        Assert.Equal("Order.Attack", order.IntentId);
        Assert.Contains($"\"Target\":{EnemyId}", order.JsonParams);
    }

    [Fact]
    public void CE3169_ACancelledPick_OrdersNothing()
    {
        var (mi, pick) = Live();
        Click(mi, Id("Move"));
        pick.Location!.SetCanceled();
        Assert.Empty(Published(mi));
        Assert.False(mi.MenuOrders.IsPicking);

        Click(mi, Id("Attack"));
        pick.Enemy!.SetResult(-1);                                     // the adapter's "nothing valid picked"
        Assert.Empty(Published(mi));
    }

    [Fact]
    public void CE3169_Stop_HoldsAtOnce()
    {
        var (mi, _) = Live();
        Click(mi, Id("Hold"));
        _w.Bus.SwapBuffers();
        var order = Assert.Single(_w.Bus.ReadManaged<AssignTacticalIntentEvent>().ToArray());
        Assert.Equal("Order.Hold", order.IntentId);
    }

    /// <summary>⭐ CE-3174 (§8d) — the unit menu's Possess (FPS) asks to possess the unit for THIS station; the station's map then
    /// follows PossessedBy into first person (PossessionStationTests).</summary>
    [Fact]
    public void CE3174_Possess_AsksToPossessTheUnit_ForThisStation()
    {
        var (mi, _) = Live();
        Assert.Contains("Possess (FPS)", ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit));
        Click(mi, GlobalActionIds.Possess);
        _w.Bus.SwapBuffers();
        var ask = Assert.Single(_w.Bus.Read<Fdp.Toolkit.Behavior.Possession.PossessRequestEvent>().ToArray());
        Assert.Equal(_unit, ask.Unit);
        Assert.Equal(Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps, ask.Mode);
    }

    [Fact]
    public void CE3169_AHostWithNoPick_IssuesNothing_SoARelayedMenuActionIsNotOrderedTwice()
    {
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w });   // headless: nobody set PickService
        Click(mi, Id("Hold"));
        Click(mi, Id("Move"));
        Assert.Empty(Published(mi));
    }

    [Fact]
    public void CE3169_TheUnitMenu_OffersOrders_OnALiveMap_AndNotInAReplay()
    {
        var unit = _unit;
        Assert.DoesNotContain("\"Move\"", ContextMenuProjectorGizmo.MenuJsonFor(_w, unit));   // no map built yet

        using var replayWorld = new EntityRepository();
        replayWorld.RegisterComponent<SimTransform>();
        replayWorld.RegisterComponent<NetworkIdentity>();
        var replay = MapInteractionPack.Build(new MapInteractionContext { World = replayWorld, IsReplayActive = static () => true });
        Assert.False(replay.Actions.TryGetHandler(Id("Move"), out _), "a replay registers no orders");
        var ghost = replayWorld.CreateEntity();
        replayWorld.AddComponent(ghost, new NetworkIdentity(5));
        Assert.DoesNotContain("\"Move\"", ContextMenuProjectorGizmo.MenuJsonFor(replayWorld, ghost));

        var (live, _) = Live();
        var menu = ContextMenuProjectorGizmo.MenuJsonFor(_w, unit);
        Assert.Contains("\"Move\"", menu);                                  // ⭐ CE-2143 — the orders a soldier's class fulfils
        Assert.Contains("\"Attack\"", menu);
        Assert.Contains($"\"id\":{Id("Hold")},", menu);
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

    /// <summary>⭐ CE-2143 (D4-D, D4-H) — the menu lists EXACTLY the orders the unit's class fulfils: a soldier gets Move / Attack /
    /// Hold and Possess; a squad (a body-less unit, DIS Kind 11) gets its own three and no Possess; an entity no class of which has
    /// an order behaviour gets no orders at all.</summary>
    [Fact]
    public void CE2143_TheMenu_ListsExactlyTheOrdersTheUnitsClassFulfils()
    {
        var (_, _) = Live();
        var soldier = ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit);
        foreach (var o in new[] { "Move", "Attack", "Hold" }) Assert.Contains($"\"id\":{Id(o)},", soldier);
        Assert.Contains("Possess (FPS)", soldier);

        _w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitEntity>();
        var squad = Add(99);
        _w.AddComponent(squad, new Fdp.Core.CommandHierarchy.UnitEntity());
        _w.SetDisType(squad, Hrot.Core.Tkb.UnitDis.Land(Hrot.Core.Tkb.UnitArm.Infantry, Hrot.Core.Tkb.UnitEchelon.Squad, 225));
        var squadMenu = ContextMenuProjectorGizmo.MenuJsonFor(_w, squad);
        foreach (var o in new[] { "Move", "Attack", "Hold" }) Assert.Contains($"\"id\":{Id(o)},", squadMenu);
        Assert.DoesNotContain("Possess (FPS)", squadMenu);

        var munition = Add(100);
        _w.SetDisType(munition, new DISEntityType { Value = 2UL << 56 });            // a munition: no class has an order behaviour
        var none = ContextMenuProjectorGizmo.MenuJsonFor(_w, munition);
        foreach (var o in OrderMenu.Orders) Assert.DoesNotContain($"\"id\":{o.ActionId},", none);
    }

    // ── CE-2148 (P5, design §13.11) ─────────────────────────────────────────────────────────────────────────────────

    private struct TwoPicks
    {
        [System.ComponentModel.DataAnnotations.Required] public Fdp.Toolkit.Replication.EntityRef Who;
        [System.ComponentModel.DataAnnotations.Required] public Fdp.Toolkit.Behavior.Params.PickableGeoPoint Where;
    }

    private sealed class FakeForm : IOrderForm
    {
        public OrderDraft? Opened;
        public void Open(OrderDraft draft) => Opened = draft;
    }

    /// <summary>⭐ F5 — every sender gets the same refusal: an order missing a required field is refused, NAMING it, and nothing is
    /// published; a key spelled in another case counts (R-2005).</summary>
    [Fact]
    public void CE2148_AnOrderMissingARequiredField_IsRefusedNamingIt()
    {
        var orders = new EntityOrders(_w, _map, null);
        var r = orders.Order(UnitId, "Move", "{}");
        Assert.Equal(OrderRefusal.BadRequest, r.Refusal);
        Assert.Contains("Location", r.Detail);
        r = orders.Order(UnitId, "Attack", "{\"target\":0}");
        Assert.Equal(OrderRefusal.BadRequest, r.Refusal);
        Assert.Contains("Target", r.Detail);
        _w.Bus.SwapBuffers();
        Assert.Empty(_w.Bus.ReadManaged<AssignTacticalIntentEvent>());

        Assert.True(orders.Order(UnitId, "Attack", $"{{\"target\":{EnemyId}}}").Accepted);
    }

    /// <summary>⭐ F6 / P5-d — a draft the gesture cannot complete goes to the host's order FORM (a world singleton), which sends it
    /// through the draft; with no form on the host it is refused (logged) and nothing is sent.</summary>
    [Fact]
    public void CE2148_AnIncompleteDraft_OpensTheOrderForm_WhichSendsThroughTheDraft()
    {
        var info = new OrderInfo("Attack", typeof(TwoPicks), 0, Hrot.Map.Definitions.Behavior.Orders.OrderGesture.None,
            OrderMenu.PickOf(typeof(TwoPicks)).Pick, null, OrderMenu.FieldsOf(typeof(TwoPicks)));
        string? sent = null;
        var draft = OrderMenu.Draft(info, new[] { UnitId }, new OrderContext(EnemyId, null), json => { sent = json; return Array.Empty<OrderResult>(); });
        Assert.Equal("Where", draft.Missing.Single().Name);

        MenuOrders.OpenForm(_w, draft);   // no form on this host: refused, nothing sent
        Assert.Null(sent);

        var form = new FakeForm();
        _w.SetSingletonManaged<IOrderForm>(form);
        MenuOrders.OpenForm(_w, draft);
        Assert.Same(draft, form.Opened);
        form.Opened!.Send("{\"Who\":88,\"Where\":[1,2]}");
        Assert.Equal("{\"Who\":88,\"Where\":[1,2]}", sent);
    }

    // ── CE-2149 (P6, design §13.3) ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ F7 stage 1 — Designate › Set target picks an entity once; a later Attack from the menu then goes at once with that
    /// target (no second pick); Set route narrows the pick to roads; Clear forgets.</summary>
    [Fact]
    public void CE2149_ADesignatedTarget_LetsAttackGoWithoutAPick_AndClearForgetsIt()
    {
        var (mi, pick) = Live();
        Click(mi, GlobalActionIds.DesignateTarget);
        Assert.Null(pick.Presets);
        pick.Enemy!.SetResult((int)EnemyId);
        Assert.Empty(Published(mi));                                      // a designation is not an order
        Assert.True(OrderDesignations.Of(_w)!.TryGet(UnitId, DesignationKind.Target, out var t));
        Assert.Equal(EnemyId, t.GetValue<long>());

        pick.Enemy = null;
        Click(mi, Id("Attack"));
        Assert.Null(pick.Enemy);                                          // no pick: the designation filled Target
        var sent = Published(mi).Single(e => e.Entity == _unit);
        Assert.Contains($"\"Target\":{EnemyId}", sent.JsonParams);

        Click(mi, GlobalActionIds.DesignateRoute);
        Assert.Equal(new[] { OrderDesignations.RoutePreset }, pick.Presets);

        Click(mi, GlobalActionIds.DesignateClear);
        Assert.False(OrderDesignations.Of(_w)!.TryGet(UnitId, DesignationKind.Target, out _));
    }

    /// <summary>⭐ §13.3 staleness — a designated target that is gone is dropped, and Attack asks for a pick again.</summary>
    [Fact]
    public void CE2149_AGoneTarget_IsDropped_AndAttackPicksAgain()
    {
        var (mi, pick) = Live();
        OrderDesignations.For(_w).Set(UnitId, DesignationKind.Target, System.Text.Json.Nodes.JsonValue.Create(EnemyId));
        Assert.True(_map.TryGetEntity(EnemyId, out var enemy));
        _w.DestroyEntity(enemy);

        Click(mi, Id("Attack"));
        Assert.NotNull(pick.Enemy);                                       // the pick is back
        Assert.False(OrderDesignations.Of(_w)!.TryGet(UnitId, DesignationKind.Target, out _));
    }

    /// <summary>⭐ The unit menu carries the Designate submenu where the unit takes orders.</summary>
    [Fact]
    public void CE2149_TheUnitMenu_OffersDesignate()
    {
        var (_, _) = Live();
        var menu = ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit);
        Assert.Contains("Designate", menu);
        Assert.Contains($"\"id\":{GlobalActionIds.DesignateArea},", menu);
    }
}
