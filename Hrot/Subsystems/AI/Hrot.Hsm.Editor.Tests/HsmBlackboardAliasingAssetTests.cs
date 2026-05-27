using System;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

/// <summary>
/// Tests for TASK-BB-1d-01/1d-02/1d-05: alias binding model on HsmAsset.
/// </summary>
public sealed class HsmBlackboardAliasingAssetTests
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
        var asset    = HsmAssetProjector.Project(blob, metadata, null, Guid.NewGuid(), name, "", false, "");
        asset.IsBlackboardEditorManaged = true;
        return asset;
    }

    private static BlackboardAliasBinding MakeBinding(
        string assetName = "Shoot_BT",
        string path      = "Shoot_BT > Action#1") =>
        new(
            RequiringAssetId:   Guid.NewGuid(),
            RequiringElementId: Guid.NewGuid(),
            RequiringAssetName: assetName,
            RequiredByPath:     path,
            DtoType:            typeof(float));

    // ---- HsmAsset_AddAlias_stores_binding -----------------------------------

    [Fact]
    public void HsmAsset_AddAlias_stores_binding()
    {
        var asset   = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("speed", typeof(float), null));
        var binding = MakeBinding();

        asset.AddAlias("speed", binding);

        var result = asset.GetAliasesFor("speed");
        Assert.Single(result);
        Assert.Equal(binding.RequiringAssetId,   result[0].RequiringAssetId);
        Assert.Equal(binding.RequiringAssetName, result[0].RequiringAssetName);
    }

    // ---- HsmAsset_RemoveAlias_removes_binding -------------------------------

    [Fact]
    public void HsmAsset_RemoveAlias_removes_binding()
    {
        var asset   = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("speed", typeof(float), null));
        var binding = MakeBinding();
        asset.AddAlias("speed", binding);

        asset.RemoveAlias("speed", binding.RequiringAssetId, binding.RequiringElementId);

        Assert.Empty(asset.GetAliasesFor("speed"));
    }

    // ---- HsmAsset_RemoveVariable_clears_aliases -----------------------------

    [Fact]
    public void HsmAsset_RemoveVariable_clears_aliases()
    {
        var asset   = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("speed", typeof(float), null));
        var binding = MakeBinding();
        asset.AddAlias("speed", binding);

        asset.RemoveVariable("speed");

        // Aliases for the removed variable must be gone.
        Assert.Empty(asset.GetAliasesFor("speed"));
    }

    // ---- HsmAsset_RenameVariable_renames_alias_dict_key ---------------------

    [Fact]
    public void HsmAsset_RenameVariable_renames_alias_dict_key()
    {
        var asset   = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("speed", typeof(float), null));
        var binding = MakeBinding();
        asset.AddAlias("speed", binding);

        asset.RenameVariable("speed", "velocity");

        Assert.Empty(asset.GetAliasesFor("speed"));
        var result = asset.GetAliasesFor("velocity");
        Assert.Single(result);
        Assert.Equal(binding.RequiringAssetId, result[0].RequiringAssetId);
    }
}
