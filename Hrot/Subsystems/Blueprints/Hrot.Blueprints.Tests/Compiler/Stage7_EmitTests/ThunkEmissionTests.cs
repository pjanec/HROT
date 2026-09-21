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
        // ⭐⭐⭐ E3a — and the PARAMS ride the same slot: one key, one lookup, one lifetime (§28).
        Assert.Contains("HsmOccurrence.ResolveOrAttach<Params, WorkingState>", src);
        Assert.DoesNotContain("global::Fdp.Toolkit.Behavior.Components.Blackboard1024", src);

        // ⭐ CE-297 — the params SEED comes from the blackboard, never from the kernel's instance
        //   pointer. ⚠ E3a demoted it from the live home to the seed; HsmThunk_TakesParamsFromThe-
        //   OccurrenceSlot_E3a pins that it sits inside the freshly-attached arm.
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

        // ⭐⭐ O7 / E3 / E3a + CE-297 — the same corrections as the action thunk; one shared body.
        Assert.Contains("HsmOccurrence.ResolveOrAttach<Params, WorkingState>", src);
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
    /// ⭐⭐⭐ <b><c>E3a</c> / <c>CE-298</c> — the thunk's params come from the OCCURRENCE SLOT, and the
    /// blackboard is only the SEED.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.
    ///
    /// <para>⚠ <b>This rail was a DEFECT PIN and has now FLIPPED.</b> It asserted
    /// <c>ref bb.BehaviorParameters[0], (nint)0</c> as the LIVE projection while that was true, and
    /// reddened the moment <c>E3a</c> landed — which is the whole point of a pin.</para>
    ///
    /// <para>🔒 <b>User, <c>2026-09-21</c>:</b> <i>"the simplest case like two actions running in two
    /// hsm regions would overwrite the params. Forget the fact it is not in use now. it will be."</i>
    /// ⛔ My own first answer — that it <i>"buys nothing measurable today"</i> because 0 of 27 goldens
    /// mutate <c>Params</c> — reasoned from the corpus to a CAPABILITY question and was wrong.</para>
    ///
    /// <para>⚠ <b>Why a rail on emitted TEXT:</b> the property is an ADDRESS one — <i>"the params
    /// address depends on the occurrence"</i> — and the address is baked by the emitter. ⭐ The
    /// behavioural half is <c>O7_R24</c>/<c>O7_R25</c> in <c>Fdp.Toolkits.Tests</c>, which write
    /// through two occurrences and prove they do not move each other.</para>
    /// </summary>
    [Fact]
    public void HsmThunk_TakesParamsFromTheOccurrenceSlot_E3a()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("ParamsPerEntity")
            .WithHostings(AiPrimitiveHosting.HsmAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var src = EmitAndGetSource(asset);

        // ⭐⭐ ONE slot carries BOTH — one key, one lookup, one lifetime.
        Assert.Contains("HsmOccurrence.ResolveOrAttach<Params, WorkingState>", src);
        Assert.DoesNotContain("HsmOccurrence.ResolveOrAttach<WorkingState>", src);

        // ⭐ The live read is from the slot…
        Assert.Contains("ref var p = ref *__params;", src);

        // …and the blackboard survives ONLY as the seed, inside the freshly-attached arm.
        int seed = src.IndexOf("*__params = ", StringComparison.Ordinal);
        int fresh = src.IndexOf("if (freshlyAttached)", StringComparison.Ordinal);
        Assert.True(fresh >= 0 && seed > fresh,
            "the blackboard copy must sit INSIDE the freshlyAttached arm — a seed, not a live read");

        // ⭐⭐⭐ E3b-0 (§28.6): and the seed offset is the STATE'S OWN BINDING, not a literal 0 —
        //    that is what lets two parallel regions seed from different variables.
        Assert.Contains("int __seedOffset = global::Fdp.Toolkit.Behavior.HsmOccurrence.SeedParamsOffset(instance, writer);", src);
        Assert.Contains("ref bb.BehaviorParameters[0], (nint)__seedOffset", src);

        // ⛔ …and the HSM thunk no longer bakes a literal 0 anywhere.
        Assert.Equal(0, CountOccurrences(src, "ref bb.BehaviorParameters[0], (nint)0"));
    }

    /// <summary>
    /// ⭐⭐ <b><c>E3b-0</c> — the STANDALONE BTree thunk keeps the literal <c>0</c>, and that is CORRECT
    /// BY CONSTRUCTION.</b>
    ///
    /// <para>⛔ Standalone hosting is the single-occurrence case — the <c>@0</c> in its own registration
    /// key has always said so, and <c>O7d</c>'s slot key is ASSET-scoped for the same reason. ⭐ There is
    /// no site to bind, so asking a binding table would be a lookup whose answer is always 0.</para>
    ///
    /// <para>⚠ The rail exists because the obvious wrong symmetry is to route BOTH paths through the
    /// binding — which would add a per-dispatch lookup to the path that provably cannot need one.</para>
    /// </summary>
    [Fact]
    public void StandaloneThunk_KeepsTheLiteralZeroSeed_E3b0()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("StandaloneSeed")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var src = EmitAndGetSource(asset);

        Assert.Contains("ref bb.BehaviorParameters[0], (nint)0", src);
        Assert.DoesNotContain("SeedParamsOffset", src);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
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
        Assert.Contains("OccurrenceWorkingState.ResolveOrAttach<Params, WorkingState>", src);

        // 🔴 …and the legacy per-entity blackboard is gone from this thunk.
        Assert.DoesNotContain("global::Fdp.Toolkit.Behavior.Components.Blackboard1024", src);
        Assert.DoesNotContain("memory + 8", src);
    }
}
