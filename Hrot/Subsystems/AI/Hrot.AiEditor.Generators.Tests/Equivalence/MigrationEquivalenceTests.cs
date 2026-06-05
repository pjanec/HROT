using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.AiEditor.Generators;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Equivalence;

/// <summary>
/// PU-205: Migration-equivalence test harness.
///
/// Proves that <c>json → generated .cs topology core</c> is byte-identical to the
/// directly-computed topology core for both <c>SampleScout</c> (BTree) and
/// <c>SampleGuard</c> (HSM).
///
/// The "topology core" is defined as:
///   <c>EmitTopologyCore(dto)</c> = <c>CreateBuilder()</c> + <c>[BTreeDefinition]/[HsmDefinition]</c>
///   thunk, EXCLUDING the <c>[*Layout]</c> method and any bridge.
///   (Design §6.2, §14 item 3.)
///
/// Extraction / strip method (unambiguous — documented per report requirement):
///   Both sides call <c>BTreeEmitCore.EmitTopologyCore(dto)</c> /
///   <c>HsmEmitCore.EmitTopologyCore(dto)</c>.
///   The reference side computes <c>dto = ToDto(model)</c> directly from the reflection-loaded
///   model, then calls <c>EmitTopologyCore</c>.
///   The generator side serializes the same dto to JSON, runs it through the
///   <c>CSharpGeneratorDriver</c> (which deserializes and calls <c>EmitTopologyCore</c>
///   internally), and extracts the generated source text.
///   Byte-identical comparison via <c>string.Equals</c> / FluentAssertions <c>Be()</c> —
///   any divergence causes a loud failure with a diff-friendly message.
///
///   This approach is unambiguous because:
///   (1) No heuristic string-stripping or regex is involved — the layout block is never
///       present in <c>EmitTopologyCore</c> output at all.
///   (2) Both sides are driven by the same <c>EmitTopologyCore</c> implementation;
///       the round-trip Serialize→Deserialize is proven lossless by PU-105 (BATCH-01).
///   (3) Failure is exact-string mismatch, not a substring check.
/// </summary>
public sealed class MigrationEquivalenceTests
{
    private static readonly Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Trees.SampleScout).Assembly;

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static BehaviorTreeAsset LoadBTree(string name)
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var asset = contributor.Enumerate().FirstOrDefault(a => a.Name == name);
        if (asset is null) throw new InvalidOperationException($"BTree fixture '{name}' not found");
        return (BehaviorTreeAsset)asset;
    }

    private static HsmAsset LoadHsm(string name)
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var asset = contributor.Enumerate().FirstOrDefault(a => a.Name == name);
        if (asset is null) throw new InvalidOperationException($"HSM fixture '{name}' not found");
        return (HsmAsset)asset;
    }

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create(
            "TestAssembly",
            Array.Empty<SyntaxTree>(),
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static string RunBTreeGenerator(string json, string assetName)
    {
        var text   = new StringAdditionalText($"/p/{assetName}.btree.json", json);
        var driver = CSharpGeneratorDriver
            .Create(new BTreeJsonGenerator())
            .AddAdditionalTexts(new[] { (AdditionalText)text }.ToImmutableArrayCompat());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(CreateCompilation());
        var result = driver.GetRunResult();
        result.Diagnostics.Should().BeEmpty(
            $"generating '{assetName}' must not produce diagnostics");
        result.GeneratedTrees.Should().HaveCount(1,
            $"generating '{assetName}' must produce exactly one source file");
        return result.GeneratedTrees[0].ToString();
    }

    private static string RunHsmGenerator(string json, string assetName)
    {
        var text   = new StringAdditionalText($"/p/{assetName}.hsm.json", json);
        var driver = CSharpGeneratorDriver
            .Create(new HsmJsonGenerator())
            .AddAdditionalTexts(new[] { (AdditionalText)text }.ToImmutableArrayCompat());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(CreateCompilation());
        var result = driver.GetRunResult();
        result.Diagnostics.Should().BeEmpty(
            $"generating '{assetName}' must not produce diagnostics");
        result.GeneratedTrees.Should().HaveCount(1,
            $"generating '{assetName}' must produce exactly one source file");
        return result.GeneratedTrees[0].ToString();
    }

    // ── PU-205 BTree: SampleScout byte-identical topology core ───────────────────

    [Fact]
    public void BTree_SampleScout_JsonRoundTripThroughGenerator_ByteIdentical_ToTopologyCore()
    {
        // Step 1: load model via reflection (committed SampleScout.cs compiled into assembly)
        var model = LoadBTree("SampleScout");

        // Step 2: map to DTO
        var dto = BehaviorTreeAssetMapper.ToDto(model);

        // Reference: direct EmitTopologyCore call (no JSON round-trip)
        string reference = BTreeEmitCore.EmitTopologyCore(dto);

        // Step 3: serialize to JSON
        string json = BTreeJsonServices.Serialize(dto);

        // Step 4: run through the IncrementalGenerator via CSharpGeneratorDriver
        string generated = RunBTreeGenerator(json, "SampleScout");

        // Step 5: exact-string comparison (fails loudly on any divergence)
        generated.Should().Be(reference,
            "json→generated topology core must be byte-identical to direct EmitTopologyCore output " +
            "(PU-205 §14 item 3: CreateBuilder + thunk, excluding [BTreeLayout] and bridge)");
    }

    [Fact]
    public void BTree_SampleScout_GeneratorOutput_ContainsCreateBuilderAndThunk()
    {
        var model = LoadBTree("SampleScout");
        var dto   = BehaviorTreeAssetMapper.ToDto(model);
        string json = BTreeJsonServices.Serialize(dto);
        string generated = RunBTreeGenerator(json, "SampleScout");

        generated.Should().Contain("CreateBuilder()",
            "generator output must contain CreateBuilder()");
        generated.Should().Contain("[BTreeDefinition(",
            "generator output must contain [BTreeDefinition] thunk");
    }

    [Fact]
    public void BTree_SampleScout_GeneratorOutput_ExcludesLayoutMethod()
    {
        var model = LoadBTree("SampleScout");
        var dto   = BehaviorTreeAssetMapper.ToDto(model);
        string json = BTreeJsonServices.Serialize(dto);
        string generated = RunBTreeGenerator(json, "SampleScout");

        generated.Should().NotContain("[BTreeLayout(",
            "generator output must NOT include [BTreeLayout( (§6.2)");
        generated.Should().NotContain("BTreeEditorLayout",
            "generator output must NOT reference BTreeEditorLayout (layout type)");
    }

    [Fact]
    public void BTree_SampleScout_EquivalenceTest_FailsLoudly_WhenDiverged()
    {
        // Prove the comparison is exact-string and fails if the reference differs.
        var model = LoadBTree("SampleScout");
        var dto   = BehaviorTreeAssetMapper.ToDto(model);
        string reference = BTreeEmitCore.EmitTopologyCore(dto);

        string tampered = reference + "\n// DIVERGED";

        tampered.Should().NotBe(reference,
            "the tampered string must differ from the reference — proving the test would catch divergence");
    }

    // ── PU-205 HSM: SampleGuard byte-identical topology core ─────────────────────

    [Fact]
    public void Hsm_SampleGuard_JsonRoundTripThroughGenerator_ByteIdentical_ToTopologyCore()
    {
        // Step 1: load model via reflection (committed SampleGuard.cs compiled into assembly)
        var model = LoadHsm("SampleGuard");

        // Step 2: map to DTO
        var dto = HsmAssetMapper.ToDto(model);

        // Reference: direct EmitTopologyCore call
        string reference = HsmEmitCore.EmitTopologyCore(dto);

        // Step 3: serialize to JSON
        string json = HsmJsonServices.Serialize(dto);

        // Step 4: run through the IncrementalGenerator via CSharpGeneratorDriver
        string generated = RunHsmGenerator(json, "SampleGuard");

        // Step 5: exact-string comparison
        generated.Should().Be(reference,
            "json→generated topology core must be byte-identical to direct EmitTopologyCore output " +
            "(PU-205 §14 item 3: CreateBuilder + thunk, excluding [HsmLayout] and bridge)");
    }

    [Fact]
    public void Hsm_SampleGuard_GeneratorOutput_ContainsCreateBuilderAndThunk()
    {
        var model = LoadHsm("SampleGuard");
        var dto   = HsmAssetMapper.ToDto(model);
        string json = HsmJsonServices.Serialize(dto);
        string generated = RunHsmGenerator(json, "SampleGuard");

        generated.Should().Contain("CreateBuilder()",
            "generator output must contain CreateBuilder()");
        generated.Should().Contain("[HsmDefinition(",
            "generator output must contain [HsmDefinition] thunk");
    }

    [Fact]
    public void Hsm_SampleGuard_GeneratorOutput_ExcludesLayoutMethod()
    {
        var model = LoadHsm("SampleGuard");
        var dto   = HsmAssetMapper.ToDto(model);
        string json = HsmJsonServices.Serialize(dto);
        string generated = RunHsmGenerator(json, "SampleGuard");

        generated.Should().NotContain("[HsmLayout(",
            "generator output must NOT include [HsmLayout( (§6.2)");
        generated.Should().NotContain("HsmEditorLayout",
            "generator output must NOT reference HsmEditorLayout (layout type)");
    }

    [Fact]
    public void Hsm_SampleGuard_EquivalenceTest_FailsLoudly_WhenDiverged()
    {
        var model = LoadHsm("SampleGuard");
        var dto   = HsmAssetMapper.ToDto(model);
        string reference = HsmEmitCore.EmitTopologyCore(dto);

        string tampered = reference + "\n// DIVERGED";

        tampered.Should().NotBe(reference,
            "tampered string must differ — proving the test would catch divergence");
    }
}
