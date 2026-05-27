using System;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// Tests for TASK-BB-1d-01/1d-02/1d-05: alias binding model on BehaviorTreeAsset.
/// </summary>
public sealed class BlackboardAliasingAssetTests
{
    // ---- Helpers ------------------------------------------------------------

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "test",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset()
    {
        var asset = new BehaviorTreeAsset(
            Guid.NewGuid(),
            "TestTree",
            "/trees/TestTree.cs",
            isEditorOwned: true,
            "MyBlackboard",
            "MyContext",
            EmptyBlob());
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

    // ---- BehaviorTreeAsset_AddAlias_stores_binding --------------------------

    [Fact]
    public void BehaviorTreeAsset_AddAlias_stores_binding()
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

    // ---- BehaviorTreeAsset_RemoveAlias_removes_binding ----------------------

    [Fact]
    public void BehaviorTreeAsset_RemoveAlias_removes_binding()
    {
        var asset   = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("speed", typeof(float), null));
        var binding = MakeBinding();
        asset.AddAlias("speed", binding);

        asset.RemoveAlias("speed", binding.RequiringAssetId, binding.RequiringElementId);

        Assert.Empty(asset.GetAliasesFor("speed"));
    }

    // ---- BehaviorTreeAsset_RemoveVariable_clears_aliases --------------------

    [Fact]
    public void BehaviorTreeAsset_RemoveVariable_clears_aliases()
    {
        var asset   = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("speed", typeof(float), null));
        var binding = MakeBinding();
        asset.AddAlias("speed", binding);

        asset.RemoveVariable("speed");

        // Aliases for the removed variable must be gone.
        Assert.Empty(asset.GetAliasesFor("speed"));
    }

    // ---- BehaviorTreeAsset_RenameVariable_renames_alias_dict_key ------------

    [Fact]
    public void BehaviorTreeAsset_RenameVariable_renames_alias_dict_key()
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
