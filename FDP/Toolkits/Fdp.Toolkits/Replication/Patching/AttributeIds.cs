using System;

namespace Fdp.Toolkit.Replication.Patching;

/// <summary>
/// Well-known attribute ID constants for the ATTR2 binary wire schema.
/// Each constant maps a human-readable attribute name to the <c>ushort</c> ID
/// transmitted in <c>AttributeRecord.AttributeId</c> over DDS.
///
/// <para><b>Reserved Numeric Range Strategy:</b></para>
/// <list type="table">
///   <listheader><term>Range</term><description>Domain</description></listheader>
///   <item><term>1 – 99</term><description>Core entity data (identity, affiliation, status).</description></item>
///   <item><term>100 – 199</term><description>Geo-spatial / positional attributes (WGS-84 geodetic, orientation).</description></item>
///   <item><term>200 – 499</term><description>Domain-specific core extensions (weapons, sensors, comms).</description></item>
///   <item><term>500+</term><description>Application/project domain extensions — add in companion files (see below).</description></item>
/// </list>
///
/// <para><b>Extension Pattern:</b><br/>
/// Domain projects that need additional IDs should declare a companion static class in their
/// own assembly rather than modifying this file. Example:
/// <code>
/// // In MyDomain project:
/// namespace MyDomain.DdsSchema;
///
/// // Extends the schema without touching FDP core.
/// public static class MyDomainAttributeIds
/// {
///     public const ushort WeaponAmmo = 500;
///     public const ushort RadioFrequency = 501;
/// }
/// </code>
/// </para>
/// </summary>
public static class AttributeIds
{
    // ── Range 1–99: Core entity data ──────────────────────────────────────────────

    /// <summary>
    /// Display name of the entity (<c>IgEntityData.Name</c>).
    /// Value type: <c>AttributeValueType.String</c>.
    /// </summary>
    public const ushort Name = 1;

    /// <summary>
    /// Force/side affiliation (<c>IgEntityData.ForceId</c>).
    /// Value type: <c>AttributeValueType.Int32</c> (maps to the ForceId enum).
    /// </summary>
    public const ushort Affiliation = 2;

    // ── Range 100–199: Geo-spatial / positional ───────────────────────────────────

    /// <summary>
    /// WGS-84 geodetic latitude in decimal degrees (<c>SimTransform</c>).
    /// Value type: <c>AttributeValueType.Float64</c>.
    /// </summary>
    public const ushort GeoLat = 10;

    /// <summary>
    /// WGS-84 geodetic longitude in decimal degrees (<c>SimTransform</c>).
    /// Value type: <c>AttributeValueType.Float64</c>.
    /// </summary>
    public const ushort GeoLon = 11;

    /// <summary>
    /// WGS-84 altitude above the reference ellipsoid in metres (<c>SimTransform</c>).
    /// Value type: <c>AttributeValueType.Float64</c>.
    /// </summary>
    public const ushort GeoAlt = 12;

    /// <summary>
    /// ⭐⭐⭐ <b>Compass heading in degrees — <c>0 = North, 90 = East, clockwise</b></c> — applied to
    /// <c>SimTransform.Rotation</c>. Value type: <c>AttributeValueType.Float64</c>.
    ///
    /// <para>⭐⭐ <b>This constant is the ONLY new thing heading needed.</b> 📐 Measured
    /// <c>2026-08-25</c>: the convention, the conversion and the wire field all already exist —
    /// <c>SimTransformBridgeSystem.HeadingDegToRotation</c> / <c>RotationToHeadingDeg</c>
    /// *(documented with this exact convention)*, <c>EulerOri.Heading</c>, and
    /// <c>GeoSpatialEgressTranslator</c>'s yaw→compass step. ⇒ ⛔ **no new conversion math was written**;
    /// the installer reuses the bridge. 📄 <c>DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §6 ②.</para>
    ///
    /// <para>⚠⚠ <b>That claim was true of the INSTALLER and false of two other callers — corrected by
    /// <c>Q59-C1</c>/<c>F5</c>.</b> 📐 Measured <c>2026-08-26</c>: the JSON <c>"Heading"</c> handler INLINED
    /// the same formula *(harmless — numerically identical)*, and <c>DescriptorMapper</c> had drifted to
    /// <b>a yaw about Y with no compass offset</b>, which disagreed at every heading and pointed straight UP
    /// at <c>h=90</c>. ⭐ All three now CALL <c>HeadingDegToRotation</c>.</para>
    ///
    /// <para>⭐⭐ <b>RENAMED <c>GeoHeading</c> → <c>Heading</c> by <c>Q59-N1</c>.</b> 📐 The old name
    /// advertised a JSON path that does not exist: the route is <c>"Heading"</c>, and
    /// <c>{"GeoPosition":{"Heading":…}}</c> applies <b>nothing, silently</b>. ⚠ The rename is
    /// <b>source-only</b> — the wire carries the <c>ushort</c> <c>13</c> and no <c>.idl</c> names the
    /// constant — whereas renaming the PATH would be a breaking external contract change. 📄 <c>Q59</c> §8.2.</para>
    ///
    /// <para>⚠ <b>Degrees, not radians, and compass, not math-yaw</b> — deliberately the same units the
    /// wire and the DebugApi already use *(<c>headingDeg</c>)*, so nothing on the path has to convert
    /// twice. ⛔ A radians-or-math-yaw id would be a second convention for one concept.</para>
    ///
    /// <para>⚠ <b>Numbering:</b> the class doc reserves 100–199 for geo-spatial, but the shipped
    /// <c>Geo*</c> ids are 10/11/12 — a pre-existing doc/value mismatch. ⭐ This follows the VALUES and
    /// takes <c>13</c>, keeping the family contiguous; ⛔ these ids are a WIRE schema, so renumbering the
    /// existing three to match the prose is not a free edit and is not attempted here.</para>
    /// </summary>
    public const ushort Heading = 13;
}
