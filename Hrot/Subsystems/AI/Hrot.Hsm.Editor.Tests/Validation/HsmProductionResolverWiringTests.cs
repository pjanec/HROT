using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Validation;

/// <summary>
/// ⭐⭐⭐ <b><c>E4</c> — the stateful-subtree resolver reaches the PRODUCTION entry points.</b>
///
/// <para>
/// 🔴🔴 <b>What was wrong, quoted from <c>DEBT-AIB-028</c>:</b> <i>"(b) <c>_isStatefulSubtree</c>
/// defaults to <c>_ =&gt; false</c> and production never supplies a real resolver; (c) the production
/// <c>HsmAssetValidator</c> entry point isn't threaded to pass the resolver."</i> ⇒ rules 8/8b were
/// <b>present, tested and INERT</b> — they could not fire against a real asset however it was authored.
/// ⛔ Trap #5 in its purest form.
/// </para>
///
/// <para>
/// ⭐ <b>These tests drive the PRODUCTION constructor</b> — <see cref="HsmAssetValidator"/>, the
/// <c>IAssetValidator</c> the Diagnostics window is given — not the inner <c>HsmValidator</c> that the
/// existing S2-4 tests already cover. That is the whole difference between "the rule works" and "the
/// rule is reachable".
/// </para>
///
/// <para>
/// ⚠⚠ <b>Rules 8/8b may still not fire on assets loaded from disk, and that is EXPECTED.</b>
/// <c>StateNode.SubtreeAssetId</c> is not persisted (<c>DEBT-AIB-028</c>(a)) so nothing sets the field
/// on a round trip — that is <c>E5</c>'s prerequisite, explicitly out of scope here. ⭐ This item makes
/// the wiring honest; <c>E5</c> makes it reachable. ⇒ the fixtures below set the field directly.
/// </para>
/// </summary>
public sealed class HsmProductionResolverWiringTests
{
    private static (HsmAsset Asset, StateNode Parallel, StateNode C0, StateNode C1) MakeParallelAsset()
    {
        var root     = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        var rn0 = new RegionNode("R0") { RegionIndex = 0 };
        var rn1 = new RegionNode("R1") { RegionIndex = 1 };
        parallel.RegionNodes.Add(rn0);
        parallel.RegionNodes.Add(rn1);

        var c0 = new StateNode("C0") { IsInitial = true, RegionIndex = 0, Parent = parallel };
        var c1 = new StateNode("C1") { RegionIndex = 1, Parent = parallel };
        parallel.Children.Add(c0);
        parallel.Children.Add(c1);

        var asset = new HsmAsset(
            Guid.NewGuid(), "TestAsset", "", false, "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            root,
            new List<StateNode> { parallel, c0, c1 },
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode> { rn0, rn1 },
            new List<EventDefinition>());

        return (asset, parallel, c0, c1);
    }

    /// <summary>
    /// 🔴 <b>The rule fires THROUGH the production adapter</b> once it is handed the resolver.
    /// </summary>
    [Fact]
    public void TheProductionValidator_FiresRule8_WhenGivenAResolver()
    {
        var (asset, _, c0, c1) = MakeParallelAsset();
        var subtreeId = Guid.NewGuid();
        c0.SubtreeAssetId = subtreeId;
        c1.SubtreeAssetId = subtreeId;

        var validator = new HsmAssetValidator(
            schema: null, isStatefulSubtree: id => id == subtreeId);

        Assert.Contains(validator.Validate(asset),
            d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree.ToString());
    }

    /// <summary>
    /// ⛔ <b>And is silent without one</b> — the shipped default. ⭐ This is the state Batch 68 found
    /// production in, pinned so the difference between the two is visible rather than argued.
    /// </summary>
    [Fact]
    public void TheProductionValidator_IsSilent_WithTheDefaultResolver()
    {
        var (asset, _, c0, c1) = MakeParallelAsset();
        var subtreeId = Guid.NewGuid();
        c0.SubtreeAssetId = subtreeId;
        c1.SubtreeAssetId = subtreeId;

        Assert.DoesNotContain(new HsmAssetValidator().Validate(asset),
            d => d.Code == HsmDiagnosticCode.ConcurrentStatefulSubtree.ToString());
    }

    /// <summary>
    /// ⭐ <b>The node-badge entry point takes the SAME resolver.</b> ⛔ Two surfaces validate the same
    /// asset — <see cref="HsmGraphModel"/> drives the badges, <see cref="HsmAssetValidator"/> the
    /// Diagnostics window — and a resolver on only one of them would make a state light up in one and
    /// not the other.
    /// </summary>
    [Fact]
    public void TheGraphModel_AcceptsTheSameResolver()
    {
        var (asset, parallel, c0, c1) = MakeParallelAsset();
        var subtreeId = Guid.NewGuid();
        c0.SubtreeAssetId = subtreeId;
        c1.SubtreeAssetId = subtreeId;

        var model = new HsmGraphModel(asset, isStatefulSubtree: id => id == subtreeId);
        var node  = model.Nodes.FirstOrDefault(n => n.Id.Value == parallel.StableId);

        Assert.NotNull(node);
        Assert.Equal(NodeEditor.Primitives.NodeState.Error, node!.State);
    }

    // ── the two HasAnyStatefulNode predicates ───────────────────────────────────

    /// <summary>
    /// ⭐ <b>The HSM predicate is the SAME definition <c>E1</c>'s emitter uses:</b> a
    /// <c>Role = State</c> variable scoped <c>Behavior</c> or <c>Entity</c> — exactly the set
    /// <c>HsmBridgeEmitCore</c> emits a <c>StatefulSlotInfo</c> for. ⛔ A different notion of "stateful"
    /// here would let the validator and the emitter disagree about which assets own a partition slot.
    /// </summary>
    [Theory]
    [InlineData(BlackboardVariableRole.State, WorkingStateScope.Behavior, true)]
    [InlineData(BlackboardVariableRole.State, WorkingStateScope.Entity,   true)]
    // ⚠ Node scope is excluded for the same reason emission skips it: its key needs a node id.
    [InlineData(BlackboardVariableRole.State, WorkingStateScope.Node,     false)]
    // ⛔ Input is the PARAMETER role, not working state.
    [InlineData(BlackboardVariableRole.Input, WorkingStateScope.Behavior, false)]
    public void HsmHasAnyStatefulNode_MatchesTheEmittersOwnFilter(
        BlackboardVariableRole role, WorkingStateScope scope, bool expected)
    {
        var (asset, _, _, _) = MakeParallelAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("v", typeof(int), null, false, null, role, scope),
        });

        Assert.Equal(expected, asset.HasAnyStatefulNode());
    }

    /// <summary>⭐ No variables ⇒ not stateful, and the predicate does not throw on an empty asset.</summary>
    [Fact]
    public void AnHsmWithNoVariables_IsNotStateful()
        => Assert.False(MakeParallelAsset().Asset.HasAnyStatefulNode());

    // ── E4 completion (Batch 69): sharedScopeKeys ───────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Rule 8b fires through the PRODUCTION adapter once <c>sharedScopeKeys</c> is supplied.</b>
    /// ⚠ Batch 68 threaded the parameter and flagged that it was still defaulted to
    /// <c>_ =&gt; Array.Empty&lt;int&gt;()</c>, so the rule could not fire; this is the other half.
    /// </summary>
    [Fact]
    public void TheProductionValidator_FiresRule8b_WhenGivenSharedScopeKeys()
    {
        var (asset, _, c0, c1) = MakeParallelAsset();
        var subtreeA = Guid.NewGuid();
        var subtreeB = Guid.NewGuid();
        c0.SubtreeAssetId = subtreeA;
        c1.SubtreeAssetId = subtreeB;      // ⛔ DIFFERENT subtrees -- so rule 8 does NOT apply...

        // ...but both resolve to the SAME shared scope key, which is what 8b is for.
        var shared = new[] { 0x1234 };
        var validator = new HsmAssetValidator(
            schema: null,
            isStatefulSubtree: _ => true,
            sharedScopeKeys:   _ => shared);

        Assert.Contains(validator.Validate(asset),
            d => d.Code == HsmDiagnosticCode.ConcurrentSharedScopeKey.ToString());
    }

    /// <summary>⛔ And is silent on the shipped default — the state Batch 68 left it in.</summary>
    [Fact]
    public void TheProductionValidator_IsSilentOnRule8b_WithTheDefaultResolver()
    {
        var (asset, _, c0, c1) = MakeParallelAsset();
        c0.SubtreeAssetId = Guid.NewGuid();
        c1.SubtreeAssetId = Guid.NewGuid();

        Assert.DoesNotContain(new HsmAssetValidator(schema: null, isStatefulSubtree: _ => true).Validate(asset),
            d => d.Code == HsmDiagnosticCode.ConcurrentSharedScopeKey.ToString());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The shared-scope-key set is ONE definition, and this pins it to the emitter's.</b>
    /// <c>HsmAsset.GetSharedScopeKeys()</c> calls <c>BTreeBridgeEmitCore.ComputeStatefulSlotKey</c> —
    /// the same algorithm <c>E1</c>'s emission uses — so the validator's idea of "these two regions
    /// touch one shared slot" cannot drift from the allocator's. ⚠ The expected value is COMPUTED, not
    /// pasted: a literal would still pass if both sides moved together.
    /// </summary>
    [Fact]
    public void SharedScopeKeys_UseTheEmittersOwnKeyAlgorithm()
    {
        var (asset, _, _, _) = MakeParallelAsset();
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("Cursor", typeof(int), null, false, null,
                BlackboardVariableRole.State, WorkingStateScope.Behavior),
        });

        int expected = Hrot.AiEditor.Persistence.Emit.BTreeBridgeEmitCore.ComputeStatefulSlotKey(
            asset.AssetId, WorkingStateScope.Behavior, Guid.Empty, "Cursor");

        Assert.Equal(new[] { expected }, asset.GetSharedScopeKeys());
        // ⭐ And HasAnyStatefulNode is now literally "is that set non-empty" -- one filter, not two.
        Assert.True(asset.HasAnyStatefulNode());
    }
}
