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

    // ── Geo-spatial / positional ──────────────────────────────────────────────────

    /// <summary>
    /// ⛔ RESERVED — was <c>GeoLat</c> (WGS-84 latitude, degrees), retired by <c>CE-4073</c> (AQ107 B's tail, R-4092): every position the
    /// attribute route carries is now LOCAL ENU metres (<see cref="PositionX"/>/<see cref="PositionY"/>/<see cref="PositionZ"/>), the
    /// frame the NED wire already speaks. ⚠ Do NOT reuse 10 — a stale sender would be read as the wrong quantity.
    /// </summary>
    public const ushort RetiredGeoLat = 10;

    /// <summary>⛔ RESERVED — was <c>GeoLon</c> (WGS-84 longitude, degrees), retired by <c>CE-4073</c>. ⚠ Do NOT reuse 11.</summary>
    public const ushort RetiredGeoLon = 11;

    /// <summary>⛔ RESERVED — was <c>GeoAlt</c> (WGS-84 altitude, metres), retired by <c>CE-4073</c>. ⚠ Do NOT reuse 12.</summary>
    public const ushort RetiredGeoAlt = 12;

    /// <summary>
    /// ⭐⭐ <c>CE-4073</c> — local ENU <b>east</b> metres: <c>SimTransform.Position.X</c>. Value type: <c>Float64</c>.
    /// The three axes are independent plain copies — a patch carrying only <c>Position.X</c> moves the entity east and leaves its
    /// north and altitude alone — and they exist on EVERY host (nothing to convert with, so nothing to gate on).
    /// </summary>
    public const ushort PositionX = 14;

    /// <summary>⭐⭐ <c>CE-4073</c> — local ENU <b>north</b> metres: <c>SimTransform.Position.Y</c>. <c>Float64</c>. See <see cref="PositionX"/>.</summary>
    public const ushort PositionY = 15;

    /// <summary>⭐⭐ <c>CE-4073</c> — local ENU <b>up</b> metres: <c>SimTransform.Position.Z</c>. <c>Float64</c>. See <see cref="PositionX"/>.</summary>
    public const ushort PositionZ = 16;

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
    /// <c>{"Position":{"Heading":…}}</c> applies <b>nothing, silently</b>. ⚠ The rename is
    /// <b>source-only</b> — the wire carries the <c>ushort</c> <c>13</c> and no <c>.idl</c> names the
    /// constant — whereas renaming the PATH would be a breaking external contract change. 📄 <c>Q59</c> §8.2.</para>
    ///
    /// <para>⚠ <b>Degrees, not radians, and compass, not math-yaw</b> — deliberately the same units the
    /// wire and the DebugApi already use *(<c>headingDeg</c>)*, so nothing on the path has to convert
    /// twice. ⛔ A radians-or-math-yaw id would be a second convention for one concept.</para>
    ///
    /// <para>⚠ <b>Numbering:</b> the class doc reserves 100–199 for geo-spatial, but the shipped
    /// position ids sit at 10–16 — a pre-existing doc/value mismatch. ⭐ This follows the VALUES and
    /// takes <c>13</c>, keeping the family contiguous; ⛔ these ids are a WIRE schema, so renumbering
    /// them to match the prose is not a free edit and is not attempted. <c>CE-4073</c> retired 10–12
    /// and added <see cref="PositionX"/>–<see cref="PositionZ"/> at 14–16.</para>
    /// </summary>
    public const ushort Heading = 13;

    // ── CE-3216 S1-0 — the scenario clock (R-3012, W-8; docs/designs/weather/DESIGN_Weather_And_Time.md) ─────────────────
    // ⚠ In the 1–99 "core" range, beside the entity data: a scenario-level attribute of the World Environment, not a domain extension.

    /// <summary>
    /// ⭐ The scenario START TIME — <c>ScenarioClock.StartUtcTicks</c> (UTC ticks at sim time 0). Value type: <c>Int64</c>. A runtime
    /// edit simply sets a new start time, applied as sent by the clock's owner (W-8).
    /// </summary>
    public const ushort ScenarioStartUtcTicks = 20;

    /// <summary>⭐ The clock's display offset from UTC in minutes — <c>ScenarioClock.DisplayUtcOffsetMinutes</c>. Value type: <c>Int32</c>.</summary>
    public const ushort ScenarioUtcOffsetMinutes = 21;

    // ── CE-4024 — a civilian's fright (AQ93 §15 K5, CV-12; docs/DESIGN_Civilians_And_Crowd_Control.md) ──────────────────────
    // ⭐ How a SUMO pedestrian converted on the traffic node reaches the brain node already in panic: CivilianState is brain-local
    //   and never replicated, so the creation request carries it as attributes. Also an operator's way to frighten a civilian.

    /// <summary>⭐ <c>CivilianState.Fear</c>, 0 (calm) … 1 (terrified). Value type: <c>Float64</c>.</summary>
    public const ushort CivilianFear = 30;

    /// <summary>⭐ <c>CivilianState.FrightFrom.X</c> — where the fright came from (world metres); sets <c>HasFrightFrom</c>. <c>Float64</c>.</summary>
    public const ushort CivilianFrightFromX = 31;

    /// <summary>⭐ <c>CivilianState.FrightFrom.Y</c>. See <see cref="CivilianFrightFromX"/>.</summary>
    public const ushort CivilianFrightFromY = 32;

    /// <summary>⭐ <c>CivilianState.FrightFrom.Z</c>. See <see cref="CivilianFrightFromX"/>.</summary>
    public const ushort CivilianFrightFromZ = 33;
}
