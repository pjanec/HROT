using System;
using System.Collections.Generic;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Debug;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Renderers;
using NodeEditor.Core.Canvas;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmBreakpointRendererTests
{
    // Minimal asset factory: root + given states + given transitions.
    private static HsmAsset MakeAsset(
        List<StateNode> states,
        List<TransitionNode>? transitions = null)
    {
        var root = new StateNode("__root__");
        foreach (var s in states) s.Parent = root;

        return new HsmAsset(
            Guid.NewGuid(), "TestAsset", "", false, "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            root,
            states,
            transitions ?? new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());
    }

    [Fact]
    public void Id_is_hsm_breakpoint_gutter()
    {
        var asset = MakeAsset(new List<StateNode>());
        var renderer = new HsmBreakpointGutterRenderer(asset);
        renderer.Id.Should().Be("hsm.breakpoint_gutter");
    }

    [Fact]
    public void Pass_is_AfterNodes()
    {
        var asset = MakeAsset(new List<StateNode>());
        var renderer = new HsmBreakpointGutterRenderer(asset);
        renderer.Pass.Should().Be(CanvasRenderPass.AfterNodes);
    }

    [Fact]
    public void CountBreakpoints_returns_zero_when_no_session()
    {
        var asset = MakeAsset(new List<StateNode>());
        var renderer = new HsmBreakpointGutterRenderer(asset);
        var (states, trans) = renderer.CountBreakpoints();
        states.Should().Be(0);
        trans.Should().Be(0);
    }

    [Fact]
    public void CountBreakpoints_counts_state_breakpoint()
    {
        var state = new StateNode("A") { StableId = Guid.NewGuid() };
        var asset = MakeAsset(new List<StateNode> { state });

        var session = new FakeHsmSession();
        // Manually add a breakpoint pointing at this state's StableId.
        session.AddBreakpoint(new Breakpoint(
            new BreakpointId(1),
            asset.AssetId,
            state.StableId,
            0,
            true,
            "A"));

        var renderer = new HsmBreakpointGutterRenderer(asset);
        renderer.SetSession(session);

        var (stateDots, transDots) = renderer.CountBreakpoints();
        stateDots.Should().Be(1);
        transDots.Should().Be(0);
    }

    [Fact]
    public void CountBreakpoints_counts_transition_breakpoint()
    {
        var stateA = new StateNode("A") { StableId = Guid.NewGuid() };
        var stateB = new StateNode("B") { StableId = Guid.NewGuid() };
        var transition = new TransitionNode
        {
            VisualId = Guid.NewGuid(),
            Source   = stateA,
            Target   = stateB,
        };
        var asset = MakeAsset(
            new List<StateNode> { stateA, stateB },
            new List<TransitionNode> { transition });

        var session = new FakeHsmSession();
        session.AddBreakpoint(new Breakpoint(
            new BreakpointId(1),
            asset.AssetId,
            transition.VisualId,
            0,
            true,
            "A->B"));

        var renderer = new HsmBreakpointGutterRenderer(asset);
        renderer.SetSession(session);

        var (stateDots, transDots) = renderer.CountBreakpoints();
        stateDots.Should().Be(0);
        transDots.Should().Be(1);
    }

    [Fact]
    public void CountBreakpoints_ignores_disabled_breakpoints()
    {
        var state = new StateNode("A") { StableId = Guid.NewGuid() };
        var asset = MakeAsset(new List<StateNode> { state });

        var session = new FakeHsmSession();
        session.AddBreakpoint(new Breakpoint(
            new BreakpointId(1),
            asset.AssetId,
            state.StableId,
            0,
            false,   // disabled
            "A"));

        var renderer = new HsmBreakpointGutterRenderer(asset);
        renderer.SetSession(session);

        var (stateDots, transDots) = renderer.CountBreakpoints();
        stateDots.Should().Be(0);
        transDots.Should().Be(0);
    }

    [Fact]
    public void CountBreakpoints_ignores_breakpoints_for_other_asset()
    {
        var state = new StateNode("A") { StableId = Guid.NewGuid() };
        var asset = MakeAsset(new List<StateNode> { state });

        var session = new FakeHsmSession();
        // Use a different AssetId — should be ignored.
        session.AddBreakpoint(new Breakpoint(
            new BreakpointId(1),
            Guid.NewGuid(),          // wrong asset
            state.StableId,
            0,
            true,
            "A"));

        var renderer = new HsmBreakpointGutterRenderer(asset);
        renderer.SetSession(session);

        var (stateDots, transDots) = renderer.CountBreakpoints();
        stateDots.Should().Be(0);
        transDots.Should().Be(0);
    }

    [Fact]
    public void CountBreakpoints_counts_mixed_state_and_transition_breakpoints()
    {
        var stateA = new StateNode("A") { StableId = Guid.NewGuid() };
        var stateB = new StateNode("B") { StableId = Guid.NewGuid() };
        var transition = new TransitionNode
        {
            VisualId = Guid.NewGuid(),
            Source   = stateA,
            Target   = stateB,
        };
        var asset = MakeAsset(
            new List<StateNode> { stateA, stateB },
            new List<TransitionNode> { transition });

        var session = new FakeHsmSession();
        session.AddBreakpoint(new Breakpoint(new BreakpointId(1), asset.AssetId, stateA.StableId,    0, true, "A"));
        session.AddBreakpoint(new Breakpoint(new BreakpointId(2), asset.AssetId, transition.VisualId, 0, true, "A->B"));

        var renderer = new HsmBreakpointGutterRenderer(asset);
        renderer.SetSession(session);

        var (stateDots, transDots) = renderer.CountBreakpoints();
        stateDots.Should().Be(1);
        transDots.Should().Be(1);
    }
}
