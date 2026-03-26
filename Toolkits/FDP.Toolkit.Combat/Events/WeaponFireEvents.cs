using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace FDP.Toolkit.Combat.Events
{
    // ── WeaponFireIntent ──────────────────────────────────────────────────────

    /// <summary>
    /// Published by <see cref="Executors.AimAndFireExecutor"/> on the Brain node when
    /// weapon conditions are met.  Identifies the shooter and target using stable network
    /// entity IDs (not local ECS <see cref="Entity"/> handles).
    /// <para>
    /// In a split topology this event is translated to a <c>WeaponFireRequest</c> DDS
    /// message by <c>WeaponFireIntentEgressTranslator</c> and forwarded to the Muscle node.
    /// In AllInOne, <c>FireProcessingSystem</c> consumes this event directly from the local
    /// event bus.
    /// </para>
    /// </summary>
    [EventId(CombatConstants.WeaponFireIntentEventId)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WeaponFireIntent
    {
        /// <summary>Network entity ID of the firing entity.</summary>
        public long ShooterEntityId;

        /// <summary>Network entity ID of the intended target.</summary>
        public long TargetEntityId;

        /// <summary>Zero-based index of the weapon slot being fired (POC: always 0).</summary>
        public int WeaponIndex;
    }

    // ── WeaponFireNotification ────────────────────────────────────────────────

    /// <summary>
    /// Published by <c>FireProcessingSystem</c> on the Muscle node after a bullet entity
    /// has been spawned.  Consumed by <c>WeaponFireNotificationEgressTranslator</c> to
    /// broadcast a <c>WeaponFire</c> DDS message (IG draws muzzle-flash effect).
    /// </summary>
    [EventId(CombatConstants.WeaponFireNotificationEventId)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WeaponFireNotification
    {
        /// <summary>Network entity ID of the firing entity.</summary>
        public long ShooterEntityId;

        /// <summary>Network entity ID of the intended target.</summary>
        public long TargetEntityId;

        /// <summary>Zero-based weapon slot index.</summary>
        public int WeaponIndex;
    }
}
