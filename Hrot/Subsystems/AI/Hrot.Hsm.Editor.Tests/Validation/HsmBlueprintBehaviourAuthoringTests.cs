using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Selection;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Hrot.Hsm.Editor.Validation;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Validation;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-385</c> — an editor-authored HSM can BIND A BLUEPRINT as a state's activity and as
/// a transition's guard, and can MARK a transition polled.</b>
/// 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.1, §3.2, §7, §9 ③.
///
/// <para>🔴 <b>What was missing.</b> <c>CE-381</c>..<c>CE-384</c> made all of this WORK at runtime
/// and shipped the DTO fields — but the editor MODEL had none of them, so the only way to author a
/// blueprint-hosted behaviour was to hand-edit the <c>.hsm.json</c>. ⛔ Every rail below is about
/// the authoring path, not the runtime: the runtime is <c>PolledTransitionKernelTests</c>'s and
/// <c>ExplicitActionIdOverrideTests</c>'s to prove, and they already do.</para>
///
/// <para>⭐ Shaped on <c>HsmSubtreeAuthoringTests</c> (the <c>E5</c> precedent), because the
/// name+Guid pick is literally the same rule — see <c>HsmFacetDispatcher.ResolvePickedAssetId</c>,
/// which both now share.</para>
/// </summary>
public sealed class HsmBlueprintBehaviourAuthoringTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    private sealed class CatalogAsset : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "ChaseTarget";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath => "/x.bp.json";
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

    /// <summary>Two states and one transition between them — the smallest machine the user's
    /// acceptance description needs ("a few states and transitions between them").</summary>
    private static HsmAsset MakeMachine(out StateNode from, out TransitionNode link)
    {
        var root = new StateNode("__root__");
        var a    = new StateNode("Seeking") { IsInitial = true, Parent = root };
        var b    = new StateNode("Engaging") { Parent = root };
        root.Children.Add(a);
        root.Children.Add(b);

        var t = new TransitionNode { VisualId = Guid.NewGuid(), Source = a, Target = b };
        a.OutgoingTransitions.Add(t);

        from = a;
        link = t;

        return new HsmAsset(
            Guid.NewGuid(), "Hunter", "", false, "Demo.Machines",
            new HsmDefinitionBlob(), new MachineMetadata(),
            root,
            new List<StateNode> { a, b },
            new List<TransitionNode> { t },
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());
    }

    // ── ① the pick writes BOTH halves, for BOTH slots ─────────────────────────

    /// <summary>
    /// ⭐⭐⭐ The Guid is captured AT PICK TIME, while the catalogue entry is in hand — ⛔ deriving
    /// it on load only would leave nothing to heal from after a rename. Same claim as
    /// <c>HsmSubtreeAuthoringTests.PickingATree_WritesTheNameAndCapturesTheGuid</c>, now for the
    /// slot the emitter bakes an id from.
    /// </summary>
    [Fact]
    public void PickingAnActivityBlueprint_WritesTheNameAndCapturesTheGuid()
    {
        var bp  = new CatalogAsset { Name = "ChaseTarget" };
        var hsm = MakeMachine(out var state, out _);
        var d   = new HsmFacetDispatcher(hsm, null, new FakeCatalog(bp));

        var sel   = new HsmStateSelection(state.StableId);
        var facet = (StateFacet)d.GetFacet(sel)!;
        facet.ActivityBlueprintName = "ChaseTarget";
        d.ApplyFacet(sel, facet);

        (state.Activity?.BlueprintName).Should().Be("ChaseTarget");
        (state.Activity?.BlueprintAssetId ?? Guid.Empty).Should().Be(bp.AssetId,
            "the id the emitter bakes comes from the Guid, never from the name");
    }

    [Fact]
    public void PickingAGuardBlueprint_WritesTheNameAndCapturesTheGuid()
    {
        var bp  = new CatalogAsset { Name = "IsInRange" };
        var hsm = MakeMachine(out _, out var t);
        var d   = new HsmFacetDispatcher(hsm, null, new FakeCatalog(bp));

        var sel   = new HsmTransitionSelection(t.VisualId);
        var facet = (TransitionFacet)d.GetFacet(sel)!;
        facet.GuardBlueprintName = "IsInRange";
        d.ApplyFacet(sel, facet);

        (t.Guard?.BlueprintName).Should().Be("IsInRange");
        (t.Guard?.BlueprintAssetId ?? Guid.Empty).Should().Be(bp.AssetId);
    }

    /// <summary>
    /// ⭐ Clearing the field UNSETS both halves — an empty name over a live Guid is a reference the
    /// designer can neither see nor edit.
    /// </summary>
    [Fact]
    public void ClearingTheBlueprintName_AlsoClearsTheGuid()
    {
        var bp  = new CatalogAsset();
        var hsm = MakeMachine(out var state, out _);
        (state.Activity ??= new BehaviorActionBinding()).BlueprintName    = bp.Name;
        (state.Activity ??= new BehaviorActionBinding()).BlueprintAssetId = bp.AssetId;

        var d     = new HsmFacetDispatcher(hsm, null, new FakeCatalog(bp));
        var sel   = new HsmStateSelection(state.StableId);
        var facet = (StateFacet)d.GetFacet(sel)!;
        facet.ActivityBlueprintName = "";
        d.ApplyFacet(sel, facet);

        (state.Activity?.BlueprintName).Should().BeNull();
        (state.Activity?.BlueprintAssetId ?? Guid.Empty).Should().Be(Guid.Empty);
    }

    /// <summary>
    /// ⛔⛔ <b>THE NEVER-ERASE BRANCH</b> (§7.1a ③). A name with no catalogue match — a stale
    /// reference, or a headless host with no catalogue at all — keeps the Guid already captured.
    /// ⚠ A catalogue that cannot SEE an asset must never DESTROY the reference to it.
    /// </summary>
    [Fact]
    public void AnUnresolvableName_KeepsThePreviouslyCapturedGuid()
    {
        var bp  = new CatalogAsset { Name = "ChaseTarget" };
        var hsm = MakeMachine(out var state, out _);
        (state.Activity ??= new BehaviorActionBinding()).BlueprintName    = "ChaseTarget";
        (state.Activity ??= new BehaviorActionBinding()).BlueprintAssetId = bp.AssetId;

        // A catalogue that knows nothing — the partial-checkout case.
        var d     = new HsmFacetDispatcher(hsm, null, new FakeCatalog());
        var sel   = new HsmStateSelection(state.StableId);
        var facet = (StateFacet)d.GetFacet(sel)!;
        d.ApplyFacet(sel, facet);

        (state.Activity?.BlueprintAssetId ?? Guid.Empty).Should().Be(bp.AssetId,
            "a catalogue that cannot see the asset must not destroy the reference to it");
    }

    /// <summary>
    /// ⚠ A BTree picked into the BLUEPRINT slot must not resolve. ⭐ The kind check is what stops
    /// the two asset pickers bleeding into each other — see <c>CE-396</c>'s drawer fix, which is the
    /// same defect one layer up.
    /// </summary>
    [Fact]
    public void AnAssetOfTheWrongKind_DoesNotResolve()
    {
        var tree = new CatalogAsset { Name = "PatrolTree", Kind = AssetKind.BTree };
        var hsm  = MakeMachine(out var state, out _);
        var d    = new HsmFacetDispatcher(hsm, null, new FakeCatalog(tree));

        var sel   = new HsmStateSelection(state.StableId);
        var facet = (StateFacet)d.GetFacet(sel)!;
        facet.ActivityBlueprintName = "PatrolTree";
        d.ApplyFacet(sel, facet);

        (state.Activity?.BlueprintAssetId ?? Guid.Empty).Should().Be(Guid.Empty,
            "a BTree is not a blueprint, however plausible the name looks in the dropdown");
    }

    // ── ② the polled marker is authorable ─────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <c>CE-381</c>'s marker, reachable from the inspector. ⛔ Not the same as "a transition
    /// with no event" — 📄 §2.3.
    /// </summary>
    [Fact]
    public void TheInspectorCanMarkATransitionPolled()
    {
        var hsm = MakeMachine(out _, out var t);
        var d   = new HsmFacetDispatcher(hsm);

        var sel   = new HsmTransitionSelection(t.VisualId);
        var facet = (TransitionFacet)d.GetFacet(sel)!;
        facet.IsPolled.Should().BeFalse("nothing is polled until it is marked");
        facet.IsPolled = true;
        d.ApplyFacet(sel, facet);

        t.IsPolled.Should().BeTrue();
        ((TransitionFacet)d.GetFacet(sel)!).IsPolled.Should().BeTrue("and the facet reads it back");
    }

    // ── ③ the five fields survive the REAL save → reopen path ─────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The mapper's both directions, through the real persistence path.</b>
    /// ⚠ This is the rail that would have caught a half-mapped field — the failure mode
    /// <c>CE-387</c> exists to fix on the transition side (mapped for transitions, never for states
    /// ⇒ always null, silently).
    /// </summary>
    [Fact]
    public void TheFiveFieldsSurviveSaveAndReopen()
    {
        var bpActivity = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
        var bpGuard    = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

        var hsm = MakeMachine(out var state, out var t);
        (state.Activity ??= new BehaviorActionBinding()).BlueprintAssetId = bpActivity;
        (state.Activity ??= new BehaviorActionBinding()).BlueprintName    = "ChaseTarget";
        (t.Guard ??= new BehaviorActionBinding()).BlueprintAssetId        = bpGuard;
        (t.Guard ??= new BehaviorActionBinding()).BlueprintName           = "IsInRange";
        t.IsPolled                     = true;

        string json  = HsmJsonServices.Serialize(HsmAssetMapper.ToDto(hsm));
        var    back  = HsmAssetMapper.ToModel(HsmJsonServices.Deserialize(json)!, "", true);

        var s2 = back.AllStates.Single(x => x.Name == "Seeking");
        (s2.Activity?.BlueprintAssetId ?? Guid.Empty).Should().Be(bpActivity);
        (s2.Activity?.BlueprintName).Should().Be("ChaseTarget");

        var t2 = back.AllTransitions.Single();
        (t2.Guard?.BlueprintAssetId ?? Guid.Empty).Should().Be(bpGuard);
        (t2.Guard?.BlueprintName).Should().Be("IsInRange");
        t2.IsPolled.Should().BeTrue();
    }

    /// <summary>
    /// ⛔⛔ <b>THE NO-CHURN PROPERTY, asserted rather than hoped.</b> An asset that binds none of
    /// this must serialise WITHOUT the new keys — otherwise every shipped asset's canonical JSON
    /// moves the day this lands, and <c>HsmGoldenCorpusTests</c> goes red for a feature nobody used.
    /// 📄 design §8a ⑥, which made this a build CONSTRAINT rather than a hope.
    /// </summary>
    [Fact]
    public void AnAssetBindingNoneOfThis_SerialisesWithoutTheNewKeys()
    {
        string json = HsmJsonServices.Serialize(HsmAssetMapper.ToDto(MakeMachine(out _, out _)));

        json.Should().NotContain("ActivityBlueprint");
        json.Should().NotContain("GuardBlueprint");
        json.Should().NotContain("IsPolled");
    }

    // ── ④ acceptance rail ③ — METHOD XOR BLUEPRINT ────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Design §9 ③, and it was NEITHER BUILT NOR FILED until now.</b> A state naming both
    /// an activity action and an activity blueprint was silently ACCEPTED.
    ///
    /// <para>🔴 <b>Why silence is the worst outcome here:</b> <c>HsmFlattener</c>'s explicit-id
    /// override (<c>CE-383</c>) makes the BLUEPRINT win, so the asset compiles, the named action is
    /// discarded with no message, and the designer debugs the wrong behaviour.</para>
    /// </summary>
    [Fact]
    public void AStateBindingBothAnActionAndABlueprint_IsAnError()
    {
        var hsm = MakeMachine(out var state, out _);
        (state.Activity ??= new BehaviorActionBinding()).MethodFqn           = "Demo.Actions.Chase";
        (state.Activity ??= new BehaviorActionBinding()).BlueprintAssetId = Guid.NewGuid();

        new HsmValidator().Validate(hsm)
            .Should().Contain(d => d.Code == HsmDiagnosticCode.MethodAndBlueprintBothBound
                                && d.Severity == HsmDiagnosticSeverity.Error);
    }

    [Fact]
    public void ATransitionBindingBothAGuardFunctionAndABlueprint_IsAnError()
    {
        var hsm = MakeMachine(out _, out var t);
        (t.Guard ??= new BehaviorActionBinding()).MethodFqn         = "Demo.Guards.InRange";
        (t.Guard ??= new BehaviorActionBinding()).BlueprintAssetId = Guid.NewGuid();

        new HsmValidator().Validate(hsm)
            .Should().Contain(d => d.Code == HsmDiagnosticCode.MethodAndBlueprintBothBound);
    }

    /// <summary>
    /// ⭐⭐ <b>The converse, and it is what makes the rule above load-bearing rather than noisy.</b>
    /// Binding EITHER one alone is the normal case and must stay silent. ⛔ A rule that fires on the
    /// ordinary asset gets switched off within a batch.
    /// </summary>
    [Theory]
    [InlineData(true,  false)]   // action only
    [InlineData(false, true)]    // blueprint only
    [InlineData(false, false)]   // neither
    public void BindingOnlyOneOfThem_IsSilent(bool action, bool blueprint)
    {
        var hsm = MakeMachine(out var state, out _);
        if (action)    (state.Activity ??= new BehaviorActionBinding()).MethodFqn           = "Demo.Actions.Chase";
        if (blueprint) (state.Activity ??= new BehaviorActionBinding()).BlueprintAssetId = Guid.NewGuid();

        new HsmValidator().Validate(hsm)
            .Should().NotContain(d => d.Code == HsmDiagnosticCode.MethodAndBlueprintBothBound);
    }

    /// <summary>
    /// ⚠ <b>The rule keys on the GUID, not the NAME.</b> A stale display name left behind by a
    /// rename must not manufacture a conflict — only a real bound blueprint does.
    /// </summary>
    [Fact]
    public void AStaleBlueprintNameWithNoGuid_DoesNotTripTheRule()
    {
        var hsm = MakeMachine(out var state, out _);
        (state.Activity ??= new BehaviorActionBinding()).MethodFqn        = "Demo.Actions.Chase";
        (state.Activity ??= new BehaviorActionBinding()).BlueprintName = "SomethingRenamedAwayLongAgo";

        new HsmValidator().Validate(hsm)
            .Should().NotContain(d => d.Code == HsmDiagnosticCode.MethodAndBlueprintBothBound);
    }
}
