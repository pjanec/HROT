using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// ⭐⭐⭐ <c>CE-503</c> — <b>an HSM runs its bound BLUEPRINT resolver asset</b>, exactly as a BTree does (<c>T40</c>). The shipped
/// pair: <c>HsmResolverDemo.hsm.json</c> (Input <c>Speed</c>, default 3; State <c>Doubled</c>) names
/// <c>HsmResolverDemoResolver.bp.json</c>, whose Parameter <c>HalfSpeed</c> (default 1.5) is the AUTHORED input; its one
/// Construction graph writes <c>Speed = HalfSpeed * 2</c> and <c>Doubled = Speed * 2</c>.
///
/// <para>🔴 RED before <c>CE-503</c>: <c>HsmAssetDto</c> had no <c>Resolver</c> field, so the HSM generator emitted bake + supply
/// only — <c>Speed</c> stayed its baked 3 whatever the JSON said, and <c>Doubled</c> stayed 0.</para>
/// <para>⭐ Driven through the REAL assembly scan and the REAL ingress, like <c>T40_BehaviorResolverAsset_ProofTests</c>.</para>
/// </summary>
public sealed class HsmResolverDemo_ProofTests : IDisposable
{
    private readonly BehaviorRegistry _registry = new();
    public void Dispose() => _registry.Clear();

    private unsafe (float Speed, float Doubled) AssignAndRead(string json)
    {
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(typeof(DemoAiPrimitiveNodes).Assembly, staging, _registry);

        var world = new EntityRepository();
        world.RegisterComponent<BehaviorState>();
        BlueprintTierTable.RegisterAll(world);
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        RootStateAccess.EnsureRootState(world, entity);

        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity = entity, BehaviorName = "HsmResolverDemo", JsonParams = json,
        });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(_registry).Execute(world, 0.016f);

        ref var blk = ref RootParamsAccess.RootRef(world, entity);
        var block = System.Runtime.CompilerServices.Unsafe.As<byte, Hrot.AI.Behaviors.Machines.HsmResolverDemo_Block>(ref blk);
        return (block.In.Speed, block.St.Doubled);
    }

    /// <summary>⭐⭐ The resolver is handed the AUTHORED parameters and writes both halves of the HSM's block.
    /// <para>✅ Red-proof: drop <c>Resolver = dto.Resolver</c> from <c>HsmBridgeEmitCore.BlackboardOwner</c> ⇒ Speed stays 3 and
    /// Doubled 0.</para></summary>
    [Fact]
    public void TheResolverGetsTheAuthoredParameters_AndWritesBothHalves()
    {
        var (speed, doubled) = AssignAndRead("{\"HalfSpeed\":2.5}");
        speed.Should().Be(5f, "HsmResolverDemoResolver writes block.In.Speed = HalfSpeed * 2");
        doubled.Should().Be(10f, "and block.St.Doubled = Speed * 2");
    }

    /// <summary>⭐ No JSON ⇒ the resolver sees its Parameter DEFAULT (HalfSpeed 1.5).</summary>
    [Fact]
    public void WithNoJson_TheResolverSeesItsParameterDefaults()
    {
        var (speed, doubled) = AssignAndRead(string.Empty);
        speed.Should().Be(3f);
        doubled.Should().Be(6f);
    }

    /// <summary>⭐ The HSM's definition names its resolver and publishes stage 3; ⛔ a curated resolver on top is a second
    /// binding for one region and throws (<c>R-149</c>) — the same guard as the BTree's.</summary>
    [Fact]
    public unsafe void TheDefinitionNamesItsResolver_AndACuratedOneOnTopThrows()
    {
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(typeof(DemoAiPrimitiveNodes).Assembly, staging, _registry);
        _registry.TryGetId("HsmResolverDemo", out int id).Should().BeTrue();
        _registry.TryGetDefinition(id, out var def).Should().BeTrue();
        def!.BrainTier.Should().Be(BehaviorConstants.BrainTierHsm);
        def.ResolveStage.Should().NotBeNull();
        def.ResolverName.Should().Be("HsmResolverDemoResolver");

        var act = () => _registry.RegisterResolver("HsmResolverDemo",
            (string j, byte* m, int c, EntityRepository w, Entity s) => { }, typeof(float));
        act.Should().Throw<InvalidOperationException>().WithMessage("*R-149*");
    }
}
