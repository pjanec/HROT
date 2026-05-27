using System;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Blackboard;

/// <summary>
/// Tests for SetLoadDiagnostic / LoadState / LoadDiagnosticMessage on BehaviorTreeAsset.
/// Corrective tests for BATCH-10 P2 gap (Issue 2).
/// </summary>
public sealed class BTreeAssetLoadStateTests
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

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(
            Guid.NewGuid(),
            "TestTree",
            "/trees/TestTree.cs",
            isEditorOwned: true,
            "MyBlackboard",
            "MyContext",
            EmptyBlob());

    // ---- LoadState_DefaultsToClean ------------------------------------------

    [Fact]
    public void LoadState_DefaultsToClean()
    {
        var asset = MakeAsset();

        Assert.Equal(BlackboardLoadState.Clean, asset.LoadState);
        Assert.Null(asset.LoadDiagnosticMessage);
    }

    // ---- SetLoadDiagnostic_SetsClean ----------------------------------------

    [Fact]
    public void SetLoadDiagnostic_SetsClean()
    {
        var asset = MakeAsset();
        asset.SetLoadDiagnostic(BlackboardLoadState.Clean, null);

        Assert.Equal(BlackboardLoadState.Clean, asset.LoadState);
        Assert.Null(asset.LoadDiagnosticMessage);
    }

    // ---- SetLoadDiagnostic_SetsStructParseFailed ----------------------------

    [Fact]
    public void SetLoadDiagnostic_SetsStructParseFailed()
    {
        var asset = MakeAsset();
        asset.SetLoadDiagnostic(BlackboardLoadState.StructParseFailed, "Parse error");

        Assert.Equal(BlackboardLoadState.StructParseFailed, asset.LoadState);
        Assert.Equal("Parse error", asset.LoadDiagnosticMessage);
    }

    // ---- SetLoadDiagnostic_SetsAssemblyFailed --------------------------------

    [Fact]
    public void SetLoadDiagnostic_SetsAssemblyFailed()
    {
        var asset = MakeAsset();
        asset.SetLoadDiagnostic(BlackboardLoadState.AssemblyFailed, "Build failed");

        Assert.Equal(BlackboardLoadState.AssemblyFailed, asset.LoadState);
        Assert.Equal("Build failed", asset.LoadDiagnosticMessage);
    }
}
