using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.References;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Catalog;

/// <summary>
/// ⭐⭐⭐ <b><c>HSM-017</c> — renaming a bound HSM variable must not dangle the binding.</b>
///
/// <para>📐 The two halves, and they fail for different reasons: (a) the reference CATALOGUE never knew about HSM
/// variables at all — the complete set of contributors held one variable contributor and it was BTree's; (b) the
/// rename ROUTE (<c>VariableRenameCommit</c> → <c>RefactorService</c>) rewrites LINES OF THE SOURCE FILE that
/// contain the composite key <c>{assetId:D}::{name}</c>, and a <c>.hsm.json</c> stores the bare name — so it edits
/// nothing and the declaration moves alone.</para>
///
/// <para>⚠ The second half is why the model-level rails below exist: a contributor rail alone would have gone
/// green over a rename that still dangles.</para>
/// </summary>
public class HsmBlackboardVariableReferenceTests
{
    private static HsmAsset MakeAsset(out StateNode idle, out TransitionNode transition, bool managed = true)
    {
        var root = new StateNode("__root__");
        idle = new StateNode("Idle") { IsInitial = true, Parent = root };
        var busy = new StateNode("Busy") { Parent = root };
        root.Children.Add(idle);
        root.Children.Add(busy);

        transition = new TransitionNode { VisualId = Guid.NewGuid(), Source = idle, Target = busy };

        var asset = new HsmAsset(
            Guid.NewGuid(), "TestAsset", "", true, "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            root,
            new List<StateNode> { idle, busy },
            new List<TransitionNode> { transition },
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());

        // ⚠ The ctor's 4th arg is isEditorOwned, NOT the blackboard flag — they are different properties.
        asset.IsBlackboardEditorManaged = managed;
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("speed",      typeof(float), null),
            new BlackboardVariableEntry("speedLimit", typeof(float), null),
        });
        return asset;
    }

    // ---- (a) the catalogue knows HSM variables ----

    [Fact]
    public void The_contributor_enumerates_blackboard_variables_with_the_shared_key_format()
    {
        var asset = MakeAsset(out _, out _);

        var elements = new HsmReferenceContributor().EnumerateElements(asset);

        elements.Should().Contain(e =>
            e.Kind == SubElementKind.BlackboardVariable &&
            e.Key == $"{asset.AssetId:D}::speed");
    }

    [Fact]
    public void An_unmanaged_blackboard_contributes_no_variable_elements()
    {
        var asset = MakeAsset(out _, out _, managed: false);

        new HsmReferenceContributor().EnumerateElements(asset)
            .Where(e => e.Kind == SubElementKind.BlackboardVariable)
            .Should().BeEmpty();
    }

    [Fact]
    public void A_states_binding_contributes_a_variable_reference()
    {
        var asset = MakeAsset(out var idle, out _);
        idle.Activity = new BehaviorActionBinding { MethodFqn = "Ai.A.Run", ExpressionTargetField = "speed" };

        var refs = new HsmReferenceContributor().EnumerateReferences(asset);

        refs.Should().Contain(r =>
            r.TargetKind == SubElementKind.BlackboardVariable &&
            r.TargetKey  == $"{asset.AssetId:D}::speed" &&
            r.HostElementId == idle.StableId);
    }

    [Fact]
    public void A_transitions_guard_binding_contributes_a_variable_reference()
    {
        var asset = MakeAsset(out _, out var transition);
        transition.Guard = new BehaviorActionBinding { MethodFqn = "Ai.G.Can", ExpressionTargetField = "speed" };

        var refs = new HsmReferenceContributor().EnumerateReferences(asset);

        refs.Should().Contain(r =>
            r.TargetKind == SubElementKind.BlackboardVariable &&
            r.HostElementId == transition.VisualId);
    }

    [Fact]
    public void A_binding_with_no_output_target_contributes_no_variable_reference()
    {
        var asset = MakeAsset(out var idle, out _);
        idle.Activity = new BehaviorActionBinding { MethodFqn = "Ai.A.Run" };

        new HsmReferenceContributor().EnumerateReferences(asset)
            .Where(r => r.TargetKind == SubElementKind.BlackboardVariable)
            .Should().BeEmpty();
    }

    // ---- (b) the rename actually moves the bindings ----

    [Fact]
    public void Renaming_a_variable_retargets_every_binding_that_names_it()
    {
        var asset = MakeAsset(out var idle, out var transition);
        idle.OnEntry    = new BehaviorActionBinding { MethodFqn = "Ai.A.Enter", ExpressionTargetField = "speed" };
        idle.Activity   = new BehaviorActionBinding { MethodFqn = "Ai.A.Run",   ExpressionTargetField = "speed" };
        transition.Action = new BehaviorActionBinding { MethodFqn = "Ai.A.Go",  ExpressionTargetField = "speed" };

        asset.RenameVariable("speed", "velocity");

        idle.OnEntry!.ExpressionTargetField.Should().Be("velocity");
        idle.Activity!.ExpressionTargetField.Should().Be("velocity");
        transition.Action!.ExpressionTargetField.Should().Be("velocity");
        asset.BlackboardVariables.Should().Contain(v => v.Name == "velocity");
    }

    [Fact]
    public void Renaming_does_not_touch_a_binding_that_names_a_DIFFERENT_variable()
    {
        // ⭐ The substring trap a text-level rewrite would fall into: "speed" is a prefix of "speedLimit".
        var asset = MakeAsset(out var idle, out _);
        idle.Activity = new BehaviorActionBinding { MethodFqn = "Ai.A.Run", ExpressionTargetField = "speedLimit" };

        asset.RenameVariable("speed", "velocity");

        idle.Activity!.ExpressionTargetField.Should().Be("speedLimit");
    }

    [Fact]
    public void Renaming_an_unknown_variable_changes_nothing()
    {
        var asset = MakeAsset(out var idle, out _);
        idle.Activity = new BehaviorActionBinding { MethodFqn = "Ai.A.Run", ExpressionTargetField = "speed" };

        asset.RenameVariable("nosuch", "whatever");

        idle.Activity!.ExpressionTargetField.Should().Be("speed");
    }

    [Fact]
    public void The_renamed_bindings_are_exactly_the_ones_the_reference_count_claimed()
    {
        // ⭐ The count and the fix-up walk the same sites on purpose; this pins them together.
        var asset = MakeAsset(out var idle, out var transition);
        idle.OnExit       = new BehaviorActionBinding { MethodFqn = "Ai.A.Exit", ExpressionTargetField = "speed" };
        transition.Guard  = new BehaviorActionBinding { MethodFqn = "Ai.G.Can",  ExpressionTargetField = "speed" };

        asset.CountNodesReferencingVariable("speed").Should().Be(2);

        asset.RenameVariable("speed", "velocity");

        asset.CountNodesReferencingVariable("speed").Should().Be(0);
        asset.CountNodesReferencingVariable("velocity").Should().Be(2);
    }
}
