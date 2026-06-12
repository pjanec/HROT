using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Renderers;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Renderers;

public sealed class HsmInitialArrowGeometryTests
{
    // ── Manual asset constructor (full control — no normalizer/compiler side-effects) ──

    private static HsmAsset BuildManualAsset(
        StateNode root,
        List<StateNode> allStates,
        List<RegionNode>? allRegions = null)
    {
        return new HsmAsset(
            Guid.NewGuid(), "Test", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            root,
            allStates,
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            allRegions ?? new List<RegionNode>(),
            new List<EventDefinition>());
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // CollectInitialMarkers tests
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollectInitialMarkers_CompositeWithInitialChild_ReturnsOneMarker()
    {
        // Tree: root -> A -> B (B is the initial child of A)
        var root   = new StateNode("__root__");
        var parent = new StateNode("A");
        var child  = new StateNode("B") { IsInitial = true };
        parent.Children.Add(child);
        child.Parent = parent;
        root.Children.Add(parent);
        parent.Parent = root;

        var asset = BuildManualAsset(root, new List<StateNode> { parent, child });

        var markers = HsmInitialArrowRenderer.CollectInitialMarkers(asset);

        markers.Should().HaveCount(1);
        markers[0].Container.Name.Should().Be("A");
        markers[0].InitialChild.Name.Should().Be("B");
        markers[0].RegionIndex.Should().Be(-1);
    }

    [Fact]
    public void CollectInitialMarkers_CompositeWithoutInitialChild_ReturnsZeroMarkers()
    {
        // Composite A has child B, but B.IsInitial == false.
        var root   = new StateNode("__root__");
        var parent = new StateNode("A");
        var child  = new StateNode("B"); // IsInitial remains false (default)
        parent.Children.Add(child);
        child.Parent = parent;
        root.Children.Add(parent);
        parent.Parent = root;

        var asset = BuildManualAsset(root, new List<StateNode> { parent, child });

        var markers = HsmInitialArrowRenderer.CollectInitialMarkers(asset);

        markers.Should().BeEmpty();
    }

    [Fact]
    public void CollectInitialMarkers_ParallelWithTwoRegionsEachWithInitialChild_ReturnsTwoMarkers()
    {
        // P is parallel with two regions, each with an InitialChild.
        var root   = new StateNode("__root__");
        var pState = new StateNode("P") { IsParallel = true };
        root.Children.Add(pState);
        pState.Parent = root;

        var childA = new StateNode("A") { Parent = pState };
        var childB = new StateNode("B") { Parent = pState };

        var r0 = new RegionNode("R0") { RegionIndex = 0, InitialChild = childA };
        var r1 = new RegionNode("R1") { RegionIndex = 1, InitialChild = childB };
        pState.RegionNodes.Add(r0);
        pState.RegionNodes.Add(r1);

        var allRegions = new List<RegionNode> { r0, r1 };
        var allStates  = new List<StateNode> { pState, childA, childB };

        var asset = BuildManualAsset(root, allStates, allRegions);

        var markers = HsmInitialArrowRenderer.CollectInitialMarkers(asset);

        markers.Should().HaveCount(2);
        markers.Should().ContainSingle(m => m.RegionIndex == 0 && m.InitialChild.Name == "A");
        markers.Should().ContainSingle(m => m.RegionIndex == 1 && m.InitialChild.Name == "B");

        foreach (var m in markers)
            m.Container.Name.Should().Be("P");
    }

    [Fact]
    public void CollectInitialMarkers_ParallelRegionWithNullInitialChild_Skipped()
    {
        // Parallel P with two regions; one has null InitialChild.
        var root   = new StateNode("__root__");
        var pState = new StateNode("P") { IsParallel = true };
        root.Children.Add(pState);
        pState.Parent = root;

        var childA = new StateNode("A") { Parent = pState };
        var childB = new StateNode("B") { Parent = pState };

        var regionWithChild = new RegionNode("R0") { RegionIndex = 0, InitialChild = childA };
        var regionNull      = new RegionNode("R1") { RegionIndex = 1, InitialChild = null };
        pState.RegionNodes.Add(regionWithChild);
        pState.RegionNodes.Add(regionNull);

        var allRegions = new List<RegionNode> { regionWithChild, regionNull };
        var allStates  = new List<StateNode> { pState, childA, childB };

        var asset = BuildManualAsset(root, allStates, allRegions);

        var markers = HsmInitialArrowRenderer.CollectInitialMarkers(asset);

        markers.Should().HaveCount(1);
        markers[0].InitialChild.Name.Should().Be("A");
        markers[0].RegionIndex.Should().Be(0);
        markers[0].Container.Name.Should().Be("P");
    }

    [Fact]
    public void CollectInitialMarkers_SyntheticRootSkipped()
    {
        // Root has child A (a composite with child B).
        // Root itself must never appear as a marker container.
        var root   = new StateNode("__root__");
        var parent = new StateNode("A");
        var child  = new StateNode("B") { IsInitial = true };
        parent.Children.Add(child);
        child.Parent = parent;
        root.Children.Add(parent);
        parent.Parent = root;

        var asset = BuildManualAsset(root, new List<StateNode> { parent, child });

        var markers = HsmInitialArrowRenderer.CollectInitialMarkers(asset);

        // A should be the only container.
        markers.Should().HaveCount(1);
        markers[0].Container.Name.Should().Be("A");
        markers.Should().NotContain(m => m.Container == asset.RootState);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // ComputeMarkerGeometry tests
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeMarkerGeometry_ReturnsExpectedValues()
    {
        var childPos  = new Vector2(100f, 200f);
        var childSize = new Vector2(120f, 40f);

        var (circleCenter, arrowStart, arrowEnd) =
            HsmInitialArrowRenderer.ComputeMarkerGeometry(childPos, childSize);

        // Child top-center X = 100 + 120 * 0.5 = 160
        arrowEnd.X.Should().Be(160f);
        arrowEnd.Y.Should().Be(200f);

        // Circle 24f above child top edge
        circleCenter.X.Should().Be(160f);
        circleCenter.Y.Should().Be(176f); // 200 - 24

        // Arrow start is the circle center (line from circle to child top)
        arrowStart.Should().Be(circleCenter);
    }
}
