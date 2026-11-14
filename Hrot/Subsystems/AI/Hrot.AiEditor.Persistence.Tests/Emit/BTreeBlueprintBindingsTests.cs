using System;
using System.Collections.Generic;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Emit;

/// <summary>
/// ⭐⭐ <c>CE-417</c> B-1 (slice 4c) — a BTree blueprint binding persists the blueprint's id + name; the TickCore it calls
/// is derived at build time. 📄 <c>DESIGN_Behavior_Action_Binding.md</c> §5.5.
/// </summary>
public sealed class BTreeBlueprintBindingsTests
{
    private static readonly Guid ParamDemo = Guid.Parse("00000000-aaaa-0002-0000-0000000000d0");

    private static BehaviorTreeAssetDto Asset(params BTreeNodeDto[] nodes)
        => new() { Nodes = new List<BTreeNodeDto>(nodes) };

    [Fact]
    public void TheCorpusClassName_IsTheOneTheGeneratorAlwaysEmitted()
    {
        // ⭐ The pre-B-1 corpus carried exactly this FQN for ParamDemo (T33, T39) ⇒ deriving it changes no output.
        BlueprintClassNaming.TickCoreFqn(ParamDemo, "ParamDemo")
            .Should().Be("Hrot.AI.Behaviors.Generated.ParamDemo_CEFE162F_Bp.TickCore");
    }

    /// <summary>⭐ The catalogue answers by ID — the persisted name can be stale (a rename) or ambiguous (two blueprints
    /// share a name) without changing which class is called.</summary>
    [Fact]
    public void ABlueprintBinding_IsResolvedByItsId_NotItsPersistedName()
    {
        var action = new BTreeActionNodeDto
        {
            Action = new BehaviorActionBindingDto { BlueprintAssetId = ParamDemo, BlueprintName = "StaleOldName" },
            DelegateShape = BTreeDelegateShapeDto.AiPrimitiveTickCore,
        };

        int n = BTreeBlueprintBindings.ResolveMethods(Asset(action), id => id == ParamDemo ? "ParamDemo_CEFE162F_Bp" : null);

        n.Should().Be(1);
        action.Action!.MethodFqn.Should().Be("Hrot.AI.Behaviors.Generated.ParamDemo_CEFE162F_Bp.TickCore");
    }

    /// <summary>A blueprint missing from the build falls back to the persisted name (which the editor heals on open).</summary>
    [Fact]
    public void WithoutACatalogEntry_TheClassComesFromThePersistedName()
    {
        var condition = new BTreeConditionNodeDto
        {
            Condition = new BehaviorActionBindingDto { BlueprintAssetId = ParamDemo, BlueprintName = "ParamDemo" },
        };

        BTreeBlueprintBindings.ResolveMethods(Asset(condition), _ => null);

        condition.Condition!.MethodFqn.Should().Be("Hrot.AI.Behaviors.Generated.ParamDemo_CEFE162F_Bp.TickCore");
    }

    [Fact]
    public void AMethodBinding_IsLeftAlone()
    {
        var action = new BTreeActionNodeDto
        {
            Action = new BehaviorActionBindingDto { MethodFqn = "Hrot.AI.Behaviors.Brains.DemoAiPrimitiveNodes.TickCore" },
        };

        BTreeBlueprintBindings.ResolveMethods(Asset(action), _ => "Wrong_00000000_Bp").Should().Be(0);
        action.Action!.MethodFqn.Should().Be("Hrot.AI.Behaviors.Brains.DemoAiPrimitiveNodes.TickCore");
    }

    /// <summary>⛔ A blueprint id with neither a catalogue entry nor a name cannot be called — left unresolved, never guessed.
    /// The method-compatibility validator then reports the unbound leaf (BTREE0002).</summary>
    [Fact]
    public void AnIdWithNoCatalogEntryAndNoName_IsLeftUnresolved()
    {
        var action = new BTreeActionNodeDto { Action = new BehaviorActionBindingDto { BlueprintAssetId = ParamDemo } };

        BTreeBlueprintBindings.ResolveMethods(Asset(action), _ => null).Should().Be(0);
        action.Action!.MethodFqn.Should().BeNull();
    }
}
