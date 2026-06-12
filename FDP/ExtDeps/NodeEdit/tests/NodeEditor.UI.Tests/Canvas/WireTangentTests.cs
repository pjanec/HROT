using System;
using System.Numerics;
using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Canvas;
using Xunit;

namespace NodeEditor.UI.Tests.Canvas;

/// <summary>
/// Tests that wire bezier control points (tangents) leave/enter pins along the
/// graph's PinOrientation axis: horizontally for Blueprint/HSM, vertically for
/// BTree. Pure math via <see cref="HitTester.WireTangents"/> — no ImGui.
/// </summary>
public sealed class WireTangentTests
{
    // From/output pin at top of child, To/input pin at bottom of parent above it,
    // i.e. a (output) is LOWER on screen (larger Y) than b (input).
    private static readonly Vector2 _a = new(300f, 400f); // output pin (child top)
    private static readonly Vector2 _b = new(320f, 100f); // input pin (parent bottom)

    [Fact]
    public void Vertical_TangentsLeaveAndEnterAlongY_NotX()
    {
        var (c1, c2) = HitTester.WireTangents(_a, _b, PinOrientation.Vertical);

        // Control points share the endpoint X (no sideways sprout).
        c1.X.Should().BeApproximately(_a.X, 0.01f, "vertical wire must not bulge sideways from the output pin");
        c2.X.Should().BeApproximately(_b.X, 0.01f, "vertical wire must not bulge sideways into the input pin");

        // Output pin (a, lower) faces UP -> its control point has a smaller Y than a.
        c1.Y.Should().BeLessThan(_a.Y, "output pin on the top edge faces up");
        // Input pin (b, upper) faces DOWN -> its control point has a larger Y than b.
        c2.Y.Should().BeGreaterThan(_b.Y, "input pin on the bottom edge faces down");
    }

    [Fact]
    public void Vertical_TangentMagnitudeScalesWithVerticalGap()
    {
        var near = HitTester.WireTangents(new Vector2(0, 0), new Vector2(0, -40), PinOrientation.Vertical);
        var far  = HitTester.WireTangents(new Vector2(0, 0), new Vector2(0, -400), PinOrientation.Vertical);

        float nearOffset = MathF.Abs(near.c1.Y);      // |c1.Y - a.Y|, a.Y = 0
        float farOffset  = MathF.Abs(far.c1.Y);

        // Short gap clamps to the 50px floor; long gap is proportionally larger.
        nearOffset.Should().BeApproximately(50f, 0.5f);
        farOffset.Should().BeGreaterThan(nearOffset);
    }

    [Fact]
    public void Horizontal_Unchanged_TangentsLeaveAlongX()
    {
        // Default orientation is Horizontal; behavior must match the original.
        var (c1, c2) = HitTester.WireTangents(_a, _b);

        c1.Y.Should().BeApproximately(_a.Y, 0.01f, "horizontal wire keeps the output pin Y");
        c2.Y.Should().BeApproximately(_b.Y, 0.01f, "horizontal wire keeps the input pin Y");
        c1.X.Should().BeGreaterThan(_a.X, "output pin faces right");
        c2.X.Should().BeLessThan(_b.X, "input pin faces left");
    }
}
