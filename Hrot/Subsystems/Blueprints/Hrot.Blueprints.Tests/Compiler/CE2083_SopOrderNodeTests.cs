using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐ <c>CE-2083</c> — the blueprint SOP order node ("Do when idle" / "React"). 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.10
/// (D4–D6). ⭐ The load-bearing rail RUNS the generated code through the real ingress and brain tick: the order reaches the
/// bus as the gate would see it, and <c>Accepted</c> says whether the gate let it through.
/// </summary>
public class CE2083_SopOrderNodeTests
{
    private static Pin DataPin(string name, string direction, string typeId) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, TypeRef = new BlueprintTypeRef { TypeId = typeId } };

    /// <summary>A behaviour whose Tick issues one SOP order and stores its <c>Accepted</c> in the variable <c>Accepted</c>; a
    /// React order's <c>Params</c> is an int literal 7 (any struct type works the same: <c>SopActions.React&lt;T&gt;</c>).</summary>
    private static BlueprintAsset Sender(string name, SopOrderKind kind, string behaviour)
    {
        bool react = kind == SopOrderKind.React;
        var asset = BlueprintAssetBuilder.Behavior(name)
            .WithVariable("Accepted", typeof(bool))
            .WithGraph("Tick", g => g.Entry()
                .SopOrder(kind, behaviour, SopOrderUrgency.Hit, react ? "System.Int32" : null)
                .SetVariable("Accepted", "")
                .Return(NodeStatus.Running))   // keep running: a finished run clears its block before it is read
            .Build();
        var tick = asset.Graphs.Single(g => g.Name == "Tick");
        var order = tick.Nodes.OfType<SopOrderNode>().Single();
        var set = tick.Nodes.OfType<SetVariableNode>().Single();
        set.VariableId = asset.Variables.Single().Id.ToString();
        var value = DataPin("Value", "In", "System.Boolean");
        set.Pins.Add(value);
        tick.Links.Add(new Link { FromNodeId = order.Id, FromPinId = order.Pins.Single(p => p.Name == SopOrderNode.AcceptedPin).Id,
                                  ToNodeId = set.Id, ToPinId = value.Id });
        if (react)
        {
            var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "7" };
            var litOut = DataPin("Value", "Out", "System.Int32");
            lit.Pins.Add(litOut);
            tick.Nodes.Add(lit);
            tick.Links.Add(new Link { FromNodeId = lit.Id, FromPinId = litOut.Id,
                                      ToNodeId = order.Id, ToPinId = order.Pins.Single(p => p.Name == SopOrderNode.ParamsPin).Id });
        }
        return asset;
    }

    internal static BlueprintAsset CoverageSender() => Sender("Ce2083SopCoverage", SopOrderKind.React, "Ce2083Cover");

    /// <summary>Runs <paramref name="asset"/> as the unit's task (an order, origin Superior) for one tick; returns what it
    /// published and its <c>Accepted</c>.</summary>
    private static unsafe (IReadOnlyList<AssignBehaviorEvent> Sent, bool Accepted, Fdp.Core.Entity Unit) RunOnce(BlueprintAsset asset)
    {
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.CompileAndLoad(asset, GoldenCorpus.Options());
        Assert.True(fixture.BehaviorRegistry.TryGetId(asset.Name, out int id));
        Assert.True(fixture.BehaviorRegistry.TryGetDefinition(id, out var def));

        var world = fixture.World;
        var e = fixture.CreateEntity();
        world.AddComponent(e, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity = e, BehaviorName = asset.Name, JsonParams = string.Empty, Origin = BehaviorOrigin.Superior,
        });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(fixture.BehaviorRegistry).Execute(world, 0.016f);
        world.Bus.SwapBuffers();
        new BrainTickSystem(fixture.BehaviorRegistry).Execute(world, 0.016f);

        Assert.True(Fdp.Toolkit.Behavior.RootParamsAccess.TryGetRootBytes(world, e, out byte* root));
        bool accepted = *(bool*)(root + BlueprintBehaviourTests.VarOffset(def!, "Accepted"));
        world.Bus.SwapBuffers();
        return (world.Bus.ReadManaged<AssignBehaviorEvent>().ToList(), accepted, e);
    }

    /// <summary>
    /// 🔴 A React order under a running order is ADMITTED (a reaction pauses the task, R-199): the node publishes ONE
    /// assignment — the named behaviour, origin Reaction, its urgency, the Params pin serialised with the ONE params options
    /// — and <c>Accepted</c> is true.
    /// </summary>
    [Fact]
    public void CE2083_AReactOrder_PublishesTheReaction_WithItsParams_AndIsAccepted()
    {
        var (sent, accepted, unit) = RunOnce(Sender("Ce2083React", SopOrderKind.React, "Ce2083Cover"));

        var order = Assert.Single(sent);
        Assert.Equal(unit, order.Entity);
        Assert.Equal("Ce2083Cover", order.BehaviorName);
        Assert.Equal(BehaviorOrigin.Reaction, order.Origin);
        Assert.Equal(ReactionUrgency.Hit, order.Urgency);
        Assert.Equal("7", order.JsonParams);   // the wired Params value, serialised with BehaviorParams.JsonOptions
        Assert.True(accepted);
    }

    /// <summary>
    /// 🔴 "Do when idle" under a running ORDER is refused by the gate's pre-check: nothing is published (a refused Sop-origin
    /// assignment would wake the SOP, R-195) and <c>Accepted</c> is false — a Branch on it tries the next row.
    /// </summary>
    [Fact]
    public void CE2083_ADoWhenIdleOrder_UnderAnOrder_PublishesNothing_AndIsNotAccepted()
    {
        var (sent, accepted, _) = RunOnce(Sender("Ce2083Idle", SopOrderKind.DoWhenIdle, "Ce2083Patrol"));

        Assert.Empty(sent);
        Assert.False(accepted);
    }

    /// <summary>⛔ D6 — an order with no behaviour, or in a Library function (no unit), is BP1688; in a resolver graph it is a
    /// side effect (the purity rule).</summary>
    [Fact]
    [CoversDiagnosticCode("BP1688")]
    public void CE2083_AnOrderWithNoBehaviour_OrInALibrary_IsBp1688()
    {
        var noName = BlueprintAssetBuilder.Behavior("Ce2083NoName")
            .WithGraph("Tick", g => g.Entry().SopOrder(SopOrderKind.DoWhenIdle, "").Return(NodeStatus.Success)).Build();
        Assert.Contains(Compile(noName).Diagnostics, d => d.Code == DiagnosticCodes.BP1688 && d.Message.Contains("names no behaviour"));

        var library = BlueprintAssetBuilder.Library("Ce2083Lib")
            .WithGraph("Fn", g => g.Entry().SopOrder(SopOrderKind.DoWhenIdle, "Patrol").Return(NodeStatus.Success)).Build();
        Assert.Contains(Compile(library).Diagnostics, d => d.Code == DiagnosticCodes.BP1688 && d.Message.Contains("Library"));
    }

    /// <summary>⭐ The mirror enum agrees with the runtime one by NAME and VALUE (this assembly is also netstandard2.0).</summary>
    [Fact]
    public void CE2083_SopOrderUrgency_MirrorsReactionUrgency()
    {
        var mirror = Enum.GetValues<SopOrderUrgency>().Select(v => (v.ToString(), (int)v));
        var runtime = Enum.GetValues<ReactionUrgency>().Select(v => (v.ToString(), (int)v));
        Assert.Equal(runtime, mirror);
    }

    private static CompileResult Compile(BlueprintAsset asset)
        => new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
}
