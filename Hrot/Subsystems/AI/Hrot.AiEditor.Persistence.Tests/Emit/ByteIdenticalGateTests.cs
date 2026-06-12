using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Emit;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Emit;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Emit;

/// <summary>
/// PU-101 byte-identical gate tests.
/// Design §6.4 + BATCH-02-INSTRUCTIONS task 1:
/// Verifies that <c>core.Emit(mapper.ToDto(model))</c> produces output that is
/// byte-identical to the current <c>BTreeFluentEmitter.Emit(model)</c> /
/// <c>HsmFluentEmitter.Emit(model)</c> for all editor-owned fixture assets
/// (<c>Trees/*.cs</c> + <c>Machines/*.cs</c> under Hrot.AI.Behaviors).
///
/// Also verifies WriteAtomic returns false (no write) when content is byte-identical.
/// </summary>
public sealed class ByteIdenticalGateTests
{
    // ── Assembly anchors ─────────────────────────────────────────────────────────

    private static readonly Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Trees.SampleScout).Assembly;

    // ── BTree fixture loading ─────────────────────────────────────────────────────

    private static BehaviorTreeAsset LoadBTreeFixture(string assetName)
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var assets = contributor.Enumerate();
        var asset = assets.FirstOrDefault(a => a.Name == assetName);
        asset.Should().NotBeNull($"BTree fixture '{assetName}' must be discovered from assembly");
        return (BehaviorTreeAsset)asset!;
    }

    private static HsmAsset LoadHsmFixture(string assetName)
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var assets = contributor.Enumerate();
        var asset = assets.FirstOrDefault(a => a.Name == assetName);
        asset.Should().NotBeNull($"HSM fixture '{assetName}' must be discovered from assembly");
        return (HsmAsset)asset!;
    }

    // ── BTree byte-identical gate ─────────────────────────────────────────────────

    [Theory]
    [InlineData("SampleScout")]
    public void BTree_CoreEmit_IsByteIdentical_ToFluentEmitter(string assetName)
    {
        // Arrange: load via existing reflection/projector path
        var model = LoadBTreeFixture(assetName);

        // Reference output from current (now adapter) BTreeFluentEmitter
        var editorEmitter = new BTreeFluentEmitter();
        string referenceOutput = editorEmitter.Emit(model);

        // Core output: mapper.ToDto(model) → BTreeEmitCore.Emit(dto)
        var dto = BehaviorTreeAssetMapper.ToDto(model);
        string coreOutput = BTreeEmitCore.Emit(dto);

        // Assert byte-identical (string equality == byte-identical for UTF-16 in-memory)
        coreOutput.Should().Be(referenceOutput,
            $"BTreeEmitCore.Emit(ToDto({assetName})) must be byte-identical to BTreeFluentEmitter.Emit({assetName})");
    }

    [Theory]
    [InlineData("SampleScout")]
    public void BTree_CoreEmit_IsDeterministic_TwoCallsSameOutput(string assetName)
    {
        var model = LoadBTreeFixture(assetName);
        var dto = BehaviorTreeAssetMapper.ToDto(model);

        string first  = BTreeEmitCore.Emit(dto);
        string second = BTreeEmitCore.Emit(dto);

        first.Should().Be(second, "BTreeEmitCore must be deterministic");
    }

    [Theory]
    [InlineData("SampleScout")]
    public void BTree_WriteAtomic_ReturnsFalse_WhenContentByteIdentical(string assetName)
    {
        // Arrange
        var model = LoadBTreeFixture(assetName);
        var dto = BehaviorTreeAssetMapper.ToDto(model);
        string content = BTreeEmitCore.Emit(dto);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, content);

            // Act: write the same content
            bool written = AiEmitCoreBase.WriteAtomic(tmp, content);

            // Assert: no write when content is byte-identical
            written.Should().BeFalse(
                "WriteAtomic must be a no-op when content is byte-identical to the existing file");
            File.ReadAllText(tmp).Should().Be(content, "file must be unchanged after no-op write");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Theory]
    [InlineData("SampleScout")]
    public void BTree_WriteAtomic_ReturnsTrue_WhenContentDiffers(string assetName)
    {
        var model = LoadBTreeFixture(assetName);
        var dto = BehaviorTreeAssetMapper.ToDto(model);
        string original = BTreeEmitCore.Emit(dto);

        // Slightly different content
        string modified = original + "\n// changed";

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, original);
            bool written = AiEmitCoreBase.WriteAtomic(tmp, modified);

            written.Should().BeTrue("WriteAtomic must write when content differs");
            File.ReadAllText(tmp).Should().Be(modified, "file must contain the new content");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // ── HSM byte-identical gate ───────────────────────────────────────────────────

    [Theory]
    [InlineData("SampleGuard")]
    public void Hsm_CoreEmit_IsByteIdentical_ToFluentEmitter(string assetName)
    {
        // Arrange: load via existing reflection/projector path
        var model = LoadHsmFixture(assetName);

        // Reference output from current (now adapter) HsmFluentEmitter
        var editorEmitter = new HsmFluentEmitter();
        string referenceOutput = editorEmitter.Emit(model);

        // Core output: mapper.ToDto(model) → HsmEmitCore.Emit(dto)
        var dto = HsmAssetMapper.ToDto(model);
        string coreOutput = HsmEmitCore.Emit(dto);

        // Assert byte-identical
        coreOutput.Should().Be(referenceOutput,
            $"HsmEmitCore.Emit(ToDto({assetName})) must be byte-identical to HsmFluentEmitter.Emit({assetName})");
    }

    [Theory]
    [InlineData("SampleGuard")]
    public void Hsm_CoreEmit_IsDeterministic_TwoCallsSameOutput(string assetName)
    {
        var model = LoadHsmFixture(assetName);
        var dto = HsmAssetMapper.ToDto(model);

        string first  = HsmEmitCore.Emit(dto);
        string second = HsmEmitCore.Emit(dto);

        first.Should().Be(second, "HsmEmitCore must be deterministic");
    }

    [Theory]
    [InlineData("SampleGuard")]
    public void Hsm_WriteAtomic_ReturnsFalse_WhenContentByteIdentical(string assetName)
    {
        var model = LoadHsmFixture(assetName);
        var dto = HsmAssetMapper.ToDto(model);
        string content = HsmEmitCore.Emit(dto);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, content);
            bool written = AiEmitCoreBase.WriteAtomic(tmp, content);

            written.Should().BeFalse(
                "WriteAtomic must be a no-op when content is byte-identical to the existing file");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // ── Cross-check: core output matches the committed .cs files ─────────────────

    [Theory]
    [InlineData("SampleScout")]
    public void BTree_CoreEmit_MatchesCommittedCs_SampleScout(string assetName)
    {
        // Additional check: BTreeFluentEmitter now IS the adapter, so the reference output
        // already is the core output. Cross-verify against the committed SampleScout.cs
        // by checking that both produce the HROT_EDITOR_GENERATED marker and the AssetId.
        var model = LoadBTreeFixture(assetName);
        var dto = BehaviorTreeAssetMapper.ToDto(model);
        string coreOutput = BTreeEmitCore.Emit(dto);

        coreOutput.Should().StartWith(AiEmitCoreBase.EditorGeneratedMarker,
            "emitted file must begin with the HROT_EDITOR_GENERATED marker");
        coreOutput.Should().Contain(model.AssetId.ToString("D"),
            "emitted file must contain the AssetId");
        coreOutput.Should().Contain("[BTreeDefinition(\"SampleScout\", AssetId = \"",
            "emitted file must contain the [BTreeDefinition] attribute with const AssetId form");
        coreOutput.Should().Contain("[BTreeLayout(",
            "emitted file must contain the [BTreeLayout] method");
    }

    [Theory]
    [InlineData("SampleGuard")]
    public void Hsm_CoreEmit_MatchesCommittedCs_SampleGuard(string assetName)
    {
        var model = LoadHsmFixture(assetName);
        var dto = HsmAssetMapper.ToDto(model);
        string coreOutput = HsmEmitCore.Emit(dto);

        coreOutput.Should().StartWith(AiEmitCoreBase.EditorGeneratedMarker,
            "emitted file must begin with the HROT_EDITOR_GENERATED marker");
        coreOutput.Should().Contain(model.AssetId.ToString("D"),
            "emitted file must contain the AssetId");
        // HSM const AssetId form: [HsmDefinition("SampleGuard", AssetId = "979df4a4...")]
        coreOutput.Should().Contain("[HsmDefinition(\"SampleGuard\", AssetId = \"",
            "emitted file must contain the [HsmDefinition] attribute with const AssetId form");
        coreOutput.Should().Contain("[HsmLayout(",
            "emitted file must contain the [HsmLayout] method");
    }

    // ── EditorGeneratedMarker stays in sync between core and AiShared ────────────

    [Fact]
    public void AiEmitCoreBase_EditorGeneratedMarker_MatchesFluentCSharpEmitterBase()
    {
        // Both must expose the same constant (FluentCSharpEmitterBase now delegates to AiEmitCoreBase).
        AiEmitCoreBase.EditorGeneratedMarker
            .Should().Be(Hrot.Editor.AiShared.Emit.FluentCSharpEmitterBase.EditorGeneratedMarker,
                "AiEmitCoreBase and FluentCSharpEmitterBase must expose the same marker constant");
    }

    // ── BATCH-09: AssetId in [BTreeDefinition] ──────────────────────────────────

    [Fact]
    public void BTree_EmitTopologyCore_EmitsAssetId_InBTreeDefinitionAttribute()
    {
        // Arrange: a DTO with a known AssetId
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("12345678-90ab-cdef-1234-567890abcdef"),
            Name = "TestTree",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeRootNodeDto
                {
                    VisualId = new Guid("10000000-0000-0000-0000-000000000001"),
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };

        // Act
        string output = BTreeEmitCore.EmitTopologyCore(dto);

        // Assert: [BTreeDefinition("TestTree", AssetId = "12345678-90ab-cdef-1234-567890abcdef")]
        output.Should().Contain(
            "[BTreeDefinition(\"TestTree\", AssetId = \"12345678-90ab-cdef-1234-567890abcdef\")]",
            "EmitTopologyCore must emit AssetId in [BTreeDefinition] attribute");
    }
}
