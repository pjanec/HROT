using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat.Components;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.Common.Orders
{
    /// <summary>Why <see cref="EntityOrders"/> refused an order (<see cref="None"/> = accepted).</summary>
    public enum OrderRefusal : byte
    {
        None = 0,
        /// <summary>No entity has that network id on this node.</summary>
        UnknownEntity,
        /// <summary>The unit is killed (health ≤ 0) — it can take no order.</summary>
        Dead,
        /// <summary>The session is locked to another side (<see cref="EntityOrders.SideLock"/>).</summary>
        WrongSide,
        /// <summary>The order itself is malformed (no behaviour name, no move target).</summary>
        BadRequest,
        /// <summary>⭐ <c>CE-3172</c> (C-3) — a station possesses the unit: no order reaches it until it is released.</summary>
        Possessed,
    }

    /// <summary>The outcome of one <see cref="EntityOrders"/> call: the refusal, or what was published.</summary>
    public readonly record struct OrderResult(OrderRefusal Refusal, string Detail, string IntentId, string JsonParams)
    {
        /// <summary>True when the order was published.</summary>
        public bool Accepted => Refusal == OrderRefusal.None;

        internal static OrderResult Refused(OrderRefusal why, string detail) => new(why, detail, string.Empty, string.Empty);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3167</c> — THE ONE way an operator orders a unit (<c>docs/DESIGN_Entity_Control.md</c> §3.1, R-270): the HTTP
    /// routes, the map menu and the RTS tool all call this, so they cannot drift apart. Every order is an
    /// <see cref="AssignTacticalIntentEvent"/> on THIS node's bus at origin <see cref="BehaviorOrigin.Operator"/> with
    /// <see cref="AssignTacticalIntentEvent.ClearsPriorIntent"/> (the mission plan goes, ruling 50); the brain owner resolves it
    /// — locally in the editor, through the intent translators on a cluster (R-271 (backend): the network is only a translator).
    /// <para>Units are addressed by NETWORK id (the id every node and the HTTP API share). A move is sent as lat/lon whenever this
    /// node has a geographic transform, because local x/y depends on the node's origin (<c>MoveToLocationParamsJsonDto.X</c>).</para>
    /// </summary>
    public sealed class EntityOrders
    {
        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _map;
        private readonly IGeographicTransform? _geo;

        /// <param name="geo">This node's ONE geographic transform (the user's 2026-09-08 ruling: handed in, never looked up
        /// here). Null keeps a local x/y move as x/y.</param>
        public EntityOrders(EntityRepository world, NetworkEntityMap map, IGeographicTransform? geo)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _geo = geo;
        }

        /// <summary>⭐ C-10 — when set, orders to units of any other side are refused (the AI player plays one side).</summary>
        public ForceId? SideLock { get; set; }

        /// <summary>Move to a point: geo (<paramref name="lat"/>/<paramref name="lon"/>) or this node's local metres
        /// (<paramref name="x"/>/<paramref name="y"/>, converted to geo when this node can). Optional speed (m/s), arrival radius (m)
        /// and road use (<c>Never</c> · <c>Neutral</c> · <c>Prefer</c> · <c>StronglyPrefer</c>).</summary>
        public OrderResult Move(long unit, double? lat = null, double? lon = null, float? x = null, float? y = null,
            float? speed = null, float? arrivalRadius = null, string? roadUse = null)
        {
            var p = new JsonObject();
            if (lat is double la && lon is double lo)
            {
                p["targetLat"] = la;
                p["targetLon"] = lo;
            }
            else if (x is float px && y is float py)
            {
                if (_geo != null)
                {
                    var (gLat, gLon, _) = _geo.ToGeodetic(new System.Numerics.Vector3(px, py, 0f));
                    p["targetLat"] = gLat;
                    p["targetLon"] = gLon;
                }
                else
                {
                    p["x"] = px;
                    p["y"] = py;
                }
            }
            else return OrderResult.Refused(OrderRefusal.BadRequest, "A move needs lat+lon or x+y.");

            if (speed is float s) p["speed"] = s;
            if (arrivalRadius is float r) p["arrivalRadius"] = r;
            if (!string.IsNullOrWhiteSpace(roadUse)) p["roadUse"] = roadUse;
            return Assign(unit, BehaviorNames.MoveToLocation, p.ToJsonString());
        }

        /// <summary>⭐ <c>CE-3170</c> (C-5) — ATTACK a unit: <c>CombatPosture</c> with that target designated and <c>assault</c> — the unit
        /// closes in and fights IT (a nearer threat does not take over), picking its own way, cover and weapon; the order succeeds
        /// when the target is out of the fight. ⚠ ROE still applies: a unit on HoldFire closes in but does not fire.</summary>
        public OrderResult Attack(long unit, long target)
        {
            if (target == unit) return OrderResult.Refused(OrderRefusal.BadRequest, "A unit cannot attack itself.");
            if (!_map.TryGetEntity(target, out var victim) || !_world.IsAlive(victim))
                return OrderResult.Refused(OrderRefusal.BadRequest, $"Target {target} not found.");
            if (_world.IsComponentTypeRegistered<Health>() && _world.HasComponent<Health>(victim)
                && _world.GetComponentRO<Health>(victim).Current <= 0)
                return OrderResult.Refused(OrderRefusal.BadRequest, $"Target {target} is already killed.");
            var p = new JsonObject { ["target"] = target, ["assault"] = true };
            return Assign(unit, BehaviorNames.CombatPosture, p.ToJsonString());
        }

        /// <summary>Hold where it stands — the operator orders <c>Idle</c>, which never ends (AQ83 E). The menu's "Stop".</summary>
        public OrderResult Hold(long unit) => Assign(unit, BehaviorNames.Idle, "{}");

        /// <summary>Any behaviour by name, with its parameter JSON (discover them with <c>GET /behaviors?entityId=</c>).</summary>
        public OrderResult Behavior(long unit, string behaviorName, string? jsonParams)
        {
            if (string.IsNullOrWhiteSpace(behaviorName))
                return OrderResult.Refused(OrderRefusal.BadRequest, "A behaviour order needs a behaviour name.");
            return Assign(unit, behaviorName, string.IsNullOrWhiteSpace(jsonParams) ? "{}" : jsonParams!);
        }

        /// <summary>Release the unit — the operator's order ends and the unit's SOP picks again (a Clear intent).</summary>
        public OrderResult Release(long unit)
        {
            if (!TryAddress(unit, out var entity, out var refused)) return refused;
            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity = entity, Kind = TacticalIntentKind.Clear, Origin = BehaviorOrigin.Operator,
            });
            return new OrderResult(OrderRefusal.None, "released", string.Empty, string.Empty);
        }

        // ── possession (CE-3172, C-3/C-4/C-8; docs/DESIGN_Entity_Control.md §8b) ──────────────────────────────

        /// <summary>⭐ <c>CE-3172</c> — this node, as a STATION (C-8: one operator per node). Null = the node id on the unit's
        /// <c>NetworkAuthority</c> (every replicated unit carries this node's id), or 0 on a node with no authority data.</summary>
        public long? Station { get; set; }

        /// <summary>⭐ <c>CE-3172</c> — POSSESS the unit (FPS): its task pauses and this station's control frames drive it, until
        /// <see cref="Unpossess"/> (or it dies) resumes the task. Another station's hold is taken over (C-3).</summary>
        public OrderResult Possess(long unit)
        {
            if (!TryAddress(unit, out var entity, out var refused, ignorePossession: true)) return refused;
            Publish(new PossessRequestEvent { Unit = entity, Mode = PossessionMode.Fps, StationNodeId = StationFor(entity) });
            return new OrderResult(OrderRefusal.None, "possessing", string.Empty, string.Empty);
        }

        /// <summary>⭐ <c>CE-3172</c> (C-4) — let the possessed unit go: the task it was doing resumes.</summary>
        public OrderResult Unpossess(long unit)
        {
            if (!TryHeld(unit, out var entity, out var refused)) return refused;
            Publish(new ReleasePossessionEvent { Unit = entity, StationNodeId = StationFor(entity) });
            return new OrderResult(OrderRefusal.None, "released", string.Empty, string.Empty);
        }

        /// <summary>⭐ <c>CE-3172</c> / <c>CE-3173</c> — one control frame for the unit this station possesses (M-6: the newest
        /// <paramref name="frame"/><c>.Seq</c> wins). <paramref name="frame"/>'s unit and station are filled in here.</summary>
        public OrderResult Control(long unit, OperatorControlFrameEvent frame)
        {
            if (!TryHeld(unit, out var entity, out var refused)) return refused;
            frame.Unit = entity;
            frame.StationNodeId = StationFor(entity);
            frame.IsRemote = false;
            Publish(frame);
            return new OrderResult(OrderRefusal.None, "control", string.Empty, string.Empty);
        }

        /// <summary>The station holding the unit (null = nobody) — what a station reads to know it still holds it.</summary>
        public long? HolderOf(long unit)
            => _map.TryGetEntity(unit, out var e) && _world.IsAlive(e) ? PossessionSystem.HolderOf(_world, e) : null;

        private bool TryHeld(long unit, out Entity entity, out OrderResult refused)
        {
            if (!TryAddress(unit, out entity, out refused, ignorePossession: true)) return false;
            long station = StationFor(entity);
            if (PossessionSystem.HolderOf(_world, entity) is long holder && holder == station) return true;
            refused = OrderResult.Refused(OrderRefusal.Possessed, PossessionSystem.HolderOf(_world, entity) is long other
                ? $"Entity {unit} is possessed by station {other}, not this one ({station})."
                : $"Entity {unit} is not possessed by this station ({station}).");
            return false;
        }

        private long StationFor(Entity entity)
            => Station ?? (_world.IsComponentTypeRegistered<NetworkAuthority>() && _world.HasComponent<NetworkAuthority>(entity)
                ? _world.GetComponentRO<NetworkAuthority>(entity).LocalNodeId : 0L);

        private void Publish<T>(T evt) where T : unmanaged
        {
            if (!_world.Bus.IsRegistered<T>()) _world.Bus.Register<T>();
            _world.Bus.Publish(evt);
        }

        private OrderResult Assign(long unit, string intentId, string json)
        {
            if (!TryAddress(unit, out var entity, out var refused)) return refused;
            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity = entity, IntentId = intentId, JsonParams = json,
                Origin = BehaviorOrigin.Operator, ClearsPriorIntent = true,
            });
            return new OrderResult(OrderRefusal.None, "ordered", intentId, json);
        }

        /// <summary>The checks every order shares: the unit exists here, is not killed, and is on the locked side.</summary>
        private bool TryAddress(long unit, out Entity entity, out OrderResult refused, bool ignorePossession = false)
        {
            refused = default;
            if (!_map.TryGetEntity(unit, out entity) || !_world.IsAlive(entity))
            {
                refused = OrderResult.Refused(OrderRefusal.UnknownEntity, $"Entity {unit} not found.");
                return false;
            }
            if (_world.IsComponentTypeRegistered<Health>() && _world.HasComponent<Health>(entity)
                && _world.GetComponentRO<Health>(entity).Current <= 0)
            {
                refused = OrderResult.Refused(OrderRefusal.Dead, $"Entity {unit} is killed.");
                return false;
            }
            // ⭐ CE-3172 (C-3) — a possessed unit takes no order until its station lets it go (the ingress would refuse an
            //   Operator order under a Possession anyway; this says WHY instead of a silent drop)
            if (!ignorePossession && PossessionSystem.HolderOf(_world, entity) is long holder)
            {
                refused = OrderResult.Refused(OrderRefusal.Possessed, $"Entity {unit} is possessed by station {holder}; release it first.");
                return false;
            }
            if (SideLock is ForceId side)
            {
                var force = _world.IsComponentTypeRegistered<EntityInfo>() && _world.HasComponent<EntityInfo>(entity)
                    ? _world.GetComponentRO<EntityInfo>(entity).ForceId : ForceId.Neutral;
                if (force != side)
                {
                    refused = OrderResult.Refused(OrderRefusal.WrongSide,
                        $"Entity {unit} is {force.ToString()}; this session is locked to {side.ToString()}.");
                    return false;
                }
            }
            return true;
        }
    }
}
