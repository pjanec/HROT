using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace FDP.Toolkit.Combat.Events
{
    // ── DetonationNotification ────────────────────────────────────────────────
    // Moved to FDP.Toolkit.Combat.Contracts (BS1-T010) so that FDP.Toolkit.Physics
    // can publish it without creating a circular project dependency.
    // DetonationNotification is now accessible via using FDP.Toolkit.Combat.Contracts.
    // Existing code in FDP.Toolkit.Combat continues to resolve it transitively.

    // ── DamageAssessedEvent ───────────────────────────────────────────────────

    /// <summary>
    /// Published by <c>DamageCalculationSystem</c> within the Damage Assessment Module
    /// after HP loss has been computed for a bullet impact.
    /// <para>
    /// Translated to an <c>EntityHitDamage</c> DDS message by
    /// <c>DamageAssessedEgressTranslator</c>.  The authoritative node applies the damage
    /// to the <see cref="FDP.Toolkit.Combat.Contracts.Components.Health"/> component upon
    /// receiving the DDS message back through <c>EntityHitDamageIngressTranslator</c>.
    /// </para>
    /// </summary>
    [EventId(CombatConstants.DamageAssessedEventId)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DamageAssessedEvent
    {
        /// <summary>Network entity ID of the struck entity.</summary>
        public long HitEntityId;

        /// <summary>Total computed HP loss (POC: flat damage from <c>BallisticProjectile.Damage</c>).</summary>
        public float TotalDamage;
    }
}
