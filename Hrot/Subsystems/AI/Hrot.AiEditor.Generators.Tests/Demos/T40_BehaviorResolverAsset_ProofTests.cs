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
/// ⭐⭐⭐ <c>CE-428</c>/<c>CE-443</c> — <b>a behaviour runs its bound BLUEPRINT resolver asset, handed the SOURCE.</b>
/// 📄 <c>DESIGN_Parameter_Model.md</c> §P.2/§P.7/§P.8. The shipped pair: <c>T40_BehaviorResolverAsset.btree.json</c>
/// (Input <c>Speed</c>, default 3; State <c>Doubled</c>) names <c>T40Resolver.bp.json</c>, whose Parameter
/// <c>HalfSpeed</c> (default 1.5) is the AUTHORED input; its one Construction graph writes
/// <c>Speed = HalfSpeed * 2</c> and <c>Doubled = Speed * 2</c>.
///
/// <para>⭐ Driven through the REAL assembly scan and the REAL ingress, so the proof covers both generators,
/// their cross-generator static call, and the stage order (bake → the resolver INSTEAD of the default copy).</para>
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
    /// ⭐⭐ The resolver is handed the SOURCE — the JSON parsed into ITS Parameters — and writes the block itself.
    /// <para>⚠ Inverse-edit red-proof: emit the default overlay onto In before the resolver in
    /// <c>BTreeBridgeEmitCore.EmitParseParamsLocal</c> and the JSON's <c>Speed</c> no longer loses to the resolver's.</para>
    /// </summary>
    [Fact]
    public void TheResolverGetsTheAuthoredParameters_AndWritesBothHalves()
    {
        var (speed, doubled) = AssignAndRead("{\"HalfSpeed\":2.5}");
        speed.Should().Be(5f, "T40Resolver writes block.In.Speed = HalfSpeed * 2");
        doubled.Should().Be(10f, "and block.St.Doubled = Speed * 2");
    }

    /// <summary>
    /// ⭐⭐ <c>CE-443</c> — NOTHING is copied onto In when a resolver is bound: a JSON key naming a block field
    /// (<c>Speed</c>) is not a Parameter of the resolver, so it is ignored — the resolver's own value stands.
    /// </summary>
    [Fact]
    public void AKeyThatIsNotAResolverParameter_IsNotCopiedOntoTheBlock()
    {
        var (speed, doubled) = AssignAndRead("{\"Speed\":99}");
        speed.Should().Be(3f, "the resolver wrote HalfSpeed(1.5 default) * 2; the JSON Speed was never copied");
        doubled.Should().Be(6f);
    }

    /// <summary>⭐ No JSON ⇒ the resolver sees its Parameter DEFAULT (HalfSpeed 1.5).</summary>
    [Fact]
    public void WithNoJson_TheResolverSeesItsParameterDefaults()
    {
        var (speed, doubled) = AssignAndRead(string.Empty);
        speed.Should().Be(3f);
        doubled.Should().Be(6f);
    }

    /// <summary>⭐ <c>CE-443</c> — the published authored contract is the resolver's Parameters, not the block's In.</summary>
    [Fact]
    public void ThePublishedContract_IsTheResolversParameters()
    {
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(typeof(DemoAiPrimitiveNodes).Assembly, staging, _registry);
        _registry.TryGetId("T40_BehaviorResolverAsset", out int id).Should().BeTrue();
        _registry.TryGetDefinition(id, out var def).Should().BeTrue();
        def!.JsonParamsDtoType.Should().NotBeNull();
        def.JsonParamsDtoType!.Name.Should().Be("Params");
        def.JsonParamsDtoType.GetField("HalfSpeed").Should().NotBeNull();
        def.JsonParamsDtoType.GetField("Speed").Should().BeNull();
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
