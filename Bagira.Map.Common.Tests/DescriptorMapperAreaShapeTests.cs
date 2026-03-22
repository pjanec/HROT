using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using Bagira.Map.Common.Replication.Utils;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;

namespace Bagira.Map.Common.Tests;

/// <summary>
/// Tests for <see cref="DescriptorMapper.MapToComponents"/> with <c>dtMapVisualOverlay</c>
/// descriptors (OC1-B003 — Tactical Shape Authoring: Shape Position Wrong).
///
/// <para>Verifies that the coordinate contract is correct end-to-end: vertices stored by
/// <c>ActivateAreaAuthoringTool</c> as <em>relative geodetic offsets</em> from the centroid
/// are reconstructed into <em>relative Cartesian offsets</em> from the entity's
/// <see cref="SimTransform"/> position. When the rendering system adds
/// <c>SimTransform.Position + EditablePolyline.Points[i]</c>, the result must equal the
/// original absolute Cartesian position of vertex <em>i</em>.</para>
/// </summary>
public class DescriptorMapperAreaShapeTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Simple flat-earth geo transform: lon→X, lat→Y, alt→Z (metres).
    /// Round-trips are exact by construction and produce easy-to-verify numbers.
    /// </summary>
    private sealed class FlatGeoTransform : IGeographicTransform
    {
        public void SetOrigin(double latDeg, double lonDeg, double altMeters) { }

        public Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters)
            => new Vector3((float)lonDeg, (float)latDeg, (float)altMeters);

        public (double lat, double lon, double alt) ToGeodetic(Vector3 localPos)
            => (localPos.Y, localPos.X, localPos.Z);
    }

    /// <summary>
    /// Builds a descriptor list that mirrors what <c>ActivateAreaAuthoringTool</c> emits:
    /// <list type="bullet">
    ///   <item><c>dtGeoSpatial</c> with centroid (mean of absPositions).</item>
    ///   <item><c>dtMapVisualOverlay</c> with relative-geo offsets
    ///         (absPos[i] - centroid).</item>
    /// </list>
    /// </summary>
    private static List<EntityDescriptorUnion> BuildAreaDescriptors(
        IReadOnlyList<(double lat, double lon)> absPositions)
    {
        double refLat = 0, refLon = 0;
        foreach (var p in absPositions) { refLat += p.Item1; refLon += p.Item2; }
        refLat /= absPositions.Count;
        refLon /= absPositions.Count;

        var relGeoPoints = new List<GeoPosition>(absPositions.Count);
        foreach (var p in absPositions)
            relGeoPoints.Add(new GeoPosition { Latitude = p.Item1 - refLat, Longitude = p.Item2 - refLon });

        return new List<EntityDescriptorUnion>
        {
            new EntityDescriptorUnion
            {
                _d         = EDescriptorType.dtGeoSpatial,
                GeoSpatial = new GeoSpatial { Pos = new GeoPosition { Latitude = refLat, Longitude = refLon } }
            },
            new EntityDescriptorUnion
            {
                _d               = EDescriptorType.dtMapVisualOverlay,
                MapVisualOverlay = new MapVisualOverlay { Points = relGeoPoints }
            },
        };
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// OC1-B003 SC1 — core contract verification:
    /// Given absolute vertex positions encoded as relative geo offsets from the centroid,
    /// <see cref="DescriptorMapper.MapToComponents"/> must produce relative Cartesian offsets
    /// such that <c>SimTransform.Position + relCart[i] == ToCartesian(absPos[i])</c>.
    /// </summary>
    [Fact]
    public void MapToComponents_AreaShape_ProducesCorrectRelativeCartesianOffsets()
    {
        var geo = new FlatGeoTransform();
        var absPositions = new (double lat, double lon)[]
        {
            (lat: 10.0, lon: 20.0),
            (lat: 12.0, lon: 22.0),
            (lat: 11.0, lon: 21.0),
        };

        var descriptors = BuildAreaDescriptors(absPositions);
        var components  = DescriptorMapper.MapToComponents(descriptors, geo);

        var simTransform = components.OfType<SimTransform>().Single();
        var polyline     = components.OfType<EditablePolyline>().Single();

        Assert.Equal(absPositions.Length, polyline.Points.Count);

        for (int i = 0; i < absPositions.Length; i++)
        {
            var expectedAbsCart = geo.ToCartesian(absPositions[i].lat, absPositions[i].lon, 0.0);
            var reconstructedAbsCart = simTransform.Position + new Vector3(polyline.Points[i].X, polyline.Points[i].Y, 0f);

            Assert.Equal(expectedAbsCart.X, reconstructedAbsCart.X, precision: 4);
            Assert.Equal(expectedAbsCart.Y, reconstructedAbsCart.Y, precision: 4);
        }
    }

    /// <summary>
    /// Centroid of a 3-vertex polygon must equal the arithmetic mean of the vertex positions.
    /// The <c>SimTransform.Position</c> produced by <see cref="DescriptorMapper.MapToComponents"/>
    /// must match the centroid Cartesian coordinates.
    /// </summary>
    [Fact]
    public void MapToComponents_AreaShape_SimTransformPositionIsCentroid()
    {
        var geo = new FlatGeoTransform();
        var absPositions = new (double lat, double lon)[]
        {
            (lat: 50.0, lon: 10.0),
            (lat: 51.0, lon: 11.0),
            (lat: 52.0, lon: 12.0),
        };

        var descriptors = BuildAreaDescriptors(absPositions);
        var components  = DescriptorMapper.MapToComponents(descriptors, geo);

        var simTransform = components.OfType<SimTransform>().Single();

        // Centroid = mean of absolute positions.
        var expectedCentroid = geo.ToCartesian(
            (50.0 + 51.0 + 52.0) / 3,
            (10.0 + 11.0 + 12.0) / 3,
            0.0);

        Assert.Equal(expectedCentroid.X, simTransform.Position.X, precision: 4);
        Assert.Equal(expectedCentroid.Y, simTransform.Position.Y, precision: 4);
    }

    /// <summary>
    /// When a <c>dtMapVisualOverlay</c> descriptor has no paired <c>dtGeoSpatial</c>
    /// descriptor, the fallback path must not throw and must still produce an
    /// <see cref="EditablePolyline"/> (even if the positions are in origin-relative space).
    /// </summary>
    [Fact]
    public void MapToComponents_AreaShape_WithoutGeoSpatial_FallbackDoesNotThrow()
    {
        var geo = new FlatGeoTransform();
        var descriptors = new List<EntityDescriptorUnion>
        {
            new EntityDescriptorUnion
            {
                _d               = EDescriptorType.dtMapVisualOverlay,
                MapVisualOverlay = new MapVisualOverlay
                {
                    Points = new List<GeoPosition>
                    {
                        new GeoPosition { Latitude = 1.0, Longitude = 2.0 },
                        new GeoPosition { Latitude = 3.0, Longitude = 4.0 },
                    }
                }
            },
        };

        var ex         = Record.Exception(() => DescriptorMapper.MapToComponents(descriptors, geo));
        var components = DescriptorMapper.MapToComponents(descriptors, geo);
        var polyline   = components.OfType<EditablePolyline>().Single();

        Assert.Null(ex);
        Assert.Equal(2, polyline.Points.Count);
    }

    /// <summary>
    /// Repeated authoring consistency (OC1-B003 SC3 analogue):
    /// Five different vertex configurations must each produce correct relative-Cartesian
    /// offsets with no positional drift.
    /// </summary>
    [Theory]
    [InlineData(0.0,  0.0,  1.0,  1.0,  0.5,  0.5)]
    [InlineData(49.0, 9.0,  50.0, 10.0, 51.0, 11.0)]
    [InlineData(-10.0, -20.0, -9.0, -19.0, -8.0, -18.0)]
    public void MapToComponents_AreaShape_ThreeVertices_AllAtCorrectPosition(
        double lat0, double lon0,
        double lat1, double lon1,
        double lat2, double lon2)
    {
        var geo = new FlatGeoTransform();
        var absPositions = new (double lat, double lon)[] { (lat0, lon0), (lat1, lon1), (lat2, lon2) };

        var descriptors = BuildAreaDescriptors(absPositions);
        var components  = DescriptorMapper.MapToComponents(descriptors, geo);

        var simTransform = components.OfType<SimTransform>().Single();
        var polyline     = components.OfType<EditablePolyline>().Single();

        for (int i = 0; i < absPositions.Length; i++)
        {
            var expected     = geo.ToCartesian(absPositions[i].lat, absPositions[i].lon, 0.0);
            var reconstructed = simTransform.Position + new Vector3(polyline.Points[i].X, polyline.Points[i].Y, 0f);

            Assert.Equal(expected.X, reconstructed.X, precision: 4);
            Assert.Equal(expected.Y, reconstructed.Y, precision: 4);
        }
    }
}
