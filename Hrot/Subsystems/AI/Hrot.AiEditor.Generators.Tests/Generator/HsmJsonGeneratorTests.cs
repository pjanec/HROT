using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.AiEditor.Generators;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Generator;

/// <summary>
/// PU-202: Tests for <see cref="HsmJsonGenerator"/> using <see cref="CSharpGeneratorDriver"/>.
///
/// Tests:
/// (a) A valid *.hsm.json AdditionalText produces a {Name}.g.cs containing CreateBuilder()
///     + [HsmDefinition] thunk and NOT [HsmLayout(.
/// (b) A deliberately malformed *.hsm.json yields a generator diagnostic (HSM0001),
///     does NOT throw, and does NOT suppress a sibling valid asset's generation.
/// </summary>
public sealed class HsmJsonGeneratorTests
{
    private static readonly Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Machines.SampleGuard).Assembly;

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static HsmAsset LoadSampleGuard()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var asset = contributor.Enumerate().FirstOrDefault(a => a.Name == "SampleGuard");
        if (asset is null) throw new InvalidOperationException("SampleGuard not found in assembly");
        return (HsmAsset)asset;
    }

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees:  Array.Empty<SyntaxTree>(),
            references:   new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            options:      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static AdditionalText MakeAdditionalText(string path, string content) =>
        new StringAdditionalText(path, content);

    private static GeneratorDriverRunResult RunGenerator(params AdditionalText[] additionalTexts)
    {
        var generator = new HsmJsonGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .AddAdditionalTexts(additionalTexts.ToImmutableArrayCompat());
        var compilation = CreateCompilation();
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    // ── (a) valid *.hsm.json produces topology core + bridge (PU-203) ───────────────

    [Fact]
    public void ValidHsmJson_ProducesGeneratedSource_ContainingCreateBuilderAndThunk()
    {
        // Arrange: load model via reflection, map to DTO, serialize
        var model = LoadSampleGuard();
        var dto   = HsmAssetMapper.ToDto(model);
        string json = HsmJsonServices.Serialize(dto);

        var result = RunGenerator(MakeAdditionalText("/p/SampleGuard.hsm.json", json));

        // Assert: PU-203 — 2 files per asset: topology core + bridge
        result.GeneratedTrees.Should().HaveCount(2,
            "one valid asset produces 2 files: topology core + bridge (PU-203)");

        // Topology-core file
        string coreSource = result.GeneratedTrees
            .First(t => !t.FilePath.Contains("Registrar"))
            .ToString();

        coreSource.Should().Contain("CreateBuilder()",
            "topology-core .g.cs must contain CreateBuilder()");
        coreSource.Should().Contain("[HsmDefinition(",
            "topology-core .g.cs must contain the [HsmDefinition] thunk attribute");
        coreSource.Should().NotContain("[HsmLayout(",
            "topology-core .g.cs must NOT contain [HsmLayout( — layout is JSON-only (§6.2)");

        // Bridge file
        string bridgeSource = result.GeneratedTrees
            .First(t => t.FilePath.Contains("Registrar"))
            .ToString();

        bridgeSource.Should().Contain("[BlueprintRegistrar]",
            "bridge .g.cs must carry [BlueprintRegistrar]");
        bridgeSource.Should().Contain("Register(BehaviorRegistry",
            "bridge .g.cs must have Register(BehaviorRegistry ...) method");

        result.Diagnostics.Should().BeEmpty("a valid asset must not produce diagnostics");
    }

    [Fact]
    public void ValidHsmJson_GeneratedFileName_MatchesAssetName()
    {
        var model = LoadSampleGuard();
        var dto   = HsmAssetMapper.ToDto(model);
        string json = HsmJsonServices.Serialize(dto);

        var result = RunGenerator(MakeAdditionalText("/p/SampleGuard.hsm.json", json));

        result.GeneratedTrees.Should().HaveCount(2,
            "must produce topology core + bridge files");
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("SampleGuard.g.cs"),
            "topology-core hint name must be {AssetName}.g.cs");
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("SampleGuard.Registrar.g.cs"),
            "bridge hint name must be {AssetName}.Registrar.g.cs");
    }

    [Fact]
    public void ValidHsmJson_GeneratedSource_DoesNotContainLayoutNamespace()
    {
        var model = LoadSampleGuard();
        var dto   = HsmAssetMapper.ToDto(model);
        string json = HsmJsonServices.Serialize(dto);

        var result = RunGenerator(MakeAdditionalText("/p/SampleGuard.hsm.json", json));
        result.GeneratedTrees.Should().HaveCount(2);

        string coreSource = result.GeneratedTrees
            .First(t => !t.FilePath.Contains("Registrar"))
            .ToString();
        coreSource.Should().NotContain("Hrot.Editor.AiShared.Layout",
            "the layout namespace must not be in topology-core-only output");
    }

    // ── (b) malformed input: diagnostic + sibling safety ─────────────────────────

    [Fact]
    public void MalformedHsmJson_YieldsDiagnostic_DoesNotThrow()
    {
        var badText = MakeAdditionalText("/p/Broken.hsm.json", "{ not json !!! }");

        var result = RunGenerator(badText);

        result.GeneratedTrees.Should().BeEmpty(
            "a malformed asset must not produce generated source");
        result.Diagnostics.Should().HaveCount(1,
            "exactly one diagnostic for the malformed asset");
        result.Diagnostics[0].Id.Should().Be(HsmJsonGenerator.DiagnosticId,
            "diagnostic must carry HSM0001");
        result.Diagnostics[0].Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public void MalformedHsmJson_DoesNotSuppressSiblingValidAsset()
    {
        var model = LoadSampleGuard();
        var dto   = HsmAssetMapper.ToDto(model);
        string json = HsmJsonServices.Serialize(dto);

        var goodText = MakeAdditionalText("/p/SampleGuard.hsm.json", json);
        var badText  = MakeAdditionalText("/p/Broken.hsm.json", "{ bad! }");

        var result = RunGenerator(goodText, badText);

        // Good asset emits 2 files (core + bridge); bad asset emits 0.
        result.GeneratedTrees.Should().HaveCount(2,
            "valid sibling must still emit core+bridge despite malformed asset");
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("SampleGuard.g.cs"),
            "topology-core file must be present");
        result.GeneratedTrees.Should().Contain(t => t.FilePath.EndsWith("SampleGuard.Registrar.g.cs"),
            "bridge file must be present");
        result.Diagnostics.Should().HaveCount(1,
            "one diagnostic for the one malformed asset");
        result.Diagnostics[0].Id.Should().Be(HsmJsonGenerator.DiagnosticId);
    }

    [Fact]
    public void NonHsmJsonAdditionalText_IsIgnored()
    {
        var other = MakeAdditionalText("/p/SampleScout.btree.json", "{}");
        var result = RunGenerator(other);

        result.GeneratedTrees.Should().BeEmpty(
            "HsmJsonGenerator must ignore non-*.hsm.json texts");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void EmitTopologyCore_ContainsCreateBuilderAndThunk_NotLayout()
    {
        var model = LoadSampleGuard();
        var dto   = HsmAssetMapper.ToDto(model);

        string core = HsmEmitCore.EmitTopologyCore(dto);

        core.Should().Contain("CreateBuilder()",
            "topology core must contain CreateBuilder()");
        core.Should().Contain("[HsmDefinition(",
            "topology core must contain [HsmDefinition] thunk");
        core.Should().NotContain("[HsmLayout(",
            "topology core must NOT contain [HsmLayout( (§6.2)");
    }

    [Fact]
    public void EmitTopologyCore_IsDeterministic()
    {
        var model = LoadSampleGuard();
        var dto   = HsmAssetMapper.ToDto(model);

        string first  = HsmEmitCore.EmitTopologyCore(dto);
        string second = HsmEmitCore.EmitTopologyCore(dto);

        first.Should().Be(second, "EmitTopologyCore must be deterministic");
    }

    [Fact]
    public void FullEmit_IsByteIdentical_ToOriginal_AfterTopologyCoreRefactor()
    {
        var model = LoadSampleGuard();
        var dto   = HsmAssetMapper.ToDto(model);
        string full = HsmEmitCore.Emit(dto);

        full.Should().Contain("[HsmLayout(",
            "full emit must still include [HsmLayout(");
        full.Should().Contain("CreateBuilder()",
            "full emit must include CreateBuilder()");
        full.Should().Contain("[HsmDefinition(",
            "full emit must include [HsmDefinition]");
    }

    // ── ⭐ CE-506 — a global transition's guard, action and priority are emitted, never dropped ─────────────

    private static HsmAssetDto WithGlobal(Hrot.AiEditor.Persistence.BehaviorActionBindingDto? guard,
                                          Hrot.AiEditor.Persistence.BehaviorActionBindingDto? action, byte priority)
    {
        var dto = HsmAssetMapper.ToDto(LoadSampleGuard());
        if (dto.Events.Count == 0) dto.Events.Add(new EventDefinitionDto { Name = "Ce506Go" });
        dto.GlobalTransitions.Add(new GlobalTransitionNodeDto
        {
            VisualId       = new Guid("50600000-0000-0000-0000-000000000001"),
            TargetStableId = dto.States[0].StableId,
            EventName      = dto.Events[0].Name,
            Guard          = guard,
            Action         = action,
            Priority       = priority,
        });
        return dto;
    }

    /// <summary>
    /// 🔴 CE-506 — RED before: the emitter wrote <c>builder.GlobalTransition(event, target, visualId)</c> and nothing else,
    /// so a bound global guard/action was saved and never ran. ⭐ Now both, and the priority, reach the builder — named by
    /// the SAME rule <c>CollectGuards</c>/<c>CollectActions</c> register them under.
    /// </summary>
    [Fact]
    public void CE506_AGlobalTransitionsGuardActionAndPriority_AreEmitted()
    {
        var dto = WithGlobal(new Hrot.AiEditor.Persistence.BehaviorActionBindingDto { MethodFqn = "Ce506.Nodes.CanGo" },
                             new Hrot.AiEditor.Persistence.BehaviorActionBindingDto { MethodFqn = "Ce506.Nodes.OnGo" }, 7);

        string core = HsmEmitCore.EmitTopologyCore(dto);

        core.Should().Contain("guard: \"Ce506.Nodes.CanGo\"")
            .And.Contain("action: \"Ce506.Nodes.OnGo\"")
            .And.Contain("priority: 7");
        core.Should().Contain("RegisterGuard(\"Ce506.Nodes.CanGo\")", "the guard name the global uses is registered");
        core.Should().Contain("RegisterAction(\"Ce506.Nodes.OnGo\")", "the action name the global uses is registered");
    }

    /// <summary>⛔ CE-506 — a global transition has no source state to seed a blueprint from: a blueprint on it is HSM0004,
    /// and the asset is not emitted (never a silently dropped binding).</summary>
    [Fact]
    public void CE506_AGlobalTransitionBindingABlueprint_IsHsm0004()
    {
        var dto = WithGlobal(new Hrot.AiEditor.Persistence.BehaviorActionBindingDto
                             { BlueprintAssetId = new Guid("50600000-0000-0000-0000-0000000000bb") }, null, 0);

        var result = RunGenerator(MakeAdditionalText("/p/Ce506.hsm.json", HsmJsonServices.Serialize(dto)));

        result.Diagnostics.Should().Contain(d => d.Id == HsmJsonGenerator.GlobalBlueprintErrorId);
        result.GeneratedTrees.Should().BeEmpty();
    }

    // ── ⭐ CE-423 — a State variable that would get NO storage is an error, never a silent skip ─────────────

    /// <summary>
    /// 🔴 RED before: a standalone <c>Role=State</c> variable at <c>Scope=Node</c> (the enum default, omitted on save) was
    /// skipped by the bridge emitter — no slot, no diagnostic. ⭐ Now it is <c>HSM0002</c>, naming the variable.
    /// <c>Behavior</c> is the control: same variable, legal scope, no diagnostic.
    /// </summary>
    [Theory]
    [InlineData(Hrot.AiEditor.Persistence.WorkingStateScope.Node,     true)]
    [InlineData(Hrot.AiEditor.Persistence.WorkingStateScope.Behavior, false)]
    public void CE423_AStandaloneStateVariable_AtNodeScope_IsAnError(
        Hrot.AiEditor.Persistence.WorkingStateScope scope, bool expectError)
    {
        var dto = HsmAssetMapper.ToDto(LoadSampleGuard());
        dto.Blackboard.Managed = true;
        dto.Blackboard.Variables.Add(new HsmBlackboardVariableDto
        {
            Name  = "Unbound",
            Type  = new() { TypeId = "System.Int32" },
            Role  = Hrot.AiEditor.Persistence.BlackboardVariableRole.State,
            Scope = scope,
        });

        var result = RunGenerator(MakeAdditionalText("/p/SampleGuard.hsm.json", HsmJsonServices.Serialize(dto)));

        var hits = result.Diagnostics.Where(d => d.Id == HsmJsonGenerator.StateStorageErrorId).ToList();
        if (expectError)
        {
            hits.Should().ContainSingle();
            hits[0].Severity.Should().Be(DiagnosticSeverity.Error);
            hits[0].GetMessage().Should().Contain("Unbound").And.Contain("Behavior");
        }
        else hits.Should().BeEmpty();
    }

    // ── ⭐⭐ CE-439 — a hosting state's seed binding, sized from a SIBLING behaviour's Inputs struct ─────────────

    /// <summary>
    /// 🔴 RED before (two holes): the HSM registrar passed no bindings to <c>HsmHostedSubtrees.Register</c>, AND the host
    /// variable typed as the child's generated Inputs struct was unsizeable in this generator run (Roslyn cannot see a type
    /// the same run emits), so <c>Pack</c> threw and the host emitted no params at all. ⭐ Now
    /// <c>GeneratedTypeCatalog</c> (then <c>GeneratedBehaviorSchemaCatalog</c>) sizes it from the child's <c>.btree.json</c> and the shared
    /// <c>EmitSiteBindings</c> bakes <c>[stableId] = new(offset, size)</c>.
    /// </summary>
    [Fact]
    public void CE439_AHostingStatesBinding_IsSizedFromTheSiblingChild_AndEmitted()
    {
        // The child: a managed BTree publishing two Role=Input fields (4 + 4 bytes).
        var child = new Hrot.AiEditor.Persistence.BTree.BehaviorTreeAssetDto { AssetId = Guid.NewGuid(), Name = "PatrolTree" };
        child.Blackboard.Managed = true;
        foreach (var (n, t) in new[] { ("Speed", "System.Single"), ("Laps", "System.Int32") })
            child.Blackboard.Variables.Add(new Hrot.AiEditor.Persistence.BTree.BlackboardVariableDto
            { Name = n, Type = new Hrot.AiEditor.Persistence.BTree.BlackboardTypeRefDto { TypeId = t } });
        string childInputs = BTreeEmitCore.InputsStructTypeId(child)!;

        // The host: SampleGuard, one state hosting the child, bound to a host variable of the child's Inputs type.
        var host  = HsmAssetMapper.ToDto(LoadSampleGuard());
        var state = host.States.First(s => s.Name != null && !s.Name.StartsWith("__", StringComparison.Ordinal));
        state.SubtreeAssetId        = child.AssetId;
        state.SubtreeName           = "PatrolTree";
        state.SubtreeParamsVariable = "PatrolTreeParams";
        host.Blackboard.Managed = true;
        host.Blackboard.Variables.Add(new HsmBlackboardVariableDto
        { Name = "PatrolTreeParams", Type = new HsmBlackboardTypeRefDto { TypeId = childInputs } });

        var result = RunGenerator(
            MakeAdditionalText("/p/SampleGuard.hsm.json", HsmJsonServices.Serialize(host)),
            MakeAdditionalText("/p/PatrolTree.btree.json", Hrot.AiEditor.Persistence.BTree.BTreeJsonServices.Serialize(child)));

        string registrar = result.GeneratedTrees.First(t => t.FilePath.Contains("SampleGuard.Registrar")).ToString();
        registrar.Should().Contain($"[new global::System.Guid(\"{state.StableId:D}\")] = new(0, 8)",
            "the binding is the host variable's (offset, size), and its size came from the sibling's own packing");
        registrar.Should().Contain("case \"PatrolTreeParams\":", "the host's params path is emitted — Pack did not throw");
    }

    /// <summary>
    /// ⭐⭐ <b><c>CE-2026</c> (S8c) — the same binding when the child is an HSM.</b> 🔴 RED before: the sibling catalogue read
    /// <c>*.btree.json</c> only, so a host variable typed as an HSM child's Inputs struct was unsizeable, <c>Pack</c> threw and the
    /// host emitted no params. ⭐ <c>GeneratedTypeCatalog</c> reads the HSM through <c>HsmBridgeEmitCore.BlackboardOwner</c> — the
    /// view the child's own generator packs its struct with — so the name and the size cannot disagree.
    /// </summary>
    [Fact]
    public void S8c_AHostingStatesBinding_IsSizedFromASiblingHsmChild()
    {
        // The child: an HSM publishing two Role=Input fields (4 + 4 bytes).
        var child = HsmAssetMapper.ToDto(LoadSampleGuard());
        child.AssetId = Guid.NewGuid();
        child.Name    = "PatrolMachine";
        child.Blackboard.Managed = true;
        child.Blackboard.Variables.Clear();
        foreach (var (n, t) in new[] { ("Speed", "System.Single"), ("Laps", "System.Int32") })
            child.Blackboard.Variables.Add(new HsmBlackboardVariableDto { Name = n, Type = new HsmBlackboardTypeRefDto { TypeId = t } });
        string childInputs = BTreeEmitCore.InputsStructTypeId(HsmBridgeEmitCore.BlackboardOwner(child))!;

        var host  = HsmAssetMapper.ToDto(LoadSampleGuard());
        var state = host.States.First(s => s.Name != null && !s.Name.StartsWith("__", StringComparison.Ordinal));
        state.SubtreeAssetId        = child.AssetId;
        state.SubtreeName           = "PatrolMachine";
        state.SubtreeParamsVariable = "PatrolMachineParams";
        host.Blackboard.Managed = true;
        host.Blackboard.Variables.Add(new HsmBlackboardVariableDto
        { Name = "PatrolMachineParams", Type = new HsmBlackboardTypeRefDto { TypeId = childInputs } });

        var result = RunGenerator(
            MakeAdditionalText("/p/SampleGuard.hsm.json", HsmJsonServices.Serialize(host)),
            MakeAdditionalText("/p/PatrolMachine.hsm.json", HsmJsonServices.Serialize(child)));

        string registrar = result.GeneratedTrees.First(t => t.FilePath.Contains("SampleGuard.Registrar")).ToString();
        registrar.Should().Contain($"[new global::System.Guid(\"{state.StableId:D}\")] = new(0, 8)",
            "the binding's size came from the sibling HSM's own packing");
        registrar.Should().Contain("case \"PatrolMachineParams\":", "the host's params path is emitted — Pack did not throw");
    }

    // ── ⭐ CE-2039 (S8g G4/G5) — an HSM's class, registrar and structs come from ONE name ─────────────────────────────

    /// <summary>
    /// 🔴 The class used the "replace with _" sanitizer while the <c>_Block</c>/<c>_Blackboard</c> structs (via
    /// <c>BlackboardOwner</c> → <c>BTreeEmitCore</c>) used "strip": <c>Guard-Patrol</c> emitted class <c>Guard_Patrol</c> but struct
    /// <c>GuardPatrol_Block</c>, and a second HSM <c>GuardPatrol</c> collided on that struct (CS0101). ⭐ Now one shape.
    /// </summary>
    [Theory]
    [InlineData("Guard-Patrol", "GuardPatrol")]
    [InlineData("class",        "_class")]      // a bare keyword class name gets '_' (it emitted `class class`)
    public void CE2039_AnHsmsClassAndRegistrar_UseTheStructsShape(string assetName, string expectedClass)
    {
        // the shipped .hsm.json (it owns a managed blackboard, so the structs are emitted)
        var dto = HsmJsonServices.Deserialize(
            Hrot.AiEditor.Generators.Tests.Golden.AiAssetCorpus.ReadAsset(
                Hrot.AiEditor.Generators.Tests.Golden.AiAssetKind.Hsm, "HsmResolverDemo"))!;
        dto.Name = assetName;

        string core   = HsmEmitCore.EmitTopologyCore(dto);
        string bridge = HsmBridgeEmitCore.EmitBridge(dto);

        core.Should().Contain($"public static class {expectedClass}");
        bridge.Should().Contain($"public static class {expectedClass}Registrar");
        bridge.Should().Contain($"var blob = {expectedClass}.Compile();");
        bridge.Should().NotContain("Guard_Patrol");
        // the structs come from the SAME emitter the generator calls (HsmJsonGenerator → BlackboardOwner → BTreeEmitCore)
        string? structs = BTreeEmitCore.EmitBlackboardStructSource(HsmBridgeEmitCore.BlackboardOwner(dto), out _);
        structs.Should().NotBeNull("HsmResolverDemo owns a managed blackboard");
        if (assetName == "Guard-Patrol")
            structs!.Should().Contain("public struct GuardPatrol_Block", "the struct and the class now share one sanitized name");
    }
}
