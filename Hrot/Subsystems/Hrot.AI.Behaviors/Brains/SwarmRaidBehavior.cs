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
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-6 (IM-5, MV-12; 📄 docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §5 / §9) — <c>SwarmRaid</c>, a COMMANDER's play
    /// over a roster of bikes, in four phases:
    /// <list type="number">
    /// <item><b>Approach</b> — bike i of N rides to its own point on a ring around the objective, on bearing 2π·i/N (N directions);</item>
    /// <item><b>Converge</b> — all ride in to the objective from those bearings (the pillions fire on the approach: their own weapon brains,
    ///   bounded by the firing seat);</item>
    /// <item><b>Fight</b> — they hold at the objective for <c>Fight</c> (stopped: the rider's stopped-only seat fires too, PF-4);</item>
    /// <item><b>Disperse</b> — each rides OUT on its own bearing (divergent routes), then to the rally point; Success when all are there.</item>
    /// </list>
    /// A phase moves on when every live bike has arrived, or after <see cref="PhaseLimitSeconds"/>. Orders go through <see cref="CommandOrders.Assign"/>
    /// (<c>MoveToLocation</c>) — events, never a write to a member. ⚠ Not built (L-15): dismounting to fight, picking up casualties, night.
    /// </summary>
    public static class SwarmRaidBehavior
    {
        public const float DefaultApproachMetres = 300f, DefaultFightSeconds = 60f, DefaultSpeed = 14f;
        public const float ConvergeMetres = 20f, ArrivedMetres = 30f, OutwardFactor = 1.5f, PhaseLimitSeconds = 180f;
        public const byte Approach = 0, Converge = 1, Fight = 2, DisperseOut = 3, DisperseRally = 4;

        [StructLayout(LayoutKind.Sequential)]
        public struct SwarmRaidParams
        {
            public Vector3 Objective;
            public Vector3 Rally;
            public float ApproachMetres;
            public SimDuration Fight;
            public float Speed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SwarmRaidState
        {
            public byte Phase;
            public byte Ordered;
            public SimInstant Since;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SwarmRaidBlackboard
        {
            public SwarmRaidParams Params;
            public SwarmRaidState State;
        }

        [BehaviorResolver(BehaviorNames.SwarmRaid)]
        public static void Resolve(in SwarmRaidParamsJsonDto authored, ref SwarmRaidBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new SwarmRaidParams
            {
                Objective = authored.Objective,
                Rally = authored.Rally,
                ApproachMetres = authored.ApproachMetres > 0f ? authored.ApproachMetres : DefaultApproachMetres,
                Fight = (float)authored.Fight > 0f ? authored.Fight : (SimDuration)DefaultFightSeconds,
                Speed = authored.Speed > 0f ? authored.Speed : DefaultSpeed,
            };
            block.State = default;
        }

        // ⭐ R-3096 (found by ua-moto-swarm-raid) — the raid FULFILS a Dash: a mounted rush into the objective that no contact reaction may halt or
        //   send to cover (OrderConduct: Dash forbids All). ⛔ With no kind its order forbade nothing, and the commander's ReactToContact displaced the
        //   raid at the first sight of the post — the bikes were ordered to Suppress instead of riding to their approach points.
        [Behavior(BehaviorNames.SwarmRaid, Params = typeof(SwarmRaidParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.Dash), Categories = BehaviorCategory.Commander,
            Description = "Bikes converge on an objective from several directions, fight, then disperse on divergent routes to a rally point.")]
        [BTreeDefinition(BehaviorNames.SwarmRaid, Curated = true, Group = "Library/Insurgent", ParamsType = typeof(SwarmRaidBlackboard))]
        public static BTreeBuilder<SwarmRaidBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SwarmRaidBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        /// <summary>Bearing i of n (radians), the direction bike i comes from and leaves by.</summary>
        public static Vector3 Direction(int i, int n)
        {
            float a = n <= 0 ? 0f : 2f * MathF.PI * i / n;
            return new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
        }

        /// <summary>Where bike i of n should be in <paramref name="phase"/>.</summary>
        public static Vector3 PointOf(in SwarmRaidParams p, byte phase, int i, int n) => phase switch
        {
            Approach => p.Objective + Direction(i, n) * p.ApproachMetres,
            Converge or Fight => p.Objective + Direction(i, n) * ConvergeMetres,
            DisperseOut => p.Objective + Direction(i, n) * (p.ApproachMetres * OutwardFactor),
            _ => p.Rally + Direction(i, n) * ConvergeMetres,
        };

        public static NodeStatus Tick(ref SwarmRaidParams p, ref SwarmRaidState s, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self)) return NodeStatus.Failure;
            var roster = world.GetComponentRO<UnitRoster>(self);
            int n = roster.Count;
            if (n == 0) return NodeStatus.Failure;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;
            if (s.Ordered == 0)
            {
                if (s.Phase != Fight)
                    for (int i = 0; i < n; i++)
                    {
                        var member = roster.SubordinateEntities[i];
                        if (!world.IsAlive(member)) continue;
                        CommandOrders.Assign(world, self, member, BehaviorNames.MoveToLocation, MoveJson(PointOf(in p, s.Phase, i, n), p.Speed), newOrder: true);
                    }
                s.Ordered = 1;
                s.Since = (SimInstant)now;
                return NodeStatus.Running;
            }
            double held = now - (double)s.Since;
            bool done = s.Phase == Fight ? held >= (double)p.Fight : AllThere(world, in roster, in p, s.Phase) || held >= PhaseLimitSeconds;
            if (!done) return NodeStatus.Running;
            if (s.Phase == DisperseRally) return NodeStatus.Success;
            s.Phase++;
            s.Ordered = 0;
            return NodeStatus.Running;
        }

        private static bool AllThere(EntityRepository world, in UnitRoster roster, in SwarmRaidParams p, byte phase)
        {
            int n = roster.Count;
            for (int i = 0; i < n; i++)
            {
                var member = roster.SubordinateEntities[i];
                if (!world.IsAlive(member) || !world.HasComponent<SimTransform>(member)) continue;
                var at = world.GetComponentRO<SimTransform>(member).Position;
                var want = PointOf(in p, phase, i, n);
                if (Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(want.X, want.Y)) > ArrivedMetres) return false;
            }
            return true;
        }

        private static string MoveJson(Vector3 to, float speed)
            => string.Create(CultureInfo.InvariantCulture,
                $"{{\"target\":[{to.X:0.##},{to.Y:0.##},{to.Z:0.##}],\"speed\":{speed:0.##},\"arrivalRadius\":10}}");
    }
}
