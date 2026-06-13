using System.Numerics;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Tests for Blueprint wire reroute support (RR-02):
/// InsertReroute / MoveReroute / RemoveReroute command handling in
/// <see cref="BlueprintCommandSink"/>, waypoint persistence, and the
/// mandatory round-trip test guarding the JSON serialization caveat.
/// </summary>
public sealed class BlueprintRerouteTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an asset with two nodes connected by one link, returns
    /// the asset, graph, and the ids needed to construct test commands.
    /// </summary>
    private static (BlueprintAsset asset, Graph graph, LinkId linkId,
                    Guid fromPinId, Guid toPinId)
        BuildGraphWithLink()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = new Guid("aa000000-0000-0000-0000-000000000001"),
            Name     = "RerouteTestAsset",
            Dispatch = BlueprintDispatchKind.Library,
        };

        var fromPinId = new Guid("aa000000-0001-0001-0001-000000000001");
        var toPinId   = new Guid("aa000000-0002-0002-0002-000000000002");

        var n1 = new FunctionCallNode
        {
            Id   = new Guid("aa000000-0003-0003-0003-000000000003"),
            Pins = { new Pin { Id = fromPinId, Name = "Out", Direction = "Out", IsExec = false,
                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } } },
        };
        var n2 = new FunctionCallNode
        {
            Id   = new Guid("aa000000-0004-0004-0004-000000000004"),
            Pins = { new Pin { Id = toPinId, Name = "In", Direction = "In", IsExec = false,
                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } } },
        };

        var graph = new Graph
        {
            Id    = new Guid("aa000000-0005-0005-0005-000000000005"),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { n1, n2 },
            Links = { new Link { FromNodeId = n1.Id, FromPinId = fromPinId,
                                 ToNodeId   = n2.Id, ToPinId   = toPinId } },
        };
        asset.Graphs.Add(graph);

        var linkId = BlueprintGraphModel.MakeLinkId(fromPinId, toPinId);
        return (asset, graph, linkId, fromPinId, toPinId);
    }

    private static (BlueprintCommandSink sink,
                    BlueprintGraphModel  model,
                    List<BlueprintAsset> dirtyLog)
        MakeSink(BlueprintAsset asset, Graph graph)
    {
        var typeSystem  = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model       = new BlueprintGraphModel(asset, graph);
        var catalog     = new BlueprintNodeCatalog(new NodeKindRegistry());
        var validator   = new BlueprintLinkValidator(model, typeSystem);
        var history     = new CommandHistory();
        var dirtyLog    = new List<BlueprintAsset>();
        var editService = new EditService
        {
            Context = new EditServiceContext(history, a => dirtyLog.Add(a))
        };

        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editService,
            markDirty: a => dirtyLog.Add(a));

        return (sink, model, dirtyLog);
    }

    // ── InsertReroute ─────────────────────────────────────────────────────────

    [Fact]
    public void InsertReroute_KnownLink_AddsWaypointToAssetLink()
    {
        var (asset, graph, linkId, _, _) = BuildGraphWithLink();
        var (sink, _, _) = MakeSink(asset, graph);
        var assetLink = graph.Links[0];

        var result = sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(10f, 20f)));

        Assert.True(result.Success);
        Assert.NotNull(assetLink.Waypoints);
        Assert.Single(assetLink.Waypoints!);
        Assert.Equal(10f, assetLink.Waypoints![0].X, precision: 3);
        Assert.Equal(20f, assetLink.Waypoints![0].Y, precision: 3);
    }

    [Fact]
    public void InsertReroute_KnownLink_MarksAssetDirty()
    {
        var (asset, graph, linkId, _, _) = BuildGraphWithLink();
        var (sink, _, dirtyLog) = MakeSink(asset, graph);

        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(1f, 2f)));

        Assert.Contains(asset, dirtyLog);
    }

    [Fact]
    public void InsertReroute_KnownLink_ModelWaypointsReflectAfterRebuild()
    {
        var (asset, graph, linkId, _, _) = BuildGraphWithLink();
        var (sink, model, _) = MakeSink(asset, graph);

        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(50f, 60f)));

        // After rebuild, the model link must expose the waypoint.
        var modelLink = model.FindLink(linkId);
        Assert.NotNull(modelLink);
        Assert.Single(modelLink!.Waypoints);
        Assert.Equal(50f, modelLink.Waypoints[0].X, precision: 3);
        Assert.Equal(60f, modelLink.Waypoints[0].Y, precision: 3);
    }

    [Fact]
    public void InsertReroute_UnknownLink_SafeNoOp()
    {
        var (asset, graph, _, _, _) = BuildGraphWithLink();
        var (sink, _, dirtyLog) = MakeSink(asset, graph);
        var unknownId = new LinkId(Guid.NewGuid());

        var result = sink.Apply(new GraphCommand.InsertReroute(unknownId, new Vector2(1f, 1f)));

        // Must succeed (safe no-op) and must NOT mark dirty.
        Assert.True(result.Success);
        Assert.Empty(dirtyLog);
    }

    // ── MoveReroute ───────────────────────────────────────────────────────────

    [Fact]
    public void MoveReroute_ExistingIndex_UpdatesWaypoint()
    {
        var (asset, graph, linkId, _, _) = BuildGraphWithLink();
        var (sink, _, _) = MakeSink(asset, graph);
        var assetLink = graph.Links[0];

        // First insert a waypoint.
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(10f, 20f)));

        // Now move it.
        var result = sink.Apply(new GraphCommand.MoveReroute(linkId, 0, new Vector2(99f, 88f)));

        Assert.True(result.Success);
        Assert.NotNull(assetLink.Waypoints);
        Assert.Equal(99f, assetLink.Waypoints![0].X, precision: 3);
        Assert.Equal(88f, assetLink.Waypoints![0].Y, precision: 3);
    }

    [Fact]
    public void MoveReroute_OutOfRangeIndex_SafeNoOp()
    {
        var (asset, graph, linkId, _, _) = BuildGraphWithLink();
        var (sink, _, dirtyLog) = MakeSink(asset, graph);

        // Insert one waypoint, then try to move index 5 (out of range).
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(1f, 1f)));
        var countBefore = dirtyLog.Count;

        var result = sink.Apply(new GraphCommand.MoveReroute(linkId, 5, new Vector2(9f, 9f)));

        Assert.True(result.Success);
        Assert.Equal(countBefore, dirtyLog.Count);  // no new dirty mark
    }

    [Fact]
    public void MoveReroute_UnknownLink_SafeNoOp()
    {
        var (asset, graph, _, _, _) = BuildGraphWithLink();
        var (sink, _, dirtyLog) = MakeSink(asset, graph);
        var unknownId = new LinkId(Guid.NewGuid());

        var result = sink.Apply(new GraphCommand.MoveReroute(unknownId, 0, new Vector2(1f, 1f)));

        Assert.True(result.Success);
        Assert.Empty(dirtyLog);
    }

    // ── RemoveReroute ─────────────────────────────────────────────────────────

    [Fact]
    public void RemoveReroute_ExistingIndex_RemovesWaypoint()
    {
        var (asset, graph, linkId, _, _) = BuildGraphWithLink();
        var (sink, _, _) = MakeSink(asset, graph);
        var assetLink = graph.Links[0];

        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(10f, 20f)));
        Assert.Single(assetLink.Waypoints!);

        var result = sink.Apply(new GraphCommand.RemoveReroute(linkId, 0));

        Assert.True(result.Success);
        Assert.Empty(assetLink.Waypoints!);
    }

    [Fact]
    public void RemoveReroute_OutOfRangeIndex_SafeNoOp()
    {
        var (asset, graph, linkId, _, _) = BuildGraphWithLink();
        var (sink, _, dirtyLog) = MakeSink(asset, graph);

        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(1f, 1f)));
        var countBefore = dirtyLog.Count;

        var result = sink.Apply(new GraphCommand.RemoveReroute(linkId, 99));

        Assert.True(result.Success);
        Assert.Equal(countBefore, dirtyLog.Count);  // no new dirty mark
    }

    [Fact]
    public void RemoveReroute_UnknownLink_SafeNoOp()
    {
        var (asset, graph, _, _, _) = BuildGraphWithLink();
        var (sink, _, dirtyLog) = MakeSink(asset, graph);
        var unknownId = new LinkId(Guid.NewGuid());

        var result = sink.Apply(new GraphCommand.RemoveReroute(unknownId, 0));

        Assert.True(result.Success);
        Assert.Empty(dirtyLog);
    }

    // ── Round-trip (MANDATORY) ────────────────────────────────────────────────

    /// <summary>
    /// MANDATORY ROUND-TRIP GUARD: Serializes a BlueprintAsset that contains a Link with a
    /// waypoint, then deserializes it, and asserts that the waypoint survived with the correct
    /// X/Y values.  This proves that <see cref="LinkWaypoint"/> (which uses float PROPERTIES)
    /// round-trips correctly through <see cref="BlueprintJsonServices"/>, unlike a raw
    /// <see cref="System.Numerics.Vector2"/> whose X/Y are FIELDS — those would serialize to {}
    /// if <c>IncludeFields</c> were false.
    /// </summary>
    [Fact]
    public void LinkWaypoints_SerializeAndDeserialize_WaypointSurvivesRoundTrip()
    {
        var fromPinId = new Guid("bb000000-0001-0001-0001-000000000001");
        var toPinId   = new Guid("bb000000-0002-0002-0002-000000000002");

        var asset = new BlueprintAsset
        {
            AssetId  = new Guid("bb000000-0000-0000-0000-000000000001"),
            Name     = "WaypointRoundTrip",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   =
            [
                new Graph
                {
                    Id    = new Guid("bb000000-0099-0001-0001-000000000001"),
                    Name  = "Main",
                    Kind  = GraphKind.Function,
                    Links =
                    [
                        new Link
                        {
                            FromNodeId = Guid.NewGuid(),
                            FromPinId  = fromPinId,
                            ToNodeId   = Guid.NewGuid(),
                            ToPinId    = toPinId,
                            Waypoints  = new List<LinkWaypoint>
                            {
                                new() { X = 123.5f, Y = 456.75f },
                            },
                        },
                    ],
                },
            ],
        };

        // Serialize → deserialize via the real BlueprintJsonServices.
        var json         = BlueprintJsonServices.Serialize(asset);
        var deserialized = BlueprintJsonServices.Deserialize(json);

        Assert.NotNull(deserialized);

        var deserializedLink = deserialized!.Graphs[0].Links[0];
        Assert.NotNull(deserializedLink.Waypoints);
        Assert.Single(deserializedLink.Waypoints!);
        Assert.Equal(123.5f,  deserializedLink.Waypoints![0].X, precision: 3);
        Assert.Equal(456.75f, deserializedLink.Waypoints![0].Y, precision: 3);

        // Also verify byte-stable: re-serialized JSON must equal the first serialization.
        var json2 = BlueprintJsonServices.Serialize(deserialized);
        Assert.Equal(json, json2);
    }

    /// <summary>
    /// A Link WITHOUT waypoints must still round-trip cleanly (null Waypoints omitted from JSON).
    /// This confirms the WhenWritingNull attribute leaves straight-wire links unchanged.
    /// </summary>
    [Fact]
    public void LinkWithoutWaypoints_RoundTrip_WaypointsStillNull()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = new Guid("bb000000-0000-0000-0000-000000000002"),
            Name     = "NoWaypointRoundTrip",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   =
            [
                new Graph
                {
                    Id    = new Guid("bb000000-0099-0001-0001-000000000002"),
                    Name  = "Main",
                    Kind  = GraphKind.Function,
                    Links =
                    [
                        new Link
                        {
                            FromNodeId = Guid.NewGuid(),
                            FromPinId  = Guid.NewGuid(),
                            ToNodeId   = Guid.NewGuid(),
                            ToPinId    = Guid.NewGuid(),
                            // No waypoints — straight wire
                        },
                    ],
                },
            ],
        };

        var json         = BlueprintJsonServices.Serialize(asset);
        var deserialized = BlueprintJsonServices.Deserialize(json);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized!.Graphs[0].Links[0].Waypoints);

        // JSON must not contain "Waypoints" for a null list.
        Assert.DoesNotContain("Waypoints", json);
    }

    // ── Model projection after rebuild ────────────────────────────────────────

    [Fact]
    public void BlueprintGraphModel_AfterInsertRerouteAndRebuild_WaypointVisibleViaFindLink()
    {
        var (asset, graph, linkId, fromPinId, toPinId) = BuildGraphWithLink();
        var (sink, model, _) = MakeSink(asset, graph);

        // No waypoints initially.
        var beforeLink = model.FindLink(linkId);
        Assert.NotNull(beforeLink);
        Assert.Empty(beforeLink!.Waypoints);

        // Insert a waypoint.
        sink.Apply(new GraphCommand.InsertReroute(linkId, new Vector2(7f, 8f)));

        // Model is rebuilt by the sink; now the waypoint must be visible.
        var afterLink = model.FindLink(linkId);
        Assert.NotNull(afterLink);
        Assert.Single(afterLink!.Waypoints);
        Assert.Equal(7f, afterLink.Waypoints[0].X, precision: 3);
        Assert.Equal(8f, afterLink.Waypoints[0].Y, precision: 3);
    }

    [Fact]
    public void BlueprintGraphModel_FindAssetLink_ReturnsCorrectLink()
    {
        var (asset, graph, linkId, fromPinId, toPinId) = BuildGraphWithLink();
        var model = new BlueprintGraphModel(asset, graph);

        var assetLink = model.FindAssetLink(linkId);

        Assert.NotNull(assetLink);
        Assert.Equal(fromPinId, assetLink!.FromPinId);
        Assert.Equal(toPinId,   assetLink!.ToPinId);
    }

    [Fact]
    public void BlueprintGraphModel_FindAssetLink_UnknownId_ReturnsNull()
    {
        var (asset, graph, _, _, _) = BuildGraphWithLink();
        var model = new BlueprintGraphModel(asset, graph);

        var result = model.FindAssetLink(new LinkId(Guid.NewGuid()));

        Assert.Null(result);
    }
}
