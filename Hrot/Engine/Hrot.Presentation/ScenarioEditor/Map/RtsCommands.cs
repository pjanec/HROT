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
        /// there is a move to its position). Returns how many orders were accepted.</summary>
        public int Command(Vector2 worldXY)
        {
            var map = Map();
            if (map is null) return 0;
            var orders = new EntityOrders(_world, map, Geo());
            var hit = EntityAt(worldXY);
            int accepted = 0;
            foreach (var unit in _selected())
            {
                if (unit.IsNull || unit.Equals(hit) || !map.TryGetNetworkId(unit, out long id)) continue;
                OrderResult r = !hit.IsNull && IsHostile(unit, hit) && map.TryGetNetworkId(hit, out long target)
                    ? orders.Attack(id, target)
                    : Move(orders, id, worldXY);
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

        private OrderResult Move(EntityOrders orders, long id, Vector2 at)
            => orders.Move(id, x: at.X, y: at.Y);   // local metres; converted to geodetic by the service when the node can

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
    /// <c>DebugGizmoLayer</c>) and raw input. LMB release selects (Shift adds), RMB press commands, Esc leaves. Draws nothing:
    /// the selection already highlights the units.
    /// </summary>
    public sealed class RtsCommandGizmo : IEntityStatefulGizmo
    {
        private readonly RtsCommands _commands;
        private readonly Action _onRemove;
        private bool _shift;

        public RtsCommandGizmo(RtsCommands commands, Action onRemove)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
        }

        public bool RequiresExclusiveFocus => true;
        public bool WantsRawInput => true;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw) { }

        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            var xy = new Vector2(worldPos.X, worldPos.Y);
            if (button == MapMouseButton.Left && !isPressed) _commands.Select(xy, _shift);
            else if (button == MapMouseButton.Right && isPressed) _commands.Command(xy);
        }

        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            if (key is MapKeyboardKey.LeftShift or MapKeyboardKey.RightShift) _shift = isPressed;
            else if (key == MapKeyboardKey.Escape && isPressed) _onRemove();
        }

        public void OnDragUpdate(Vector3 worldPos) { }
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }
        public void Dispose() { }
    }
}
