using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐⭐⭐ <b><c>Q59-A1′</c> — the ONE declaration of what an entity attribute IS.</b>
///
/// <para>📄 <c>docs/blueprints/Architect_Question_59_Attribute_Vocabulary_Single_Source.md</c> §7.2 · §9.</para>
///
/// <para>🔒 <b>User ruling, <c>2026-08-26</c>:</b> attributes are *"entity-related, network agnostic"*.
/// ⇒ ⛔ <b>no descriptor ordinal here.</b> A descriptor is a NED grouping; the join between the two worlds is
/// the ECS <b>component</b>, and <see cref="ComponentDescriptorMap"/> supplies the rest.</para>
///
/// <para>⭐⭐⭐ <b>WHAT DERIVES FROM THIS, AND WHAT HONESTLY CANNOT.</b> ⚠ Stated plainly, because the first
/// cut of this design claimed more than is true:</para>
///
/// <list type="bullet">
///   <item>⭐⭐⭐ <b>DERIVED — the edge table</b> *(<c>AttributeCompilerFactory.BuildEdgeCompiler</c>)*. It is
///   pure metadata: path → id + kind, nothing else. 📌 <b>And it is the table that actually drifted</b> —
///   <c>AX-018</c>'s missing <c>Heading</c> row. ⇒ it can no longer be missing a row, by construction.</item>
///   <item>⭐⭐ <b>DERIVED — the published schema</b> *(<c>JsonAttributeCompiler.ExportSchema</c>)*, which now
///   knows the real type of every path instead of calling everything a string.</item>
///   <item>⛔ <b>NOT DERIVED — the JSON setters and the binary handlers.</b> 📐 Measured: each carries
///   <b>per-attribute logic</b> *(parse this token, write that field, share a geo accumulator)* with two
///   different delegate signatures. ⇒ ⭐ <b>they are not redundancy, they are distinct code</b>, and folding
///   them into one record would produce a worse thing than the three tables. ⚠ They are instead
///   <b>cross-checked</b> against this list by <c>TheFourRoutingTablesAgreeTests</c>.</item>
/// </list>
///
/// <para>⛔⛔ <b>Adding an attribute means adding a row HERE FIRST.</b> The edge table and the schema then
/// follow automatically, and the rails red until the JSON setter and binary handler exist too. 📌 That is the
/// <c>UXI-30</c>/<c>AX-001</c> shape: the half-registration <c>AX-018</c> found is now unrepresentable for
/// the metadata, and loudly detected for the logic.</para>
/// </summary>
public static class AttributeVocabulary
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE VOCABULARY.</b> ⚠ Six entries — ⛔ do not add one without the JSON setter in
    /// <see cref="AttributeCompilerFactory.Build"/> and the handler in an installer.
    /// </summary>
    public static readonly IReadOnlyList<AttributeDefinition> All = new AttributeDefinition[]
    {
        new("Name",                  AttributeIds.Name,        AttributeValueKind.CsString,  typeof(EntityInfo)),

        // ⚠ Declared CsString, and a NUMBER is equally valid: ExCon's default enum serialisation emits `2`
        //   rather than "FORCE_OPPOSING". ⭐ AX-018 made the edge compiler honour the token, so both cross.
        new("Affiliation",           AttributeIds.Affiliation, AttributeValueKind.CsString,  typeof(EntityInfo)),

        // ⚠ RequiresGeoTransform: without an IGeographicTransform there is nothing to convert geodetic
        //   coordinates WITH, so these three are not registered on the ECS side at all.
        new("GeoPosition.Latitude",  AttributeIds.GeoLat,      AttributeValueKind.CsFloat64, typeof(SimTransform), RequiresGeoTransform: true),
        new("GeoPosition.Longitude", AttributeIds.GeoLon,      AttributeValueKind.CsFloat64, typeof(SimTransform), RequiresGeoTransform: true),
        new("GeoPosition.Altitude",  AttributeIds.GeoAlt,      AttributeValueKind.CsFloat64, typeof(SimTransform), RequiresGeoTransform: true),

        // ⭐⭐ Heading needs NO geo transform — a compass heading is already in the units the conversion takes
        //    — so it is registered unconditionally. 📌 Q59-N1 renamed the id from GeoHeading, which had
        //    advertised a "GeoPosition.Heading" path that does not exist.
        new("Heading",               AttributeIds.Heading,     AttributeValueKind.CsFloat64, typeof(SimTransform)),
    };

    /// <summary>⭐ The definitions active on a host, given whether it has a geographic transform.</summary>
    public static IEnumerable<AttributeDefinition> For(bool hasGeoTransform)
        => All.Where(d => hasGeoTransform || !d.RequiresGeoTransform);

    /// <summary>
    /// ⭐⭐ The distinct ECS components the attribute path writes.
    ///
    /// <para>⭐⭐⭐ This is the set <see cref="ComponentDescriptorMap"/> must cover, and the reason the
    /// coverage rail can exist at all: ⛔ an attribute whose component no translator covers can be applied
    /// and will never be republished — the <c>AX-015</c> failure, generalised.</para>
    /// </summary>
    public static IEnumerable<Type> WrittenComponents => All.Select(d => d.Component).Distinct();
}

/// <summary>
/// ⭐⭐ One entity attribute, network-agnostically. ⛔ No descriptor ordinal — see
/// <see cref="AttributeVocabulary"/>.
/// </summary>
/// <param name="JsonPath">
/// ⭐⭐⭐ The authoring name, and an <b>EXTERNAL CONTRACT</b> — ExCon, the debug API and authoring JSON all
/// write it. ⛔ Renaming one is a breaking change; 📌 <c>Q59-N1</c> renamed the C# <i>constant</i> instead,
/// precisely because the constant's name is source-only while this is on the wire.
/// </param>
/// <param name="AttributeId">The binary wire id. ⭐ The VALUE is the contract; the constant's name is not.</param>
/// <param name="Kind">
/// ⚠ The <b>expected</b> kind — it selects the numeric width, ⛔ it does not override the JSON token's
/// category *(<c>AX-018</c>)*.
/// </param>
/// <param name="Component">⭐⭐ The ECS component written. This is the JOIN to the descriptor world.</param>
/// <param name="RequiresGeoTransform">⭐ True when the path is unregisterable without an <c>IGeographicTransform</c>.</param>
public sealed record AttributeDefinition(
    string JsonPath,
    ushort AttributeId,
    AttributeValueKind Kind,
    Type Component,
    bool RequiresGeoTransform = false);
