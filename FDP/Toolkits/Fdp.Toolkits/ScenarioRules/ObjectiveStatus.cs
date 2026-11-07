using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.ScenarioRules
{
    /// <summary>⭐ rules S1 (SR-113) — what an objective entity IS: an objective, a measure (a value against a target) or a trigger (S2).</summary>
    public enum ObjectiveRole : byte { Objective = 0, Measure = 1, Trigger = 2 }

    /// <summary>⭐ rules S1 (SR-113 / SR-117) — the lifecycle of an objective.</summary>
    public enum ObjectiveState : byte { Pending = 0, Active = 1, Achieved = 2, Failed = 3, Inactive = 4, Skipped = 5 }

    /// <summary>
    /// ⭐ rules S2-c (§10.11 T8) — where a TRIGGER is in its state machine. Only a <see cref="ObjectiveRole.Trigger"/> carries a meaningful one (0 = Disarmed
    /// for everything else). Published at once on change (it is a state field, not progress).
    /// </summary>
    public enum TriggerState : byte { Disarmed = 0, Armed = 1, Holding = 2, Waiting = 3, Acting = 4, Fired = 5, Cooldown = 6, Done = 7 }

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
        /// <summary>⭐ rules S4 ② — the pass mark (0..1): GO / NO-GO is the read <c>Score ≥ Pass</c>. Written in ONE place (<see cref="ObjectiveRules.PassOf"/>); on a composite this is the field to read (<c>Target</c> keeps the same number there for old screens).</summary>
        public float Pass;
        public float Points;
        /// <summary>Sim time (s) of the last state change.</summary>
        public SimInstant ChangedAt;
        /// <summary>A short text: "held 60 s, 5 Blue / 0 Red".</summary>
        public FixedString64 Reason;
        public FixedString64 Title;
        public FixedString32 Kind;
        /// <summary>⭐ rules S4 E-3 — the evaluation group (mission · fire · reports · conduct …) a frontend sections the rows by; "" = none. A projection of <c>common.group</c>.</summary>
        public FixedString32 Group;
        public byte Required;
        public byte Scored;
        public float Weight;
        /// <summary>Bit mask by <see cref="ForceId"/> value (bit 1 = Friend, bit 2 = Hostile ...); 0 = nobody asked, the display decides.</summary>
        public byte VisibleSides;
        /// <summary>Triggers: how many times it fired.</summary>
        public int FireCount;
        /// <summary>Triggers: sim time (s) of the last firing.</summary>
        public double LastFired;
        /// <summary>Triggers (<see cref="ObjectiveRole.Trigger"/>): the §10.11 machine's state.</summary>
        public TriggerState TriggerState;
        /// <summary>
        /// ⭐ rules S2-d (SR-114) — a request for the owning behaviour to RESTART: 1 = reset, then Active; 2 = reset, then Inactive. Written by
        /// <c>SetObjectiveActive</c>, honoured (and cleared) by <c>ObjectiveHost.Enter</c> — the ONE place a run is re-begun. Local: never on the wire.
        /// </summary>
        public byte ResetRequested;

        /// <summary>⭐ rule editor ① (§00.11a) — Triggers, while <see cref="TriggerState.Acting"/>: the 1-based index of the running <c>do</c> step and the list's length (0 / 0 otherwise); after a FAILED step <see cref="StepCur"/> stays at that step (its reason is in <see cref="Reason"/>). Written in ONE place (<c>TriggerBehavior.Act</c>). On the wire.</summary>
        public byte StepCur, StepCount;

        /// <summary>⭐ rules V-1 (§00.10) — the network ids of the entities a trigger's <c>when</c> NAMES (<c>OnEntity</c> refs, recursively): the first <see cref="DependsOnCount"/> of <see cref="DependsOn"/>. Static; on the wire.</summary>
        public byte DependsOnCount;
        public NetIds8 DependsOn;
        /// <summary>⭐ rules V-1 — the targets a trigger's <c>do</c> list NAMES (any step's <c>target</c>): the first <see cref="ActsOnCount"/> of <see cref="ActsOn"/>.</summary>
        public byte ActsOnCount;
        public NetIds8 ActsOn;
        /// <summary>⭐ rules V-2 — the authored timing (s; 0 = none) of a trigger; <see cref="Deadline"/> is every kind's <c>common.deadlineSeconds</c> (sim s since the start).</summary>
        public float HoldFor;
        public SimDuration Delay, Cooldown;
        public float Deadline;
        /// <summary>⭐ rules V-2 — sim s of the NEXT thing the owner already knows will happen (Waiting ⇒ the delay's end · Holding ⇒ edge + holdFor · Cooldown ⇒ its end · an Active one with a deadline ⇒ the deadline); 0 = unknown / final.</summary>
        public SimInstant ExpectedAt;

        /// <summary>⭐ G-8 (DESIGN_Scenario_Rules §00.12, CE-4230) — a CAPTURE point's facts, so every node's HUD reads them from the status (display fields, on the wire): <see cref="Capture"/> 1 = a capture point;
        /// <see cref="Challenger"/> = the side whose clock runs against the owner (Neutral = none); <see cref="AreaCenter"/> / <see cref="AreaRadius"/> = the area's bounding circle (a circle as authored, a rectangle's half diagonal, an area entity's outline from its centroid). Written by <c>HoldAreaBehavior</c>.</summary>
        public byte Capture;
        public ForceId Challenger;
        public WorldPoint AreaCenter;
        public float AreaRadius;

        /// <summary>⭐ CE-4238 follow-up (ui's ask) — 1 = this trigger's <c>do</c> list holds an <c>EndExercise</c> step, so a HUD on ANY node (the authored params live only on the Brain) can count down to the round's end. Static, set at assignment with <see cref="ActsOn"/>; on the wire.</summary>
        public byte EndsExercise;

        /// <summary>Achieved, Failed or Skipped — it will not change by itself.</summary>
        public readonly bool IsFinal => State == ObjectiveState.Achieved || State == ObjectiveState.Failed || State == ObjectiveState.Skipped;
    }
}
