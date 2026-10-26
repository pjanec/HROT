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
[Xunit.Collection(Hrot.Presentation.Tests.ImGuiTestCollection.Name)]   // ⭐ one global ImGui context
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

    /// <summary>
    /// ⭐⭐ CE-1076 (docs/DESIGN_Entity_Control.md §13.6) — the keys the unit menu shows run its items on the selected unit: a typed M
    /// sends the SAME menu action a click on "Move" sends (its pick, its Esc), A Attack, H Hold, P Possess. 🔒 User, 2026-10-14: "the
    /// keys shown next to the order name in the entity context menu (M = move, A = attack …) i supposed they would work plainly for the
    /// currently selected entity". Not with Ctrl / Alt, not with nothing selected, not while another tool holds the map — but yes in
    /// RTS command mode.
    /// </summary>
    [Fact]
    public void CE1076_AMenuShortcut_RunsTheSelectedUnitsItem_ThroughTheSameMenuAction()
    {
        var (mi, _) = Live();
        int? Typed(int key, bool modified = false)
        {
            mi.InteractionBus.Publish(new Hrot.Common.Interactions.MapShortcutKeyEvent { Key = key, Modified = modified });
            mi.InteractionBus.SwapBuffers();
            mi.MenuShortcuts.Execute(_w, 0.016f);
            mi.InteractionBus.SwapBuffers();
            var sent = mi.InteractionBus.Read<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMenuActionEvent>().ToArray();
            if (sent.Length == 0) return null;
            Assert.Equal(UnitId, Assert.Single(sent).AnchorId);   // the menu of the selected unit, as a click on it
            return sent[0].ActionId;
        }

        Assert.Null(Typed('M'));                                       // nothing selected: nothing to order
        if (!_w.IsComponentTypeRegistered<Hrot.IG.Components.SelectionState>()) _w.RegisterComponent<Hrot.IG.Components.SelectionState>();
        mi.Selection.PrimarySelected = _unit;
        Assert.Equal(Id("Move"), Typed('M'));
        Assert.Equal(Id("Attack"), Typed('A'));
        Assert.Equal(Id("Hold"), Typed('H'));
        Assert.Equal(GlobalActionIds.Possess, Typed('P'));
        Assert.Equal(GlobalActionIds.CenterOnEntity, Typed('C'));
        Assert.Null(Typed('Q'));                                       // no item shows Q
        Assert.Null(Typed('M', modified: true));                       // Ctrl+M / Alt+M are the application's

        mi.MenuOrders.PickService = () => new FakePick();
        mi.Tools.Register(new Hrot.ScenarioEditor.Tools.ToolDescriptor("test.tool", "Busy", Hrot.ScenarioEditor.Tools.ToolModality.Modal,
            Hrot.ScenarioEditor.Tools.ToolArbiter.None), _ => Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Armed);
        Assert.True(mi.Tools.Activate("test.tool"));
        Assert.Null(Typed('M'));                                       // a tool holds the map's keys
        Assert.True(mi.Tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.RtsCommand));
        Assert.Equal(Id("Move"), Typed('M'));                          // …except the RTS command tool
        Assert.Equal(6, mi.MenuShortcuts.Fired);
        Assert.Contains(mi.MenuShortcuts, mi.InteractionSystems);       // scheduled wherever the map's interaction runs
        Assert.Contains(typeof(MenuShortcuts), mi.RequiredSystems);
    }

    /// <summary>⭐ CE-1076 — the canvas' key layer hands every typed key on, with Ctrl / Alt marked, and none in first person (where
    /// W, A, S, D and C drive the unit).</summary>
    [Fact]
    public void CE1076_TheKeyLayer_ForwardsTypedKeys_ButNotInFirstPerson()
    {
        var bus = new FdpEventBus();
        Hrot.Common.Interactions.InteractionEventRegistry.RegisterAll(bus);
        var camera = new Fdp.Toolkit.Vis3D.MapCamera3D { ViewportOverride = new Vector2(640, 480) };
        bool ctrl = false;
        var layer = new MenuShortcutKeyLayer(bus, () => camera, () => ctrl);
        Assert.False(layer.HandleKeyInput((Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey)'M'));   // consumes nothing
        ctrl = true;
        layer.HandleKeyInput((Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey)'C');
        bus.SwapBuffers();
        var keys = bus.Read<Hrot.Common.Interactions.MapShortcutKeyEvent>().ToArray();
        Assert.Equal(new[] { ('M', false), ('C', true) }, keys.Select(k => ((char)k.Key, k.Modified)).ToArray());

        camera.Driver = new NoDriver();
        layer.HandleKeyInput((Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey)'M');
        bus.SwapBuffers();
        Assert.Empty(bus.Read<Hrot.Common.Interactions.MapShortcutKeyEvent>().ToArray());
    }

    private sealed class NoDriver : Fdp.Toolkit.Vis3D.ICameraDriver
    {
        public void Update(Fdp.Toolkit.Vis3D.MapCamera3D camera, float dt) { }
        public bool HandleInput(Fdp.Toolkit.Vis3D.MapCamera3D camera, Fdp.Toolkit.Vis2D.Abstractions.IInputProvider input) => true;
        public void Detached(Fdp.Toolkit.Vis3D.MapCamera3D camera) { }
    }

    [Fact]
    public void CE1076_TheShortcutText_IsALetterOrAKeyName_AndADisabledItemDoesNotRun()
    {
        Assert.Equal('M', MenuShortcuts.KeyOf("m"));
        Assert.Equal(32, MenuShortcuts.KeyOf("Space"));
        Assert.Equal((int)Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey.Escape, MenuShortcuts.KeyOf("Esc"));
        Assert.Null(MenuShortcuts.KeyOf(""));
        var items = new[]
        {
            new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Id = 5, Label = "Move", Shortcut = "M", Enabled = false },
            new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Label = "More", Children = new[]
            {
                new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Id = 9, Label = "Mark", Shortcut = "K" },
            } },
        };
        Assert.Null(MenuShortcuts.ActionFor(items, 'M'));               // disabled (a heavily damaged unit's Move)
        Assert.Equal(9, MenuShortcuts.ActionFor(items, 'K'));           // found in a submenu
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

    /// <summary>⭐ CE-1205 (docs/DESIGN_Entity_Control.md §8m PK-7) — a WEAPON is manned, never possessed: its menu keeps Possess (FPS)
    /// greyed with the reason, and the possess order (the menu's, HTTP's, MCP's) is refused with the same words — no request goes out.
    /// ✅ Red-proof: Possessability.Refusal returning null ⇒ the item is live and the request is published.</summary>
    [Fact]
    public void CE1205_AWeapon_IsNotPossessed_TheMenuSaysWhy_AndTheOrderIsRefused()
    {
        var (mi, _) = Live();
        if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.WeaponSystemParams>()) _w.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
        _w.AddComponent(_unit, new Fdp.Toolkit.Combat.WeaponSystemParams { NeedsOperator = 1 });
        var menu = ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit);
        Assert.Contains("Possess (FPS)", menu);
        Assert.Contains(Fdp.Toolkit.Behavior.Possession.Possessability.WeaponIsManned, menu);
        var orders = new EntityOrders(_w, _map, geo: null);
        var refused = orders.Possess(UnitId);
        Assert.Equal(OrderRefusal.NotPossessable, refused.Refusal);
        Assert.Contains("manned", refused.Detail);
        _w.Bus.SwapBuffers();
        Assert.Empty(_w.Bus.Read<Fdp.Toolkit.Behavior.Possession.PossessRequestEvent>().ToArray());
        _ = mi;
    }

    /// <summary>⭐ CE-1230 (docs/designs/gizmos-1/canvas-context-menu-design.md §11 OM-2, OM-3, R-1019) — the map's (and so the ORBAT
    /// tree's) menu embarks and disembarks: a man on foot offers "Embark…", which picks the vehicle on the map and boards him; aboard, he
    /// offers "Disembark", which sends him out of the vehicle he is in; a vehicle offers neither. ✅ Red-proof: the Embark handler not
    /// registered ⇒ the click finds no action.</summary>
    [Fact]
    public void CE1230_TheSharedMenu_EmbarksOnAVehiclePick_AndDisembarksAUnitAboard()
    {
        var (mi, pick) = Live();
        if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()) _w.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.PassengerBuffer>()) _w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
        var bmp = Add(99);
        _w.AddComponent(bmp, new Fdp.Toolkit.Behavior.Components.PassengerBuffer());
        string Item(int id) => $"\"id\":{id},";
        Assert.Contains(Item(GlobalActionIds.Embark), ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit));
        Assert.DoesNotContain(Item(GlobalActionIds.Embark), ContextMenuProjectorGizmo.MenuJsonFor(_w, bmp));   // a vehicle is boarded, it does not board

        Click(mi, GlobalActionIds.Embark);
        pick.Enemy!.SetResult(99);
        mi.MenuOrders.Execute(_w, 0.016f);
        _w.Bus.SwapBuffers();
        var board = Assert.Single(_w.Bus.Read<Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand>().ToArray());
        Assert.Equal((_unit, bmp), (board.Passenger, board.Vehicle));

        _w.AddComponent(_unit, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = bmp });   // aboard now
        var menu = ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit);
        Assert.Contains(Item(GlobalActionIds.Disembark), menu);
        Assert.DoesNotContain(Item(GlobalActionIds.Embark), menu);
        Click(mi, GlobalActionIds.Disembark);
        _w.Bus.SwapBuffers();
        var leave = Assert.Single(_w.Bus.Read<Fdp.Toolkit.Behavior.Events.DisembarkEntityCommand>().ToArray());
        Assert.Equal((_unit, bmp), (leave.Passenger, leave.Vehicle));
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
        MapInteractionPack.Build(new MapInteractionContext { World = replayWorld });
        Fdp.Toolkit.Replay.ReplayState.Enter(replayWorld);   // ⭐ DAR F7 — the world says it shows a recording (R-3064)
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

    // ── CE-1059 (P5b, the order form — design §13.12) ───────────────────────────────────────────────────────────────

    private static void DrawFrame(Hrot.Presentation.Orders.OrderForm form)
    {
        using var imgui = new Hrot.Presentation.Tests.ImGuiTestFixture();
        imgui.NewFrame();
        form.Draw();
        imgui.Render();
    }

    /// <summary>⭐ Acceptance 1 — an order with two required picks opens the form PREFILLED with the gesture's pick; a form left
    /// incomplete sends nothing; once the second pick lands (a map pick through the form's own scoped broker, drawn by THE params
    /// form) Send goes through the draft, once per unit, carrying both.</summary>
    [Fact]
    public void CE1059_ATwoPickOrder_OpensTheFormPrefilled_SendsOncePerUnit_AndAnIncompleteFormSendsNothing()
    {
        var pick = new FakePick();
        var form = new Hrot.Presentation.Orders.OrderForm(_w, () => pick);
        var info = new OrderInfo("TwoPicks", typeof(TwoPicks), 0, Hrot.Map.Definitions.Behavior.Orders.OrderGesture.None,
            OrderMenu.PickOf(typeof(TwoPicks)).Pick, null, OrderMenu.FieldsOf(typeof(TwoPicks)));
        var sent = new List<(long Unit, string Json)>();
        long[] units = { UnitId, 66 };
        var draft = OrderMenu.Draft(info, units, new OrderContext(EnemyId, null),
            json => units.Select(u => { sent.Add((u, json)); return new OrderResult(OrderRefusal.None, "", "", ""); }).ToArray());
        Assert.Equal(OrderPick.Several, info.Pick);

        form.Open(draft);
        var entry = Assert.Single(form.Entries);
        Assert.Contains($"\"Who\":{EnemyId}", entry.Json().ToJsonString());          // prefilled from the gesture
        Assert.Equal("Where", Assert.Single(entry.Missing).Name);
        Assert.False(entry.Send());                                                    // incomplete: nothing sent
        DrawFrame(form);
        Assert.Empty(sent);

        form.PicksFor(entry).RequestLocationPick("$.Where");                           // the field's Pick Map button
        pick.Location!.SetResult(new GeoPoint(50.5, 14.25, 0));
        DrawFrame(form);                                                               // the params form consumes the pick
        Assert.Empty(entry.Missing);

        Assert.True(entry.Send());
        Assert.Equal(new[] { UnitId, 66L }, sent.Select(x => x.Unit));                 // once per unit
        Assert.All(sent, x => Assert.Contains($"\"Who\":{EnemyId}", x.Json));
        Assert.All(sent, x => Assert.Contains("\"Where\":[50.5,14.25]", x.Json));
        DrawFrame(form);
        Assert.Empty(form.Entries);                                                    // every unit accepted: the form closes
    }

    /// <summary>
    /// ⭐ §6n's last blocked item, measured after behaviors' IF-2 (docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.7) — the FIRE-MISSION
    /// form: a battery (unit 304, DIS 11.1.225.4) and an SP howitzer (107, 1.1.225.4) are offered Strike in the unit menu and in "Order with
    /// options", and the Strike form carries every fire-mission field (method, rounds, sheaf and its width, control, time on target, danger
    /// close) — the same reflected form every order uses, no Strike-specific screen.
    /// </summary>
    [Fact]
    public void IF2_ABatteryAndAnSpGun_AreOfferedStrike_AndItsFormCarriesTheFireMission()
    {
        Assert.True(OrderMenu.TryGetByName("Strike", out var strike));
        var fields = strike.Fields.Select(f => f.Name).ToArray();
        foreach (var name in new[] { "Location", "Method", "Rounds", "Sheaf", "SheafWidthM", "Control", "ImpactInS", "DangerClose" })
            Assert.Contains(name, fields);

        var (mi, _) = Live();
        mi.OrderForm!.Register();   // "Order with options" is offered where a form is drawn
        foreach (ulong dis in new[] { (11UL << 56) | (1UL << 48) | (225UL << 32) | (4UL << 24), (1UL << 56) | (1UL << 48) | (225UL << 32) | (4UL << 24) })
        {
            _w.SetDisType(_unit, new DISEntityType { Value = dis });
            var menu = ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit);
            Assert.Contains($"\"id\":{strike.ActionId},", menu);
            Assert.Contains($"\"id\":{GlobalActionIds.OrderWithOptionsBase + (strike.ActionId - OrderMenu.ActionBase)},", menu);
        }

        mi.MenuOrders.Selected = () => new[] { _unit };
        Click(mi, GlobalActionIds.OrderWithOptionsBase + (strike.ActionId - OrderMenu.ActionBase));
        var entry = Assert.Single(mi.OrderForm.Entries);
        Assert.Equal("Location", Assert.Single(entry.Missing).Name);   // the point is the one required field; the mission has defaults
        DrawFrame(mi.OrderForm);                                        // the reflected form draws the fire-mission fields
    }

    /// <summary>
    /// ⭐ §6x FX-1 / FX-2 (docs/DESIGN_Map_3D_Mode.md) — the fire mission's aim points: three M109s laid on a parallel sheaf (their replicated
    /// posture AIMED at their own points, as <c>FireAtPoint</c> reports while they wait for the fire time) draw three rings of their round's burst
    /// with a cross each; only the SELECTED gun draws its dashed line; a rifleman aimed at a point (no indirect weapon) and a gun that is only
    /// READY draw nothing; every map host registers the overlay, a replay included.
    /// </summary>
    [Fact]
    public void FX1_EachGunLaidOnAPoint_MarksItsAimPoint_AndOnlyTheSelectedOneDrawsItsLine()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        _w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        _w.RegisterComponent<TkbIdentity>();
        _w.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        Entity Unit(long type, Vector3 at, Fdp.Toolkit.Combat.Components.WeaponPostureKind kind, Vector3 aim)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at });
            _w.AddComponent(e, new TkbIdentity { TkbType = type });
            _w.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = kind, Point = aim });
            return e;
        }
        var guns = new List<Entity>();
        for (int i = 0; i < 3; i++)   // a parallel sheaf: each gun on its own point, 40 m apart across the line of fire
            guns.Add(Unit(Hrot.Map.Common.TkbEntityTypes.Sph_M109, new Vector3(0f, 40f * i, 0f), Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, new Vector3(6000f, 40f * i, 0f)));
        Unit(Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, new Vector3(50f, 0f, 0f), Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, new Vector3(300f, 0f, 0f));
        Unit(Hrot.Map.Common.TkbEntityTypes.Sph_M109, new Vector3(0f, 200f, 0f), Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready, new Vector3(6000f, 200f, 0f));

        var aims = new List<(Entity Gun, Vector3 Aim, float Radius)>();
        Hrot.Presentation.Orders.FireMissionOverlayGizmo.AimsOf(_w, aims);
        Assert.Equal(guns, aims.Select(a => a.Gun));
        Assert.Equal(new[] { 0f, 40f, 80f }, aims.Select(a => a.Aim.Y));
        Assert.All(aims, a => Assert.True(a.Radius > 5f, "a ring the size of the round's burst"));

        var overlay = new Hrot.Presentation.Orders.FireMissionOverlayGizmo(() => new[] { guns[1] });
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer(capacity: 64);
        overlay.UpdateAndDraw(_w, 0f, buffer);
        Assert.Equal((3, 1), (overlay.MarksDrawn, overlay.LinesDrawn));
        int rings = 0, lines = 0;
        foreach (var p in buffer.GetFrame())
        {
            if (p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Sphere) rings++;
            if (p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Line) lines++;
        }
        Assert.Equal(3, rings);
        Assert.Equal(3 * 2 + 1, lines);   // a cross per mark, one line to the selected gun's point

        string pack = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "Hrot/Engine/Hrot.Presentation/ScenarioEditor/Map/MapInteractionPack.cs"));
        int registered = pack.IndexOf("new Hrot.Presentation.Orders.FireMissionOverlayGizmo(", StringComparison.Ordinal);
        Assert.True(registered > 0 && registered < pack.IndexOf("menuOrders.RegisterOn(actions)", StringComparison.Ordinal),
            "registered on every map host, before the orders (a replay draws the marks too)");
    }

    private static string RepoRoot()
    {
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "HROT.sln"))) d = d.Parent;
        return d!.FullName;
    }

    /// <summary>⭐ O5 / O6 / O7 — "Order with options › Move" opens the form for the SELECTION the clicked unit is in; Send publishes
    /// the order once per selected unit through EntityOrders, and the remember tick stores the point as both units' designation.</summary>
    [Fact]
    public void CE1059_OrderWithOptions_OrdersTheSelection_ThroughTheForm_AndRemembersThePoint()
    {
        var (mi, pick) = Live();
        var other = Add(66);
        _w.SetDisType(other, _w.GetDisType(_unit));
        mi.MenuOrders.Selected = () => new[] { _unit, other };
        Assert.NotNull(mi.OrderForm);
        mi.OrderForm!.Register();

        Click(mi, GlobalActionIds.OrderWithOptionsBase + (Id("Move") - OrderMenu.ActionBase));
        var entry = Assert.Single(mi.OrderForm.Entries);
        Assert.Equal(new[] { UnitId, 66L }, entry.Draft.Units);
        Assert.Empty(Published(mi));                                                   // nothing until Send

        mi.OrderForm.PicksFor(entry).RequestLocationPick("$.Location");
        pick.Location!.SetResult(new GeoPoint(50.5, 14.25, 0));   // a geodetic point — behaviors' CE-2176 fix refuses a local [y, x] (|lat| > 90)
        DrawFrame(mi.OrderForm);
        entry.Remember = true;
        Assert.True(entry.Send());

        var orders = Published(mi);
        Assert.Equal(2, orders.Length);
        Assert.All(orders, o => Assert.Equal("Order.Move", o.IntentId));
        Assert.Equal(new[] { _unit, other }, orders.Select(o => o.Entity).OrderBy(e => e.Index));
        foreach (long u in new[] { UnitId, 66L })
            Assert.True(OrderDesignations.Of(_w)!.TryGet(u, DesignationKind.MapPoint, out _), $"unit {u} remembers the point");
    }

    /// <summary>⭐ Acceptance 2 — the menu shows a <c>Several</c> order as <c>Name…</c> and offers "Order with options" ONLY where a form
    /// is registered; a node that never draws a form (headless) shows neither.</summary>
    [Fact]
    public void CE1059_TheMenu_OffersTheForm_OnlyWhereOneIsRegistered()
    {
        var several = new OrderInfo("Escort", typeof(TwoPicks), OrderMenu.ActionBase + 5, Hrot.Map.Definitions.Behavior.Orders.OrderGesture.None,
            OrderPick.Several, null, OrderMenu.FieldsOf(typeof(TwoPicks)));
        Assert.Null(ContextMenuProjectorGizmo.OrderItem(several, degraded: false, form: false));
        var item = ContextMenuProjectorGizmo.OrderItem(several, degraded: false, form: true)!;
        Assert.Equal("Escort…", item.Label);
        Assert.Equal(several.ActionId, item.Id);                                        // its own id ⇒ MenuOrders.Begin ⇒ the form

        var (mi, _) = Live();                                                           // a live map, but nothing drew the form yet
        Assert.DoesNotContain("Order with options", ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit));
        DrawFrame(mi.OrderForm!);                                                       // the host draws it ⇒ registered ⇒ offered
        Assert.True(mi.OrderForm!.IsRegistered);
        var menu = ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit);
        Assert.Contains("Order with options", menu);
        Assert.Contains($"\"id\":{GlobalActionIds.OrderWithOptionsBase + (Id("Move") - OrderMenu.ActionBase)},", menu);
    }

    /// <summary>⭐ U6 / O6 — Designate › Set target on a unit of the selection sets it for EVERY selected unit; the overlay then draws
    /// a dashed target line from each selected unit.</summary>
    [Fact]
    public void CE1059_DesignateOnASelection_SetsEveryUnit_AndTheOverlayDrawsIt()
    {
        var (mi, pick) = Live();
        var other = Add(66);
        mi.MenuOrders.Selected = () => new[] { _unit, other };
        Click(mi, GlobalActionIds.DesignateTarget);
        pick.Enemy!.SetResult((int)EnemyId);
        Published(mi);
        foreach (long u in new[] { UnitId, 66L })
            Assert.True(OrderDesignations.Of(_w)!.TryGet(u, DesignationKind.Target, out _), $"unit {u}");

        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer(capacity: 32);
        new Hrot.Presentation.Orders.DesignationOverlayGizmo(() => new[] { _unit, other }).UpdateAndDraw(_w, 0f, buffer);
        int lines = 0;
        foreach (var p in buffer.GetFrame())
            if (p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Line
                && p.Color.Equals(Hrot.Presentation.Orders.DesignationOverlayGizmo.TargetColor)) lines++;
        Assert.Equal(2, lines);

        var nothing = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer(capacity: 32);
        new Hrot.Presentation.Orders.DesignationOverlayGizmo(() => Array.Empty<Entity>()).UpdateAndDraw(_w, 0f, nothing);
        Assert.Equal(0, nothing.Count);                                                 // only the SELECTION is drawn
    }

    /// <summary>⭐ O3 — the entity LIST answers through the same broker as a map pick: the choice lands as a completed entity pick
    /// for the field's path; the list leaves out the units being ordered.</summary>
    [Fact]
    public void CE1059_TheEntityList_AnswersThroughTheSamePick_AndLeavesOutTheOrderedUnits()
    {
        var broker = new Hrot.Presentation.Facades.MapPickBroker(new FakePick());
        Assert.False(broker.CanListEntities);
        string[]? asked = null;
        broker.EntityList = (presets, chosen) => { asked = presets; chosen((int)EnemyId); return true; };
        Assert.True(broker.CanListEntities);
        var scoped = new Fdp.Presentation.Editing.ScopedMapPickContext(broker, "$.orderForm1");
        Assert.True(scoped.CanListEntities);
        scoped.RequestEntityListPick("$.Who", new[] { "units_ground" });
        Assert.Equal(new[] { "units_ground" }, asked);
        Assert.True(scoped.TryConsumeEntityPick("$.Who", out var picked));
        Assert.Equal(EnemyId, picked.NetworkId);

        var entries = Hrot.Presentation.Orders.EntityListSource.Entries(_w, null, exclude: new[] { UnitId });
        Assert.Contains(entries, e => e.Tag is long id && id == EnemyId);
        Assert.DoesNotContain(entries, e => e.Tag is long id && id == UnitId);
    }

    /// <summary>⭐ DAR F7 (D-E, <c>DESIGN_Replay_Time_Axis_And_Review.md</c> §12j row 41) — a map built LIVE hides its order items (and their
    /// key shortcuts, which read the same menu) for as long as its world shows a recording, then offers them again. ⛔ The build-time
    /// predicate this replaces left the editor and CGF offering orders in a review: they build their map before any review starts.</summary>
    [Fact]
    public void DARF7_ALiveMap_HidesItsOrders_WhileItShowsARecording_AndOffersThemAgainAfter()
    {
        var (live, _) = Live();
        Assert.Contains("\"Move\"", ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit));

        Fdp.Toolkit.Replay.ReplayState.Enter(_w);   // a review or a replay starts on this world (R-3064)
        var inReview = ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit);
        Assert.DoesNotContain("\"Move\"", inReview);
        Assert.DoesNotContain($"\"id\":{GlobalActionIds.Possess},", inReview);
        Assert.DoesNotContain("Designate", inReview);

        Fdp.Toolkit.Replay.ReplayState.Leave(_w);   // back to live
        Assert.Contains("\"Move\"", ContextMenuProjectorGizmo.MenuJsonFor(_w, _unit));
        Assert.Contains(live.MenuOrders, live.InteractionSystems);
    }

    /// <summary>⭐ DAR F7 — Add Entity greys out while the map shows a recording, and gives the host's own reason back after.</summary>
    [Fact]
    public void DARF7_AddEntity_IsSuspended_WhileARecordingIsShown()
    {
        string? hostReason = null;
        var mi = MapInteractionPack.Build(new MapInteractionContext
        {
            World = _w,
            EntityAuthoring = new Hrot.UI.Common.AddEntity.EntityAuthoringInputs(() => null, () => null, () => null)
            {
                SuspendedReason = () => hostReason,
            },
        });
        var add = mi.EntityAuthoring!.AddEntity;
        Assert.Null(add.SuspendedReason);

        Fdp.Toolkit.Replay.ReplayState.Enter(_w);
        Assert.Equal("a recording is shown", add.SuspendedReason);

        Fdp.Toolkit.Replay.ReplayState.Leave(_w);
        hostReason = "loading";
        Assert.Equal("loading", add.SuspendedReason);
    }
}
