using Bagira.BDC.SSTD;
using Bagira.DDS.DM;

namespace Bagira.BDC.SSTM
{
    /// <summary>
    /// Patch Object pattern DTO representing all possible entity property overrides.
    ///
    /// <para>
    /// Uses nullable types so only explicitly set fields are applied, leaving
    /// TKB defaults or existing values intact for unset fields. This provides
    /// compile-time property-name safety and eliminates ad-hoc dictionary or
    /// anonymous-object allocations in the IOS.
    /// </para>
    ///
    /// <para>
    /// <b>IOS serialization:</b> Serialize to JSON with
    /// <c>NullValueHandling.Ignore</c>; pass as <c>initialPropertiesJson</c>
    /// in <c>MapCommandRequest.CommandArgsJson</c>.
    /// Deserialize on the IG side (e.g. in the tool callback) with
    /// <c>JsonConvert.DeserializeObject&lt;EntityPropertyPatch&gt;(json)</c>.
    /// </para>
    ///
    /// <para>
    /// <b>SimHost creation path:</b> Passed as <see cref="Bagira.BDC.SSTM.CreateEntityRequest.InitialAttributes"/>
    /// entries via <see cref="EntityAttributeCompilerExtensions"/>; the
    /// <c>EntityAttributeCompiler</c> merges these into the spawned entity's
    /// ECS components after TKB defaults and descriptor overrides have been applied.
    /// </para>
    /// </summary>
    public class EntityPropertyPatch
    {
        /// <summary>
        /// Human-readable entity name override (mapped to <c>EntityInfo.Name</c>).
        /// When null, the TKB default name is preserved.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Force affiliation override (mapped to <c>EntityInfo.ForceIdentifier</c>).
        /// When null, the TKB default affiliation is preserved.
        /// </summary>
        public eForceIdentifier? Affiliation { get; set; }

        /// <summary>
        /// Geographic position override (mapped to <c>GeoSpatial.Pos</c>).
        /// When set in <c>InitialAttributes</c>, overrides the position derived from
        /// the <c>dtGeoSpatial</c> descriptor. Useful for programmatic entity creation
        /// where no operator map-click is involved.
        /// </summary>
        public GeoPosition? GeoPosition { get; set; }

        // ── Auto-name generation ──────────────────────────────────────────────

        /// <summary>
        /// When <c>true</c>, the IG automatically generates a unique sequential name
        /// for each entity created during the placement session.
        /// E.g. <c>NamePrefix="Tank-"</c> → "Tank-1", "Tank-2", …
        /// </summary>
        public bool? AutogenerateName { get; set; }

        /// <summary>
        /// Prefix used when <see cref="AutogenerateName"/> is <c>true</c>.
        /// When null or empty, the IG falls back to the TKB template name followed by a hyphen.
        /// E.g. template "M1 Abrams" → prefix "M1 Abrams-".
        /// </summary>
        public string? NamePrefix { get; set; }
    }
}
