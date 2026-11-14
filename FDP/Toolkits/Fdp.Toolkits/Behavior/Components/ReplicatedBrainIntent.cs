using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-3048</c> (V7) — the last AI intent the unit's Brain owner PUBLISHED, held on every other Brain-capable node:
    /// what runs in the task slot, the SOP slot and the ROE, in the scenario's own shape (<see cref="InitialBrainIntent"/>,
    /// R-192). The node that GAINS the unit's Brain starts it (<c>BrainHandOverSystem</c>). 📄
    /// <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.7.
    /// <para>⚠ <see cref="DataPolicy.Transient"/>: a network replica, never saved or recorded.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.ReplicatedBrainIntent)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class ReplicatedBrainIntent
    {
        public InitialBrainIntent Intent { get; init; } = new();
    }
}
