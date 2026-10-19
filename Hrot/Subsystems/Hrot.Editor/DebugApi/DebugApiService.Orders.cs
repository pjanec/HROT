using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Hrot.Common.Orders;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐ <c>CE-3167</c> — the AI player's hands and eyes (<c>docs/DESIGN_Entity_Control.md</c> §4.1, §7 S1; R-270, R-271 (backend)).
    /// Orders go through <see cref="EntityOrders"/> — the ONE order path the map menu and the RTS tool share — at origin
    /// Operator, by network id, from any node. The situation report shows what a SIDE knows (its units' target memory), never
    /// the truth unless asked (C-9).
    /// </summary>
    public sealed partial class DebugApiService
    {
        /// <summary>⭐ C-10 — the side this API session is locked to (null = any). Applies to every order and to the sitrep.</summary>
        private ForceId? _sideLock;

        private EntityOrders Orders()
            => new(_world, _entityMap, _geoTransform ?? WorldGeoTransformOrNull()) { SideLock = _sideLock };

        // ── orders ──────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <c>CE-2148</c> (P5-f) — <c>GET /orders</c>: the order vocabulary (the <c>[Order]</c> DTOs) with what each needs — its fields
        /// (kind, <c>[Required]</c>, entity-pick presets) and its params schema (with <c>required</c>). <paramref name="networkId"/>
        /// narrows it to the orders that unit's class fulfils (the receiver's own answer, §8).
        /// </summary>
        public (JsonNode? result, int status, string? error, string? hint) GetOrders(long? networkId)
        {
            IReadOnlyList<OrderInfo> orders = OrderMenu.Orders;
            if (networkId is long id)
            {
                if (!_entityMap.TryGetEntity(id, out var unit) || !_world.IsAlive(unit))
                    return (null, 404, $"Entity {id} not found. List entities with GET /entities.", DebugApiHints.Entity);
                orders = OrderMenu.OfferedFor(_world.GetDisType(unit));
            }
            var list = new JsonArray();
            foreach (var o in orders)
            {
                var fields = new JsonArray();
                foreach (var f in o.Fields)
                {
                    var field = new JsonObject { ["name"] = f.Name, ["kind"] = f.Kind.ToString(), ["required"] = f.Required };
                    if (f.Presets.Count > 0) field["presets"] = new JsonArray(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(f.Presets, p => (JsonNode?)p)));
                    fields.Add(field);
                }
                list.Add(new JsonObject
                {
                    ["name"] = o.Name,
                    ["gesture"] = o.Gesture.ToString(),
                    ["pick"] = o.Pick.ToString(),
                    ["fields"] = fields,
                    ["paramSchema"] = DtoJsonSchemaExtractor.ExtractParams(o.Dto),
                });
            }
            var result = new JsonObject { ["orders"] = list };
            if (networkId is long n) result["networkId"] = n;
            return (result, 200, null, null);
        }

        /// <summary><c>POST /entities/{networkId}/order</c> — one order; <c>kind</c> = move · hold (stop) · behavior · release.</summary>
        public (JsonNode? result, int status, string? error, string? hint) PostOrder(long networkId, JsonNode? body)
        {
            var r = IssueOrder(networkId, body);
            return Answer(networkId, r);
        }

        /// <summary><c>DELETE /entities/{networkId}/order</c> — release the unit: the operator's order ends, its SOP picks again.</summary>
        public (JsonNode? result, int status, string? error, string? hint) DeleteOrder(long networkId)
            => Answer(networkId, Orders().Release(networkId));

        /// <summary><c>POST /orders</c> — several orders in one call: <c>{ orders: [ { networkId, kind, … } ] }</c>. Each is
        /// answered on its own; one refusal does not stop the rest.</summary>
        public (JsonNode? result, string? error) PostOrders(JsonNode? body)
        {
            if (body?["orders"] is not JsonArray list)
                return (null, "Body must be { orders: [ { networkId, kind, ... } ] }.");
            var results = new JsonArray();
            foreach (var item in list)
            {
                if (item is null || !long.TryParse(item["networkId"]?.ToString(), out long id))
                {
                    results.Add(new JsonObject { ["accepted"] = false, ["refusal"] = OrderRefusal.BadRequest.ToString(), ["detail"] = "networkId missing." });
                    continue;
                }
                var (result, _, error, _) = Answer(id, IssueOrder(id, item));
                results.Add(result ?? new JsonObject { ["networkId"] = id, ["accepted"] = false, ["detail"] = error });
            }
            return (new JsonObject { ["results"] = results }, null);
        }

        private OrderResult IssueOrder(long networkId, JsonNode? body)
        {
            var orders = Orders();
            string kind = (body?["kind"]?.GetValue<string>() ?? "").Trim().ToLowerInvariant();
            // ⭐ CE-2143 (D4-G) — an order with its DTO in `params` takes the generic path even when its name is also a legacy kind
            //   ({kind:"move", params:{Location:[lat,lon]}}); the legacy fields (lat, lon, targetId …) stay for callers without params.
            if (kind != "behavior" && body?["params"] is JsonNode orderParams && Hrot.Common.Orders.OrderMenu.TryGetByName(kind, out var named))
                return orders.Order(networkId, named.Name, orderParams is JsonValue ov && ov.TryGetValue<string>(out var os) ? os : orderParams.ToJsonString());
            switch (kind)
            {
                case "move":
                    return orders.Move(networkId,
                        lat: Double(body, "lat"), lon: Double(body, "lon"),
                        x: Float(body, "x"), y: Float(body, "y"),
                        speed: Float(body, "speed"), arrivalRadius: Float(body, "arrivalRadius"),
                        roadUse: body?["roadUse"]?.GetValue<string>());
                case "hold":
                case "stop":
                    return orders.Hold(networkId);
                case "release":
                    return orders.Release(networkId);
                case "behavior":
                {
                    string name = body?["behavior"]?.GetValue<string>() ?? "";
                    if (_behaviorRegistry is { } reg && !string.IsNullOrWhiteSpace(name) && !reg.TryGetId(name, out _))
                        return new OrderResult(OrderRefusal.BadRequest, $"Unknown behaviour '{name}'.", "", "");
                    var p = body?["params"];
                    string json = p is null ? "{}" : p is JsonValue v && v.TryGetValue<string>(out var s) ? s : p.ToJsonString();
                    return orders.Behavior(networkId, name, json);
                }
                case "attack":
                {
                    var t = body?["targetId"];
                    if (t is null || !t.AsValue().TryGetValue<long>(out long targetId))
                        return new OrderResult(OrderRefusal.BadRequest, "An attack needs targetId (the enemy's network id).", "", "");
                    return orders.Attack(networkId, targetId);
                }
                default:
                {
                    // ⭐ CE-2143 (D4-G) — any ORDER by name (case-insensitive), its DTO JSON in `params`; the unit picks how (§8).
                    if (!Hrot.Common.Orders.OrderMenu.TryGetByName(kind, out var order))
                        return new OrderResult(OrderRefusal.BadRequest,
                            "kind must be an order (" + string.Join(", ", System.Linq.Enumerable.Select(Hrot.Common.Orders.OrderMenu.Orders, o => o.Name))
                            + ") or one of: move, attack, hold (stop), behavior, release.", "", "");
                    var p = body?["params"];
                    string json = p is null ? "{}" : p is JsonValue v && v.TryGetValue<string>(out var s) ? s : p.ToJsonString();
                    return orders.Order(networkId, order.Name, json);
                }
            }
        }

        // ── possession (CE-3172, docs/DESIGN_Entity_Control.md §8b) ─────────────────────────────

        /// <summary><c>POST /entities/{networkId}/possess</c> — this node, as a station, possesses the unit (FPS): its task pauses
        /// and <c>POST …/control</c> frames drive it until <c>DELETE …/possess</c>.</summary>
        public (JsonNode? result, int status, string? error, string? hint) PostPossess(long networkId)
            => Answer(networkId, Orders().Possess(networkId));

        /// <summary><c>DELETE /entities/{networkId}/possess</c> — let the unit go; the task it was doing resumes.</summary>
        public (JsonNode? result, int status, string? error, string? hint) DeletePossess(long networkId)
            => Answer(networkId, Orders().Unpossess(networkId));

        /// <summary><c>POST /entities/{networkId}/control</c> — one control frame for the possessed unit:
        /// <c>{seq, forward, lateral, steer, yaw, gait, trigger, aim:[x,y,z], stance, shot, cancel, ammo, shotTarget}</c> (all optional
        /// but <c>seq</c>; newest seq wins; ⭐ CE-1074 <c>stance</c> = "standing" | "crouched" | "prone", absent = keep; ⭐ CE-1078 / CE-1079
        /// <c>shot</c> / <c>cancel</c> / <c>ammo</c> = the station's press COUNTERS (a new value is a press; DESIGN_Entity_Control §8e),
        /// <c>shotTarget</c> = the network id of the unit the last shot press locked on).</summary>
        public (JsonNode? result, int status, string? error, string? hint) PostControl(long networkId, JsonNode? body)
        {
            if (body?["seq"] is not JsonNode seqNode || !uint.TryParse(seqNode.ToString(), out uint seq))
                return Answer(networkId, new OrderResult(OrderRefusal.BadRequest, "A control frame needs seq (rising per frame).", "", ""));
            var frame = new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent
            {
                Seq = seq,
                Forward = Float(body, "forward") ?? 0f,
                Lateral = Float(body, "lateral") ?? 0f,
                Steer = Float(body, "steer") ?? 0f,
                Yaw = Float(body, "yaw") ?? 0f,
                Vertical = Float(body, "vertical") ?? 0f,   // ⭐ CE-2168 — a flyer's climb axis
                Gait = (byte)(Float(body, "gait") ?? 0f),
                Trigger = (byte)((body?["trigger"]?.ToString() is "true" or "1") ? 1 : 0),
                Mount = (byte)(Float(body, "mount") ?? 0f),
                Stance = body?["stance"]?.ToString().ToLowerInvariant() switch
                {
                    "standing" or "stand" => 1, "crouched" or "crouch" => 2, "prone" => 3, _ => 0,
                },
                ShotSeq = (byte)(Float(body, "shot") ?? 0f),
                CancelSeq = (byte)(Float(body, "cancel") ?? 0f),
                AmmoSeq = (byte)(Float(body, "ammo") ?? 0f),
                ShotTarget = long.TryParse(body?["shotTarget"]?.ToString(), out long shotTarget) ? shotTarget : 0L,
                // ⭐ CE-1151 (§8h U-2) — the use press counter, its verb (0 = the primary) and its target's network id
                UseSeq = (byte)(Float(body, "use") ?? 0f),
                UseVerb = (byte)(Float(body, "useVerb") ?? 0f),
                UseTarget = long.TryParse(body?["useTarget"]?.ToString(), out long useTarget) ? useTarget : 0L,
                // ⭐ CE-1212 (§8j L-1) — the lean held: "left" / "right" (else none)
                Lean = body?["lean"]?.ToString().ToLowerInvariant() switch { "left" => 1, "right" => 2, _ => 0 },
                Binoculars = (byte)((body?["binoculars"]?.ToString() is "true" or "1") ? 1 : 0),   // ⭐ CE-1219
            };
            if (body?["aim"] is JsonArray aim && aim.Count >= 2)
                frame.AimPoint = new System.Numerics.Vector3(Float(aim, 0), Float(aim, 1), aim.Count > 2 ? Float(aim, 2) : 0f);
            return Answer(networkId, Orders().Control(networkId, frame));
        }

        private static float Float(JsonArray a, int i)
            => float.TryParse(a[i]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;

        private static (JsonNode? result, int status, string? error, string? hint) Answer(long networkId, OrderResult r)
        {
            if (r.Accepted)
            {
                var ok = new JsonObject { ["networkId"] = networkId, ["accepted"] = true, ["detail"] = r.Detail };
                if (r.IntentId.Length > 0) ok["behavior"] = r.IntentId;
                if (r.JsonParams.Length > 0) ok["params"] = JsonNode.Parse(r.JsonParams);
                ok["note"] = "The brain owner resolves it on its next frames; read the result with GET /entities/{id}/order.";
                return (ok, 200, null, null);
            }
            int status = r.Refusal switch
            {
                OrderRefusal.UnknownEntity => 404,
                OrderRefusal.Dead or OrderRefusal.WrongSide or OrderRefusal.Possessed or OrderRefusal.NotFulfilled => 409,   // ⭐ CE-2143
                _ => 400,
            };
            string? hint = r.Refusal switch
            {
                OrderRefusal.UnknownEntity => DebugApiHints.Entity,
                OrderRefusal.BadRequest => DebugApiHints.Behavior,
                _ => null,
            };
            return (null, status, $"{r.Refusal}: {r.Detail}", hint);
        }

        /// <summary>
        /// <c>GET /entities/{networkId}/order</c> — the unit's operator order: <c>running</c> · <c>paused</c> (a reaction
        /// interrupted it) · <c>succeeded</c> / <c>failed</c> / <c>faulted</c> (its last one ended so) · <c>none</c>; with what
        /// runs now. Complete only on the unit's BRAIN node (its owner); elsewhere it says so.
        /// </summary>
        public JsonNode GetOrder(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };

            var node = new JsonObject { ["networkId"] = networkId };
            if (!_world.IsComponentTypeRegistered<BehaviorState>() || !_world.HasComponent<BehaviorState>(unit))
            {
                node["state"] = "unknown";
                node["note"] = "This node holds no brain state for the unit — read on its brain node (the Scenario perspective on a cluster).";
                return node;
            }

            var bs = _world.GetComponentRO<BehaviorState>(unit);
            node["onBrainNode"] = _world.HasAuthority<BehaviorState>(unit);
            node["current"] = BehaviourNode(bs.ActiveBehaviorHash, bs.Origin);
            // ⭐ CE-2295 (CC-E, Platoon §12.4) — the running task's params whatever its ORIGIN: a platoon demo reads the Superior order a
            //   squad was given (the "state" below stays the OPERATOR's order, as the order tools expect)
            if (bs.ActiveBehaviorHash != BehaviorIds.None && _world.HasManagedComponent<BehaviorStartRecord>(unit)
                && ((ISimulationView)_world).GetManagedComponentRO<BehaviorStartRecord>(unit) is { } running && running.InstanceId == bs.InstanceId)
                node["task"] = new JsonObject
                {
                    ["behavior"] = NameOf(bs.ActiveBehaviorHash), ["origin"] = bs.Origin.ToString(), ["since"] = bs.RunSince,
                    ["params"] = TryParse(running.JsonParams),
                };

            if (bs.ActiveBehaviorHash != BehaviorIds.None && bs.Origin == BehaviorOrigin.Operator)
            {
                node["state"] = "running";
                node["behavior"] = NameOf(bs.ActiveBehaviorHash);
                node["since"] = bs.RunSince;
                if (_world.HasManagedComponent<BehaviorStartRecord>(unit))
                {
                    var rec = ((ISimulationView)_world).GetManagedComponentRO<BehaviorStartRecord>(unit);
                    if (rec != null && rec.InstanceId == bs.InstanceId) node["params"] = TryParse(rec.JsonParams);
                }
                return node;
            }

            if (BehaviorOriginRank.Pauses(bs.Origin) && _world.HasManagedComponent<PausedTask>(unit))   // ⭐ CE-3172 — a reaction or a possession
            {
                var paused = ((ISimulationView)_world).GetManagedComponentRO<PausedTask>(unit);
                if (paused != null && paused.Origin == BehaviorOrigin.Operator)
                {
                    node["state"] = "paused";
                    node["behavior"] = paused.BehaviorName;
                    node["interruptedBy"] = NameOf(bs.ActiveBehaviorHash);
                    return node;
                }
            }

            if (_world.TryGetTable(typeof(OperatorOrderOutcome), out _) && _world.HasComponent<OperatorOrderOutcome>(unit))
            {
                var o = _world.GetComponentRO<OperatorOrderOutcome>(unit);
                node["state"] = o.Outcome switch
                {
                    BehaviorOutcome.Succeeded => "succeeded",
                    BehaviorOutcome.Failed => "failed",
                    _ => "faulted",
                };
                node["behavior"] = NameOf(o.BehaviorHash);
                node["finishedAt"] = o.FinishedAt;
                if (o.FaultCode != BehaviorFaultCode.None) node["fault"] = o.FaultCode.ToString();
                return node;
            }

            node["state"] = "none";
            return node;
        }

        // ── the side lock ───────────────────────────────────────────────────

        /// <summary><c>POST /orders/side-lock {side}</c> — lock this session to one side (Friend · Hostile · Neutral …), or
        /// <c>null</c> to unlock.</summary>
        public (JsonNode? result, string? error) SetSideLock(JsonNode? body)
        {
            var raw = body?["side"];
            if (raw is null || (raw is JsonValue jv && jv.TryGetValue<string>(out var s0) && string.IsNullOrWhiteSpace(s0)))
            {
                _sideLock = null;
                return (new JsonObject { ["side"] = null }, null);
            }
            if (!TryParseSide(raw.ToString(), out var side))
                return (null, $"Unknown side '{raw}'. One of: {string.Join(", ", Enum.GetNames(typeof(ForceId)))}.");
            _sideLock = side;
            return (new JsonObject { ["side"] = side.ToString() }, null);
        }

        // ── the situation report ────────────────────────────────────────────

        /// <summary>
        /// <c>GET /sitrep?side=&amp;truth=</c> — one call per decision: the side's units (position, health, ammo, the order) and
        /// the enemies the side KNOWS of (merged from its units' target memory, freshest kept; a heard contact has no id).
        /// <c>truth=true</c> lists every other unit instead (debugging, C-9). Read on the brain node: memory lives there.
        /// </summary>
        public unsafe (JsonNode? result, string? error) GetSitrep(string? sideText, bool truth)
        {
            ForceId side;
            if (!string.IsNullOrWhiteSpace(sideText))
            {
                if (!TryParseSide(sideText!, out side))
                    return (null, $"Unknown side '{sideText}'. One of: {string.Join(", ", Enum.GetNames(typeof(ForceId)))}.");
            }
            else if (_sideLock is ForceId locked) side = locked;
            else return (null, "Name a side (?side=Friend) or lock one (POST /orders/side-lock).");
            if (_sideLock is ForceId l && l != side)
                return (null, $"This session is locked to {l}; it cannot read {side}'s picture.");

            bool hasInfo = _world.IsComponentTypeRegistered<EntityInfo>();
            var units = new JsonArray();
            var ownIds = new HashSet<long>();
            var known = new Dictionary<long, JsonObject>();
            var heard = new JsonArray();
            var others = new JsonArray();

            foreach (var (netId, e) in LiveNetworkEntities())
            {
                if (!hasInfo || !_world.HasComponent<EntityInfo>(e)) continue;
                var force = _world.GetComponentRO<EntityInfo>(e).ForceId;
                if (force != side)
                {
                    if (truth) others.Add(UnitRow(netId, e, force, withOrder: false));
                    continue;
                }
                ownIds.Add(netId);
                units.Add(UnitRow(netId, e, force, withOrder: true));
                if (truth || !_world.IsComponentTypeRegistered<TargetMemory>() || !_world.HasComponent<TargetMemory>(e)) continue;

                ref readonly var m = ref _world.GetComponentRO<TargetMemory>(e);
                int count = Math.Min(m.Count, PerceptionConstants.MaxTrackedTargets);
                fixed (long* ids = m.EntityIds)
                fixed (float* x = m.PositionsX, y = m.PositionsY, z = m.PositionsZ, fresh = m.Freshness, radius = m.Radius)
                fixed (byte* anon = m.Anonymous)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var pos = new JsonArray(Round(x[i]), Round(y[i]), Round(z[i]));
                        if (anon[i] != 0)
                        {
                            heard.Add(new JsonObject { ["heard"] = true, ["position"] = pos, ["radius"] = Round(radius[i]), ["freshness"] = Round(fresh[i]), ["by"] = netId });
                            continue;
                        }
                        var target = new Entity((ulong)ids[i]);
                        if (target.IsNull || !_entityMap.TryGetNetworkId(target, out long tid)) continue;
                        if (known.TryGetValue(tid, out var prev) && prev["freshness"]!.GetValue<double>() >= Round(fresh[i])) continue;
                        known[tid] = new JsonObject
                        {
                            ["networkId"] = tid, ["position"] = pos, ["freshness"] = Round(fresh[i]), ["by"] = netId,
                            ["alive"] = _world.IsAlive(target),
                        };
                    }
                }
            }

            JsonArray contacts;
            if (truth) contacts = others;
            else
            {
                contacts = new JsonArray();
                foreach (var kv in known)
                    if (!ownIds.Contains(kv.Key)) contacts.Add(kv.Value);
                foreach (var h in heard) contacts.Add(h!.DeepClone());
            }

            return (new JsonObject
            {
                ["side"] = side.ToString(),
                ["truth"] = truth,
                ["units"] = units,
                ["contacts"] = contacts,
            }, null);
        }

        private JsonObject UnitRow(long netId, Entity e, ForceId force, bool withOrder)
        {
            var row = new JsonObject { ["networkId"] = netId, ["side"] = force.ToString() };
            var name = _world.GetComponentRO<EntityInfo>(e).Name.ToString();
            if (!string.IsNullOrEmpty(name)) row["name"] = name;
            if (_world.HasComponent<SimTransform>(e))
            {
                var p = _world.GetComponentRO<SimTransform>(e).Position;
                row["position"] = new JsonArray(Round(p.X), Round(p.Y), Round(p.Z));
            }
            if (_world.IsComponentTypeRegistered<Health>() && _world.HasComponent<Health>(e))
            {
                var h = _world.GetComponentRO<Health>(e);
                row["health"] = new JsonObject { ["current"] = Round(h.Current), ["max"] = Round(h.Max) };
                row["killed"] = h.Current <= 0;
            }
            if (_world.IsComponentTypeRegistered<WeaponState>() && _world.HasComponent<WeaponState>(e))
            {
                var w = _world.GetComponentRO<WeaponState>(e);
                row["ammo"] = new JsonObject { ["current"] = w.Ammo, ["max"] = w.MaxAmmo };
            }
            if (withOrder && _world.IsComponentTypeRegistered<BehaviorState>() && _world.HasComponent<BehaviorState>(e))
            {
                var bs = _world.GetComponentRO<BehaviorState>(e);
                row["doing"] = BehaviourNode(bs.ActiveBehaviorHash, bs.Origin);
            }
            return row;
        }

        private IEnumerable<(long NetId, Entity Entity)> LiveNetworkEntities()
        {
            var list = new List<(long, Entity)>();
            var q = _world.Query().With<EntityInfo>().Build();
            foreach (var e in q)
                if (_entityMap.TryGetNetworkId(e, out long id)) list.Add((id, e));
            return list;
        }

        private JsonObject BehaviourNode(int hash, BehaviorOrigin origin)
            => new() { ["behavior"] = hash == BehaviorIds.None ? null : NameOf(hash), ["origin"] = origin.ToString() };

        private string NameOf(int hash)
            => _behaviorRegistry is { } reg && reg.TryGetName(hash, out var n) ? n : hash.ToString();

        private static JsonNode? TryParse(string json)
        {
            try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
            catch (System.Text.Json.JsonException) { return json; }
        }

        private static bool TryParseSide(string text, out ForceId side)
            => Enum.TryParse(text.Trim(), ignoreCase: true, out side) && Enum.IsDefined(typeof(ForceId), side);

        private static double? Double(JsonNode? body, string key)
            => body?[key] is JsonNode n && double.TryParse(n.ToString(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

        private static float? Float(JsonNode? body, string key)
            => Double(body, key) is double d ? (float)d : null;
    }
}
