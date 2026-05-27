using System;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Blackboard;

/// <summary>
/// Tests for PruneStaleAliasBindings and GetKnownSubAssetIds on HsmAsset.
/// Corrective tests for BATCH-10 P2 gap (Issue 1).
/// </summary>
public sealed class HsmPruneStaleBindingsTests
{
    // ---- Helpers ------------------------------------------------------------

    private static HsmAsset MakeAsset(string name = "TestMachine")
    {
        var builder  = new HsmBuilder(name);
        builder.State("Idle").Initial();
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flat     = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flat);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        return HsmAssetProjector.Project(blob, metadata, null, Guid.NewGuid(), name, "", false, "");
    }

    private static BlackboardAliasBinding MakeBinding(Guid requiringAssetId, Guid requiringElementId) =>
        new(
            RequiringAssetId:   requiringAssetId,
            RequiringElementId: requiringElementId,
            RequiringAssetName: "SomeAsset",
            RequiredByPath:     "SomeAsset > Node#1",
            DtoType:            typeof(float));

    // ---- PruneStaleAliasBindings_RemovesBindings_ForUnknownRequiringAsset --

    [Fact]
    public void PruneStaleAliasBindings_RemovesBindings_ForUnknownRequiringAsset()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("speed", typeof(float), null));

        var assetIdA = Guid.NewGuid();
        var assetIdB = Guid.NewGuid();
        var bindingA = MakeBinding(assetIdA, Guid.NewGuid());
        var bindingB = MakeBinding(assetIdB, Guid.NewGuid());

        asset.AddAlias("speed", bindingA);
        asset.AddAlias("speed", bindingB);

        int changedCount = 0;
        asset.Changed += () => changedCount++;

        // Prune with only assetIdA present; assetIdB's binding should be removed.
        asset.PruneStaleAliasBindings(new[] { assetIdA });

        var remaining = asset.GetAliasesFor("speed");
        Assert.Single(remaining);
        Assert.Equal(assetIdA, remaining[0].RequiringAssetId);
        Assert.Equal(1, changedCount);
    }

    // ---- PruneStaleAliasBindings_NoOp_WhenAllKnownAssetIds ------------------

    [Fact]
    public void PruneStaleAliasBindings_NoOp_WhenAllKnownAssetIds()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("speed", typeof(float), null));

        var assetIdA = Guid.NewGuid();
        var assetIdB = Guid.NewGuid();
        var bindingA = MakeBinding(assetIdA, Guid.NewGuid());
        var bindingB = MakeBinding(assetIdB, Guid.NewGuid());

        asset.AddAlias("speed", bindingA);
        asset.AddAlias("speed", bindingB);

        int changedCount = 0;
        asset.Changed += () => changedCount++;

        // Prune with both IDs present; nothing should be removed.
        asset.PruneStaleAliasBindings(new[] { assetIdA, assetIdB });

        var remaining = asset.GetAliasesFor("speed");
        Assert.Equal(2, remaining.Count);
        Assert.Equal(0, changedCount);
    }

    // ---- GetKnownSubAssetIds_ReturnsAllDistinctRequiringIds -----------------

    [Fact]
    public void GetKnownSubAssetIds_ReturnsAllDistinctRequiringIds()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("speed", typeof(float), null));
        asset.AddVariable(new BlackboardVariableEntry("health", typeof(int), null));

        var assetIdA = Guid.NewGuid();
        var assetIdB = Guid.NewGuid();
        var bindingA = MakeBinding(assetIdA, Guid.NewGuid());
        var bindingB = MakeBinding(assetIdB, Guid.NewGuid());

        asset.AddAlias("speed", bindingA);
        asset.AddAlias("health", bindingB);

        var ids = asset.GetKnownSubAssetIds();

        Assert.Equal(2, ids.Count);
        Assert.Contains(assetIdA, ids);
        Assert.Contains(assetIdB, ids);
    }
}
