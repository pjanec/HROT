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
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4650</c> NA-4 (docs/designs/weapons/DESIGN_Bows_And_Crossbows.md §9) — <c>CircleRaid</c>, the mounted war party's play: a COMMANDER over a roster of MOUNTS (a horse, its rider in the saddle — the firing seat, R-4269), mirroring
    /// <see cref="SwarmRaidBehavior"/> (one group play; orders are events through <c>CommandOrders</c>, never a write to a member — the per-entity parallel rule):
    /// <list type="number">
    /// <item><b>Approach</b> — mount i of N rides to its own point on its circle (bearing 2π·i/N, radius 40…80 m by its index);</item>
    /// <item><b>Circle</b> — every <see cref="LegSeconds"/> each mount is sent to the point it will reach next on its circle, counter-clockwise at the gallop (the chord of a short arc), so the group rings the objective at 40–80 m; the riders are
    ///   ordered to shoot the <c>Target</c> ONCE and shoot on the move (the bow fires on the move, BW-8); a rider whose health fell since last seen BREAKS OFF to the rally point for <c>Breakoff</c>, then rejoins his circle;</item>
    /// <item><b>Disperse</b> — every mount rides to the rally point; Success when all are there (or after the limit).</item>
    /// </list>
    /// ⚠ Not built: dismounting to fight, picking up a casualty.
    /// </summary>
    public static class CircleRaidBehavior
    {
        public const float DefaultRadiusMin = 40f, DefaultRadiusMax = 80f, DefaultSpeed = 12f, DefaultCircleSeconds = 40f, DefaultBreakoffSeconds = 12f;
        public const float LegSeconds = 2.5f, ArrivedMetres = 25f, PhaseLimitSeconds = 120f;
        public const int MaxMounts = 8;
        public const byte Approach = 0, Circle = 1, Disperse = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct CircleRaidParams
        {
            public Vector3 Objective, Rally;
            public EntityRef Target;
            public float RadiusMin, RadiusMax, Speed;
            public SimDuration Circle, Breakoff;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct CircleRaidState
        {
            public byte Phase, Ordered, Shooting, Count;
            public SimInstant Since, NextLeg;
            /// <summary>Per mount: the rider's health when last seen, and the sim second a break-off ends (0 = on the circle).</summary>
            public fixed float Hp[MaxMounts];
            public fixed float BreakUntil[MaxMounts];
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CircleRaidBlackboard
        {
            public CircleRaidParams Params;
            public CircleRaidState State;
        }

        [BehaviorResolver(BehaviorNames.CircleRaid)]
        public static void Resolve(in CircleRaidParamsJsonDto authored, ref CircleRaidBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new CircleRaidParams
            {
                Objective = authored.Objective, Rally = authored.Rally, Target = authored.Target.Ref,
                RadiusMin = authored.RadiusMin > 0f ? authored.RadiusMin : DefaultRadiusMin,
                RadiusMax = authored.RadiusMax > 0f ? authored.RadiusMax : DefaultRadiusMax,
                Speed = authored.Speed > 0f ? authored.Speed : DefaultSpeed,
                Circle = (float)authored.Circle > 0f ? authored.Circle : (SimDuration)DefaultCircleSeconds,
                Breakoff = (float)authored.Breakoff > 0f ? authored.Breakoff : (SimDuration)DefaultBreakoffSeconds,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.CircleRaid, Params = typeof(CircleRaidParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.Dash), Categories = BehaviorCategory.Commander,
            Description = "A mounted group circles its objective at a gallop at 40-80 m, each rider shooting inward while riding; a hit rider breaks off and rejoins.")]
        [BTreeDefinition(BehaviorNames.CircleRaid, Curated = true, Group = "Library/Insurgent", ParamsType = typeof(CircleRaidBlackboard))]
        public static BTreeBuilder<CircleRaidBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<CircleRaidBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        /// <summary>Mount i's circle radius: the near, middle or far ring by its index (so the group is a spread of rings, not one).</summary>
        public static float RadiusOf(in CircleRaidParams p, int i) => p.RadiusMin + (p.RadiusMax - p.RadiusMin) * ((i % 3) * 0.5f);

        /// <summary>The bearing (rad) mount i of n holds at <paramref name="t"/> seconds into the circle: its start 2π·i/n, advancing counter-clockwise at speed ÷ radius.</summary>
        public static float BearingOf(in CircleRaidParams p, int i, int n, float t) => 2f * MathF.PI * i / MathF.Max(1, n) + t * p.Speed / RadiusOf(in p, i);

        /// <summary>The point on mount i's circle at bearing <paramref name="bearing"/>.</summary>
        public static Vector3 RingPoint(in CircleRaidParams p, int i, float bearing)
            => p.Objective + new Vector3(MathF.Cos(bearing), MathF.Sin(bearing), 0f) * RadiusOf(in p, i);

        public static unsafe NodeStatus Tick(ref CircleRaidParams p, ref CircleRaidState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self)) return NodeStatus.Failure;
            var roster = world.GetComponentRO<UnitRoster>(self);
            int n = Math.Min(roster.Count, MaxMounts);
            if (n == 0) return NodeStatus.Failure;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            if (s.Ordered != 0 && s.Phase == Approach && n != s.Count) s.Ordered = 0;   // the roster is still filling as the mounts register: order the late ones too
            if (s.Ordered == 0)
            {
                for (int i = 0; i < n; i++)
                {
                    var mount = roster.SubordinateEntities[i];
                    if (!world.IsAlive(mount)) continue;
                    var to = s.Phase == Approach ? RingPoint(in p, i, BearingOf(in p, i, n, 0f)) : p.Rally;
                    CommandOrders.Assign(world, self, mount, BehaviorNames.MoveToLocation, MoveJson(to, p.Speed), newOrder: true);
                }
                s.Ordered = 1; s.Count = (byte)n; s.Since = (SimInstant)now; s.NextLeg = (SimInstant)(now + LegSeconds);
                return NodeStatus.Running;
            }
            double held = now - (double)s.Since;
            if (s.Phase == Circle)
            {
                if (s.Shooting == 0) { ShootOrders(world, self, in roster, n, in p); s.Shooting = 1; for (int i = 0; i < n; i++) s.Hp[i] = RiderHealth(world, roster.SubordinateEntities[i]); }
                if (now >= (double)s.NextLeg)
                {
                    s.NextLeg = (SimInstant)(now + LegSeconds);
                    float t = (float)held;
                    for (int i = 0; i < n; i++)
                    {
                        var mount = roster.SubordinateEntities[i];
                        if (!world.IsAlive(mount)) continue;
                        float hp = RiderHealth(world, mount);
                        if (hp >= 0f && hp < s.Hp[i] - 0.5f) { s.BreakUntil[i] = (float)(now + (double)p.Breakoff); }   // hit: he breaks off
                        if (hp >= 0f) s.Hp[i] = hp;
                        if (s.BreakUntil[i] > now)
                            CommandOrders.Assign(world, self, mount, BehaviorNames.MoveToLocation, MoveJson(p.Rally, p.Speed), newOrder: true);
                        else
                            CommandOrders.Assign(world, self, mount, BehaviorNames.MoveToLocation, MoveJson(RingPoint(in p, i, BearingOf(in p, i, n, t + LegSeconds)), p.Speed), newOrder: true);
                    }
                }
                if (held < (double)p.Circle) return NodeStatus.Running;
            }
            else
            {
                bool done = AllThere(world, in roster, n, in p, s.Phase) || held >= PhaseLimitSeconds;
                if (!done) return NodeStatus.Running;
                if (s.Phase == Disperse) return NodeStatus.Success;
            }
            s.Phase++;
            s.Ordered = 0;
            return NodeStatus.Running;
        }

        /// <summary>The riders are told to shoot the target ONCE (a FireAtTarget order); with none authored their own weapon brains decide.</summary>
        private static void ShootOrders(EntityRepository world, Entity commander, in UnitRoster roster, int n, in CircleRaidParams p)
        {
            int id = p.Target.IsNone ? 0 : p.Target.NetworkId;
            if (id == 0) return;
            string json = string.Create(CultureInfo.InvariantCulture, $"{{\"targetNetworkId\":{id}}}");
            for (int i = 0; i < n; i++)
            {
                var rider = RiderOf(world, roster.SubordinateEntities[i]);
                if (!rider.IsNull && world.IsAlive(rider)) CommandOrders.Assign(world, commander, rider, BehaviorNames.FireAtTarget, json, newOrder: true);
            }
        }

        private static Entity RiderOf(EntityRepository world, Entity mount)
        {
            if (!world.HasComponent<PassengerBuffer>(mount)) return Entity.Null;
            ref readonly var b = ref world.GetComponentRO<PassengerBuffer>(mount);
            return b.Count > 0 ? b.Passengers[0] : Entity.Null;
        }

        /// <summary>The rider's health (−1 when there is no rider or no health).</summary>
        private static float RiderHealth(EntityRepository world, Entity mount)
        {
            var r = RiderOf(world, mount);
            return !r.IsNull && world.IsAlive(r) && world.HasComponent<Health>(r) ? world.GetComponentRO<Health>(r).Current : -1f;
        }

        private static bool AllThere(EntityRepository world, in UnitRoster roster, int n, in CircleRaidParams p, byte phase)
        {
            for (int i = 0; i < n; i++)
            {
                var mount = roster.SubordinateEntities[i];
                if (!world.IsAlive(mount) || !world.HasComponent<SimTransform>(mount)) continue;
                var at = world.GetComponentRO<SimTransform>(mount).Position;
                var want = phase == Approach ? RingPoint(in p, i, BearingOf(in p, i, n, 0f)) : p.Rally;
                if (Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(want.X, want.Y)) > ArrivedMetres * 2f) return false;
            }
            return true;
        }

        private static string MoveJson(Vector3 to, float speed)
            => string.Create(CultureInfo.InvariantCulture, $"{{\"target\":[{to.X:0.##},{to.Y:0.##},{to.Z:0.##}],\"speed\":{speed:0.##},\"arrivalRadius\":10}}");
    }
}
