using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Selection;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Validation;

/// <summary>
/// ⭐⭐⭐ <b>HSM SUBTREE AUTHORING — the state's hosted BTree becomes PICKABLE.</b>
/// 📄 <c>HSM_Editor_NodeEditor_Host_Design.md</c> §11.1a; the heal rule is
/// <c>AI_Editor_Shared_Infrastructure.md</c> §7.1a.
///
/// <para>🔒 User, <c>2026-09-26</c>: *"the tree asset must be pickable."*</para>
///
/// <para>🔴 <b>What was broken:</b> <c>E5</c> built the entire runtime for *"an HSM state hosts a
/// BTree"* — the slot, the registry, the tick, validator rules 8/8b/10 — and <c>StateFacet</c> had
/// <b>no subtree field</b>. ⇒ nothing outside the mapper could write the reference, so every rule
/// <c>E5</c> added was unreachable on a real asset. These rails pin the authoring path that closes it.</para>
/// </summary>
public sealed class HsmSubtreeAuthoringTests
{
    private sealed class Tree : IEditableAsset, IBehaviorInputsContract
    {
        public string? InputsTypeId { get; init; }   // ⭐ CE-439: the child's Inputs struct, as the compose step reads it
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "PatrolTree";
        public AssetKind Kind { get; init; } = AssetKind.BTree;
        public string SourceFilePath => "/t.btree.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _a;
        public FakeCatalog(params IEditableAsset[] assets) => _a = assets.ToList();
        public IReadOnlyList<IEditableAsset> All => _a;
        public IEditableAsset? FindByAssetId(Guid id) => _a.FirstOrDefault(x => x.AssetId == id);
        public IEditableAsset? FindByName(string n) => _a.FirstOrDefault(x => x.Name == n);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id) => Array.Empty<IEditableAsset>();
#pragma warning disable 67
        public event Action<AssetKind>? Changed;
#pragma warning restore 67
    }

    private static HsmAsset MakeMachine(out StateNode hostState)
    {
        var root = new StateNode("__root__");
        var s    = new StateNode("Patrolling") { IsInitial = true, Parent = root };
        root.Children.Add(s);
        hostState = s;

        return new HsmAsset(
            Guid.NewGuid(), "Sentry", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            root,
            new List<StateNode> { s },
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());
    }

    // ── the pick writes BOTH halves ───────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The Guid is captured AT PICK TIME</b>, while the catalogue entry is in hand.
    /// ⛔ Deriving it only on load would mean a rename between the pick and the first reload leaves
    /// nothing to heal from — and the entire reason for storing both would be lost.
    /// </summary>
    [Fact]
    public void PickingATree_WritesTheNameAndCapturesTheGuid()
    {
        var tree = new Tree();
        var hsm  = MakeMachine(out var state);
        var d    = new HsmFacetDispatcher(hsm, null, new FakeCatalog(tree));

        var facet = (StateFacet)d.GetFacet(new HsmStateSelection(state.StableId))!;
        facet.SubtreeName = "PatrolTree";
        d.ApplyFacet(new HsmStateSelection(state.StableId), facet);

        state.SubtreeName.Should().Be("PatrolTree");
        state.SubtreeAssetId.Should().Be(tree.AssetId);     // ⭐ captured, not left empty
        state.IsSubtreeResolved.Should().BeTrue();
    }

    /// <summary>⭐ Clearing the field unsets the host entirely — a designer must be able to say "none".</summary>
    [Fact]
    public void ClearingTheField_UnsetsBothHalves()
    {
        var tree = new Tree();
        var hsm  = MakeMachine(out var state);
        state.SubtreeName    = "PatrolTree";
        state.SubtreeAssetId = tree.AssetId;

        var d     = new HsmFacetDispatcher(hsm, null, new FakeCatalog(tree));
        var facet = (StateFacet)d.GetFacet(new HsmStateSelection(state.StableId))!;
        facet.SubtreeName = null;
        d.ApplyFacet(new HsmStateSelection(state.StableId), facet);

        state.SubtreeName.Should().BeNull();
        state.SubtreeAssetId.Should().Be(Guid.Empty);
    }

    // ── the walker applies the shared heal rule ───────────────────────────────

    [Fact]
    public void TheResolver_HealsTheNameAfterARename_AndReportsIt()
    {
        var tree = new Tree { Name = "PatrolTree_v2" };
        var hsm  = MakeMachine(out var state);
        state.SubtreeName    = "PatrolTree";     // the old name, as saved
        state.SubtreeAssetId = tree.AssetId;     // the rename survivor

        int healed = HsmSubtreeResolver.Resolve(hsm, new FakeCatalog(tree));

        healed.Should().Be(1);                            // ⭐ the caller must mark dirty
        state.SubtreeName.Should().Be("PatrolTree_v2");   // ⭐ healed
        state.IsSubtreeResolved.Should().BeTrue();
    }

    /// <summary>
    /// ⛔⛔⛔ <b>NEVER ERASE.</b> The HSM twin of the shipped BTree defect: a reference that resolves
    /// by neither name nor Guid keeps <b>both</b> fields. 🔒 The asset may be absent from THIS
    /// session's catalogue and present in the next.
    /// </summary>
    [Fact]
    public void ADanglingReference_KeepsBothHalves()
    {
        var strangerId = Guid.NewGuid();
        var hsm = MakeMachine(out var state);
        state.SubtreeName    = "Vanished";
        state.SubtreeAssetId = strangerId;

        HsmSubtreeResolver.Resolve(hsm, new FakeCatalog());

        state.SubtreeName.Should().Be("Vanished");
        state.SubtreeAssetId.Should().Be(strangerId);   // ⛔ NOT Guid.Empty
        state.IsSubtreeResolved.Should().BeFalse();
    }

    // ── the validator rule ────────────────────────────────────────────────────

    [Fact]
    public void ADanglingSubtree_IsAValidationError_NamingTheState()
    {
        var hsm = MakeMachine(out var state);
        state.SubtreeName    = "Vanished";
        state.SubtreeAssetId = Guid.NewGuid();
        var catalog = new FakeCatalog();
        HsmSubtreeResolver.Resolve(hsm, catalog);

        var d = new HsmValidator(catalog: catalog).Validate(hsm);

        var dangling = d.Should()
            .ContainSingle(x => x.Code == HsmDiagnosticCode.SubtreeReferenceDangling).Subject;
        dangling.Severity.Should().Be(HsmDiagnosticSeverity.Error);
        dangling.Message.Should().Contain("Patrolling").And.Contain("Vanished");
    }

    /// <summary>
    /// 🔴 <b>THE RED-PROOF / control arm:</b> the same machine with a reference that RESOLVES must
    /// produce no dangling diagnostic. ⛔ If this reddens, the rule is firing on ordinary assets.
    /// </summary>
    [Fact]
    public void AResolvingSubtree_ProducesNoDanglingDiagnostic()
    {
        var tree    = new Tree();
        var hsm     = MakeMachine(out var state);
        state.SubtreeName    = "PatrolTree";
        state.SubtreeAssetId = tree.AssetId;
        var catalog = new FakeCatalog(tree);
        HsmSubtreeResolver.Resolve(hsm, catalog);

        new HsmValidator(catalog: catalog).Validate(hsm)
            .Should().NotContain(x => x.Code == HsmDiagnosticCode.SubtreeReferenceDangling);
    }

    /// <summary>
    /// ⛔⛔⛔ <b>NO CATALOGUE ⇒ NO VERDICT.</b> 🔴 The rail that caught a real defect in this very
    /// rule: the first draft read the derived <c>IsSubtreeResolved</c> flag, which defaults to
    /// <c>false</c>, so a validator with no catalogue reported <b>every</b> hosted subtree as
    /// dangling — reddening <c>CE-338</c>/<c>CE-339</c>'s control arms in
    /// <c>HsmDocumentFactoryTests</c>.
    ///
    /// <para>⭐ Without a catalogue you cannot know whether a reference resolves. ⚠ Rule 10
    /// (<c>SubtreeAssetCycle</c>) already skips for exactly this reason; this rule now matches it.</para>
    /// </summary>
    [Fact]
    public void WithNoCatalog_TheRuleIsSkippedRatherThanGuessing()
    {
        var hsm = MakeMachine(out var state);
        state.SubtreeName    = "Vanished";
        state.SubtreeAssetId = Guid.NewGuid();

        new HsmValidator().Validate(hsm)
            .Should().NotContain(x => x.Code == HsmDiagnosticCode.SubtreeReferenceDangling);
    }

    /// <summary>
    /// ⚠ <b>A state that hosts NOTHING is the ordinary case</b> — it must not be reported. ⛔ Without
    /// this the rule would light up every plain state in every machine.
    /// </summary>
    [Fact]
    public void AStateThatHostsNothing_IsNotReported()
    {
        var hsm = MakeMachine(out _);
        HsmSubtreeResolver.Resolve(hsm, new FakeCatalog());

        new HsmValidator().Validate(hsm)
            .Should().NotContain(x => x.Code == HsmDiagnosticCode.SubtreeReferenceDangling);
    }

    // ── ⭐⭐ CE-439 — the pick binds the child's params (Q76 §12.28) ─────────────────────────────

    /// <summary>A stand-in for a child's generated Inputs struct (any loaded struct resolves the same way).</summary>
    public struct PatrolInputs { public float Speed; public int Laps; }

    private static string PatrolInputsId => typeof(PatrolInputs).FullName!;

    private static (HsmAsset Hsm, StateNode State, HsmFacetDispatcher D) Pick(Tree tree, string name = "PatrolTree")
    {
        var hsm = MakeMachine(out var state);
        var d   = new HsmFacetDispatcher(hsm, null, new FakeCatalog(tree));
        var facet = (StateFacet)d.GetFacet(new HsmStateSelection(state.StableId))!;
        facet.SubtreeName = name;
        d.ApplyFacet(new HsmStateSelection(state.StableId), facet);
        return (hsm, state, d);
    }

    /// <summary>
    /// 🔴 RED before: the pick wrote the name and Guid only — nothing created or bound a host variable, so the child always
    /// started from its defaults. ⭐ Now the host gains a <c>Role=Input</c> variable typed as the child's Inputs struct, bound
    /// as the state's <c>SubtreeParamsVariable</c>.
    /// </summary>
    [Fact]
    public void CE439_PickingATreeWithInputs_ComposesAndBindsItsParamsVariable()
    {
        var (hsm, state, _) = Pick(new Tree { InputsTypeId = PatrolInputsId });

        state.SubtreeParamsVariable.Should().Be("PatrolTreeParams");
        var v = hsm.BlackboardVariables.Single(x => x.Name == "PatrolTreeParams");
        v.FieldType.Should().Be(typeof(PatrolInputs));
        v.Role.Should().Be(Hrot.AiEditor.Persistence.BlackboardVariableRole.Input);
        hsm.IsBlackboardEditorManaged.Should().BeTrue("both emitters gate the params path on the managed flag");
    }

    /// <summary>⭐ Re-applying the same pick (every facet edit does) neither duplicates the variable nor loses the binding.</summary>
    [Fact]
    public void CE439_ReapplyingTheSamePick_KeepsOneVariable()
    {
        var (hsm, state, d) = Pick(new Tree { InputsTypeId = PatrolInputsId });
        var facet = (StateFacet)d.GetFacet(new HsmStateSelection(state.StableId))!;
        d.ApplyFacet(new HsmStateSelection(state.StableId), facet);

        hsm.BlackboardVariables.Count(x => x.FieldType == typeof(PatrolInputs)).Should().Be(1);
        state.SubtreeParamsVariable.Should().Be("PatrolTreeParams");
    }

    /// <summary>⭐ A child that publishes no Inputs binds nothing — it starts from its own defaults (§11.4).</summary>
    [Fact]
    public void CE439_ATreeWithNoInputs_BindsNothing()
    {
        var (hsm, state, _) = Pick(new Tree { InputsTypeId = null });

        state.SubtreeParamsVariable.Should().BeNull();
        hsm.BlackboardVariables.Should().NotContain(x => x.Name == "PatrolTreeParams");
    }

    /// <summary>⭐ Clearing the pick unbinds the site (the variable stays — it may be referenced elsewhere).</summary>
    [Fact]
    public void CE439_ClearingThePick_Unbinds()
    {
        var (_, state, d) = Pick(new Tree { InputsTypeId = PatrolInputsId });
        var facet = (StateFacet)d.GetFacet(new HsmStateSelection(state.StableId))!;
        facet.SubtreeName = "";
        d.ApplyFacet(new HsmStateSelection(state.StableId), facet);

        state.SubtreeParamsVariable.Should().BeNull();
    }
}
