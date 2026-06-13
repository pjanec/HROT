using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using FluentAssertions;
using Fbt;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.BTree;

/// <summary>
/// RR-03: Tests that waypoints on BTreeEditorNode survive round-trips through
/// the DTO mapper (ToDto → ToModel) and JSON serialization (Serialize → Deserialize).
/// Also confirms SampleScout (no waypoints) is byte-stable after the change.
/// </summary>
public sealed class BTreeWaypointRoundTripTests
{
    // ---- Helpers ------------------------------------------------------------

    private static BehaviorTreeBlob EmptyBlob() => new BehaviorTreeBlob
    {
        TreeName        = "test",
        Nodes           = Array.Empty<NodeDefinition>(),
        MethodNames     = Array.Empty<string>(),
        FloatParams     = Array.Empty<float>(),
        IntParams       = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };

    /// <summary>
    /// Builds a minimal asset with one Sequence node that has two waypoints.
    /// Returns the asset and the node's VisualId.
    /// </summary>
    private static (BehaviorTreeAsset asset, Guid nodeVisualId) MakeAssetWithWaypoints()
    {
        var assetId = new Guid("aaaaaaaa-0001-0002-0003-000000000001");
        var asset = new BehaviorTreeAsset(
            assetId, "WaypointTree", "/WaypointTree.cs", true,
            "BB", "Ctx", EmptyBlob());

        var node = new BTreeEditorNode
        {
            VisualId     = new Guid("bbbbbbbb-0001-0002-0003-000000000002"),
            KernelType   = NodeType.Sequence,
            DisplayLabel = "Seq",
            Position     = new Vector2(100f, 200f),
            KernelBlobIndex = -1,
        };
        node.Waypoints.Add(new Vector2(10f, 20f));
        node.Waypoints.Add(new Vector2(30f, 40f));

        asset.AddNode(node);
        return (asset, node.VisualId);
    }

    // ---- ToDto / ToModel round-trip -----------------------------------------

    [Fact]
    public void NodeToDto_WithWaypoints_WaypointsAreMapped()
    {
        var (asset, nodeId) = MakeAssetWithWaypoints();

        var dto = BehaviorTreeAssetMapper.ToDto(asset);

        var nodeDto = dto.Nodes.Single(n => n.VisualId == nodeId);
        nodeDto.EditorMetadata.Waypoints.Should().NotBeNull();
        nodeDto.EditorMetadata.Waypoints!.Should().HaveCount(2);
        nodeDto.EditorMetadata.Waypoints[0].X.Should().BeApproximately(10f, 0.001f);
        nodeDto.EditorMetadata.Waypoints[0].Y.Should().BeApproximately(20f, 0.001f);
        nodeDto.EditorMetadata.Waypoints[1].X.Should().BeApproximately(30f, 0.001f);
        nodeDto.EditorMetadata.Waypoints[1].Y.Should().BeApproximately(40f, 0.001f);
    }

    [Fact]
    public void NodeToDto_WithNoWaypoints_WaypointsIsNull()
    {
        var asset = new BehaviorTreeAsset(
            Guid.NewGuid(), "NoWpTree", "/NoWpTree.cs", true,
            "BB", "Ctx", EmptyBlob());
        var node = new BTreeEditorNode
        {
            VisualId     = Guid.NewGuid(),
            KernelType   = NodeType.Sequence,
            DisplayLabel = "Seq",
            KernelBlobIndex = -1,
        };
        asset.AddNode(node);

        var dto = BehaviorTreeAssetMapper.ToDto(asset);

        dto.Nodes.Single().EditorMetadata.Waypoints
            .Should().BeNull("empty waypoints must not serialize");
    }

    [Fact]
    public void ToDtoToModel_WaypointsSurviveRoundTrip()
    {
        var (asset, nodeId) = MakeAssetWithWaypoints();

        var dto      = BehaviorTreeAssetMapper.ToDto(asset);
        var restored = BehaviorTreeAssetMapper.FromDto(dto);
        var restoredNode = restored.FindNode(nodeId)!;

        restoredNode.Waypoints.Should().HaveCount(2, "both waypoints must survive ToDto→ToModel");
        restoredNode.Waypoints[0].Should().Be(new Vector2(10f, 20f));
        restoredNode.Waypoints[1].Should().Be(new Vector2(30f, 40f));
    }

    [Fact]
    public void ToDtoToModel_NodeWithNoWaypoints_WaypointsStayEmpty()
    {
        var asset = new BehaviorTreeAsset(
            Guid.NewGuid(), "NoWpTree", "/NoWpTree.cs", true,
            "BB", "Ctx", EmptyBlob());
        var nodeId = Guid.NewGuid();
        var node = new BTreeEditorNode
        {
            VisualId     = nodeId,
            KernelType   = NodeType.Sequence,
            DisplayLabel = "Seq",
            KernelBlobIndex = -1,
        };
        asset.AddNode(node);

        var dto      = BehaviorTreeAssetMapper.ToDto(asset);
        var restored = BehaviorTreeAssetMapper.FromDto(dto);

        restored.FindNode(nodeId)!.Waypoints.Should().BeEmpty();
    }

    // ---- JSON serialize / deserialize round-trip ----------------------------

    [Fact]
    public void JsonSerializeDeserialize_WaypointsSurviveRoundTrip()
    {
        var (asset, nodeId) = MakeAssetWithWaypoints();
        var dto = BehaviorTreeAssetMapper.ToDto(asset);

        var json     = BTreeJsonServices.Serialize(dto);
        var restored = BTreeJsonServices.Deserialize(json);

        restored.Should().NotBeNull();
        var nodeDto = restored!.Nodes.Single(n => n.VisualId == nodeId);
        nodeDto.EditorMetadata.Waypoints.Should().NotBeNull();
        nodeDto.EditorMetadata.Waypoints!.Should().HaveCount(2);
        nodeDto.EditorMetadata.Waypoints[0].X.Should().BeApproximately(10f, 0.001f);
        nodeDto.EditorMetadata.Waypoints[0].Y.Should().BeApproximately(20f, 0.001f);
        nodeDto.EditorMetadata.Waypoints[1].X.Should().BeApproximately(30f, 0.001f);
        nodeDto.EditorMetadata.Waypoints[1].Y.Should().BeApproximately(40f, 0.001f);
    }

    [Fact]
    public void JsonSerializeDeserialize_NodeWithNoWaypoints_WaypointsFieldAbsent()
    {
        // A node without waypoints must not emit a "Waypoints" key in JSON.
        var asset = new BehaviorTreeAsset(
            Guid.NewGuid(), "NoWpTree", "/NoWpTree.cs", true,
            "BB", "Ctx", EmptyBlob());
        var node = new BTreeEditorNode
        {
            VisualId     = Guid.NewGuid(),
            KernelType   = NodeType.Sequence,
            DisplayLabel = "Seq",
            KernelBlobIndex = -1,
        };
        asset.AddNode(node);

        var dto  = BehaviorTreeAssetMapper.ToDto(asset);
        var json = BTreeJsonServices.Serialize(dto);

        // "Waypoints" must not appear in the JSON output for a node with no waypoints.
        json.Should().NotContain("\"Waypoints\"",
            "empty waypoint list must be omitted (WhenWritingNull) to preserve byte-identity");
    }
}
