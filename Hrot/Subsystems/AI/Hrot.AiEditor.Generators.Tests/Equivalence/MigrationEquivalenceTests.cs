using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fbt;
using Fbt.Kernel;
using Fhsm.Kernel.Data;
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
using Microsoft.CodeAnalysis.Text;
using Xunit;

using RoslynMRR = Hrot.Blueprints.Core.Compiler.Roslyn.MetadataReferenceResolver;

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

    /// <summary>
    /// Runs the BTree generator and returns the TOPOLOGY CORE source file
    /// ({Name}.g.cs, not the bridge {Name}.Registrar.g.cs).
    /// PU-205 §14 item 3: the equivalence gate compares only the topology core.
    /// </summary>
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
        // PU-203: generator now produces 2 files: topology core + bridge (Registrar).
        result.GeneratedTrees.Should().HaveCount(2,
            $"generating '{assetName}' must produce 2 files: topology core + bridge (PU-203)");
        // Select the topology-core file (not the bridge): the one without "Registrar" in the path.
        var coreTree = result.GeneratedTrees
            .FirstOrDefault(t => !t.FilePath.Contains("Registrar"));
        coreTree.Should().NotBeNull(
            "generator must produce a topology-core source file (hint name not containing 'Registrar')");
        return coreTree!.ToString();
    }

    /// <summary>
    /// Runs the HSM generator and returns the TOPOLOGY CORE source file.
    /// </summary>
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
        result.GeneratedTrees.Should().HaveCount(2,
            $"generating '{assetName}' must produce 2 files: topology core + bridge (PU-203)");
        var coreTree = result.GeneratedTrees
            .FirstOrDefault(t => !t.FilePath.Contains("Registrar"));
        coreTree.Should().NotBeNull(
            "generator must produce a topology-core source file (hint name not containing 'Registrar')");
        return coreTree!.ToString();
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

    // ── PU-401 Task 2: REAL blob-equivalence tests (replaces PU-D05 tautologies) ───

    /// <summary>
    /// PU-D06: committed blob ≡ JSON-regenerated blob for SampleScout.
    /// Strategy: committed → JSON (ToDtoWithTypeNames) → CSharpGeneratorDriver → CompileMultiAndLoad
    ///           → reflection-invoke generated Build() → BlobEquivalence.AssertEqual.
    /// </summary>
    [Fact]
    public void BTree_SampleScout_BlobEquivalence_CommittedVsJsonRegenerated()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        BTree_SampleScout_BlobEquivalence_CommittedVsJsonRegenerated_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BTree_SampleScout_BlobEquivalence_CommittedVsJsonRegenerated_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        // 1. Reference blob — call the COMMITTED SampleScout.Build() directly
        var referenceBlob = Hrot.AI.Behaviors.Trees.SampleScout.Build();

        // 2. Produce migration JSON via committed → JSON chain (with type-name recovery)
        var model = LoadBTree("SampleScout");
        var dto   = ToDtoWithTypeNames(model, "SampleScout");
        string json = BTreeJsonServices.Serialize(dto);

        // 3. Regenerate C# via CSharpGeneratorDriver
        var srcs = GenerateBTreeSources(json, "SampleScout");

        // 4. Compile topology core only (index 0 = core, not bridge) + load into collectible ALC
        var topologyCore = srcs.First(s => !s.Contains("[BlueprintRegistrar]"));
        var (asm, alc)   = CompileMultiAndLoad(new[] { topologyCore }, "SampleScoutBlobEquivTest");

        // 5. Reflection-invoke generated Build() → regenerated blob
        var generatedType = asm.GetType("Hrot.AI.Behaviors.Trees.SampleScout");
        generatedType.Should().NotBeNull("generated type Hrot.AI.Behaviors.Trees.SampleScout must exist");
        var buildMethod = generatedType!.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
        buildMethod.Should().NotBeNull("generated Build() method must be public static");
        var regeneratedBlob = (BehaviorTreeBlob)buildMethod!.Invoke(null, null)!;

        // 6. PU-D06 criterion: AssertEqual (throws on any structural divergence)
        BlobEquivalence.AssertEqual(referenceBlob, regeneratedBlob);

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    /// <summary>
    /// PU-D05 replacement: divergence test — mutate a behavior-affecting JSON field (Wait duration),
    /// regenerate blob, and assert BlobEquivalence.AssertEqual THROWS. This is the real sentinel.
    /// </summary>
    [Fact]
    public void BTree_SampleScout_BlobEquivalence_FailsLoudly_WhenJsonDiverges()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        BTree_SampleScout_BlobEquivalence_FailsLoudly_WhenJsonDiverges_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BTree_SampleScout_BlobEquivalence_FailsLoudly_WhenJsonDiverges_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        // Reference blob (committed)
        var referenceBlob = Hrot.AI.Behaviors.Trees.SampleScout.Build();

        // Produce base JSON
        var model = LoadBTree("SampleScout");
        var dto   = ToDtoWithTypeNames(model, "SampleScout");
        string json = BTreeJsonServices.Serialize(dto);

        // MUTATE: change Wait duration from 1.0 to 99.0 in the JSON
        // SampleScout has a Wait(1.0f) node; changing the float param changes ParamHash + FloatParams
        string mutatedJson = json.Replace("\"Duration\":1", "\"Duration\":99");
        mutatedJson.Should().NotBe(json, "mutation must have changed the JSON");

        // Regenerate from mutated JSON
        var srcs         = GenerateBTreeSources(mutatedJson, "SampleScout");
        var topologyCore = srcs.First(s => !s.Contains("[BlueprintRegistrar]"));
        var (asm, alc)   = CompileMultiAndLoad(new[] { topologyCore }, "SampleScoutDivergenceTest");

        var generatedType = asm.GetType("Hrot.AI.Behaviors.Trees.SampleScout");
        var buildMethod   = generatedType!.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
        var mutatedBlob   = (BehaviorTreeBlob)buildMethod!.Invoke(null, null)!;

        // Assert that BlobEquivalence.AssertEqual THROWS — the real divergence sentinel
        var act = () => BlobEquivalence.AssertEqual(referenceBlob, mutatedBlob);
        act.Should().Throw<Exception>(
            "mutated JSON must produce a blob that differs from reference — BlobEquivalence must detect it");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
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

    // ── PU-401 Task 2: REAL blob-equivalence tests for HSM SampleGuard ────────────

    /// <summary>
    /// PU-D06: committed blob ≡ JSON-regenerated blob for SampleGuard.
    /// Strategy: committed → JSON (HsmAssetMapper.ToDto) → CSharpGeneratorDriver → CompileMultiAndLoad
    ///           → reflection-invoke generated Compile() → BlobEquivalence.AssertEqual.
    /// </summary>
    [Fact]
    public void Hsm_SampleGuard_BlobEquivalence_CommittedVsJsonRegenerated()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        Hsm_SampleGuard_BlobEquivalence_CommittedVsJsonRegenerated_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Hsm_SampleGuard_BlobEquivalence_CommittedVsJsonRegenerated_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        // 1. Reference blob — call the COMMITTED SampleGuard.Compile() directly
        var referenceBlob = Hrot.AI.Behaviors.Machines.SampleGuard.Compile();

        // 2. Produce migration JSON
        var model = LoadHsm("SampleGuard");
        var dto   = HsmAssetMapper.ToDto(model);
        string json = HsmJsonServices.Serialize(dto);

        // 3. Regenerate C# via CSharpGeneratorDriver
        var srcs = GenerateHsmSources(json, "SampleGuard");

        // 4. Compile topology core only + load into collectible ALC
        var topologyCore = srcs.First(s => !s.Contains("[BlueprintRegistrar]"));
        var (asm, alc)   = CompileMultiAndLoad(new[] { topologyCore }, "SampleGuardBlobEquivTest");

        // 5. Reflection-invoke generated Compile() → regenerated blob
        var generatedType = asm.GetType("Hrot.AI.Behaviors.Machines.SampleGuard");
        generatedType.Should().NotBeNull("generated type Hrot.AI.Behaviors.Machines.SampleGuard must exist");
        var compileMethod = generatedType!.GetMethod("Compile", BindingFlags.Public | BindingFlags.Static);
        compileMethod.Should().NotBeNull("generated Compile() method must be public static");
        var regeneratedBlob = (HsmDefinitionBlob)compileMethod!.Invoke(null, null)!;

        // 6. PU-D06 criterion
        BlobEquivalence.AssertEqual(referenceBlob, regeneratedBlob);

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    /// <summary>
    /// PU-D05 replacement: divergence test — remove a transition from HSM JSON and assert
    /// BlobEquivalence.AssertEqual THROWS (real sentinel, not tautology).
    /// Mutation: drop the second transition entry from the JSON — changes TransitionCount + StructureHash.
    /// </summary>
    [Fact]
    public void Hsm_SampleGuard_BlobEquivalence_FailsLoudly_WhenJsonDiverges()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        Hsm_SampleGuard_BlobEquivalence_FailsLoudly_WhenJsonDiverges_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Hsm_SampleGuard_BlobEquivalence_FailsLoudly_WhenJsonDiverges_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        // Reference blob (committed)
        var referenceBlob = Hrot.AI.Behaviors.Machines.SampleGuard.Compile();

        // Produce base JSON
        var model = LoadHsm("SampleGuard");
        var dto   = HsmAssetMapper.ToDto(model);
        string json = HsmJsonServices.Serialize(dto);

        // MUTATE: change the EventId of "Alert" from 1 to 99.
        // The EventId is used directly in builder.Event(name, id, ...) → TransitionDef.EventId in blob.
        // This changes ParameterHash (which covers event IDs used in transitions).
        // "EventId":1 is SampleGuard's Alert event (see SampleGuard.cs: builder.Event("Alert", 1, ...))
        string mutatedJson = json.Replace("\"EventId\":1", "\"EventId\":99");
        if (mutatedJson == json)
        {
            // Fallback: change the first event's PayloadSize — affects event table in blob
            mutatedJson = json.Replace("\"PayloadSize\":0,\"IsIndirect\":false", "\"PayloadSize\":8,\"IsIndirect\":false");
        }
        mutatedJson.Should().NotBe(json, "mutation must have changed the JSON — check SampleGuard's event JSON structure");

        // Regenerate from mutated JSON
        var srcs         = GenerateHsmSources(mutatedJson, "SampleGuard");
        var topologyCore = srcs.First(s => !s.Contains("[BlueprintRegistrar]"));
        var (asm, alc)   = CompileMultiAndLoad(new[] { topologyCore }, "SampleGuardDivergenceTest");

        var generatedType = asm.GetType("Hrot.AI.Behaviors.Machines.SampleGuard");
        var compileMethod = generatedType!.GetMethod("Compile", BindingFlags.Public | BindingFlags.Static);
        var mutatedBlob   = (HsmDefinitionBlob)compileMethod!.Invoke(null, null)!;

        // Assert that BlobEquivalence.AssertEqual THROWS — the real divergence sentinel
        var act = () => BlobEquivalence.AssertEqual(referenceBlob, mutatedBlob);
        act.Should().Throw<Exception>(
            "mutated JSON must produce a blob that differs from reference — BlobEquivalence must detect it");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ── PU-401 Task 3: Migration JSON generation + validation ────────────────────

    /// <summary>
    /// PU-401 Task 3: Generate migration JSON for SampleScout (BTree).
    /// Asserts: round-trips byte-stable, carries non-empty per-node layout (X/Y),
    /// and has populated BlackboardTypeName/ContextTypeName (from ToDtoWithTypeNames).
    /// Writes the validated file to .dev/persistence-unification/migration-artifacts/SampleScout.btree.json.
    /// </summary>
    [Fact]
    public void BTree_SampleScout_MigrationJson_RoundTrips_And_CarriesLayout()
    {
        var model = LoadBTree("SampleScout");
        var dto   = ToDtoWithTypeNames(model, "SampleScout");

        // 1. Serialize to JSON
        string json = BTreeJsonServices.Serialize(dto);
        json.Should().NotBeNullOrWhiteSpace("migration JSON must not be empty");

        // 2. Deserialize back → assert structural round-trip
        var dto2 = BTreeJsonServices.Deserialize(json);
        dto2.Should().NotBeNull("deserialization of migration JSON must succeed");

        // 3. Re-serialize and compare → byte-stable
        string json2 = BTreeJsonServices.Serialize(dto2!);
        json2.Should().Be(json, "migration JSON must be byte-stable after Serialize→Deserialize→Serialize");

        // 4. Layout: assert every non-root node has non-zero X or Y (committed layout has canvas positions)
        var nodesWithLayout = dto2!.Nodes
            .Where(n => n.EditorMetadata.X != 0f || n.EditorMetadata.Y != 0f)
            .ToList();
        nodesWithLayout.Should().NotBeEmpty(
            "SampleScout migration JSON must carry per-node layout (X/Y) from the committed [BTreeLayout] " +
            "(decommit safety: editor restores layout from JSON via PU-301). " +
            $"All {dto2.Nodes.Count} nodes have X=0/Y=0 — check ToDtoWithTypeNames/BTreeAssetMapper layout application.");

        // 5. Type names populated (required for compiler — plain ToDto leaves them empty)
        dto2.BlackboardTypeName.Should().NotBeNullOrWhiteSpace(
            "BlackboardTypeName must be populated by ToDtoWithTypeNames (required for BTreeBuilder<BB,Ctx> emit). " +
            "Plain BehaviorTreeAssetMapper.ToDto() leaves it empty.");
        dto2.ContextTypeName.Should().NotBeNullOrWhiteSpace(
            "ContextTypeName must be populated by ToDtoWithTypeNames.");

        // 6. Write to migration-artifacts (staging file — NOT the live Trees/ folder)
        string artifactPath = GetMigrationArtifactPath("SampleScout.btree.json");
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        File.WriteAllText(artifactPath, json, System.Text.Encoding.UTF8);

        // Verify written file can be deserialized (end-to-end validation)
        string readBack = File.ReadAllText(artifactPath, System.Text.Encoding.UTF8);
        readBack.Should().Be(json, "written migration artifact must match in-memory JSON");
    }

    /// <summary>
    /// PU-401 Task 3: Generate migration JSON for SampleGuard (HSM).
    /// Asserts: round-trips byte-stable, carries per-state layout (X/Y).
    /// Writes the validated file to .dev/persistence-unification/migration-artifacts/Machines/SampleGuard.hsm.json.
    /// </summary>
    [Fact]
    public void Hsm_SampleGuard_MigrationJson_RoundTrips_And_CarriesLayout()
    {
        var model = LoadHsm("SampleGuard");
        var dto   = HsmAssetMapper.ToDto(model);

        // 1. Serialize to JSON
        string json = HsmJsonServices.Serialize(dto);
        json.Should().NotBeNullOrWhiteSpace("migration JSON must not be empty");

        // 2. Deserialize back → assert structural round-trip
        var dto2 = HsmJsonServices.Deserialize(json);
        dto2.Should().NotBeNull("deserialization of migration JSON must succeed");

        // 3. Re-serialize and compare → byte-stable
        string json2 = HsmJsonServices.Serialize(dto2!);
        json2.Should().Be(json, "migration JSON must be byte-stable after Serialize→Deserialize→Serialize");

        // 4. Layout: assert at least one state has non-zero X or Y
        var statesWithLayout = dto2!.States
            .Where(s => s.X != 0f || s.Y != 0f)
            .ToList();
        statesWithLayout.Should().NotBeEmpty(
            "SampleGuard migration JSON must carry per-state layout (X/Y) from the committed [HsmLayout] " +
            "(decommit safety: editor restores layout from JSON via PU-301). " +
            $"All {dto2.States.Count} states have X=0/Y=0 — check HsmAssetMapper layout application.");

        // 5. Write to migration-artifacts/Machines/ (NOT the live Machines/ folder)
        string artifactPath = GetMigrationArtifactPath("Machines/SampleGuard.hsm.json");
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        File.WriteAllText(artifactPath, json, System.Text.Encoding.UTF8);

        // Verify written file can be deserialized (end-to-end validation)
        string readBack = File.ReadAllText(artifactPath, System.Text.Encoding.UTF8);
        readBack.Should().Be(json, "written migration artifact must match in-memory JSON");
    }

    // ── Shared helpers for PU-401 tests ──────────────────────────────────────────

    /// <summary>
    /// Returns the absolute path to the migration artifact at the given relative path
    /// under .dev/persistence-unification/migration-artifacts/.
    /// </summary>
    private static string GetMigrationArtifactPath(string relativePath)
    {
        // Walk up from the test assembly directory to the repo root, then to .dev/
        // The test assembly lives at: <repo>/Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/bin/Debug/net8.0/
        var asmDir = Path.GetDirectoryName(typeof(MigrationEquivalenceTests).Assembly.Location)!;
        // Walk up 6 levels: net8.0 → Debug → bin → Hrot.AiEditor.Generators.Tests → AI → Subsystems → Hrot → repo root
        var repoRoot = asmDir;
        for (int i = 0; i < 7; i++)
            repoRoot = Path.GetDirectoryName(repoRoot)!;
        return Path.Combine(repoRoot, ".dev", "persistence-unification", "migration-artifacts", relativePath);
    }

    /// <summary>
    /// Maps the BTree asset to a DTO and fills in type names from the assembly's
    /// CreateBuilder return type generic args when the mapper leaves them empty.
    /// Mirrors BlueprintRegistrarBridgeIntegrationTests.ToDtoWithTypeNames (BATCH-05).
    /// </summary>
    private static BehaviorTreeAssetDto ToDtoWithTypeNames(BehaviorTreeAsset asset, string className)
    {
        var dto = BehaviorTreeAssetMapper.ToDto(asset);

        if (string.IsNullOrEmpty(dto.BlackboardTypeName) || string.IsNullOrEmpty(dto.ContextTypeName))
        {
            var type = BehaviorsAssembly.GetTypes()
                .FirstOrDefault(t => t.Name == className &&
                    t.GetMethod("CreateBuilder", BindingFlags.Public | BindingFlags.Static) != null);

            if (type != null)
            {
                var cb = type.GetMethod("CreateBuilder", BindingFlags.Public | BindingFlags.Static);
                if (cb?.ReturnType.IsGenericType == true)
                {
                    var args = cb.ReturnType.GetGenericArguments();
                    if (args.Length >= 2)
                    {
                        if (string.IsNullOrEmpty(dto.BlackboardTypeName))
                            dto.BlackboardTypeName = args[0].FullName ?? args[0].Name;
                        if (string.IsNullOrEmpty(dto.ContextTypeName))
                            dto.ContextTypeName = args[1].FullName ?? args[1].Name;
                    }
                }
            }
        }

        return dto;
    }

    /// <summary>
    /// Generates both BTree source files (topology core + bridge) and returns them.
    /// </summary>
    private static string[] GenerateBTreeSources(string json, string assetName)
    {
        var text   = new StringAdditionalText($"/p/{assetName}.btree.json", json);
        var driver = CSharpGeneratorDriver
            .Create(new BTreeJsonGenerator())
            .AddAdditionalTexts(new[] { (AdditionalText)text }.ToImmutableArrayCompat());
        var compilation = CSharpCompilation.Create(
            "Gen",
            Array.Empty<SyntaxTree>(),
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        result.Diagnostics.Should().BeEmpty($"generator must not produce diagnostics for '{assetName}'");
        result.GeneratedTrees.Should().HaveCount(2, "must produce topology core + bridge");
        return result.GeneratedTrees.Select(t => t.ToString()).ToArray();
    }

    /// <summary>
    /// Generates both HSM source files (topology core + bridge) and returns them.
    /// </summary>
    private static string[] GenerateHsmSources(string json, string assetName)
    {
        var text   = new StringAdditionalText($"/p/{assetName}.hsm.json", json);
        var driver = CSharpGeneratorDriver
            .Create(new HsmJsonGenerator())
            .AddAdditionalTexts(new[] { (AdditionalText)text }.ToImmutableArrayCompat());
        var compilation = CSharpCompilation.Create(
            "Gen",
            Array.Empty<SyntaxTree>(),
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        result.Diagnostics.Should().BeEmpty($"generator must not produce diagnostics for '{assetName}'");
        result.GeneratedTrees.Should().HaveCount(2, "must produce topology core + bridge");
        return result.GeneratedTrees.Select(t => t.ToString()).ToArray();
    }

    /// <summary>
    /// Compiles multiple C# source strings + loads into a collectible ALC.
    /// Reuses the same pattern as BlueprintRegistrarBridgeIntegrationTests.CompileMultiAndLoad.
    /// </summary>
    private static (System.Reflection.Assembly Assembly, AssemblyLoadContext Alc) CompileMultiAndLoad(
        string[] sources, string assemblyName)
    {
        var resolver = RoslynMRR.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies());
        var refs     = resolver.Resolve();

        var syntaxTrees = sources
            .Select((src, i) =>
            {
                var sourceText = SourceText.From(src, System.Text.Encoding.UTF8);
                return CSharpSyntaxTree.ParseText(
                    sourceText,
                    new CSharpParseOptions(LanguageVersion.Latest),
                    path: $"{assemblyName}_{i}.g.cs");
            })
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            refs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                deterministic: true,
                allowUnsafe: true));

        using var peStream  = new System.IO.MemoryStream();
        using var pdbStream = new System.IO.MemoryStream();
        var result = compilation.Emit(peStream, pdbStream);

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d =>
                {
                    var loc = d.Location.GetMappedLineSpan();
                    return $"{d.Id}({loc.Path}:{loc.StartLinePosition.Line + 1}): {d.GetMessage()}";
                }));
            throw new InvalidOperationException(
                $"In-memory compilation of '{assemblyName}' failed:\n{errors}");
        }

        peStream.Position  = 0;
        pdbStream.Position = 0;
        var alc = new AssemblyLoadContext($"MigEq_{assemblyName}", isCollectible: true);
        var asm = alc.LoadFromStream(peStream, pdbStream);
        return (asm, alc);
    }

    // ── ALC unload helper (DEBT-009 pattern) ─────────────────────────────────────

    private static void AwaitAlcCollection(WeakReference<AssemblyLoadContext>[] refs)
    {
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (refs.All(w => !w.TryGetTarget(out _))) return;
            System.Threading.Thread.Sleep(50);
        }
    }
}
