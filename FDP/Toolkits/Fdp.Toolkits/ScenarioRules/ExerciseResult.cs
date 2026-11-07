using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Weather;

namespace Fdp.Toolkit.ScenarioRules
{
    /// <summary>⭐ rules S2-e (SR-116 / SR-122) — where the exercise is: running, closing (every objective is being judged) or ended.</summary>
    public enum ExercisePhase : byte { Running = 0, Closing = 1, Ended = 2 }

    /// <summary>⭐ rules S2-e (SR-122) — how a side came out.</summary>
    public enum SideOutcome : byte { None = 0, Win = 1, Lose = 2, Draw = 3 }

    /// <summary>⭐ rules S2-e — one side's result: the outcome, the points its achieved root objectives carry, and the weighted score (0..1) of its roots.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SideResult
    {
        public SideOutcome Outcome;
        public float Points;
        public float Score;
    }

    /// <summary>
    /// ⭐ rules S2-e (📄 docs/DESIGN_Scenario_Rules.md §00.9, SR-116 / SR-122) — the exercise's RESULT: its phase and the outcome per side. Lives on the World
    /// Environment (the entity carrying <see cref="ScenarioClock"/>); written by <c>EndExercise</c> on the node that owns it; replicated as
    /// <c>dtExerciseResult</c> (Brain group). ⛔ The objective TREE is not in here — it is the statuses (one owner per fact). Unmanaged, <c>NoScenario</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ExerciseResult)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct ExerciseResult
    {
        public ExercisePhase Phase;
        /// <summary>Sim time (s) when <see cref="Phase"/> became Ended.</summary>
        public SimInstant EndedAt;
        /// <summary>Why it ended (the <c>EndExercise</c> reason).</summary>
        public FixedString64 Reason;
        /// <summary>Per side (<see cref="ForceId"/>): Friend, Hostile, Neutral.</summary>
        public SideResult Friend, Hostile, Neutral;

        /// <summary>The result of <paramref name="side"/>.</summary>
        public readonly SideResult Of(ForceId side) => side == ForceId.Friend ? Friend : side == ForceId.Hostile ? Hostile : Neutral;

        /// <summary>The phase of the exercise on <paramref name="world"/>, Running when no result exists (yet).</summary>
        public static ExercisePhase PhaseOf(ISimulationView world)
        {
            if (world is EntityRepository r && !r.IsComponentTypeRegistered<ExerciseResult>()) return ExercisePhase.Running;
            foreach (var e in world.Query().With<ExerciseResult>().WithLifecycle(EntityLifecycle.All).Build())
                return world.GetComponentRO<ExerciseResult>(e).Phase;
            return ExercisePhase.Running;
        }
    }

    /// <summary>
    /// ⭐ rules S2-e (SR-122) — the exercise ended (<see cref="ExerciseResult.Phase"/> became Ended): raised ONCE per node — by <c>EndExercise</c> on the node
    /// that owns the result, by the <c>EntityExerciseResult</c> ingress on every other node. Consumed by pause-on-end and the summaries.
    /// </summary>
    [EventId(BehaviorConstants.EventId_ExerciseEnded)]
    public struct ExerciseEndedEvent
    {
        /// <summary>Sim time (s) of the end.</summary>
        public SimInstant EndedAt;
    }

    /// <summary>
    /// ⭐ rules S7 P-4 (📄 docs/DESIGN_Scenario_Rules.md §00.13) — a capture point CHANGED HANDS to <see cref="Side"/>: raised once, by the point (<c>HoldArea</c> capture by side), one frame.
    /// <see cref="Men"/> are the first <see cref="MenCount"/> (≤ 16) men of that side standing inside at that moment — what a <c>Count captures</c> measure credits. Local to the Brain node.
    /// </summary>
    [EventId(BehaviorConstants.EventId_ObjectiveCaptured)]
    [StructLayout(LayoutKind.Sequential)]
    public struct ObjectiveCapturedEvent
    {
        /// <summary>The capture-point objective.</summary>
        public Entity Point;
        /// <summary>The side that now owns it (never Neutral).</summary>
        public ForceId Side;
        public Ents16 Men;
        public int MenCount;
    }

    /// <summary>
    /// ⭐ rules S2-f (SR-121) — a message to the players, as the local FDP event every node reads: published by the <c>Message</c> action, carried by the
    /// <c>PlayerMessage</c> topic, raised again on every other node with <see cref="OriginNodeId"/> set. The subtitle and the log are the Player's (ui).
    /// </summary>
    public sealed class PlayerMessageEvent
    {
        public int MessageId;
        /// <summary>The node that raised it: 0 = THIS node (the egress sends it); otherwise it arrived from that node.</summary>
        public Fdp.Core.NodeId OriginNodeId;
        /// <summary>All / Side / Station / Unit / Instructor.</summary>
        public string ToKind = "All";
        /// <summary>Which side (a <see cref="ForceId"/>) / station / unit (network id) <see cref="ToKind"/> names; 0 for All / Instructor.</summary>
        public int ToValue;
        public string Text = "";
        /// <summary>How long a subtitle shows (s).</summary>
        public float Seconds = 6f;
        public byte Priority;
        /// <summary>Sim time (s) it was sent.</summary>
        public SimInstant SentAt;
        /// <summary>The network id of the trigger (or entity) that sent it; 0 = none.</summary>
        public int Source;
    }
}
