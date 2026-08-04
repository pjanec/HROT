using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// R6 rename (2026-08-04): the five collection-consumer node "kind" tags were renamed
/// Component*->Collection* (see <c>Nodes.cs</c>). These tests pin the legacy-tag-on-read
/// rewrite in <c>BlueprintJsonServices.Deserialize</c> (v1 assets carrying an old tag must
/// keep loading forever) and the new-tag/schemaVersion-2 write path in
/// <c>BlueprintJsonServices.Serialize</c>.
/// </summary>
public sealed class LegacyNodeKindMigrationTests
{
    [Fact]
    public void Deserialize_LegacyComponentItemCountTag_ProducesCollectionItemCountNode()
    {
        var json = """
            {
              "AssetId": "a1000000-0000-0000-0000-000000000001",
              "Name": "LegacyKindTest",
              "Dispatch": "Library",
              "Graphs": [
                {
                  "Id": "a1000000-0000-0000-0000-000000000002",
                  "Name": "Main",
                  "Kind": "Function",
                  "Nodes": [
                    {
                      "kind": "ComponentItemCount",
                      "Id": "a1000000-0000-0000-0000-000000000003",
                      "ComponentTypeFqn": "Hrot.AI.Behaviors.BpCollectionDemo",
                      "CountAccessorFqn": "Hrot.AI.Behaviors.BpCollectionDemoOps.Count"
                    }
                  ]
                }
              ]
            }
            """;

        var result = BlueprintJsonServices.Deserialize(json);

        Assert.NotNull(result);
        var node = Assert.Single(Assert.Single(result!.Graphs).Nodes);
        var itemCount = Assert.IsType<CollectionItemCountNode>(node);
        Assert.Equal("Hrot.AI.Behaviors.BpCollectionDemo", itemCount.ComponentTypeFqn);
        Assert.Equal("Hrot.AI.Behaviors.BpCollectionDemoOps.Count", itemCount.CountAccessorFqn);
    }

    [Fact]
    public void Serialize_CollectionItemCountNode_EmitsNewTagAndSchemaVersion2_AndRoundTrips()
    {
        var asset = new BlueprintAsset
        {
            AssetId = new Guid("a2000000-0000-0000-0000-000000000001"),
            Name    = "SchemaVersionRoundTrip",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs = new List<Graph>
            {
                new Graph
                {
                    Id   = new Guid("a2000000-0000-0000-0000-000000000002"),
                    Name = "Main",
                    Kind = GraphKind.Function,
                    Nodes = new List<Node>
                    {
                        new CollectionItemCountNode
                        {
                            Id = new Guid("a2000000-0000-0000-0000-000000000003"),
                            ComponentTypeFqn = "Hrot.AI.Behaviors.BpCollectionDemo",
                            CountAccessorFqn = "Hrot.AI.Behaviors.BpCollectionDemoOps.Count",
                        },
                    },
                },
            },
        };

        var json = BlueprintJsonServices.Serialize(asset);

        Assert.Contains("\"kind\":\"CollectionItemCount\"", json);
        Assert.DoesNotContain("ComponentItemCount", json);

        var dom  = JsonNode.Parse(json)!.AsObject();
        var meta = JsonEnvelope.Read(dom);
        Assert.Equal(2, meta.SchemaVersion);

        var reparsed = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(reparsed);
        var node = Assert.Single(Assert.Single(reparsed!.Graphs).Nodes);
        Assert.IsType<CollectionItemCountNode>(node);
    }
}
