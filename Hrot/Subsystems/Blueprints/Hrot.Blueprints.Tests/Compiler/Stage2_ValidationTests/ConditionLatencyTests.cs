using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b>The latency rail's <c>Condition</c> row — <c>BP1101</c>, WIDENED, not re-invented.</b>
///
/// <para>
/// 🔴🔴 <b>The defect.</b> <c>BTreeEvaluate</c> emits
/// <c>return TickCore(…) == NodeStatus.Success;</c>, so <b><c>Running</c> becomes <c>false</c></b>.
/// A latent condition reads <i>false</i> while it waits — <b>indistinguishable from "the condition
/// does not hold"</b> — then flips true later with <c>__phase</c> mid-sequence. ⛔ No throw, no
/// warning: the tree just takes the wrong branch.
/// </para>
///
/// <para>
/// ⛔⛔ <b>The carried task said this row was missing. Measured, it was not.</b> <c>BP1101</c> in
/// <c>V_AiPrimitiveIntent</c> has forbidden latent nodes in a Condition primitive all along — with its
/// <b>own</b> inline list of latent kinds and <b>no</b> walk through <see cref="MacroCallNode"/>.
/// ⇒ ⭐ <b>the right answer was to route into the existing rule, not to allocate a second code</b>;
/// <c>MacroLatency</c>'s own doc says <i>"do not write a second latent-detection rule"</i>, and the
/// inline list was the last copy it had not yet absorbed.
/// </para>
///
/// <para>
/// ⭐⭐ <b>What widening actually bought — ONE case, measured, not the two first assumed.</b> The real
/// gap is <b>a <c>ChannelCommandNode</c> carrying an <c>ActionFqn</c></b>, which <c>WaitLowering</c>
/// suspends exactly like a <c>Delay</c>: a fourth SHAPE of an already-listed type, which is why a
/// type-match missed it. ⛔ <b>Latency behind a macro call was NOT a gap</b> — the scan already covers
/// macro bodies — and a call-following arm written for it was removed for producing a duplicate
/// diagnostic.
/// </para>
///
/// <para>
/// 📌 <b><c>TestAssets/Invalid/ConditionWithDelay.bp.json</c> has sat unreferenced since the fixture
/// set was written.</b> ⭐ It is referenced here at last, so the rule is proved against the artefact
/// the design left behind rather than only against builder-made assets.
/// </para>
///
/// <para>
/// ⚠ <b>Only the <c>Condition</c> row ships.</b> The rule is <i>"latency is legal iff the hosting can
/// RE-ENTER"</i>; <c>BTreeCondition</c>/<c>HsmGuard</c> never can, which makes this row fully
/// specified today. ⛔ The <c>Action</c> rows (HSM <c>Entry</c>/<c>Exit</c>/<c>Timer</c> cannot
/// re-enter either) stay unimplemented on purpose — they are speculative until <c>E5</c> defines HSM
/// activity hosting, and guessing them would refuse assets the design has not ruled on.
/// </para>
/// </summary>
public sealed class ConditionLatencyTests
{
    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>())));
        return sink.All;
    }

    /// <summary>🔴 The direct case: a <c>Delay</c> in the condition's own body.</summary>
    [Fact]
    public void ConditionIntent_WithALatentNode_EmitsBP1101()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("LatentCondition")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g.Entry().Delay(0.5f).Return())
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1101);
    }

    /// <summary>
    /// ⭐ <b>Latency behind a macro call is caught — and it needs no call-following arm.</b> The
    /// validator scans every graph in the asset, macro bodies included, so the latent node is reported
    /// against the macro that holds it before expansion could splice it anywhere.
    ///
    /// <para>
    /// 📌 <b>Recorded because a call-following arm WAS written for this and then removed:</b> it fired
    /// in addition to the direct scan, so one defect produced two diagnostics. ⇒ this test pins that
    /// the case is covered, not that a second mechanism covers it.
    /// </para>
    /// </summary>
    [Fact]
    public void ConditionIntent_WithLatencyBehindAMacroCall_EmitsBP1101()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("LatentViaMacro")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Wait", GraphKind.Macro, g => g.Entry().Delay(0.25f).Return())
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var macro = asset.Graphs.Single(g => g.Kind == GraphKind.Macro);
        var main  = asset.Graphs.Single(g => g.Name == "Main");
        main.Nodes.Add(new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = macro.Id.ToString() });

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1101);
    }

    /// <summary>
    /// ⭐⭐ <b>The kind the old inline list did not name.</b> A <c>ChannelCommandNode</c> carrying an
    /// <c>ActionFqn</c> is an inline action, and <c>WaitLowering</c> gives it the same suspend/resume
    /// block split as a <c>Delay</c>. ⛔ The pre-widening rule matched on three node TYPES and this is
    /// a fourth shape of an already-listed type — so a latent condition of exactly this form compiled
    /// clean.
    /// </summary>
    [Fact]
    public void ConditionIntent_WithAnInlineActionInvocation_EmitsBP1101()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("InlineActionCondition")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation("Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp.Call")
                .Return())
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1101);
    }

    /// <summary>
    /// ⚠ <b>A plain channel write is NOT latent</b> — no <c>ActionFqn</c>, fire-and-forget, the same
    /// discrimination Stage 5 makes. ⛔ Without this the test above would be satisfied by a rule that
    /// simply banned every <c>ChannelCommandNode</c>, which would refuse working conditions.
    /// </summary>
    [Fact]
    public void ConditionIntent_WithAPlainChannelWrite_IsClean()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("ChannelWriteCondition")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g.Entry().ChannelCommand("Locomotion", "Stop").Return())
            .Build();

        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP1101);
    }

    /// <summary>
    /// ⚠ <b>Even an UNCALLED latent macro is refused</b>, because the scan is per-graph and does not
    /// ask who calls whom. 📌 <b>Asserted as the SHIPPED behaviour, not endorsed:</b> the rule predates
    /// this batch and dead weight in a Condition asset cannot actually misbehave, so narrowing it is a
    /// design call rather than a fix to smuggle into a widening. ⭐ What the test buys is that the
    /// breadth is now written down and falsifiable.
    /// </summary>
    [Fact]
    public void ConditionIntent_WithAnUncalledLatentMacro_IsAlsoRefused()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("UncalledLatentMacro")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Unused", GraphKind.Macro, g => g.Entry().Delay(0.25f).Return())
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1101);
    }

    /// <summary>
    /// ⭐ <b>The rule is about INTENT, not about latency.</b> An Action primitive is hosted as a
    /// <c>BTreeAction</c>, which re-enters on the next tick and observes <c>Running</c> properly. ⛔ If
    /// this ever reddens, the rail has widened into refusing the very capability latent primitives
    /// exist for.
    /// </summary>
    [Fact]
    public void ActionIntent_WithTheSameLatentNode_IsClean()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("LatentAction")
            .WithIntent(AiPrimitiveIntent.Action)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Delay(0.5f).Return())
            .Build();

        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP1101);
    }

    /// <summary>⭐ A synchronous condition — the ordinary shape — stays clean.</summary>
    [Fact]
    public void ConditionIntent_WithoutLatency_IsClean()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("PlainCondition")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP1101);
    }

    /// <summary>
    /// 📌 <b>The abandoned fixture, finally load-bearing.</b> <c>ConditionWithDelay.bp.json</c> is a
    /// hand-authored asset in the <c>Invalid/</c> set that no test has ever referenced.
    /// </summary>
    [Fact]
    public void TheAbandonedInvalidFixture_NowFailsTheRuleItWasWrittenFor()
    {
        var asset = TestData.LoadAsset("Invalid/ConditionWithDelay");

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1101);
    }
}
