using System;
using System.IO;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Tests.Squad;

public sealed class SquadPrimitiveNodeTests
{
    // ---- SC-P6-02-1: Catalog has 4 node entries in Squad/Primitives category ----

    [Fact]
    public void SquadPrimitiveNodeCatalog_HasFourEntriesInSquadCategory()
    {
        var entries = SquadPrimitiveNodeCatalog.Entries;
        Assert.Equal(4, entries.Length);
        Assert.All(entries, e => Assert.Equal("Squad/Primitives", e.Category));
        Assert.Contains(entries, e => e.Kind == "PartitionElements");
        Assert.Contains(entries, e => e.Kind == "AssignRoles");
        Assert.Contains(entries, e => e.Kind == "AdvancePhase");
        Assert.Contains(entries, e => e.Kind == "AcquireSlot");
    }

    // ---- SC-P6-02-1b: All 4 node types are JSON-serializable with correct kind discriminator ----

    [Fact]
    public void SquadPrimitiveNodes_JsonRoundTrip_PreservesKindDiscriminator()
    {
        var nodes = new Node[]
        {
            new PartitionElementsNode { Id = Guid.NewGuid(), ElementCount = 2 },
            new AssignRolesNode       { Id = Guid.NewGuid(), ManeuverKind = 2 },
            new AdvancePhaseNode      { Id = Guid.NewGuid(), AbortPhaseId = 3, DwellTimeoutTicks = 0 },
            new AcquireSlotNode       { Id = Guid.NewGuid(), TotalSlots = 6 },
        };

        var options = new System.Text.Json.JsonSerializerOptions();
        foreach (var node in nodes)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(node, options);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<Node>(json, options);
            Assert.NotNull(deserialized);
            Assert.Equal(node.GetType(), deserialized.GetType());
        }
    }

    // ---- SC-P6-02-2: Worked example Blueprint JSON loads and contains squad primitive nodes ----

    [Fact]
    public void BoundingOverwatchSwap_Blueprint_LoadsAndContainsSquadNodes()
    {
        // Load the worked example Blueprint JSON.
        var jsonPath = Path.Combine(
            Path.GetDirectoryName(typeof(SquadPrimitiveNodeTests).Assembly.Location)!,
            "TestAssets", "Recipes", "BoundingOverwatchSwap.bp.json");
        var json = File.ReadAllText(jsonPath);

        var asset = System.Text.Json.JsonSerializer.Deserialize<BlueprintAsset>(json);
        Assert.NotNull(asset);

        var graph = Assert.Single(asset.Graphs);
        Assert.Equal("SwapOnBound", graph.Name);

        // Verify squad primitive nodes are present in the graph.
        Assert.Contains(graph.Nodes, n => n is AdvancePhaseNode);
        Assert.Contains(graph.Nodes, n => n is AssignRolesNode a && a.ManeuverKind == 2);
    }
}
