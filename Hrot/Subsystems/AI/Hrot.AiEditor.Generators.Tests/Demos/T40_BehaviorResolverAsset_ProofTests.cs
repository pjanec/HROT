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
/// ⭐⭐⭐ <c>CE-428</c> — <b>a behaviour runs its bound BLUEPRINT resolver asset as stage 3.</b>
/// 📄 <c>Architect_Question_76</c> §12.20. The shipped pair: <c>T40_BehaviorResolverAsset.btree.json</c>
/// (Input <c>Speed</c>, default 3; State <c>Doubled</c>) names <c>T40Resolver.bp.json</c>, whose one
/// Construction graph writes <c>Doubled = Speed * 2</c>.
///
/// <para>⭐ Driven through the REAL assembly scan and the REAL ingress, so the proof covers both generators,
/// their cross-generator static call, and the stage ORDER (bake → overlay → resolve) in one go.</para>
/// </summary>
public sealed class T40_BehaviorResolverAsset_ProofTests : IDisposable
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
            Entity = entity, BehaviorName = "T40_BehaviorResolverAsset", JsonParams = json,
        });
        world.Bus.SwapBuffers();
        new BehaviorIngressSystem(_registry).Execute(world, 0.016f);

        ref var blk = ref RootParamsAccess.RootRef(world, entity);
        var block = System.Runtime.CompilerServices.Unsafe.As<byte, Hrot.AI.Behaviors.Trees.T40_BehaviorResolverAsset_Block>(ref blk);
        return (block.In.Speed, block.St.Doubled);
    }

    /// <summary>
    /// ⭐⭐ Stage ORDER: the resolver sees the JSON-overlaid Speed (5), not the baked default (3).
    /// <para>⚠ Inverse-edit red-proof: move the emitted <c>__ResolveStage</c> call ahead of the overlay in
    /// <c>BTreeBridgeEmitCore.EmitParseParamsLocal</c> and Doubled reads 6.</para>
    /// </summary>
    [Fact]
    public void TheResolverRunsAfterTheOverlay_AndWritesTheStateHalf()
    {
        var (speed, doubled) = AssignAndRead("{\"Speed\":5}");
        speed.Should().Be(5f);
        doubled.Should().Be(10f, "T40Resolver writes block.St.Doubled = block.In.Speed * 2 after the overlay");
    }

    /// <summary>⭐ No JSON ⇒ the BAKED default (3) is what the resolver refines.</summary>
    [Fact]
    public void WithNoJson_TheResolverRefinesTheBakedDefault()
    {
        var (speed, doubled) = AssignAndRead(string.Empty);
        speed.Should().Be(3f);
        doubled.Should().Be(6f);
    }

    /// <summary>⭐ The registrar publishes stage 3 on its own, and names the asset; ⛔ a curated resolver on
    /// the same behaviour is a SECOND binding for one region and throws (<c>R-149</c>).</summary>
    [Fact]
    public unsafe void TheDefinitionNamesItsResolver_AndACuratedOneOnTopThrows()
    {
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(typeof(DemoAiPrimitiveNodes).Assembly, staging, _registry);
        _registry.TryGetId("T40_BehaviorResolverAsset", out int id).Should().BeTrue();
        _registry.TryGetDefinition(id, out var def).Should().BeTrue();
        def!.ResolveStage.Should().NotBeNull();
        def.ResolverName.Should().Be("T40Resolver");

        var act = () => _registry.RegisterResolver("T40_BehaviorResolverAsset",
            (string j, byte* m, int c, EntityRepository w, Entity s, IHostVariableAccess? h) => { }, typeof(float));
        act.Should().Throw<InvalidOperationException>().WithMessage("*R-149*");
    }
}
