using System.Numerics;
using System.Text.Json;
using Hrot.NED.Descriptors;
using Hrot.SimHost.Installers;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Core;
using Fdp.Modules.Geographic;

namespace Hrot.SimHost;

/// <summary>
/// Builds the application-wide <see cref="JsonAttributeCompiler"/> singleton used by
/// <see cref="Systems.CreateEntityRequestSystem"/> and
/// <see cref="Hrot.Map.Common.Systems.UpdateEntityAttributeRequestSystem"/>.
///
/// <para>
/// Registers the following JSON attribute paths:
/// <list type="bullet">
///   <item><c>"Name"</c> → <see cref="Fdp.Core.EntityInfo.Name"/> (ordinal: <c>dtEntityInfo</c>)</item>
///   <item><c>"Affiliation"</c> → <see cref="Fdp.Core.EntityInfo.ForceId"/> (ordinal: <c>dtEntityInfo</c>)</item>
///   <item><c>"GeoPosition.Latitude"</c>, <c>"GeoPosition.Longitude"</c>, <c>"GeoPosition.Altitude"</c>
///         → <see cref="Fdp.Toolkit.Replication.Components.SimTransform.Position"/> via
///         <c>IGeographicTransform.ToCartesian</c> (ordinal: <c>dtGeoSpatial</c>).
///         Registered only when <paramref name="geoTransform"/> is non-null.</item>
/// </list>
/// </para>
/// </summary>
public static class AttributeCompilerFactory
{
    private const long EntityInfoOrdinal  = (long)EDescriptorType.dtEntityInfo;
    private const long GeoSpatialOrdinal  = (long)EDescriptorType.dtWorldPos;

    // ── Public factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs an immutable <see cref="JsonAttributeCompiler"/> with the standard SimHost
    /// attribute routing table.
    /// </summary>
    /// <param name="geoTransform">
    /// Geographic transform used to convert geodetic coordinates to local Cartesian space.
    /// When <c>null</c>, <c>GeoPosition</c> leaf paths are not registered and geo-position
    /// patches are silently ignored.
    /// </param>
    public static JsonAttributeCompiler Build(IGeographicTransform? geoTransform)
    {
        var builder = new AttributeCompilerBuilder()

            // ── IgEntityData — unmanaged struct paths ───────────────────────────────────
            .RegisterValuePath<Fdp.Core.EntityInfo>(
                "Name",
                ( ref Fdp.Core.EntityInfo c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r ) =>
                    c.Name = r.GetString() ?? string.Empty,
                descriptorOrdinal: EntityInfoOrdinal )

            .RegisterValuePath<Fdp.Core.EntityInfo>(
                "Affiliation",
                ( ref Fdp.Core.EntityInfo c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r ) =>
                    c.ForceId = r.TokenType == JsonTokenType.Number
                        ? MapAffiliationInt( r.GetInt32())
                        : MapAffiliationString( r.GetString()),
                descriptorOrdinal: EntityInfoOrdinal );

        // ── SimTransform — unmanaged struct paths (GeoPosition) ───────────────
        if (geoTransform != null)
        {
            var acc = new GeoCoordAccumulator(geoTransform);

            builder
                .RegisterValuePath<SimTransform>(
                    "GeoPosition.Latitude",
                    (ref SimTransform st, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    {
                        acc.Lat = r.GetDouble();
                        acc.TryApply(ref st);
                    },
                    descriptorOrdinal: GeoSpatialOrdinal)

                .RegisterValuePath<SimTransform>(
                    "GeoPosition.Longitude",
                    (ref SimTransform st, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    {
                        acc.Lon = r.GetDouble();
                        acc.TryApply(ref st);
                    },
                    descriptorOrdinal: GeoSpatialOrdinal)

                .RegisterValuePath<SimTransform>(
                    "GeoPosition.Altitude",
                    (ref SimTransform st, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    {
                        acc.Alt = r.GetDouble();
                        acc.TryApply(ref st);
                    },
                    descriptorOrdinal: GeoSpatialOrdinal);
        }

        // ── SimTransform — Heading (compass degrees, always registered) ───────
        builder.RegisterValuePath<SimTransform>(
            "Heading",
            (ref SimTransform st, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
            {
                float headingDeg  = (float)r.GetDouble();
                float mathYawRad  = (90f - headingDeg) * (MathF.PI / 180f);
                st.Rotation       = System.Numerics.Quaternion.CreateFromAxisAngle(
                    System.Numerics.Vector3.UnitZ, mathYawRad);
            },
            descriptorOrdinal: GeoSpatialOrdinal);

        return builder.Build();
    }

    /// <summary>
    /// Constructs an immutable <see cref="JsonToRecordCompiler"/> with the standard SimHost
    /// binary attribute routing schema.
    /// </summary>
    /// <remarks>
    /// Registers the same five paths as <see cref="Build"/> so that the JSON→ECS and
    /// JSON→Binary pipelines stay in perfect sync.
    /// </remarks>
    public static JsonToRecordCompiler BuildEdgeCompiler()
    {
        return new JsonToRecordCompilerBuilder()
            .Register("Name",                   AttributeIds.Name,        AttributeValueKind.CsString)
            .Register("Affiliation",             AttributeIds.Affiliation,  AttributeValueKind.CsString)
            .Register("GeoPosition.Latitude",   AttributeIds.GeoLat,      AttributeValueKind.CsFloat64)
            .Register("GeoPosition.Longitude",  AttributeIds.GeoLon,      AttributeValueKind.CsFloat64)
            .Register("GeoPosition.Altitude",   AttributeIds.GeoAlt,      AttributeValueKind.CsFloat64)
            .Build();
    }

    /// <summary>
    /// Constructs a <see cref="BinaryInterpreter"/> configured with the standard SimHost
    /// domain installers.
    /// </summary>
    /// <param name="geoTransform">
    /// Geographic transform used by <see cref="SimTransformAttributeInstaller"/> to convert
    /// geodetic coordinates to Cartesian space.  When <c>null</c>,
    /// <see cref="SimTransformAttributeInstaller"/> is not added and geo-position attribute
    /// records are silently ignored.
    /// </param>
    public static BinaryInterpreter<EntityAttributeChange> BuildBinaryInterpreter(IGeographicTransform? geoTransform)
    {
        var builder = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
            .AddInstaller(new EntityDataAttributeInstaller())
            // ⭐⭐ Axis-B item ② — heading. ⚠ Added UNCONDITIONALLY, unlike the position installer:
            //    📐 it needs no IGeographicTransform, because a compass heading is already in the units
            //    SimTransformBridgeSystem.HeadingDegToRotation takes. ⇒ ⭐ heading works on a host with no
            //    geo transform, and gating it behind one would refuse rotation for no reason.
            .AddInstaller(new SimTransformHeadingInstaller());

        if (geoTransform != null)
            builder.AddInstaller(new SimTransformAttributeInstaller(geoTransform));

        return builder.Build();
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Maps a JSON affiliation string (e.g. <c>"FORCE_FRIENDLY"</c>) to a <see cref="ForceId"/>.
    /// Unrecognised values map to <see cref="ForceId.Neutral"/>.
    /// </summary>
    internal static ForceId MapAffiliationString(string? value) =>
        value switch
        {
            "FORCE_FRIENDLY" => ForceId.Friend,
            "FORCE_OPPOSING" => ForceId.Hostile,
            "FORCE_NEUTRAL"  => ForceId.Neutral,
            _                => ForceId.Neutral,
        };

    /// <summary>
    /// Maps a JSON affiliation integer (the raw <see cref="eForceIdentifier"/> ordinal) to a
    /// <see cref="ForceId"/>. Handles the ExCon default JSON serialisation which emits enums
    /// as their underlying integer value (e.g. <c>2</c> for <c>FORCE_OPPOSING</c>).
    /// </summary>
    internal static ForceId MapAffiliationInt(int value) =>
        (eForceIdentifier)value switch
        {
            eForceIdentifier.FORCE_FRIENDLY => ForceId.Friend,
            eForceIdentifier.FORCE_OPPOSING => ForceId.Hostile,
            eForceIdentifier.FORCE_NEUTRAL  => ForceId.Neutral,
            _                               => ForceId.Neutral,
        };

    /// <summary>
    /// Accumulates individual GeoPosition leaf values (Latitude, Longitude, Altitude) and
    /// fires the Cartesian coordinate conversion only when all three are available.
    /// Resets automatically after a successful conversion to prepare for the next entity.
    /// </summary>
    /// <remarks>
    /// A single accumulator instance is captured by all three GeoPosition delegates registered
    /// in <see cref="Build"/>. The delegates fire sequentially within a single
    /// <c>JsonAttributeCompiler.Compile</c> call, so there is no concurrency concern.
    /// Partial updates (fewer than three fields) are silently deferred; the final value applied
    /// is always the result of the last complete Latitude/Longitude/Altitude triple received.
    ///
    /// TODO ATTR-BATCH-04: The coordinate math relies on a non-linear WGS84 transformation.
    /// Because the Earth is curved, "Altitude" does not simply map to the Cartesian Z axis.
    /// If an entity moves far from the tangent origin, its "Up" vector tilts.
    /// Thus, to apply a partial update (e.g., just changing the Altitude), we cannot just offset
    /// Z. We must first perform an inverse calculation: get the current Cartesian Position, 
    /// convert it to Geodetic (Lat/Lon/Alt) via ToGeodetic, overwrite the provided coordinate(s), 
    /// and then run ToCartesian again to get the final Cartesian Position.
    /// </remarks>
    private sealed class GeoCoordAccumulator
    {
        private readonly IGeographicTransform _geo;

        public double? Lat { get; set; }
        public double? Lon { get; set; }
        public double? Alt { get; set; }

        public GeoCoordAccumulator(IGeographicTransform geo) => _geo = geo;

        /// <summary>
        /// Attempts to apply the accumulated coordinates to <paramref name="st"/>.
        /// Fires only when all three (Lat, Lon, Alt) are non-null, then resets.
        /// </summary>
        public void TryApply(ref SimTransform st)
        {
            if (!Lat.HasValue || !Lon.HasValue || !Alt.HasValue)
                return;

            var cart = _geo.ToCartesian(Lat.Value, Lon.Value, Alt.Value);
            st.Position = new Vector3((float)cart.X, (float)cart.Y, (float)cart.Z);

            // Reset for subsequent entities.
            Lat = Lon = Alt = null;
        }
    }
}
