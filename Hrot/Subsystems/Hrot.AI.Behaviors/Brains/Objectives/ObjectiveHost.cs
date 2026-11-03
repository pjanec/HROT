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
            /// <summary>⭐ V-4 — a <see cref="RuleRequest"/> the trigger itself carries out this tick (<c>Fire</c> / <c>Rearm</c>); the rest the host already applied.</summary>
            public RuleRequest Control;
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
            if (world.IsComponentTypeRegistered<RuleControl>() && !world.HasComponent<RuleControl>(self))
            {
                // ⭐ V-4 — the OWNER holds the control component and its authority bit, so the attribute path's gate (CanWrite) lets a request in here and nowhere else
                world.AddComponent(self, new RuleControl());
                world.SetAuthority(self, ComponentTypeRegistry.GetOrRegisterManaged(typeof(RuleControl)), true);
            }
            if (world.TryGetTable(typeof(RuleEdit), out _) && !world.HasManagedComponent<RuleEdit>(self))
            {
                // ⭐ rules ② — the same for a live edit's request record (managed, immutable): the owner holds it and its authority bit; the node's RuleEditSystem applies it
                world.SetManagedComponent(self, new RuleEdit());
                world.SetAuthority(self, ManagedComponentType<RuleEdit>.ID, true);
            }
            double now = Now(world);
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            var control = HonourControl(world, self, ref s, role, now);
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
                ObjectiveRules.Display(ref s, in c, kind, role);
                ObjectiveRules.Timing(ref s, in c);
                dt = now > run.LastNow ? now - run.LastNow : 0;
            }
            run.LastNow = now;
            // ⭐ SR-116 — the exercise is closing (or ended): this status takes its kind's OnClose rule, once, and nothing changes it afterwards.
            if (close == CloseMode.Own && s.Closed == 0 && ExerciseResult.PhaseOf(world) != ExercisePhase.Running)
                ObjectiveRules.OnClose(ref s, ObjectiveRules.CloseRuleOf(kind, capture), now);
            return new Frame { Now = now, Dt = dt, Reset = then != 0, Evaluate = s.Closed == 0 && s.State == ObjectiveState.Active, Control = control };
        }

        /// <summary>
        /// ⭐ rules V-4 (§00.10) — honours a <see cref="RuleControl"/> request ONCE per new <c>Seq</c> (<c>Done</c> remembers the last), journals it on the entity
        /// ("&lt;request&gt; by &lt;origin&gt;"). Activate / Deactivate / Disarm / Reset go through <see cref="ObjectiveRules.SetActive"/> — the ONE SR-114 implementation
        /// (<c>SetObjectiveActive</c> calls it too); <c>Fire</c> / <c>Rearm</c> are a trigger's and come back for it to carry out.
        /// </summary>
        private static RuleRequest HonourControl(EntityRepository world, Entity self, ref ObjectiveStatus s, ObjectiveRole role, double now)
        {
            if (!world.IsComponentTypeRegistered<RuleControl>() || !world.HasComponent<RuleControl>(self)) return RuleRequest.NoRequest;
            ref var rc = ref world.GetComponentRW<RuleControl>(self);
            if (rc.Seq == rc.Done) return RuleRequest.NoRequest;
            var request = (RuleRequest)rc.Request;
            int seq = rc.Seq; byte origin = rc.Origin;
            rc.Done = seq;
            string? refused = null;
            var forTrigger = RuleRequest.NoRequest;
            switch (request)
            {
                case RuleRequest.Activate: refused = ObjectiveRules.SetActive(ref s, true, false, now); break;
                case RuleRequest.Deactivate:
                case RuleRequest.Disarm: refused = ObjectiveRules.SetActive(ref s, false, false, now); break;
                case RuleRequest.Reset: refused = ObjectiveRules.SetActive(ref s, true, true, now); break;
                case RuleRequest.Fire:
                case RuleRequest.Rearm:
                    if (role == ObjectiveRole.Trigger) forTrigger = request; else refused = $"{request} is a trigger's request";
                    break;
                default: refused = "no such request"; break;
            }
            if (refused != null) s.Reason = new FixedString64($"{request} refused: {refused}");
            // ⭐ CE-2380 part 2 — the final verdict of a request that came with an id (Fire / Rearm are carried out by the trigger, which reports them: TriggerBehavior.Control)
            if (forTrigger == RuleRequest.NoRequest) ReportControl(world, self, refused == null ? null : $"{request} refused: {refused}");
            Fdp.Core.Logging.FdpLog<Log>.Info($"rule '{s.Title}': {request} by {(RuleOrigin)origin}" + (refused != null ? $" — refused: {refused}" : ""));
            Fdp.Toolkit.Behavior.Diagnostics.BrainJournalOf.Write(world, self, Fdp.Toolkit.Behavior.Diagnostics.BrainJournalKind.RuleControl,
                a: (int)request, b: seq, origin: origin, cause: (byte)(refused != null ? 1 : 0));
            return forTrigger;
        }

        /// <summary>⭐ CE-2380 part 2 — reports the verdict of the pending <see cref="RuleControl"/> request of <paramref name="self"/> (its id from <see cref="RuleRequestIds"/>): Applied when <paramref name="refusedWhy"/> is null, else Refused with the text.</summary>
        public static void ReportControl(EntityRepository world, Entity self, string? refusedWhy)
        {
            var id = RuleRequestIds.Take(world, self);
            if (id == Guid.Empty) return;
            int node = Fdp.Toolkit.Replication.Edits.EditOutcomes.LocalNodeOf(world, self);
            Fdp.Toolkit.Replication.Edits.EditOutcomes.Report(world, id, Fdp.Toolkit.Replication.Services.NetworkIdResolver.RuntimeNetworkIdOf(world, self),
                refusedWhy == null ? Fdp.Toolkit.Replication.Edits.EditStatus.Applied : Fdp.Toolkit.Replication.Edits.EditStatus.Refused,
                refusedWhy ?? "applied", node >= 0 ? node : null);
        }

        /// <summary>⭐ SR-116 — a composite's close: once the exercise is closing and EVERY child is closed (its own verdict settled first), it is closed too.</summary>
        public static void CloseComposite(EntityRepository world, Entity self, ref ObjectiveStatus s, double now, Func<Entity, bool>? member = null)
        {
            if (s.Closed != 0 || ExerciseResult.PhaseOf(world) == ExercisePhase.Running) return;
            var t = ObjectiveChildren.Of(world, self, member);
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
