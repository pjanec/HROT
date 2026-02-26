using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Abstractions;
using Bagira.IG.Components;
using Bagira.IG.Tools;
using Raylib_cs;

namespace Bagira.IG.Tests;

/// <summary>
/// Unit tests for <see cref="CreationTool"/> (TASK-IF006).
///
/// Validates that the tool writes a correctly-formed <see cref="CreateEntityRequest"/>
/// to the <see cref="IDdsWriter{T}"/> when the operator left-clicks the map canvas,
/// and that right-click cancels without writing.
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

    // ── CapturingDdsWriter<T> stub ────────────────────────────────────────────

    /// <summary>
    /// Test double that records every sample passed to <see cref="Write"/>.
    /// </summary>
    private sealed class CapturingDdsWriter<T> : IDdsWriter<T>
    {
        public List<T> Written { get; } = new List<T>();
        public void Write(T sample) => Written.Add(sample);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CapturingDdsWriter<CreateEntityRequest> CreateWriter()
        => new CapturingDdsWriter<CreateEntityRequest>();

    // ── Left-click publishes exactly one DDS request ──────────────────────────

    /// <summary>
    /// A left-click must call <see cref="IDdsWriter{T}.Write"/> exactly once,
    /// confirming that the request is sent over DDS (not via the local event bus).
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_WritesExactlyOneRequest()
    {
        var writer = CreateWriter();
        var tool   = new CreationTool(writer, tkbType: TestTkbType);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Single(writer.Written);
    }

    /// <summary>
    /// The published request must have a non-empty <see cref="CreateEntityRequest.RequestId"/>
    /// so responses can be correlated by the SimHost.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_RequestHasNonEmptyRequestId()
    {
        var writer = CreateWriter();
        var tool   = new CreationTool(writer, tkbType: TestTkbType);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.NotEqual(Guid.Empty, writer.Written[0].RequestId);
    }

    /// <summary>
    /// <see cref="CreateEntityRequest.Owner"/> must be the zeroed <see cref="NodeId"/>
    /// so the SimHost (authoritative node) assigns itself as owner, consistent with the
    /// ghost-node convention.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_RequestOwnerIsZeroedNodeId()
    {
        var writer = CreateWriter();
        var tool   = new CreationTool(writer, tkbType: TestTkbType);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.Equal(default(NodeId), writer.Written[0].Owner);
    }

    /// <summary>
    /// <see cref="CreateEntityRequest.InitialDescriptors"/> must contain a
    /// <c>dtEntityMaster</c> entry carrying the TKB type supplied at construction.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_InitialDescriptorsContainEntityMasterWithCorrectTkbType()
    {
        var writer = CreateWriter();
        var tool   = new CreationTool(writer, tkbType: TestTkbType);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var descriptors = writer.Written[0].InitialDescriptors;
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
        var writer = CreateWriter();
        var tool   = new CreationTool(writer, tkbType: TestTkbType);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        var descriptors = writer.Written[0].InitialDescriptors;
        Assert.NotNull(descriptors);
        var geoEntry = descriptors.FirstOrDefault(d => d._d == EDescriptorType.dtGeoSpatial);
        Assert.Equal(EDescriptorType.dtGeoSpatial, geoEntry._d);
        Assert.Equal(ClickY, geoEntry.GeoSpatial.Pos.Latitude,  precision: 3);
        Assert.Equal(ClickX, geoEntry.GeoSpatial.Pos.Longitude, precision: 3);
    }

    /// <summary>
    /// The <see cref="OnCommandPublished"/> event must fire once with the same request
    /// that was written to DDS, enabling test and debug integrators to observe spawning.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_RaisesOnCommandPublishedWithSamePayload()
    {
        var writer = CreateWriter();
        var tool   = new CreationTool(writer, tkbType: TestTkbType);

        CreateEntityRequest? captured = null;
        tool.OnCommandPublished += req => captured = req;

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Left);

        Assert.NotNull(captured);
        Assert.Equal(writer.Written[0].RequestId, captured!.Value.RequestId);
    }

    // ── Right-click does NOT write ────────────────────────────────────────────

    /// <summary>
    /// A right-click must not call <see cref="IDdsWriter{T}.Write"/> — it cancels
    /// the placement without sending any DDS request.
    /// </summary>
    [Fact]
    public void HandleClick_RightClick_DoesNotWriteToDds()
    {
        var writer = CreateWriter();
        var tool   = new CreationTool(writer, tkbType: TestTkbType);

        tool.HandleClick(new Vector2(ClickX, ClickY), MouseButton.Right);

        Assert.Empty(writer.Written);
    }

    // ── Default TKB type fallback ─────────────────────────────────────────────

    /// <summary>
    /// Passing <c>tkbType = 0</c> falls back to
    /// <see cref="CreationToolConstants.DefaultTkbType"/> in the EntityMaster descriptor.
    /// </summary>
    [Fact]
    public void Ctor_TkbTypeZero_UsesDefaultTkbType()
    {
        var writer = CreateWriter();
        var tool   = new CreationTool(writer, tkbType: 0);

        tool.HandleClick(Vector2.Zero, MouseButton.Left);

        var masterEntry = writer.Written[0].InitialDescriptors
            .First(d => d._d == EDescriptorType.dtEntityMaster);
        Assert.Equal(CreationToolConstants.DefaultTkbType, masterEntry.EntityMaster.TkbType);
    }
}

