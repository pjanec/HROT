using System;
using System.Collections.Generic;
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
        private OrderDraft? _draft;
        private DesignationKind? _designating;   // ⭐ CE-2149 — the pending pick sets a designation, not an order
        private long[] _designateFor = Array.Empty<long>();   // ⭐ CE-1059 (U6) — every unit the pending designation is for
        private Entity _embarking = Entity.Null;              // ⭐ CE-1230 — the unit whose vehicle pick is pending

        public MenuOrders(EntityRepository world) => _world = world ?? throw new ArgumentNullException(nameof(world));

        /// <summary>The host's map pick — set by every host that builds one (a resolver: the hosts build their pick after the map).</summary>
        public Func<IMapPickService?>? PickService { get; set; }

        /// <summary>⭐ <c>CE-1059</c> (U6, design §13.12 O6) — the map's selection: Designate › Set… and Order with options apply to
        /// every selected unit when the clicked unit is one of them. Set by <see cref="MapInteractionPack"/>; null ⇒ the clicked unit.</summary>
        public Func<IReadOnlyCollection<Entity>>? Selected { get; set; }

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
            // ⭐ CE-2149 (§13.3) — the Designate submenu: one pick each (presets passed), stored per unit for a later order's missing
            //   [Required] field; Clear forgets them. LOCAL only, like the orders.
            actions.RegisterLocal(GlobalActionIds.DesignateTarget, (_, unit) => BeginDesignate(DesignationKind.Target, unit));
            actions.RegisterLocal(GlobalActionIds.DesignatePoint,  (_, unit) => BeginDesignate(DesignationKind.MapPoint, unit));
            actions.RegisterLocal(GlobalActionIds.DesignateRoute,  (_, unit) => BeginDesignate(DesignationKind.Route, unit));
            actions.RegisterLocal(GlobalActionIds.DesignateArea,   (_, unit) => BeginDesignate(DesignationKind.Area, unit));
            actions.RegisterLocal(GlobalActionIds.DesignateClear,  (_, unit) =>
            {
                if (PickService?.Invoke() is null || OrderDesignations.Of(_world) is not { } store) return;
                foreach (long id in UnitsOf(unit)) store.ClearAll(id);   // ⭐ CE-1059 — the selection
            });
            // ⭐ CE-1059 (§13.12 O5) — Order with options: the ORDER FORM for any order, prefilled, for the selection.
            for (int i = 0; i < GlobalActionIds.OrderWithOptionsMax; i++)
            {
                int orderId = OrderMenu.ActionBase + i;
                actions.RegisterLocal(GlobalActionIds.OrderWithOptionsBase + i, (_, unit) =>
                {
                    if (OrderMenu.TryGetById(orderId, out var order)) BeginForm(order, unit);
                });
            }
            // ⭐ CE-3174 (§8d) — Possess (FPS): this station takes the unit; the map follows PossessedBy into first person
            //   (PossessionStation). Like the orders, only where a pick exists — a windowed station, never a relayed headless node.
            actions.RegisterLocal(GlobalActionIds.Possess,  (_, unit) =>
            {
                if (PickService?.Invoke() is null || !TryNetworkId(unit, out long id)) return;
                Report(Orders()?.Possess(id));
            });
            // ⭐ CE-1230 (canvas context menu §11 OM-2, OM-3, R-1019) — Disembark at once; Embark… picks the VEHICLE on the map (the pick
            //   Engage uses), then the one embark path. LOCAL, and only where a pick exists, like the orders.
            actions.RegisterLocal(GlobalActionIds.Disembark, (_, unit) =>
            {
                if (PickService?.Invoke() is null) return;
                Hrot.Common.Orders.EmbarkOrders.Disembark(_world, unit);
            });
            actions.RegisterLocal(GlobalActionIds.Embark, (_, unit) =>
            {
                var pick = PickService?.Invoke();
                if (pick is null || !_world.IsAlive(unit)) return;
                _order = null;
                _draft = null;
                _designating = null;
                _location = null;
                _unit = TryNetworkId(unit, out long id) ? id : 0;
                _embarking = unit;
                _enemy = pick.PickEntityAsync(null);
            });
        }

        /// <summary>⭐ CE-2143 (D4-E) / ⭐ <c>CE-2148</c> (F6) — the order goes as a DRAFT: complete ⇒ issued now; one missing
        /// <c>[Required]</c> pick ⇒ the operator picks it (an entity pick narrowed by the field's <c>[MapPickableEntity]</c> presets);
        /// more ⇒ the order form (<see cref="IOrderForm"/>), or refused when this host has none.</summary>
        private void Begin(OrderInfo order, Entity unit)
        {
            var pick = PickService?.Invoke();
            if (pick is null || !TryNetworkId(unit, out long id)) return;
            _unit = id;
            _order = order;
            _designating = null;
            _embarking = Entity.Null;
            _location = null;
            _enemy = null;
            _draft = OrderMenu.Draft(order, new[] { id }, OrderContext.None, json => Send(new[] { id }, order, json),
                designation: DesignationLookup());
            if (_draft.IsComplete) { Report(Send(_draft)); return; }
            switch (order.Pick)
            {
                case OrderPick.Point:  _location = pick.PickLocationAsync(); break;
                case OrderPick.Entity: _enemy = pick.PickEntityAsync(order.PickTarget?.Presets is { Count: > 0 } p ? System.Linq.Enumerable.ToArray(p) : null); break;
                default: OpenForm(_world, _draft); break;
            }
        }

        /// <summary>⭐ <c>CE-1059</c> (§13.12 O5) — "Order with options › X": the order form for X, prefilled (gesture none, the
        /// designations of a one-unit draft), for the selection the clicked unit is in.</summary>
        private void BeginForm(OrderInfo order, Entity unit)
        {
            if (PickService?.Invoke() is null) return;
            var units = UnitsOf(unit);
            if (units.Length == 0) return;
            OpenForm(_world, OrderMenu.Draft(order, units, OrderContext.None, json => Send(units, order, json),
                designation: DesignationLookup()));
        }

        /// <summary>⭐ <c>CE-1059</c> (O6) — the units a click on <paramref name="unit"/> means: the whole selection when the unit is
        /// in it, else the unit alone (network ids).</summary>
        private long[] UnitsOf(Entity unit)
        {
            if (!TryNetworkId(unit, out long clicked)) return Array.Empty<long>();
            var selected = Selected?.Invoke();
            if (selected is null || !System.Linq.Enumerable.Contains(selected, unit)) return new[] { clicked };
            var ids = new List<long>();
            foreach (var e in selected)
                if (TryNetworkId(e, out long id)) ids.Add(id);
            return ids.ToArray();
        }

        /// <summary>⭐ <c>CE-2149</c> (§13.3) — Designate › Set…: one pick of the kind's shape (an entity pick narrowed to routes or areas),
        /// stored for the unit when it completes (⭐ <c>CE-1059</c>: for every selected unit when the clicked one is selected).</summary>
        private void BeginDesignate(DesignationKind kind, Entity unit)
        {
            var pick = PickService?.Invoke();
            if (pick is null || !TryNetworkId(unit, out long id)) return;
            _embarking = Entity.Null;
            _unit = id;
            _designateFor = UnitsOf(unit);
            _order = null;
            _draft = null;
            _designating = kind;
            _location = null;
            _enemy = null;
            if (kind == DesignationKind.MapPoint) _location = pick.PickLocationAsync();
            else _enemy = pick.PickEntityAsync(kind switch
            {
                DesignationKind.Route => new[] { OrderDesignations.RoutePreset },
                DesignationKind.Area => new[] { OrderDesignations.AreaPreset },
                _ => null,
            });
        }

        /// <summary>Turns a completed pick into its order (or ⭐ <c>CE-2149</c> its designation); a cancelled or failed pick does nothing.</summary>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_location is { IsCompleted: true } location)
            {
                _location = null;
                if (location.Status == TaskStatus.RanToCompletion)
                {
                    var at = location.Result;
                    // The adapter answers geodetic when the node has a transform, else world metres in the same fields (x, y):
                    // without a transform an order's point travels as [y, x] (OrderGeo's fallback).
                    var point = Geo() != null ? new JsonArray(at.Latitude, at.Longitude) : new JsonArray(at.Longitude, at.Latitude);
                    if (_designating is DesignationKind kind) SetDesignation(kind, point);
                    else if (_order is { } order && _draft is { } draft)
                        Report(Send(OrderMenu.Draft(order, draft.Units, new OrderContext(null, point), draft.Send, draft.Params, DesignationLookup())));
                }
            }
            if (_enemy is { IsCompleted: true } enemy)
            {
                _enemy = null;
                if (enemy.Status == TaskStatus.RanToCompletion && enemy.Result > 0 && enemy.Result != _unit)
                {
                    if (!_embarking.IsNull)   // ⭐ CE-1230 — the picked VEHICLE
                    {
                        if (Map() is { } map && map.TryGetEntity(enemy.Result, out var vehicle))
                            Hrot.Common.Orders.EmbarkOrders.Embark(_world, _embarking, vehicle);
                    }
                    else if (_designating is DesignationKind kind) SetDesignation(kind, JsonValue.Create((long)enemy.Result));
                    else if (_order is { } order && _draft is { } draft)
                        Report(Send(OrderMenu.Draft(order, draft.Units, new OrderContext(enemy.Result, null), draft.Send, draft.Params, DesignationLookup())));
                }
                _embarking = Entity.Null;
            }
        }

        private void SetDesignation(DesignationKind kind, JsonNode value)
        {
            var store = OrderDesignations.For(_world);
            foreach (long unit in _designateFor) store.Set(unit, kind, value);
        }

        /// <summary>⭐ <c>CE-2149</c> — this station's designations as the draft's lookup (null when nothing was ever designated).</summary>
        private Func<long, DesignationKind, JsonNode?>? DesignationLookup()
            => OrderDesignations.Of(_world) is { } store && Map() is { } map
                ? store.Lookup(id => OrderDesignations.IsLive(_world, map, id))
                : null;

        /// <summary>⭐ <c>CE-2148</c> — a draft the gesture could not complete goes to the host's order form (the ui lane's
        /// <see cref="IOrderForm"/>, a world singleton); without one it is refused, said in the log — ⛔ never sent half-filled.</summary>
        internal static void OpenForm(EntityRepository world, OrderDraft draft)
        {
            if (world.HasSingletonManaged<IOrderForm>() && world.GetSingletonManaged<IOrderForm>() is { } form) form.Open(draft);
            else FdpLog<MenuOrders>.Warn("[Orders] {0} needs the order form ({1} missing) — none on this host.",
                draft.Order.Name, string.Join(", ", System.Linq.Enumerable.Select(draft.Missing, f => f.Name)));
        }

        /// <summary>Issues a draft (only when complete — <see cref="EntityOrders"/> would refuse it anyway, naming the field).</summary>
        private static OrderResult? Send(OrderDraft draft)
        {
            var results = draft.Send(draft.Params.ToJsonString());
            return results.Count > 0 ? results[0] : null;
        }

        private IReadOnlyList<OrderResult> Send(long[] units, OrderInfo order, string json)
        {
            var orders = Orders();
            if (orders is null) return Array.Empty<OrderResult>();
            var results = new OrderResult[units.Length];
            for (int i = 0; i < units.Length; i++) results[i] = orders.Order(units[i], order.Name, json);
            return results;
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
