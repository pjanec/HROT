using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace FDP.Toolkit.Combat.Contracts
{
    /// <summary>
    /// Published by <c>HitResolutionSystem</c> (in <c>FDP.Toolkit.Physics</c>) on the Muscle
    /// node when a bullet impact is resolved.  Carries the hit position so the IG can place
    /// an explosion particle effect at the correct world position.
    ///
    /// <b>Placement note (BS1-T010):</b> Moved from <c>FDP.Toolkit.Combat.Events</c> to this
    /// thin contracts assembly so that <c>FDP.Toolkit.Physics</c> (which cannot reference
    /// <c>FDP.Toolkit.Combat</c> without a circular dependency) can publish this event.
    /// Both <c>FDP.Toolkit.Physics</c> and <c>FDP.Toolkit.Combat</c> reference
    /// <c>FDP.Toolkit.Combat.Contracts</c>; neither references the other directly.
    ///
    /// <b>PACK-P003:</b> Fields changed from <c>long</c> network IDs to local ECS
    /// <see cref="Entity"/> handles.  Network-ID resolution is deferred to egress
    /// translators (<c>MunitionDetonationEgressTranslator</c>) which inject
    /// <c>NetworkEntityMap</c> for the conversion.
    /// </summary>
    [EventId(5005)]   // = CombatConstants.DetonationNotificationEventId; must not change.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DetonationNotification
    {
        /// <summary>Local ECS entity handle of the shooter.</summary>
        public Entity Shooter;

        /// <summary>Local ECS entity handle of the entity that was struck.</summary>
        public Entity Target;

        /// <summary>World-space X coordinate of the hit position.</summary>
        public float HitX;

        /// <summary>World-space Y coordinate of the hit position.</summary>
        public float HitY;

        /// <summary>World-space Z coordinate of the hit position.</summary>
        public float HitZ;
    }
}
