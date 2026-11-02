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
            /// <summary>⭐ SR-114 — this tick RESTARTED the objective (a reset was requested): the kind clears its own working state now.</summary>
            public bool Reset;
        }

        /// <summary>⭐ SR-116 — how a status meets the exercise closing: <b>Own</b> = closed by its kind's OnClose rule at its next tick · <b>Composite</b> = closed
        /// only once every child is (<see cref="CloseComposite"/>, so it aggregates their verdicts first) · <b>Hold</b> = not closed this tick (a trigger still
        /// running <c>EndExercise</c> must finish its list).</summary>
        public enum CloseMode { Own, Composite, Hold }

        private sealed class Log { }

        public static double Now(EntityRepository world)
            => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : world.SimulationTime;

        public static Frame Enter(EntityRepository world, Entity self, string kind, ObjectiveRole role, in ObjectiveCommonBlock c, ref ObjectiveRun run,
            CloseMode close = CloseMode.Own, bool capture = false)
        {
            if (!world.HasComponent<ObjectiveStatus>(self)) world.AddComponent(self, new ObjectiveStatus());
            if (!world.HasComponent<ObjectiveLinks>(self)) world.AddComponent(self, new ObjectiveLinks());
            double now = Now(world);
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            ref var l = ref world.GetComponentRW<ObjectiveLinks>(self);
            double dt = 0;
            byte then = 0;
            if (s.ResetRequested != 0)
            {
                // ⭐ SR-114 — a reset: the run begins again (the ONE place), the progress is cleared, the kind clears its working state (Frame.Reset).
                then = s.ResetRequested; s.ResetRequested = 0;
                if (s.Closed == 0)
                {
                    run.Began = 0; run.LastNow = 0;
                    s.ProgressCur = 0; s.Value = 0f; s.Score = 0f; s.Reason = default;
                }
                else then = 0;
            }
            if (run.Began == 0)
            {
                ObjectiveRules.Begin(ref s, ref l, in c, kind, role, now);
                ObjectiveRules.Timing(ref s, in c);
                if (then != 0) s.State = then == 1 ? ObjectiveState.Active : ObjectiveState.Inactive;
                run.Began = 1;
                Fdp.Core.Logging.FdpLog<Log>.Info($"objective '{s.Title}' {kind}: begins {s.State}");
            }
            else
            {
                ObjectiveRules.CopyLinks(ref l, in c);
                ObjectiveRules.Display(ref s, in c);
                ObjectiveRules.Timing(ref s, in c);
                dt = now > run.LastNow ? now - run.LastNow : 0;
            }
            run.LastNow = now;
            // ⭐ SR-116 — the exercise is closing (or ended): this status takes its kind's OnClose rule, once, and nothing changes it afterwards.
            if (close == CloseMode.Own && s.Closed == 0 && ExerciseResult.PhaseOf(world) != ExercisePhase.Running)
                ObjectiveRules.OnClose(ref s, ObjectiveRules.CloseRuleOf(kind, capture), now);
            return new Frame { Now = now, Dt = dt, Reset = then != 0, Evaluate = s.Closed == 0 && s.State == ObjectiveState.Active };
        }

        /// <summary>⭐ SR-116 — a composite's close: once the exercise is closing and EVERY child is closed (its own verdict settled first), it is closed too.</summary>
        public static void CloseComposite(EntityRepository world, Entity self, ref ObjectiveStatus s, double now)
        {
            if (s.Closed != 0 || ExerciseResult.PhaseOf(world) == ExercisePhase.Running) return;
            var t = ObjectiveChildren.Of(world, self);
            if (t.Closed < t.Count) return;
            ObjectiveRules.OnClose(ref s, CloseRule.Ordinary, now);
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
