using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules S1 — what every kind's tick shares and NO kind decides: the status components, the first tick (<see cref="ObjectiveRules.Begin"/>),
    /// the links re-copied each tick, the sim clock and its step, and the one log line per state change.
    /// </summary>
    public static class ObjectiveHost
    {
        public struct Frame
        {
            /// <summary>Sim seconds since the start.</summary>
            public double Now;
            /// <summary>Seconds since this objective's last tick (0 on the first).</summary>
            public double Dt;
            /// <summary>Evaluate the kind now: Active and not closed (Inactive = frozen, final = only the links are kept).</summary>
            public bool Evaluate;
        }

        private sealed class Log { }

        public static double Now(EntityRepository world)
            => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;

        public static Frame Enter(EntityRepository world, Entity self, string kind, ObjectiveRole role, in ObjectiveCommonBlock c, ref ObjectiveRun run)
        {
            if (!world.HasComponent<ObjectiveStatus>(self)) world.AddComponent(self, new ObjectiveStatus());
            if (!world.HasComponent<ObjectiveLinks>(self)) world.AddComponent(self, new ObjectiveLinks());
            double now = Now(world);
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            ref var l = ref world.GetComponentRW<ObjectiveLinks>(self);
            double dt = 0;
            if (run.Began == 0)
            {
                ObjectiveRules.Begin(ref s, ref l, in c, kind, role, now);
                run.Began = 1;
                Fdp.Core.Logging.FdpLog<Log>.Info($"objective '{s.Title}' {kind}: begins {s.State}");
            }
            else
            {
                ObjectiveRules.CopyLinks(ref l, in c);
                ObjectiveRules.Display(ref s, in c);
                dt = now > run.LastNow ? now - run.LastNow : 0;
            }
            run.LastNow = now;
            return new Frame { Now = now, Dt = dt, Evaluate = s.Closed == 0 && s.State == ObjectiveState.Active };
        }

        /// <summary>The rule's <see cref="ObjectiveRules.Settle"/> plus the log line when the state changed.</summary>
        public static bool Settle(ref ObjectiveStatus s, string kind, ObjectiveState next, string reason, double now)
        {
            var old = s.State;
            bool changed = ObjectiveRules.Settle(ref s, next, reason, now);
            if (changed) Fdp.Core.Logging.FdpLog<Log>.Info($"objective '{s.Title}' {kind}: {old} → {next} ({reason})");
            return changed;
        }
    }
}
