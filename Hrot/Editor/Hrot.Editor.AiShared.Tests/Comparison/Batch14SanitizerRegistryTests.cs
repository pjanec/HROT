using System;
using System.Collections.Generic;
using System.IO;
using Hrot.BTree.Editor.Comparison;
using Hrot.Blueprints.Editor.Comparison;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Hsm.Editor.Comparison;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Comparison;

/// <summary>
/// BATCH-14 / AIE-050: verifies that a <see cref="SanitizerRegistry"/> populated with the
/// three production sanitizers (BTree, HSM, Blueprint) has exactly one sanitizer per
/// <see cref="AssetKind"/>, and that sanitizing the same content twice yields identical
/// stripped output (determinism).
/// </summary>
public sealed class Batch14SanitizerRegistryTests
{
    // ---- catalog stub -------------------------------------------------------

    private sealed class EmptyCatalog : IAssetCatalog
    {
        public IReadOnlyList<IEditableAsset> All => Array.Empty<IEditableAsset>();
        public IEditableAsset? FindByAssetId(Guid id)   => null;
        public IEditableAsset? FindByName(string name)   => null;
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id) => Array.Empty<IEditableAsset>();
        public event Action? Changed { add { } remove { } }
    }

    // ---- helpers ------------------------------------------------------------

    private static SanitizerRegistry BuildProductionRegistry()
    {
        var catalog  = new EmptyCatalog();
        var registry = new SanitizerRegistry();
        registry.Register(new BTreeComparisonSanitizer(catalog));
        registry.Register(new HsmComparisonSanitizer(catalog));
        registry.Register(new BlueprintComparisonSanitizer(
            new NoOpComparisonMigrationAdapter(),
            new NoOpMetaEnvelopeSanitizer(),
            catalog));
        return registry;
    }

    /// <summary>
    /// Writes content to a temp file and returns (path, cleanup action).
    /// </summary>
    private static (string path, Action cleanup) WriteTempFile(string content, string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + suffix);
        File.WriteAllText(path, content);
        return (path, () => { if (File.Exists(path)) File.Delete(path); });
    }

    // ---- tests: per-kind presence ------------------------------------------

    [Theory]
    [InlineData(AssetKind.BTree)]
    [InlineData(AssetKind.Hsm)]
    [InlineData(AssetKind.Blueprint)]
    public void SanitizerRegistry_HasSanitizer_PerAssetKind(AssetKind kind)
    {
        var registry = BuildProductionRegistry();

        // Must not throw; must return the registered sanitizer.
        var sanitizer = registry.Get(kind);

        Assert.NotNull(sanitizer);
        Assert.Equal(kind, sanitizer.TargetKind);
    }

    [Fact]
    public void SanitizerRegistry_BTree_Sanitizer_TargetKind_IsBTree()
    {
        var registry  = BuildProductionRegistry();
        var sanitizer = registry.Get(AssetKind.BTree);
        Assert.Equal(AssetKind.BTree, sanitizer.TargetKind);
    }

    [Fact]
    public void SanitizerRegistry_Hsm_Sanitizer_TargetKind_IsHsm()
    {
        var registry  = BuildProductionRegistry();
        var sanitizer = registry.Get(AssetKind.Hsm);
        Assert.Equal(AssetKind.Hsm, sanitizer.TargetKind);
    }

    [Fact]
    public void SanitizerRegistry_Blueprint_Sanitizer_TargetKind_IsBlueprint()
    {
        var registry  = BuildProductionRegistry();
        var sanitizer = registry.Get(AssetKind.Blueprint);
        Assert.Equal(AssetKind.Blueprint, sanitizer.TargetKind);
    }

    // ---- tests: determinism ------------------------------------------------

    /// <summary>
    /// Determinism: sanitizing the same BTree source file twice must produce bit-for-bit
    /// identical stripped output. Tests the real <see cref="BTreeComparisonSanitizer"/>.
    /// </summary>
    [Fact]
    public void BTreeSanitizer_Deterministic_SameInputTwice_ProducesIdenticalOutput()
    {
        var registry  = BuildProductionRegistry();
        var sanitizer = registry.Get(AssetKind.BTree);

        // Minimal valid BTree C# source that the sanitizer can process.
        const string source =
            "// HROT_EDITOR_GENERATED BTree v1.0; manual edits between HROT_MANUAL_BEGIN/END only.\n" +
            "namespace Ai.Trees\n" +
            "{\n" +
            "    public sealed class OrcGuard_BT\n" +
            "    {\n" +
            "        public static object CreateBuilder() => null;\n" +
            "    }\n" +
            "}\n";

        var (path, cleanup) = WriteTempFile(source, ".cs");
        try
        {
            var request = new AssetExportRequest(path, null, AssetKind.BTree);

            var result1 = sanitizer.Sanitize(request);
            var result2 = sanitizer.Sanitize(request);

            // Both sanitization passes must produce bit-for-bit identical stripped text.
            Assert.Equal(result1.SanitizedText, result2.SanitizedText);
            Assert.Equal(result1.Metadata.Kind, result2.Metadata.Kind);
        }
        finally
        {
            cleanup();
        }
    }

    /// <summary>
    /// Determinism: sanitizing the same HSM C# source file twice must produce identical output.
    /// </summary>
    [Fact]
    public void HsmSanitizer_Deterministic_SameInputTwice_ProducesIdenticalOutput()
    {
        var registry  = BuildProductionRegistry();
        var sanitizer = registry.Get(AssetKind.Hsm);

        const string source =
            "// HROT_EDITOR_GENERATED HSM v1.0; manual edits between HROT_MANUAL_BEGIN/END only.\n" +
            "namespace Ai.Machines\n" +
            "{\n" +
            "    public sealed class OrcMachine\n" +
            "    {\n" +
            "        public static object CreateBuilder() => null;\n" +
            "    }\n" +
            "}\n";

        var (path, cleanup) = WriteTempFile(source, ".cs");
        try
        {
            var request = new AssetExportRequest(path, null, AssetKind.Hsm);

            var result1 = sanitizer.Sanitize(request);
            var result2 = sanitizer.Sanitize(request);

            Assert.Equal(result1.SanitizedText, result2.SanitizedText);
        }
        finally
        {
            cleanup();
        }
    }

    /// <summary>
    /// Determinism: sanitizing the same Blueprint JSON document twice must produce identical output.
    /// Uses a minimal .bp.json file on disk as the sanitizer reads from disk.
    /// </summary>
    [Fact]
    public void BlueprintSanitizer_Deterministic_SameInputTwice_ProducesIdenticalOutput()
    {
        var registry  = BuildProductionRegistry();
        var sanitizer = registry.Get(AssetKind.Blueprint);

        var assetId = Guid.NewGuid();
        var bpJson = $$"""
            {
              "AssetId": "{{assetId:D}}",
              "Name": "TestBlueprint",
              "Graphs": [],
              "Variables": []
            }
            """;

        var (path, cleanup) = WriteTempFile(bpJson, ".bp.json");
        try
        {
            var request = new AssetExportRequest(path, null, AssetKind.Blueprint);

            var result1 = sanitizer.Sanitize(request);
            var result2 = sanitizer.Sanitize(request);

            Assert.Equal(result1.SanitizedText, result2.SanitizedText);
        }
        finally
        {
            cleanup();
        }
    }
}
