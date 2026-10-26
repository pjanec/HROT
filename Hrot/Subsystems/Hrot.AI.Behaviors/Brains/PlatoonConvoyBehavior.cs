using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Squad.Command;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Common.Components;
using Hrot.Map.Definitions.Behavior;
using Hrot.Map.Definitions.Behavior.Orders;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2347</c> (R-2069) — the convoy's plan: the route, the phase <see cref="PlatoonConvoyBehavior.KeepConvoy"/> orders for, and per
    /// roster slot the vehicle its squad rides (recorded at the plan from where its men sit), whether it follows on foot, and what is still
    /// to be (re-)ordered. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §11a.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ConvoyPlan
    {
        public const int Capacity = CommandPicture.MaxSubordinates;
        public const byte PhasePlan = 0, PhaseMove = 1, PhaseContact = 2, PhaseSecure = 3, PhaseRemount = 4;
        public const byte VehicleNone = 0, VehicleRoute = 1, VehicleHold = 2;

        public long RouteNet;
        public Vector3 RouteEnd;
        public float Speed;
        public float ClearSeconds;
        public byte Made;
        public byte Phase;
        public float PhaseAt;
        /// <summary>The vehicle each slot's squad rides (<see cref="Entity.PackedValue"/>; 0 = none).</summary>
        public fixed ulong Vehicle[Capacity];
        /// <summary>1 = the squad follows on foot (no vehicle that can take it).</summary>
        public fixed byte OnFoot[Capacity];
        /// <summary>1 = the squad's order of this phase is still to be given.</summary>
        public fixed byte Dirty[Capacity];
        /// <summary>The order this run last gave the slot's vehicle: <see cref="VehicleNone"/>, <see cref="VehicleRoute"/>, <see cref="VehicleHold"/>.</summary>
        public fixed byte VehicleOrder[Capacity];
        /// <summary>DA-102 — how many casualty recoveries the squad was sent on in this remount.</summary>
        public fixed byte Recovers[Capacity];

        public Entity VehicleOf(int slot) => Vehicle[slot] == 0 ? Entity.Null : new Entity(Vehicle[slot]);
    }

    /// <summary>⭐ <c>CE-2347</c> — what <see cref="PlatoonConvoyBehavior.KeepConvoy"/> keeps: how many orders it gave.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ConvoyKeep
    {
        public int Orders;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2347</c> (R-2069, DA-99…DA-103; 📄 Defend and Attack §11a) — <c>PlatoonConvoy</c>, the mounted variant of DA-17: the squads ride
    /// the vehicles they sit in along a route (CMove); on a man out, a vehicle stopped dead, a squad in its contact drill or an element seen
    /// near (CContact) the squads' OWN SOP dismounts and fights and the platoon orders no squad — the vehicles drive on as they do (a carrier
    /// pushes through a kill zone itself, CE-2307), except one whose squad got out, which waits for it; the platoon SOP's hold may pause the
    /// task meanwhile (it leaves a fighting squad alone, CE-4032) and the task picks up when it ends; after
    /// <see cref="ConvoyPlan.ClearSeconds"/> with nothing seen and nobody fighting the dismounted squads secure at their vehicles (CSecure);
    /// then each brings its wounded aboard and remounts, onto another vehicle with room when its own cannot move, else it follows on foot
    /// (CRemount); the vehicles carry on from where they stand (CMove). Done at the route's end. One activity
    /// (<see cref="KeepConvoy"/>) gives every order; a squad running its own reaction is never ordered.
    /// </summary>
    public static class PlatoonConvoyBehavior
    {
        public const float DefaultClearSeconds = 30f;
        public const float ContactMetres = 300f, ContactFreshSeconds = 10f;
        public const float SecureRadius = 25f, SecureTimeoutSeconds = 60f, RemountTimeoutSeconds = 120f;
        public const float ArrivalMetres = 15f, FootArrivalMetres = 30f;
        public const int MaxRecovers = 4;
        public const float EmbarkTimeoutSeconds = 60f;

        [StructLayout(LayoutKind.Sequential)]
        public struct PlatoonConvoyBlackboard
        {
            public ConvoyPlan Plan;
            public ConvoyKeep Keep;
        }

        [BehaviorResolver(BehaviorNames.PlatoonConvoy)]
        public static void Resolve(in PlatoonConvoyParamsJsonDto authored, ref PlatoonConvoyBlackboard block, EntityRepository world, Entity self)
        {
            block.Plan = default;
            block.Keep = default;
            block.Plan.RouteNet = authored.Route.NetworkId;
            block.Plan.Speed = authored.Speed;
            block.Plan.ClearSeconds = authored.ClearSeconds > 0f ? authored.ClearSeconds : DefaultClearSeconds;
        }

        [Behavior(BehaviorNames.PlatoonConvoy, Params = typeof(PlatoonConvoyParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [HsmDefinition(BehaviorNames.PlatoonConvoy, Curated = true, Group = "Library/Platoon", ParamsType = typeof(PlatoonConvoyBlackboard))]
        public static StateMachineGraph BuildPlatoonConvoy()
        {
            var builder = new HsmBuilder("PlatoonConvoy_HSM");
            var plan = builder.State("CPlan").Initial()
                .Activity<PlatoonConvoyBlackboard, ConvoyPlan, ConvoyKeep>(bb => bb.Plan, bb => bb.Keep, PlanConvoy);
            var move = builder.State("CMove")
                .OnEntry<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, EnterMove)
                .Activity<PlatoonConvoyBlackboard, ConvoyPlan, ConvoyKeep>(bb => bb.Plan, bb => bb.Keep, KeepConvoy);
            var contact = builder.State("CContact")
                .OnEntry<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, EnterContact)
                .Activity<PlatoonConvoyBlackboard, ConvoyPlan, ConvoyKeep>(bb => bb.Plan, bb => bb.Keep, KeepConvoy);
            var secure = builder.State("CSecure")
                .OnEntry<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, EnterSecure)
                .Activity<PlatoonConvoyBlackboard, ConvoyPlan, ConvoyKeep>(bb => bb.Plan, bb => bb.Keep, KeepConvoy);
            var remount = builder.State("CRemount")
                .OnEntry<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, EnterRemount)
                .Activity<PlatoonConvoyBlackboard, ConvoyPlan, ConvoyKeep>(bb => bb.Plan, bb => bb.Keep, KeepConvoy);
            builder.State("CDone").Final();

            plan.On(0).GoTo("CMove").Guard<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, Planned).Polled();
            move.On(0).GoTo("CContact").Guard<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, InContact).Polled();
            move.On(0).GoTo("CDone").Guard<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, Arrived).Polled();
            contact.On(0).GoTo("CSecure").Guard<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, Quiet).Polled();
            secure.On(0).GoTo("CContact").Guard<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, ContactAgain).Polled();
            secure.On(0).GoTo("CRemount").Guard<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, Secured).Polled();
            remount.On(0).GoTo("CContact").Guard<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, ContactAgain).Polled();
            remount.On(0).GoTo("CMove").Guard<PlatoonConvoyBlackboard, ConvoyPlan>(bb => bb.Plan, Mounted).Polled();
            return builder.Build();
        }

        // ── the plan (DA-99) ────────────────────────────────────────────────────────────────────────────────────────────────

        private static readonly HrotRouteSource s_routes = new();

        /// <summary>CPlan's activity: the route's end (waits until the route is known) and each slot's vehicle — the one its men ride; a
        /// squad riding nothing follows on foot.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus PlanConvoy(ref ConvoyPlan p, ref ConvoyKeep ws, Entity self, EntityRepository world)
        {
            if (p.Made != 0) return NodeStatus.Running;
            int n = RosterCount(world, self);
            if (n == 0) return NodeStatus.Running;
            var points = new List<NavRouteWaypoint>();
            if (!s_routes.TryGetRoute(world, p.RouteNet, points, out _, out _)) return NodeStatus.Running;
            p.RouteEnd = points[^1].Position;
            for (int s = 0; s < n; s++)
            {
                var v = RiddenVehicle(world, CommandPictureOps.Subordinate(world, self, s), 0);
                p.Vehicle[s] = v.IsNull ? 0UL : v.PackedValue;
                p.OnFoot[s] = v.IsNull ? (byte)1 : (byte)0;
            }
            p.Made = 1;
            return NodeStatus.Running;
        }

        /// <summary>The vehicle a man of <paramref name="unit"/> rides (a unit: its members, depth-first), or Null.</summary>
        public static Entity RiddenVehicle(EntityRepository world, Entity unit, int depth)
        {
            if (unit.IsNull || !world.IsAlive(unit)) return Entity.Null;
            var v = AirHolds.CarrierOf(world, unit);
            if (!v.IsNull) return v;
            if (depth >= 4 || !world.HasComponent<UnitRoster>(unit)) return Entity.Null;
            var roster = world.GetComponentRO<UnitRoster>(unit);
            for (int i = 0; i < roster.Count; i++)
            {
                v = RiddenVehicle(world, roster.SubordinateEntities[i], depth + 1);
                if (!v.IsNull) return v;
            }
            return Entity.Null;
        }

        // ── the phases ───────────────────────────────────────────────────────────────────────────────────────────────────

        [SharedAiAction]
        public static NodeStatus EnterMove(ref ConvoyPlan p, Entity self, EntityRepository world) { SetPhase(ref p, world, ConvoyPlan.PhaseMove); return NodeStatus.Success; }

        [SharedAiAction]
        public static NodeStatus EnterContact(ref ConvoyPlan p, Entity self, EntityRepository world) { SetPhase(ref p, world, ConvoyPlan.PhaseContact); return NodeStatus.Success; }

        [SharedAiAction]
        public static NodeStatus EnterSecure(ref ConvoyPlan p, Entity self, EntityRepository world) { SetPhase(ref p, world, ConvoyPlan.PhaseSecure); return NodeStatus.Success; }

        /// <summary>DA-102 — CRemount's entry: a squad whose vehicle cannot move takes the first one that can and has seats for all its
        /// live men (seats already promised to another squad in this pass counted), else it follows on foot.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus EnterRemount(ref ConvoyPlan p, Entity self, EntityRepository world)
        {
            SetPhase(ref p, world, ConvoyPlan.PhaseRemount);
            int n = Math.Min(RosterCount(world, self), ConvoyPlan.Capacity);
            Span<int> promised = stackalloc int[ConvoyPlan.Capacity];
            for (int s = 0; s < n; s++)
            {
                p.Recovers[s] = 0;
                if (p.OnFoot[s] != 0) continue;
                var squad = CommandPictureOps.Subordinate(world, self, s);
                if (!LiveSquad(world, squad)) continue;
                if (ConvoyConditions.CanMove(world, p.VehicleOf(s))) continue;
                int need = LiveMen(world, squad, 0);
                p.Vehicle[s] = 0;
                p.OnFoot[s] = 1;
                for (int o = 0; o < n; o++)
                {
                    var other = p.VehicleOf(o);
                    if (o == s || p.OnFoot[o] != 0 || !ConvoyConditions.CanMove(world, other)) continue;
                    int k = SlotOfVehicle(ref p, other, n);
                    if (FreePassengerSeats(world, other) - promised[k] < need) continue;
                    promised[k] += need;
                    p.Vehicle[s] = other.PackedValue;
                    p.VehicleOrder[s] = p.VehicleOrder[k];   // the order its owner already gave it
                    p.OnFoot[s] = 0;
                    break;
                }
            }
            return NodeStatus.Success;
        }

        private static unsafe int SlotOfVehicle(ref ConvoyPlan p, Entity v, int n)
        {
            for (int s = 0; s < n; s++) if (p.OnFoot[s] == 0 && p.Vehicle[s] == v.PackedValue) return s;
            return 0;
        }

        private static unsafe void SetPhase(ref ConvoyPlan p, EntityRepository world, byte phase)
        {
            p.Phase = phase;
            p.PhaseAt = CommandPictureOps.Now(world);
            for (int s = 0; s < ConvoyPlan.Capacity; s++) p.Dirty[s] = 1;
        }

        /// <summary>The ONE order writer: each slot's vehicle and squad get this phase's order when it does not stand or the phase changed.
        /// ⛔ A squad running its own reaction is left to it (CE-4032) and ordered when that ends.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus KeepConvoy(ref ConvoyPlan p, ref ConvoyKeep ws, Entity self, EntityRepository world)
        {
            if (p.Made == 0) return NodeStatus.Running;
            int n = Math.Min(RosterCount(world, self), ConvoyPlan.Capacity);
            var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            if (p.Phase == ConvoyPlan.PhaseRemount && CommandPictureOps.Now(world) - p.PhaseAt >= RemountTimeoutSeconds)
                for (int s = 0; s < n; s++)   // DA-102 — whoever is not aboard by now follows on foot
                    if (p.OnFoot[s] == 0 && !SquadEmbarkBehavior.AboardIt(world, CommandPictureOps.Subordinate(world, self, s), p.VehicleOf(s)))
                    { p.OnFoot[s] = 1; p.Dirty[s] = 1; }
            for (int s = 0; s < n; s++)
            {
                KeepVehicle(ref p, ref ws, s, n, self, world, json);
                KeepSquad(ref p, ref ws, s, self, world, json);
            }
            return NodeStatus.Running;
        }

        /// <summary>The slot's vehicle: on the route in CMove; in CContact left to drive on as it does (its own SOP pushes through a kill
        /// zone, CE-2307) unless a squad it carries has a man out — then it waits for them; held in CSecure and CRemount. A vehicle running
        /// its own reaction is left to it.</summary>
        private static unsafe void KeepVehicle(ref ConvoyPlan p, ref ConvoyKeep ws, int slot, int n, Entity self, EntityRepository world, JsonSerializerOptions json)
        {
            var v = p.VehicleOf(slot);
            // ⭐ CE-2354 — a vehicle is ordered whoever rides it: a squad left on foot at the remount (DA-102) still has a vehicle that
            //   carries on (DA-103). 📌 Live on uc-urban-battle both squads went on foot after the 120 s and the APC and IFV held for good.
            if (v.IsNull || !ConvoyConditions.CanMove(world, v) || PlatoonHoldBehavior.Fighting(world, v)) return;
            for (int o = 0; o < slot; o++) if (p.Vehicle[o] == p.Vehicle[slot]) return;   // a shared vehicle: ordered once
            byte want;
            switch (p.Phase)
            {
                case ConvoyPlan.PhaseMove: want = ConvoyPlan.VehicleRoute; break;
                case ConvoyPlan.PhaseContact: want = CarriesDismounted(ref p, v, n, self, world) ? ConvoyPlan.VehicleHold : ConvoyPlan.VehicleNone; break;
                default: want = ConvoyPlan.VehicleHold; break;
            }
            if (want == ConvoyPlan.VehicleNone) return;
            if (p.VehicleOrder[slot] == want && CommandOrders.Stands(world, self, v)) return;
            p.VehicleOrder[slot] = want;
            if (want == ConvoyPlan.VehicleRoute)
            {
                var order = JsonSerializer.Serialize(new FollowRouteParamsJsonDto { RouteEntityId = new EntityRef(p.RouteNet), Speed = p.Speed }, json);
                if (CommandOrders.StillRunning(world, self, v, BehaviorNames.FollowRoute, order)) return;
                CommandOrders.Assign(world, self, v, BehaviorNames.FollowRoute, order, newOrder: true);
            }
            else CommandOrders.Order(world, self, v, "Hold", "{}", newOrder: true);
            ws.Orders++;
        }

        /// <summary>A squad riding <paramref name="v"/> has a man out of it.</summary>
        private static unsafe bool CarriesDismounted(ref ConvoyPlan p, Entity v, int n, Entity self, EntityRepository world)
        {
            for (int s = 0; s < n; s++)
                if (p.OnFoot[s] == 0 && p.Vehicle[s] == v.PackedValue && Dismounted(world, CommandPictureOps.Subordinate(world, self, s))) return true;
            return false;
        }

        private static unsafe void KeepSquad(ref ConvoyPlan p, ref ConvoyKeep ws, int slot, Entity self, EntityRepository world, JsonSerializerOptions json)
        {
            var squad = CommandPictureOps.Subordinate(world, self, slot);
            if (!LiveSquad(world, squad) || PlatoonHoldBehavior.Fighting(world, squad)) return;
            if (!OrderFor(ref p, slot, squad, world, json, out var behaviour, out var order, out bool isAbstract))
            {
                p.Dirty[slot] = 0;
                return;
            }
            if (isAbstract)
            {
                if (p.Dirty[slot] == 0 && CommandOrders.Stands(world, self, squad)) return;
                p.Dirty[slot] = 0;
                CommandOrders.Order(world, self, squad, behaviour, order, newOrder: true);
            }
            else
            {
                // the same order this run last gave is not given again — a finished embark is not restarted — except a recovery the
                // squad reported ended (Set or TaskFailed) with the man still lying: tried again, at most MaxRecovers times
                if (CommandOrders.Gave(world, self, squad, behaviour, order)
                    && (behaviour != BehaviorNames.SquadRecover || !CasevacAnswers.Returned(world, self, slot))) return;
                if (behaviour == BehaviorNames.SquadRecover) p.Recovers[slot]++;
                CommandOrders.Assign(world, self, squad, behaviour, order, newOrder: true);
            }
            ws.Orders++;
        }

        /// <summary>The order <paramref name="slot"/>'s squad gets in the plan's phase (false = none: it keeps what it has — riding, or its
        /// own SOP's fight).</summary>
        private static unsafe bool OrderFor(ref ConvoyPlan p, int slot, Entity squad, EntityRepository world, JsonSerializerOptions json,
                                            out string behaviour, out string order, out bool isAbstract)
        {
            behaviour = order = "";
            isAbstract = false;
            var v = p.VehicleOf(slot);
            switch (p.Phase)
            {
                case ConvoyPlan.PhaseMove:
                    if (p.OnFoot[slot] == 0) return false;
                    behaviour = "Move";
                    isAbstract = true;
                    order = JsonSerializer.Serialize(new MoveOrder { Location = OrderGeo.ToGeo(world, p.RouteEnd), Speed = p.Speed, ArrivalRadius = FootArrivalMetres }, json);
                    return true;
                case ConvoyPlan.PhaseSecure:   // DA-101 — only a squad with a man out; one still aboard stays
                    if (!Dismounted(world, squad)) return false;
                    behaviour = BehaviorNames.SquadDefend;
                    order = JsonSerializer.Serialize(new SquadDefendParamsJsonDto
                    {
                        Position = !v.IsNull && world.HasComponent<SimTransform>(v) ? world.GetComponentRO<SimTransform>(v).Position : StaffComposerSystem.CentreOf(world, squad),
                        SearchRadius = SecureRadius, Speed = p.Speed,
                    }, json);
                    return true;
                case ConvoyPlan.PhaseRemount:   // DA-102 — its wounded first, kept carried, then aboard its vehicle
                    if (p.OnFoot[slot] != 0 || v.IsNull || SquadEmbarkBehavior.AboardIt(world, squad, v)) return false;
                    var vat = world.HasComponent<SimTransform>(v) ? world.GetComponentRO<SimTransform>(v).Position : Vector3.Zero;
                    var casualty = p.Recovers[slot] < MaxRecovers ? Casualty(world, squad, 0) : Entity.Null;
                    if (!casualty.IsNull)
                    {
                        behaviour = BehaviorNames.SquadRecover;
                        order = JsonSerializer.Serialize(new SquadRecoverParamsJsonDto
                        {
                            Casualty = EntityRef.Of(world, casualty), To = vat, PutDown = false, Speed = p.Speed,
                        }, json);
                        return true;
                    }
                    behaviour = BehaviorNames.SquadEmbark;
                    order = JsonSerializer.Serialize(new SquadEmbarkParamsJsonDto
                    {
                        Vehicle = EntityRef.Of(world, v), TimeoutSeconds = EmbarkTimeoutSeconds, Speed = p.Speed,
                    }, json);
                    return true;
                default:   // contact: the squads' own SOP fights
                    return false;
            }
        }

        // ── the guards ───────────────────────────────────────────────────────────────────────────────────────────────

        [SharedAiCondition]
        public static bool Planned(ref ConvoyPlan p, Entity self, EntityRepository world) => p.Made != 0;

        /// <summary>CMove → CContact (DA-100): a squad in its contact drill, an element seen within <see cref="ContactFreshSeconds"/> and
        /// <see cref="ContactMetres"/>, a riding squad with a man out, or its vehicle stopped dead.</summary>
        [SharedAiCondition]
        public static unsafe bool InContact(ref ConvoyPlan p, Entity self, EntityRepository world)
        {
            if (p.Made == 0) return false;
            if (AnyEngaged(world, self) || ElementSeen(world, self, ContactFreshSeconds, float.NegativeInfinity)) return true;
            int n = Math.Min(RosterCount(world, self), ConvoyPlan.Capacity);
            for (int s = 0; s < n; s++)
            {
                if (p.OnFoot[s] != 0) continue;
                var squad = CommandPictureOps.Subordinate(world, self, s);
                if (!LiveSquad(world, squad)) continue;
                if (Dismounted(world, squad) || !ConvoyConditions.CanMove(world, p.VehicleOf(s))) return true;
            }
            return false;
        }

        /// <summary>CMove → CDone (DA-103): every vehicle that can move is at the route's end, every squad on foot reports Set or is near it.</summary>
        [SharedAiCondition]
        public static unsafe bool Arrived(ref ConvoyPlan p, Entity self, EntityRepository world)
        {
            if (p.Made == 0) return false;
            int n = Math.Min(RosterCount(world, self), ConvoyPlan.Capacity);
            for (int s = 0; s < n; s++)
            {
                var squad = CommandPictureOps.Subordinate(world, self, s);
                if (p.OnFoot[s] != 0 && LiveSquad(world, squad)
                    && CommandPictureOps.ProgressOf(world, self, s) != ProgressKind.Set
                    && Flat(StaffComposerSystem.CentreOf(world, squad), p.RouteEnd) > FootArrivalMetres) return false;
                var v = p.VehicleOf(s);   // ⭐ CE-2354 — every vehicle that can move, whoever rides it
                if (v.IsNull || !ConvoyConditions.CanMove(world, v)) continue;
                if (!world.HasComponent<SimTransform>(v) || Flat(world.GetComponentRO<SimTransform>(v).Position, p.RouteEnd) > ArrivalMetres) return false;
            }
            return true;
        }

        /// <summary>CContact → CSecure (DA-101): nothing seen for <see cref="ConvoyPlan.ClearSeconds"/> within <see cref="ContactMetres"/>
        /// and no squad in its contact drill.</summary>
        [SharedAiCondition]
        public static bool Quiet(ref ConvoyPlan p, Entity self, EntityRepository world)
            => !AnyEngaged(world, self) && !ElementSeen(world, self, p.ClearSeconds, float.NegativeInfinity);

        /// <summary>CSecure / CRemount → CContact: a squad in its contact drill, or an element seen since the phase began.</summary>
        [SharedAiCondition]
        public static bool ContactAgain(ref ConvoyPlan p, Entity self, EntityRepository world)
            => AnyEngaged(world, self) || ElementSeen(world, self, ContactFreshSeconds, p.PhaseAt);

        /// <summary>CSecure → CRemount (DA-101): every squad with a man out reports Set on its post, or <see cref="SecureTimeoutSeconds"/>.</summary>
        [SharedAiCondition]
        public static bool Secured(ref ConvoyPlan p, Entity self, EntityRepository world)
        {
            if (CommandPictureOps.Now(world) - p.PhaseAt >= SecureTimeoutSeconds) return true;
            int n = Math.Min(RosterCount(world, self), ConvoyPlan.Capacity);
            for (int s = 0; s < n; s++)
            {
                var squad = CommandPictureOps.Subordinate(world, self, s);
                if (!LiveSquad(world, squad) || !Dismounted(world, squad)) continue;
                if (CommandPictureOps.ProgressOf(world, self, s) != ProgressKind.Set) return false;
            }
            return true;
        }

        /// <summary>CRemount → CMove (DA-102): every live squad is aboard its vehicle or follows on foot.</summary>
        [SharedAiCondition]
        public static unsafe bool Mounted(ref ConvoyPlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), ConvoyPlan.Capacity);
            for (int s = 0; s < n; s++)
            {
                var squad = CommandPictureOps.Subordinate(world, self, s);
                if (!LiveSquad(world, squad) || p.OnFoot[s] != 0) continue;
                if (!SquadEmbarkBehavior.AboardIt(world, squad, p.VehicleOf(s))) return false;
            }
            return true;
        }

        private static readonly int ReactHash = BehaviorHash.FromName(BehaviorNames.ReactToContact);
        private static readonly int DismountHash = BehaviorHash.FromName(BehaviorNames.SquadDismount);

        // ── the reads ────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>A squad in its contact drill: its SOP's reaction is <c>ReactToContact</c> or <c>SquadDismount</c>.</summary>
        public static bool Engaged(EntityRepository world, Entity squad)
        {
            if (squad.IsNull || !world.IsAlive(squad) || !world.HasComponent<BehaviorState>(squad)) return false;
            ref readonly var bs = ref world.GetComponentRO<BehaviorState>(squad);
            return bs.Origin == BehaviorOrigin.Reaction && (bs.ActiveBehaviorHash == ReactHash || bs.ActiveBehaviorHash == DismountHash);
        }

        private static bool AnyEngaged(EntityRepository world, Entity self)
        {
            int n = RosterCount(world, self);
            for (int s = 0; s < n; s++) if (Engaged(world, CommandPictureOps.Subordinate(world, self, s))) return true;
            return false;
        }

        /// <summary>A live element in the platoon's picture within <see cref="ContactMetres"/> of the platoon, observed within
        /// <paramref name="freshSeconds"/> and after <paramref name="since"/>.</summary>
        public static bool ElementSeen(EntityRepository world, Entity self, float freshSeconds, float since)
        {
            if (!world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            var at = StaffComposerSystem.CentreOf(world, self);
            float now = CommandPictureOps.Now(world);
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count == 0 || now - e.ObservedAt > freshSeconds || e.ObservedAt <= since) continue;
                if (Flat(e.At, at) <= ContactMetres) return true;
            }
            return false;
        }

        /// <summary>A live man of <paramref name="squad"/> (a unit: of its members) stands outside any vehicle; a man down does not count
        /// (he is carried in at the remount).</summary>
        public static bool Dismounted(EntityRepository world, Entity squad, int depth = 0)
        {
            if (squad.IsNull || !world.IsAlive(squad)) return false;
            if (SquadManeuverRunner.IsUnit(world, squad) || world.HasComponent<UnitRoster>(squad))
            {
                if (depth >= 4 || !world.HasComponent<UnitRoster>(squad)) return false;
                var roster = world.GetComponentRO<UnitRoster>(squad);
                for (int i = 0; i < roster.Count; i++)
                    if (Dismounted(world, roster.SubordinateEntities[i], depth + 1)) return true;
                return false;
            }
            return Injury.IsInTheFight(world, squad) && AirHolds.CarrierOf(world, squad).IsNull;
        }

        /// <summary>The first man of <paramref name="squad"/> who is down and nobody carries or seats (a unit: its members), or Null.</summary>
        public static Entity Casualty(EntityRepository world, Entity squad, int depth)
        {
            if (squad.IsNull || depth >= 4 || !world.HasComponent<UnitRoster>(squad)) return Entity.Null;
            var roster = world.GetComponentRO<UnitRoster>(squad);
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (!world.IsAlive(m)) continue;
                if (SquadManeuverRunner.IsUnit(world, m)) { var c = Casualty(world, m, depth + 1); if (!c.IsNull) return c; continue; }
                if (Injury.IsIncapacitated(world, m) && AirHolds.CarrierOf(world, m).IsNull) return m;
            }
            return Entity.Null;
        }

        /// <summary>The men of <paramref name="squad"/> who need a seat: alive (a man down too — he is carried in).</summary>
        public static int LiveMen(EntityRepository world, Entity squad, int depth)
        {
            if (squad.IsNull || !world.IsAlive(squad)) return 0;
            if (!world.HasComponent<UnitRoster>(squad)) return CombatLife.IsAlive(world, squad) ? 1 : 0;
            if (depth >= 4) return 0;
            var roster = world.GetComponentRO<UnitRoster>(squad);
            int count = 0;
            for (int i = 0; i < roster.Count; i++) count += LiveMen(world, roster.SubordinateEntities[i], depth + 1);
            return count;
        }

        private static bool LiveSquad(EntityRepository world, Entity squad) => LiveMen(world, squad, 0) > 0;

        /// <summary>The empty passenger seats of <paramref name="vehicle"/> (a seat whose man is dead counts as empty).</summary>
        public static unsafe int FreePassengerSeats(EntityRepository world, Entity vehicle)
        {
            int capacity = Embarkation.CapacityOf(world, vehicle);
            if (capacity == 0) return 0;
            var buffer = world.GetComponentRO<PassengerBuffer>(vehicle);
            var seats = world.IsComponentTypeRegistered<CarrierSeats>() && world.HasComponent<CarrierSeats>(vehicle)
                ? world.GetComponentRO<CarrierSeats>(vehicle) : default;
            int used = 0;
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                if (i < seats.SeatCount && seats.Roles[i] != SeatRole.Passenger) continue;
                var e = buffer.Passengers[i];
                if (!e.IsNull && world.IsAlive(e) && CombatLife.IsAlive(world, e)) used++;
            }
            return Math.Max(0, capacity - used);
        }

        private static int RosterCount(EntityRepository world, Entity self)
            => world.HasComponent<UnitRoster>(self) ? world.GetComponentRO<UnitRoster>(self).Count : 0;

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
    }
}
