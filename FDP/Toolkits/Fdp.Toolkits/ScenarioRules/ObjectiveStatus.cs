using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.ScenarioRules
{
    /// <summary>⭐ rules S1 (SR-113) — what an objective entity IS: an objective, a measure (a value against a target) or a trigger (S2).</summary>
    public enum ObjectiveRole : byte { Objective = 0, Measure = 1, Trigger = 2 }

    /// <summary>⭐ rules S1 (SR-113 / SR-117) — the lifecycle of an objective.</summary>
    public enum ObjectiveState : byte { Pending = 0, Active = 1, Achieved = 2, Failed = 3, Inactive = 4, Skipped = 5 }

    /// <summary>
    /// ⭐ rules S1-a (📄 docs/DESIGN_Scenario_Rules.md §00.8, SR-120) — an objective / measure / trigger entity's status: lifecycle state,
    /// progress, value against target, score, and the DISPLAY projection of its params (title, kind, weights, who may see it) so a HUD
    /// reads one topic and never parses JSON. Written by the entity's own behaviour (Brain group), replicated as <c>dtObjectiveStatus</c>;
    /// runtime state — never authored in a scenario (the params are).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ObjectiveStatus)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct ObjectiveStatus
    {
        public ObjectiveRole Role;
        public ObjectiveState State;
        /// <summary>1 = final: the exercise closed it (OnClose rules, SR-117) — nothing changes it any more.</summary>
        public byte Closed;
        /// <summary>The side the objective belongs to.</summary>
        public ForceId Owner;
        public int ProgressCur;
        public int ProgressNeed;
        public float Value;
        public float Target;
        /// <summary>0..1.</summary>
        public float Score;
        public float Points;
        /// <summary>Sim time (s) of the last state change.</summary>
        public double ChangedAt;
        /// <summary>A short text: "held 60 s, 5 Blue / 0 Red".</summary>
        public FixedString64 Reason;
        public FixedString64 Title;
        public FixedString32 Kind;
        public byte Required;
        public byte Scored;
        public float Weight;
        /// <summary>Bit mask by <see cref="ForceId"/> value (bit 1 = Friend, bit 2 = Hostile ...); 0 = nobody asked, the display decides.</summary>
        public byte VisibleSides;
        /// <summary>Triggers: how many times it fired.</summary>
        public int FireCount;
        /// <summary>Triggers: sim time (s) of the last firing.</summary>
        public double LastFired;

        /// <summary>Achieved, Failed or Skipped — it will not change by itself.</summary>
        public readonly bool IsFinal => State == ObjectiveState.Achieved || State == ObjectiveState.Failed || State == ObjectiveState.Skipped;
    }
}
