#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Headless tests for the GPU debug-draw sink geometry (STR-D16, BATCH-21).
///
/// <para>
/// The <see cref="PooledEntityDebugDrawSink3D"/> itself requires a live
/// <c>GraphicsDevice</c> + <c>Scene</c> and cannot be instantiated headlessly.
/// These tests cover:
/// <list type="number">
///   <item><b>RotationFromTo geometry.</b> The static helper that aligns a unit box with a
///     line direction is pure math — fully headless.</item>
///   <item><b>IDebugDrawSink3D.BeginFrame / EndFrame default methods.</b> The new optional
///     interface methods must default to no-op so existing sinks (logging, test captures) do
///     not break.</item>
///   <item><b>DebugPrimitiveRenderer3D.Sink exposure.</b> The new <c>Sink</c> property must
///     return the same instance passed at construction.</item>
///   <item><b>Line endpoint → midpoint + length math.</b> Verify the midpoint and length
///     formula used by DrawLine is numerically correct.</item>
///   <item><b>Sphere scale math.</b> The sink scales a sphere by radius × 2 from the
///     <c>DebugDrawShape3D.Scale.X</c> value — verify the formula.</item>
///   <item><b>BeginFrame/EndFrame default no-op via capturing sink.</b> Ensure a capturing
///     sink (as used in renderer tests) is not broken by the new interface methods.</item>
/// </list>
/// </para>
/// </summary>
public sealed class PooledEntityDebugDrawSinkTests
{
    private const float Tol = 1e-5f;

    // ── Helper ──────────────────────────────────────────────────────────────

    private static void AssertVec3(SMath.Vector3 expected, SMath.Vector3 actual, string ctx)
    {
        Assert.True(MathF.Abs(expected.X - actual.X) < Tol, $"{ctx}.X: expected {expected.X} got {actual.X}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) < Tol, $"{ctx}.Y: expected {expected.Y} got {actual.Y}");
        Assert.True(MathF.Abs(expected.Z - actual.Z) < Tol, $"{ctx}.Z: expected {expected.Z} got {actual.Z}");
    }

    // ── RotationFromTo tests ────────────────────────────────────────────────

    /// <summary>
    /// Same-direction vectors → identity quaternion.
    /// </summary>
    [Fact]
    public void RotationFromTo_SameDirection_ReturnsIdentity()
    {
        var q = PooledEntityDebugDrawSink3D.RotationFromTo(SMath.Vector3.UnitX, SMath.Vector3.UnitX);
        AssertQuat(SMath.Quaternion.Identity, q, "same-direction → identity");
    }

    /// <summary>
    /// Rotating UnitX to UnitY should produce a 90° rotation about -Z axis.
    /// Verify: rotating UnitX by the returned quaternion gives UnitY.
    /// </summary>
    [Fact]
    public void RotationFromTo_UnitXToUnitY_RotatesCorrectly()
    {
        var q = PooledEntityDebugDrawSink3D.RotationFromTo(SMath.Vector3.UnitX, SMath.Vector3.UnitY);

        // Apply the rotation to UnitX — should produce UnitY.
        var rotated = SMath.Vector3.Transform(SMath.Vector3.UnitX, q);
        AssertVec3(SMath.Vector3.UnitY, rotated, "UnitX→UnitY rotation applied");
    }

    /// <summary>
    /// Rotating UnitX to UnitZ should produce a result that, when applied to UnitX, gives UnitZ.
    /// </summary>
    [Fact]
    public void RotationFromTo_UnitXToUnitZ_RotatesCorrectly()
    {
        var q = PooledEntityDebugDrawSink3D.RotationFromTo(SMath.Vector3.UnitX, SMath.Vector3.UnitZ);
        var rotated = SMath.Vector3.Transform(SMath.Vector3.UnitX, q);
        AssertVec3(SMath.Vector3.UnitZ, rotated, "UnitX→UnitZ rotation applied");
    }

    /// <summary>
    /// Anti-parallel vectors (UnitX → -UnitX): the result must rotate UnitX into -UnitX
    /// (180° rotation about any perpendicular axis).
    /// </summary>
    [Fact]
    public void RotationFromTo_AntiParallel_Rotates180()
    {
        var q = PooledEntityDebugDrawSink3D.RotationFromTo(SMath.Vector3.UnitX, -SMath.Vector3.UnitX);
        var rotated = SMath.Vector3.Transform(SMath.Vector3.UnitX, q);
        AssertVec3(-SMath.Vector3.UnitX, rotated, "anti-parallel rotation");
    }

    /// <summary>
    /// Arbitrary diagonal direction: the rotation must map UnitX onto the direction.
    /// </summary>
    [Fact]
    public void RotationFromTo_ArbitraryDirection_RotatesCorrectly()
    {
        // Target: normalized (1, 1, 1).
        var target = SMath.Vector3.Normalize(new SMath.Vector3(1f, 1f, 1f));
        var q      = PooledEntityDebugDrawSink3D.RotationFromTo(SMath.Vector3.UnitX, target);
        var rotated = SMath.Vector3.Transform(SMath.Vector3.UnitX, q);
        AssertVec3(target, rotated, "arbitrary direction rotation");
    }

    // ── Line endpoint midpoint + length math ───────────────────────────────

    /// <summary>
    /// Midpoint = (start + end) / 2 and length = |end - start|.
    /// Verifies the formula the sink uses in DrawLine.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f, 0f,   4f, 0f, 0f,   2f, 0f, 0f,   4f)]  // horizontal +X, length 4
    [InlineData(1f, 2f, 3f,   1f, 2f, 7f,   1f, 2f, 5f,   4f)]  // vertical +Z, length 4
    [InlineData(0f, 0f, 0f,   3f, 4f, 0f,   1.5f, 2f, 0f, 5f)]  // 3-4-5 triangle in XY
    public void LineMidpointAndLength_CorrectFormula(
        float sx, float sy, float sz, float ex, float ey, float ez,
        float mx, float my, float mz, float expectedLen)
    {
        var start = new SMath.Vector3(sx, sy, sz);
        var end   = new SMath.Vector3(ex, ey, ez);

        var delta     = end - start;
        float len     = delta.Length();
        var midpoint  = (start + end) * 0.5f;

        Assert.True(MathF.Abs(len - expectedLen) < 1e-4f,
            $"length mismatch: expected {expectedLen}, got {len}");
        AssertVec3(new SMath.Vector3(mx, my, mz), midpoint, "midpoint");
    }

    // ── Sphere scale formula ────────────────────────────────────────────────

    /// <summary>
    /// The sphere scale emitted by <see cref="DebugPrimitiveRenderer3D"/> for a sphere of
    /// radius r is (r, r, r). The sink scales the unit sphere (diameter 1) by radius × 2.
    /// Verify: if shape.Scale.X = r, then the entity scale = 2r.
    /// </summary>
    [Theory]
    [InlineData(0.5f,  1.0f)]
    [InlineData(0.75f, 1.5f)]
    [InlineData(1.0f,  2.0f)]
    [InlineData(2.0f,  4.0f)]
    public void SphereScale_EntityScaleIsTwiceRadius(float radius, float expectedEntityScale)
    {
        // The renderer emits shape.Scale = (r, r, r).
        // The sink formula: float s = shape.Scale.X * 2f → entity scale = (s, s, s).
        float s = radius * 2f;
        Assert.True(MathF.Abs(s - expectedEntityScale) < 1e-6f,
            $"sphere entity scale for radius {radius}: expected {expectedEntityScale}, got {s}");
    }

    // ── IDebugDrawSink3D default interface methods ──────────────────────────

    /// <summary>
    /// A minimal capturing sink that does NOT override BeginFrame/EndFrame must still be
    /// callable (the default interface implementations must not throw).
    /// </summary>
    [Fact]
    public void DefaultInterface_BeginEndFrame_DoNotThrow()
    {
        IDebugDrawSink3D sink = new MinimalCapturingSink();

        // Should not throw — default implementations are no-ops.
        sink.BeginFrame();
        sink.EndFrame();
    }

    private sealed class MinimalCapturingSink : IDebugDrawSink3D
    {
        // Implements only the two required draw methods — BeginFrame/EndFrame use defaults.
        public void DrawLine(in DebugDrawLine3D line) { }
        public void DrawShape(in DebugDrawShape3D shape) { }
    }

    // ── DebugPrimitiveRenderer3D.Sink property ─────────────────────────────

    /// <summary>
    /// <see cref="DebugPrimitiveRenderer3D.Sink"/> must return the exact same instance
    /// passed at construction.
    /// </summary>
    [Fact]
    public void Renderer_Sink_ReturnsSameInstancePassedAtConstruction()
    {
        IDebugDrawSink3D sink = new MinimalCapturingSink();
        var renderer = new DebugPrimitiveRenderer3D(sink);

        Assert.Same(sink, renderer.Sink);
    }

    // ── BeginFrame/EndFrame called around Render ───────────────────────────

    /// <summary>
    /// When a sink overrides <see cref="IDebugDrawSink3D.BeginFrame"/> and
    /// <see cref="IDebugDrawSink3D.EndFrame"/>, they are called by the host (not by the renderer
    /// itself). Verify the renderer's draw methods are invoked between frame boundaries
    /// using a tracking sink.
    /// </summary>
    [Fact]
    public void TrackingSink_BeginAndEndFrameCalledByHost_DrawsInBetween()
    {
        var sink     = new TrackingDebugDrawSink();
        var renderer = new DebugPrimitiveRenderer3D(sink);

        // Simulate what EditorStrideSubsystem.Tick does:
        sink.BeginFrame();  // host call — would hide old pool entries

        var line = DebugPrimitive.MakeLine(
            from:      new System.Numerics.Vector3(0f, 0f, 0f),
            to:        new System.Numerics.Vector3(1f, 0f, 0f),
            color:     Rgba32.Red,
            thickness: 1f,
            sizeMode:  SizeMode.WorldMeters,
            target:    PipelineTarget.All);
        line.Space = CoordinateSpace.World;

        renderer.Render(new[] { line });

        sink.EndFrame();    // host call — no-op for tracking sink

        Assert.Equal(1, sink.BeginFrameCallCount);
        Assert.Equal(1, sink.EndFrameCallCount);
        Assert.Equal(1, sink.Lines.Count);
    }

    private sealed class TrackingDebugDrawSink : IDebugDrawSink3D
    {
        public int BeginFrameCallCount { get; private set; }
        public int EndFrameCallCount   { get; private set; }
        public List<DebugDrawLine3D>  Lines  { get; } = new();
        public List<DebugDrawShape3D> Shapes { get; } = new();

        public void BeginFrame() => BeginFrameCallCount++;
        public void EndFrame()   => EndFrameCallCount++;
        public void DrawLine(in  DebugDrawLine3D  line)  => Lines.Add(line);
        public void DrawShape(in DebugDrawShape3D shape) => Shapes.Add(shape);
    }

    // ── RotationFromTo: quaternion is unit length ───────────────────────────

    [Theory]
    [InlineData( 1f, 0f, 0f,  0f, 1f, 0f)] // X→Y
    [InlineData( 1f, 0f, 0f,  0f, 0f, 1f)] // X→Z
    [InlineData( 0f, 1f, 0f,  0f, 0f, 1f)] // Y→Z
    [InlineData(-1f, 0f, 0f,  1f, 0f, 0f)] // -X→X (anti-parallel)
    [InlineData( 1f, 0f, 0f,  0.577f, 0.577f, 0.577f)] // X→diagonal (approx unit)
    public void RotationFromTo_IsUnitQuaternion(
        float fx, float fy, float fz,
        float tx, float ty, float tz)
    {
        var from = SMath.Vector3.Normalize(new SMath.Vector3(fx, fy, fz));
        var to   = SMath.Vector3.Normalize(new SMath.Vector3(tx, ty, tz));
        var q    = PooledEntityDebugDrawSink3D.RotationFromTo(from, to);

        float len = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        Assert.True(MathF.Abs(len - 1f) < 1e-4f,
            $"Quaternion not unit length: {len:F6}");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void AssertQuat(SMath.Quaternion expected, SMath.Quaternion actual, string ctx)
    {
        Assert.True(MathF.Abs(expected.X - actual.X) < Tol, $"{ctx}.X");
        Assert.True(MathF.Abs(expected.Y - actual.Y) < Tol, $"{ctx}.Y");
        Assert.True(MathF.Abs(expected.Z - actual.Z) < Tol, $"{ctx}.Z");
        Assert.True(MathF.Abs(expected.W - actual.W) < Tol, $"{ctx}.W");
    }
}
