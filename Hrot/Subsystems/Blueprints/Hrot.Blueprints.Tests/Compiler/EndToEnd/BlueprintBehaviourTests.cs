using System;
using System.Linq;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-446</c> — a behaviour implemented by a blueprint, end to end.</b> 📄 <c>Architect_Question_77</c> §5.6.
///
/// <para>
/// ⭐ A <see cref="BlueprintDispatchKind.Behavior"/> asset is the Instance body (full node set, latent cursor) whose Tick
/// returns a status. These rails drive the REAL chain: compile → load (the generated registrar registers a
/// <c>BehaviorDefinition</c> on the blueprint brain tier) → <c>AssignBehaviorEvent</c> through <c>BehaviorIngressSystem</c>
/// → <c>BrainTickSystem</c> → finish → the clear (<c>CE-449</c>).
/// </para>
/// </summary>
public sealed unsafe class BlueprintBehaviourTests : IDisposable
{
    private readonly BlueprintTestFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    /// <summary>Tick: wait one second (latent), then Return(Success).</summary>
    private static BlueprintAsset WaitThenSucceed(string name) => BlueprintAssetBuilder
        .Behavior(name)
        .WithGraph("Tick", g => g.Entry().Delay(1f).Return(Hrot.Blueprints.Core.Assets.NodeStatus.Success))
        .Build();

    // ── emission ────────────────────────────────────────────────────────────

    [Fact]
    public void CE446_ABlueprintBehaviour_EmitsAStatusTick_AndRegistersOnTheBlueprintBrainTier()
    {
        var result = new BlueprintCompiler().Compile(WaitThenSucceed("WaitThenSucceed"), GoldenCorpus.Options());
        Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Code + ": " + d.Message)));
        var src = result.GeneratedSource!;

        Assert.Contains("public static global::Fbt.NodeStatus Tick(", src);
        Assert.Contains("return global::Fbt.NodeStatus.Success;", src);     // the Return node finishes it
        Assert.Contains("return global::Fbt.NodeStatus.Running;", src);     // suspended on the delay
        Assert.Contains("BrainTier = global::Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBlueprint", src);
        Assert.Contains("BlueprintTick = ", src);
        // ⛔ never staged as an attachable Instance
        Assert.DoesNotContain("BlueprintDispatchKind.Instance", src);
        Assert.DoesNotContain("TickThunk", src);
    }

    [Fact]
    public void CE446_ATickThatFallsOffTheEnd_IsRunning_NotFinished()
    {
        var src = new BlueprintCompiler().Compile(BlueprintAssetBuilder
            .Behavior("NeverEnds")
            .WithGraph("Tick", g => g.Entry())
            .Build(), GoldenCorpus.Options()).GeneratedSource!;

        Assert.Contains("return global::Fbt.NodeStatus.Running;", src);
        Assert.DoesNotContain("NodeStatus.Success", src);
    }

    // ── runtime, through the real ingress and brain tick ───────────────────

    /// <summary>
    /// ⭐⭐⭐ assign → it runs (Running while the latent delay waits) → the delay elapses → Return(Success) →
    /// <c>BehaviorFinishedEvent(Success)</c> exactly once → cleared (<c>BrainTier = 0</c>, <c>CE-449</c>).
    /// </summary>
    [Fact]
    public void CE446_ABlueprintBehaviour_WaitsOnItsLatentNode_ThenFinishesAndIsCleared()
    {
        const string Name = "CE446WaitThenSucceed";
        _fixture.CompileAndLoad(WaitThenSucceed(Name));
        Assert.True(_fixture.BehaviorRegistry.TryGetId(Name, out int id), "the generated registrar must register the behaviour by name");
        Assert.True(_fixture.BehaviorRegistry.TryGetDefinition(id, out var def));
        Assert.Equal(BehaviorConstants.BrainTierBlueprint, def!.BrainTier);

        var world = _fixture.World;
        var e = _fixture.CreateEntity();
        world.AddComponent(e, new BehaviorState());
        world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = Name, JsonParams = string.Empty });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(_fixture.BehaviorRegistry).Execute(world, 0.016f);
        Assert.Equal(BehaviorConstants.BrainTierBlueprint, world.GetComponent<BehaviorState>(e).BrainTier);

        var brain = new BrainTickSystem(_fixture.BehaviorRegistry);
        int finished = 0;
        void Frame(float t)
        {
            world.SetSimulationTime(t);
            brain.Execute(world, 0.016f);
            world.Bus.SwapBuffers();
            foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Index == e.Index && evt.Result == Fbt.NodeStatus.Success) finished++;
        }

        Frame(10.0f);   // enters the delay (until 11.0)
        Frame(10.5f);   // still waiting
        Assert.Equal(0, finished);
        Assert.Equal(BehaviorConstants.BrainTierBlueprint, world.GetComponent<BehaviorState>(e).BrainTier);

        Frame(11.5f);   // the delay elapsed ⇒ Return(Success)
        Frame(12.0f);   // nothing ticks a finished behaviour
        Assert.Equal(1, finished);
        Assert.Equal(0, world.GetComponent<BehaviorState>(e).BrainTier);
        Assert.False(RootParamsAccess.TryGetRootBytes(world, e, out _), "the block is freed by the clear");
    }
}
