using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Abstractions;
using MapMouseButton = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton;
using MapKeyboardKey = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey;
using Hrot.Common.Constants;
using Hrot.Core.Mission;
using Hrot.ScenarioEditor.Map;
using Hrot.UI.Common.Facades;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐⭐ <c>CE-3171</c> (docs/DESIGN_Entity_Control.md §8a, §7 S3) — the RTS command tool: LMB selects (Shift adds, empty ground
/// clears) through the one selection request; RMB on a hostile attacks it, anywhere else moves every selected unit there; the
/// tool is exclusive (so the map's menus are off) and Esc leaves; it exists only where the map can order units.
/// </summary>
public sealed class RtsCommandToolTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly NetworkEntityMap _map = new();
    private readonly HitAt _pick = new();

    public RtsCommandToolTests()
    {
        _w.RegisterComponent<SimTransform>();
        _w.RegisterComponent<NetworkIdentity>();
        _w.RegisterComponent<EntityInfo>();
        _w.SetSingletonManaged(_map);
    }

    public void Dispose() => _w.Dispose();

    /// <summary>A station whose map has one entity under each registered point.</summary>
    private sealed class HitAt : IMapPickService
    {
        public readonly Dictionary<Vector2, int> Under = new();
        public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default) => new TaskCompletionSource<GeoPoint>().Task;
        public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default) => new TaskCompletionSource<int>().Task;
        public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());
        public int EntityAt(Vector2 worldXY) => Under.TryGetValue(worldXY, out int id) ? id : -1;
    }

    private Entity Unit(long id, ForceId side, Vector2 at)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new SimTransform { Position = new Vector3(at, 0) });
        _w.AddComponent(e, new NetworkIdentity(id));
        _w.AddComponent(e, new EntityInfo { ForceId = side });
        _map.Register(id, e);
        _pick.Under[at] = (int)id;
        _w.SetDisType(e, new DISEntityType { Value = (3UL << 56) | (1UL << 48) | (225UL << 32) | (1UL << 24) });   // ⭐ CE-2143 — a soldier
        return e;
    }

    private RtsCommands Commands(params Entity[] selected) => new(_w, () => selected, () => _pick);

    private T[] Read<T>() where T : class
    {
        _w.Bus.SwapBuffers();
        return _w.Bus.ReadManaged<T>().ToArray();
    }

    [Fact]
    public void CE3171_LeftClick_SelectsTheUnit_ShiftAdds_EmptyGroundClears()
    {
        var a = Unit(10, ForceId.Friend, new Vector2(1, 1));
        var rts = Commands();

        rts.Select(new Vector2(1, 1), additive: false);
        var replace = Assert.Single(Read<SelectionChangeRequest>());
        Assert.Equal(SelectionChangeMode.Replace, replace.Mode);
        Assert.Equal(a, Assert.Single(replace.Entities));

        rts.Select(new Vector2(1, 1), additive: true);
        Assert.Equal(SelectionChangeMode.Add, Assert.Single(Read<SelectionChangeRequest>()).Mode);

        rts.Select(new Vector2(50, 50), additive: false);
        Assert.Equal(SelectionChangeMode.Clear, Assert.Single(Read<SelectionChangeRequest>()).Mode);
        rts.Select(new Vector2(50, 50), additive: true);   // Shift on empty ground keeps the selection
        Assert.Empty(Read<SelectionChangeRequest>());
    }

    [Fact]
    public void CE3171_RightClick_OnAHostile_AttacksIt_WithEverySelectedUnit()
    {
        var a = Unit(10, ForceId.Friend, new Vector2(1, 1));
        var b = Unit(11, ForceId.Friend, new Vector2(2, 1));
        Unit(20, ForceId.Hostile, new Vector2(40, 0));

        Assert.Equal(2, Commands(a, b).Command(new Vector2(40, 0)));
        var orders = Read<AssignTacticalIntentEvent>();
        Assert.Equal(2, orders.Length);
        Assert.All(orders, o => Assert.Equal("Order.Attack", o.IntentId));     // ⭐ CE-2143 — the EnemyClick order; the unit picks how
        Assert.All(orders, o => Assert.Contains("\"Target\":20", o.JsonParams));
        Assert.Equal(new[] { a, b }, orders.Select(o => o.Entity).ToArray());
    }

    [Fact]
    public void CE3171_RightClick_OnGround_OrAFriendOrANeutral_MovesThere()
    {
        var a = Unit(10, ForceId.Friend, new Vector2(1, 1));
        Unit(12, ForceId.Friend, new Vector2(5, 5));
        Unit(30, ForceId.Neutral, new Vector2(9, 9));
        var rts = Commands(a);

        foreach (var at in new[] { new Vector2(120, -40), new Vector2(5, 5), new Vector2(9, 9) })
        {
            Assert.Equal(1, rts.Command(at));
            var move = Assert.Single(Read<AssignTacticalIntentEvent>());
            Assert.Equal("Order.Move", move.IntentId);                            // ⭐ CE-2143 — the GroundClick order
            Assert.Contains($"\"Location\":[{at.Y},{at.X}]", move.JsonParams);   // no transform: [y, x]
        }
    }

    [Fact]
    public void CE3171_TheGizmo_IsExclusive_RoutesItsGestures_AndEscLeaves()
    {
        var a = Unit(10, ForceId.Friend, new Vector2(1, 1));
        Unit(20, ForceId.Hostile, new Vector2(40, 0));
        bool removed = false;
        var gizmo = new RtsCommandGizmo(Commands(a), () => removed = true);
        Assert.True(gizmo.RequiresExclusiveFocus, "exclusive focus is what switches the map's menus off");
        Assert.True(gizmo.WantsRawInput);

        gizmo.OnKeyEvent(MapKeyboardKey.LeftShift, isPressed: true);
        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(1, 1, 0));
        Assert.Equal(SelectionChangeMode.Add, Assert.Single(Read<SelectionChangeRequest>()).Mode);

        gizmo.OnMouseEvent(MapMouseButton.Right, isPressed: true, new Vector3(40, 0, 0));
        Assert.Empty(Read<AssignTacticalIntentEvent>());               // ⭐ CE-1077 — not on the press …
        gizmo.OnMouseEvent(MapMouseButton.Right, isPressed: false, new Vector3(40, 0, 0));
        Assert.Equal("Order.Attack", Assert.Single(Read<AssignTacticalIntentEvent>()).IntentId);   // … on the click's release
        Assert.False(removed, "RMB commands; it does not cancel the tool");

        gizmo.OnKeyEvent(MapKeyboardKey.Escape, isPressed: true);
        Assert.True(removed);
    }

    /// <summary>
    /// ⭐⭐ CE-1075 / CE-1071 — END TO END on the backend half: View ▸ RTS Command Mode arms the tool, it takes the input focus and puts
    /// its capture binding in the frame, and a mouse event on the map's bus reaches it (the path a real click takes after the
    /// terminal). 🔒 User, 2026-10-14: "In RTS mode the key shortcut do not work, not even mouse click do anything". 🔴 Two defects
    /// broke it: the frame was FULL and the binding was dropped (so the terminal never routed a click — CE-1071), and a Shift-click
    /// arrived as <c>Left | ShiftMask</c>, which matched no branch (CE-1075). The older rails called the gizmo directly and saw neither.
    /// </summary>
    [Fact]
    public void CE1075_AClick_ReachesTheArmedTool_ThroughTheMapsBus_EvenWithAFullFrame_AndShiftClickAdds()
    {
        var a = Unit(10, ForceId.Friend, new Vector2(1, 1));
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w, BufferCapacity = 64 });
        mi.MenuOrders.PickService = () => _pick;
        Assert.True(mi.Actions.TryGetHandler(GlobalActionIds.ToggleRtsCommand, out var toggle));
        toggle!(_w, Entity.Null);

        for (int i = 0; i < 200; i++)                                         // a frame far over capacity
            mi.Buffer.DrawLine(Vector3.Zero, Vector3.UnitX, new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(1, 2, 3, 255));
        mi.GlobalManager.Execute(_w, 0.016f);
        Assert.Contains(mi.Buffer.GetFrame().ToArray(), p => p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.InputCaptureBinding);

        void Click(MapMouseButton button, Vector3 at)
        {
            mi.InteractionBus.Publish(new GizmoMouseEvent { Button = button, IsPressed = false, WorldPos = at });
            mi.InteractionBus.SwapBuffers();
            mi.GlobalManager.Execute(_w, 0.016f);
        }

        Click(MapMouseButton.Left, new Vector3(1, 1, 0));
        var replace = Assert.Single(Read<SelectionChangeRequest>());
        Assert.Equal(SelectionChangeMode.Replace, replace.Mode);
        Assert.Equal(a, Assert.Single(replace.Entities));

        Click(MapMouseButton.Left | MapMouseButton.ShiftMask, new Vector3(1, 1, 0));   // what the terminal sends for a Shift-click
        Assert.Equal(SelectionChangeMode.Add, Assert.Single(Read<SelectionChangeRequest>()).Mode);
    }

    /// <summary>
    /// ⭐ CE-1133 — 🔒 user, 2026-10-16: <i>"The right-click-to-move also seems to affect also for no longer selected entities."</i>
    /// END TO END through the pack: select A and B, then click C alone (a Replace), let the selection system apply each request as
    /// the host does, then right-click the ground — exactly C is ordered. Pins that the order loop reads the selection the map
    /// shows, never an earlier one.
    /// </summary>
    [Fact]
    public void CE1133_AfterReselecting_ARightClick_OrdersOnlyTheUnitsSelectedNow()
    {
        _w.RegisterComponent<Hrot.IG.Components.SelectionState>();
        var a = Unit(10, ForceId.Friend, new Vector2(1, 1));
        var b = Unit(11, ForceId.Friend, new Vector2(2, 1));
        var c = Unit(12, ForceId.Friend, new Vector2(3, 1));
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w, BufferCapacity = 64 });
        mi.MenuOrders.PickService = () => _pick;
        Assert.True(mi.Actions.TryGetHandler(GlobalActionIds.ToggleRtsCommand, out var toggle));
        toggle!(_w, Entity.Null);
        mi.GlobalManager.Execute(_w, 0.016f);

        void Mouse(MapMouseButton button, bool pressed, Vector3 at)
        {
            mi.InteractionBus.Publish(new GizmoMouseEvent { Button = button, IsPressed = pressed, WorldPos = at });
            mi.InteractionBus.SwapBuffers();
            mi.GlobalManager.Execute(_w, 0.016f);
            _w.Bus.SwapBuffers();
            mi.SelectionRequests.Execute(_w, 0.016f);                       // the host applies the request in its tick
        }

        Mouse(MapMouseButton.Left, false, new Vector3(1, 1, 0));
        Mouse(MapMouseButton.Left | MapMouseButton.ShiftMask, false, new Vector3(2, 1, 0));
        Assert.Equal(new[] { a, b }, mi.Selection.SelectedEntities.ToArray());

        Mouse(MapMouseButton.Left, false, new Vector3(3, 1, 0));             // C alone: A and B are deselected
        Assert.Equal(new[] { c }, mi.Selection.SelectedEntities.ToArray());

        Mouse(MapMouseButton.Right, true, new Vector3(80, 80, 0));
        mi.InteractionBus.Publish(new GizmoMouseEvent { Button = MapMouseButton.Right, IsPressed = false, WorldPos = new Vector3(80, 80, 0) });
        mi.InteractionBus.SwapBuffers();
        mi.GlobalManager.Execute(_w, 0.016f);
        var move = Assert.Single(Read<AssignTacticalIntentEvent>());
        Assert.Equal(c, move.Entity);
    }

    /// <summary>
    /// ⭐⭐ CE-1077 (docs/DESIGN_Entity_Control.md §13.6) — 🔒 user, 2026-10-14: "in RTS mode a right click commands and a right drag
    /// only looks". A right press, the mouse moving (the terminal's drag updates — the 3-D look, the 2-D pan) and a release orders
    /// nothing; a still click (a little jitter allowed) orders at the PRESS point, where the operator clicked.
    /// </summary>
    [Fact]
    public void CE1077_ARightDrag_OnlyLooks_ARightClick_CommandsAtThePressPoint()
    {
        var a = Unit(10, ForceId.Friend, new Vector2(1, 1));
        Unit(20, ForceId.Hostile, new Vector2(40, 0));
        var gizmo = new RtsCommandGizmo(Commands(a), () => { });

        gizmo.OnMouseEvent(MapMouseButton.Right, isPressed: true, new Vector3(40, 0, 0));
        for (int i = 0; i < 12; i++) gizmo.OnDragUpdate(new Vector3(40 + i, i, 0));   // looking around
        gizmo.OnMouseEvent(MapMouseButton.Right, isPressed: false, new Vector3(60, 9, 0));
        Assert.Empty(Read<AssignTacticalIntentEvent>());

        gizmo.OnMouseEvent(MapMouseButton.Right, isPressed: true, new Vector3(40, 0, 0));
        gizmo.OnDragUpdate(new Vector3(40.1f, 0, 0));                                    // a hand's jitter
        gizmo.OnDragUpdate(new Vector3(40, 0.1f, 0));
        gizmo.OnMouseEvent(MapMouseButton.Right, isPressed: false, new Vector3(40.2f, 0.1f, 0));
        var order = Assert.Single(Read<AssignTacticalIntentEvent>());
        Assert.Equal("Order.Attack", order.IntentId);                                    // the hostile under the PRESS
        Assert.Contains("\"Target\":20", order.JsonParams);

        gizmo.OnMouseEvent(MapMouseButton.Right, isPressed: false, new Vector3(40, 0, 0)); // a release with no press of its own
        Assert.Empty(Read<AssignTacticalIntentEvent>());
    }

    [Fact]
    public void CE3171_TheTool_IsOfferedOnALiveMap_ArmsWithAPick_AndIsAbsentInAReplay()
    {
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w });
        Assert.True(mi.Actions.TryGetHandler(GlobalActionIds.ToggleRtsCommand, out var toggle));
        Assert.False(mi.Tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.RtsCommand), "headless: no pick, nothing to click");

        mi.MenuOrders.PickService = () => _pick;
        toggle!(_w, Entity.Null);
        Assert.Equal(Hrot.ScenarioEditor.Tools.ScenarioToolIds.RtsCommand, mi.Tools.ActiveModal?.Id);

        using var replayWorld = new EntityRepository();
        var replay = MapInteractionPack.Build(new MapInteractionContext { World = replayWorld, IsReplayActive = static () => true });
        Assert.False(replay.Actions.TryGetHandler(GlobalActionIds.ToggleRtsCommand, out _));
    }
}
