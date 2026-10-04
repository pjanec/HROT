using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-2078</c> (R-199 ②④) — the ONE task a running reaction paused: what it was started with and who ordered it,
    /// so it RESTARTS through the gate when the reaction ends (resume is <c>CE-2081</c>). 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.1.
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

        /// <summary>Who ordered it — the restart carries this origin, so the task keeps its rank.</summary>
        public BehaviorOrigin Origin { get; init; }
    }
}
