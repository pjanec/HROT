using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Fdp.Toolkit.Weather;

namespace Hrot.AI.Behaviors.Brains.Objectives.Verbs
{
    /// <summary>A verb with no working state of its own.</summary>
    public struct VerbNoState { public int Pad; }

    /// <summary>⭐ <c>SetObjectiveActive</c>'s params: the target objective, switched on / off, optionally restarted (SR-114).</summary>
    public struct SetObjectiveActiveParams
    {
        public EntityLink Target;
        public bool Active;
        public bool Reset;
        public SetObjectiveActiveParams() { Target = default; Active = true; Reset = false; }
    }

    /// <summary>⭐ S6 G-3 — <c>AddToCounter</c>'s params: the target <c>Counter</c> measure and what to add (negative drains).</summary>
    public struct AddToCounterParams { public EntityLink Target; public float Amount; }

    /// <summary>⭐ <c>EndExercise</c>'s params and working state.</summary>
    public struct EndExerciseParams : IActionArgsCheck
    {
        public FixedString64 Reason;
        /// <summary>⭐ ui ⑥ — optional: <c>Friend</c> · <c>Hostile</c> · <c>Draw</c> (case-insensitive) NAMES the winner (the scenario's own rule — tickets, the lead, domination); empty = the SR-122 rule over the root objectives.</summary>
        public FixedString32 Winner;

        public static readonly string[] Winners = { "Friend", "Hostile", "Draw" };

        public readonly string? Check(JsonObject args)
        {
            foreach (var (k, v) in args)
                if (string.Equals(k, "winner", StringComparison.OrdinalIgnoreCase) && v is JsonValue jv && jv.TryGetValue<string>(out var w) && w.Length > 0
                    && Array.FindIndex(Winners, x => string.Equals(x, w, StringComparison.OrdinalIgnoreCase)) < 0)
                    return $"winner '{w}' is not one of {string.Join(", ", Winners)}";
            return null;
        }
    }
    public struct EndExerciseState { public byte Began; public double StartedAt; }

    /// <summary>⭐ <c>Message</c>'s params (SR-121). <c>ToKind</c>: All / Side / Station / Unit / Instructor.</summary>
    public struct MessageParams : IActionArgsCheck
    {
        public FixedString32 ToKind;
        public int ToValue;
        public FixedString128 Text;
        public float Seconds;
        public byte Priority;
        public MessageParams() { ToKind = new FixedString32("All"); ToValue = 0; Text = default; Seconds = 6f; Priority = 0; }

        public const int MaxText = 127;
        public static readonly string[] Kinds = { "All", "Side", "Station", "Unit", "Instructor" };

        public readonly string? Check(JsonObject args)
        {
            foreach (var (k, v) in args)
            {
                if (string.Equals(k, "text", StringComparison.OrdinalIgnoreCase) && v is JsonValue jv && jv.TryGetValue<string>(out var t) && t.Length > MaxText)
                    return $"the text is {t.Length} characters; a player message holds at most {MaxText}";
                if (string.Equals(k, "toKind", StringComparison.OrdinalIgnoreCase) && v is JsonValue kv && kv.TryGetValue<string>(out var kind)
                    && Array.FindIndex(Kinds, x => string.Equals(x, kind, StringComparison.OrdinalIgnoreCase)) < 0)
                    return $"toKind '{kind}' is not one of {string.Join(", ", Kinds)}";
            }
            return null;
        }
    }

    /// <summary>
    /// ⭐ rules S2-d / S2-e / S2-f (📄 docs/DESIGN_Scenario_Rules.md §00.9) — the VERBS: stateful <c>[SharedAiAction]</c>s any trigger's <c>do</c> list (or any BTree)
    /// runs, each naming its target in its params (an <see cref="EntityLink"/>); <c>self</c> = who asked. A failure sets <see cref="ActionFailure"/>.
    /// </summary>
    public static partial class ScenarioVerbs
    {
        private sealed class Log { }

        // ═══════════════════════════════════════ SetObjectiveActive (SR-114) ═══════════════════════════════════════

        /// <summary>Switches the target objective on / off, or restarts it (<c>reset</c>). Instant: Success the same tick. Failure: the target does not resolve, has no
        /// status, is closed, or is final without a reset.</summary>
        [SharedAiAction]
        public static NodeStatus SetObjectiveActive(ref SetObjectiveActiveParams p, ref VerbNoState s, Entity self, EntityRepository world)
        {
            var target = p.Target.Ref.Resolve(world);
            if (target.IsNull || !world.IsAlive(target)) return Fail(world, self, "target not found");
            if (!world.IsComponentTypeRegistered<ObjectiveStatus>() || !world.HasComponent<ObjectiveStatus>(target)) return Fail(world, self, "target has no objective status");
            ref var st = ref world.GetComponentRW<ObjectiveStatus>(target);
            var refusal = ObjectiveRules.SetActive(ref st, p.Active, p.Reset, ObjectiveHost.Now(world));
            if (refusal != null) return Fail(world, self, refusal);
            RuleCadence.Wake(world, target);   // ⭐ CE-2383 — the target may be asleep (final, waiting): it takes the switch at the next frame
            Fdp.Core.Logging.FdpLog<Log>.Info($"objective '{st.Title}': {(p.Reset ? "reset, " : "")}{(p.Active ? "on" : "off")} (asked by {self})");
            return NodeStatus.Success;
        }

        // ═══════════════════════════════════════ AddToCounter (S6 G-3) ═══════════════════════════════════════

        /// <summary>⭐ S6 G-3 (📄 DESIGN_Scenario_Rules §00.12) — adds <c>amount</c> to a <c>Counter</c> measure's value (the crate's +15, the drain's −1, the operator's hand edit: one
        /// shared action). Instant. The counter itself keeps the value within its floor / ceiling at its next tick. Failure: the target does not resolve, is no Counter, or is closed.</summary>
        [SharedAiAction]
        public static NodeStatus AddToCounter(ref AddToCounterParams p, ref VerbNoState s, Entity self, EntityRepository world)
        {
            var target = p.Target.Ref.Resolve(world);
            if (target.IsNull || !world.IsAlive(target)) return Fail(world, self, "target not found");
            if (!world.IsComponentTypeRegistered<ObjectiveStatus>() || !world.HasComponent<ObjectiveStatus>(target)) return Fail(world, self, "target has no objective status");
            ref var st = ref world.GetComponentRW<ObjectiveStatus>(target);
            if (st.Kind.ToString() != CounterBehavior.Kind) return Fail(world, self, "target is not a Counter");
            if (st.Closed != 0) return Fail(world, self, "the counter is closed");
            st.Value += p.Amount;
            Fdp.Core.Logging.FdpLog<Log>.Info($"counter '{st.Title}': {p.Amount:+0.##;-0.##} → {st.Value:0.##} (asked by {self})");
            return NodeStatus.Success;
        }

        // ═══════════════════════════════════════ EndExercise (SR-116) ═══════════════════════════════════════

        /// <summary>How long (sim s) the end waits for every objective to close before it goes on and names the stragglers.</summary>
        public const double CloseTimeoutSeconds = 2.0;

        /// <summary>
        /// ① <c>ExerciseResult.Phase = Closing</c> on the World Environment (every objective's next tick runs its kind's OnClose rule — <c>ObjectiveHost.Enter</c>);
        /// ② Running until every objective / measure status is Closed, or <see cref="CloseTimeoutSeconds"/> of sim time passed (the stragglers' titles are logged);
        /// ③ the outcome per side, <c>Phase = Ended</c>, <c>ExerciseEndedEvent</c> raised on THIS node (the owner; the ingress raises it on every other), Success.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus EndExercise(ref EndExerciseParams p, ref EndExerciseState s, Entity self, EntityRepository world)
        {
            double now = ObjectiveHost.Now(world);
            if (!world.IsComponentTypeRegistered<ExerciseResult>() || !world.IsComponentTypeRegistered<ScenarioClock>()) return Fail(world, self, "no World Environment on this node");
            var env = Entity.Null;
            foreach (var e in world.Query().With<ScenarioClock>().WithLifecycle(EntityLifecycle.All).Build()) { env = e; break; }
            if (env.IsNull) return Fail(world, self, "no World Environment");
            if (s.Began == 0)
            {
                if (!world.HasComponent<ExerciseResult>(env)) world.AddComponent(env, new ExerciseResult());
                ref var r0 = ref world.GetComponentRW<ExerciseResult>(env);
                if (r0.Phase == ExercisePhase.Ended) return Fail(world, self, "the exercise already ended");
                r0.Phase = ExercisePhase.Closing; r0.Reason = p.Reason;
                s.Began = 1; s.StartedAt = now;
                Fdp.Core.Logging.FdpLog<Log>.Info($"exercise closing ({p.Reason}) at {now:0.###} s");
                return NodeStatus.Running;   // the objectives close at their next tick
            }
            var open = Stragglers(world, out int total);
            if (open.Count > 0 && now - s.StartedAt < CloseTimeoutSeconds - 1e-9) return NodeStatus.Running;
            if (open.Count > 0)
                Fdp.Core.Logging.FdpLog<Log>.Warn($"exercise end: {open.Count} of {total} objectives did not close within {CloseTimeoutSeconds} s: {string.Join(", ", open)}");
            ref var r = ref world.GetComponentRW<ExerciseResult>(env);
            Outcomes(world, ref r);
            NameTheWinner(ref r, p.Winner.ToString());
            r.Phase = ExercisePhase.Ended; r.EndedAt = now;
            if (!world.Bus.IsRegistered<ExerciseEndedEvent>()) world.Bus.Register<ExerciseEndedEvent>();
            world.Bus.Publish(new ExerciseEndedEvent { EndedAt = now });
            Fdp.Core.Logging.FdpLog<Log>.Info($"exercise ended at {now:0.###} s: Friend {r.Friend.Outcome} {r.Friend.Score:0.00}, Hostile {r.Hostile.Outcome} {r.Hostile.Score:0.00}");
            return NodeStatus.Success;
        }

        /// <summary>The titles of the objective / measure statuses that are not closed yet.</summary>
        private static List<string> Stragglers(EntityRepository world, out int total)
        {
            total = 0;
            var open = new List<string>();
            foreach (var e in world.Query().With<ObjectiveStatus>().Build())
            {
                ref readonly var st = ref world.GetComponentRO<ObjectiveStatus>(e);
                if (st.Role == ObjectiveRole.Trigger) continue;
                total++;
                if (st.Closed == 0) open.Add(st.Title.Length > 0 ? st.Title.ToString() : st.Kind.ToString());
            }
            return open;
        }

        /// <summary>⭐ ui ⑥ — an authored winner overrides the SR-122 outcomes (Score / Points per side stay as computed): Friend ⇒ Friend Win, Hostile Lose · Hostile ⇒ the reverse · Draw ⇒ both Draw. Empty = untouched.</summary>
        private static void NameTheWinner(ref ExerciseResult r, string winner)
        {
            if (winner.Length == 0) return;
            if (string.Equals(winner, "Draw", StringComparison.OrdinalIgnoreCase)) { r.Friend.Outcome = SideOutcome.Draw; r.Hostile.Outcome = SideOutcome.Draw; }
            else if (string.Equals(winner, "Friend", StringComparison.OrdinalIgnoreCase)) { r.Friend.Outcome = SideOutcome.Win; r.Hostile.Outcome = SideOutcome.Lose; }
            else if (string.Equals(winner, "Hostile", StringComparison.OrdinalIgnoreCase)) { r.Friend.Outcome = SideOutcome.Lose; r.Hostile.Outcome = SideOutcome.Win; }
        }

        private struct Acc { public int Roots, Required, RequiredAchieved, Achieved; public double W, WScore; public float Points; }

        /// <summary>
        /// SR-122 — per side: the ROOT objectives (no parent) whose subject belongs to the side (no subject = Neutral). Score = the weighted mean of their scores
        /// (Skipped excluded unless required); Points = the points of the achieved ones; Outcome Win when every REQUIRED root Achieved (none required: every root),
        /// else Lose; None when the side has no root.
        /// </summary>
        private static void Outcomes(EntityRepository world, ref ExerciseResult r)
        {
            var acc = new Acc[3];
            foreach (var e in world.Query().With<ObjectiveStatus>().Build())
            {
                ref readonly var st = ref world.GetComponentRO<ObjectiveStatus>(e);
                if (st.Role != ObjectiveRole.Objective) continue;
                var links = world.HasComponent<ObjectiveLinks>(e) ? world.GetComponentRO<ObjectiveLinks>(e) : default;
                if (!links.Parent.IsNone) continue;
                int side = Math.Min((int)ObjectiveCensus.SideOf(world, links.Subject.Resolve(world)), 2);
                ref var a = ref acc[side];
                bool skipped = st.State == ObjectiveState.Skipped;
                if (skipped && st.Required == 0) continue;
                a.Roots++;
                bool achieved = st.State == ObjectiveState.Achieved;
                if (achieved) { a.Achieved++; a.Points += st.Points; }
                if (st.Required != 0) { a.Required++; if (achieved) a.RequiredAchieved++; }
                if (st.Scored != 0) { double w = st.Weight > 0f ? st.Weight : 1f; a.W += w; a.WScore += w * (skipped ? 0f : st.Score); }
            }
            r.Neutral = Side(in acc[(int)ForceId.Neutral]);
            r.Friend = Side(in acc[(int)ForceId.Friend]);
            r.Hostile = Side(in acc[(int)ForceId.Hostile]);
        }

        private static SideResult Side(in Acc a) => new()
        {
            Outcome = a.Roots == 0 ? SideOutcome.None
                    : (a.Required > 0 ? a.RequiredAchieved == a.Required : a.Achieved == a.Roots) ? SideOutcome.Win : SideOutcome.Lose,
            Points = a.Points,
            Score = a.W > 0 ? (float)(a.WScore / a.W) : 0f,
        };

        // ═══════════════════════════════════════ Message (SR-121) ═══════════════════════════════════════

        private static int s_messageSeq;

        /// <summary>Publishes a <see cref="PlayerMessageEvent"/> on this node (the <c>PlayerMessage</c> egress sends it; every other node raises it again). Instant.</summary>
        [SharedAiAction]
        public static NodeStatus Message(ref MessageParams p, ref VerbNoState s, Entity self, EntityRepository world)
        {
            if (!world.Bus.IsRegisteredManaged<PlayerMessageEvent>()) world.Bus.RegisterManaged<PlayerMessageEvent>();
            world.Bus.PublishManaged(new PlayerMessageEvent
            {
                MessageId = System.Threading.Interlocked.Increment(ref s_messageSeq), OriginNodeId = Fdp.Core.NodeId.None,
                ToKind = p.ToKind.Length == 0 ? "All" : p.ToKind.ToString(), ToValue = p.ToValue, Text = WithSubject(world, self, p.Text.ToString()),
                Seconds = p.Seconds, Priority = p.Priority, SentAt = ObjectiveHost.Now(world),
                Source = world.HasComponent<NetworkIdentity>(self) ? world.GetComponentRO<NetworkIdentity>(self).Value : 0,
            });
            return NodeStatus.Success;
        }

        /// <summary>⭐ S7 P-8d — <c>{subject}</c> in a message is the name of the sending trigger's subject (<c>ObjectiveLinks.Subject</c>; <c>EntityInfo.Name</c>, else "#netid"): one macro serves every man.</summary>
        internal static string WithSubject(EntityRepository world, Entity self, string text)
        {
            if (!text.Contains("{subject}")) return text;
            string name = "";
            if (world.HasComponent<ObjectiveLinks>(self))
            {
                var who = world.GetComponentRO<ObjectiveLinks>(self).Subject;
                var e = who.Resolve(world);
                if (!e.IsNull && world.IsAlive(e) && world.HasComponent<EntityInfo>(e)) name = world.GetComponentRO<EntityInfo>(e).Name.ToString();
                if (name.Length == 0 && !who.IsNone) name = "#" + who.NetworkId;
            }
            return text.Replace("{subject}", name);
        }

        private static NodeStatus Fail(EntityRepository world, Entity self, string reason)
        {
            ActionFailure.Set(world, self, reason);
            return NodeStatus.Failure;
        }
    }
}
