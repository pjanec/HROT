using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using Bagira.IG.Tools;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Patching;
using Raylib_cs;

namespace Bagira.IG.Tests;

/// <summary>
/// Unit tests for <see cref="CreationTool"/> (TASK-IF006).
///
/// Validates that the tool invokes the <c>onEntityCreated</c> delegate with a
/// correctly-formed <see cref="CreateEntityRequest"/> when the operator left-clicks
/// the map canvas, and that right-click cancels without invoking the delegate.
///
/// No Raylib window context is required — <see cref="CreationTool.HandleClick"/>
/// operates purely on in-memory state; <c>_canvas?.PopTool()</c> is null-safe when
/// <c>OnEnter</c> has not been called.
/// </summary>
public class CreationToolTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const long  TestTkbType = 202L;
    private const float ClickX      = 1234.5f;
    private const float ClickY      = 5678.9f;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a capture list and a <see cref="CreationTool"/> that appends each
    /// published request to it. Returns both so tests can assert on the list.
    /// </summary>
    private static (List<CreateEntityRequest> captured, CreationTool tool)
        CreateTool(long tkbType = TestTkbType, IGeographicTransform? geoTransform = null,
                   string? initialPropertiesJson = null)
    {
        var captured = new List<CreateEntityRequest>();
        var tool     = new CreationTool(
            req => captured.Add(req),
            geoTransform:          geoTransform,
            tkbType:               tkbType,
            initialPropertiesJson: initialPropertiesJson);
        return (captured, tool);
    }

    // ── Left-click publishes exactly one DDS request ──────────────────────────

    /// <summary>
    /// A left-click must invoke the delegate exactly once,
    /// confirming that the request is routed (not dropped silently).
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_WritesExactlyOneRequest()
    {
        var (captured, tool) = CreateTool();

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Single(captured);
    }

    /// <summary>
    /// The published request must have a non-empty <see cref="CreateEntityRequest.RequestId"/>
    /// so responses can be correlated by the SimHost.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_RequestHasNonEmptyRequestId()
    {
        var (captured, tool) = CreateTool();

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.NotEqual(Guid.Empty, captured[0].RequestId);
    }

    /// <summary>
    /// <see cref="CreateEntityRequest.Owner"/> must be the zeroed <see cref="NodeId"/>
    /// so the SimHost (authoritative node) assigns itself as owner, consistent with the
    /// ghost-node convention.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_RequestOwnerIsZeroedNodeId()
    {
        var (captured, tool) = CreateTool();

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal(default(NodeId), captured[0].Owner);
    }

    /// <summary>
    /// <see cref="CreateEntityRequest.InitialDescriptors"/> must contain a
    /// <c>dtEntityMaster</c> entry carrying the TKB type supplied at construction.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_InitialDescriptorsContainEntityMasterWithCorrectTkbType()
    {
        var (captured, tool) = CreateTool();

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var descriptors = captured[0].InitialDescriptors;
        Assert.NotNull(descriptors);
        var masterEntry = descriptors.FirstOrDefault(d => d._d == EDescriptorType.dtEntityMaster);
        Assert.Equal(EDescriptorType.dtEntityMaster, masterEntry._d);
        Assert.Equal(TestTkbType, masterEntry.EntityMaster.TkbType);
    }

    /// <summary>
    /// <see cref="CreateEntityRequest.InitialDescriptors"/> must contain a
    /// <c>dtGeoSpatial</c> entry with <c>Latitude = worldPos.Y</c> and
    /// <c>Longitude = worldPos.X</c>, matching the FDP canvas coordinate convention.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_InitialDescriptorsContainGeoSpatialWithClickCoordinates()
    {
        var (captured, tool) = CreateTool();

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var descriptors = captured[0].InitialDescriptors;
        Assert.NotNull(descriptors);
        var geoEntry = descriptors.FirstOrDefault(d => d._d == EDescriptorType.dtGeoSpatial);
        Assert.Equal(EDescriptorType.dtGeoSpatial, geoEntry._d);
        Assert.Equal(ClickY, geoEntry.GeoSpatial.Pos.Latitude,  precision: 3);
        Assert.Equal(ClickX, geoEntry.GeoSpatial.Pos.Longitude, precision: 3);
    }

    /// <summary>
    /// The <see cref="OnCommandPublished"/> event must fire once with the same request
    /// that was passed to the delegate, enabling test and debug integrators to observe
    /// spawning without capturing the delegate's list.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_RaisesOnCommandPublishedWithSamePayload()
    {
        var (captured, tool) = CreateTool();

        CreateEntityRequest? observed = null;
        tool.OnCommandPublished += req => observed = req;

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.NotNull(observed);
        Assert.Equal(captured[0].RequestId, observed!.Value.RequestId);
    }

    // ── Right-click does NOT publish ──────────────────────────────────────────

    /// <summary>
    /// A right-click must not invoke the delegate — it cancels
    /// the placement without sending any request.
    /// </summary>
    [Fact]
    public void HandleClick_RightClick_DoesNotWriteToDds()
    {
        var (captured, tool) = CreateTool();

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Right);

        Assert.Empty(captured);
    }

    // ── Default TKB type fallback ─────────────────────────────────────────────

    /// <summary>
    /// Passing <c>tkbType = 0</c> falls back to
    /// <see cref="CreationToolConstants.DefaultTkbType"/> in the EntityMaster descriptor.
    /// </summary>
    [Fact]
    public void Ctor_TkbTypeZero_UsesDefaultTkbType()
    {
        var (captured, tool) = CreateTool(tkbType: 0);

        tool.HandleClick(Vector2.Zero, MouseButton.Left);

        var masterEntry = captured[0].InitialDescriptors
            .First(d => d._d == EDescriptorType.dtEntityMaster);
        Assert.Equal(CreationToolConstants.DefaultTkbType, masterEntry.EntityMaster.TkbType);
    }

    //  Geo transform coordinate conversion 

    /// <summary>
    /// Stub <see cref="IGeographicTransform"/> that always returns predetermined
    /// lat/lon values, making tests deterministic without real WGS84 math.
    /// </summary>
    private sealed class FixedResultGeoTransform : IGeographicTransform
    {
        private readonly double _lat;
        private readonly double _lon;

        public FixedResultGeoTransform(double lat, double lon)
        {
            _lat = lat;
            _lon = lon;
        }

        public void SetOrigin(double latDeg, double lonDeg, double altMeters) { }
        public Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters) => Vector3.Zero;
        public (double lat, double lon, double alt) ToGeodetic(Vector3 localPos) => (_lat, _lon, 0.0);
    }

    /// <summary>
    /// When an <see cref="IGeographicTransform"/> is provided the GeoSpatial
    /// descriptor must carry the converted lat/lon from the transform, NOT the
    /// raw world-space metres.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_WithGeoTransform_UsesConvertedCoordinates()
    {
        const double ExpectedLat = 52.501;
        const double ExpectedLon = 13.402;

        var geo              = new FixedResultGeoTransform(ExpectedLat, ExpectedLon);
        var (captured, tool) = CreateTool(geoTransform: geo);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var descriptors = captured[0].InitialDescriptors;
        var geoEntry    = descriptors.First(d => d._d == EDescriptorType.dtGeoSpatial);
        Assert.Equal(ExpectedLat, geoEntry.GeoSpatial.Pos.Latitude,  precision: 4);
        Assert.Equal(ExpectedLon, geoEntry.GeoSpatial.Pos.Longitude, precision: 4);
    }

    /// <summary>
    /// Without a geo transform (null) the descriptor must fall back to using
    /// <c>worldPos.Y</c> as latitude and <c>worldPos.X</c> as longitude (offline
    /// / test mode behaviour).
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_WithoutGeoTransform_FallsBackToRawCoordinates()
    {
        var (captured, tool) = CreateTool(); // no geoTransform

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var descriptors = captured[0].InitialDescriptors;
        var geoEntry    = descriptors.First(d => d._d == EDescriptorType.dtGeoSpatial);
        Assert.Equal(ClickY, geoEntry.GeoSpatial.Pos.Latitude,  precision: 3);
        Assert.Equal(ClickX, geoEntry.GeoSpatial.Pos.Longitude, precision: 3);
    }

    // ── nameResolver ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="CreationTool"/> with a custom name-resolver delegate,
    /// using no JSON properties.
    /// </summary>
    private static (List<CreateEntityRequest> captured, CreationTool tool)
        CreateToolWithResolver(Func<string> nameResolver, long tkbType = TestTkbType)
    {
        var captured = new List<CreateEntityRequest>();
        var tool     = new CreationTool(
            req => captured.Add(req),
            tkbType:      tkbType,
            nameResolver: nameResolver);
        return (captured, tool);
    }

    /// <summary>
    /// With the dumb-pipe design the <c>nameResolver</c> delegate is retained for
    /// future use but no longer affects the outgoing descriptor list.  The request
    /// must still fire without throwing and <see cref="CreateEntityRequest.InitialAttributesJson"/>
    /// is <c>null</c> because no <c>initialPropertiesJson</c> was supplied.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_NameResolver_DoesNotThrowAndInitialAttributesJsonIsNull()
    {
        var (captured, tool) = CreateToolWithResolver(() => "Generated-5");

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Single(captured);
        Assert.Null(captured[0].InitialAttributesJson);
    }

    /// <summary>
    /// With the dumb-pipe design the resolver delegate is invoked on each click
    /// (retained for future wiring) but does not affect the outgoing message.
    /// Verify that two successive clicks still fire two requests using the same
    /// <c>InitialAttributesJson = null</c> payload.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_NameResolver_TwoClicksProduceTwoRequests()
    {
        int callIndex = 0;
        var (captured, tool) = CreateToolWithResolver(() => "G-" + ++callIndex);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);
        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal(2, captured.Count);
        // InitialAttributesJson is null in both because no initialPropertiesJson was supplied.
        Assert.Null(captured[0].InitialAttributesJson);
        Assert.Null(captured[1].InitialAttributesJson);
    }

    /// <summary>
    /// When no resolver is supplied <c>InitialAttributesJson</c> carries the
    /// raw <c>initialPropertiesJson</c> string verbatim (dumb-pipe forwarding).
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_NullNameResolver_InitialAttributesJsonForwardedVerbatim()
    {
        const string json = "{\"name\":\"MyUnit\"}";
        var (captured, tool) = CreateTool(
            initialPropertiesJson: json);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal(json, captured[0].InitialAttributesJson);
    }

    // ── ATTR-S2T1: dumb-pipe descriptor / payload tests ──────────────────────

    /// <summary>
    /// After ATTR-S2T1 the <c>InitialDescriptors</c> list must contain exactly
    /// two entries — <c>dtEntityMaster</c> and <c>dtGeoSpatial</c>.
    /// <c>dtEntityInfo</c> must no longer be included.
    /// </summary>
    [Fact]
    public void CreationTool_EmitsOnly_EntityMaster_And_GeoSpatial_Descriptors()
    {
        var (captured, tool) = CreateTool();
        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var descriptors = captured[0].InitialDescriptors;
        Assert.Equal(2, descriptors.Count);
        Assert.Contains(descriptors, d => d._d == EDescriptorType.dtEntityMaster);
        Assert.Contains(descriptors, d => d._d == EDescriptorType.dtGeoSpatial);
        Assert.DoesNotContain(descriptors, d => d._d == EDescriptorType.dtEntityInfo);
    }

    /// <summary>
    /// When <c>initialPropertiesJson</c> is supplied at construction time, the
    /// tool forwards it verbatim as <see cref="CreateEntityRequest.InitialAttributesJson"/>.
    /// </summary>
    [Fact]
    public void CreationTool_SetsInitialAttributesJson_FromInitialPropertiesJson()
    {
        const string json = "{\"name\":\"Alpha\",\"affiliation\":\"FORCE_FRIENDLY\"}";
        var (captured, tool) = CreateTool(initialPropertiesJson: json);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal(json, captured[0].InitialAttributesJson);
    }

    /// <summary>
    /// When no <c>initialPropertiesJson</c> is supplied, <c>InitialAttributesJson</c>
    /// must be <c>null</c> — the tool must not synthesise a default value.
    /// </summary>
    [Fact]
    public void CreationTool_InitialAttributesJson_IsNull_WhenNoPropertiesJson()
    {
        var (captured, tool) = CreateTool(); // no initialPropertiesJson

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Null(captured[0].InitialAttributesJson);
    }

    /// <summary>
    /// <c>ParseAffiliationFromJson</c> must still be called during construction so
    /// the ghost-entity colour is correct even though the descriptor list no longer
    /// carries <c>dtEntityInfo</c>.  Verify by inspecting the private
    /// <c>_affiliationForDisplay</c> field via reflection.
    /// </summary>
    [Fact]
    public void CreationTool_GhostColor_StillReflectsAffiliation()
    {
        const string json = "{\"affiliation\":\"FORCE_FRIENDLY\"}";
        var captured = new List<CreateEntityRequest>();
        var tool = new CreationTool(
            req => captured.Add(req),
            initialPropertiesJson: json);

        var field = typeof(CreationTool).GetField(
            "_affiliationForDisplay",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        var affiliation = (ForceId)field!.GetValue(tool)!;
        Assert.Equal(ForceId.Friend, affiliation);
    }

    // ── ATTR2-P6T1: EdgeCompiler injection ────────────────────────────────────

    // Attribute IDs must match AT2 schema (AttributeIds.Name=1, GeoLat=10, GeoLon=11, GeoAlt=12).
    private const ushort AttrName    = 1;
    private const ushort AttrGeoLat  = 10;
    private const ushort AttrGeoLon  = 11;
    private const ushort AttrGeoAlt  = 12;

    /// <summary>
    /// Builds a minimal edge compiler that recognises "Name", "GeoPosition.Latitude",
    /// "GeoPosition.Longitude", and "GeoPosition.Altitude" paths.
    /// </summary>
    private static JsonToRecordCompiler BuildTestEdgeCompiler()
        => new JsonToRecordCompilerBuilder()
            .Register("Name",                  AttrName,   AttributeValueType.KindString)
            .Register("GeoPosition.Latitude",  AttrGeoLat, AttributeValueType.KindFloat64)
            .Register("GeoPosition.Longitude", AttrGeoLon, AttributeValueType.KindFloat64)
            .Register("GeoPosition.Altitude",  AttrGeoAlt, AttributeValueType.KindFloat64)
            .Build();

    /// <summary>
    /// Creates a <see cref="CreationTool"/> with an injected edge compiler and returns
    /// the captured requests and the tool.
    /// </summary>
    private static (List<CreateEntityRequest> captured, CreationTool tool) CreateToolWithEdgeCompiler(
        string? initialPropertiesJson,
        JsonToRecordCompiler edgeCompiler)
    {
        var captured = new List<CreateEntityRequest>();
        var tool     = new CreationTool(
            req => captured.Add(req),
            tkbType:               TestTkbType,
            initialPropertiesJson: initialPropertiesJson,
            edgeCompiler:          edgeCompiler);
        return (captured, tool);
    }

    /// <summary>
    /// Without an edge compiler the tool must use the legacy JSON path:
    /// <c>InitialAttributesJson</c> is set and <c>InitialAttributeRecords</c> is null.
    /// </summary>
    [Fact]
    public void CreationTool_WithoutEdgeCompiler_UsesLegacyJsonPath()
    {
        const string json = "{\"Name\":\"Alpha\"}";
        var (captured, tool) = CreateTool(initialPropertiesJson: json);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Single(captured);
        Assert.Equal(json, captured[0].InitialAttributesJson);
        Assert.Null(captured[0].InitialAttributeRecords);
    }

    /// <summary>
    /// With an edge compiler the tool must publish binary records and clear
    /// <c>InitialAttributesJson</c> to null.
    /// </summary>
    [Fact]
    public void CreationTool_WithEdgeCompiler_PublishesBinaryRecordsAndNullJson()
    {
        const string json = "{\"Name\":\"BinaryAlpha\"}";
        var compiler         = BuildTestEdgeCompiler();
        var (captured, tool) = CreateToolWithEdgeCompiler(json, compiler);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Single(captured);
        var req = captured[0];

        // InitialAttributesJson must be null on the binary-only wire.
        Assert.Null(req.InitialAttributesJson);

        // InitialAttributeRecords must be non-null.
        Assert.NotNull(req.InitialAttributeRecords);
    }

    /// <summary>
    /// With a JSON fixture containing 1 registered path ("Name"), the compiler must
    /// emit exactly 1 binary record with the correct attribute ID and value.
    /// </summary>
    [Fact]
    public void CreationTool_WithEdgeCompiler_RecordCountMatchesRegisteredPaths()
    {
        const string json = "{\"Name\":\"Gamma\"}";
        var compiler         = BuildTestEdgeCompiler();
        var (captured, tool) = CreateToolWithEdgeCompiler(json, compiler);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var records = captured[0].InitialAttributeRecords!;
        Assert.Single(records);
        Assert.Equal(AttrName, records[0].AttributeId);
        Assert.Equal(AttributeValueType.KindString, records[0].Value.ValueType);
        Assert.Equal("Gamma", records[0].Value.StringValue);
    }

    /// <summary>
    /// A JSON fixture with 3 registered geo paths must produce exactly 3 binary records.
    /// </summary>
    [Fact]
    public void CreationTool_WithEdgeCompiler_ThreeGeoPaths_ProducesThreeRecords()
    {
        const string json = "{\"GeoPosition\":{\"Latitude\":32.0,\"Longitude\":35.0,\"Altitude\":150.0}}";
        var compiler         = BuildTestEdgeCompiler();
        var (captured, tool) = CreateToolWithEdgeCompiler(json, compiler);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var records = captured[0].InitialAttributeRecords!;
        Assert.Equal(3, records.Count);

        var latRecord = records.First(r => r.AttributeId == AttrGeoLat);
        Assert.Equal(32.0, latRecord.Value.DoubleValue);
        var lonRecord = records.First(r => r.AttributeId == AttrGeoLon);
        Assert.Equal(35.0, lonRecord.Value.DoubleValue);
        var altRecord = records.First(r => r.AttributeId == AttrGeoAlt);
        Assert.Equal(150.0, altRecord.Value.DoubleValue);
    }
}