using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Lowering;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Core.Compiler.Transform;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐ <b>The node-level and op-level "can this suspend?" predicates must agree.</b>
///
/// <para>
/// <c>MacroLatency.IsLatent</c> answers the question for an authored <see cref="Node"/> — it is what
/// <c>BP1661</c> (a latent macro called from a Function graph) and collapse legality (Q26-F) consult.
/// <c>LocalStorage.CanSuspend</c> answers it for a scheduled <see cref="IrGraph"/> — it is what the
/// wait lowering and Q27-A3's storage choice consult. They are two views of one fact, and its own doc
/// comment says so: <i>"Do not write a second latent-detection rule."</i>
/// </para>
///
/// <para>
/// ⚠⚠ <b>There were two, and they disagreed.</b> <c>IsLatent</c> listed three node kinds;
/// <c>ChannelCommandNode</c> with <c>ActionFqn</c> set was missing, even though
/// <c>Stage5.ScheduleInlineActionNode</c> turns exactly that shape into <c>IrOp_InlineActionCall</c>
/// and the wait lowering gives it a full suspend/resume block split. ⇒ a macro whose only latent node
/// was an inline action read as <b>synchronous</b> to <c>BP1661</c> and to collapse.
/// </para>
///
/// <para>
/// ⭐ <b>This test is the agreement, not a restatement of either list.</b> It schedules each node
/// shape and compares what the IR actually did against what <c>IsLatent</c> claims — so a future node
/// kind that suspends cannot be added to one side alone.
/// </para>
/// </summary>
public sealed class MacroLatencyCoverageTests
{
    private const string FakeActionFqn = "Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp.Call";
    private const string FakeParamsFqn = "Hrot.AI.Behaviors.Generated.MoveToTarget_AABBCCDD_Bp+Params";

    private static CompileOptions DefaultOptions() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static IrAsset RunSchedule(BlueprintAsset asset)
    {
        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, DefaultOptions());
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        return Stage5_Schedule.Run(typed, ctx);
    }

    /// <summary>
    /// ⭐ <b>The claim the whole §1.2 ruling rests on, tested rather than reasoned from doc comments.</b>
    /// An <c>ActionInvocation</c> really does become a suspending op — so a predicate that omits it is
    /// wrong, not merely conservative.
    /// </summary>
    [Fact]
    public void AnActionInvocation_ReallyDoesSuspend()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("InlineActionOnly")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ActionInvocation(FakeActionFqn, FakeParamsFqn)
                .Return())
            .Build();

        var ir = RunSchedule(asset);
        Assert.All(ir.Graphs, g => Assert.True(LocalStorage.CanSuspend(g),
            "A ChannelCommandNode with ActionFqn set schedules IrOp_InlineActionCall, which the wait "
            + "lowering splits into a suspend/resume state machine."));
    }

    /// <summary>
    /// ⭐⭐ <b>The agreement.</b> For every shape, what <c>MacroLatency.IsLatent</c> says about the
    /// authored node must match what the scheduled IR actually contains.
    ///
    /// <para>
    /// ⚠ The negative case matters as much as the positives: a <c>ChannelCommandNode</c> WITHOUT
    /// <c>ActionFqn</c> is a fire-and-forget channel write and does <b>not</b> suspend, so a predicate
    /// that simply matched the node type would be wrong in the other direction — and would make
    /// <c>BP1661</c> refuse macros that are perfectly legal.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("delay",   true)]
    [InlineData("channel", true)]
    [InlineData("action",  true)]
    [InlineData("command", false)]
    [InlineData("none",    false)]
    public void IsLatent_AgreesWithWhatTheIrActuallyDid(string shape, bool expected)
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("LatencyShape_" + shape)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g =>
            {
                g.Entry();
                switch (shape)
                {
                    case "delay":   g.Delay(0.4f); break;
                    case "channel": g.WaitForChannel("Hrot.AI.Behaviors.BpChannelDemo"); break;
                    case "action":  g.ActionInvocation(FakeActionFqn, FakeParamsFqn); break;
                    case "command": g.ChannelCommand("Hrot.AI.Behaviors.BpChannelDemo", "Go"); break;
                    case "none":    break;
                    default: throw new ArgumentOutOfRangeException(nameof(shape));
                }
                g.Return();
            })
            .Build();

        var nodes = asset.Graphs.SelectMany(g => g.Nodes).ToList();
        bool nodeLevel = nodes.Any(MacroLatency.IsLatent);
        bool irLevel   = RunSchedule(asset).Graphs.Any(LocalStorage.CanSuspend);

        Assert.Equal(expected, irLevel);       // what the compiler really does
        Assert.Equal(irLevel, nodeLevel);      // ⭐ and what the shared predicate claims about it
    }
}
