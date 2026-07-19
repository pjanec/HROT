using System.Linq;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Q#14 slice 2a: a PublishEvent node baked with a custom event's FQN (the editor-discovered shape) lowers
/// via the baked branch to <c>world.Bus.Publish(new global::{Fqn}{...})</c> — no EngineEventCatalog entry
/// required. Baking an existing event's FQN (ClearBehaviorEvent) so the generated C# references a real type.
/// </summary>
public sealed class PublishCustomEventTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: System.Array.Empty<BlueprintSignature>());

    [Fact]
    public void BakedEventTypeFqn_LowersToTypedBusPublish()
    {
        const string fqn = "Fdp.Toolkit.Behavior.Events.ClearBehaviorEvent";

        var asset = BlueprintAssetBuilder
            .AiPrimitive("PubCustom")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry()
                .PublishCustomEvent(fqn, targetFieldName: "Entity")
                .Return(NodeStatus.Success))
            .Build();

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            $"Publish (baked FQN) failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.GeneratedSource);
        // The baked FQN — not a catalog lookup — drives the emitted publish, with the target field self-defaulted.
        Assert.Contains("world.Bus.Publish(new global::" + fqn, result.GeneratedSource!);
        Assert.Contains("Entity =", result.GeneratedSource!);
    }
}
