using System;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Blackboard;

/// <summary>
/// Tests for SetLoadDiagnostic / LoadState / LoadDiagnosticMessage on HsmAsset.
/// Corrective tests for BATCH-10 P2 gap (Issue 2).
/// </summary>
public sealed class HsmAssetLoadStateTests
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
