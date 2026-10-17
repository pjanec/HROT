using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Events
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
    [StructLayout(LayoutKind.Sequential)]
    public struct DamageAssessedEvent
    {
        /// <summary>ECS handle of the struck entity.</summary>
        public Entity HitEntity;

        /// <summary>Total computed HP loss (POC: flat damage from <c>BallisticProjectile.Damage</c>).</summary>
        public float TotalDamage;

        /// <summary>True when this event originated from network ingress.</summary>
        public bool IsRemote;

        /// <summary>⭐ <c>CE-3210</c> (hit location HL-6) — APPENDED: 1 when the damage landed on a body PART (<see cref="BodyPart"/>);
        /// the Health owner then adds it to that part's <c>PartDamage</c> level.</summary>
        public byte HasBodyPart;

        /// <summary>⭐ <c>CE-3210</c> — the part id within the body plan.</summary>
        public byte BodyPart;

        /// <summary>⭐ <c>CE-3219</c> (civilians B-1, CV-6) — APPENDED: what the hit does besides its damage (<see cref="HitEffect.Kinetic"/>
        /// = nothing, every hit before).</summary>
        public HitEffect Effect;

        /// <summary>⭐ <c>CE-3219</c> — how long <see cref="Effect"/> lasts (s); the Health owner stores it in <see cref="EffectState"/>.</summary>
        public float EffectSeconds;

        /// <summary>⭐ <c>CE-3237</c> (civilians B-15, AW-23 — APPENDED; docs/DESIGN_Civilians_And_Crowd_Control.md §7.16 DS-1) — who caused it,
        /// where the producer knows: the round's shooter (a person, a vehicle, or a weapon system — <c>WeaponSystems.ControllerOf</c> names
        /// who works it), a melee striker. <see cref="Entity.Null"/> = unknown (a cloud, a canister in hand, a crash, an ejection).</summary>
        public Entity Source;
    }
}
