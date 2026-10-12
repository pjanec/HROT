using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Constants;
using Hrot.Common.Orders;
using Hrot.UI.Common.Facades;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐⭐ <c>CE-3169</c> (<c>docs/DESIGN_Entity_Control.md</c> §4.2, §7 S2) — the unit menu's ORDERS (⭐ <c>CE-2143</c>: every order the
    /// unit's class fulfils, from <see cref="OrderMenu"/> — was a fixed Move Here · Engage · Stop), ONE
    /// implementation registered by <see cref="MapInteractionPack"/> on every live map host. Move and Engage ask the operator for a
    /// follow-up pick (a point / an enemy) through the host's <see cref="IMapPickService"/>; Stop holds at once. Every order goes
    /// through <see cref="EntityOrders"/> — the same publish the HTTP order route makes, so a menu order and an AI player's order
    /// cannot drift apart.
    /// <para>⚠ A pick completes on a pool thread (<c>PickerToolHost</c> runs continuations asynchronously), so this system POLLS its
    /// pending pick each frame and publishes from the frame — never from the continuation.</para>
    /// <para>⚠ Orders are issued only where an operator can pick: a host whose <see cref="PickService"/> answers nothing (headless)
    /// ignores the three ids — a menu action relayed from a remote station must not issue the order a second time.</para>
    /// </summary>
    public sealed class MenuOrders : IEcsModuleSystem
    {
        private readonly EntityRepository _world;
        private Task<Hrot.Core.Mission.GeoPoint>? _location;
        private Task<int>? _enemy;
        private long _unit;
        private OrderInfo? _order;

        public MenuOrders(EntityRepository world) => _world = world ?? throw new ArgumentNullException(nameof(world));

        /// <summary>The host's map pick — set by every host that builds one (a resolver: the hosts build their pick after the map).</summary>
        public Func<IMapPickService?>? PickService { get; set; }

        /// <summary>True while a follow-up pick is waiting for the operator.</summary>
        public bool IsPicking => _location is { IsCompleted: false } || _enemy is { IsCompleted: false };

        /// <summary>Registers the menu ids on <paramref name="actions"/>: ⭐ CE-2143 one per ORDER (<see cref="OrderMenu"/> ids, looked up
        /// at click time, so a reloaded vocabulary needs no re-registration) and Possess (FPS).</summary>
        public void RegisterOn(Hrot.Common.Interactions.GlobalActionRegistry actions)
        {
            // ⭐ CE-3178 — LOCAL clicks only: a station's click relayed to another windowed node (every node holding the unit
            //   accepts the relay) would issue the order a second time there and open a pick on a window nobody clicked.
            for (int id = OrderMenu.ActionBase; id < OrderMenu.ActionBase + OrderMenu.MaxOrders; id++)
            {
                int actionId = id;
                actions.RegisterLocal(actionId, (_, unit) =>
                {
                    if (OrderMenu.TryGetById(actionId, out var order)) Begin(order, unit);
                });
            }
            // ⭐ CE-3174 (§8d) — Possess (FPS): this station takes the unit; the map follows PossessedBy into first person
            //   (PossessionStation). Like the orders, only where a pick exists — a windowed station, never a relayed headless node.
            actions.RegisterLocal(GlobalActionIds.Possess,  (_, unit) =>
            {
                if (PickService?.Invoke() is null || !TryNetworkId(unit, out long id)) return;
                Report(Orders()?.Possess(id));
            });
        }

        /// <summary>⭐ CE-2143 (D4-E) — the order's pick comes from its DTO's field type: a point, an entity, or none (issue now).</summary>
        private void Begin(OrderInfo order, Entity unit)
        {
            var pick = PickService?.Invoke();
            if (pick is null || !TryNetworkId(unit, out long id)) return;
            _unit = id;
            _order = order;
            _location = null;
            _enemy = null;
            switch (order.Pick)
            {
                case OrderPick.None:  Report(Orders()?.Order(id, order.Name, "{}")); break;
                case OrderPick.Point: _location = pick.PickLocationAsync(); break;
                case OrderPick.Entity: _enemy = pick.PickEntityAsync(); break;
            }
        }

        /// <summary>Turns a completed pick into its order; a cancelled or failed pick issues nothing.</summary>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_location is { IsCompleted: true } location)
            {
                _location = null;
                if (location.Status == TaskStatus.RanToCompletion && _order is { } order)
                {
                    var at = location.Result;
                    // The adapter answers geodetic when the node has a transform, else world metres in the same fields (x, y):
                    // without a transform an order's point travels as [y, x] (OrderGeo's fallback).
                    var point = Geo() != null ? new JsonArray(at.Latitude, at.Longitude) : new JsonArray(at.Longitude, at.Latitude);
                    Report(Orders()?.Order(_unit, order.Name, OrderMenu.Json(order, point)));
                }
            }
            if (_enemy is { IsCompleted: true } enemy)
            {
                _enemy = null;
                if (enemy.Status == TaskStatus.RanToCompletion && enemy.Result > 0 && enemy.Result != _unit && _order is { } order)
                    Report(Orders()?.Order(_unit, order.Name, OrderMenu.Json(order, JsonValue.Create((long)enemy.Result))));
            }
        }

        private EntityOrders? Orders()
        {
            var map = Map();
            return map is null ? null : new EntityOrders(_world, map, Geo());
        }

        private NetworkEntityMap? Map()
            => _world.HasSingletonManaged<NetworkEntityMap>() ? _world.GetSingletonManaged<NetworkEntityMap>() : null;

        private IGeographicTransform? Geo()
            => _world.HasSingletonManaged<IGeographicTransform>() ? _world.GetSingletonManaged<IGeographicTransform>() : null;

        private bool TryNetworkId(Entity unit, out long id)
        {
            id = 0;
            return !unit.IsNull && Map() is { } map && map.TryGetNetworkId(unit, out id);
        }

        private static void Report(OrderResult? result)
        {
            if (result is { Accepted: false } r)
                FdpLog<MenuOrders>.Warn("[Orders] menu order refused: {0}", r.Detail);
        }
    }
}
