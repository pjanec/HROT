using System;
using System.Numerics;
using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using NodeEditor.UI.Canvas;
using Xunit;

namespace NodeEditor.UI.Tests.Canvas;

/// <summary>
/// Tests that pin positions respect the graph's PinOrientation setting.
/// Uses <see cref="CanvasLayoutBuilder.ComputePinGraphPosition"/> and
/// <see cref="CanvasLayoutBuilder.PinCenterX"/> which are pure math — no ImGui dependency.
/// </summary>
public sealed class CanvasLayoutTests
{
    // Shared test geometry constants matching CanvasLayoutBuilder defaults.
    private const float HeaderHt    = 30f;
    private const float NodeW       = 200f;
    private const float NodeH       = 80f;
    private readonly Vector2        _graphPos = new(100f, 200f);

    // ── Vertical orientation ─────────────────────────────────────────────────

    [Fact]
    public void Layout_VerticalOrientation_OutputPinOnTopEdge_InputOnBottom()
    {
        // Single pin each — centered on the edge.
        var outPos = CanvasLayoutBuilder.ComputePinGraphPosition(
            PinOrientation.Vertical, PinDirection.Output,
            _graphPos, NodeW, NodeH, HeaderHt, index: 0, count: 1);
        var inPos = CanvasLayoutBuilder.ComputePinGraphPosition(
            PinOrientation.Vertical, PinDirection.Input,
            _graphPos, NodeW, NodeH, HeaderHt, index: 0, count: 1);

        // Output pin on the TOP edge — its Y is near the node top.
        outPos.Y.Should().BeApproximately(_graphPos.Y + CanvasLayoutBuilder.PinTopPadGu, 1f,
            "output pin should sit on the top edge of the node");
        // Input pin on the BOTTOM edge — its Y is near the node bottom.
        inPos.Y.Should().BeApproximately(_graphPos.Y + NodeH - CanvasLayoutBuilder.PinBottomPadGu, 1f,
            "input pin should sit on the bottom edge of the node");

        // Output.Y (top) < Input.Y (bottom) — vertical separation matches tree reading direction.
        outPos.Y.Should().BeLessThan(inPos.Y,
            "in Vertical orientation the output pin (top) must be above the input pin (bottom)");

        // Both centered on X.
        float expectCenterX = _graphPos.X + NodeW * 0.5f;
        outPos.X.Should().BeApproximately(expectCenterX, 1f,
            "single output pin should be horizontally centered on the top edge");
        inPos.X.Should().BeApproximately(expectCenterX, 1f,
            "single input pin should be horizontally centered on the bottom edge");
    }

    [Fact]
    public void Layout_VerticalOrientation_MultiplePins_SpreadAcrossWidth()
    {
        // Three output pins — should spread across the node width.
        var p0 = CanvasLayoutBuilder.ComputePinGraphPosition(
            PinOrientation.Vertical, PinDirection.Output,
            _graphPos, NodeW, NodeH, HeaderHt, index: 0, count: 3);
        var p1 = CanvasLayoutBuilder.ComputePinGraphPosition(
            PinOrientation.Vertical, PinDirection.Output,
            _graphPos, NodeW, NodeH, HeaderHt, index: 1, count: 3);
        var p2 = CanvasLayoutBuilder.ComputePinGraphPosition(
            PinOrientation.Vertical, PinDirection.Output,
            _graphPos, NodeW, NodeH, HeaderHt, index: 2, count: 3);

        // All on the same Y (top edge).
        p0.Y.Should().BeApproximately(p1.Y, 0.1f);
        p1.Y.Should().BeApproximately(p2.Y, 0.1f);

        // X increases left to right: p0.X < p1.X < p2.X.
        p0.X.Should().BeLessThan(p1.X);
        p1.X.Should().BeLessThan(p2.X);

        // All X within node horizontal bounds.
        float left  = _graphPos.X + CanvasLayoutBuilder.NodeHorizPadGu;
        float right = _graphPos.X + NodeW - CanvasLayoutBuilder.NodeHorizPadGu;
        p0.X.Should().BeInRange(left, right);
        p2.X.Should().BeInRange(left, right);
    }

    [Fact]
    public void Layout_VerticalOrientation_InputPinOnBottom_HasLargerY()
    {
        // For any node with both pins in vertical, input Y > output Y.
        for (int count = 1; count <= 4; count++)
        {
            var outPos = CanvasLayoutBuilder.ComputePinGraphPosition(
                PinOrientation.Vertical, PinDirection.Output,
                _graphPos, NodeW, NodeH, HeaderHt, index: 0, count: count);
            var inPos = CanvasLayoutBuilder.ComputePinGraphPosition(
                PinOrientation.Vertical, PinDirection.Input,
                _graphPos, NodeW, NodeH, HeaderHt, index: 0, count: count);

            outPos.Y.Should().BeLessThan(inPos.Y,
                $"for {count} pins, output (top) must be above input (bottom)");
        }
    }

    // ── Horizontal orientation (regression) ──────────────────────────────────

    [Fact]
    public void Layout_HorizontalOrientation_InputOnLeftEdge_OutputOnRightEdge()
    {
        var inPos = CanvasLayoutBuilder.ComputePinGraphPosition(
            PinOrientation.Horizontal, PinDirection.Input,
            _graphPos, NodeW, NodeH, HeaderHt, index: 0, count: 1);
        var outPos = CanvasLayoutBuilder.ComputePinGraphPosition(
            PinOrientation.Horizontal, PinDirection.Output,
            _graphPos, NodeW, NodeH, HeaderHt, index: 0, count: 1);

        // Input on LEFT edge — small X.
        inPos.X.Should().BeApproximately(_graphPos.X + CanvasLayoutBuilder.NodeHorizPadGu, 1f,
            "input pin should be on the left edge");
        // Output on RIGHT edge — large X.
        outPos.X.Should().BeApproximately(_graphPos.X + NodeW - CanvasLayoutBuilder.NodeHorizPadGu, 1f,
            "output pin should be on the right edge");

        outPos.X.Should().BeGreaterThan(inPos.X,
            "in Horizontal orientation the output pin is on the right, input on the left");
    }

    [Fact]
    public void Layout_HorizontalOrientation_Unchanged_DefaultIsHorizontal()
    {
        // Regression: default GraphKindDescriptor has Orientation = Horizontal.
        var defaultKind = new GraphKindDescriptor("test", "Test", false, false);
        defaultKind.Orientation.Should().Be(PinOrientation.Horizontal,
            "default orientation must be Horizontal so Blueprint/HSM are unchanged");
    }

    // ── PinCenterX helper ────────────────────────────────────────────────────

    [Fact]
    public void PinCenterX_SinglePin_ReturnsCenter()
    {
        float x = CanvasLayoutBuilder.PinCenterX(100f, 200f, index: 0, count: 1);
        x.Should().BeApproximately(200f, 0.1f); // 100 + 200/2 = 200
    }

    [Fact]
    public void PinCenterX_TwoPins_SpreadsEvenly()
    {
        float x0 = CanvasLayoutBuilder.PinCenterX(100f, 200f, index: 0, count: 2);
        float x1 = CanvasLayoutBuilder.PinCenterX(100f, 200f, index: 1, count: 2);

        // x0 at left margin, x1 at right margin.
        float left  = 100f + CanvasLayoutBuilder.NodeHorizPadGu;       // 112
        float right = 100f + 200f - CanvasLayoutBuilder.NodeHorizPadGu; // 288
        x0.Should().BeApproximately(left, 0.1f);
        x1.Should().BeApproximately(right, 0.1f);
    }

    [Fact]
    public void PinCenterX_Xpositions_WithinNodeBounds()
    {
        for (int count = 1; count <= 5; count++)
        {
            for (int i = 0; i < count; i++)
            {
                float x = CanvasLayoutBuilder.PinCenterX(100f, 200f, i, count);
                x.Should().BeInRange(100f, 300f,
                    $"pin {i}/{count} must stay within node X bounds [100, 300]");
            }
        }
    }
}
