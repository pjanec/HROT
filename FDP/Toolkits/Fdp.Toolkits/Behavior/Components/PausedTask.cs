using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-2078</c> (R-199 ②④) — the ONE task a running reaction paused: what it was started with and who ordered it,
    /// so it RESUMES when the reaction ends — ⭐ <c>CE-2081</c>: its storage and parts were kept, and its run is restored as it
    /// was (<see cref="Hash"/>, <see cref="InstanceId"/>, <see cref="BrainTier"/>); a pause that could not keep them
    /// (<see cref="Restart"/>) restarts through the gate as before. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.1, §4.9a.
    /// <para>Written by <see cref="Systems.BehaviorIngressSystem"/> when a reaction replaces a task; consumed when the
    /// reaction ends (finish or self-clear); dropped when an order replaces or clears the reaction. ⛔ Never stacked: a more
    /// urgent reaction replaces the running one and the paused task stays as it was.</para>
    /// <para>⚠ <see cref="DataPolicy.Transient"/>, like <see cref="BehaviorStartRecord"/>: a reaction is short-lived; a
    /// snapshot taken during one restores the reaction alone (the AI snapshot, <c>CE-3042</c>, saves the task).</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.PausedTask)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class PausedTask
    {
        /// <summary>The paused behaviour's registered name.</summary>
        public string BehaviorName { get; init; } = "";

        /// <summary>The JSON it was started with (<see cref="BehaviorStartRecord.JsonParams"/>).</summary>
        public string JsonParams { get; init; } = "{}";

        /// <summary>Who ordered it — the resume (or restart) carries this origin, so the task keeps its rank.</summary>
        public BehaviorOrigin Origin { get; init; }

        /// <summary>⭐ <c>CE-2081</c> — the paused behaviour's hash (its storage keys derive from it).</summary>
        public int Hash { get; init; }

        /// <summary>⭐ <c>CE-2081</c> — the paused run's token: restored on resume, so its owned parts, its channel stamp and a
        /// blueprint's cursor version match again.</summary>
        public uint InstanceId { get; init; }

        /// <summary>⭐ <c>CE-2081</c> — the paused behaviour's brain tier.</summary>
        public byte BrainTier { get; init; }

        /// <summary>⭐ <c>CE-2081</c> — every storage slot key the task held at the pause (the SOP's excluded): no sweep detaches
        /// them while paused; a DROP detaches them.</summary>
        public int[] HeldKeys { get; set; } = System.Array.Empty<int>();

        /// <summary>⭐ <c>CE-2081</c> — true when this pause could not keep the task (the reaction's storage overlaps it):
        /// the task restarts through the gate instead of resuming.</summary>
        public bool Restart { get; set; }
    }
}
