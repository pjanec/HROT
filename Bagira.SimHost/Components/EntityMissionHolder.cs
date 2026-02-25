using Bagira.BDC.SSTD;

namespace Bagira.SimHost.Components
{
    /// <summary>
    /// Managed ECS component that holds a DDS <see cref="EntityMission"/> value.
    ///
    /// <para>
    /// <see cref="EntityMission"/> is a <c>partial struct</c> whose <c>MissionPlan.Tasks</c>
    /// field is a <c>List&lt;MissionTask&gt;</c> — a managed reference type.  That prevents
    /// it from satisfying the <c>unmanaged</c> constraint required by the ECS native-chunk
    /// storage tier.  This thin container lets the ECS managed-component tier (Tier 2)
    /// carry the struct without duplicating or redefining any of its fields.
    /// </para>
    ///
    /// <para>
    /// Always access the mission data through <see cref="Mission"/>; do not recreate this
    /// type's field structure in SimHost.
    /// </para>
    /// </summary>
    public sealed class EntityMissionHolder
    {
        /// <summary>
        /// The DDS <see cref="EntityMission"/> payload for this entity.
        /// </summary>
        public EntityMission Mission;
    }
}
