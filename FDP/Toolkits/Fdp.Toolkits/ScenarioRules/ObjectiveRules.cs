using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.ScenarioRules
{
    /// <summary>⭐ rules S1 (SR-117) — how an objective is judged when the exercise ends.</summary>
    public enum CloseRule : byte
    {
        /// <summary>Active → Failed ("not achieved by the end"); Pending / Inactive → Skipped.</summary>
        Ordinary = 0,
        /// <summary>A constraint (Protect, NoHarm): Active → Achieved when it did not fail.</summary>
        JudgedAtEnd = 1,
        /// <summary>A capture objective: freezes its Owner, the state stays.</summary>
        Capture = 2,
    }

    /// <summary>
    /// ⭐ rules S1 (📄 docs/DESIGN_Scenario_Rules.md §10.2) — the AUTHORED block every objective kind's params begin with
    /// (<c>"common": {...}</c>). Zero is the default everywhere a default is not zero: <see cref="Scored"/> / <see cref="StartActive"/>
    /// are <c>null</c> = true, <see cref="Weight"/> 0 = 1, <see cref="VisibleSides"/> 0 = everybody (0xFF).
    /// </summary>
    public struct ObjectiveCommon
    {
        [JsonPropertyName("title")] public string? Title;
        /// <summary>Whose men it watches (a unit, a man); none = nobody.</summary>
        [JsonPropertyName("subject")] public EntityLink Subject;
        /// <summary>The composite this objective belongs to (the tree, SR-53).</summary>
        [JsonPropertyName("parent")] public EntityLink Parent;
        [JsonPropertyName("required")] public bool Required;
        /// <summary>Counts in the score; <c>null</c> = true.</summary>
        [JsonPropertyName("scored")] public bool? Scored;
        /// <summary>0 = 1.</summary>
        [JsonPropertyName("weight")] public float Weight;
        [JsonPropertyName("points")] public float Points;
        /// <summary>Sim seconds since the start; 0 = none.</summary>
        [JsonPropertyName("deadlineSeconds")] public float DeadlineSeconds;
        /// <summary>Starts Active (<c>null</c> = true) or Inactive.</summary>
        [JsonPropertyName("startActive")] public bool? StartActive;
        /// <summary>Bit mask by <see cref="ForceId"/> value; 0 = everybody.</summary>
        [JsonPropertyName("visibleSides")] public byte VisibleSides;
    }

    /// <summary>⭐ rules S1 — the unmanaged twin of <see cref="ObjectiveCommon"/> a blackboard can hold (defaults applied).</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct ObjectiveCommonBlock
    {
        public FixedString64 Title;
        public EntityRef Subject, Parent;
        public byte Required, Scored, StartActive, VisibleSides;
        public float Weight, Points, DeadlineSeconds;

        public static ObjectiveCommonBlock From(in ObjectiveCommon c) => new()
        {
            Title = new FixedString64(c.Title ?? string.Empty),
            Subject = c.Subject.Ref, Parent = c.Parent.Ref,
            Required = (byte)(c.Required ? 1 : 0),
            Scored = (byte)(c.Scored ?? true ? 1 : 0),
            StartActive = (byte)(c.StartActive ?? true ? 1 : 0),
            VisibleSides = c.VisibleSides == 0 ? (byte)0xFF : c.VisibleSides,
            Weight = c.Weight == 0f ? 1f : c.Weight,
            Points = c.Points, DeadlineSeconds = c.DeadlineSeconds,
        };
    }

    /// <summary>⭐ rules S1 — the run record every kind keeps in its blackboard (recorded and restored with it).</summary>
    public struct ObjectiveRun { public byte Began; public double LastNow; }

    /// <summary>A fixed set of 16 ids (the NoHarm victims).</summary>
    [InlineArray(16)] public struct Ids16 { private long _e; }

    /// <summary>A fixed set of 8 network ids (a target list).</summary>
    [InlineArray(8)] public struct NetIds8 { private int _e; }

    /// <summary>
    /// ⭐⭐ rules S1 (📄 docs/DESIGN_Scenario_Rules.md §00.8 S1-e, SR-113 … SR-117) — THE one rule set: the lifecycle, the score and the
    /// OnClose of an objective are decided HERE and nowhere else; every kind is a thin behaviour that reads the world and calls these.
    /// Pure functions over <see cref="ObjectiveStatus"/>.
    /// </summary>
    public static class ObjectiveRules
    {
        /// <summary>First tick: the starting state, the display projection of the params, the links. Links are re-copied by <see cref="CopyLinks"/>.</summary>
        public static void Begin(ref ObjectiveStatus s, ref ObjectiveLinks l, in ObjectiveCommonBlock c, string kind, ObjectiveRole role, double now)
        {
            s.Role = role;
            s.State = c.StartActive != 0 ? ObjectiveState.Active : ObjectiveState.Inactive;
            s.ChangedAt = now;
            s.Kind = new FixedString32(kind);
            Display(ref s, in c);
            CopyLinks(ref l, in c);
        }

        /// <summary>The display fields (a projection of the params — never written back).</summary>
        public static void Display(ref ObjectiveStatus s, in ObjectiveCommonBlock c)
        {
            s.Title = c.Title; s.Required = c.Required; s.Scored = c.Scored; s.Weight = c.Weight; s.Points = c.Points; s.VisibleSides = c.VisibleSides;
        }

        /// <summary>S1-c — the links are the params' copy, rewritten every tick (ONE owner: the params).</summary>
        public static void CopyLinks(ref ObjectiveLinks l, in ObjectiveCommonBlock c) { l.Subject = c.Subject; l.Parent = c.Parent; }

        /// <summary>
        /// Moves the objective to <paramref name="next"/> with <paramref name="reason"/>; <c>true</c> when the STATE changed. A closed objective
        /// changes nothing; a final one never leaves its state (SR-114 — only S2's reset does); the same state only refreshes the reason.
        /// <c>ChangedAt</c> moves with the state only.
        /// </summary>
        public static bool Settle(ref ObjectiveStatus s, ObjectiveState next, string reason, double now)
        {
            if (s.Closed != 0) return false;
            if (s.IsFinal && next != s.State) return false;
            s.Reason = new FixedString64(reason ?? string.Empty);
            if (next == s.State) return false;
            s.State = next;
            s.ChangedAt = now;
            return true;
        }

        public static bool DeadlinePassed(in ObjectiveCommonBlock c, double now) => c.DeadlineSeconds > 0f && now >= c.DeadlineSeconds;

        /// <summary>
        /// The normalised score (0..1) of a status, <see cref="float.NaN"/> when it is EXCLUDED (Skipped — SR-117). Achieved 1, Failed 0;
        /// Active / Pending / Inactive 0 for an ordinary objective; a <paramref name="judgedAtEnd"/> constraint scores 1 while Active (within
        /// limits); a Measure is its value against its target.
        /// </summary>
        public static float Score(in ObjectiveStatus s, bool judgedAtEnd = false)
        {
            if (s.State == ObjectiveState.Skipped) return float.NaN;
            if (s.Role == ObjectiveRole.Measure)
            {
                if (s.Target <= 0f) return s.Value <= 0f ? 1f : 0f;
                return Math.Clamp(s.Value / s.Target, 0f, 1f);
            }
            return s.State switch
            {
                ObjectiveState.Achieved => 1f,
                ObjectiveState.Failed => 0f,
                ObjectiveState.Active => judgedAtEnd ? 1f : 0f,
                _ => 0f,
            };
        }

        /// <summary>Writes <see cref="Score"/> into the status (a parent reads one field); an excluded status stores 0 (the state says Skipped).</summary>
        public static void WriteScore(ref ObjectiveStatus s, bool judgedAtEnd = false)
        {
            float v = Score(in s, judgedAtEnd);
            s.Score = float.IsNaN(v) ? 0f : v;
        }

        /// <summary>How the kind named <paramref name="kind"/> closes (S2's EndExercise asks): the constraints are judged at the end.</summary>
        public static CloseRule CloseRuleOf(string kind, bool capture = false)
            => capture ? CloseRule.Capture : kind is "Protect" or "NoHarm" ? CloseRule.JudgedAtEnd : CloseRule.Ordinary;

        /// <summary>
        /// ⭐ SR-117 — closes an objective at the end of the exercise: judged-at-end → Achieved when it did not fail; every other Active →
        /// Failed "not achieved by the end"; Pending / Inactive → Skipped; a capture freezes its Owner (state stays); a measure freezes its
        /// value. Always sets <c>Closed</c> (nothing changes it afterwards). S1 only rails it — S2's EndExercise calls it.
        /// </summary>
        public static void OnClose(ref ObjectiveStatus s, CloseRule rule, double now)
        {
            if (s.Closed != 0) return;
            if (s.Role != ObjectiveRole.Measure && !s.IsFinal)
            {
                switch (s.State)
                {
                    case ObjectiveState.Active when rule == CloseRule.JudgedAtEnd:
                        Settle(ref s, ObjectiveState.Achieved, "within limits at the end", now); break;
                    case ObjectiveState.Active when rule == CloseRule.Ordinary:
                        Settle(ref s, ObjectiveState.Failed, "not achieved by the end", now); break;
                    case ObjectiveState.Pending:
                    case ObjectiveState.Inactive:
                        Settle(ref s, ObjectiveState.Skipped, "never became active", now); break;
                }
            }
            s.Closed = 1;
        }
    }
}
