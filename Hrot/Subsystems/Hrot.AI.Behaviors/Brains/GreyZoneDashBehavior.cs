using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-4621</c> M-4 — the legs of a dash, shared by the commander's play and each bike's ride.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DashLegs
    {
        public const int Max = GreyZoneDashParamsJsonDto.MaxLegs;
        public fixed float X[Max];
        public fixed float Y[Max];
        public fixed float Z[Max];
        public int Count;

        public Vector3 Point(int i) => new(X[i], Y[i], Z[i]);

        public void Set(int i, Vector3 p) { X[i] = p.X; Y[i] = p.Y; Z[i] = p.Z; }

        public static DashLegs From(Fdp.Toolkit.Replication.PointLink[]? legs)
        {
            var d = default(DashLegs);
            int n = legs == null ? 0 : Math.Min(legs.Length, Max);
            for (int i = 0; i < n; i++) d.Set(i, legs![i].Local);
            d.Count = n;
            return d;
        }

        /// <summary>The legs as the JSON a <c>DashRide</c> order carries (invariant culture).</summary>
        public string Json(float speed)
        {
            var sb = new System.Text.StringBuilder("{\"Legs\":[");
            for (int i = 0; i < Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(string.Create(CultureInfo.InvariantCulture, $"[{X[i]:0.##},{Y[i]:0.##},{Z[i]:0.##}]"));
            }
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"],\"Speed\":{speed:0.##}}}"));
            return sb.ToString();
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-4 (MC-3, MC-5, MC-7; 📄 docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §5 / §9) — <c>GreyZoneDash</c>, a COMMANDER's
    /// play over a roster of bikes, fulfilling an order of kind <c>Dash</c> (so THE ONE GATE refuses the unit's halting reactions, CD-3): it
    /// releases bike i onto the route (its own <see cref="DashRideBehavior"/>, also a Dash) once bike i − 1 is <c>Spacing</c> off — one drone,
    /// one bike; a bike hit or a man down changes nothing for the others (MC-5: no halt is ordered, the gate forbids the reactions). Success
    /// once every live bike has ended its ride (at the edge, riders out — MC-6), or the play's limit. Orders go through
    /// <see cref="CommandOrders.Assign"/> — events, never a write to a member.
    /// </summary>
    public static class GreyZoneDashBehavior
    {
        public const float DefaultSpeed = 25f, DefaultSpacing = 80f, LimitSeconds = 900f, GraceSeconds = 2f;

        [StructLayout(LayoutKind.Sequential)]
        public struct GreyZoneDashParams
        {
            public DashLegs Legs;
            public float Speed;
            public float Spacing;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GreyZoneDashState
        {
            /// <summary>How many roster slots were released (0 … n).</summary>
            public byte Released;
            public SimInstant Since;
            public byte Started;
            /// <summary>When the last bike was released (an order lands a tick later: its ride is not judged before <see cref="GraceSeconds"/>).</summary>
            public SimInstant LastRelease;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GreyZoneDashBlackboard
        {
            public GreyZoneDashParams Params;
            public GreyZoneDashState State;
        }

        [BehaviorResolver(BehaviorNames.GreyZoneDash)]
        public static void Resolve(in GreyZoneDashParamsJsonDto authored, ref GreyZoneDashBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new GreyZoneDashParams
            {
                Legs = DashLegs.From(authored.Legs),
                Speed = authored.Speed > 0f ? authored.Speed : DefaultSpeed,
                Spacing = authored.Spacing > 0f ? authored.Spacing : DefaultSpacing,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.GreyZoneDash, Params = typeof(GreyZoneDashParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.Dash),
            Categories = BehaviorCategory.Commander,
            Description = "Bikes cross an exposed zone at full speed, spaced, never stopping; they break across a diving drone and dismount at the edge.")]
        [BTreeDefinition(BehaviorNames.GreyZoneDash, Curated = true, Group = "Library/Insurgent", ParamsType = typeof(GreyZoneDashBlackboard))]
        public static BTreeBuilder<GreyZoneDashBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<GreyZoneDashBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        public static NodeStatus Tick(ref GreyZoneDashParams p, ref GreyZoneDashState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self) || p.Legs.Count == 0) return NodeStatus.Failure;
            var roster = world.GetComponentRO<UnitRoster>(self);
            int n = roster.Count;
            if (n == 0) return NodeStatus.Failure;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            if (s.Started == 0) { s.Started = 1; s.Since = (SimInstant)now; }

            // ⭐ MC-3 — release the next bike once the one ahead is Spacing off (a dead or gone one releases the next at once)
            while (s.Released < n)
            {
                int i = s.Released;
                if (i > 0 && !Clear(world, roster.SubordinateEntities[i - 1], roster.SubordinateEntities[i], p.Spacing)) break;
                var bike = roster.SubordinateEntities[i];
                if (world.IsAlive(bike))
                    CommandOrders.Assign(world, self, bike, BehaviorNames.DashRide, p.Legs.Json(p.Speed), newOrder: true);
                s.Released++;
                s.LastRelease = (SimInstant)now;
            }
            if (s.Released < n) return NodeStatus.Running;
            if (now - (double)s.Since >= LimitSeconds) return NodeStatus.Success;
            if (now - (double)s.LastRelease < GraceSeconds) return NodeStatus.Running;
            // ⭐ MC-6 — done once no live bike still runs its ride (it ends at the edge, riders out)
            for (int i = 0; i < n; i++)
            {
                var bike = roster.SubordinateEntities[i];
                if (!world.IsAlive(bike) || !Fdp.Toolkit.Combat.Injury.IsInTheFight(world, bike)) continue;
                var order = Hrot.AI.Behaviors.Orders.CurrentOrders.Of(world, bike);   // its Dash still runs (no string, no allocation)
                if (order.Exists && order.Kind == Hrot.AI.Behaviors.Orders.OrderKind.Dash) return NodeStatus.Running;
            }
            return NodeStatus.Success;
        }

        /// <summary>The bike ahead is far enough (or gone): the next may go.</summary>
        private static bool Clear(EntityRepository world, Entity ahead, Entity next, float spacing)
        {
            if (!world.IsAlive(ahead) || !world.HasComponent<SimTransform>(ahead) || !world.HasComponent<SimTransform>(next)) return true;
            var a = world.GetComponentRO<SimTransform>(ahead).Position;
            var b = world.GetComponentRO<SimTransform>(next).Position;
            return Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y)) >= spacing;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-4 (MC-3, MC-4, MC-6; 📄 docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §4 / §9) — <c>DashRide</c>, run by the BIKE
    /// under an order of kind <c>Dash</c> (its vehicle SOP's shelter is refused at the gate): the legs at full speed; the LOOKOUT — its riders'
    /// memory (a declared last-tick read) holds a drone diving on it, its time-to-go from two sightings ≤ <see cref="BreakAtSeconds"/> (the FPV's
    /// commit + 0.3 s, MV-8) ⇒ a HARD BREAK: a turn off its own heading, away from the drone for <see cref="BreakSeconds"/>, then the leg again; at the last leg's end
    /// it halts and puts its riders out (<see cref="EjectPassengersCommand"/>) — Success, the order is no longer a Dash.
    /// </summary>
    public static class DashRideBehavior
    {
        public const float DefaultSpeed = 25f, ArrivalRadius = 12f;
        /// <summary>MV-8 — the break starts at this time-to-go (s): the FPV's commit (0.7 s) + 0.3 s.</summary>
        public const float BreakAtSeconds = AirAttackNodes.CommitSeconds + 0.3f;
        public const float BreakSeconds = 2.5f, BreakSideMetres = 40f, BreakAheadMetres = 15f;
        /// <summary>A contact this far over the ground is a flyer the lookout watches (m); farther than <see cref="WatchMetres"/> is ignored.</summary>
        public const float AirborneMetres = 4f, WatchMetres = 250f;

        [StructLayout(LayoutKind.Sequential)]
        public struct DashRideParams
        {
            public DashLegs Legs;
            public float Speed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DashRideState
        {
            public int Leg;
            public byte Issued;
            public byte Done;
            /// <summary>The break holds until this sim time (0 = not breaking).</summary>
            public SimInstant BreakUntil;
            /// <summary>The watched drone (packed id) and its last sighting (xyz, w = sim seconds).</summary>
            public long Drone;
            public Vector4 Seen;
            /// <summary>How many breaks this ride made.</summary>
            public byte Breaks;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DashRideBlackboard
        {
            public DashRideParams Params;
            public DashRideState State;
        }

        [BehaviorResolver(BehaviorNames.DashRide)]
        public static void Resolve(in DashRideParamsJsonDto authored, ref DashRideBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new DashRideParams { Legs = DashLegs.From(authored.Legs), Speed = authored.Speed > 0f ? authored.Speed : DefaultSpeed };
            block.State = default;
        }

        [Behavior(BehaviorNames.DashRide, Params = typeof(DashRideParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.Dash),
            Classes = new[] { "Motorcycle" },
            Description = "A bike's dash: the legs at full speed, a hard break across a diving drone, the riders out at the end.")]
        [BTreeDefinition(BehaviorNames.DashRide, Curated = true, Group = "Library/Insurgent", ParamsType = typeof(DashRideBlackboard))]
        public static BTreeBuilder<DashRideBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<DashRideBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        public static NodeStatus Tick(ref DashRideParams p, ref DashRideState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self) || p.Legs.Count == 0) return NodeStatus.Failure;
            if (s.Done != 0) return NodeStatus.Success;
            var here = world.GetComponentRO<SimTransform>(self).Position;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            var source = LocomotionMoveTo.OrderSource(world, self);

            // ⭐ MC-6 — the edge: halt, riders out, the dash is over
            var end = p.Legs.Point(p.Legs.Count - 1);
            if (s.Leg >= p.Legs.Count - 1 && Flat(here, end) <= ArrivalRadius)
            {
                LocomotionMoveTo.Stop(world, self);
                if (world.Bus.IsRegistered<EjectPassengersCommand>()) world.Bus.Publish(new EjectPassengersCommand { Vehicle = self });
                s.Done = 1;
                return NodeStatus.Success;
            }

            // ⭐ MC-4 — the lookout: a drone diving on us at the break's time-to-go ⇒ break hard across its approach
            if (s.BreakUntil <= 0d && Diving(world, self, here, ref s, now, out var approach))
            {
                var heading = Heading(world, self, here, p.Legs.Point(Math.Min(s.Leg, p.Legs.Count - 1)));
                // a HARD TURN off its own heading (the one acceleration a bike at full speed has), to the side AWAY from the drone — across the
                //   approach of a drone chasing it, the usual case; a little ahead, never back (📐 the rail: "across the approach" taken
                //   literally sent a bike crossed from the side straight on, where it cannot accelerate)
                var side = new Vector2(-heading.Y, heading.X);
                if (Vector2.Dot(side, approach) < 0f) side = -side;   // along its travel = away from where it is
                var to = here + new Vector3(side * BreakSideMetres + heading * BreakAheadMetres, 0f);
                if (!LocomotionMoveTo.Issue(world, self, to, p.Speed, 0f, MoveGait.Keep, source: source)) return NodeStatus.Failure;
                s.BreakUntil = (SimInstant)(now + BreakSeconds);
                s.Issued = 0;
                s.Breaks++;
                return NodeStatus.Running;
            }
            if (s.BreakUntil > 0d)
            {
                if (now < (double)s.BreakUntil) return NodeStatus.Running;
                s.BreakUntil = default;
                s.Drone = 0;
            }

            // the legs at full speed (MC-3: never stop)
            var leg = p.Legs.Point(s.Leg);
            if (s.Leg < p.Legs.Count - 1 && Flat(here, leg) <= ArrivalRadius) { s.Leg++; s.Issued = 0; leg = p.Legs.Point(s.Leg); }
            if (s.Issued == 0)
            {
                if (!LocomotionMoveTo.Issue(world, self, leg, p.Speed, s.Leg == p.Legs.Count - 1 ? ArrivalRadius * 0.5f : 0f, MoveGait.Keep, source: source))
                    return NodeStatus.Failure;
                s.Issued = 1;
            }
            return NodeStatus.Running;
        }

        /// <summary>⭐ MC-4 — its riders' memory (the declared last-tick read) holds an airborne contact closing on the bike with a time-to-go
        /// (range over the closing speed, from two sightings) of <see cref="BreakAtSeconds"/> or less; <paramref name="approach"/> = the drone's
        /// horizontal travel (unit).</summary>
        public static unsafe bool Diving(EntityRepository world, Entity bike, Vector3 here, ref DashRideState s, double now, out Vector2 approach)
        {
            approach = default;
            if (!world.HasComponent<PassengerBuffer>(bike) || !world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.TargetMemory>()) return false;
            ref readonly var crew = ref world.GetComponentRO<PassengerBuffer>(bike);
            float ground = Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(here.X, here.Y) ?? here.Z;
            long best = 0; Vector3 at = default; float bestRange = WatchMetres;
            for (int k = 0; k < PassengerBuffer.Capacity; k++)
            {
                var man = crew.Passengers[k];
                if (man.IsNull || !world.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(man)) continue;
                ref readonly var mem = ref world.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(man);
                for (int i = 0; i < mem.Count; i++)
                {
                    if (Fdp.Toolkit.Perception.Components.TargetMemory.IsAnonymous(in mem, i)) continue;
                    var q = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]);
                    if (q.Z - ground < AirborneMetres) continue;
                    float r = Vector3.Distance(q, here);
                    if (r < bestRange) { bestRange = r; best = mem.EntityIds[i]; at = q; }
                }
            }
            if (best == 0) return false;
            float t = (float)now;
            if (best != s.Drone || at == new Vector3(s.Seen.X, s.Seen.Y, s.Seen.Z))
            {
                if (best != s.Drone) { s.Drone = best; s.Seen = new Vector4(at, t); }
                return false;   // a first sighting, or nothing new: no rate yet
            }
            var before = new Vector3(s.Seen.X, s.Seen.Y, s.Seen.Z);
            float dt = t - s.Seen.W;
            s.Seen = new Vector4(at, t);
            if (dt <= 1e-3f) return false;
            float closing = (Vector3.Distance(before, here) - bestRange) / dt;
            if (closing <= 1f) return false;
            if (bestRange / closing > BreakAtSeconds) return false;
            var travel = new Vector2(at.X - before.X, at.Y - before.Y);
            approach = travel.LengthSquared() > 1e-4f ? Vector2.Normalize(travel) : Vector2.Normalize(new Vector2(here.X - at.X, here.Y - at.Y) + new Vector2(1e-3f, 0f));
            return true;
        }

        private static Vector2 Heading(EntityRepository world, Entity self, Vector3 here, Vector3 leg)
        {
            if (Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, self, out _, out var v) && v.X * v.X + v.Y * v.Y > 1f)
                return Vector2.Normalize(new Vector2(v.X, v.Y));
            var d = new Vector2(leg.X - here.X, leg.Y - here.Y);
            return d.LengthSquared() > 1e-4f ? Vector2.Normalize(d) : Vector2.UnitX;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
    }
}
