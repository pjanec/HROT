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
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Squad.Command;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;
using Hrot.Map.Common.Components;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2284</c> (UM-1…UM-7) — the movement's plan: the route as points (the lead's start first, then each leg's end), the leg the
    /// lead heads for, and per roster slot the point it was last ordered to. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §11b.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct UrbanPlan
    {
        public const int Capacity = CommandPicture.MaxSubordinates;
        public const int MaxPoints = UrbanMovementParamsJsonDto.MaxLegs + 1;

        public fixed float X[MaxPoints];
        public fixed float Y[MaxPoints];
        public fixed float Z[MaxPoints];
        /// <summary>The points held: before the plan the authored legs only; after it the start first.</summary>
        public int Count;
        public long RouteNet;
        public byte Made;
        /// <summary>The point the lead heads for (1 … <see cref="Count"/> − 1); <see cref="Count"/> = every leg done.</summary>
        public int Leg;
        public float Speed;
        public float Spacing;
        public float ArrivalRadius;
        /// <summary>Per slot: the point last ordered (its march goes there while it is within <see cref="UrbanMovementBehavior.ReorderMetres"/>
        /// of the point it should have now).</summary>
        public fixed float GivenX[Capacity];
        public fixed float GivenY[Capacity];
        public fixed float GivenZ[Capacity];
        public fixed byte HasGiven[Capacity];
        /// <summary>Per slot: 1 = it was ordered as the lead (with the crossing drill).</summary>
        public fixed byte GivenLead[Capacity];

        public Vector3 Point(int i) => new(X[i], Y[i], Z[i]);

        public void Set(int i, Vector3 p) { X[i] = p.X; Y[i] = p.Y; Z[i] = p.Z; }
    }

    /// <summary>⭐ <c>CE-2284</c> — what <see cref="UrbanMovementBehavior.KeepMoving"/> keeps: how many orders it gave.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UrbanKeep
    {
        public int Orders;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2284</c> slice U-1 (DA-17, UM-1…UM-7; 📄 Defend and Attack §11b) — <c>UrbanMovement</c>, a platoon moving through town on foot:
    /// the first live squad leads, marching leg by leg with its crossing drill (<c>SquadMarch</c> <c>DangerAreas</c>); each next live squad
    /// marches to the route point <see cref="UrbanPlan.Spacing"/> × k behind where the lead stands on the route (travelling overwatch, the
    /// reserve last). One activity (<see cref="KeepMoving"/>) gives every order; a squad in its own reaction is left to it; after a platoon
    /// reaction (the SOP's hold) the task resumes at its leg and orders every squad again from where it stands. Done when the last leg is
    /// walked and every squad is at its point.
    /// </summary>
    public static class UrbanMovementBehavior
    {
        public const float DefaultSpeed = 1.5f, DefaultSpacing = 30f, DefaultArrivalRadius = 8f;
        /// <summary>UM-4 — a follower's point is ordered again once the point it should have moved this far (m).</summary>
        public const float ReorderMetres = 10f;

        [StructLayout(LayoutKind.Sequential)]
        public struct UrbanMovementBlackboard
        {
            public UrbanPlan Plan;
            public UrbanKeep Keep;
        }

        [BehaviorResolver(BehaviorNames.UrbanMovement)]
        public static unsafe void Resolve(in UrbanMovementParamsJsonDto authored, ref UrbanMovementBlackboard block, EntityRepository world, Entity self)
        {
            block.Plan = default;
            block.Keep = default;
            block.Plan.RouteNet = authored.Route.NetworkId;
            block.Plan.Speed = authored.Speed > 0f ? authored.Speed : DefaultSpeed;
            block.Plan.Spacing = authored.Spacing > 0f ? authored.Spacing : DefaultSpacing;
            block.Plan.ArrivalRadius = authored.ArrivalRadius > 0f ? authored.ArrivalRadius : DefaultArrivalRadius;
            var legs = authored.Legs;
            int n = legs == null ? 0 : Math.Min(legs.Length, UrbanMovementParamsJsonDto.MaxLegs);
            for (int i = 0; i < n; i++) block.Plan.Set(i, legs![i]);
            block.Plan.Count = n;
        }

        [Behavior(BehaviorNames.UrbanMovement, Params = typeof(UrbanMovementParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [HsmDefinition(BehaviorNames.UrbanMovement, Curated = true, Group = "Library/Platoon", ParamsType = typeof(UrbanMovementBlackboard))]
        public static StateMachineGraph BuildUrbanMovement()
        {
            var builder = new HsmBuilder("UrbanMovement_HSM");
            var plan = builder.State("UPlan").Initial()
                .Activity<UrbanMovementBlackboard, UrbanPlan, UrbanKeep>(bb => bb.Plan, bb => bb.Keep, PlanRoute);
            var move = builder.State("UMove")
                .Activity<UrbanMovementBlackboard, UrbanPlan, UrbanKeep>(bb => bb.Plan, bb => bb.Keep, KeepMoving);
            builder.State("UDone").Final();
            plan.On(0).GoTo("UMove").Guard<UrbanMovementBlackboard, UrbanPlan>(bb => bb.Plan, Planned).Polled();
            move.On(0).GoTo("UDone").Guard<UrbanMovementBlackboard, UrbanPlan>(bb => bb.Plan, Arrived).Polled();
            return builder.Build();
        }

        private static readonly HrotRouteSource s_routes = new();

        /// <summary>UM-1 — UPlan's activity: the authored legs (else the route entity's waypoints, once it is known), with the lead's
        /// position first.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus PlanRoute(ref UrbanPlan p, ref UrbanKeep ws, Entity self, EntityRepository world)
        {
            if (p.Made != 0) return NodeStatus.Running;
            int lead = Lead(world, self);
            if (lead < 0) return NodeStatus.Running;
            if (p.Count == 0)
            {
                if (p.RouteNet == 0) return NodeStatus.Failure;   // neither legs nor a route: nothing to walk
                var points = new List<NavRouteWaypoint>();
                if (!s_routes.TryGetRoute(world, p.RouteNet, points, out _, out _) || points.Count == 0) return NodeStatus.Running;
                int n = Math.Min(points.Count, UrbanMovementParamsJsonDto.MaxLegs);
                for (int i = 0; i < n; i++) p.Set(i, points[i].Position);
                p.Count = n;
            }
            for (int i = p.Count; i > 0; i--) p.Set(i, p.Point(i - 1));   // the lead's start first
            p.Set(0, StaffComposerSystem.CentreOf(world, CommandPictureOps.Subordinate(world, self, lead)));
            p.Count++;
            p.Leg = 1;
            p.Made = 1;
            return NodeStatus.Running;
        }

        /// <summary>
        /// The ONE order writer (UM-2…UM-7): the lead marches to its leg's end with its crossing drill and takes the next leg within
        /// <see cref="UrbanPlan.ArrivalRadius"/> of it; each next live squad marches to the route point <see cref="UrbanPlan.Spacing"/> × k
        /// behind where the lead stands on the route. An order is given when it is not this run's last one to the squad — so a march that
        /// finished is not restarted, and one a platoon reaction replaced is given again when the task resumes. ⛔ A squad in its own
        /// reaction is left to it.
        /// </summary>
        [SharedAiAction]
        public static unsafe NodeStatus KeepMoving(ref UrbanPlan p, ref UrbanKeep ws, Entity self, EntityRepository world)
        {
            if (p.Made == 0) return NodeStatus.Running;
            int lead = Lead(world, self);
            if (lead < 0) return NodeStatus.Running;
            var leadSquad = CommandPictureOps.Subordinate(world, self, lead);
            var leadAt = StaffComposerSystem.CentreOf(world, leadSquad);
            while (p.Leg < p.Count && Flat(leadAt, p.Point(p.Leg)) <= p.ArrivalRadius) p.Leg++;   // UM-5 — the next leg
            float along = Along(ref p, leadAt);
            int n = Math.Min(RosterCount(world, self), UrbanPlan.Capacity);
            int rank = 0;
            for (int s = 0; s < n; s++)
            {
                var squad = CommandPictureOps.Subordinate(world, self, s);
                if (PlatoonConvoyBehavior.LiveMen(world, squad, 0) == 0) continue;
                bool isLead = s == lead;
                if (!isLead) rank++;
                if (PlatoonHoldBehavior.Fighting(world, squad)) continue;   // UM-6 — its own reaction runs; ordered when it ends
                var want = isLead ? p.Point(Math.Min(p.Leg, p.Count - 1)) : PointAt(ref p, along - p.Spacing * rank);
                // UM-4 — keep the point last given while the wanted one is within ReorderMetres of it (no restart every tick)
                if (p.HasGiven[s] != 0 && p.GivenLead[s] == (isLead ? 1 : 0)
                    && Flat(new Vector3(p.GivenX[s], p.GivenY[s], p.GivenZ[s]), want) < (isLead ? 0.01f : ReorderMetres))
                    want = new Vector3(p.GivenX[s], p.GivenY[s], p.GivenZ[s]);
                var order = MarchOrder(ref p, want, isLead);
                if (CommandOrders.Gave(world, self, squad, BehaviorNames.SquadMarch, order)) continue;
                CommandOrders.Assign(world, self, squad, BehaviorNames.SquadMarch, order, newOrder: true);
                p.GivenX[s] = want.X; p.GivenY[s] = want.Y; p.GivenZ[s] = want.Z;
                p.HasGiven[s] = 1;
                p.GivenLead[s] = (byte)(isLead ? 1 : 0);
                ws.Orders++;
            }
            return NodeStatus.Running;
        }

        /// <summary>The march a squad is ordered: in file to <paramref name="to"/>; the lead with its crossing drill (DA-17).</summary>
        public static string MarchOrder(ref UrbanPlan p, Vector3 to, bool lead)
            => JsonSerializer.Serialize(new SquadMarchParamsJsonDto
            {
                Objective = to, Shape = 0, Speed = p.Speed, ArrivalRadius = lead ? p.ArrivalRadius * 0.5f : p.ArrivalRadius,
                DangerAreas = lead,
            }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

        // ── the guards ───────────────────────────────────────────────────────────────────────────────────────────────

        [SharedAiCondition]
        public static bool Planned(ref UrbanPlan p, Entity self, EntityRepository world) => p.Made != 0;

        /// <summary>UM-5 — every leg walked and every live squad within 2 × <see cref="UrbanPlan.ArrivalRadius"/> of the point it has now.</summary>
        [SharedAiCondition]
        public static unsafe bool Arrived(ref UrbanPlan p, Entity self, EntityRepository world)
        {
            if (p.Made == 0 || p.Leg < p.Count) return false;
            int lead = Lead(world, self);
            if (lead < 0) return false;
            float along = Along(ref p, StaffComposerSystem.CentreOf(world, CommandPictureOps.Subordinate(world, self, lead)));
            int n = Math.Min(RosterCount(world, self), UrbanPlan.Capacity);
            int rank = 0;
            for (int s = 0; s < n; s++)
            {
                var squad = CommandPictureOps.Subordinate(world, self, s);
                if (PlatoonConvoyBehavior.LiveMen(world, squad, 0) == 0) continue;
                if (s != lead) rank++;
                var at = s == lead ? p.Point(p.Count - 1) : PointAt(ref p, along - p.Spacing * rank);
                if (Flat(StaffComposerSystem.CentreOf(world, squad), at) > 2f * p.ArrivalRadius) return false;
            }
            return true;
        }

        // ── the route (pure) ────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>How far along the route <paramref name="at"/> stands: its nearest point on the segments up to the lead's leg (m).</summary>
        public static float Along(ref UrbanPlan p, Vector3 at)
        {
            int last = Math.Min(p.Leg, p.Count - 1);
            float best = float.MaxValue, bestAlong = 0f, walked = 0f;
            var q = new Vector2(at.X, at.Y);
            for (int i = 1; i <= last; i++)
            {
                var a = new Vector2(p.Point(i - 1).X, p.Point(i - 1).Y);
                var b = new Vector2(p.Point(i).X, p.Point(i).Y);
                float len = Vector2.Distance(a, b);
                float t = len < 1e-3f ? 0f : Math.Clamp(Vector2.Dot(q - a, b - a) / (len * len), 0f, 1f);
                float d = Vector2.Distance(q, a + (b - a) * t);
                if (d < best) { best = d; bestAlong = walked + t * len; }
                walked += len;
            }
            return bestAlong;
        }

        /// <summary>The route point <paramref name="along"/> metres from its start (clamped to the route).</summary>
        public static Vector3 PointAt(ref UrbanPlan p, float along)
        {
            if (along <= 0f || p.Count < 2) return p.Point(0);
            float walked = 0f;
            for (int i = 1; i < p.Count; i++)
            {
                var a = p.Point(i - 1);
                var b = p.Point(i);
                float len = Flat(a, b);
                if (walked + len >= along) return Vector3.Lerp(a, b, len < 1e-3f ? 0f : (along - walked) / len);
                walked += len;
            }
            return p.Point(p.Count - 1);
        }

        // ── the reads ────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>UM-2 — the lead: the first roster slot whose squad has a live man (−1 = none).</summary>
        public static int Lead(EntityRepository world, Entity self)
        {
            int n = Math.Min(RosterCount(world, self), UrbanPlan.Capacity);
            for (int s = 0; s < n; s++)
                if (PlatoonConvoyBehavior.LiveMen(world, CommandPictureOps.Subordinate(world, self, s), 0) > 0) return s;
            return -1;
        }

        private static int RosterCount(EntityRepository world, Entity self)
            => world.HasComponent<UnitRoster>(self) ? world.GetComponentRO<UnitRoster>(self).Count : 0;

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
    }
}
