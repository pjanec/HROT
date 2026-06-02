using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;
using SNum = System.Numerics;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Headless behavioural tests for <see cref="DebugPrimitiveRenderer3D"/> (STR-P5-T1, design §11).
///
/// <para>
/// Exercises the two-pass anchor-resolve + <see cref="FdpStrideTransform"/> swizzle against a
/// synthetic <see cref="DebugPrimitive"/> buffer with a capturing sink, asserting actual numeric
/// Stride-space values. The real GPU draw is deferred (no immediate-mode debug-shape API in
/// Stride 4.2.1.2487) and human-verified.
/// </para>
/// </summary>
public sealed class DebugPrimitiveRenderer3DTests
{
    private const float Tol = 1e-4f;

    // ── Capturing sink ─────────────────────────────────────────────────────
    private sealed class CapturingSink : IDebugDrawSink3D
    {
        public List<DebugDrawLine3D> Lines { get; } = new();
        public List<DebugDrawShape3D> Shapes { get; } = new();
        public void DrawLine(in DebugDrawLine3D line) => Lines.Add(line);
        public void DrawShape(in DebugDrawShape3D shape) => Shapes.Add(shape);
    }

    private static void AssertSVec3(SMath.Vector3 expected, SMath.Vector3 actual, string ctx)
    {
        Assert.True(MathF.Abs(expected.X - actual.X) < Tol, $"{ctx} X: expected {expected.X}, got {actual.X}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) < Tol, $"{ctx} Y: expected {expected.Y}, got {actual.Y}");
        Assert.True(MathF.Abs(expected.Z - actual.Z) < Tol, $"{ctx} Z: expected {expected.Z}, got {actual.Z}");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Live-struct binding (no hardcoded offsets / no hardcoded size)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BindsToLiveDebugPrimitiveStruct_NotHardcodedLayout()
    {
        // The renderer must compile-bind to the LIVE DebugPrimitive (named-field access), and the
        // struct must still be the 64-byte one-cache-line contract the renderer relies on. If the
        // struct changes shape, this test (and the renderer's named-field reads) move with it.
        Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());

        // Sanity: the named fields the renderer reads exist on the live struct (reflection — would
        // fail to compile-bind in the renderer if renamed, and fail here if removed/renamed).
        var t = typeof(DebugPrimitive);
        foreach (var f in new[] { "NetworkId", "AnchorWorldX", "AnchorWorldY", "AnchorWorldZ",
                                  "Heading", "Pitch", "Roll", "LineStart", "LineEnd",
                                  "SphereCenter", "SphereRadius", "AnchorIndex" })
            Assert.True(t.GetField(f) != null, $"live DebugPrimitive must expose field '{f}'");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Anchor + shape → absolute world transform, then swizzle
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnchorPlusSphere_ResolvesAbsoluteWorld_ThenSwizzlesToStride()
    {
        // Anchor at FDP world (10, 20, 3), heading 90° (yaw about Up/Z).
        var anchor = DebugPrimitive.MakeSpatialAnchor(
            networkId: 7, worldX: 10f, worldY: 20f, worldZ: 3f, headingDeg: 90f);

        // EntityLocal sphere offset (2,0,0) in the anchor's local frame, radius 0.5.
        var sphere = DebugPrimitive.MakeSphere(
            center: new SNum.Vector3(2f, 0f, 0f), radius: 0.5f, color: Rgba32.Red);
        sphere.Space = CoordinateSpace.EntityLocal;
        sphere.AnchorIndex = 7; // resolves against anchor netId 7

        var sink = new CapturingSink();
        var sut = new DebugPrimitiveRenderer3D(sink);

        int emitted = sut.Render(new[] { anchor, sphere });

        Assert.Equal(1, emitted);
        var shape = Assert.Single(sink.Shapes);
        Assert.Equal(DebugDrawShapeKind.Sphere, shape.Kind);

        // Resolve by hand: yaw 90° about FDP Up rotates local +East(X) → +North(Y).
        //   wx = 10 + cos90*2 - sin90*0 = 10 + 0  = 10
        //   wy = 20 + sin90*2 + cos90*0 = 20 + 2  = 22
        //   wz = 3 + 0 = 3
        // FDP absolute world center = (10, 22, 3).
        // Swizzle to Stride = (fdp.X, fdp.Z, fdp.Y) = (10, 3, 22).
        AssertSVec3(new SMath.Vector3(10f, 3f, 22f), shape.Position, "sphere center swizzled");

        // Radius preserved as uniform scale.
        AssertSVec3(new SMath.Vector3(0.5f, 0.5f, 0.5f), shape.Scale, "sphere radius scale");

        // Color preserved.
        Assert.Equal((byte)255, shape.Color.R);
        Assert.Equal((byte)0, shape.Color.G);
        Assert.Equal((byte)0, shape.Color.B);
    }

    [Fact]
    public void WorldSpaceSphere_NoAnchorNeeded_SwizzlesDirectly()
    {
        // A World-space sphere (the default for MakeSphere) is emitted with a direct swizzle.
        var sphere = DebugPrimitive.MakeSphere(
            center: new SNum.Vector3(1f, 2f, 5f), radius: 1f, color: Rgba32.Green);
        // MakeSphere leaves Space=World (0) by default.

        var sink = new CapturingSink();
        new DebugPrimitiveRenderer3D(sink).Render(new[] { sphere });

        var shape = Assert.Single(sink.Shapes);
        // Stride = (fdp.X, fdp.Z, fdp.Y) = (1, 5, 2).
        AssertSVec3(new SMath.Vector3(1f, 5f, 2f), shape.Position, "world sphere swizzle");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Line endpoints resolve + swizzle
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnchorPlusLine_BothEndpoints_ResolveAndSwizzle()
    {
        // Anchor at FDP (0, 0, 0), heading 0° → identity rotation, pure translation by origin.
        // Use a non-trivial anchor to prove translation is applied.
        var anchor = DebugPrimitive.MakeSpatialAnchor(
            networkId: 3, worldX: 5f, worldY: 0f, worldZ: 1f, headingDeg: 0f);

        var line = DebugPrimitive.MakeLine(
            from: new SNum.Vector3(0f, 0f, 0f),
            to:   new SNum.Vector3(0f, 4f, 0f),
            color: Rgba32.White);
        line.Space = CoordinateSpace.EntityLocal;
        line.AnchorIndex = 3;

        var sink = new CapturingSink();
        new DebugPrimitiveRenderer3D(sink).Render(new[] { anchor, line });

        var emittedLine = Assert.Single(sink.Lines);
        // heading 0 → cos=1,sin=0 → world start = (5,0,1), world end = (5,4,1).
        // Swizzle start (5,0,1) → Stride (5,1,0). end (5,4,1) → Stride (5,1,4).
        AssertSVec3(new SMath.Vector3(5f, 1f, 0f), emittedLine.Start, "line start swizzle");
        AssertSVec3(new SMath.Vector3(5f, 1f, 4f), emittedLine.End, "line end swizzle");
    }

    [Fact]
    public void WorldLine_SwizzlesEachEndpoint_AndGradientColors()
    {
        var line = DebugPrimitive.MakeLine(
            from: new SNum.Vector3(1f, 0f, 0f),
            to:   new SNum.Vector3(0f, 0f, 9f),
            color: Rgba32.Red);
        line.EndColor = Rgba32.Green; // distinct gradient end

        var sink = new CapturingSink();
        new DebugPrimitiveRenderer3D(sink).Render(new[] { line });

        var emitted = Assert.Single(sink.Lines);
        AssertSVec3(new SMath.Vector3(1f, 0f, 0f), emitted.Start, "world line start"); // (1,0,0)→(1,0,0)
        AssertSVec3(new SMath.Vector3(0f, 9f, 0f), emitted.End, "world line end");     // (0,0,9)→(0,9,0)
        Assert.Equal((byte)255, emitted.StartColor.R);
        Assert.Equal((byte)255, emitted.EndColor.G);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Two-pass ordering + filtering
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Pass1CachesAnchor_EvenWhenAnchorAppearsAfterShape()
    {
        // Shape listed BEFORE its anchor; the two-pass scheme must still resolve it
        // (Pass 1 caches all anchors before Pass 2 resolves any shape).
        var sphere = DebugPrimitive.MakeSphere(new SNum.Vector3(0f, 0f, 0f), 1f, Rgba32.Yellow);
        sphere.Space = CoordinateSpace.EntityLocal;
        sphere.AnchorIndex = 99;

        var anchor = DebugPrimitive.MakeSpatialAnchor(99, 7f, 8f, 9f, 0f);

        var sink = new CapturingSink();
        new DebugPrimitiveRenderer3D(sink).Render(new[] { sphere, anchor }); // shape first

        var shape = Assert.Single(sink.Shapes);
        // local (0,0,0) at anchor (7,8,9) → world (7,8,9) → Stride (7,9,8).
        AssertSVec3(new SMath.Vector3(7f, 9f, 8f), shape.Position, "shape-before-anchor resolve");
    }

    [Fact]
    public void DanglingAnchorReference_IsSkipped_NotEmitted()
    {
        var sphere = DebugPrimitive.MakeSphere(new SNum.Vector3(0f, 0f, 0f), 1f, Rgba32.Red);
        sphere.Space = CoordinateSpace.EntityLocal;
        sphere.AnchorIndex = 12345; // no such anchor in the buffer

        var sink = new CapturingSink();
        int emitted = new DebugPrimitiveRenderer3D(sink).Render(new[] { sphere });

        Assert.Equal(0, emitted);
        Assert.Empty(sink.Shapes);
        Assert.Empty(sink.Lines);
    }

    [Fact]
    public void AnchorAndMetaPrimitives_AreNeverDrawn()
    {
        var anchor = DebugPrimitive.MakeSpatialAnchor(1, 0f, 0f, 0f, 0f);
        var menu = DebugPrimitive.MakeContextMenuBinding(1, 0xABCD);
        var mainMenu = DebugPrimitive.MakeMainMenuBinding(0x1234);

        var sink = new CapturingSink();
        int emitted = new DebugPrimitiveRenderer3D(sink).Render(new[] { anchor, menu, mainMenu });

        Assert.Equal(0, emitted);
        Assert.Empty(sink.Shapes);
        Assert.Empty(sink.Lines);
    }

    [Fact]
    public void Render_IsRepeatable_AcrossFrames_NoStaleAnchorLeak()
    {
        var sut = new DebugPrimitiveRenderer3D(new CapturingSink());

        // Frame 1: anchor 5 present.
        var anchor = DebugPrimitive.MakeSpatialAnchor(5, 1f, 1f, 1f, 0f);
        var s1 = DebugPrimitive.MakeSphere(new SNum.Vector3(0f, 0f, 0f), 1f, Rgba32.Red);
        s1.Space = CoordinateSpace.EntityLocal; s1.AnchorIndex = 5;
        Assert.Equal(1, sut.Render(new[] { anchor, s1 }));

        // Frame 2: anchor 5 ABSENT → the shape must NOT resolve against last frame's cached anchor.
        var s2 = DebugPrimitive.MakeSphere(new SNum.Vector3(0f, 0f, 0f), 1f, Rgba32.Red);
        s2.Space = CoordinateSpace.EntityLocal; s2.AnchorIndex = 5;
        Assert.Equal(0, sut.Render(new[] { s2 }));
    }
}
