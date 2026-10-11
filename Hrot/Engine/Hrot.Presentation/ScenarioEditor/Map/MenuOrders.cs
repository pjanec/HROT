using System;
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
    /// ⭐⭐ <c>CE-3169</c> (<c>docs/DESIGN_Entity_Control.md</c> §4.2, §7 S2) — the unit menu's <b>Move Here · Engage · Stop</b>, ONE
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
        private enum Kind : byte { Move, Attack }

        private readonly EntityRepository _world;
        private Task<Hrot.Core.Mission.GeoPoint>? _location;
        private Task<int>? _enemy;
        private long _unit;

        public MenuOrders(EntityRepository world) => _world = world ?? throw new ArgumentNullException(nameof(world));

        /// <summary>The host's map pick — set by every host that builds one (a resolver: the hosts build their pick after the map).</summary>
        public Func<IMapPickService?>? PickService { get; set; }

        /// <summary>True while a follow-up pick is waiting for the operator.</summary>
        public bool IsPicking => _location is { IsCompleted: false } || _enemy is { IsCompleted: false };

        /// <summary>Registers the three menu ids on <paramref name="actions"/>.</summary>
        public void RegisterOn(Hrot.Common.Interactions.GlobalActionRegistry actions)
        {
            actions.Register(GlobalActionIds.MoveHere, (_, unit) => Begin(Kind.Move, unit));
            actions.Register(GlobalActionIds.Engage,   (_, unit) => Begin(Kind.Attack, unit));
            actions.Register(GlobalActionIds.Stop,     (_, unit) =>
            {
                if (PickService?.Invoke() is null || !TryNetworkId(unit, out long id)) return;
                Report(Orders()?.Hold(id));
            });
        }

        private void Begin(Kind kind, Entity unit)
        {
            var pick = PickService?.Invoke();
            if (pick is null || !TryNetworkId(unit, out long id)) return;
            _unit = id;
            _location = null;
            _enemy = null;
            if (kind == Kind.Move) _location = pick.PickLocationAsync();
            else _enemy = pick.PickEntityAsync();
        }

        /// <summary>Turns a completed pick into its order; a cancelled or failed pick issues nothing.</summary>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_location is { IsCompleted: true } location)
            {
                _location = null;
                if (location.Status == TaskStatus.RanToCompletion)
                {
                    var at = location.Result;
                    var orders = Orders();
                    // The adapter answers geodetic when the node has a transform, else world metres in the same fields.
                    Report(Geo() != null
                        ? orders?.Move(_unit, lat: at.Latitude, lon: at.Longitude)
                        : orders?.Move(_unit, x: (float)at.Latitude, y: (float)at.Longitude));
                }
            }
            if (_enemy is { IsCompleted: true } enemy)
            {
                _enemy = null;
                if (enemy.Status == TaskStatus.RanToCompletion && enemy.Result > 0)
                    Report(Orders()?.Attack(_unit, enemy.Result));
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
