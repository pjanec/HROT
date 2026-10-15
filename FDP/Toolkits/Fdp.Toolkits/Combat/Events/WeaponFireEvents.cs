using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Events
{
    // ── WeaponFireIntent ──────────────────────────────────────────────────────

    /// <summary>
    /// Published by <see cref="Executors.AimAndFireExecutor"/> on the Brain node when
    /// weapon conditions are met.  Identifies the shooter and target using local ECS
    /// <see cref="Entity"/> handles.
    /// <para>
    /// In a split topology this event is translated to a <c>WeaponFireRequest</c> DDS
    /// message by <c>WeaponFireIntentEgressTranslator</c>, which resolves the entity
    /// handles to stable network IDs before writing the wire message.
    /// In AllInOne, <c>FireProcessingSystem</c> consumes this event directly from the local
    /// event bus.
    /// </para>
    /// <para>
    /// <b>PACK-P003:</b> Changed from <c>long</c> network IDs to local ECS handles so that
    /// neither <c>AimAndFireExecutor</c> nor <c>FireProcessingSystem</c> require
    /// <c>NetworkEntityMap</c>.
    /// </para>
    /// </summary>
    [EventId(CombatConstants.WeaponFireIntentEventId)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WeaponFireIntent
    {
        /// <summary>Local ECS entity handle of the firing entity.</summary>
        public Entity Shooter;

        /// <summary>Local ECS entity handle of the intended target.</summary>
        public Entity Target;

        /// <summary>Zero-based index of the weapon slot being fired (POC: always 0).</summary>
        public int WeaponIndex;

        /// <summary>True when this event originated from network ingress.</summary>
        public bool IsRemote;

        /// <summary>⭐ Stage 6 (<c>CE-1032</c>, W-8) — fire at <see cref="TargetPoint"/> instead of an entity (<see cref="Target"/> is
        /// <see cref="Entity.Null"/>): a thrown grenade, a mortar. A warhead marked <c>Indirect</c> flies a gravity arc to it.</summary>
        public bool AtPoint;

        /// <summary>⭐ <c>CE-1032</c> — the aim point when <see cref="AtPoint"/>.</summary>
        public System.Numerics.Vector3 TargetPoint;

        /// <summary>⭐ M8 (D-I) — the munition type of the LOADED round (the weapon brain's choice from the mount's loadout);
        /// <b>0 = the mount's own <c>AmmoGuid</c></b>, as before. <c>FireProcessingSystem</c> fires this round.</summary>
        public long AmmoGuid;

        /// <summary>⭐ Indirect fire IF-1 (I3) — the fire solution's CHARGE (1-based into the round's <c>ChargeSpeedsMps</c>);
        /// <b>0 = no solution</b>: today's arc (<c>ThrowArc</c>, the mount's one speed). <c>FireProcessingSystem</c> flies this charge.</summary>
        public byte Charge;

        /// <summary>⭐ IF-1 (I3) — with a <see cref="Charge"/>: 1 = the high arc, 0 = the low one. The brain decided; the SimHost flies it.</summary>
        public byte HighArc;
    }

    // ── WeaponFireNotification ────────────────────────────────────────────────

    /// <summary>
    /// Published by <c>FireProcessingSystem</c> on the Muscle node after a bullet entity
    /// has been spawned.  Consumed by <c>WeaponFireNotificationEgressTranslator</c> to
    /// broadcast a <c>WeaponFire</c> DDS message (IG draws muzzle-flash effect).
    /// <para>
    /// <b>PACK-P003:</b> Changed from <c>long</c> network IDs to local ECS handles,
    /// consistent with <see cref="WeaponFireIntent"/>.  The egress translator resolves
    /// the handles to network IDs before writing the DDS wire message.
    /// </para>
    /// </summary>
    [EventId(CombatConstants.WeaponFireNotificationEventId)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WeaponFireNotification
    {
        /// <summary>Local ECS entity handle of the firing entity.</summary>
        public Entity Shooter;

        /// <summary>Local ECS entity handle of the intended target.</summary>
        public Entity Target;

        /// <summary>Zero-based weapon slot index.</summary>
        public int WeaponIndex;

        /// <summary>True when this event originated from network ingress.</summary>
        public bool IsRemote;
    }
}
