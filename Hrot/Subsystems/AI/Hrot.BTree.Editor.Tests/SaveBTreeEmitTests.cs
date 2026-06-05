using System;
using System.IO;
using Fbt;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.BTree.Editor.Emit;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Emit;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// AIE-026: Save_BTree_EmitsDeterministicCSharp
/// PU-105 (re-based): verifies that BTreeFluentEmitter (thin adapter) and BTreeEmitCore
/// produce identical byte-for-byte output for the same asset.
///
/// Verifies that:
///   1. BTreeFluentEmitter (adapter → core) produces identical output for the same asset.
///   2. BTreeEmitCore directly produces the same output as BTreeFluentEmitter.
///   3. Emitting the same (unchanged) model results in WriteAtomic returning false
///      (no-op write — file is already byte-identical).
///   4. A modified asset produces different output.
///   5. AiAssetEmitService wraps the emitter and writes atomically.
/// </summary>
public sealed class SaveBTreeEmitTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BehaviorTreeBlob EmptyBlob() => new()
    {
        TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
        MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
        IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
    };

    private static BehaviorTreeAsset MakeAsset(
        string name      = "MyTree",
        string sourceFile = "",
        string ns         = "Hrot.AI.Behaviors.Trees")
    {
        var assetId = new Guid("aabbccdd-1111-2222-3333-444444444444");
        var asset = new BehaviorTreeAsset(
            assetId, name, sourceFile, isEditorOwned: true,
            "Test.Bb", "Test.Ctx", EmptyBlob(), ns);

        var root   = new BTreeEditorNode { VisualId = new Guid("10000000-0000-0000-0000-000000000001"), KernelType = NodeType.Root };
        var seq    = new BTreeEditorNode { VisualId = new Guid("20000000-0000-0000-0000-000000000001"), KernelType = NodeType.Sequence };
        var action = new BTreeEditorNode
        {
            VisualId    = new Guid("30000000-0000-0000-0000-000000000001"),
            KernelType  = NodeType.Action,
            Action      = new BTreeActionPayload
            {
                MethodFqn     = "Hrot.AI.Behaviors.Trees.Actions.Patrol",
                DelegateShape = BTreeActionDelegateShape.FourParamFull,
            },
        };

        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(action.VisualId);

        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(action);

        return asset;
    }

    // ── AIE-026 SC1: BTreeFluentEmitter adapter produces same output as core ──

    [Fact]
    public void Save_BTree_EmitterAdapter_MatchesCore_DirectCall()
    {
        var asset = MakeAsset();

        // Adapter path (emitter → mapper → core)
        string adapterOutput = new BTreeFluentEmitter().Emit(asset);

        // Direct core path (mapper → core, no emitter wrapper)
        var dto = BehaviorTreeAssetMapper.ToDto(asset);
        string coreOutput = BTreeEmitCore.Emit(dto);

        Assert.Equal(adapterOutput, coreOutput);
    }

    // ── AIE-026 SC2: byte-identical re-emit → WriteAtomic no-op ──────────────

    [Fact]
    public void Save_BTree_EmitsDeterministicCSharp_ByteIdentical_OnNoChange()
    {
        // Arrange
        var emitter = new BTreeFluentEmitter();
        var asset   = MakeAsset();

        // First emit.
        string code1 = emitter.Emit(asset);
        // Second emit of the SAME (unchanged) model.
        string code2 = emitter.Emit(asset);

        // Assert: outputs are byte-for-byte identical.
        Assert.Equal(code1, code2);

        // Assert: WriteAtomic returns false when content already matches file.
        // Uses AiEmitCoreBase.WriteAtomic (the authoritative source; FluentCSharpEmitterBase delegates to it).
        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, code1);
            bool written = AiEmitCoreBase.WriteAtomic(tmp, code2);
            Assert.False(written, "WriteAtomic must be a no-op when content is byte-identical.");
            Assert.Equal(code1, File.ReadAllText(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Save_BTree_Core_IsDeterministic_DirectPath()
    {
        // Core directly (bypassing the editor adapter): same model → same output.
        var asset = MakeAsset();
        var dto = BehaviorTreeAssetMapper.ToDto(asset);

        string first  = BTreeEmitCore.Emit(dto);
        string second = BTreeEmitCore.Emit(dto);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Save_BTree_WriteAtomic_ReturnsFalse_WhenByteIdentical()
    {
        var dto = BehaviorTreeAssetMapper.ToDto(MakeAsset());
        string content = BTreeEmitCore.Emit(dto);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, content);
            bool written = FluentCSharpEmitterBase.WriteAtomic(tmp, content);
            Assert.False(written, "FluentCSharpEmitterBase.WriteAtomic must delegate to AiEmitCoreBase and be a no-op.");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Save_BTree_EmitsDeterministicCSharp_WritesFile_WhenContentDiffers()
    {
        // Arrange: write one version, then emit a modified asset.
        var emitter = new BTreeFluentEmitter();
        var asset   = MakeAsset();
        string original = emitter.Emit(asset);

        // Modify the asset: add a node so the output changes.
        var wait = new BTreeEditorNode
        {
            VisualId   = new Guid("40000000-0000-0000-0000-000000000001"),
            KernelType = NodeType.Wait,
            Wait       = new BTreeWaitPayload { Duration = 2.5f },
        };
        asset.AddNode(wait);
        string modified = emitter.Emit(asset);

        // The two outputs must differ (structural change).
        Assert.NotEqual(original, modified);

        // WriteAtomic must write the new content.
        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, original);
            bool written = FluentCSharpEmitterBase.WriteAtomic(tmp, modified);
            Assert.True(written, "WriteAtomic must write when content differs.");
            Assert.Equal(modified, File.ReadAllText(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Save_BTree_EmitService_EmitsAndClearsAssetDirty()
    {
        // Arrange: asset with a temp source file.
        string tmpPath = Path.GetTempFileName();
        try
        {
            var emitter = new BTreeFluentEmitter();
            var asset   = MakeAsset(sourceFile: tmpPath);
            asset.MarkDirty();
            Assert.True(asset.IsDirty);

            bool cleared = false;
            var svc = new AiAssetEmitService(
                emitDelegate: a => (a is BehaviorTreeAsset bt) ? emitter.Emit(bt) : null,
                postEmit:     (a, _) =>
                {
                    if (a is BehaviorTreeAsset bt) { bt.ClearDirty(); cleared = true; }
                });

            // Act: emit the asset.
            bool written = svc.Emit(asset);

            // Assert: file was written and dirty flag was cleared.
            Assert.True(written);
            Assert.True(cleared);
            Assert.False(asset.IsDirty);

            // Second emit of the same content → no-op write.
            bool writtenAgain = svc.Emit(asset);
            Assert.False(writtenAgain);
        }
        finally
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
        }
    }

    [Fact]
    public void Save_BTree_EmitService_EmptySourcePath_ReturnsFalse()
    {
        var asset = MakeAsset(sourceFile: ""); // no file path
        var svc   = new AiAssetEmitService(
            emitDelegate: a => "content",
            postEmit:     null);

        bool written = svc.Emit(asset);
        Assert.False(written);
    }
}
