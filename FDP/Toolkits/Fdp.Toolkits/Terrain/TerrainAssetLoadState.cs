using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// How far this node has got in making a zone's terrain data resident.
    /// </summary>
    public enum LoadPhase : byte
    {
        /// <summary>Nothing has been attempted for this entity on this node.</summary>
        NotLoaded = 0,

        /// <summary>A load is in flight. ⚠ This is a phase and not a bool precisely because streaming is
        /// not instantaneous — a bare "loaded" flag cannot express in-flight.</summary>
        Loading = 1,

        /// <summary>The data for <see cref="TerrainAssetLoadState.SourceHash"/> is resident.</summary>
        Loaded = 2,

        /// <summary>The load was attempted and failed. ⛔ Deliberately distinct from
        /// <see cref="NotLoaded"/>: a failure is the one state an operator must be able to see.</summary>
        Failed = 3,
    }

    /// <summary>
    /// NODE-LOCAL record of whether this node holds the terrain data covering a zone entity, and of the
    /// zone footprint it was loaded for.
    ///
    /// <para><b>⛔ This is NOT entity state — it is node state keyed by entity.</b> It answers
    /// "has THIS node got the data resident", which is legitimately a different answer on every node.
    /// Replicating it would assert one value for a per-node fact. Hence
    /// <c>[DataPolicy(NoScenario | NoReplay)]</c>: it must appear in neither a saved scenario nor a
    /// recording. It is fully re-derivable by re-running the load, so nothing is lost by omitting it —
    /// which is also why it needs neither a TKB home nor a published descriptor.</para>
    ///
    /// <para><b>⭐ Nodes disagreeing is CORRECT, not corruption</b> — transiently while one is still
    /// building, and persistently when one has <see cref="LoadPhase.Failed"/>. A remote failure is
    /// surfaced through the cluster op result, never by replicating this component.</para>
    ///
    /// <para><b>⭐ Why a HASH and not a version counter.</b> <see cref="SourceHash"/> is the resolved
    /// world footprint from <see cref="ZoneFootprint.Compute"/>. A counter requires every mutation path
    /// to cooperate, and the one that exists (<c>EditablePolyline.Version</c>) has already failed that in
    /// production: nothing increments it and the edit tool resets it. A hash requires cooperation from
    /// nobody, and because points are RELATIVE, including the transform is what lets it see a MOVE.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2 (new-type table), §9.1, §9.7 ③c.
    /// </summary>
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    [ComponentId(GlobalComponentIds.TerrainAssetLoadState)]
    public struct TerrainAssetLoadState
    {
        /// <summary>How far this node has got. See <see cref="LoadPhase"/>.</summary>
        public LoadPhase Phase;

        /// <summary>
        /// The footprint hash the resident data was loaded for. A reader compares it against the
        /// entity's CURRENT footprint: equal ⇒ fresh, different ⇒ <b>stale</b>.
        /// <para>⚠ Stale means DEGRADED FIDELITY, not corruption — the base terrain is always present and
        /// a zone only adds resolution. It must never gate an exercise start or a save; it drives the
        /// "needs loading" badge and the "load all stale" action.</para>
        /// </summary>
        public ulong SourceHash;
    }
}
