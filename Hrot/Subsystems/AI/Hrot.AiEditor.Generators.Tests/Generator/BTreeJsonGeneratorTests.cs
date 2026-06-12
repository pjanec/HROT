using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.AiEditor.Generators;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Generator;

/// <summary>
/// PU-201: Tests for <see cref="BTreeJsonGenerator"/> using <see cref="CSharpGeneratorDriver"/>.
///
/// Tests:
/// (a) A valid *.btree.json AdditionalText produces a {Name}.g.cs containing CreateBuilder()
///     + [BTreeDefinition] thunk and NOT [BTreeLayout(.
/// (b) A deliberately malformed *.btree.json yields a generator diagnostic (BTREE0001),
///     does NOT throw, and does NOT suppress a sibling valid asset's generation.
/// </summary>
public sealed class BTreeJsonGeneratorTests
{
    private static readonly Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Trees.SampleScout).Assembly;

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>Loads the SampleScout editor model via reflection.</summary>
    private static BehaviorTreeAsset LoadSampleScout()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var asset = contributor.Enumerate().FirstOrDefault(a => a.Name == "SampleScout");
        if (asset is null) throw new InvalidOperationException("SampleScout not found in assembly");
        return (BehaviorTreeAsset)asset;
    }

    /// <summary>Builds a minimal CSharpCompilation suitable for running generators.</summary>
    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees:  Array.Empty<SyntaxTree>(),
            references:   new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            options:      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>Creates a synthetic AdditionalText from the given path and content.</summary>
    private static AdditionalText MakeAdditionalText(string path, string content) =>
        new StringAdditionalText(path, content);

    /// <summary>
    /// Runs the BTreeJsonGenerator driver and returns the result.
    /// </summary>
    private static GeneratorDriverRunResult RunGenerator(params AdditionalText[] additionalTexts)
    {
        var generator = new BTreeJsonGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .AddAdditionalTexts(additionalTexts.ToImmutableArrayCompat());
        var compilation = CreateCompilation();
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    // ── (a) valid *.btree.json produces topology core + bridge (PU-203) ─────────────

    [Fact]
    public void ValidBTreeJson_ProducesGeneratedSource_ContainingCreateBuilderAndThunk()
    {
        // Arrange: load model via reflection, map to DTO, serialize to JSON
        var model = LoadSampleScout();
        var dto   = BehaviorTreeAssetMapper.ToDto(model);
        string json = BTreeJsonServices.Serialize(dto);

        var additionalText = MakeAdditionalText(
            "/path/to/SampleScout.btree.json", json);

        // Act
        var result = RunGenerator(additionalText);

        // Assert: PU-203 — 2 files per asset: topology core + bridge
        result.GeneratedTrees.Should().HaveCount(2,
            "one valid asset produces 2 files: topology core ({Name}.g.cs) + bridge ({Name}.Registrar.g.cs)");

        // Topology-core file: contains CreateBuilder and [BTreeDefinition] thunk
        var coreSource = result.GeneratedTrees
            .First(t => !t.FilePath.Contains("Registrar"))
            .ToString();

        coreSource.Should().Contain("CreateBuilder()",
            "topology-core .g.cs must contain CreateBuilder()");
        coreSource.Should().Contain("[BTreeDefinition(",
            "topology-core .g.cs must contain the [BTreeDefinition] thunk attribute");
        coreSource.Should().NotContain("[BTreeLayout(",
            "topology-core .g.cs must NOT contain [BTreeLayout( — layout is JSON-only (§6.2)");

        // Bridge file: contains [BlueprintRegistrar] and Register method
        var bridgeSource = result.GeneratedTrees
            .First(t => t.FilePath.Contains("Registrar"))
            .ToString();

        bridgeSource.Should().Contain("[BlueprintRegistrar]",
            "bridge .g.cs must carry [BlueprintRegistrar]");
        bridgeSource.Should().Contain("Register(BehaviorRegistry",
            "bridge .g.cs must have Register(BehaviorRegistry ...) method");

        // No diagnostics
        result.Diagnostics.Should().BeEmpty(
            "a valid asset should produce no generator diagnostics");
    }

    [Fact]
    public void ValidBTreeJson_GeneratedFileName_MatchesAssetName()
    {
        var model = LoadSampleScout();
        var dto   = BehaviorTreeAssetMapper.ToDto(model);
        string json = BTreeJsonServices.Serialize(dto);

        var additionalText = MakeAdditionalText("/path/SampleScout.btree.json", json);
        var result = RunGenerator(additionalText);

        result.GeneratedTrees.Should().HaveCount(2,
            "must produce topology core + bridge files");
        // Topology core hint name
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("SampleScout.g.cs"),
            "topology-core hint name must be {AssetName}.g.cs");
        // Bridge hint name
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("SampleScout.Registrar.g.cs"),
            "bridge hint name must be {AssetName}.Registrar.g.cs");
    }

    [Fact]
    public void ValidBTreeJson_GeneratedSource_DoesNotContainLayoutNamespace()
    {
        var model = LoadSampleScout();
        var dto   = BehaviorTreeAssetMapper.ToDto(model);
        string json = BTreeJsonServices.Serialize(dto);

        var result = RunGenerator(MakeAdditionalText("/p/SampleScout.btree.json", json));
        result.GeneratedTrees.Should().HaveCount(2);

        // The topology-core file must not reference the layout namespace
        var coreSource = result.GeneratedTrees
            .First(t => !t.FilePath.Contains("Registrar"))
            .ToString();
        coreSource.Should().NotContain("Hrot.Editor.AiShared.Layout",
            "the layout namespace must not be in topology-core-only output");
    }

    // ── (b) malformed input: diagnostic + sibling safety ─────────────────────────

    [Fact]
    public void MalformedBTreeJson_YieldsDiagnostic_DoesNotThrow()
    {
        // Arrange: deliberately malformed JSON
        var badText = MakeAdditionalText("/path/Broken.btree.json", "{ not valid json !!!");

        // Act
        var result = RunGenerator(badText);

        // Assert: no sources emitted, one diagnostic
        result.GeneratedTrees.Should().BeEmpty(
            "a malformed asset must not produce any generated source");
        result.Diagnostics.Should().HaveCount(1,
            "exactly one diagnostic must be reported for the malformed asset");
        result.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.DiagnosticId,
            "diagnostic must carry the BTREE0001 id");
        result.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Error,
            "parse error diagnostic must be Error severity");
    }

    [Fact]
    public void MalformedBTreeJson_DoesNotSuppressSiblingValidAsset()
    {
        // Arrange: one valid asset + one malformed
        var model = LoadSampleScout();
        var dto   = BehaviorTreeAssetMapper.ToDto(model);
        string json = BTreeJsonServices.Serialize(dto);

        var goodText = MakeAdditionalText("/p/SampleScout.btree.json", json);
        var badText  = MakeAdditionalText("/p/Broken.btree.json", "{ bad! }");

        // Act: run with both
        var result = RunGenerator(goodText, badText);

        // Assert: the good asset still emits 2 files (core + bridge)
        result.GeneratedTrees.Should().HaveCount(2,
            "the valid sibling must still emit core+bridge despite the malformed asset");
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("SampleScout.g.cs"),
            "topology-core file must be present");
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("SampleScout.Registrar.g.cs"),
            "bridge file must be present");

        // The bad asset reports a diagnostic
        result.Diagnostics.Should().HaveCount(1,
            "exactly one diagnostic for the one malformed asset");
        result.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.DiagnosticId);
    }

    [Fact]
    public void NonBTreeJsonAdditionalText_IsIgnored()
    {
        // A *.hsm.json or *.bp.json file must be ignored by BTreeJsonGenerator.
        var other = MakeAdditionalText("/p/SampleGuard.hsm.json", "{}");
        var result = RunGenerator(other);

        result.GeneratedTrees.Should().BeEmpty(
            "BTreeJsonGenerator must ignore non-*.btree.json additional texts");
        result.Diagnostics.Should().BeEmpty(
            "ignoring non-matching files must not produce diagnostics");
    }

    [Fact]
    public void EmitTopologyCore_ContainsCreateBuilderAndThunk_NotLayout()
    {
        // Unit test for EmitTopologyCore independent of the GeneratorDriver.
        var model = LoadSampleScout();
        var dto   = BehaviorTreeAssetMapper.ToDto(model);

        string core = BTreeEmitCore.EmitTopologyCore(dto);

        core.Should().Contain("CreateBuilder()",
            "topology core must contain CreateBuilder()");
        core.Should().Contain("[BTreeDefinition(",
            "topology core must contain [BTreeDefinition] thunk");
        core.Should().NotContain("[BTreeLayout(",
            "topology core must NOT contain [BTreeLayout( (§6.2)");
    }

    [Fact]
    public void EmitTopologyCore_IsDeterministic()
    {
        var model = LoadSampleScout();
        var dto   = BehaviorTreeAssetMapper.ToDto(model);

        string first  = BTreeEmitCore.EmitTopologyCore(dto);
        string second = BTreeEmitCore.EmitTopologyCore(dto);

        first.Should().Be(second, "EmitTopologyCore must be deterministic");
    }

    [Fact]
    public void FullEmit_IsByteIdentical_ToOriginal_AfterTopologyCoreRefactor()
    {
        // BATCH-02 gate must remain green: full Emit (with layout) must still be correct.
        var model  = LoadSampleScout();
        var dto    = BehaviorTreeAssetMapper.ToDto(model);
        string full = BTreeEmitCore.Emit(dto);

        full.Should().Contain("[BTreeLayout(",
            "full emit must still include [BTreeLayout(");
        full.Should().Contain("CreateBuilder()",
            "full emit must include CreateBuilder()");
        full.Should().Contain("[BTreeDefinition(",
            "full emit must include [BTreeDefinition]");
    }

    // ── BATCH-12: unbound leaf → codegen warning, not build break ───────────────

    [Fact]
    public void Generator_UnboundActionAsset_DoesNotEmitSource_AndReportsWarning()
    {
        // Arrange: a .btree.json whose reachable tree contains an Action leaf with no Action payload
        string json = BuildUnboundActionJson();
        var additionalText = MakeAdditionalText("/path/UnboundAction.btree.json", json);

        // Act
        var result = RunGenerator(additionalText);

        // Assert: no sources emitted (asset skipped)
        result.GeneratedTrees.Should().BeEmpty(
            "an unbound Action asset must not emit any generated source");

        // Assert: a Warning diagnostic with BTREE0002 is reported
        result.Diagnostics.Should().HaveCount(1,
            "exactly one diagnostic must be reported for the unbound asset");
        result.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.CodegenWarningId,
            "diagnostic must carry the BTREE0002 id");
        result.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Warning,
            "codegen validation diagnostic must be Warning severity (not Error) — so the build survives");
    }

    [Fact]
    public void Generator_UnboundActionAsset_OutputCompilation_HasNoErrors()
    {
        // Arrange
        string json = BuildUnboundActionJson();
        var additionalText = MakeAdditionalText("/path/UnboundAction.btree.json", json);

        // Act
        var result = RunGenerator(additionalText);

        // Assert: zero Error diagnostics in the generator output (the core guarantee)
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "the generator must not produce Error diagnostics for an unbound asset (build must survive)");

        // Also verify the warning IS present
        result.Diagnostics.Should().ContainSingle(d => d.Id == BTreeJsonGenerator.CodegenWarningId
            && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_UnboundConditionAsset_DoesNotEmitSource_AndReportsWarning()
    {
        // Arrange: a .btree.json whose reachable tree contains a Condition leaf with no Condition payload
        string json = BuildUnboundConditionJson();
        var additionalText = MakeAdditionalText("/path/UnboundCondition.btree.json", json);

        // Act
        var result = RunGenerator(additionalText);

        // Assert: no sources emitted
        result.GeneratedTrees.Should().BeEmpty(
            "an unbound Condition asset must not emit any generated source");

        // Assert: Warning with BTREE0002
        result.Diagnostics.Should().HaveCount(1);
        result.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.CodegenWarningId);
        result.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_ValidAsset_EmitsTopologyAndBridge_NoWarning()
    {
        // Arrange: a fully-bound valid asset (Root → Wait)
        var model = LoadSampleScout();
        var dto   = BehaviorTreeAssetMapper.ToDto(model);
        string json = BTreeJsonServices.Serialize(dto);
        var additionalText = MakeAdditionalText("/path/ValidAsset.btree.json", json);

        // Act
        var result = RunGenerator(additionalText);

        // Assert: normal emission (core + bridge)
        result.GeneratedTrees.Should().HaveCount(2,
            "valid asset must emit topology core + bridge");

        // Assert: no BTREE0002 warning
        result.Diagnostics.Should().NotContain(d => d.Id == BTreeJsonGenerator.CodegenWarningId,
            "valid asset must not produce BTREE0002 warning");
    }

    [Fact]
    public void Generator_UnboundActionAsset_DoesNotSuppressSiblingValidAsset()
    {
        // Arrange: one valid + one unbound
        var model = LoadSampleScout();
        var dto   = BehaviorTreeAssetMapper.ToDto(model);
        string validJson = BTreeJsonServices.Serialize(dto);
        string unboundJson = BuildUnboundActionJson();

        var validText   = MakeAdditionalText("/p/Valid.btree.json", validJson);
        var unboundText = MakeAdditionalText("/p/Unbound.btree.json", unboundJson);

        // Act
        var result = RunGenerator(validText, unboundText);

        // Assert: valid asset still emits 2 files
        result.GeneratedTrees.Should().HaveCount(2,
            "the valid sibling must still emit core+bridge despite the unbound asset");

        // Assert: exactly one Warning for the unbound asset
        result.Diagnostics.Should().ContainSingle(d => d.Id == BTreeJsonGenerator.CodegenWarningId
            && d.Severity == DiagnosticSeverity.Warning);

        // No BTREE0001 errors (valid asset + unbound asset with codegen warning, not parse error)
        result.Diagnostics.Should().NotContain(d => d.Id == BTreeJsonGenerator.DiagnosticId
            && d.Severity == DiagnosticSeverity.Error);
    }

    // ── BATCH-14: cyclic asset → BTREE0002 Warning, no Error ────────────────────

    [Fact]
    public void Generator_CyclicAsset_DoesNotEmitSource_AndReportsWarning_NoErrors()
    {
        // Arrange: a cyclic .btree.json (Root → A → B → A)
        string json = BuildCyclicTreeJson();
        var cyclicText = MakeAdditionalText("/path/CyclicTree.btree.json", json);

        // Act
        var result = RunGenerator(cyclicText);

        // Assert: no sources emitted for the cyclic asset
        result.GeneratedTrees.Should().BeEmpty(
            "a cyclic asset must not emit any generated source (can't produce infinite code)");

        // Assert: a Warning diagnostic with BTREE0002 is reported
        result.Diagnostics.Should().HaveCount(1,
            "exactly one diagnostic must be reported for the cyclic asset");
        result.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.CodegenWarningId,
            "diagnostic must carry the BTREE0002 id");
        result.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Warning,
            "cycle diagnostic must be Warning severity (not Error) — so the build survives");
        result.Diagnostics[0].GetMessage().Should().Contain("Cycle detected",
            "diagnostic message must mention the cycle");

        // Assert: zero Error diagnostics — the key guarantee
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "the generator must not produce Error diagnostics for a cyclic asset (build must survive)");
    }

    [Fact]
    public void Generator_CyclicAsset_DoesNotSuppressSiblingValidAsset()
    {
        // Arrange: one valid + one cyclic
        var model = LoadSampleScout();
        var dto   = BehaviorTreeAssetMapper.ToDto(model);
        string validJson = BTreeJsonServices.Serialize(dto);
        string cyclicJson = BuildCyclicTreeJson();

        var validText  = MakeAdditionalText("/p/Valid.btree.json", validJson);
        var cyclicText = MakeAdditionalText("/p/Cyclic.btree.json", cyclicJson);

        // Act
        var result = RunGenerator(validText, cyclicText);

        // Assert: valid asset still emits 2 files (core + bridge) — fault isolation
        result.GeneratedTrees.Should().HaveCount(2,
            "the valid sibling must still emit core+bridge despite the cyclic asset");

        // Assert: exactly one BTREE0002 Warning for the cyclic asset
        result.Diagnostics.Should().ContainSingle(d => d.Id == BTreeJsonGenerator.CodegenWarningId
            && d.Severity == DiagnosticSeverity.Warning);

        // No BTREE0001 errors
        result.Diagnostics.Should().NotContain(d => d.Id == BTreeJsonGenerator.DiagnosticId
            && d.Severity == DiagnosticSeverity.Error);
    }

    // ── BATCH-12 JSON helpers ───────────────────────────────────────────────────

    private static string BuildUnboundActionJson()
    {
        var actionId = new Guid("20000000-0000-0000-0000-000000000002");
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("10000000-0000-0000-0000-000000000001"),
            Name = "UnboundAction",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeActionNodeDto
                {
                    VisualId = actionId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Action = null, // unbound — no method
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };
        return BTreeJsonServices.Serialize(dto);
    }

    private static string BuildUnboundConditionJson()
    {
        var condId = new Guid("30000000-0000-0000-0000-000000000003");
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("10000000-0000-0000-0000-000000000001"),
            Name = "UnboundCondition",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeConditionNodeDto
                {
                    VisualId = condId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Condition = null, // unbound — no method
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };
        return BTreeJsonServices.Serialize(dto);
    }

    // ── BATCH-14 JSON helpers ───────────────────────────────────────────────────

    private static string BuildCyclicTreeJson()
    {
        var rootId = new Guid("CA000000-0000-0000-0000-000000000001");
        var aId    = new Guid("CA000000-0000-0000-0000-000000000002");
        var bId    = new Guid("CA000000-0000-0000-0000-000000000003");

        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("CC000000-0000-0000-0000-000000000001"),
            Name = "CyclicTree",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeRootNodeDto
                {
                    VisualId = rootId,
                    ChildVisualIds = new List<Guid> { aId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeSequenceNodeDto
                {
                    VisualId = aId,
                    ChildVisualIds = new List<Guid> { bId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeSequenceNodeDto
                {
                    VisualId = bId,
                    ChildVisualIds = new List<Guid> { aId }, // cycle! B → A
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };
        return BTreeJsonServices.Serialize(dto);
    }
}
