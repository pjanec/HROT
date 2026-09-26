using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
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

    /// <summary>
    /// ⭐⭐⭐ <b><c>Q49</c> D + <c>Q50</c> A — run the generator over TWO SIBLING trees.</b>
    /// ⛔ The single-asset <see cref="Run"/> above cannot exercise subtree sync at all: the master needs
    /// its CALLEE's <c>*.btree.json</c> in the same <c>AdditionalTexts</c> set, because that is the only
    /// way a generator can learn what blackboard type the callee declares *(it cannot load assets)*.
    /// </summary>
    private static GeneratorDriverRunResult RunTwo(
        IIncrementalGenerator generator,
        (string Path, string Json) a, (string Path, string Json) b)
    {
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .AddAdditionalTexts(new AdditionalText[]
            {
                new StringAdditionalText(a.Path, a.Json),
                new StringAdditionalText(b.Path, b.Json),
            }.ToImmutableArrayCompat());
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

    /// <summary>
    /// ⭐⭐⭐ <b>INVERTED <c>2026-09-23</c> (<c>CE-333</c> / <c>E5</c>) — an HSM alias emits NOTHING.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.11.2.
    ///
    /// <para>🔴 <b>What this used to assert, and why every line of it was wrong:</b> that an alias
    /// produced <c>[HsmAction(Name = "Orchestrate_GuardSubTree")] public static NodeStatus
    /// Orchestrate_GuardSubTree_Tick(…)</c> ending in <c>GetInterpreter().Tick(...)</c>. 📐 Measured:
    /// ① that method shape cannot carry <c>[HsmAction]</c> — the ABI is a thunk
    /// <c>(void*, void*, HsmCommandWriter*)</c>, and <c>&amp;</c> of a <c>NodeStatus</c>-returning
    /// method does not convert; ② <c>GetInterpreter()</c> is <b>defined nowhere in the repository</b>
    /// (<c>CE-335</c>). ⇒ this rail asserted, line by line, the text of a file that has never
    /// compiled.</para>
    ///
    /// <para>🔒 <b>That is the lesson worth keeping: a text-asserting golden cannot tell you the code
    /// it pins is not valid C#.</b> ⭐ The rail that WOULD have caught it — compile the emitted text —
    /// is acceptance <c>A2</c> and is still missing.</para>
    ///
    /// <para>⭐⭐ HSM hosting now lives on the STATE: <c>StateNode.SubtreeName</c> +
    /// <c>SubtreeAssetId</c>, a slot declared by <c>HsmBridgeEmitCore</c>, and
    /// <c>BrainTickSystem.TickHostedChildren</c> ticking the child every frame.</para>
    /// </summary>
    [Fact]
    public void AnHsmAliasEmitsNoOrchestrator_HostingIsPerState_CE333()
    {
        var dto  = SampleGuardDto();
        string varName = EnsureVariable(dto);
        dto.Aliases = new Dictionary<string, List<BlackboardAliasBindingDto>>
        {
            [varName] = new() { Alias("GuardSubTree") },
        };

        var result = Run(new HsmJsonGenerator(), "/p/SampleGuard.hsm.json",
            HsmJsonServices.Serialize(dto));

        OrchestratorText(result).Should().BeNull(
            "the HSM alias arm is retired (CE-333): the [HsmAction] it emitted could not compile, and "
            + "an HSM action is dispatched at most once per event round (CE-334) so it could not have "
            + "ticked a child every frame. Hosting moved to the STATE — DESIGN §32.");
        result.GeneratedTrees.Should().HaveCount(2,
            "topology core + registrar, and no orchestrator file — the same shape as an unaliased asset");
    }


    /// <summary>
    /// ⭐⭐ <b>RE-HOMED ONTO THE COLLECTOR <c>2026-09-23</c> (<c>CE-337</c>).</b> The claim —
    /// a repeated (variable, sub-tree) pair yields ONE method — belongs to
    /// <see cref="OrchestratorAliasCollector"/>, and it survives its callers' retirement. ⛔ Asserted
    /// against the collector rather than emitted text, because no arm emits any.
    /// </summary>
    [Fact]
    public void EachUniqueVariableSubTreePairIsCollectedExactlyOnce()
    {
        var aliases = new Dictionary<string, List<BlackboardAliasBindingDto>>
        {
            ["Health"] = new() { Alias("Alpha"), Alias("Beta"), Alias("Alpha") },
        };

        var methods = OrchestratorAliasCollector.Collect(aliases, new[] { "Health" }, "BTreeAsset");

        methods.Count(m => m.SubTreeName == "Alpha").Should().Be(1, "the duplicate pair is de-duplicated");
        methods.Count(m => m.SubTreeName == "Beta").Should().Be(1);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>DtoTypeId</c> is SPLIT, never resolved</b> — the point of <c>91b</c> persisting a
    /// <c>Type.FullName</c> string rather than a <c>System.Type</c>.
    ///
    /// <para>⚠ <b>RE-HOMED ONTO THE COLLECTOR <c>2026-09-23</c> (<c>CE-337</c>)</b>, for the same
    /// reason as the rail above. ⭐ The fixture's type exists in no assembly, which is the whole
    /// assertion: if the collector ever tried to LOAD it, this reddens.</para>
    /// </summary>
    [Fact]
    public void TheDtoTypeIdIsSplitIntoNameAndNamespaceWithoutResolvingAType()
    {
        var aliases = new Dictionary<string, List<BlackboardAliasBindingDto>>
        {
            ["Health"] = new() { Alias("GuardSubTree") },
        };

        var methods = OrchestratorAliasCollector.Collect(aliases, new[] { "Health" }, "HsmAsset");

        var m = methods.Should().ContainSingle().Subject;
        m.DtoTypeName.Should().Be("PatrolParams", "the SHORT name is the segment after the last '.'");
        m.DtoTypeNs.Should().Be("Made.Up.Behaviors", "the NAMESPACE is everything before it");
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

    /// <summary>
    /// ⭐⭐⭐ <b>INVERTED <c>2026-09-23</c> (<c>CE-337</c>) — a BTree alias emits NOTHING either.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.12.
    ///
    /// <para>🔴 This asserted <c>[BTreeAction…] Orchestrate_PatrolSubTree_Tick(… ref master.{var} …)</c>
    /// and <c>HostedSubtree.Tick(</c>. 📐 <c>CE-336</c>'s compile rail then showed the emission had
    /// never been valid C# — and the last defect could not be patched: both arms project onto a MASTER
    /// BLACKBOARD STRUCT, and <c>P4</c> deleted <c>BrainBlackboard</c>, which every shipped
    /// <c>*.btree.json</c> still names. ⇒ no asset can satisfy the arm.</para>
    ///
    /// <para>⭐⭐ Hosting is per-SITE now (<c>E5</c>): a <c>{SubtreeAssetId, SubtreeName}</c> pair, a
    /// tree-state slot from <c>ComputeTreeStateKey</c>, a registration-time binding through
    /// <c>HostedChildren</c>, and a brain that ticks it every frame. ⛔ The BTree-hosts-BTree case is
    /// that same shape with the NODE's visual id as the site — NOT BUILT, and a slice, not a patch.</para>
    /// </summary>
    [Fact]
    public void ABTreeAliasEmitsNoOrchestrator_HostingIsPerSite_CE337()
    {
        var dto = SampleScoutDto();
        string varName = EnsureVariable(dto);
        dto.Aliases = new Dictionary<string, List<BlackboardAliasBindingDto>>
        {
            [varName] = new() { Alias("PatrolSubTree") },
        };

        var result = Run(new BTreeJsonGenerator(), "/p/SampleScout.btree.json",
            BTreeJsonServices.Serialize(dto));

        OrchestratorText(result).Should().BeNull(
            "CE-337 retired both BTree orchestrator arms — the emission never compiled and its "
            + "`ref master` projection needs a blackboard struct P4 deleted");
    }

    private static int CountOf(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    // ══ Q49 D + Q50 A — SUBTREE SYNC, END TO END ═════════════════════════════

    // ⛔ REMOVED 2026-09-26 (CE-337's sweep): TwoSiblingTrees_YieldAnOrchestratorAndTheMasterDeclaresItsSlice.
    //
    // ⭐ It asserted TWO things: ① the sibling pass emits the Approach-B orchestrator, and ② the
    //   master DECLARES the resolved slice field. ① died when both arms were retired; ② died three
    //   days later when the sweep stopped declaring the field at all — a variable read and written
    //   by nothing is the silent-default disease, so the declaration went with its reader.
    // ⭐⭐ Its fixture and its surviving question — "what happens to an asset that carries sync
    //   bindings?" — are now CE337_R1_SyncBindingsWarnAndDeclareNoSliceVariable, which asserts the
    //   WARNING and the ABSENCE of the slice. ⛔ Keeping this alongside it would be two rails over
    //   one behaviour, disagreeing about the answer.
    // 📄 DESIGN_Occurrence_Scoped_Storage.md §32.13.


    /// <summary>
    /// ⛔⛔ <b>The anti-vacuity half, and it is the one that matters.</b> ⚠ WITHOUT the callee's sibling
    /// JSON the generator cannot resolve the blackboard type ⇒ ⭐ it emits <b>NOTHING</b> — not a group
    /// against a missing field. 📌 That is the safety property: half-formed output is what
    /// <c>BP-306</c> was.
    /// </summary>
    [Fact]
    public void WithoutTheCalleesSiblingJson_NothingIsEmitted()
    {
        var master = SampleScoutDto();
        string masterVar = EnsureVariable(master);
        var nodeId = Guid.NewGuid();
        master.Nodes.Add(new BTreeSubtreeNodeDto
        {
            VisualId = nodeId,
            Subtree  = new BTreeSubtreePayloadDto
            {
                SubtreeAssetId = Guid.NewGuid(),   // ⛔ no sibling declares this id
                SubtreeName    = "ShootBT",
                IsResolved     = true,
            },
        });
        master.SubtreeSyncBindings[nodeId.ToString()] = new List<SubtreeSyncBindingDto>
        {
            new() { FieldName = "Health", MasterVariableName = masterVar, SyncIn = true, SyncOut = false },
        };

        var result = Run(new BTreeJsonGenerator(), "/p/MasterAI.btree.json",
            BTreeJsonServices.Serialize(master));

        OrchestratorText(result).Should().BeNull();
        string allGenerated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        allGenerated.Should().NotContain("ShootBT_",
            "no slice may be declared for a callee that cannot be resolved");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE CONSTRAINT THE RAIL ABOVE FOUND, pinned — and the SAFE failure it produces.</b>
    /// 📐 Measured <c>2026-08-22</c>: the slice's type is the <b>CALLEE's blackboard</b>. When that is a
    /// <b>GENERATED</b> (Category-2) struct it does <b>not exist in the master's compilation</b> — 📌 the
    /// same wall <c>GeneratedBlueprintSchemaCatalog</c> exists for: <i>sibling generators cannot see each
    /// other's generated output within one generation pass.</i>
    ///
    /// <para>⭐⭐ <b>And the existing validator already handles it correctly</b>: the asset is
    /// <b>SKIPPED</b> with an actionable <c>BTREE0002</c>, ⛔ never emitted half-formed. ⇒ ⭐ that is why
    /// this feature is safe to ship even though it does not cover every callee — the worst case is a
    /// skipped asset with a named reason, 📌 <b>not</b> the non-compiling output of <c>BP-306</c>.</para>
    ///
    /// <para>⚠ <b>Stated limit, not hidden:</b> Approach-B subtree sync currently works for a callee
    /// whose blackboard type is <b>resolvable</b> in the master's compilation. A generated-blackboard
    /// callee needs the shape derived from JSON instead of referenced by type — the blueprint
    /// <i>"Option A"</i> route. ⛔ Not built; recorded in <c>Q50</c>.</para>
    /// </summary>
    [Fact]
    public void ACalleeWhoseBlackboardTypeCannotBeResolved_SkipsTheAssetWithADiagnostic()
    {
        var callee = SampleScoutDto();
        callee.Name               = "ShootBT";
        callee.AssetId            = Guid.NewGuid();
        callee.BlackboardTypeName = "Made.Up.ShootBlackboard";   // ⛔ in no assembly

        var master = SampleScoutDto();
        master.Name    = "MasterAI";
        master.AssetId = Guid.NewGuid();
        master.Blackboard.Managed = true;
        string masterVar = EnsureVariable(master);

        var nodeId = Guid.NewGuid();
        master.Nodes.Add(new BTreeSubtreeNodeDto
        {
            VisualId = nodeId,
            Subtree  = new BTreeSubtreePayloadDto
            {
                SubtreeAssetId = callee.AssetId, SubtreeName = callee.Name, IsResolved = true,
            },
        });
        master.SubtreeSyncBindings[nodeId.ToString()] = new List<SubtreeSyncBindingDto>
        {
            new() { FieldName = "Health", MasterVariableName = masterVar, SyncIn = true, SyncOut = false },
        };

        var result = RunTwo(new BTreeJsonGenerator(),
            ("/p/MasterAI.btree.json", BTreeJsonServices.Serialize(master)),
            ("/p/ShootBT.btree.json",  BTreeJsonServices.Serialize(callee)));

        result.Diagnostics.Should().Contain(d => d.Id == BTreeJsonGenerator.CodegenWarningId,
            "an unresolvable slice type must SKIP the asset, loudly");
        OrchestratorText(result).Should().BeNull(
            "⛔ never a group against a field the compilation cannot type");
    }

    // ══ THE SIBLING CATALOG — UNWIRED, BUT CONTRACT-PINNED ══════════════════════════════
    //
    // 🔒 "Unreferenced is not unintentional." GeneratedBTreeSchemaCatalog lost its only CALLER when
    //    CE-337 retired the orchestrator arms — ⛔ but the CAPABILITY it provides is the one a
    //    generator cannot get any other way: what a SIBLING asset declares, read from JSON without
    //    loading an assembly (Q49 option D). Per-SITE BTree hosting needs exactly it.
    // ⛔⛔ Keeping a type while deleting its only exercise leaves it PRESENT, UNWIRED and UNTESTED —
    //    which is how a capability rots into a thing nobody dares re-wire. ⇒ these rails pin its
    //    CONTRACT directly, so it can be re-wired against a known-good answer.
    // 📄 DESIGN_Occurrence_Scoped_Storage.md §32.13.

    private static ImmutableArray<(string Path, string Text)> Texts(params (string Path, string Text)[] files)
        => ImmutableArray.Create(files);

    /// <summary>
    /// ⭐⭐ The catalog maps <c>AssetId → (Name, BlackboardTypeName)</c>, and it reads the
    /// <b>ASSET-LEVEL</b> <c>BlackboardTypeName</c>.
    ///
    /// <para>⛔⛔ <b>The field choice is load-bearing and the source says so:</b> NOT
    /// <c>dto.Blackboard.TypeName</c>, which is the BLOCK's name and a different field. The other arm
    /// reads the asset-level one, and two arms reading different properties would derive different
    /// identities for the same sub-tree — the silent divergence <c>SubtreeSyncIdentity</c> exists to
    /// prevent.</para>
    /// </summary>
    [Fact]
    public void TheSiblingCatalogReadsTheAssetLevelBlackboardTypeName()
    {
        var dto = SampleScoutDto();
        dto.Name = "ShootBT";
        dto.AssetId = Guid.NewGuid();
        dto.BlackboardTypeName = "Hrot.Game.ShootBlackboard";
        dto.Blackboard.TypeName = "NotThisOne";

        var catalog = GeneratedBTreeSchemaCatalog.Parse(
            Texts(("/p/ShootBT.btree.json", BTreeJsonServices.Serialize(dto))));

        catalog.Should().ContainKey(dto.AssetId);
        catalog[dto.AssetId].Name.Should().Be("ShootBT");
        catalog[dto.AssetId].BlackboardTypeName.Should().Be("Hrot.Game.ShootBlackboard",
            "⛔ the ASSET-level field, never the blackboard BLOCK's TypeName");
    }

    /// <summary>
    /// ⭐⭐ <b>Best-effort by contract: a malformed or type-less sibling is SKIPPED, never thrown on.</b>
    ///
    /// <para>🔒 The reason is in the source and it is a real constraint: a broken asset is already
    /// reported by its OWN generation pass, and a caller must not fail because a sibling is mid-edit.
    /// ⛔ A catalog that throws would turn one designer's half-saved file into everyone's build
    /// break.</para>
    /// </summary>
    [Fact]
    public void TheSiblingCatalogSkipsWhatItCannotUse_AndNeverThrows()
    {
        var good = SampleScoutDto();
        good.Name = "Good";
        good.AssetId = Guid.NewGuid();
        good.BlackboardTypeName = "Hrot.Game.GoodBlackboard";

        var noType = SampleScoutDto();
        noType.Name = "NoType";
        noType.AssetId = Guid.NewGuid();
        noType.BlackboardTypeName = "";       // ⛔ skipped: no identity to contribute

        var catalog = GeneratedBTreeSchemaCatalog.Parse(Texts(
            ("/p/Good.btree.json",    BTreeJsonServices.Serialize(good)),
            ("/p/NoType.btree.json",  BTreeJsonServices.Serialize(noType)),
            ("/p/Broken.btree.json",  "{ this is not json"),
            ("/p/Empty.btree.json",   "")));

        catalog.Should().ContainKey(good.AssetId, "the usable sibling must survive its neighbours");
        catalog.Should().NotContainKey(noType.AssetId, "a tree with no blackboard type contributes nothing");
        catalog.Should().HaveCount(1);
    }

    // ══ CE-337 — THE RETIRED ARMS' DATA IS LOUD, NOT SILENT ═════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE337_R1</c> — an asset that carries sub-tree sync bindings gets a WARNING, and
    /// NO auto-allocated slice variable.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.13.
    ///
    /// <para>🔴 <b>What used to happen:</b> the generator declared one blackboard variable per bound
    /// sub-tree so the Approach-B orchestrator could write <c>ref master.{slice}</c>. ⛔ That
    /// orchestrator is retired (<c>CE-337</c>), so the field would be declared, packed into the
    /// struct, and then read and written by NOTHING.</para>
    ///
    /// <para>⭐⭐ <b>The rule this serves is the silent-default one.</b> 📐 0 of 26 shipped assets
    /// carry a binding, so nothing warns today and no golden moves — ⚠ but an author can still
    /// create one in the editor, and before this they got silence.</para>
    /// </summary>
    [Fact]
    public void CE337_R1_SyncBindingsWarnAndDeclareNoSliceVariable()
    {
        var callee = SampleScoutDto();
        callee.Name = "ShootBT";
        callee.AssetId = Guid.NewGuid();
        callee.BlackboardTypeName = "System.Guid";

        var master = SampleScoutDto();
        master.Name = "MasterAI";
        master.AssetId = Guid.NewGuid();
        master.Blackboard.Managed = true;
        string masterVar = EnsureVariable(master);

        var nodeId = Guid.NewGuid();
        master.Nodes.Add(new BTreeSubtreeNodeDto
        {
            VisualId = nodeId,
            Subtree  = new BTreeSubtreePayloadDto
            {
                SubtreeAssetId = callee.AssetId, SubtreeName = callee.Name, IsResolved = true,
            },
        });
        master.SubtreeSyncBindings[nodeId.ToString()] = new List<SubtreeSyncBindingDto>
        {
            new() { FieldName = "Health", MasterVariableName = masterVar, SyncIn = true, SyncOut = true },
        };

        var result = RunTwo(new BTreeJsonGenerator(),
            ("/p/MasterAI.btree.json", BTreeJsonServices.Serialize(master)),
            ("/p/ShootBT.btree.json",  BTreeJsonServices.Serialize(callee)));

        result.Diagnostics.Should().Contain(d => d.Id == BTreeJsonGenerator.CodegenWarningId,
            "hosting data nothing consumes must be LOUD — that is the whole of CE-337's sweep");

        OrchestratorText(result).Should().BeNull("both arms are retired");

        string all = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        all.Should().NotContain("Auto-allocated sub-tree parameter slice",
            "⛔ no slice variable may be declared for a writer that no longer exists");
        all.Should().NotContain("ShootBT_",
            "⛔ and therefore no slice field reaches the generated blackboard struct");
    }

    /// <summary>
    /// ⭐⭐ <b><c>CE337_R2</c> — an ALIAS warns too, for the same reason.</b>
    ///
    /// <para>⚠ The alias arm was retired first (<c>CE-335</c>/<c>CE-337</c>), and alias data still
    /// round-trips — <c>AddAlias</c> and the mappers are live. ⇒ an author can create an alias and it
    /// will simply do nothing. ⭐ This makes that visible at build time.</para>
    /// </summary>
    [Fact]
    public void CE337_R2_AnAliasWarnsBecauseNothingConsumesIt()
    {
        var dto = SampleScoutDto();
        string varName = EnsureVariable(dto);
        dto.Aliases = new Dictionary<string, List<BlackboardAliasBindingDto>>
        {
            [varName] = new() { Alias("PatrolSubTree") },
        };

        var result = Run(new BTreeJsonGenerator(), "/p/SampleScout.btree.json",
            BTreeJsonServices.Serialize(dto));

        result.Diagnostics.Should().Contain(d => d.Id == BTreeJsonGenerator.CodegenWarningId,
            "an alias that nothing consumes must say so rather than being silently inert");
        OrchestratorText(result).Should().BeNull();
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE337_R3</c> — and the CORPUS stays silent.</b>
    ///
    /// <para>🔒 The other half of the claim, and the one that protects the build: a warning that
    /// fires on shipped assets would be noise nobody reads within a week. 📐 0 of 26 carry either
    /// kind of hosting data, so an unaliased asset must produce NO codegen warning at all.</para>
    /// </summary>
    [Fact]
    public void CE337_R3_AnOrdinaryAssetWarnsAboutNothing()
    {
        var result = Run(new BTreeJsonGenerator(), "/p/SampleScout.btree.json",
            BTreeJsonServices.Serialize(SampleScoutDto()));

        result.Diagnostics.Should().NotContain(d => d.Id == BTreeJsonGenerator.CodegenWarningId,
            "⛔ the corpus must stay silent — a warning everyone sees is a warning nobody reads");
    }

    // ══ CE-336 — THE EMITTED TEXT MUST COMPILE ═══════════════════════════════════════════
    //
    // 🔒 The rail this suite was missing, and its absence is what let TWO defects ship in one
    //    emission: an [HsmAction] carrying the FastBTree ACTION signature (CE-333) and a call to
    //    {Child}.GetInterpreter(), a method defined NOWHERE (CE-335).
    // ⛔⛔ Every other rail here asserts STRINGS. A text-asserting golden cannot tell you the code
    //    it pins is not valid C#. 📄 DESIGN_Occurrence_Scoped_Storage.md §32.10 A2.

    /// <summary>
    /// ⭐⭐ A DTO type that EXISTS — the deliberate opposite of <see cref="UnloadableDtoTypeId"/>.
    ///
    /// <para>⚠ The other rails use an unloadable id on purpose, to prove the emitter never resolves a
    /// <c>System.Type</c>. ⛔ A COMPILE rail cannot use it: the emitted text names the type, so the
    /// type must be real. ⇒ the two fixtures are complementary, not redundant.</para>
    /// </summary>
    private static BlackboardAliasBindingDto CompilableAlias(string subAssetName) => new()
    {
        RequiringAssetId   = Guid.NewGuid(),
        RequiringElementId = Guid.NewGuid(),
        RequiringAssetName = subAssetName,
        RequiredByPath     = "Root/Move",
        DtoTypeId          = typeof(Ce336PatrolParams).FullName,
    };

    /// <summary>
    /// ⭐⭐⭐ Every assembly this test process has loaded, as metadata references.
    ///
    /// <para>⭐ The <c>typeof</c> touches are load-bearing: a referenced assembly is not loaded until
    /// something in it is used, and an emitted file that references <c>HostedSubtree</c> against a
    /// compilation that never loaded <c>Fdp.Toolkits</c> fails for the WRONG reason.</para>
    /// </summary>
    private static MetadataReference[] RealReferences()
    {
        _ = typeof(Fdp.Toolkit.Behavior.HostedSubtree);
        _ = typeof(Fdp.Toolkit.Behavior.HostedChildren);
        _ = typeof(Fdp.Toolkit.Behavior.HsmHostedSubtrees);
        _ = typeof(Fdp.Toolkit.Behavior.BehaviorRegistry);
        _ = typeof(Fbt.NodeStatus);
        _ = typeof(Fbt.Runtime.Interpreter<byte, Fdp.Toolkit.Behavior.BTreeContext>);
        _ = typeof(Fhsm.Kernel.Data.HsmCommandWriter);
        _ = typeof(Fdp.Core.EntityRepository);
        _ = BehaviorsAssembly;

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .GroupBy(a => a.GetName().Name, StringComparer.Ordinal)
            .Select(g => (MetadataReference)MetadataReference.CreateFromFile(g.First().Location))
            .ToArray();
    }

    /// <summary>Compiles every tree the generator produced and returns only the ERRORS.</summary>
    private static IReadOnlyList<Diagnostic> CompileErrors(GeneratorDriverRunResult result)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "Ce336Emitted",
            syntaxTrees:  result.GeneratedTrees,
            references:   RealReferences(),
            options:      new CSharpCompilationOptions(
                              OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    private static string Describe(IReadOnlyList<Diagnostic> errors) =>
        string.Join("\n", errors.Take(15).Select(d => $"  {d.Id} {d.GetMessage()} @ {d.Location}"));

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE336_R1</c> — the emitted BTree REGISTRAR compiles.</b>
    ///
    /// <para>⚠ <b>REPOINTED <c>2026-09-23</c> (<c>CE-337</c>).</b> This compiled the emitted
    /// ORCHESTRATOR, and doing so is what found the three defects that got that arm retired. ⛔ With
    /// both arms emitting nothing there is no orchestrator left to compile — so the rail moves to the
    /// artefact that IS emitted for every asset and every build: the bridge registrar.</para>
    ///
    /// <para>⭐ That is not a downgrade. The registrar is where the slot manifest, the params supply
    /// and the interpreter construction live — far more surface than the orchestrator ever had, and
    /// none of it had a compile rail either.</para>
    /// </summary>
    [Fact]
    public void CE336_R1_TheEmittedBTreeRegistrarCompiles()
    {
        var result = Run(new BTreeJsonGenerator(), "/p/SampleScout.btree.json",
            BTreeJsonServices.Serialize(SampleScoutDto()));

        result.GeneratedTrees.Should().NotBeEmpty("the generator must have emitted something to compile");

        var errors = CompileErrors(result);
        errors.Should().BeEmpty(
            "the emitted BTree registrar must be valid C# — the rail CE-333/CE-335/CE-337 needed and "
            + "did not have:\n" + Describe(errors));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE336_R2</c> — the emitted HSM registrar COMPILES with a hosting state.</b>
    /// 📄 <c>E5</c> acceptance <c>A2</c>, §32.10.
    ///
    /// <para>⭐ This is the half of <c>E5</c> the runtime rails (<c>E5_R1</c>..<c>E5_R4</c>) cannot
    /// reach: they drive hand-registered tables, so they prove the MECHANISM and not the EMISSION.
    /// ⛔ Without this rail the emitter half of <c>E5</c> is proven only by inspection — which is
    /// exactly the state <c>CE-333</c> lived in.</para>
    /// </summary>
    [Fact]
    public void CE336_R2_TheEmittedHsmRegistrarWithAHostingStateCompiles()
    {
        var dto = SampleGuardDto();
        dto.States.Should().NotBeEmpty("the fixture needs a state to host from");

        // ⭐ What an author does in the editor: name a child on a state (Q36-B = A — BOTH halves).
        var host = dto.States[0];
        host.SubtreeAssetId = Guid.NewGuid();
        host.SubtreeName    = "PatrolSubTree";

        var result = Run(new HsmJsonGenerator(), "/p/SampleGuard.hsm.json",
            HsmJsonServices.Serialize(dto));

        string registrar = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        registrar.Should().Contain("HsmHostedSubtrees.Register(",
            "the hosting table must be emitted for a state that names a child");
        registrar.Should().Contain("HostedChildren.Register(beh,",
            "and the child's interpreter must be bound where the registry is in hand");
        registrar.Should().Contain("typeof(global::Fbt.BehaviorTreeState)",
            "⛔ the child's tree-state SLOT ships with the hosting call or HostedSubtree.Tick throws");

        var errors = CompileErrors(result);
        errors.Should().BeEmpty("the emitted HSM registrar must be valid C#:\n" + Describe(errors));
    }
}

// ── CE-336 fixture types ─────────────────────────────────────────────────────────────
// ⚠ TOP-LEVEL on purpose: a NESTED type's Type.FullName carries a '+' separator, and the
//   emitter splits an id at the last '.' — so a nested fixture emits `Outer+Inner`, which is
//   not valid C#. ⛔ That is a property of the FIXTURE, not a defect in the emitter, and using
//   a nested type here would have manufactured a failure that says nothing about production.
public struct Ce336PatrolParams
{
    public float Health;
    public int   Ammo;
}

/// <summary>
/// ⭐⭐ The MASTER blackboard the alias projects onto — a real struct with a real field of the
/// aliased DTO type, because <c>ref master.{VarName}</c> must bind.
///
/// <para>⚠⚠ <b>The fixture declares <c>BlackboardTypeName</c> explicitly, and that is a FINDING,
/// not convenience.</b> 📐 The default is <c>AiEmitCoreBase.DefaultBlackboardTypeName</c> =
/// <c>Fdp.Toolkit.Behavior.Components.BrainBlackboard</c> — a type <c>P4</c> <b>RETIRED</b> — and
/// every shipped <c>*.btree.json</c> still names it. ⇒ no corpus asset can satisfy this arm today.
/// 📋 <c>CE-337</c>.</para>
/// </summary>
public struct Ce336MasterBlackboard
{
    public Ce336PatrolParams Health;
}

