using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Abstractions;
using MapMouseButton = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton;
using MapKeyboardKey = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey;
using Hrot.Common.Orders;
using Hrot.Map.Definitions.Behavior.Orders;
using System.Text.Json.Nodes;
using Hrot.UI.Common.Facades;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐⭐ <c>CE-3171</c> (<c>docs/DESIGN_Entity_Control.md</c> §8a) — what the RTS command tool's three gestures DO. It owns no state:
    /// the selection is the map's (written only through <see cref="SelectionChangeRequest"/>), the orders are
    /// <see cref="EntityOrders"/>' and the hit test is the station's map pick (<see cref="IMapPickService.EntityAt"/>).
    /// </summary>
    public sealed class RtsCommands
    {
        private readonly EntityRepository _world;
        private readonly Func<IReadOnlyCollection<Entity>> _selected;
        private readonly Func<IMapPickService?> _pick;

        public RtsCommands(EntityRepository world, Func<IReadOnlyCollection<Entity>> selected, Func<IMapPickService?> pick)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _selected = selected ?? throw new ArgumentNullException(nameof(selected));
            _pick = pick ?? throw new ArgumentNullException(nameof(pick));
        }

        /// <summary>LMB: the unit under the cursor becomes the selection (<paramref name="additive"/>: joins it); empty ground
        /// clears it (unless additive).</summary>
        public void Select(Vector2 worldXY, bool additive)
        {
            var hit = EntityAt(worldXY);
            if (hit.IsNull)
            {
                if (!additive) _world.Bus.PublishManaged(new SelectionChangeRequest { Mode = SelectionChangeMode.Clear });
                return;
            }
            _world.Bus.PublishManaged(new SelectionChangeRequest
            {
                Entities = new[] { hit }, Mode = additive ? SelectionChangeMode.Add : SelectionChangeMode.Replace,
            });
        }

        /// <summary>RMB: every selected unit attacks the HOSTILE under the cursor, or moves to the point (a friendly or neutral
        /// there is a move to its position). Returns how many orders were accepted.
        /// ⭐ CE-1132 — <paramref name="forceAttack"/> (Ctrl held): the unit under the cursor is ATTACKED whatever its side; empty ground
        /// is still a move. 🔒 User, 2026-10-16: <i>"how to switch to attack mode so i can click and entity and attack it? … would not
        /// matter if something like ctrl or shift-click activated the attack"</i>.</summary>
        public int Command(Vector2 worldXY, bool forceAttack = false)
        {
            var map = Map();
            if (map is null) return 0;
            var orders = new EntityOrders(_world, map, Geo());
            var hit = EntityAt(worldXY);
            int accepted = 0;
            foreach (var unit in _selected())
            {
                if (unit.IsNull || unit.Equals(hit) || !map.TryGetNetworkId(unit, out long id)) continue;
                // ⭐ CE-2143 (D4-F) — the order a GESTURE issues ([Order(Gesture = …)]), never a behaviour name: a hostile under the
                //   cursor is the EnemyClick order (Attack), anything else the GroundClick order (Move) to the point.
                // ⭐ CE-2148 (F6) — the click is the gesture's CONTEXT: it fills the order's one missing [Required] field of its kind;
                //   an order it cannot complete goes to the order form (MenuOrders.OpenForm), never half-filled.
                OrderResult r = !hit.IsNull && (forceAttack || IsHostile(unit, hit)) && map.TryGetNetworkId(hit, out long target)
                    ? Gesture(orders, id, OrderGesture.EnemyClick, new OrderContext(target, null))
                    : Gesture(orders, id, OrderGesture.GroundClick, new OrderContext(null, Point(worldXY)));
                if (r.Accepted) accepted++;
                else FdpLog<RtsCommands>.Warn("[Orders] RTS order refused: {0}", r.Detail);
            }
            return accepted;
        }

        /// <summary>Hostile = the other side, never neutral (<see cref="EntityInfo.ForceId"/>).</summary>
        public bool IsHostile(Entity unit, Entity other)
        {
            if (!_world.IsComponentTypeRegistered<EntityInfo>()) return false;
            if (!_world.HasComponent<EntityInfo>(unit) || !_world.HasComponent<EntityInfo>(other)) return false;
            var mine = _world.GetComponentRO<EntityInfo>(unit).ForceId;
            var theirs = _world.GetComponentRO<EntityInfo>(other).ForceId;
            return theirs != mine && theirs != ForceId.Neutral;
        }

        private OrderResult Gesture(EntityOrders orders, long id, OrderGesture gesture, OrderContext context)
        {
            if (OrderMenu.ByGesture(gesture) is not { } order)
                return new OrderResult(OrderRefusal.BadRequest, $"No order is issued by a {gesture}.", string.Empty, string.Empty);
            // ⭐ CE-2149 — the unit's designations fill what the click did not (one draft per unit, so the per-unit store applies)
            var store = OrderDesignations.Of(_world);
            var map = Map();
            var draft = OrderMenu.Draft(order, new[] { id }, context, json => new[] { orders.Order(id, order.Name, json) },
                designation: store != null && map != null ? store.Lookup(n => OrderDesignations.IsLive(_world, map, n)) : null);
            if (draft.IsComplete) return draft.Send(draft.Params.ToJsonString())[0];
            MenuOrders.OpenForm(_world, draft);
            return new OrderResult(OrderRefusal.BadRequest, $"{order.Name} needs the order form.", string.Empty, string.Empty);
        }

        /// <summary>The clicked point as an order's geodetic point (without a transform, [y, x] — OrderGeo's fallback).</summary>
        private JsonArray Point(Vector2 at) => OrderPoint(_world, at);

        /// <summary>⭐ <c>CE-1213</c> — a world point as an order's geodetic point, for any front-end (the first-person command wheel
        /// too): <c>[lat, lon]</c>, or <c>[y, x]</c> without a transform (OrderGeo's fallback).</summary>
        public static JsonArray OrderPoint(EntityRepository world, Vector2 at)
        {
            var geo = world.HasSingletonManaged<IGeographicTransform>() ? world.GetSingletonManaged<IGeographicTransform>() : null;
            if (geo == null) return new JsonArray((double)at.Y, (double)at.X);
            var (lat, lon, _) = geo.ToGeodetic(new Vector3(at.X, at.Y, 0f));
            return new JsonArray(lat, lon);
        }

        private Entity EntityAt(Vector2 worldXY)
        {
            int netId = _pick()?.EntityAt(worldXY) ?? -1;
            return netId > 0 && Map() is { } map && map.TryGetEntity(netId, out var e) ? e : Entity.Null;
        }

        private NetworkEntityMap? Map()
            => _world.HasSingletonManaged<NetworkEntityMap>() ? _world.GetSingletonManaged<NetworkEntityMap>() : null;

        private IGeographicTransform? Geo()
            => _world.HasSingletonManaged<IGeographicTransform>() ? _world.GetSingletonManaged<IGeographicTransform>() : null;
    }

    /// <summary>
    /// ⭐ <c>CE-3171</c> — the RTS command tool's input: EXCLUSIVE focus (which also suppresses every map context menu,
    /// <c>DebugGizmoLayer</c>) and raw input. LMB release selects (Shift adds), an RMB CLICK commands, Esc leaves. Draws nothing:
    /// the selection already highlights the units.
    /// <para>⭐ CE-1077 (DESIGN_Entity_Control.md §13.6) — 🔒 user, 2026-10-14: "in RTS mode a right click commands and a right drag
    /// only looks". The order goes on the RMB RELEASE, at the press point, and only when the mouse did not move in between (at most
    /// <see cref="ClickMaxMoves"/> moved frames — the terminal reports one drag update per frame the mouse moves): a right-drag is the
    /// 3-D camera's look (or the 2-D pan) and orders nothing. It used to command on the PRESS, so every look also ordered a move.</para>
    /// </summary>
    public sealed class RtsCommandGizmo : IEntityStatefulGizmo
    {
        private readonly RtsCommands _commands;
        private readonly Action _onRemove;
        private readonly RtsModeState? _mode;
        private bool _shift;
        private bool _ctrl;
        private bool _rightDown;
        private int _rightMoves;
        private Vector2 _rightAt;

        /// <summary>⭐ CE-1077 — mouse-moved frames a right click may have and still be a click (hand jitter), not a drag.</summary>
        public const int ClickMaxMoves = 3;

        public RtsCommandGizmo(RtsCommands commands, Action onRemove, RtsModeState? mode = null)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
            _mode = mode;
            if (_mode != null) { _mode.Armed = true; _mode.ForceAttack = false; }
        }

        public bool RequiresExclusiveFocus => true;
        public bool WantsRawInput => true;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw) { }

        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            // ⭐ CE-1075 — the terminal packs the held modifiers into the button's high bits (MapMouseButton.ShiftMask …): a
            //   Shift-click arrived as Left|ShiftMask and matched neither branch, so "Shift adds" never added. The mask IS the Shift.
            bool shift = _shift || (button & MapMouseButton.ShiftMask) != 0;
            bool ctrl = _ctrl || (button & MapMouseButton.CtrlMask) != 0;   // ⭐ CE-1132 — Ctrl = attack what is under the cursor
            button &= ~(MapMouseButton.ShiftMask | MapMouseButton.CtrlMask | MapMouseButton.AltMask);
            var xy = new Vector2(worldPos.X, worldPos.Y);
            if (button == MapMouseButton.Left && !isPressed) _commands.Select(xy, shift);
            else if (button == MapMouseButton.Right && isPressed) { _rightDown = true; _rightMoves = 0; _rightAt = xy; }
            else if (button == MapMouseButton.Right && _rightDown)
            {
                _rightDown = false;
                if (_rightMoves <= ClickMaxMoves) _commands.Command(_rightAt, ctrl);   // ⭐ CE-1077 — a click, not a look
            }
        }

        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            key &= ~(MapKeyboardKey.ShiftMask | MapKeyboardKey.CtrlMask | MapKeyboardKey.AltMask);   // ⭐ CE-1075 — as for the mouse
            if (key is MapKeyboardKey.LeftShift or MapKeyboardKey.RightShift) _shift = isPressed;
            else if (key is MapKeyboardKey.LeftControl or MapKeyboardKey.RightControl)
            {
                _ctrl = isPressed;
                if (_mode != null) _mode.ForceAttack = isPressed;   // ⭐ CE-1132 — the overlay shows the attack reticle while held
            }
            else if (key == MapKeyboardKey.Escape && isPressed) _onRemove();
        }

        public void OnDragUpdate(Vector3 worldPos) { if (_rightDown) _rightMoves++; }
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }
        public void Dispose() { if (_mode != null) { _mode.Armed = false; _mode.ForceAttack = false; } }
    }

    /// <summary>⭐ CE-1132 — what the RTS tool is doing, for the map's overlay (the controls legend, the attack reticle): armed while the
    /// tool's gizmo lives, <see cref="ForceAttack"/> while Ctrl is held. One per world (<see cref="MapUiStates"/>).</summary>
    public sealed class RtsModeState
    {
        public bool Armed;
        public bool ForceAttack;

        /// <summary>The controls, as the legend lists them.</summary>
        public static readonly string[] Legend =
        {
            "RTS COMMAND MODE",
            "LMB  select    Shift+LMB  add    drag  box select",
            "RMB  move here / attack the enemy under the cursor",
            "Ctrl+RMB  attack whatever is under the cursor",
            "RMB drag  look    Esc  leave",
        };
    }
}
