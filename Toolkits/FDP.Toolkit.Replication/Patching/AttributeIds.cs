using System;

namespace FDP.Toolkit.Replication.Patching;

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
}
