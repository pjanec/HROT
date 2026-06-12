using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Validation;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Renderers;
using Hrot.Hsm.Editor.Validation;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmDiagnosticSurfaceTests
{
    // ---- helpers ----

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

    // ---- test 1: Node-state Error ----

    [Fact]
    public void Composite_without_initial_child_sets_state_to_Error()
    {
        var root = new StateNode("__root__");
        var composite = new StateNode("Composite") { Parent = root };
        var child1 = new StateNode("Child1") { Parent = composite };
        var child2 = new StateNode("Child2") { Parent = composite };
        var simple = new StateNode("Simple") { IsInitial = true, Parent = root };
        root.Children.Add(composite);
        root.Children.Add(simple);
        composite.Children.Add(child1);
        composite.Children.Add(child2);

        var asset = MakeAsset(root, new List<StateNode> { composite, child1, child2, simple });
        _ = new HsmGraphModel(asset);

        composite.State.Should().Be(NodeState.Error);
        composite.StatusTooltip.Should().NotBeNull();
        composite.StatusTooltip.Should().Contain("no child marked as initial");
    }

    // ---- test 2: Clean state Normal ----

    [Fact]
    public void Valid_simple_state_has_Normal_state()
    {
        var root = new StateNode("__root__");
        var simple = new StateNode("Simple") { IsInitial = true, Parent = root };
        root.Children.Add(simple);

        var asset = MakeAsset(root, new List<StateNode> { simple });
        _ = new HsmGraphModel(asset);

        simple.State.Should().Be(NodeState.Normal);
        simple.StatusTooltip.Should().BeNull();
    }

    // ---- test 3: Breakpoint preserved ----

    [Fact]
    public void Breakpoint_preserved_when_no_diagnostic()
    {
        var root = new StateNode("__root__");
        var simple = new StateNode("Simple") { IsInitial = true, IsBreakpoint = true, Parent = root };
        root.Children.Add(simple);

        var asset = MakeAsset(root, new List<StateNode> { simple });
        _ = new HsmGraphModel(asset);

        // DiagnosticState stayed null, so breakpoint fallback drives State.
        simple.DiagnosticState.Should().BeNull();
        simple.State.Should().Be(NodeState.Warning);
        simple.StatusTooltip.Should().BeNull();
    }

    // ---- test 4: LastDiagnostics + event ----

    [Fact]
    public void LastDiagnostics_contains_diagnostics_for_broken_machine()
    {
        var root = new StateNode("__root__");
        var composite = new StateNode("Composite") { Parent = root };
        var child = new StateNode("Child") { Parent = composite };
        root.Children.Add(composite);
        composite.Children.Add(child);

        var asset = MakeAsset(root, new List<StateNode> { composite, child });
        var model = new HsmGraphModel(asset);

        model.LastDiagnostics.Should().NotBeEmpty();
        model.LastDiagnostics.Should().Contain(d =>
            d.Code == HsmDiagnosticCode.CompositeWithoutInitialChild &&
            d.Severity == HsmDiagnosticSeverity.Error);
    }

    [Fact]
    public void DiagnosticsRecomputed_fires_on_rebuild()
    {
        var root = new StateNode("__root__");
        var simple = new StateNode("Simple") { IsInitial = true, Parent = root };
        root.Children.Add(simple);

        var asset = MakeAsset(root, new List<StateNode> { simple });
        var model = new HsmGraphModel(asset);

        IReadOnlyList<HsmDiagnostic>? received = null;
        model.DiagnosticsRecomputed += d => received = d;

        // Trigger rebuild via MarkDirty.
        asset.MarkDirty();

        received.Should().NotBeNull();
        // Valid machine produces no diagnostics.
        received.Should().BeEmpty();
    }

    // ---- test 5: Region-conflict reaches renderer ----

    [Fact]
    public void Renderer_wiring_pushes_diagnostics()
    {
        var root = new StateNode("__root__");
        var renderer = new HsmRegionConflictsRenderer(
            MakeAsset(root, new List<StateNode>()));
        var list = new List<HsmDiagnostic>();
        renderer.SetDiagnostics(list);
        renderer.CurrentDiagnostics.Should().BeSameAs(list);
    }

    [Fact]
    public void Parallel_state_with_conflicting_lanes_produces_OutputLaneConflict()
    {
        var root = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        var rn0 = new RegionNode("R0") { RegionIndex = 0 };
        var rn1 = new RegionNode("R1") { RegionIndex = 1 };
        parallel.RegionNodes.Add(rn0);
        parallel.RegionNodes.Add(rn1);

        var ca = new StateNode("CA") { IsInitial = true, RegionIndex = 0, OutputLaneMask = 0x01, Parent = parallel };
        var cb = new StateNode("CB") { RegionIndex = 1, OutputLaneMask = 0x01, Parent = parallel };
        parallel.Children.Add(ca);
        parallel.Children.Add(cb);

        var asset = MakeAsset(root,
            new List<StateNode> { parallel, ca, cb },
            allRegions: new List<RegionNode> { rn0, rn1 });
        var model = new HsmGraphModel(asset);

        model.LastDiagnostics.Should().Contain(d =>
            d.Code == HsmDiagnosticCode.OutputLaneConflict &&
            d.Severity == HsmDiagnosticSeverity.Warning);
    }

    // ---- test 6: registration smoke ----

    [Fact]
    public void HsmAssetValidator_supported_kind_is_Hsm()
    {
        var validator = new HsmAssetValidator(null);
        validator.SupportedKind.Should().Be(AssetKind.Hsm);
    }

    [Fact]
    public void HsmAssetValidator_produces_diagnostics_for_broken_asset()
    {
        var root = new StateNode("__root__");
        var composite = new StateNode("Composite") { Parent = root };
        var child = new StateNode("Child") { Parent = composite };
        root.Children.Add(composite);
        composite.Children.Add(child);

        var asset = MakeAsset(root, new List<StateNode> { composite, child });
        var validator = new HsmAssetValidator(null);

        var diagnostics = validator.Validate(asset);
        diagnostics.Should().NotBeEmpty();
        diagnostics.Should().Contain(d =>
            d.Code == HsmDiagnosticCode.CompositeWithoutInitialChild.ToString() &&
            d.Severity == AssetDiagnosticSeverity.Error);
    }
}
