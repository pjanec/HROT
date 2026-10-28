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
using Hrot.Map.Definitions.Behavior.Orders;

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
        /// <summary>The session is locked to another side (<see cref="EntityOrders.SideLock"/>) — or, ⭐ <c>R-4096</c> (C-3′), the vehicle
        /// carries a player of another side than this station's (<see cref="EntityOrders.Possess(long, PossessionMode)"/>).</summary>
        WrongSide,
        /// <summary>The order itself is malformed (no behaviour name, no move target).</summary>
        BadRequest,
        /// <summary>⭐ <c>CE-3172</c> (C-3) — a station possesses the unit: no order reaches it until it is released. ⭐ <c>R-4096</c> (C-3′) —
        /// and a second station's <c>Possess</c> of it is refused with this too: nobody takes a unit another live station holds.</summary>
        Possessed,
        /// <summary>⭐ <c>CE-2143</c> — no class of the unit fulfils the order (§8: no <c>&lt;Class&gt;&lt;Order&gt;</c> behaviour), or two
        /// classes tie. The receiver would start nothing; the sender says why.</summary>
        NotFulfilled,
        /// <summary>⭐ <c>CE-1205</c> (Entity Control §8m PK-7) — the entity cannot be possessed (a weapon system is manned).</summary>
        NotPossessable,
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
            // ⭐ An order may be issued on ANY node (CE-3167: the intent egress runs on every node), but only a Brain's registry
            //   (CognitiveComponentRegistry) registers the intent event — so on a SimHost or IG node, with production's explicit
            //   event registration on, the first order threw "published without being explicitly registered" (📐 live, a POST
            //   /entities/{id}/order on the SimHost perspective answered 500). The node that can order registers what it publishes.
            if (!_world.Bus.IsRegisteredManaged<AssignTacticalIntentEvent>()) _world.Bus.RegisterManaged<AssignTacticalIntentEvent>();
        }

        /// <summary>⭐ C-10 — when set, orders to units of any other side are refused (the AI player plays one side).</summary>
        public ForceId? SideLock { get; set; }

        /// <summary>Move to a point: geo (<paramref name="lat"/>/<paramref name="lon"/>) or this node's local metres
        /// (<paramref name="x"/>/<paramref name="y"/>, converted to geo when this node can). Optional speed (m/s), arrival radius (m)
        /// and road use (<c>Never</c> · <c>Neutral</c> · <c>Prefer</c> · <c>StronglyPrefer</c>).</summary>
        public OrderResult Move(long unit, double? lat = null, double? lon = null, float? x = null, float? y = null,
            float? speed = null, float? arrivalRadius = null, string? roadUse = null)
        {
            // ⭐ CE-2143 (D4-A) — the MOVE order's DTO (MoveOrder: Location [lat, lon], Speed, ArrivalRadius, RoadUse), never a
            //   behaviour: the unit picks how it moves (§8). Without a transform, x/y travel as [y, x] (OrderGeo's fallback).
            JsonArray location;
            if (lat is double la && lon is double lo) location = new JsonArray(la, lo);
            else if (x is float px && y is float py)
            {
                if (_geo != null)
                {
                    var (gLat, gLon, _) = _geo.ToGeodetic(new System.Numerics.Vector3(px, py, 0f));
                    location = new JsonArray(gLat, gLon);
                }
                else location = new JsonArray((double)py, (double)px);
            }
            else return OrderResult.Refused(OrderRefusal.BadRequest, "A move needs lat+lon or x+y.");

            var p = new JsonObject { ["Location"] = location };
            if (speed is float s) p["Speed"] = s;
            if (arrivalRadius is float r) p["ArrivalRadius"] = r;
            if (!string.IsNullOrWhiteSpace(roadUse)) p["RoadUse"] = roadUse;
            return Order(unit, "Move", p.ToJsonString());
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
            return Order(unit, "Attack", new JsonObject { ["Target"] = target }.ToJsonString());   // ⭐ CE-2143 — the ATTACK order's DTO
        }

        /// <summary>Hold where it stands — the HOLD order; the unit picks how (a person or a vehicle: stay, as <c>Idle</c>; a squad:
        /// hold and fight). The menu's "Stop".</summary>
        public OrderResult Hold(long unit) => Order(unit, "Hold", "{}");

        /// <summary>
        /// ⭐⭐ <c>CE-2143</c> (D4-A, R-273) — an ORDER by name (<c>Move</c>, <c>Attack</c>, <c>Hold</c> … — the <c>[Order]</c> DTOs) with
        /// its DTO's JSON: published as <c>Order.&lt;name&gt;</c>, resolved by the unit itself to its own behaviour (§8). Refused here,
        /// with the reason, when no class of the unit fulfils it — the same question the receiver asks (<see cref="OrderMenu"/>).
        /// </summary>
        public OrderResult Order(long unit, string orderName, string? jsonParams)
        {
            if (!OrderMenu.TryGetByName(orderName ?? "", out var order))
                return OrderResult.Refused(OrderRefusal.BadRequest,
                    $"No order '{orderName}'. Orders: {string.Join(", ", System.Linq.Enumerable.Select(OrderMenu.Orders, o => o.Name))}.");
            string json = string.IsNullOrWhiteSpace(jsonParams) ? "{}" : jsonParams!;
            // ⭐ CE-2148 (F5, P5-a) — a [Required] field the JSON does not set refuses the order here, named, for EVERY sender (menu,
            //   RTS, HTTP → 400, an AI player): the one rule the front-ends also ask (OrderMenu.MissingIn).
            JsonObject? parsed;
            try { parsed = JsonNode.Parse(json) as JsonObject; }
            catch (System.Text.Json.JsonException e) { return OrderResult.Refused(OrderRefusal.BadRequest, $"The {order.Name} params are not JSON: {e.Message}"); }
            if (parsed is null) return OrderResult.Refused(OrderRefusal.BadRequest, $"The {order.Name} params must be a JSON object.");
            var missing = OrderMenu.MissingIn(order, parsed);
            if (missing.Count > 0)
                return OrderResult.Refused(OrderRefusal.BadRequest,
                    $"{order.Name} needs {string.Join(", ", System.Linq.Enumerable.Select(missing, f => f.Name))} (required).");
            var malformed = OrderMenu.MalformedIn(order, parsed);   // ⭐ CE-2176 — a point the receiver would throw on
            if (malformed.Count > 0)
                return OrderResult.Refused(OrderRefusal.BadRequest,
                    $"{order.Name}: {string.Join("; ", System.Linq.Enumerable.Select(malformed, m => $"{m.Field.Name} {m.Why}"))}.");
            if (!TryAddress(unit, out var entity, out var refused)) return refused;
            var r = OrderMenu.Resolve(_world.GetDisType(entity), order.Name);
            if (r.Outcome != Hrot.Core.Tkb.OrderResolution.Resolved) return OrderResult.Refused(OrderRefusal.NotFulfilled, r.Detail);
            return Publish(entity, OrderAttribute.IntentFor(order.Name), json);
        }

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
        /// <see cref="Unpossess"/> (or it dies) resumes the task. ⭐ <c>R-4096</c> (C-3′) — a unit ANOTHER live station holds is refused
        /// (<see cref="OrderRefusal.Possessed"/>, naming the holder), never taken over; so is a vehicle carrying a player of another side
        /// (<see cref="OrderRefusal.WrongSide"/>).</summary>
        public OrderResult Possess(long unit) => Possess(unit, PossessionMode.Fps);

        /// <summary>
        /// ⭐ <c>CE-1158</c> (§8h U-4, module graph GAP2) — possess in <paramref name="mode"/>: <see cref="PossessionMode.Fps"/> drives the
        /// unit; <see cref="PossessionMode.Riding"/> keeps it held (its task paused) while the station drives the vehicle it sits in. A new
        /// Fps possession RELEASES this station's other Fps unit, so a station never holds two (C-8: one operator per node); a rider is kept.
        /// </summary>
        public OrderResult Possess(long unit, PossessionMode mode)
        {
            if (!TryAddress(unit, out var entity, out var refused, ignorePossession: true)) return refused;
            if (Fdp.Toolkit.Behavior.Possession.Possessability.Refusal(_world, entity) is { } why)   // ⭐ CE-1205 (§8m PK-7)
                return OrderResult.Refused(OrderRefusal.NotPossessable, $"Entity {unit}: {why}.");
            // ⭐ R-4096 (C-3′) — answered here, synchronously, from the replicated PossessedBy; the brain's PossessionSystem enforces the same
            //   rule (StationClaims) for the race of two stations. Checked BEFORE ReleaseOtherFps: a refused possess lets nothing go.
            switch (StationClaims.ForPossess(_world, entity, StationFor(entity), out long holder))
            {
                case ClaimRefusal.HeldByAnotherStation:
                    return OrderResult.Refused(OrderRefusal.Possessed,
                        $"Entity {unit} is held by station {holder}; a unit another player holds is never taken over (release is theirs).");
                case ClaimRefusal.OtherSide:
                    return OrderResult.Refused(OrderRefusal.WrongSide,
                        $"Entity {unit} carries a player of another side (station {holder}); it is not for this station.");
            }
            if (mode != PossessionMode.Riding) ReleaseOtherFps(entity);
            Publish(new PossessRequestEvent { Unit = entity, Mode = mode, StationNodeId = StationFor(entity) });
            return new OrderResult(OrderRefusal.None, mode == PossessionMode.Riding ? "riding" : "possessing", string.Empty, string.Empty);
        }

        private void ReleaseOtherFps(Entity keep)
        {
            if (!_world.IsComponentTypeRegistered<PossessedBy>()) return;
            foreach (var other in _world.Query().With<PossessedBy>().Build())
            {
                if (other == keep || !_world.IsAlive(other)) continue;
                var held = _world.GetComponentRO<PossessedBy>(other);
                if (held.Mode == PossessionMode.Riding || held.StationNodeId != StationFor(other)) continue;
                Publish(new ReleasePossessionEvent { Unit = other, StationNodeId = held.StationNodeId });
            }
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

        /// <summary>⭐ CE-3174 — this node as a station, for <paramref name="entity"/> (C-8): what <see cref="Fdp.Toolkit.Behavior.Possession.PossessedBy.StationNodeId"/> says when THIS station holds it.</summary>
        public long StationFor(Entity entity)
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
            return Publish(entity, intentId, json);
        }

        private OrderResult Publish(Entity entity, string intentId, string json)
        {
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
