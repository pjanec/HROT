using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// Permanent identity component holding the blueprint type key for a networked entity.
    /// Replaces the obsolete <c>NetworkSpawnRequest</c> as the persistent state store.
    ///
    /// This component is attached when an entity is first created (either via local spawn
    /// or ghost creation from a remote EntityMaster packet) and lives on the entity forever.
    ///
    /// The <see cref="GhostPromotionSystem"/> queries for this component (together with
    /// <c>EntityLifecycle.Ghost</c>) to find entities that are ready for promotion.
    /// Because the lifecycle transition from Ghost → Constructing removes the entity from
    /// the promotion query, the "trigger" is consumed implicitly without removing this
    /// component.
    ///
    /// <para>Note: <c>DisType</c> is NOT stored here; it lives natively inside the
    /// 96-byte <see cref="EntityHeader.DisType"/> field of every entity header.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.TkbIdentity)]
    public struct TkbIdentity
    {
        /// <summary>
        /// The TKB template type key.  Must match an entry in <c>ITkbDatabase</c>.
        /// </summary>
        public long TkbType;
    }
}
