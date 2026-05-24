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

        /// <summary>True when this event was synthesised from an incoming DDS message; egress translators skip remote events to prevent feedback loops.</summary>
        public bool IsRemote;
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

        /// <summary>True when this event was synthesised from an incoming DDS message; egress translators skip remote events to prevent feedback loops.</summary>
        public bool IsRemote;
    }
}
