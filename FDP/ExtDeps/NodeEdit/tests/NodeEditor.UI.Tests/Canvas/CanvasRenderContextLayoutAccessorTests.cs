using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using NodeEditor.Primitives;
using NodeEditor.UI.Canvas;
using Xunit;

namespace NodeEditor.UI.Tests.Canvas;

/// <summary>
/// Tests for the TryGetNodeScreenRect / TryGetPinScreenPosition accessors added
/// to CanvasRenderContextImpl in RHS-01.
///
/// Approach: construct a CanvasRenderContextImpl and seed its internal _layout
/// field directly (possible because NodeEditor.UI has InternalsVisibleTo on this
/// test project). This avoids wiring a full CanvasRenderer / ImGui frame — the
/// accessors are pure dictionary lookups and the unit under test is exactly those
/// two methods.
/// </summary>
public sealed class CanvasRenderContextLayoutAccessorTests
{
    private static CanvasRenderContextImpl MakeCtxWithLayout(
        Dictionary<NodeId, RectF>? nodeRects = null,
        Dictionary<PinId, Vector2>? pinPositions = null)
    {
        var ctx = new CanvasRenderContextImpl();
        var layout = new CanvasLayout();

        if (nodeRects != null)
            foreach (var (id, rect) in nodeRects)
                layout.NodeScreenRects[id] = rect;

        if (pinPositions != null)
            foreach (var (id, pos) in pinPositions)
                layout.PinScreenPositions[id] = pos;

        // Set the internal field directly — InternalsVisibleTo grants access.
        ctx._layout = layout;
        return ctx;
    }

    // ── TryGetNodeScreenRect ──────────────────────────────────────────────────

    [Fact]
    public void TryGetNodeScreenRect_returns_true_and_correct_rect_for_known_node()
    {
        var nodeId   = NodeId.NewId();
        var expected = new RectF(new Vector2(10f, 20f), new Vector2(160f, 64f));
        var ctx      = MakeCtxWithLayout(nodeRects: new Dictionary<NodeId, RectF> { [nodeId] = expected });

        bool found = ctx.TryGetNodeScreenRect(nodeId, out var result);

        found.Should().BeTrue();
        result.Should().Be(expected);
    }

    [Fact]
    public void TryGetNodeScreenRect_returns_false_and_default_for_unknown_node()
    {
        var ctx = MakeCtxWithLayout(); // empty layout

        bool found = ctx.TryGetNodeScreenRect(NodeId.NewId(), out var result);

        found.Should().BeFalse();
        result.Should().Be(default(RectF));
    }

    [Fact]
    public void TryGetNodeScreenRect_returns_false_when_layout_is_null()
    {
        var ctx = new CanvasRenderContextImpl(); // _layout not set → remains null

        bool found = ctx.TryGetNodeScreenRect(NodeId.NewId(), out var result);

        found.Should().BeFalse();
        result.Should().Be(default(RectF));
    }

    [Fact]
    public void TryGetNodeScreenRect_returns_correct_rect_for_each_of_multiple_nodes()
    {
        var id1 = NodeId.NewId();
        var id2 = NodeId.NewId();
        var rect1 = new RectF(new Vector2(0f, 0f),   new Vector2(100f, 50f));
        var rect2 = new RectF(new Vector2(200f, 0f), new Vector2(120f, 60f));
        var ctx = MakeCtxWithLayout(nodeRects: new Dictionary<NodeId, RectF> { [id1] = rect1, [id2] = rect2 });

        ctx.TryGetNodeScreenRect(id1, out var r1).Should().BeTrue();
        ctx.TryGetNodeScreenRect(id2, out var r2).Should().BeTrue();
        r1.Should().Be(rect1);
        r2.Should().Be(rect2);
        ctx.TryGetNodeScreenRect(NodeId.NewId(), out _).Should().BeFalse();
    }

    // ── TryGetPinScreenPosition ───────────────────────────────────────────────

    [Fact]
    public void TryGetPinScreenPosition_returns_true_and_correct_pos_for_known_pin()
    {
        var pinId    = new PinId(Guid.NewGuid());
        var expected = new Vector2(55f, 120f);
        var ctx      = MakeCtxWithLayout(pinPositions: new Dictionary<PinId, Vector2> { [pinId] = expected });

        bool found = ctx.TryGetPinScreenPosition(pinId, out var result);

        found.Should().BeTrue();
        result.Should().Be(expected);
    }

    [Fact]
    public void TryGetPinScreenPosition_returns_false_and_default_for_unknown_pin()
    {
        var ctx = MakeCtxWithLayout(); // empty layout

        bool found = ctx.TryGetPinScreenPosition(new PinId(Guid.NewGuid()), out var result);

        found.Should().BeFalse();
        result.Should().Be(default(Vector2));
    }

    [Fact]
    public void TryGetPinScreenPosition_returns_false_when_layout_is_null()
    {
        var ctx = new CanvasRenderContextImpl(); // _layout not set → remains null

        bool found = ctx.TryGetPinScreenPosition(new PinId(Guid.NewGuid()), out var result);

        found.Should().BeFalse();
        result.Should().Be(default(Vector2));
    }

    [Fact]
    public void TryGetPinScreenPosition_returns_correct_pos_for_each_of_multiple_pins()
    {
        var p1 = new PinId(Guid.NewGuid());
        var p2 = new PinId(Guid.NewGuid());
        var pos1 = new Vector2(10f, 30f);
        var pos2 = new Vector2(200f, 80f);
        var ctx = MakeCtxWithLayout(pinPositions: new Dictionary<PinId, Vector2> { [p1] = pos1, [p2] = pos2 });

        ctx.TryGetPinScreenPosition(p1, out var r1).Should().BeTrue();
        ctx.TryGetPinScreenPosition(p2, out var r2).Should().BeTrue();
        r1.Should().Be(pos1);
        r2.Should().Be(pos2);
        ctx.TryGetPinScreenPosition(new PinId(Guid.NewGuid()), out _).Should().BeFalse();
    }
}
