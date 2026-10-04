using Fdp.Toolkit.Replication;
namespace Hrot.Map.Definitions.Behavior.Intents
{
    /// <summary>
    /// ⭐ <c>CE-472</c> (decision D) — the parameter contract of the <c>"HullDownAttack"</c> tactical intent, which
    /// <c>HullDownAttackMapper</c> forwards unchanged to the <c>"HullDownAttackRun"</c> behaviour. Until now the intent
    /// had NO contract: the C# helper serialised the internal <c>HullDownAttackParams</c> struct, contradicting
    /// <c>docs/designs/tactical-intent/DESIGN.md</c> §4.2 (every intent has a <c>[BehaviorContract]</c> DTO).
    /// <para>
    /// The member names match <c>HullDownAttackParams</c>'s fields, so the receiver's parse
    /// (<c>HillAttackTankNodes.ParseHullDownAttackParams</c>, case-insensitive <c>DefaultRelaxed</c>) reads it as-is.
    /// The defaults are the constants the C# commander bakes (<c>HillAttackCommanderNodes</c>, the HullDownAttack dispatch; the <c>HullDownIntentJson</c> helper that also baked them retired <c>2026-10-01</c>). The receiver owns the run-time counters
    /// (<c>RoundsFired</c>, <c>LastObservedAmmo</c>), so they are not part of the contract.
    /// </para>
    /// 📄 <c>docs/blueprints/DESIGN_Typed_Intent_And_Json_Nodes.md</c> §4 D.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.AllMilitary)]
    public sealed class HullDownAttackIntentDto
    {
        public const string BehaviorId = BehaviorNames.HullDownAttack;

        /// <summary>Firing-line slot, X (metres, local).</summary>
        public float SlotX { get; set; }
        /// <summary>Firing-line slot, Y (metres, local).</summary>
        public float SlotY { get; set; }
        /// <summary>Baseline retreat slot, X (metres, local).</summary>
        public float BaselineX { get; set; }
        /// <summary>Baseline retreat slot, Y (metres, local).</summary>
        public float BaselineY { get; set; }
        /// <summary>Normalised attack direction, X.</summary>
        public float AttackDirX { get; set; }
        /// <summary>Normalised attack direction, Y.</summary>
        public float AttackDirY { get; set; }
        /// <summary>The target (resolved on the receiving node). ⭐ An <see cref="EntityRef"/> (<c>DESIGN_Entity_Reference.md</c>)
        /// — it was a bare <c>long</c> with no remap and no picker, the one authorable id the attributes had missed.</summary>
        public EntityRef TargetNetworkId { get; set; }
        /// <summary>Approach speed far from the slot (m/s).</summary>
        public float ApproachSpeed { get; set; } = 15f;
        /// <summary>Creep speed near the slot (m/s).</summary>
        public float CreepSpeed { get; set; } = 5f;
        /// <summary>Rounds to fire before success; 0 = unlimited.</summary>
        public int MaxRounds { get; set; } = 1;
    }
}
