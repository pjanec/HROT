using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Validation;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-387</c> — a STATE's params-seed binding is authorable, and it reaches the emitter.</b>
/// 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.4;
/// <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.6.
///
/// <para>🔴 <b>The defect, and it is a member reporting success while doing nothing.</b>
/// <c>StateNodeDto.ExpressionTargetField</c> has existed since <c>E3b-0</c> and
/// <c>HsmBridgeEmitCore.EmitStateParamBindings</c> already turns it into a
/// <c>HsmParamBindings.Register</c> table — but <c>StateNode</c> (the EDITOR model) had no such field
/// and <c>HsmAssetMapper</c> mapped it for TRANSITIONS only. ⇒ the DTO value was <b>always null</b>,
/// the table was never emitted, and every state's hosted occurrence seeded from offset 0. ⛔ Two
/// parallel regions running one asset therefore got their own COPY of the SAME authored value — the
/// exact thing <c>E3b-0</c> was built to fix.</para>
///
/// <para>⭐⭐ <b><c>E7b</c> is the worked precedent</b> — it did this for the TRANSITION-level field.
/// ⚠ <b>But the two are DIFFERENT concepts under one name</b>, and these rails pin the difference:
/// a transition's is an OUTPUT (the field that receives its action's result, hence the cross-region
/// writer-conflict rule); a state's is an INPUT (the variable its occurrence seeds FROM).</para>
/// </summary>
public sealed class HsmStateParamSeedAuthoringTests
{
    private static HsmAsset MakeMachine(out StateNode state)
    {
        var root = new StateNode("__root__");
        var s    = new StateNode("Patrolling") { IsInitial = true, Parent = root };
        root.Children.Add(s);
        state = s;

        return new HsmAsset(
            Guid.NewGuid(), "Sentry", "", false, "Demo.Machines",
            new HsmDefinitionBlob(), new MachineMetadata(),
            root,
            new List<StateNode> { s },
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());
    }

    // ── the inspector writes it, the model holds it ───────────────────────────

    [Fact]
    public void TheStateInspectorRoundTripsTheSeedBinding()
    {
        var hsm = MakeMachine(out var state);
        var d   = new HsmFacetDispatcher(hsm);
        var sel = new HsmStateSelection(state.StableId);

        var facet = (StateFacet)d.GetFacet(sel)!;
        facet.ExpressionTargetField.Should().BeNull("unbound is the common case, not an error");
        facet.ExpressionTargetField = "Alpha";
        d.ApplyFacet(sel, facet);

        state.ExpressionTargetField.Should().Be("Alpha");
        ((StateFacet)d.GetFacet(sel)!).ExpressionTargetField.Should().Be("Alpha");
    }

    // ── the mapper carries it BOTH ways ───────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The arm that did not exist.</b> ⚠ The DTO field and the emitter were both already
    /// there; this is the rail that would have caught "mapped for transitions only".
    /// </summary>
    [Fact]
    public void TheSeedBindingSurvivesSaveAndReopen()
    {
        var hsm = MakeMachine(out var state);
        state.ExpressionTargetField = "Alpha";

        string json = HsmJsonServices.Serialize(HsmAssetMapper.ToDto(hsm));
        var    back = HsmAssetMapper.ToModel(HsmJsonServices.Deserialize(json)!, "", true);

        back.AllStates.Single(s => s.Name == "Patrolling")
            .ExpressionTargetField.Should().Be("Alpha");
    }

    /// <summary>⛔ The no-churn property: an unbound state must not emit the key. 📄 §8a ⑥.</summary>
    [Fact]
    public void AnUnboundState_SerialisesWithoutTheKey()
        => HsmJsonServices.Serialize(HsmAssetMapper.ToDto(MakeMachine(out _)))
               .Should().NotContain("ExpressionTargetField");

    // ── the variable is no longer reported UNUSED ─────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Trap #5 again, for the state case.</b> <c>BlueprintLocalVariableSchemaSource</c>
    /// computes <c>IsUnused: CountNodesReferencingVariable(...) == 0</c>. ⛔ A variable a state SEEDS
    /// FROM would have read as UNUSED and been offered for deletion with a clean conscience — the
    /// same failure <c>E7b</c> fixed for transitions.
    /// </summary>
    [Fact]
    public void AVariableAStateSeedsFrom_IsNotReportedUnused()
    {
        var hsm = MakeMachine(out var state);
        hsm.CountNodesReferencingVariable("Alpha").Should().Be(0);

        state.ExpressionTargetField = "Alpha";
        hsm.CountNodesReferencingVariable("Alpha").Should().Be(1);
    }

    /// <summary>⚠ Comparison is case-insensitive, through the ONE shared predicate
    /// (<c>HsmAsset.IsExpressionTargetOf</c>) the validator also uses.</summary>
    [Fact]
    public void TheCountUsesTheOneSharedCaseInsensitivePredicate()
    {
        var hsm = MakeMachine(out var state);
        state.ExpressionTargetField = "alpha";
        hsm.CountNodesReferencingVariable("Alpha").Should().Be(1);
    }

    // ── ⛔ the difference from the transition field, pinned ────────────────────

    /// <summary>
    /// ⛔⛔ <b>A state's seed binding must NEVER join the cross-region WRITER-conflict rule.</b>
    ///
    /// <para>📐 The rule (<c>HsmValidator.CheckBlackboardRegionConflicts</c>) exists because two
    /// orthogonal regions WRITING one variable race. A state's <c>ExpressionTargetField</c> is a
    /// READ — its occurrence seeds params FROM that variable — and §9.6 explicitly permits concurrent
    /// READERS. ⇒ folding states into that rule would manufacture a hard error on a legal asset.</para>
    ///
    /// <para>⚠ The shared NAME is what makes this worth a rail: the next reader to see
    /// <c>ExpressionTargetField</c> on both types will be tempted to unify the two loops.</para>
    /// </summary>
    [Fact]
    public void TwoStatesInDifferentRegionsSeedingTheSameVariable_IsNotAConflict()
    {
        var root     = new StateNode("__root__");
        var parallel = new StateNode("Both") { IsParallel = true, IsInitial = true, Parent = root };
        var left     = new StateNode("Move")  { IsInitial = true, Parent = parallel, RegionIndex = 0 };
        var right    = new StateNode("Shoot") { IsInitial = true, Parent = parallel, RegionIndex = 1 };
        root.Children.Add(parallel);
        parallel.Children.Add(left);
        parallel.Children.Add(right);
        parallel.RegionNodes.Add(new RegionNode("R0") { RegionIndex = 0, InitialChild = left });
        parallel.RegionNodes.Add(new RegionNode("R1") { RegionIndex = 1, InitialChild = right });

        left.ExpressionTargetField  = "Alpha";
        right.ExpressionTargetField = "Alpha";

        var hsm = new HsmAsset(
            Guid.NewGuid(), "Twin", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(), root,
            new List<StateNode> { parallel, left, right },
            new List<TransitionNode>(), new List<GlobalTransitionNode>(),
            new List<RegionNode>(parallel.RegionNodes), new List<EventDefinition>());

        hsm.SetBlackboardVariables(new[] { new BlackboardVariableEntry("Alpha", typeof(int), null) });

        new Hrot.Hsm.Editor.Validation.HsmValidator().Validate(hsm, hsm)
            .Should().NotContain(
                d => d.Code == Hrot.Hsm.Editor.Validation.HsmDiagnosticCode.CrossRegionBlackboardConflict,
                "a state's ExpressionTargetField is a SEED READ, and concurrent readers are legal");
    }

    // ── CE-401 / ACCEPTANCE RAIL ⑦ — editor model → save → load → generate ───────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE401_R1</c> — <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §9 RAIL ⑦:
    /// a state's <c>ExpressionTargetField</c> set in the EDITOR survives save → load → generate and
    /// reaches <c>HsmParamBindings</c>.</b>
    ///
    /// <para>🔴 <b>Why this rail is needed although BOTH halves were already railed — and it is the
    /// exact shape of the defect <c>CE-387</c> fixed.</b> <c>TheSeedBindingSurvivesSaveAndReopen</c>
    /// above proves the editor model round-trips through the DTO;
    /// <c>HsmStateParamBindingEmissionTests</c> (in <c>Hrot.AiEditor.Generators.Tests</c>) proves a DTO
    /// reaches the emitted <c>HsmParamBindings.Register</c> table. ⛔ <b>Both stop at the DTO, from
    /// opposite sides.</b> ⚠ The emission suite builds its <c>HsmAssetDto</c> BY HAND, so it stays
    /// green no matter what <c>HsmAssetMapper</c> does — which is precisely how the original defect
    /// survived: the DTO field and the emitter had both existed since <c>E3b-0</c>, the mapper carried
    /// the field for TRANSITIONS only, and the production value was therefore always <c>null</c> with
    /// nothing red.</para>
    ///
    /// <para>⚠ <b>The ACT is the PRODUCTION path, not a convenience round-trip.</b> SAVE is
    /// model → <c>ToDto</c> → JSON; GENERATE is JSON → DTO → <c>EmitBridge</c>. ⛔ The generator never
    /// loads the editor model, so re-hydrating one here would exercise a path nothing walks.</para>
    ///
    /// <para>✅ <b>Red-proof</b> — §9 ⑦'s own stated inverse edit: stop mapping
    /// <c>ExpressionTargetField</c> for STATES in <c>HsmAssetMapper</c> ⇒ this rail reddens.</para>
    /// </summary>
    [Fact]
    public void CE401_R1_ASeedAuthoredInTheEditorModel_ReachesTheEmittedBindingTable()
    {
        // ── ARRANGE: the editor model, as the state inspector leaves it ──────────────────────────
        var root = new StateNode("__root__");
        var one  = new StateNode("One") { IsInitial = true, Parent = root, ExpressionTargetField = "Alpha" };
        var two  = new StateNode("Two") { Parent = root, ExpressionTargetField = "Beta" };
        root.Children.Add(one);
        root.Children.Add(two);

        var hsm = new HsmAsset(
            Guid.NewGuid(), "SeedEndToEnd", "", false, "Demo.Machines",
            new HsmDefinitionBlob(), new MachineMetadata(), root,
            new List<StateNode> { one, two },
            new List<TransitionNode>(), new List<GlobalTransitionNode>(),
            new List<RegionNode>(), new List<EventDefinition>());

        hsm.SetBlackboardEditorManaged(true);
        hsm.SetBlackboardVariables(new[]
        {
            // ⭐ Role defaults to Input, which is what makes them PACKED and addressable.
            new BlackboardVariableEntry("Alpha", typeof(int),   null),
            new BlackboardVariableEntry("Beta",  typeof(float), null),
        });

        // ── ACT: SAVE (model → DTO → json), then GENERATE (json → DTO → bridge) ──────────────────
        string json   = HsmJsonServices.Serialize(HsmAssetMapper.ToDto(hsm));
        var    loaded = HsmJsonServices.Deserialize(json)!;
        string bridge = Hrot.AiEditor.Persistence.Emit.HsmBridgeEmitCore.EmitBridge(loaded);

        // ── ASSERT: the table exists, and each state carries ITS OWN offset ──────────────────────
        bridge.Should().Contain("HsmParamBindings.Register(blob",
            "a seed authored in the editor must arrive as a runtime state→offset table");

        string oneLine = LineContaining(bridge, one.StableId.ToString());
        string twoLine = LineContaining(bridge, two.StableId.ToString());

        oneLine.Should().Contain("), 0)", "Alpha is the first packed variable");
        twoLine.Should().Contain("), 4)", "Beta follows a 4-byte int");
        oneLine.Should().NotBe(twoLine, "two states seeding different variables must not share an offset");
    }

    /// <summary>The one line of <paramref name="text"/> containing <paramref name="needle"/>.</summary>
    private static string LineContaining(string text, string needle)
    {
        int at = text.IndexOf(needle, StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, $"the emitted bridge must mention '{needle}'");
        int start = text.LastIndexOf('\n', at) + 1;
        int end   = text.IndexOf('\n', at);
        return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
    }
}
