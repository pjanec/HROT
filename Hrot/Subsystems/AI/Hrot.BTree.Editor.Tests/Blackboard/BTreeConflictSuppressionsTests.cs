using System;
using Fbt;
using Hrot.BTree.Editor.Model;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Blackboard;

/// <summary>
/// Tests for BehaviorTreeAsset.SetConflictSuppressed / IsConflictSuppressed.
/// TASK-BB-1f-05.
/// </summary>
public sealed class BTreeConflictSuppressionsTests
{
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

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(
            Guid.NewGuid(),
            "TestTree",
            "/trees/TestTree.cs",
            isEditorOwned: true,
            "MyBlackboard",
            "MyContext",
            EmptyBlob());

    [Fact]
    public void SetConflictSuppressed_True_AllowsVariable()
    {
        var asset = MakeAsset();
        asset.SetConflictSuppressed("speed", "pair_key", true);
        Assert.True(asset.IsConflictSuppressed("speed", "pair_key"));
    }

    [Fact]
    public void SetConflictSuppressed_False_AfterTrue_DisallowsVariable()
    {
        var asset = MakeAsset();
        asset.SetConflictSuppressed("speed", "pair_key", true);
        asset.SetConflictSuppressed("speed", "pair_key", false);
        Assert.False(asset.IsConflictSuppressed("speed", "pair_key"));
    }

    [Fact]
    public void SetConflictSuppressed_FiresChanged()
    {
        var asset = MakeAsset();
        int changedCount = 0;
        asset.Changed += () => changedCount++;
        asset.SetConflictSuppressed("speed", "pair_key", true);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void SetConflictSuppressed_DoesNotFireChanged_WhenValueUnchanged()
    {
        var asset = MakeAsset();
        int changedCount = 0;
        asset.Changed += () => changedCount++;
        asset.SetConflictSuppressed("speed", "pair_key", true);
        changedCount = 0;
        
        asset.SetConflictSuppressed("speed", "pair_key", true);
        Assert.Equal(0, changedCount);
    }
}