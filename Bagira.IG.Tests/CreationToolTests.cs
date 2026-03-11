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
    /// When a <c>nameResolver</c> is provided it must supply the entity name
    /// instead of the JSON-derived one.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_NameResolver_UsedInsteadOfJsonName()
    {
        var (captured, tool) = CreateToolWithResolver(() => "Generated-5");

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var infoEntry = captured[0].InitialDescriptors
            .First(d => d._d == EDescriptorType.dtEntityInfo);
        Assert.Equal("Generated-5", infoEntry.EntityInfo.Name);
    }

    /// <summary>
    /// The resolver must be called once per click, enabling session-sequential
    /// naming — the second click receives a different name than the first.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_NameResolver_IncrementsBetweenClicks()
    {
        int callIndex = 0;
        var (captured, tool) = CreateToolWithResolver(() => "G-" + ++callIndex);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);
        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal("G-1", captured[0].InitialDescriptors
            .First(d => d._d == EDescriptorType.dtEntityInfo).EntityInfo.Name);
        Assert.Equal("G-2", captured[1].InitialDescriptors
            .First(d => d._d == EDescriptorType.dtEntityInfo).EntityInfo.Name);
    }

    /// <summary>
    /// When no resolver is supplied and <c>initialPropertiesJson</c> contains a
    /// <c>name</c> field the tool must use that JSON-derived name.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_NullNameResolver_FallsBackToJsonName()
    {
        const string jsonName = "MyUnit";
        var (captured, tool) = CreateTool(
            initialPropertiesJson: $"{{\"name\":\"{jsonName}\"}}");

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var infoEntry = captured[0].InitialDescriptors
            .First(d => d._d == EDescriptorType.dtEntityInfo);
        Assert.Equal(jsonName, infoEntry.EntityInfo.Name);
    }
}