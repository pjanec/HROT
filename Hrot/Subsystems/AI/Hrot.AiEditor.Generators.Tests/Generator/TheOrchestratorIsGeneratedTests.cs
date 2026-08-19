using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
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

namespace Hrot.AiEditor.Generators.Tests.Generator;

/// <summary>
/// ⭐⭐⭐ <b>Batch 92 (<c>92b</c>) — the orchestrator is GENERATED.</b>
///
/// <para>⛔⛔ <b>WHY THESE RAILS BUILD A FIXTURE INSTEAD OF USING THE CORPUS.</b> 📐 Coordinator-measured
/// over every source <c>*.btree.json</c> / <c>*.hsm.json</c>: <b>no shipped asset has a populated
/// <c>Aliases</c> or <c>SubtreeSyncBindings</c></b>. ⇒ ⭐ that is exactly why the golden cannot move —
/// and ⛔⛔ <b>exactly why nothing would exercise the feature</b>: 📌 this programme's signature
/// failure is an emitter that ships green and has never produced a line. ⭐ Every rail below therefore
/// asserts the <b>EMITTED TEXT</b>, ⛔ never merely that something non-null came back.</para>
///
/// <para>⭐⭐ <b>The two hosts are covered separately and differ for a real reason</b>: BTree's
/// orchestrator is optional sugar over a tick the kernel already performs, while <b>HSM's IS the
/// hosting mechanism</b> — without it an HSM state cannot host a sub-tree at all. ⭐ And the HSM arm is
/// the one <c>91b</c> made meaningful: HSM hosts only through an Approach-A alias, which before
/// <c>91b</c> never survived a reload.</para>
/// </summary>
public sealed class TheOrchestratorIsGeneratedTests
{
    private static readonly Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Machines.SampleGuard).Assembly;

    // ── Harness ──────────────────────────────────────────────────────────────

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees:  Array.Empty<SyntaxTree>(),
            references:   new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            options:      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static GeneratorDriverRunResult Run(IIncrementalGenerator generator, string path, string json)
    {
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .AddAdditionalTexts(new AdditionalText[] { new StringAdditionalText(path, json) }
                .ToImmutableArrayCompat());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(CreateCompilation());
        return driver.GetRunResult();
    }

    private static string? OrchestratorText(GeneratorDriverRunResult result) =>
        result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("Orchestrators.g.cs", StringComparison.Ordinal))
            ?.ToString();

    /// <summary>
    /// ⭐⭐⭐ A DTO type id the generator CANNOT possibly load — 📌 the point of <c>91b</c> persisting a
    /// <c>Type.FullName</c> string. ⛔ If the core ever tried to resolve a <c>System.Type</c>, this
    /// fixture would fail rather than emit, because no such type exists in any loaded assembly.
    /// </summary>
    private const string UnloadableDtoTypeId = "Made.Up.Behaviors.PatrolParams";

    private static BlackboardAliasBindingDto Alias(string subAssetName) => new()
    {
        RequiringAssetId   = Guid.NewGuid(),
        RequiringElementId = Guid.NewGuid(),
        RequiringAssetName = subAssetName,
        RequiredByPath     = "Root/Move",
        DtoTypeId          = UnloadableDtoTypeId,
    };

    // ══ HSM ══════════════════════════════════════════════════════════════════

    private static HsmAssetDto SampleGuardDto()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var model = (HsmAsset)contributor.Enumerate().First(a => a.Name == "SampleGuard");
        return HsmAssetMapper.ToDto(model);
    }

    /// <summary>
    /// ⚠ <c>SampleGuard</c> declares no blackboard variable, and an alias is keyed BY variable name —
    /// so the fixture must author the master variable the alias projects onto. ⭐ Returns its name.
    /// </summary>
    private static string EnsureVariable(HsmAssetDto dto)
    {
        if (dto.Blackboard.Variables.Count == 0)
            dto.Blackboard.Variables.Add(new HsmBlackboardVariableDto
            {
                Name = "Health",
                Type = new HsmBlackboardTypeRefDto { TypeId = "System.Single" },
            });
        return dto.Blackboard.Variables[0].Name;
    }

    /// <summary>⭐ The BTree counterpart of <see cref="EnsureVariable(HsmAssetDto)"/>.</summary>
    private static string EnsureVariable(BehaviorTreeAssetDto dto)
    {
        if (dto.Blackboard.Variables.Count == 0)
            dto.Blackboard.Variables.Add(new BlackboardVariableDto
            {
                Name = "Health",
                Type = new BlackboardTypeRefDto { TypeId = "System.Single" },
            });
        return dto.Blackboard.Variables[0].Name;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The null case, and it is the corpus's case.</b> ⛔ No alias ⇒ <b>no fourth file at
    /// all</b>, which is the whole reason the golden cannot move.
    /// </summary>
    [Fact]
    public void AnHsmAssetWithNoAliasEmitsNoOrchestratorFileAtAll()
    {
        var result = Run(new HsmJsonGenerator(), "/p/SampleGuard.hsm.json",
            HsmJsonServices.Serialize(SampleGuardDto()));

        OrchestratorText(result).Should().BeNull(
            "an asset with no alias must produce NO Orchestrators.g.cs — not an empty one");
        result.GeneratedTrees.Should().HaveCount(2,
            "topology core + registrar, exactly as before this batch");
    }

    /// <summary>⭐⭐⭐ THE HSM rail: an alias reaches generated C#. 🔴 RED before <c>92b</c>.</summary>
    [Fact]
    public void AnHsmAliasEmitsAnHsmActionOrchestrator()
    {
        var dto  = SampleGuardDto();
        string varName = EnsureVariable(dto);
        dto.Aliases = new Dictionary<string, List<BlackboardAliasBindingDto>>
        {
            [varName] = new() { Alias("GuardSubTree") },
        };

        var text = OrchestratorText(Run(new HsmJsonGenerator(), "/p/SampleGuard.hsm.json",
            HsmJsonServices.Serialize(dto)));

        text.Should().NotBeNull("an aliased asset must produce an Orchestrators.g.cs");
        text!.Should().Contain("[HsmAction(Name = \"Orchestrate_GuardSubTree\")]",
            "the HSM arm registers through [HsmAction], not [BTreeAction]");
        text.Should().Contain("public static NodeStatus Orchestrate_GuardSubTree_Tick(");
        text.Should().Contain($"ref master.{varName}",
            "the sub-tree's blackboard is PROJECTED onto the master variable the alias names");
        text.Should().Contain("GetInterpreter().Tick(ref subBb, ref state, ref ctx);",
            "the orchestrator's job is to tick the sub-tree");
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>DtoTypeId</c> is SPLIT, never resolved.</b> The fixture's type exists in no
    /// assembly — ⛔ a generator cannot load behavior assemblies — yet its short name must appear as
    /// the projection type and its namespace as a using.
    /// </summary>
    [Fact]
    public void TheDtoTypeIdIsSplitIntoNameAndNamespaceWithoutResolvingAType()
    {
        var dto = SampleGuardDto();
        dto.Aliases = new Dictionary<string, List<BlackboardAliasBindingDto>>
        {
            [EnsureVariable(dto)] = new() { Alias("GuardSubTree") },
        };

        var text = OrchestratorText(Run(new HsmJsonGenerator(), "/p/SampleGuard.hsm.json",
            HsmJsonServices.Serialize(dto)))!;

        text.Should().Contain("Unsafe.As<PatrolParams, PatrolParams>",
            "the SHORT name is the segment after the last '.'");
        text.Should().Contain("using Made.Up.Behaviors;",
            "the NAMESPACE is everything before it, and becomes a using");
        text.Should().NotContain(UnloadableDtoTypeId,
            "the full name is split, not pasted through");
    }

    /// <summary>⭐⭐ Two aliases on one variable ⇒ two methods; ⛔ a repeat of the same pair ⇒ one.</summary>
    [Fact]
    public void EachUniqueVariableSubTreePairEmitsExactlyOneMethod()
    {
        var dto = SampleGuardDto();
        string varName = EnsureVariable(dto);
        dto.Aliases = new Dictionary<string, List<BlackboardAliasBindingDto>>
        {
            [varName] = new() { Alias("Alpha"), Alias("Beta"), Alias("Alpha") },
        };

        var text = OrchestratorText(Run(new HsmJsonGenerator(), "/p/SampleGuard.hsm.json",
            HsmJsonServices.Serialize(dto)))!;

        CountOf(text, "Orchestrate_Alpha_Tick(").Should().Be(1, "the duplicate pair is de-duplicated");
        CountOf(text, "Orchestrate_Beta_Tick(").Should().Be(1);
    }

    // ══ BTree ════════════════════════════════════════════════════════════════

    private static BehaviorTreeAssetDto SampleScoutDto()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var model = (BehaviorTreeAsset)contributor.Enumerate().First(a => a.Name == "SampleScout");
        return BehaviorTreeAssetMapper.ToDto(model);
    }

    /// <summary>⭐⭐⭐ The BTree null case — the corpus's case on this host too.</summary>
    [Fact]
    public void ABTreeAssetWithNoAliasEmitsNoOrchestratorFileAtAll()
    {
        var result = Run(new BTreeJsonGenerator(), "/p/SampleScout.btree.json",
            BTreeJsonServices.Serialize(SampleScoutDto()));

        OrchestratorText(result).Should().BeNull();
        result.GeneratedTrees.Should().HaveCount(2,
            "topology core + registrar, exactly as before this batch");
    }

    /// <summary>⭐⭐⭐ THE BTree rail: same alias arm, the other attribute. 🔴 RED before <c>92b</c>.</summary>
    [Fact]
    public void ABTreeAliasEmitsABTreeActionOrchestrator()
    {
        var dto = SampleScoutDto();
        string varName = EnsureVariable(dto);
        dto.Aliases = new Dictionary<string, List<BlackboardAliasBindingDto>>
        {
            [varName] = new() { Alias("PatrolSubTree") },
        };

        var text = OrchestratorText(Run(new BTreeJsonGenerator(), "/p/SampleScout.btree.json",
            BTreeJsonServices.Serialize(dto)));

        text.Should().NotBeNull();
        text!.Should().Contain("[BTreeAction(Name = \"Orchestrate_PatrolSubTree\")]",
            "the BTree arm registers through [BTreeAction]");
        text.Should().NotContain("[HsmAction",
            "⛔ the two hosts must not cross-emit each other's attribute");
        text.Should().Contain($"ref master.{varName}");
        text.Should().Contain("GetInterpreter().Tick(ref subBb, ref state, ref ctx);");
    }

    private static int CountOf(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
