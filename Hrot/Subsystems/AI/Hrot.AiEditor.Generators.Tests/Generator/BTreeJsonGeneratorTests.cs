using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using FluentAssertions;
using Hrot.AiEditor.Generators;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
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

    // ── BATCH-17: method compatibility validation ───────────────────────────────

    /// <summary>
    /// Runs the generator with a compilation that contains the given stub source code.
    /// The stub source should define the blackboard/context types and the action/condition methods.
    /// </summary>
    private static GeneratorDriverRunResult RunGeneratorWithStubs(
        string stubSource, params AdditionalText[] additionalTexts)
    {
        // Parse the stub source into a syntax tree.
        var stubTree = CSharpSyntaxTree.ParseText(stubSource);

        // Build a compilation with:
        //   - the stub source (defines test types + methods)
        //   - mscorlib / netstandard references
        //   - the Fbt.Kernel assembly (provides Fbt.NodeStatus, Fbt.BehaviorTreeState)
        var references = new System.Collections.Generic.List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            // Fbt.NodeStatus lives in the assembly that contains NodeLogicDelegate.
            MetadataReference.CreateFromFile(typeof(Fbt.NodeLogicDelegate<,>).Assembly.Location),
        };

        // Add transitive references needed by Fbt.Kernel (e.g. System.Runtime).
        foreach (var asmName in new[] { "System.Runtime", "netstandard" })
        {
            try
            {
                var asm = System.Reflection.Assembly.Load(asmName);
                references.Add(MetadataReference.CreateFromFile(asm.Location));
            }
            catch { /* not present in all TFMs — ignore */ }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "StubAssembly",
            syntaxTrees:  new[] { stubTree },
            references:   references,
            options:      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new BTreeJsonGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .AddAdditionalTexts(additionalTexts.ToImmutableArrayCompat());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    // Shared stubs: defines TBB = StubBb, TCtx = StubCtx,
    // plus a valid method (CompatAction) and several invalid ones.
    private const string ValidMethodStubs = @"
using Fbt;

namespace Stub
{
    public struct StubBb { }
    public struct StubCtx { }

    public static class StubNodes
    {
        // VALID: matches NodeLogicDelegate<StubBb, StubCtx> exactly.
        public static NodeStatus CompatAction(
            ref StubBb blackboard,
            ref BehaviorTreeState state,
            ref StubCtx ctx,
            int paramIndex) => NodeStatus.Running;

        // INVALID: param 0 is a DTO struct, not the declared blackboard type.
        public struct SomeDtoParam { }
        public static NodeStatus DtoParamAction(
            ref SomeDtoParam dto,
            ref BehaviorTreeState state,
            ref StubCtx ctx,
            int paramIndex) => NodeStatus.Running;

        // INVALID: param 0 matches the blackboard but wrong arity (3 params instead of 4).
        public static NodeStatus WrongArityAction(
            ref StubBb blackboard,
            ref BehaviorTreeState state,
            int paramIndex) => NodeStatus.Running;

        // INVALID: returns void instead of NodeStatus.
        public static void WrongReturnAction(
            ref StubBb blackboard,
            ref BehaviorTreeState state,
            ref StubCtx ctx,
            int paramIndex) { }
    }
}
";

    private static string BuildBoundActionJson(string methodFqn,
        BTreeDelegateShapeDto shape = BTreeDelegateShapeDto.FourParamFull)
    {
        var actionId = new Guid("BB170000-0000-0000-0000-000000000001");
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("BB170000-0000-0000-0000-AABBCCDD0001"),
            Name = "BoundActionAsset",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Stub.StubBb",
            ContextTypeName    = "Stub.StubCtx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeActionNodeDto
                {
                    VisualId = actionId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Action = new BTreeActionPayloadDto
                    {
                        MethodFqn = methodFqn,
                        DelegateShape = shape,
                    },
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };
        return BTreeJsonServices.Serialize(dto);
    }

    private static string BuildBoundConditionJson(string methodFqn,
        BTreeDelegateShapeDto shape = BTreeDelegateShapeDto.FourParamFull)
    {
        var condId = new Guid("BB170000-0000-0000-0000-000000000002");
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("BB170000-0000-0000-0000-AABBCCDD0002"),
            Name = "BoundConditionAsset",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Stub.StubBb",
            ContextTypeName    = "Stub.StubCtx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeConditionNodeDto
                {
                    VisualId = condId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Condition = new BTreeConditionPayloadDto
                    {
                        MethodFqn = methodFqn,
                        DelegateShape = shape,
                    },
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
        };
        return BTreeJsonServices.Serialize(dto);
    }

    [Fact]
    public void Generator_IncompatibleBoundMethod_DtoParam_SkipsAndWarns_NoErrors()
    {
        // Action leaf binds a method whose first param is a DTO, not the blackboard type.
        string json = BuildBoundActionJson("Stub.StubNodes.DtoParamAction");
        var result = RunGeneratorWithStubs(ValidMethodStubs,
            MakeAdditionalText("/p/DtoParam.btree.json", json));

        result.GeneratedTrees.Should().BeEmpty(
            "an Action with a DTO-param method must not emit any source");
        result.Diagnostics.Should().HaveCount(1);
        result.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.CodegenWarningId,
            "incompatible binding must produce BTREE0002 Warning");
        result.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Warning);

        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty("zero Error diagnostics — build must survive");
    }

    [Fact]
    public void Generator_IncompatibleBoundCondition_DtoParam_SkipsAndWarns_NoErrors()
    {
        // Condition leaf binds a method whose first param is a DTO, not the blackboard type.
        string json = BuildBoundConditionJson("Stub.StubNodes.DtoParamAction");
        var result = RunGeneratorWithStubs(ValidMethodStubs,
            MakeAdditionalText("/p/DtoParamCond.btree.json", json));

        result.GeneratedTrees.Should().BeEmpty(
            "a Condition with a DTO-param method must not emit any source");
        result.Diagnostics.Should().HaveCount(1);
        result.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.CodegenWarningId);
        result.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Warning);

        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty("zero Error diagnostics — build must survive");
    }

    [Fact]
    public void Generator_UnresolvedMethod_SkipsAndWarns()
    {
        // MethodFqn points to a method that doesn't exist in the compilation.
        string json = BuildBoundActionJson("Stub.StubNodes.NonExistentMethod");
        var result = RunGeneratorWithStubs(ValidMethodStubs,
            MakeAdditionalText("/p/Unresolved.btree.json", json));

        result.GeneratedTrees.Should().BeEmpty(
            "an asset binding an unresolved method must not emit any source");
        result.Diagnostics.Should().HaveCount(1);
        result.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.CodegenWarningId,
            "unresolved method must produce BTREE0002 Warning");
        result.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Warning);

        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty("zero Error diagnostics");
    }

    [Fact]
    public void Generator_CompatibleBoundMethod_EmitsNormally()
    {
        // Action leaf binds a fully-compatible NodeLogicDelegate<StubBb,StubCtx> method.
        string json = BuildBoundActionJson("Stub.StubNodes.CompatAction");
        var result = RunGeneratorWithStubs(ValidMethodStubs,
            MakeAdditionalText("/p/CompatAction.btree.json", json));

        result.GeneratedTrees.Should().HaveCount(2,
            "a compatible bound method must emit topology core + bridge normally");
        result.Diagnostics.Should().NotContain(d => d.Id == BTreeJsonGenerator.CodegenWarningId,
            "a compatible bound method must not produce BTREE0002");
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("zero Error diagnostics");
    }

    [Fact]
    public void Generator_IncompatibleAsset_DoesNotSuppressValidSibling()
    {
        // One incompatible asset + one valid (compatible) asset in the same run.
        string badJson   = BuildBoundActionJson("Stub.StubNodes.DtoParamAction");
        string goodJson  = BuildBoundActionJson("Stub.StubNodes.CompatAction");

        // Rename to avoid hint-name collision.
        var goodDto = BTreeJsonServices.Deserialize(goodJson)!;
        goodDto.Name = "GoodAsset";
        string renamedGood = BTreeJsonServices.Serialize(goodDto);

        var result = RunGeneratorWithStubs(ValidMethodStubs,
            MakeAdditionalText("/p/Bad.btree.json", badJson),
            MakeAdditionalText("/p/GoodAsset.btree.json", renamedGood));

        result.GeneratedTrees.Should().HaveCount(2,
            "the valid sibling must still emit core+bridge despite the incompatible asset");
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("GoodAsset.g.cs"),
            "topology-core file for valid sibling must be present");
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("GoodAsset.Registrar.g.cs"),
            "bridge file for valid sibling must be present");

        result.Diagnostics.Should().ContainSingle(d =>
            d.Id == BTreeJsonGenerator.CodegenWarningId &&
            d.Severity == DiagnosticSeverity.Warning,
            "exactly one BTREE0002 Warning for the incompatible asset");
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("zero Error diagnostics");
    }

    [Fact]
    public void Generator_WrongArityOrReturn_IsInvalid()
    {
        // Wrong arity (3 params instead of 4) — proves it's a real signature check.
        string wrongArityJson = BuildBoundActionJson("Stub.StubNodes.WrongArityAction");
        var arityResult = RunGeneratorWithStubs(ValidMethodStubs,
            MakeAdditionalText("/p/WrongArity.btree.json", wrongArityJson));

        arityResult.GeneratedTrees.Should().BeEmpty("wrong-arity method must not emit");
        arityResult.Diagnostics.Should().HaveCount(1);
        arityResult.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.CodegenWarningId);
        arityResult.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Warning);

        // Wrong return type (void instead of NodeStatus).
        string wrongReturnJson = BuildBoundActionJson("Stub.StubNodes.WrongReturnAction");
        var returnResult = RunGeneratorWithStubs(ValidMethodStubs,
            MakeAdditionalText("/p/WrongReturn.btree.json", wrongReturnJson));

        returnResult.GeneratedTrees.Should().BeEmpty("wrong-return-type method must not emit");
        returnResult.Diagnostics.Should().HaveCount(1);
        returnResult.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.CodegenWarningId);
        returnResult.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Warning);
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

    // ── BATCH-02 S1-2: per-asset blackboard struct + topology-over-struct ────────

    /// <summary>
    /// S1-2: Packing {int A; Vector3 B; bool C} must produce offsets that match
    /// both <see cref="BTreeBlackboardPackHelper"/> (string-based) and
    /// <see cref="BlackboardBinPacker"/> (runtime Type-based).
    /// Vector3 alignment = min(12, 8) = 8 → A at 0 (int, size 4), B at 8 (align to 8), C at 20.
    /// </summary>
    [Fact]
    public void ManagedAsset_GeneratesStruct_OffsetsMatchBinPacker()
    {
        var vars = new List<BlackboardVariableDto>
        {
            new BlackboardVariableDto { Name = "A", Type = new BlackboardTypeRefDto { TypeId = "System.Int32" } },
            new BlackboardVariableDto { Name = "B", Type = new BlackboardTypeRefDto { TypeId = "System.Numerics.Vector3" } },
            new BlackboardVariableDto { Name = "C", Type = new BlackboardTypeRefDto { TypeId = "System.Boolean" } },
        };

        var dto = BuildManagedDto("TestStruct", vars);
        var structSource = BTreeEmitCore.EmitBlackboardStructSource(dto, out var packedFields);

        // Struct source must be emitted.
        structSource.Should().NotBeNull("managed asset must emit a blackboard struct");
        packedFields.Should().HaveCount(3, "three variables");

        // Expected offsets:  A=0 (int,4), B=8 (Vector3, align=8 → pad 4 bytes), C=20 (bool, align=1)
        packedFields[0].Name.Should().Be("A"); packedFields[0].ByteOffset.Should().Be(0);
        packedFields[1].Name.Should().Be("B"); packedFields[1].ByteOffset.Should().Be(8);
        packedFields[2].Name.Should().Be("C"); packedFields[2].ByteOffset.Should().Be(20);

        // Cross-check with runtime BlackboardBinPacker.
        var runtimeDescriptors = new List<BlackboardVariableDescriptor>
        {
            new("A", typeof(int)),
            new("B", typeof(System.Numerics.Vector3)),
            new("C", typeof(bool)),
        };
        var packResult = BlackboardBinPacker.Pack(runtimeDescriptors);
        packResult.Variables[0].ByteOffset.Should().Be(packedFields[0].ByteOffset, "A runtime offset must match helper");
        packResult.Variables[1].ByteOffset.Should().Be(packedFields[1].ByteOffset, "B runtime offset must match helper");
        packResult.Variables[2].ByteOffset.Should().Be(packedFields[2].ByteOffset, "C runtime offset must match helper");

        // bool field must have [MarshalAs(UnmanagedType.I1)] in the struct source.
        structSource!.Should().Contain("[MarshalAs(UnmanagedType.I1)]",
            "bool fields require [MarshalAs(UnmanagedType.I1)] for sequential layout correctness");

        // Total must be within 100-byte inline budget.
        packResult.TotalInlineBytes.Should().BeLessOrEqualTo(BlackboardBinPacker.MaxInlineBytes,
            "test fixture must fit in the inline budget");
    }

    /// <summary>
    /// S1-2: Topology emitted for a managed asset must use <c>"{MethodFqn}@{offset}"</c> as the
    /// Action blob key, not the field-selector lambda form.
    /// Builds a Sequence [Condition, Action] with both nodes bound to managed-blackboard variables.
    /// </summary>
    [Fact]
    public void ManagedAsset_TopologyBuiltOverGeneratedStruct_BlobKeysCarryOffsets()
    {
        // Two variables: Counter at offset 0 (int, 4), Threshold at offset 4 (int, 4).
        var vars = new List<BlackboardVariableDto>
        {
            new BlackboardVariableDto { Name = "Counter",   Type = new BlackboardTypeRefDto { TypeId = "System.Int32" } },
            new BlackboardVariableDto { Name = "Threshold", Type = new BlackboardTypeRefDto { TypeId = "System.Int32" } },
        };

        var rootId      = new Guid("B2000000-0000-0000-0000-000000000000");
        var seqId       = new Guid("B2000000-0000-0000-0000-000000000001");
        var actionId    = new Guid("B2000000-0000-0000-0000-000000000002");
        var conditionId = new Guid("B2000000-0000-0000-0000-000000000003");
        const string actionFqn    = "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_IncrementCounter";
        const string conditionFqn = "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Condition_CounterBelowThreshold";

        // Root → Sequence [Condition(Counter@0), Action(Threshold@4)]
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("B0000000-0000-0000-0000-000000000001"),
            Name = "CounterTree",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeRootNodeDto
                {
                    VisualId = rootId,
                    ChildVisualIds = new List<Guid> { seqId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeSequenceNodeDto
                {
                    VisualId = seqId,
                    ChildVisualIds = new List<Guid> { conditionId, actionId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeConditionNodeDto
                {
                    VisualId = conditionId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Condition = new BTreeConditionPayloadDto
                    {
                        MethodFqn = conditionFqn,
                        DelegateShape = BTreeDelegateShapeDto.ThreeParamReusable,
                        ExpressionTargetField = "Counter",   // offset 0
                    },
                },
                new BTreeActionNodeDto
                {
                    VisualId = actionId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Action = new BTreeActionPayloadDto
                    {
                        MethodFqn = actionFqn,
                        DelegateShape = BTreeDelegateShapeDto.ThreeParamReusable,
                        ExpressionTargetField = "Threshold",  // offset 4
                    },
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
            Blackboard = new BlackboardBlockDto
            {
                Managed = true,
                TypeName = "CounterTreeBlackboard",
                Variables = vars,
            },
        };

        string topology = BTreeEmitCore.EmitTopologyCore(dto);

        // Condition bound to Counter (offset 0) → key must contain "@0"
        topology.Should().Contain($"\"{conditionFqn}@0\"",
            "condition bound to Counter at offset 0 must use key {MethodFqn}@0");

        // Action bound to Threshold (offset 4) → key must contain "@4"
        topology.Should().Contain($"\"{actionFqn}@4\"",
            "action bound to Threshold at offset 4 must use key {MethodFqn}@4");

        // Must NOT contain the old field-selector form.
        topology.Should().NotContain("dto => dto.Counter",
            "managed assets must use offset-key form, not field-selector lambda");
        topology.Should().NotContain("dto => dto.Threshold",
            "managed assets must use offset-key form, not field-selector lambda");
    }

    /// <summary>
    /// Corrective round (BATCH-02 review): verify the EMITTED registrar source for a managed asset,
    /// not the hand-rolled mechanism. Runs <see cref="BTreeBridgeEmitCore.EmitBridge"/> on the same
    /// Counter@0 / Threshold@4 shape as
    /// <see cref="ManagedAsset_TopologyBuiltOverGeneratedStruct_BlobKeysCarryOffsets"/> and asserts:
    /// (a) the registrar registers under the SAME keys the topology blob uses
    ///     ("{conditionFqn}@0", "{actionFqn}@4") — proving blob key == registry key, and
    /// (b) each thunk projects at the baked offset
    ///     (Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)0) for @0,
    ///      and (nint)4 for @4) — proving the offset is wired in, not @0 for everything.
    /// </summary>
    [Fact]
    public void ManagedAsset_Registrar_RegistersBakedOffsetThunks()
    {
        // Two variables: Counter at offset 0 (int, 4), Threshold at offset 4 (int, 4).
        var vars = new List<BlackboardVariableDto>
        {
            new BlackboardVariableDto { Name = "Counter",   Type = new BlackboardTypeRefDto { TypeId = "System.Int32" } },
            new BlackboardVariableDto { Name = "Threshold", Type = new BlackboardTypeRefDto { TypeId = "System.Int32" } },
        };

        var rootId      = new Guid("B2000000-0000-0000-0000-000000000000");
        var seqId       = new Guid("B2000000-0000-0000-0000-000000000001");
        var actionId    = new Guid("B2000000-0000-0000-0000-000000000002");
        var conditionId = new Guid("B2000000-0000-0000-0000-000000000003");
        const string actionFqn    = "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_IncrementCounter";
        const string conditionFqn = "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Condition_CounterBelowThreshold";

        // Root → Sequence [Condition(Counter@0), Action(Threshold@4)]
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("B0000000-0000-0000-0000-000000000001"),
            Name = "CounterTree",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeRootNodeDto
                {
                    VisualId = rootId,
                    ChildVisualIds = new List<Guid> { seqId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeSequenceNodeDto
                {
                    VisualId = seqId,
                    ChildVisualIds = new List<Guid> { conditionId, actionId },
                    EditorMetadata = new NodeEditorMetadataDto(),
                },
                new BTreeConditionNodeDto
                {
                    VisualId = conditionId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Condition = new BTreeConditionPayloadDto
                    {
                        MethodFqn = conditionFqn,
                        DelegateShape = BTreeDelegateShapeDto.ThreeParamReusable,
                        ExpressionTargetField = "Counter",   // offset 0
                    },
                },
                new BTreeActionNodeDto
                {
                    VisualId = actionId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Action = new BTreeActionPayloadDto
                    {
                        MethodFqn = actionFqn,
                        DelegateShape = BTreeDelegateShapeDto.ThreeParamReusable,
                        ExpressionTargetField = "Threshold",  // offset 4
                    },
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
            Blackboard = new BlackboardBlockDto
            {
                Managed = true,
                TypeName = "CounterTreeBlackboard",
                Variables = vars,
            },
        };

        string registrar = BTreeBridgeEmitCore.EmitBridge(dto);

        // (a) Registration keys must equal the topology blob keys.
        registrar.Should().Contain($"\"{conditionFqn}@0\"",
            "condition registry key must be {conditionFqn}@0 (== blob key)");
        registrar.Should().Contain($"\"{actionFqn}@4\"",
            "action registry key must be {actionFqn}@4 (== blob key)");

        // (b) Baked offset must be wired into each thunk projection.
        registrar.Should().Contain("Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)0)",
            "the @0 binding must project at byte offset 0");
        registrar.Should().Contain("Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)4)",
            "the @4 binding must project at byte offset 4 (not @0 for everything)");
    }

    /// <summary>
    /// S1-2: A managed asset whose variables exceed 100 bytes must be SKIPPED BY THE GENERATOR
    /// with a BTREE0002 Warning — never a hard build break, and no oversized struct emitted.
    ///
    /// Corrective round (BATCH-02 review): this test now RUNS THE GENERATOR on the 13×Vector3
    /// managed asset (same harness as ManagedAsset_Generator_EmitsThreeFiles_*) instead of only
    /// poking WouldOverflow/Pack directly, so it verifies the real generator path (the previous
    /// version never exercised GenerateOneAsset and so could not catch a silent oversized emit).
    /// </summary>
    [Fact]
    public void ManagedAsset_MasterDtoOver100Bytes_HardErrors()
    {
        // Build a managed DTO with 13 × Vector3 (13 × 12 = 156 bytes > 100).
        var vars = new List<BlackboardVariableDto>();
        for (int i = 0; i < 13; i++)
        {
            vars.Add(new BlackboardVariableDto
            {
                Name = $"V{i}",
                Type = new BlackboardTypeRefDto { TypeId = "System.Numerics.Vector3" },
            });
        }

        // Small standalone sanity check on the pack helper (kept from the original test).
        bool overflow = BTreeBlackboardPackHelper.WouldOverflow(vars, out string? unknownType);
        overflow.Should().BeTrue("13 Vector3 fields (156 bytes) exceeds 100-byte inline budget");
        unknownType.Should().BeNull("Vector3 is a known type");

        // Build a managed asset (Wait node — no method binding, avoids validator interference)
        // carrying the oversized blackboard, then RUN THE GENERATOR on it.
        var waitId = new Guid("EE000000-0000-0000-0000-000000000001");
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("EE000000-0000-0000-0000-000000000001"),
            Name = "OverflowTree",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeWaitNodeDto
                {
                    VisualId = waitId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Wait = new BTreeWaitPayloadDto { Duration = 1.0f },
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
            Blackboard = new BlackboardBlockDto
            {
                Managed = true,
                TypeName = "OverflowTreeBlackboard",
                Variables = vars,
            },
        };
        string json = BTreeJsonServices.Serialize(dto);

        var result = RunGenerator(MakeAdditionalText("/p/OverflowTree.btree.json", json));

        // (a) A BTREE0002 Warning diagnostic must be reported (asset skipped, build survives).
        result.Diagnostics.Should().ContainSingle(d =>
            d.Id == BTreeJsonGenerator.CodegenWarningId &&
            d.Severity == DiagnosticSeverity.Warning,
            "an oversized managed blackboard must produce exactly one BTREE0002 Warning");
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("overflow must never be a hard build break (Error)");

        // (b) NO blackboard struct file may be emitted for the skipped asset.
        result.GeneratedTrees.Should().NotContain(
            t => t.FilePath.EndsWith("OverflowTree.Blackboard.g.cs"),
            "an oversized managed asset must not emit a (oversized) blackboard struct");
        // The whole asset is skipped — no topology/bridge either.
        result.GeneratedTrees.Should().BeEmpty(
            "an oversized managed asset is skipped entirely (no topology, struct, or bridge)");
    }

    /// <summary>
    /// S1-2: A non-managed asset must NOT generate a blackboard struct.
    /// </summary>
    [Fact]
    public void NonManagedAsset_DoesNotGenerateBlackboardStruct()
    {
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("AA000000-0000-0000-0000-000000000001"),
            Name = "NonManaged",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Blackboard = new BlackboardBlockDto
            {
                Managed = false,  // Category-1: hand-written struct
                TypeName = "MyHandWrittenStruct",
                Variables = new List<BlackboardVariableDto>
                {
                    new BlackboardVariableDto { Name = "X", Type = new BlackboardTypeRefDto { TypeId = "System.Int32" } },
                },
            },
        };

        var structSource = BTreeEmitCore.EmitBlackboardStructSource(dto, out var packedFields);
        structSource.Should().BeNull("non-managed assets must not emit a blackboard struct");
        packedFields.Should().BeEmpty("non-managed assets must return empty packed fields");
    }

    /// <summary>
    /// S1-2: Generator must emit 3 files for a managed asset: topology core + blackboard struct + bridge.
    /// </summary>
    [Fact]
    public void ManagedAsset_Generator_EmitsThreeFiles_TopologyCoreBlackboardBridge()
    {
        var vars = new List<BlackboardVariableDto>
        {
            new BlackboardVariableDto { Name = "Counter", Type = new BlackboardTypeRefDto { TypeId = "System.Int32" } },
        };
        // Use a Wait node (no method binding needed — avoids validator issue).
        var waitId = new Guid("DD000000-0000-0000-0000-000000000001");
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("DD000000-0000-0000-0000-000000000001"),
            Name = "ManagedWaitTree",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeWaitNodeDto
                {
                    VisualId = waitId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Wait = new BTreeWaitPayloadDto { Duration = 1.0f },
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
            Blackboard = new BlackboardBlockDto
            {
                Managed = true,
                TypeName = "ManagedWaitTreeBlackboard",
                Variables = vars,
            },
        };
        string json = BTreeJsonServices.Serialize(dto);

        var result = RunGenerator(MakeAdditionalText("/p/ManagedWaitTree.btree.json", json));

        result.GeneratedTrees.Should().HaveCount(3,
            "managed asset must emit 3 files: topology core + blackboard struct + bridge");
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("ManagedWaitTree.g.cs"),
            "topology-core file must be present");
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("ManagedWaitTree.Blackboard.g.cs"),
            "blackboard struct file must be present");
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("ManagedWaitTree.Registrar.g.cs"),
            "bridge file must be present");

        // Struct file must contain the struct definition.
        var structSource = result.GeneratedTrees
            .First(t => t.FilePath.EndsWith("ManagedWaitTree.Blackboard.g.cs"))
            .ToString();
        structSource.Should().Contain("[StructLayout(LayoutKind.Sequential)]",
            "struct source must carry [StructLayout(Sequential)]");
        structSource.Should().Contain("ManagedWaitTreeBlackboard",
            "struct source must use the TypeName from the DTO");
        structSource.Should().Contain("public int Counter;",
            "struct source must contain the Counter field");

        result.Diagnostics.Should().BeEmpty("no diagnostics for a valid managed asset");
    }

    // ── BATCH-02 S1-4: ThreeParamReusable validator unblock ────────────────────

    // Stubs for S1-4 tests: DemoCounterParams DTO + 3-param action/condition.
    private const string ThreeParamStubs = @"
using System.Runtime.InteropServices;
using Fbt;

namespace Stub
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DemoCounterParams
    {
        public int Counter;
        public int Threshold;
    }

    public struct StubCtx { }

    public static class DemoCounterNodes
    {
        public static NodeStatus Action_IncrementCounter(
            ref DemoCounterParams p,
            ref BehaviorTreeState state,
            ref StubCtx ctx) => NodeStatus.Success;

        public static NodeStatus Condition_CounterBelowThreshold(
            ref DemoCounterParams p,
            ref BehaviorTreeState state,
            ref StubCtx ctx) => p.Counter < p.Threshold ? NodeStatus.Success : NodeStatus.Failure;

        // WRONG: type mismatch — takes StubBb, not DemoCounterParams.
        public struct StubBb { }
        public static NodeStatus WrongTypeAction(
            ref StubBb bb,
            ref BehaviorTreeState state,
            ref StubCtx ctx) => NodeStatus.Running;

        // WRONG: 4 params instead of 3.
        public static NodeStatus FourParamAction(
            ref DemoCounterParams p,
            ref BehaviorTreeState state,
            ref StubCtx ctx,
            int extra) => NodeStatus.Success;
    }
}
";

    private static BehaviorTreeAssetDto BuildManagedThreeParamDto(
        string methodFqn,
        string expressionTargetField,
        bool isCondition = false)
    {
        var actionId = new Guid("E1000000-0000-0000-0000-000000000001");
        var nodes = new List<BTreeNodeDto>();
        if (!isCondition)
        {
            nodes.Add(new BTreeActionNodeDto
            {
                VisualId = actionId,
                ChildVisualIds = new List<Guid>(),
                EditorMetadata = new NodeEditorMetadataDto(),
                Action = new BTreeActionPayloadDto
                {
                    MethodFqn = methodFqn,
                    DelegateShape = BTreeDelegateShapeDto.ThreeParamReusable,
                    ExpressionTargetField = expressionTargetField,
                },
            });
        }
        else
        {
            nodes.Add(new BTreeConditionNodeDto
            {
                VisualId = actionId,
                ChildVisualIds = new List<Guid>(),
                EditorMetadata = new NodeEditorMetadataDto(),
                Condition = new BTreeConditionPayloadDto
                {
                    MethodFqn = methodFqn,
                    DelegateShape = BTreeDelegateShapeDto.ThreeParamReusable,
                    ExpressionTargetField = expressionTargetField,
                },
            });
        }

        return new BehaviorTreeAssetDto
        {
            AssetId = new Guid("E1000000-0000-0000-0000-AABBCCDD0001"),
            Name = "ThreeParamAsset",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Stub.DemoCounterParams",
            ContextTypeName = "Stub.StubCtx",
            Nodes = nodes,
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
            Blackboard = new BlackboardBlockDto
            {
                Managed = true,
                TypeName = "DemoCounterParams",
                Variables = new List<BlackboardVariableDto>
                {
                    new BlackboardVariableDto { Name = "Counter",   Type = new BlackboardTypeRefDto { TypeId = "Stub.DemoCounterParams" } },
                    new BlackboardVariableDto { Name = "Threshold", Type = new BlackboardTypeRefDto { TypeId = "Stub.DemoCounterParams" } },
                },
            },
        };
    }

    [Fact]
    public void ThreeParamReusable_TypeMatched_Validates()
    {
        // Action with matching 3-param method: should emit normally (no BTREE0002).
        var dto = BuildManagedThreeParamDto(
            "Stub.DemoCounterNodes.Action_IncrementCounter",
            "Counter");
        string json = BTreeJsonServices.Serialize(dto);

        var result = RunGeneratorWithStubs(ThreeParamStubs,
            MakeAdditionalText("/p/ThreeParam.btree.json", json));

        // No BTREE0002 warning — the 3-param binding validates correctly.
        result.Diagnostics.Should().NotContain(d => d.Id == BTreeJsonGenerator.CodegenWarningId,
            "a type-matched ThreeParamReusable binding must not produce BTREE0002");
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("zero Error diagnostics");
    }

    [Fact]
    public void ThreeParamReusable_TypeMismatch_SkipsWithBtree0002()
    {
        // Binds WrongTypeAction whose param-0 is StubBb, not DemoCounterParams.
        // The blackboard variable "Counter" has TypeId "Stub.DemoCounterParams", so mismatch.
        var dto = BuildManagedThreeParamDto(
            "Stub.DemoCounterNodes.WrongTypeAction",
            "Counter");
        string json = BTreeJsonServices.Serialize(dto);

        var result = RunGeneratorWithStubs(ThreeParamStubs,
            MakeAdditionalText("/p/WrongType.btree.json", json));

        result.GeneratedTrees.Should().BeEmpty("type-mismatch ThreeParamReusable must not emit");
        result.Diagnostics.Should().HaveCount(1);
        result.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.CodegenWarningId,
            "type-mismatch must produce BTREE0002");
        result.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ThreeParamReusable_MissingExpressionTargetField_SkipsWithBtree0002()
    {
        // ExpressionTargetField is empty — validator must reject with BTREE0002.
        var actionId = new Guid("E2000000-0000-0000-0000-000000000001");
        var dto = new BehaviorTreeAssetDto
        {
            AssetId = new Guid("E2000000-0000-0000-0000-AABBCCDD0001"),
            Name = "MissingFieldAsset",
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Stub.DemoCounterParams",
            ContextTypeName = "Stub.StubCtx",
            Nodes = new List<BTreeNodeDto>
            {
                new BTreeActionNodeDto
                {
                    VisualId = actionId,
                    ChildVisualIds = new List<Guid>(),
                    EditorMetadata = new NodeEditorMetadataDto(),
                    Action = new BTreeActionPayloadDto
                    {
                        MethodFqn = "Stub.DemoCounterNodes.Action_IncrementCounter",
                        DelegateShape = BTreeDelegateShapeDto.ThreeParamReusable,
                        ExpressionTargetField = null, // MISSING
                    },
                },
            },
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
            Blackboard = new BlackboardBlockDto
            {
                Managed = true,
                TypeName = "DemoCounterParams",
                Variables = new List<BlackboardVariableDto>
                {
                    new BlackboardVariableDto { Name = "Counter", Type = new BlackboardTypeRefDto { TypeId = "Stub.DemoCounterParams" } },
                },
            },
        };
        string json = BTreeJsonServices.Serialize(dto);

        var result = RunGeneratorWithStubs(ThreeParamStubs,
            MakeAdditionalText("/p/MissingField.btree.json", json));

        result.GeneratedTrees.Should().BeEmpty("missing ExpressionTargetField must not emit");
        result.Diagnostics.Should().HaveCount(1);
        result.Diagnostics[0].Id.Should().Be(BTreeJsonGenerator.CodegenWarningId,
            "missing ExpressionTargetField must produce BTREE0002");
    }

    // ── BATCH-02 S1-2 helpers ───────────────────────────────────────────────────

    private static BehaviorTreeAssetDto BuildManagedDto(
        string name,
        List<BlackboardVariableDto> vars,
        params BTreeNodeDto[] extraNodes)
    {
        var waitId = new Guid("B1000000-0000-0000-0000-000000000001");
        var nodes = new List<BTreeNodeDto>
        {
            new BTreeWaitNodeDto
            {
                VisualId = waitId,
                ChildVisualIds = new List<Guid>(),
                EditorMetadata = new NodeEditorMetadataDto(),
                Wait = new BTreeWaitPayloadDto { Duration = 0f },
            },
        };
        nodes.AddRange(extraNodes);

        return new BehaviorTreeAssetDto
        {
            AssetId = new Guid("B0000000-0000-0000-0000-000000000001"),
            Name = name,
            TargetNamespace = "Test.Ns",
            BlackboardTypeName = "Test.Bb",
            ContextTypeName = "Test.Ctx",
            Nodes = nodes,
            Pills = new List<BTreePillDto>(),
            Canvas = new CanvasDto(),
            SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
            Suppressions = new SuppressionsDto(),
            Blackboard = new BlackboardBlockDto
            {
                Managed = true,
                TypeName = name + "Blackboard",
                Variables = vars,
            },
        };
    }
}
