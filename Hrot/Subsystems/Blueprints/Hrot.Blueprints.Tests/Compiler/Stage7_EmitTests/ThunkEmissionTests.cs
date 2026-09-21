using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests that verify AiPrimitiveEmitter emits correct BTree/HSM thunk structures.
/// </summary>
public sealed class ThunkEmissionTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static string EmitAndGetSource(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);
        var norm    = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(norm, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _) = Stage7_Emit.Run(lowered, CompilerMode.Debug, sink);

        if (sink.HasErrors)
            throw new InvalidOperationException(
                $"Emit errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");
        return src;
    }

    [Fact]
    public void BTreeAction_EmitsBTreeTick_Thunk()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // BTree action thunk method should be present.
        Assert.Contains("BTreeTick", src);
        // BrainBlackboard parameter should use correct namespace.
        Assert.Contains("global::Fdp.Toolkit.Behavior.Components.BrainBlackboard", src);
        // BehaviorTreeState should use Fbt namespace.
        Assert.Contains("global::Fbt.BehaviorTreeState", src);
    }

    [Fact]
    public void BTreeCondition_EmitsBTreeTick_Thunk()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("HasTarget")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g.Entry().Return(NodeStatus.Success))
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("BTreeEvaluate", src);
        Assert.Contains("global::Fdp.Toolkit.Behavior.Components.BrainBlackboard", src);
    }

    [Fact]
    public void HsmAction_EmitsHsmActivity_Thunk()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("PatrolAction")
            .WithHostings(AiPrimitiveHosting.HsmAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // HSM activity thunk.
        Assert.Contains("HsmActivity", src);
        // HsmKernelBridge should use Fdp.Toolkit.Behavior.Systems namespace.
        Assert.Contains("global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge", src);

        // ⭐⭐ O7 / E3 — the working state comes from THIS OCCURRENCE'S slot, keyed by the (region,
        //    state) the kernel stamped. 🔴 It used to be Blackboard1024 at a hard-coded memory + 8,
        //    which is ONE working state per ENTITY: two concurrently-active regions running this
        //    asset wrote the same bytes, silently (BP-297).
        Assert.Contains("global::Fdp.Toolkit.Behavior.HsmOccurrence.KeyFor(instance, AssetId, writer)", src);
        Assert.Contains("HsmOccurrence.ResolveOrAttach<WorkingState>", src);
        Assert.DoesNotContain("global::Fdp.Toolkit.Behavior.Components.Blackboard1024", src);

        // ⭐ CE-297 — params come from the blackboard, NOT from the kernel's instance pointer.
        Assert.Contains("BrainBlackboard", src);
        Assert.DoesNotContain("*(Params*)instance", src);
    }

    [Fact]
    public void HsmGuard_EmitsHsmGuard_Thunk()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("CanPatrol")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.HsmGuard)
            .WithGraph("Main", g => g.Entry().Return(NodeStatus.Success))
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("HsmGuard", src);

        // ⭐⭐ O7 / E3 + CE-297 — same two corrections as the action thunk; one shared body emits both.
        Assert.Contains("HsmOccurrence.ResolveOrAttach<WorkingState>", src);
        Assert.DoesNotContain("global::Fdp.Toolkit.Behavior.Components.Blackboard1024", src);
        Assert.DoesNotContain("*(Params*)instance", src);
    }

    [Fact]
    public void MultipleHostings_EmitsAllThunks()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MultiHosted")
            .WithHostings(AiPrimitiveHosting.BTreeAction, AiPrimitiveHosting.HsmAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("BTreeTick", src);
        Assert.Contains("HsmActivity", src);
    }

    /// <summary>
    /// 🔴🔴🔴 <b><c>CE-298</c> PINNED AT THE SOURCE — the thunk's params projection takes NO
    /// occurrence argument.</b>
    ///
    /// <para>🔒 <b>User, <c>2026-09-21</c>:</b> <i>"two actions running from hsm regions, each having
    /// its params, they can not share same single place."</i> ⭐ Correct, and
    /// <c>DESIGN_Parameter_Model.md</c> §4.1 already ruled the HSM params cell a <b>"live race"</b>.
    /// <c>O7b</c> moved WORKING STATE into the occurrence slot and left params where they were.</para>
    ///
    /// <para>⛔⛔ <b>THIS RAIL ASSERTS THE DEFECT, DELIBERATELY.</b> It is green because the emitted
    /// projection is <c>BehaviorParameters[0]</c> at a baked <c>0</c> — the same address for every
    /// occurrence. ⭐⭐ <b>When <c>CE-298</c> lands this MUST go red</b>, and whoever lands it comes
    /// here and rewrites it to assert the per-occurrence params region. ⛔ Not a <c>Skip</c>: a
    /// skipped rail tells nobody anything, and this one has to be impossible to ship past.</para>
    ///
    /// <para>⚠ <b>Why a rail on emitted TEXT and not on behaviour:</b> the collision is an ADDRESS
    /// property — "the params address does not depend on the occurrence" — and the address is baked
    /// by the emitter. A runtime rail would compare a constant with itself.</para>
    /// </summary>
    [Fact]
    public void HsmThunk_StillProjectsParamsPerEntity_CE298()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("ParamsPerEntity")
            .WithHostings(AiPrimitiveHosting.HsmAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // The working state IS occurrence-keyed (O7b).
        Assert.Contains("HsmOccurrence.ResolveOrAttach<WorkingState>", src);

        // 🔴 …and the params are NOT: one address, every occurrence. THE DEFECT.
        Assert.Contains("ref bb.BehaviorParameters[0], (nint)0", src);
        Assert.DoesNotContain("HsmOccurrence.ResolveOrAttach<Params>", src);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>O7d</c> — the STANDALONE BTree thunks are on the occurrence store.</b>
    ///
    /// <para>⚠ <b>This rail was a DEFECT PIN and has now FLIPPED</b>, which is exactly what a pin is
    /// for: it asserted <c>Blackboard1024 + 8</c> while that was true, and reddened the moment
    /// <c>O7d</c> landed so the change could not ship silently.</para>
    ///
    /// <para>⛔ <b>ASSET-scoped, and that is forced, not chosen.</b> 📐 <c>Interpreter.cs:655</c> hands
    /// an action delegate only <c>node.PayloadIndex</c> — no node identity — so one shared thunk cannot
    /// key itself per-occurrence. ⭐ Per-node IS the BRIDGE's job (it bakes a key per adapter); the
    /// standalone thunk is the degenerate single-occurrence case, which is what the <c>@0</c> in its
    /// registration key has always meant.</para>
    ///
    /// <para>🔴 <b>And it fixed more than a collision:</b> <c>Blackboard1024</c> is on ZERO production
    /// entities (both <c>AddComponent</c> sites gated on <c>HeavyDtoType</c>, which nothing sets) and
    /// <c>GetComponentRW</c> throws on a missing component ⇒ this thunk would have <b>thrown</b> the
    /// moment it was bound.</para>
    /// </summary>
    [Fact]
    public void StandaloneBTreeThunks_UseTheOccurrenceStore_O7d()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("StandaloneBTree")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // ⭐⭐ THE RAIL. Asset-scoped occurrence storage, through the SAME shared body the HSM path uses.
        Assert.Contains("OccurrenceSlots.StandaloneStateKeyFor(AssetId)", src);
        Assert.Contains("OccurrenceWorkingState.ResolveOrAttach<WorkingState>", src);

        // 🔴 …and the legacy per-entity blackboard is gone from this thunk.
        Assert.DoesNotContain("global::Fdp.Toolkit.Behavior.Components.Blackboard1024", src);
        Assert.DoesNotContain("memory + 8", src);
    }
}
