using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Renderers;
using Hrot.Hsm.Editor.Validation;
using NodeEditor.Core;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmRegionConflictsRendererTests
{
    private static HsmAsset MakeAsset(List<StateNode> states)
    {
        var root = new StateNode("__root__");
        foreach (var s in states) s.Parent = root;
        return new HsmAsset(
            Guid.NewGuid(), "T", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            root, states,
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());
    }

    private static HsmDiagnostic MakeConflict(Guid idA, Guid idB) =>
        new HsmDiagnostic(
            HsmDiagnosticCode.OutputLaneConflict,
            HsmDiagnosticSeverity.Error,
            "lane conflict",
            new List<Guid> { idA, idB });

    // FakeHitTestContext with identity viewport (graph-space == screen-space).
    private sealed class FakeHitCtx : IHitTestContext
    {
        public ViewportState Viewport { get; } = new ViewportState();
        public IGraphModel Graph => throw new NotSupportedException();
        public IReadOnlySet<NodeId> VisibleNodes { get; } = new HashSet<NodeId>();
        public IReadOnlySet<LinkId> VisibleLinks { get; } = new HashSet<LinkId>();
        public float Zoom => 1f;
    }

    [Fact]
    public void Id_is_hsm_region_conflicts()
    {
        var renderer = new HsmRegionConflictsRenderer(MakeAsset(new List<StateNode>()));
        renderer.Id.Should().Be("hsm.region_conflicts");
    }

    [Fact]
    public void Pass_is_AfterNodes()
    {
        var renderer = new HsmRegionConflictsRenderer(MakeAsset(new List<StateNode>()));
        renderer.Pass.Should().Be(CanvasRenderPass.AfterNodes);
    }

    [Fact]
    public void GlyphPositions_empty_when_no_diagnostics()
    {
        var renderer = new HsmRegionConflictsRenderer(MakeAsset(new List<StateNode>()));
        // _glyphPositions starts empty and stays empty without Render().
        renderer._glyphPositions.Should().BeEmpty();
    }

    [Fact]
    public void HitTest_returns_null_when_no_glyphs_recorded()
    {
        var renderer = new HsmRegionConflictsRenderer(MakeAsset(new List<StateNode>()));
        var hit = renderer.HitTest(new Vector2(100f, 100f), new FakeHitCtx());
        hit.Should().BeNull();
    }

    [Fact]
    public void HitTest_detects_glyph_after_manual_position_insert()
    {
        var renderer = new HsmRegionConflictsRenderer(MakeAsset(new List<StateNode>()));

        // Manually populate _glyphPositions (internal field exposed for tests).
        var graphPos = new Vector2(50f, 50f);
        renderer._glyphPositions.Add((graphPos, "conflict_key"));

        // With identity viewport, screen-space == graph-space.
        // Point exactly on the glyph should produce a hit.
        var hit = renderer.HitTest(graphPos, new FakeHitCtx());
        hit.Should().NotBeNull();
        hit!.Value.ElementKey.Should().Be("conflict_key");
        hit!.Value.Kind.Should().Be(CustomElementKind.Standalone);
    }

    [Fact]
    public void HitTest_misses_glyph_outside_hit_radius()
    {
        var renderer = new HsmRegionConflictsRenderer(MakeAsset(new List<StateNode>()));
        renderer._glyphPositions.Add((new Vector2(50f, 50f), "k"));

        // 9 px away is just outside the 8 px hit radius.
        var hit = renderer.HitTest(new Vector2(59f, 50f), new FakeHitCtx());
        hit.Should().BeNull();
    }

    [Fact]
    public void HitTest_returns_hit_at_edge_of_hit_radius()
    {
        var renderer = new HsmRegionConflictsRenderer(MakeAsset(new List<StateNode>()));
        renderer._glyphPositions.Add((new Vector2(50f, 50f), "k"));

        // Exactly 8 px away — should hit.
        var hit = renderer.HitTest(new Vector2(58f, 50f), new FakeHitCtx());
        hit.Should().NotBeNull();
    }
}
