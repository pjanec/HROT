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
}
