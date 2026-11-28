using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2312</c> (HQ-6) — what an HQ's asset is, for the answers.</summary>
    public enum SupportAsset : byte { None = 0, AttackHelicopter = 1, StrikePlane = 2, Battery = 3, GroundUnit = 4, /** ⭐ CE-2442 D-2 — a unit with a TKB 116 clearer */ Engineers = 5 }

    /// <summary>⭐ <c>CE-2312</c> (HQ-6) — the HQ's task: how near an air defence counts, and how long a reported one stays a threat.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SupportCommandParams
    {
        public float AirDefenceMetres;
        public SimDuration AirDefenceFreshSeconds;
        /// <summary>⭐ <c>CE-4589</c> (HQ-11) — the roster slots held as the RESERVE (bit k = slot k): set ⇒ a Reinforce is answered only from them; 0 ⇒ any free ground unit.</summary>
        public int Reserve;
    }

    /// <summary>⭐ <c>CE-2312</c> — what the HQ keeps: the answers it gave, the air-defence strikes it ordered (a test / diagnostics probe).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SupportCommandState
    {
        public int Answered;
        public int AirDefenceStrikes;
        /// <summary>⭐ <c>CE-4617</c> — when the last SEAD strike was ordered (one per <c>SeadIntervalSeconds</c>: an order reaches the asset's
        /// brain a frame or more later, and a unit is not "busy" until then).</summary>
        public SimInstant LastSeadAt;
        /// <summary>⭐ <c>CE-2442</c> D-2 (Minefields §13) — the ONE engineer job: 0 none · 1 the man's recon · 2 the unit's breach.</summary>
        public byte EngPhase;
        public int EngFieldNetId;
        public long EngUnitPacked, EngManPacked;
        public Vector3 EngNear, EngFar;
        public SimInstant EngSince;
        /// <summary>Engineer jobs started (a test / diagnostics probe).</summary>
        public int EngineerJobs;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2312</c> (HQ-6…HQ-8, Urban Battle §4 ①, §1 beats 6 and 8; 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §8d)
    /// — <c>SupportCommand</c>, the headquarters' task: its roster holds the platoons and the support ASSETS; it answers the requests in its
    /// picture with what is FREE (an asset still running the order this run gave it is busy — <see cref="CommandOrders.Busy"/>, or ordered within <see cref="OrderGraceSeconds"/>).
    /// <b>FireSupport</b>: an air defence reported near the point ⇒ a strike plane on it first and the request waits; else an attack
    /// helicopter searches the point (<c>AirArmedSearch</c>), else a battery fires for effect on it. <b>Reinforce</b>: a free ground unit
    /// (not the requester) defends at the requester's point. Running for good. Nothing here reads another brain but the asset's busy state.
    /// </summary>
    public static class SupportCommandBehavior
    {
        public const float DefaultAirDefenceMetres = 2000f, DefaultAirDefenceFreshSeconds = 120f;
        /// <summary>The reinforcement's search radius at the point: a platoon, a squad (m).</summary>
        public const float PlatoonDefendMetres = 60f, SquadDefendMetres = 40f;

        [StructLayout(LayoutKind.Sequential)]
        public struct SupportCommandBlackboard
        {
            public SupportCommandParams Params;
            public SupportCommandState State;
        }

        [BehaviorResolver(BehaviorNames.SupportCommand)]
        public static void Resolve(in SupportCommandParamsJsonDto authored, ref SupportCommandBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new SupportCommandParams
            {
                AirDefenceMetres = authored.AirDefenceMetres > 0f ? authored.AirDefenceMetres : DefaultAirDefenceMetres,
                AirDefenceFreshSeconds = authored.AirDefenceFreshSeconds > 0f ? authored.AirDefenceFreshSeconds : DefaultAirDefenceFreshSeconds,
                Reserve = authored.Reserve,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.SupportCommand, Params = typeof(SupportCommandParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.SupportCommand, Curated = true, Group = "Library/Combat", ParamsType = typeof(SupportCommandBlackboard))]
        public static BTreeBuilder<SupportCommandBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SupportCommandBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Command);

        /// <summary>One tick: answer the oldest FireSupport, then the oldest Reinforce, with what is free.</summary>
        public static NodeStatus Command(ref SupportCommandParams p, ref SupportCommandState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<CommandPicture>(self)) return NodeStatus.Running;
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            SimInstant now = CommandPictureOps.Now(world);

            Sead(in p, ref ws, self, world, now);

            int fire = CommandPictureOps.FirstRequest(world, self, RequestKind.FireSupport);
            if (fire >= 0)
            {
                var point = world.GetComponentRO<CommandPicture>(self).Requests.Items[fire].At;
                bool smoke = world.GetComponentRO<CommandPicture>(self).Requests.Items[fire].Round == FireSupportRound.Smoke;   // ⭐ CE-7025
                int rounds = world.GetComponentRO<CommandPicture>(self).Requests.Items[fire].Rounds;   // ⭐ CE-2436 AU-3 (A-5) — sustained: rounds per gun
                if (AirDefenceNear(world, self, point, p.AirDefenceMetres, p.AirDefenceFreshSeconds, now, out var aa))
                {
                    // HQ-7 — the air defence first; the request waits while it stays reported
                    var plane = Free(world, self, SupportAsset.StrikePlane, Entity.Null);
                    if (!plane.IsNull)
                    {
                        CommandOrders.Order(world, self, plane, nameof(OrderKind.Strike),
                            JsonSerializer.Serialize(new StrikeOrder { Location = aa }, json), newOrder: true);
                        ws.AirDefenceStrikes++;
                    }
                }
                else
                {
                    var heli = smoke ? Entity.Null : Free(world, self, SupportAsset.AttackHelicopter, Entity.Null);   // ⭐ CE-7025 — smoke is the guns' only
                    var battery = heli.IsNull ? Free(world, self, SupportAsset.Battery, Entity.Null) : Entity.Null;
                    if (!heli.IsNull)
                        CommandOrders.Assign(world, self, heli, BehaviorNames.AirArmedSearch,
                            JsonSerializer.Serialize(new AirArmedSearchOrder { Location = point }, json), newOrder: true);
                    else if (!battery.IsNull)
                        CommandOrders.Order(world, self, battery, nameof(OrderKind.Strike),
                            JsonSerializer.Serialize(new StrikeOrder { Location = point, Method = FireMissionMethod.FireForEffect, Smoke = smoke,
                                                                     Rounds = rounds > 1 ? rounds : 0,
                                                                     // ⭐ CE-4615 (R-2099) — a REQUESTED mission is declared danger close: the requester called
                                                                     //   for fire on its own fight (81 mm's 600 m always holds a company's own men), as a
                                                                     //   doctrinal call does; an undeclared strike (an operator's) keeps the battery's refusal
                                                                     DangerClose = true }, json),
                            newOrder: true);
                    if (!heli.IsNull || !battery.IsNull)
                    {
                        CommandPictureOps.Answered(world, self, fire);
                        ws.Answered++;
                    }
                }
            }

            int help = CommandPictureOps.FirstRequest(world, self, RequestKind.Reinforce);
            if (help >= 0)
            {
                var request = world.GetComponentRO<CommandPicture>(self).Requests.Items[help];
                var from = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(world, request.FromNetId);
                if (from.IsNull) from = SubordinateByNetId(world, self, request.FromNetId);
                var unit = Free(world, self, SupportAsset.GroundUnit, from, p.Reserve);   // ⭐ CE-4589 HQ-11 — only the reserve when one is designated
                if (!unit.IsNull)
                {
                    var (behaviour, order) = ReinforceOrder(world, self, unit, request.At, now);   // ⭐ CE-4589 HQ-12 — the play matches the echelon
                    CommandOrders.Route(world, self, unit, OrderKind.Defend, behaviour, order, newOrder: true);   // ⭐ agg-behaviors R-8305 — every echelon's play is a Defend
                    CommandPictureOps.Answered(world, self, help);
                    ws.Answered++;
                }
            }
            Engineers(ref ws, self, world, now);   // ⭐ CE-2442 D-2
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2442</c> D-2 (📄 docs/designs/minefields/DESIGN_Minefields.md §13) — an <c>Engineers</c> request: a free engineer unit breaches the
        /// field at the request's point along the requester's line; when the HQ's side does not know the field in full, the unit's man first
        /// recons it (<c>ReconMinefield</c>, Mark) and the breach follows when it is marked or his walk ends. One job at a time.
        /// </summary>
        private static void Engineers(ref SupportCommandState ws, Entity self, EntityRepository world, SimInstant now)
        {
            if (ws.EngPhase != 0) { AdvanceEngineers(ref ws, self, world, now); return; }
            int req = CommandPictureOps.FirstRequest(world, self, RequestKind.Engineers);
            if (req < 0) return;
            var request = world.GetComponentRO<CommandPicture>(self).Requests.Items[req];
            var unit = Free(world, self, SupportAsset.Engineers, Entity.Null);
            if (unit.IsNull || !EngineersAnswer.FieldAt(world, request.At, out var field, out int fieldNetId)) return;   // waits in the picture
            var from = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(world, request.FromNetId);
            if (from.IsNull) from = SubordinateByNetId(world, self, request.FromNetId);
            var fromAt = !from.IsNull && world.HasComponent<SimTransform>(from) ? world.GetComponentRO<SimTransform>(from).Position
                       : world.HasComponent<SimTransform>(self) ? world.GetComponentRO<SimTransform>(self).Position : request.At;
            ws.EngFieldNetId = fieldNetId;
            ws.EngUnitPacked = (long)unit.PackedValue;
            ws.EngNear = request.At;
            ws.EngFar = EngineersAnswer.FarSide(world, field, fromAt, request.At);
            ws.EngSince = now;
            ws.EngineerJobs++;
            var man = EngineersAnswer.KnowsInFull(world, self, field) ? Entity.Null : EngineersAnswer.ManOf(world, unit);
            if (man.IsNull) OrderBreach(ref ws, self, world, unit, now);
            else
            {
                var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
                CommandOrders.Assign(world, self, man, BehaviorNames.ReconMinefield, JsonSerializer.Serialize(new ReconMinefieldParamsJsonDto
                {
                    Field = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(fieldNetId)), At = request.At, Mark = true,
                }, json), newOrder: true);
                ws.EngManPacked = (long)man.PackedValue;
                ws.EngPhase = 1;
            }
            CommandPictureOps.Answered(world, self, req);
            ws.Answered++;
        }

        private static void AdvanceEngineers(ref SupportCommandState ws, Entity self, EntityRepository world, SimInstant now)
        {
            var unit = new Entity((ulong)ws.EngUnitPacked);
            if (!world.IsAlive(unit)) { ws.EngPhase = 0; return; }
            if (now - ws.EngSince < OrderGraceSeconds) return;   // an order lands a tick later
            if (ws.EngPhase == 1)
            {
                var man = new Entity((ulong)ws.EngManPacked);
                var field = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(world, ws.EngFieldNetId);
                bool marked = !field.IsNull && EngineersAnswer.KnowsInFull(world, self, field);
                bool walking = world.IsAlive(man) && CurrentOrders.Of(world, man) is { Exists: true, Paused: false };
                if (marked || !walking) OrderBreach(ref ws, self, world, unit, now);
                return;
            }
            if (CurrentOrders.Of(world, unit) is { Exists: true, Paused: false }) return;   // the breach still runs
            ws.EngPhase = 0;
        }

        private static void OrderBreach(ref SupportCommandState ws, Entity self, EntityRepository world, Entity unit, SimInstant now)
        {
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            CommandOrders.Assign(world, self, unit, BehaviorNames.Breach, JsonSerializer.Serialize(new BreachParamsJsonDto
            {
                Field = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(ws.EngFieldNetId)), Start = ws.EngNear, End = ws.EngFar,
            }, json), newOrder: true);
            ws.EngPhase = 2;
            ws.EngSince = now;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4617</c> (B-5, RS-7; DESIGN_Radar_SAM.md) — SEAD FIRST: a FRESH air-defence element in the picture (a SPOT — the side's direction
        /// finding locates a radiating radar, CE-4617's <c>EmitterLocatingSystem</c>) is struck by the first free of a strike plane, a battery
        /// (fire for effect), an armed drone — whether or not a fire support asked for it (before, an air defence was struck only while
        /// answering a request near it, HQ-7 below). One strike per tick; an asset still on its strike is busy, so a still-fresh element is
        /// struck again once one is free.
        /// </summary>
        private static void Sead(in SupportCommandParams p, ref SupportCommandState ws, Entity self, EntityRepository world, SimInstant now)
        {
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            int pick = -1;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count == 0 || (e.Equipment & ElementEquipment.AirDefence) == 0 || now - e.ObservedAt > p.AirDefenceFreshSeconds) continue;
                if (pick < 0 || e.ObservedAt > picture.Elements.Items[pick].ObservedAt) pick = i;
            }
            if (pick < 0 || (ws.LastSeadAt > 0d && now - ws.LastSeadAt < SeadIntervalSeconds)) return;
            var at = picture.Elements.Items[pick].At;
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            var plane = Free(world, self, SupportAsset.StrikePlane, Entity.Null);
            if (!plane.IsNull)
            {
                CommandOrders.Order(world, self, plane, nameof(OrderKind.Strike), JsonSerializer.Serialize(new StrikeOrder { Location = at }, json), newOrder: true);
                ws.AirDefenceStrikes++;
                ws.LastSeadAt = now;
                return;
            }
            var battery = Free(world, self, SupportAsset.Battery, Entity.Null);
            if (!battery.IsNull)
            {
                CommandOrders.Order(world, self, battery, nameof(OrderKind.Strike),
                    JsonSerializer.Serialize(new StrikeOrder { Location = at, Method = FireMissionMethod.FireForEffect }, json), newOrder: true);
                ws.AirDefenceStrikes++;
                ws.LastSeadAt = now;
                return;
            }
            var drone = FreeDrone(world, self);
            if (!drone.IsNull)
            {
                CommandOrders.Order(world, self, drone, nameof(OrderKind.Strike), JsonSerializer.Serialize(new StrikeOrder { Location = at }, json), newOrder: true);
                ws.AirDefenceStrikes++;
                ws.LastSeadAt = now;
            }
        }

        /// <summary>⭐ <c>CE-4617</c> — at most one SEAD order this often (s).</summary>
        public const float SeadIntervalSeconds = 5f;

        /// <summary>⭐ <c>CE-4617</c> — the first live, free ARMED DRONE (an unmanned aircraft, SISO air category 50) in the roster.</summary>
        private static Entity FreeDrone(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<UnitRoster>(self)) return Entity.Null;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);
            for (int i = 0; i < roster.Count; i++)
            {
                var a = roster.SubordinateEntities[i];
                if (a.IsNull || !world.IsAlive(a) || !CombatLife.IsAlive(world, a)) continue;
                if (KindOf(world, a) != SupportAsset.AttackHelicopter || !RadarSamNodes.Unmanned(world, a)) continue;
                if (CommandOrders.Busy(world, self, a) || OnFireMission(world, a) || JustOrdered(world, self, a)) continue;
                return a;
            }
            return Entity.Null;
        }

        private static readonly int SupportCommandHash = BehaviorHash.FromName(BehaviorNames.SupportCommand);

        /// <summary>⭐ <c>CE-2314</c> (as AT-13 / DA-55) — the unit's task is NOT the headquarters play. <c>BasicPlatoonSop</c>'s hold row asks it:
        /// an HQ answers reports — holding it on a SPOT ordered its helicopter, its jet and its reserve to hold (measured live, the flagship
        /// <c>uc-urban-battle</c>: a SPOT from the reserve paused <c>SupportCommand</c> and every asset went <c>PlatformHold</c>).</summary>
        [Fbt.Kernel.SharedAiCondition]
        public static bool NotCommandingSupport(Entity self, EntityRepository world)
            => !world.HasComponent<BehaviorState>(self) || world.GetComponentRO<BehaviorState>(self).ActiveBehaviorHash != SupportCommandHash;

        /// <summary>HQ-7 — a fresh AirDefence element within <paramref name="metres"/> of <paramref name="point"/>, the nearest.</summary>
        private static bool AirDefenceNear(EntityRepository world, Entity self, Vector3 point, float metres, float freshSeconds, SimInstant now, out Vector3 at)
        {
            at = default;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            float best = float.MaxValue;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count == 0 || (e.Equipment & ElementEquipment.AirDefence) == 0 || now - e.ObservedAt > freshSeconds) continue;
                float d = Vector2.Distance(new Vector2(e.At.X, e.At.Y), new Vector2(point.X, point.Y));
                if (d <= metres && d < best) { best = d; at = e.At; }
            }
            return best < float.MaxValue;
        }

        /// <summary>The first live, free asset of <paramref name="kind"/> in the HQ's roster (never <paramref name="not"/>), or Null. ⭐ CE-4589 —
        /// <paramref name="slots"/> ≠ 0 ⇒ only roster slots whose bit is set (the designated reserve, HQ-11).</summary>
        public static Entity Free(EntityRepository world, Entity self, SupportAsset kind, Entity not, int slots = 0)
        {
            if (!world.HasComponent<UnitRoster>(self)) return Entity.Null;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);
            for (int i = 0; i < roster.Count; i++)
            {
                if (slots != 0 && (i >= 32 || (slots & (1 << i)) == 0)) continue;
                var a = roster.SubordinateEntities[i];
                if (a.IsNull || a.Equals(not) || !world.IsAlive(a) || !CombatLife.IsAlive(world, a)) continue;
                if (KindOf(world, a) != kind || CommandOrders.Busy(world, self, a) || OnFireMission(world, a) || JustOrdered(world, self, a)) continue;
                return a;
            }
            return Entity.Null;
        }

        private static readonly int[] MoveHashes =
        {
            BehaviorHash.FromName(BehaviorNames.MoveToLocation), BehaviorHash.FromName(BehaviorNames.FollowRoute),
            BehaviorHash.FromName(BehaviorNames.AdvanceTo), BehaviorHash.FromName(BehaviorNames.SquadMarch),
            BehaviorHash.FromName(BehaviorNames.InfantrySquadMove), BehaviorHash.FromName(BehaviorNames.PersonMove),
            BehaviorHash.FromName(BehaviorNames.PlatformMove), BehaviorHash.FromName(BehaviorNames.AirPlatformMove),
            BehaviorHash.FromName(BehaviorNames.PlatoonConvoy), BehaviorHash.FromName(BehaviorNames.PlatformConvoy),
        };

        /// <summary>
        /// ⭐ <c>CE-5047</c> (Command Chain §8d, the <c>Free</c>/<c>Busy</c> gap; user-approved lean 2026-11-23: "busy = any running Superior order") — the
        /// asset runs a task from a superior — whoever gave it (a scenario's author, another commander) — that is more than a MOVE: an attack, a
        /// strike, a defence, a search. The HQ does not take it off that. ⭐ A move (a patrol, a transit, a march, a convoy) stays the HQ's to
        /// re-task: 📐 <c>uc-a10-zu23-dry</c> and <c>uc-urban-battle</c> author their A-10 on a <c>FollowRoute</c> and the HQ's SEAD strike pulls it
        /// off that route. An idle asset is free as before. 📐 Found on the Stechovice showcase: the HQ took the Apache off its authored attack,
        /// and (96 s) ordered the village platoon off its authored <c>PlatoonAttack</c> to reinforce the riverbank.
        /// </summary>
        private static bool OnFireMission(EntityRepository world, Entity asset)
        {
            if (!world.HasComponent<BehaviorState>(asset)) return false;
            ref readonly var bs = ref world.GetComponentRO<BehaviorState>(asset);
            if (bs.Origin != BehaviorOrigin.Superior || bs.ActiveBehaviorHash == BehaviorIds.None) return false;
            for (int i = 0; i < MoveHashes.Length; i++) if (bs.ActiveBehaviorHash == MoveHashes[i]) return false;
            return true;
        }

        /// <summary>An order given this recently has not reached the asset's brain yet (it runs from the next frame): still busy (s).</summary>
        public const float OrderGraceSeconds = 2f;

        private static bool JustOrdered(EntityRepository world, Entity self, Entity a)
        {
            if (!world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            int row = CommandPictureOps.RowOf(picture, CommandPictureOps.NetIdOf(world, a));
            return row >= 0 && picture.Subordinates.Items[row].OrderedAt > 0f
                   && CommandPictureOps.Now(world) - picture.Subordinates.Items[row].OrderedAt < OrderGraceSeconds;
        }

        /// <summary>HQ-6 — an asset's kind: an armed rotorcraft · an armed fixed-wing · an artillery battery unit · another unit.</summary>
        public static SupportAsset KindOf(EntityRepository world, Entity a)
        {
            if (SquadManeuverRunner.IsUnit(world, a) && EngineersAnswer.HasClearer(world, a)) return SupportAsset.Engineers;   // ⭐ CE-2442 D-2 — before GroundUnit
            if (SquadManeuverRunner.IsUnit(world, a))
                return world.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(a)
                       && world.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(a).TkbType == Hrot.Map.Common.TkbEntityTypes.Unit_ArtilleryBattery
                    ? SupportAsset.Battery : SupportAsset.GroundUnit;
            if (CombatTkb.MountOf(world, a, 0) == null) return SupportAsset.None;
            var loco = Fdp.Toolkit.Motion.Locomotions.Of(world, a);
            return loco == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft ? SupportAsset.AttackHelicopter
                 : loco == Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing ? SupportAsset.StrikePlane : SupportAsset.None;
        }

        /// <summary>A fresh enemy element counts as the threat for a company's line this long after it was seen (s).</summary>
        public const float ThreatFreshSeconds = 120f;

        /// <summary>⭐ <c>CE-4589</c> (HQ-12) — the reinforcement's play by its ECHELON (<see cref="Fdp.Toolkit.Squad.Echelon"/>): a company holds a line
        /// at the point facing the HQ picture's nearest fresh element (none ⇒ north), a platoon <c>PlatoonDefend</c> 60 m, a squad <c>SquadDefend</c> 40 m.</summary>
        public static (string Behaviour, string Json) ReinforceOrder(EntityRepository world, Entity self, Entity unit, Vector3 at, SimInstant now)
        {
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            int depth = Fdp.Toolkit.Squad.Echelon.DepthOf(world, unit);
            if (depth >= Fdp.Toolkit.Squad.Echelon.Company)
            {
                var dto = new DefendOrder { Position = at };   // ⭐ R-8300 — the neutral Defend order (every echelon's play reads it)
                if (NearestThreat(world, self, at, now, out var t)) dto.Threat = t;   // none ⇒ zero = north of the line
                return (BehaviorNames.CompanyDefend, JsonSerializer.Serialize(dto, json));
            }
            return depth == Fdp.Toolkit.Squad.Echelon.Platoon
                ? (BehaviorNames.PlatoonDefend, JsonSerializer.Serialize(new DefendOrder { Position = at, SearchRadius = PlatoonDefendMetres }, json))
                : (BehaviorNames.SquadDefend, JsonSerializer.Serialize(new DefendOrder { Position = at, SearchRadius = SquadDefendMetres }, json));
        }

        /// <summary>The HQ picture's nearest fresh (≤ <see cref="ThreatFreshSeconds"/>) enemy element to <paramref name="point"/>.</summary>
        private static bool NearestThreat(EntityRepository world, Entity self, Vector3 point, SimInstant now, out Vector3 at)
        {
            at = default;
            if (!world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            float best = float.MaxValue;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count == 0 || now - e.ObservedAt > ThreatFreshSeconds) continue;
                float d = Vector2.Distance(new Vector2(e.At.X, e.At.Y), new Vector2(point.X, point.Y));
                if (d < best) { best = d; at = e.At; }
            }
            return best < float.MaxValue;
        }

        /// <summary>A roster member whose picture id is <paramref name="netId"/> (a world with no network: its handle).</summary>
        private static Entity SubordinateByNetId(EntityRepository world, Entity self, int netId)
        {
            if (!world.HasComponent<UnitRoster>(self)) return Entity.Null;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);
            for (int i = 0; i < roster.Count; i++)
                if (CommandPictureOps.NetIdOf(world, roster.SubordinateEntities[i]) == netId) return roster.SubordinateEntities[i];
            return Entity.Null;
        }
    }
}

namespace Hrot.AI.Behaviors.Orders
{
    /// <summary>⭐ <c>CE-2312</c> (HQ-6) — the authored contract of the HQ's <c>SupportCommand</c>. A zero field keeps its default.</summary>
    [Fdp.Core.ParamsDto]
    [StructLayout(LayoutKind.Sequential)]
    public struct SupportCommandParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SupportCommand;

        /// <summary>An air defence reported within this of the fire support's point is struck first (m); 0 = 2000.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("AirDefenceMetres")]
        public float AirDefenceMetres;

        /// <summary>A reported air defence is a threat this long after it was observed (s); 0 = 120.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("AirDefenceFreshSeconds")]
        public float AirDefenceFreshSeconds;

        /// <summary>⭐ <c>CE-4589</c> (HQ-11) — the roster slots held as the battalion's RESERVE (bit k = slot k); a Reinforce is answered only from
        /// them. 0 = any free ground unit (as before).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("Reserve")]
        public int Reserve;
    }
}
