using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public class HsmValidationTests
{
    // ---- helpers ----

    // Builds a minimal HsmAsset directly (bypassing compiler pipeline).
    // rootState is the synthetic root (not in allStates).
    private static HsmAsset MakeAsset(
        StateNode rootState,
        List<StateNode> allStates,
        List<TransitionNode>? allTransitions = null,
        List<GlobalTransitionNode>? allGlobalTransitions = null,
        List<RegionNode>? allRegions = null,
        List<EventDefinition>? allEvents = null)
    {
        return new HsmAsset(
            Guid.NewGuid(), "TestAsset", "", false, "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            rootState,
            allStates,
            allTransitions ?? new List<TransitionNode>(),
            allGlobalTransitions ?? new List<GlobalTransitionNode>(),
            allRegions ?? new List<RegionNode>(),
            allEvents ?? new List<EventDefinition>());
    }

    // ---- tests ----

    [Fact]
    public void Valid_asset_produces_no_diagnostics()
    {
        var root = new StateNode("__root__");
        var a = new StateNode("A") { IsInitial = true, Parent = root };
        var b = new StateNode("B") { Parent = root };
        root.Children.Add(a);
        root.Children.Add(b);

        var asset = MakeAsset(root, new List<StateNode> { a, b });
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Composite_without_initial_child_produces_error()
    {
        var root = new StateNode("__root__");
        var composite = new StateNode("Composite") { Parent = root };
        var child = new StateNode("Child") { Parent = composite };
        root.Children.Add(composite);
        composite.Children.Add(child);

        var asset = MakeAsset(root, new List<StateNode> { composite, child });
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Should().ContainSingle(d =>
            d.Code == HsmDiagnosticCode.CompositeWithoutInitialChild &&
            d.Severity == HsmDiagnosticSeverity.Error &&
            d.TargetStableIds.Contains(composite.StableId));
    }

    [Fact]
    public void Multiple_initial_children_produces_error()
    {
        var root = new StateNode("__root__");
        var composite = new StateNode("Composite") { Parent = root };
        var a = new StateNode("A") { IsInitial = true, Parent = composite };
        var b = new StateNode("B") { IsInitial = true, Parent = composite };
        root.Children.Add(composite);
        composite.Children.Add(a);
        composite.Children.Add(b);

        var asset = MakeAsset(root, new List<StateNode> { composite, a, b });
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Should().ContainSingle(d =>
            d.Code == HsmDiagnosticCode.MultipleInitialChildrenInSameParent &&
            d.Severity == HsmDiagnosticSeverity.Error &&
            d.TargetStableIds.Contains(composite.StableId));
    }

    [Fact]
    public void Final_state_with_outgoing_transition_produces_error()
    {
        var root = new StateNode("__root__");
        var a = new StateNode("A") { IsFinal = true, Parent = root };
        var b = new StateNode("B") { IsInitial = true, Parent = root };
        root.Children.Add(a);
        root.Children.Add(b);
        var t = new TransitionNode { VisualId = Guid.NewGuid(), Source = a, Target = b };
        a.OutgoingTransitions.Add(t);

        var asset = MakeAsset(root,
            new List<StateNode> { a, b },
            new List<TransitionNode> { t });
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Should().Contain(d =>
            d.Code == HsmDiagnosticCode.FinalStateWithOutgoingTransition &&
            d.Severity == HsmDiagnosticSeverity.Error &&
            d.TargetStableIds.Contains(a.StableId));
    }

    [Fact]
    public void Final_state_with_children_produces_error()
    {
        var root = new StateNode("__root__");
        var finalComposite = new StateNode("FinalComposite") { IsFinal = true, Parent = root };
        var child = new StateNode("Child") { IsInitial = true, Parent = finalComposite };
        root.Children.Add(finalComposite);
        finalComposite.Children.Add(child);

        var asset = MakeAsset(root, new List<StateNode> { finalComposite, child });
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Should().Contain(d =>
            d.Code == HsmDiagnosticCode.FinalStateWithChildren &&
            d.Severity == HsmDiagnosticSeverity.Error &&
            d.TargetStableIds.Contains(finalComposite.StableId));
    }

    [Fact]
    public void History_outside_composite_produces_warning()
    {
        var root = new StateNode("__root__");
        var h = new StateNode("H") { IsHistory = true, Parent = root };
        root.Children.Add(h);

        var asset = MakeAsset(root, new List<StateNode> { h });
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Should().ContainSingle(d =>
            d.Code == HsmDiagnosticCode.HistoryOutsideComposite &&
            d.Severity == HsmDiagnosticSeverity.Warning &&
            d.TargetStableIds.Contains(h.StableId));
    }

    [Fact]
    public void State_depth_exceeded_produces_error()
    {
        var root = new StateNode("__root__");
        var allStates = new List<StateNode>();

        // Build a chain 17 levels deep: root -> S0 -> S1 -> ... -> S16
        StateNode prev = root;
        StateNode? deepest = null;
        for (int i = 0; i < 17; i++)
        {
            var s = new StateNode($"S{i}") { Parent = prev };
            prev.Children.Add(s);
            allStates.Add(s);
            // Mark each new state as the initial child of its parent.
            s.IsInitial = true;
            deepest = s;
            prev = s;
        }

        var asset = MakeAsset(root, allStates);
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Should().Contain(d =>
            d.Code == HsmDiagnosticCode.StateDepthExceeded &&
            d.Severity == HsmDiagnosticSeverity.Error &&
            d.TargetStableIds.Contains(deepest!.StableId));
    }

    [Fact]
    public void Event_reference_dangling_for_transition()
    {
        var root = new StateNode("__root__");
        var a = new StateNode("A") { IsInitial = true, Parent = root };
        var b = new StateNode("B") { Parent = root };
        root.Children.Add(a);
        root.Children.Add(b);
        var t = new TransitionNode
        {
            VisualId = Guid.NewGuid(),
            Source = a,
            Target = b,
            EventId = 99,
        };
        a.OutgoingTransitions.Add(t);

        // AllEvents is empty, so EventId=99 is dangling.
        var asset = MakeAsset(root,
            new List<StateNode> { a, b },
            new List<TransitionNode> { t },
            allEvents: new List<EventDefinition>());
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Should().Contain(d =>
            d.Code == HsmDiagnosticCode.EventReferenceDangling &&
            d.Severity == HsmDiagnosticSeverity.Error);
    }

    [Fact]
    public void Valid_composite_with_single_initial_child_no_diagnostics()
    {
        var root = new StateNode("__root__");
        var composite = new StateNode("Composite") { Parent = root };
        var init = new StateNode("Init") { IsInitial = true, Parent = composite };
        var other = new StateNode("Other") { Parent = composite };
        root.Children.Add(composite);
        composite.Children.Add(init);
        composite.Children.Add(other);

        var asset = MakeAsset(root, new List<StateNode> { composite, init, other });
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Multiple_diagnostics_for_multiple_violations()
    {
        var root = new StateNode("__root__");
        var c1 = new StateNode("C1") { IsInitial = true, Parent = root };
        var c2 = new StateNode("C2") { Parent = root };
        root.Children.Add(c1);
        root.Children.Add(c2);
        // Both composites have one child each, but neither child is marked initial.
        var child1 = new StateNode("Child1") { Parent = c1 };
        var child2 = new StateNode("Child2") { Parent = c2 };
        c1.Children.Add(child1);
        c2.Children.Add(child2);

        var asset = MakeAsset(root, new List<StateNode> { c1, c2, child1, child2 });
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Where(d => d.Code == HsmDiagnosticCode.CompositeWithoutInitialChild)
            .Should().HaveCount(2);
    }

    [Fact]
    public void OutputLaneConflict_in_parallel_state_produces_warning()
    {
        var root = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        var rn0 = new RegionNode("R0") { RegionIndex = 0 };
        var rn1 = new RegionNode("R1") { RegionIndex = 1 };
        parallel.RegionNodes.Add(rn0);
        parallel.RegionNodes.Add(rn1);

        // Both regions write to Animation lane (bit 0 = 0x01) -> conflict.
        var ca = new StateNode("CA") { IsInitial = true, RegionIndex = 0, OutputLaneMask = 0x01, Parent = parallel };
        var cb = new StateNode("CB") { RegionIndex = 1, OutputLaneMask = 0x01, Parent = parallel };
        parallel.Children.Add(ca);
        parallel.Children.Add(cb);

        var asset = MakeAsset(root,
            new List<StateNode> { parallel, ca, cb },
            allRegions: new List<RegionNode> { rn0, rn1 });
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Should().Contain(d =>
            d.Code == HsmDiagnosticCode.OutputLaneConflict &&
            d.Severity == HsmDiagnosticSeverity.Warning);
    }

    [Fact]
    public void No_conflict_when_parallel_regions_have_disjoint_lanes()
    {
        var root = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        var rn0 = new RegionNode("R0") { RegionIndex = 0 };
        var rn1 = new RegionNode("R1") { RegionIndex = 1 };
        parallel.RegionNodes.Add(rn0);
        parallel.RegionNodes.Add(rn1);

        // Region 0: Animation (bit 0 = 0x01), Region 1: Navigation (bit 1 = 0x02) -> no conflict.
        var ca = new StateNode("CA") { IsInitial = true, RegionIndex = 0, OutputLaneMask = 0x01, Parent = parallel };
        var cb = new StateNode("CB") { RegionIndex = 1, OutputLaneMask = 0x02, Parent = parallel };
        parallel.Children.Add(ca);
        parallel.Children.Add(cb);

        var asset = MakeAsset(root,
            new List<StateNode> { parallel, ca, cb },
            allRegions: new List<RegionNode> { rn0, rn1 });
        var validator = new HsmValidator();

        var diagnostics = validator.Validate(asset);

        diagnostics.Where(d => d.Code == HsmDiagnosticCode.OutputLaneConflict)
            .Should().BeEmpty();
    }

    // ---- HSM-008: the lane union is over LEAF states, not direct children ----

    [Fact]
    public void Conflict_is_found_when_the_overlapping_lanes_are_NESTED_under_the_regions()
    {
        var root = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        parallel.RegionNodes.Add(new RegionNode("R0") { RegionIndex = 0 });
        parallel.RegionNodes.Add(new RegionNode("R1") { RegionIndex = 1 });

        // Each region's DIRECT child is a composite with mask 0 — the writing states are one level down.
        // Before HSM-008 this asset validated clean while both regions wrote the Animation lane.
        var ca = new StateNode("CA") { IsInitial = true, RegionIndex = 0, OutputLaneMask = 0x00, Parent = parallel };
        var cb = new StateNode("CB") { RegionIndex = 1, OutputLaneMask = 0x00, Parent = parallel };
        var caLeaf = new StateNode("CALeaf") { IsInitial = true, OutputLaneMask = 0x01, Parent = ca };
        var cbLeaf = new StateNode("CBLeaf") { IsInitial = true, OutputLaneMask = 0x01, Parent = cb };
        ca.Children.Add(caLeaf);
        cb.Children.Add(cbLeaf);
        parallel.Children.Add(ca);
        parallel.Children.Add(cb);

        var asset = MakeAsset(root,
            new List<StateNode> { parallel, ca, cb, caLeaf, cbLeaf },
            allRegions: new List<RegionNode>(parallel.RegionNodes));

        var diagnostics = new HsmValidator().Validate(asset);

        diagnostics.Should().Contain(d =>
            d.Code == HsmDiagnosticCode.OutputLaneConflict &&
            d.Severity == HsmDiagnosticSeverity.Warning);
    }

    [Fact]
    public void No_conflict_when_nested_leaves_in_different_regions_use_disjoint_lanes()
    {
        var root = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        parallel.RegionNodes.Add(new RegionNode("R0") { RegionIndex = 0 });
        parallel.RegionNodes.Add(new RegionNode("R1") { RegionIndex = 1 });

        var ca = new StateNode("CA") { IsInitial = true, RegionIndex = 0, Parent = parallel };
        var cb = new StateNode("CB") { RegionIndex = 1, Parent = parallel };
        var caLeaf = new StateNode("CALeaf") { IsInitial = true, OutputLaneMask = 0x01, Parent = ca };
        var cbLeaf = new StateNode("CBLeaf") { IsInitial = true, OutputLaneMask = 0x02, Parent = cb };
        ca.Children.Add(caLeaf);
        cb.Children.Add(cbLeaf);
        parallel.Children.Add(ca);
        parallel.Children.Add(cb);

        var asset = MakeAsset(root,
            new List<StateNode> { parallel, ca, cb, caLeaf, cbLeaf },
            allRegions: new List<RegionNode>(parallel.RegionNodes));

        var diagnostics = new HsmValidator().Validate(asset);

        diagnostics.Where(d => d.Code == HsmDiagnosticCode.OutputLaneConflict)
            .Should().BeEmpty();
    }

    [Fact]
    public void A_composites_OWN_mask_does_not_contribute_when_it_has_leaves()
    {
        // ⭐ The kernel arbitrates the ACTIVE LEAF, so a composite's own (stale) mask must not create a
        //    conflict its leaves do not have.
        var root = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        parallel.RegionNodes.Add(new RegionNode("R0") { RegionIndex = 0 });
        parallel.RegionNodes.Add(new RegionNode("R1") { RegionIndex = 1 });

        var ca = new StateNode("CA") { IsInitial = true, RegionIndex = 0, OutputLaneMask = 0x01, Parent = parallel };
        var cb = new StateNode("CB") { RegionIndex = 1, OutputLaneMask = 0x01, Parent = parallel };
        var caLeaf = new StateNode("CALeaf") { IsInitial = true, OutputLaneMask = 0x01, Parent = ca };
        var cbLeaf = new StateNode("CBLeaf") { IsInitial = true, OutputLaneMask = 0x02, Parent = cb };
        ca.Children.Add(caLeaf);
        cb.Children.Add(cbLeaf);
        parallel.Children.Add(ca);
        parallel.Children.Add(cb);

        var asset = MakeAsset(root,
            new List<StateNode> { parallel, ca, cb, caLeaf, cbLeaf },
            allRegions: new List<RegionNode>(parallel.RegionNodes));

        var diagnostics = new HsmValidator().Validate(asset);

        diagnostics.Where(d => d.Code == HsmDiagnosticCode.OutputLaneConflict)
            .Should().BeEmpty();
    }

    // ---- HSM-007: the inspector's "Output lanes (inferred)" summary ----

    [Theory]
    [InlineData((byte)0x00, "none")]
    [InlineData((byte)0x01, "Animation")]
    [InlineData((byte)0x03, "Animation, Navigation")]
    public void The_inferred_lane_summary_names_the_lanes_in_the_mask(byte mask, string expected)
        => Hrot.Hsm.Editor.Inspector.HsmFacetMapper.DescribeLanes(mask).Should().Be(expected);

    // ---- HSM-012: a Timer binding is emitted and never armed ----

    [Fact]
    public void State_with_a_Timer_binding_is_reported_as_never_firing()
    {
        var root = new StateNode("__root__");
        var idle = new StateNode("Idle") { IsInitial = true, Parent = root };
        idle.Timer = new Hrot.Editor.AiShared.BehaviorActionBinding { MethodFqn = "Ai.States.OnTimer" };
        root.Children.Add(idle);

        var diagnostics = new HsmValidator().Validate(MakeAsset(root, new List<StateNode> { idle }));

        diagnostics.Should().Contain(d =>
            d.Code == HsmDiagnosticCode.TimerActionNotImplemented &&
            d.Severity == HsmDiagnosticSeverity.Warning);
    }

    [Fact]
    public void State_without_a_Timer_binding_is_not_reported()
    {
        var root = new StateNode("__root__");
        var idle = new StateNode("Idle") { IsInitial = true, Parent = root };
        root.Children.Add(idle);

        var diagnostics = new HsmValidator().Validate(MakeAsset(root, new List<StateNode> { idle }));

        diagnostics.Where(d => d.Code == HsmDiagnosticCode.TimerActionNotImplemented)
            .Should().BeEmpty();
    }
}
