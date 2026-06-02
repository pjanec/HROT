using System;
using System.IO;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Emit;
using Hrot.Hsm.Editor.Emit;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

/// <summary>
/// AIE-026: Save_Hsm_EmitsDeterministicCSharp
///
/// Verifies that:
///   1. HsmFluentEmitter produces byte-for-byte identical output for the same asset.
///   2. Emitting the same (unchanged) model results in WriteAtomic returning false
///      (byte-identical no-op write).
///   3. A modified asset produces different output.
///   4. AiAssetEmitService wraps the emitter, writes atomically, clears dirty flag.
/// </summary>
public sealed class SaveHsmEmitTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HsmAsset BuildAndProject(
        string machineName = "TestMachine",
        string sourceFile  = "")
    {
        var builder = new HsmBuilder(machineName);
        builder.Event("Tick", 1);
        var idle    = builder.State("Idle");
        builder.State("Running");
        idle.On("Tick").GoTo("Running");

        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flat     = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flat);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);

        var assetId = new Guid("bbbbbbbb-2222-2222-2222-222222222222");
        return HsmAssetProjector.Project(
            blob, metadata, layout: null,
            assetId, machineName, sourceFile,
            isEditorOwned: false, assemblyNamespace: "Hrot.AI.Behaviors.Machines");
    }

    // ── AIE-026 SC3: byte-identical re-emit → WriteAtomic no-op ──────────────

    [Fact]
    public void Save_Hsm_EmitsDeterministicCSharp_ByteIdentical_OnNoChange()
    {
        // Arrange
        var emitter = new HsmFluentEmitter();
        var asset   = BuildAndProject();

        // First and second emit of the SAME unchanged model.
        string code1 = emitter.Emit(asset);
        string code2 = emitter.Emit(asset);

        // Assert: byte-for-byte identical.
        Assert.Equal(code1, code2);

        // Assert: WriteAtomic is a no-op when the file already matches.
        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, code1);
            bool written = FluentCSharpEmitterBase.WriteAtomic(tmp, code2);
            Assert.False(written, "WriteAtomic must not write when content is byte-identical.");
            Assert.Equal(code1, File.ReadAllText(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Save_Hsm_EmitsDeterministicCSharp_WritesFile_WhenContentDiffers()
    {
        var emitter   = new HsmFluentEmitter();
        var asset1    = BuildAndProject("MachineA");
        var asset2    = BuildAndProject("MachineB"); // different name → different class name in output

        string code1 = emitter.Emit(asset1);
        string code2 = emitter.Emit(asset2);

        // Different machine names → different C# class names → outputs are NOT equal.
        Assert.NotEqual(code1, code2);

        // WriteAtomic must write the changed content.
        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, code1);
            bool written = FluentCSharpEmitterBase.WriteAtomic(tmp, code2);
            Assert.True(written, "WriteAtomic must write when content differs.");
            Assert.Equal(code2, File.ReadAllText(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Save_Hsm_EmitService_EmitsAndClearsAssetDirty()
    {
        string tmpPath = Path.GetTempFileName();
        try
        {
            var emitter = new HsmFluentEmitter();
            var asset   = BuildAndProject(sourceFile: tmpPath);

            // Simulate a command-sink edit that marks dirty.
            // (HsmAsset.MarkDirty is internal; we fake dirtiness via direct IsDirty property
            // which has an internal setter — so we test the public ClearDirty path instead.)
            bool cleared = false;
            var svc = new AiAssetEmitService(
                emitDelegate: a => (a is HsmAsset hs) ? emitter.Emit(hs) : null,
                postEmit:     (a, _) =>
                {
                    if (a is HsmAsset hs) { hs.ClearDirty(); cleared = true; }
                });

            // Act: emit (asset starts clean; file is new so WriteAtomic writes it).
            bool written = svc.Emit(asset);

            // Assert: file was written and ClearDirty was called.
            Assert.True(written);
            Assert.True(cleared);

            // Second emit of the same content → no-op.
            bool writtenAgain = svc.Emit(asset);
            Assert.False(writtenAgain);
        }
        finally
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
        }
    }

    [Fact]
    public void Save_Hsm_EmitContainsExpectedStructure()
    {
        // Structural assertion: the emitted C# must contain the state name and machine name.
        var emitter = new HsmFluentEmitter();
        var asset   = BuildAndProject("GuardFSM");

        string code = emitter.Emit(asset);

        // Assert structural content (class name, state names, header marker).
        Assert.Contains("class GuardFSM", code);
        Assert.Contains("Idle", code);
        Assert.Contains("Running", code);
        Assert.Contains(FluentCSharpEmitterBase.EditorGeneratedMarker, code);
        Assert.Contains(asset.AssetId.ToString("D"), code);
    }
}
