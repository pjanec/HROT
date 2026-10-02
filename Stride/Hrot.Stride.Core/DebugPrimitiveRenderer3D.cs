#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Toolkit.Diagnostics.Gizmos;
using SNum = System.Numerics;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core;

/// <summary>
/// Stride 3-D renderer for <see cref="DebugPrimitive"/> spans (STR-P5-T1, design §11).
///
/// <para>
/// Mirrors the existing raylib <c>DebugPrimitiveRenderer2D</c> two-pass scheme, but resolves
/// into <b>3-D</b> world coordinates, swizzles every position/rotation through
/// <see cref="FdpStrideTransform"/> (FDP right-handed X=East/Y=North/Z=Up → Stride
/// left-handed X=East/Y=Up/Z=North), and emits the swizzled shapes through an
/// <see cref="IDebugDrawSink3D"/> rather than a GPU call directly.
/// </para>
///
/// <list type="number">
///   <item><b>Pass 1 — anchors.</b> Sweep the buffer and cache every
///     <see cref="DebugPrimitiveShape.SpatialAnchor"/> primitive by its
///     <see cref="DebugPrimitive.NetworkId"/> (world position + heading/pitch/roll in degrees).</item>
///   <item><b>Pass 2 — shapes.</b> For each drawable primitive resolve it against its anchor
///     (when <see cref="CoordinateSpace.EntityLocal"/>) into absolute FDP world coordinates —
///     writing the resolved transform into the primitive's spare payload in-place exactly like
///     the 2-D renderer — then swizzle the FDP coordinates to Stride space and emit a
///     <see cref="DebugDrawShape3D"/> / <see cref="DebugDrawLine3D"/> to the sink.</item>
/// </list>
///
/// <para>
/// <b>Headless-testable.</b> The two-pass resolution + anchor application + swizzle is pure CPU
/// work and is unit-tested against a synthetic primitive buffer with a capturing sink (no GPU).
/// </para>
///
/// <para>
/// <b>GPU-deferred draw ([VERIFY] result for Stride 4.2.1.2487).</b> Stride 4.2.1.2487 does
/// <b>not</b> ship an immediate-mode <c>Stride.DebugRendering</c> shape API
/// (no <c>ImmediateDebugRenderSystem</c> / <c>DebugShapes</c> type exists in
/// <c>Stride.Rendering.dll</c> at this version — only <c>Stride.Profiling.DebugTextSystem</c>
/// for text, and <c>Stride.Rendering.Compositing.DebugRenderer</c> as a compositor render
/// feature). The design §11 fallbacks therefore apply for the actual draw: a concrete
/// <see cref="IDebugDrawSink3D"/> implemented by adding a <c>DebugRenderer</c> render-stage to
/// the <c>GraphicsCompositor</c>, or by emitting dynamic <c>GeometricPrimitive</c> meshes. That
/// sink is GPU-bound and human-verified; this renderer's contract ends at the swizzled
/// <see cref="IDebugDrawSink3D"/> call.
/// </para>
/// </summary>
public sealed class DebugPrimitiveRenderer3D
{
    private const float DegToRad = MathF.PI / 180f;

    private readonly IDebugDrawSink3D _sink;

    // Reused across frames to avoid per-frame allocation on the hot path.
    private readonly Dictionary<long, SpatialAnchor3D> _anchors = new();

    /// <summary>
    /// The sink this renderer emits resolved+swizzled shapes to. Exposed so the host
    /// can call <see cref="IDebugDrawSink3D.BeginFrame"/> and
    /// <see cref="IDebugDrawSink3D.EndFrame"/> around each <see cref="Render"/> call.
    /// </summary>
    public IDebugDrawSink3D Sink => _sink;

    /// <param name="sink">
    /// The 3-D debug-draw sink the resolved+swizzled shapes are emitted to. In the live GPU
    /// app this is the pooled entity sink (<see cref="PooledEntityDebugDrawSink3D"/>); in tests
    /// it is a capturing fake. Must not be null.
    /// </param>
    public DebugPrimitiveRenderer3D(IDebugDrawSink3D sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <summary>
    /// Sweeps <paramref name="primitives"/> twice — Pass 1 caches anchors, Pass 2 resolves
    /// shapes against their anchor, swizzles via <see cref="FdpStrideTransform"/>, and emits
    /// each to the sink. Returns the number of drawable primitives emitted (anchors and
    /// non-visual meta-primitives are not counted).
    /// </summary>
    /// <param name="primitives">The <see cref="DebugPrimitive"/> span to render (e.g. one frame
    /// of the gizmo ProducerBuffer).</param>
    public int Render(ReadOnlySpan<DebugPrimitive> primitives)
    {
        // ── Pass 1: cache SpatialAnchors by NetworkId ─────────────────────────
        _anchors.Clear();
        foreach (ref readonly var prim in primitives)
        {
            if (prim.Shape == DebugPrimitiveShape.SpatialAnchor)
            {
                _anchors[prim.NetworkId] = new SpatialAnchor3D
                {
                    X        = prim.AnchorWorldX,
                    Y        = prim.AnchorWorldY,
                    Z        = prim.AnchorWorldZ,
                    YawRad   = prim.Heading * DegToRad,
                    PitchRad = prim.Pitch * DegToRad,
                    RollRad  = prim.Roll * DegToRad,
                };
            }
        }

        // ── Pass 2: resolve, swizzle, emit ────────────────────────────────────
        int emitted = 0;
        foreach (ref readonly var prim in primitives)
        {
            // Anchors and non-visual meta-primitives are never drawn directly.
            switch (prim.Shape)
            {
                case DebugPrimitiveShape.SpatialAnchor:
                case DebugPrimitiveShape.ContextMenuBinding:
                case DebugPrimitiveShape.InputCaptureBinding:
                case DebugPrimitiveShape.MainMenuBinding:
                case DebugPrimitiveShape.LayerControlMask:
                    continue;
            }

            DebugPrimitive resolved = prim;
            // SemanticShape needs the anchor's full 3-D position (including altitude Z), which the
            // 64-byte payload's Resolved* fields cannot hold (no Z slot — see DebugPrimitive). Carry
            // the resolved FDP altitude alongside the stamped X/Y/angles so emit needs no lookup.
            float semanticZ = 0f;

            if (prim.Space == CoordinateSpace.EntityLocal)
            {
                // EntityLocal primitives carry their anchor's NetworkId in AnchorIndex
                // (mirrors DebugPrimitiveRenderer2D's keying).
                if (!_anchors.TryGetValue(prim.AnchorIndex, out var anchor))
                    continue; // dangling anchor reference → skip (no anchor to resolve against)

                ResolveAgainstAnchor(ref resolved, in anchor);
                resolved.Space = CoordinateSpace.World;
                semanticZ = anchor.Z;
            }

            if (EmitWorld(in resolved, semanticZ))
                emitted++;
        }

        return emitted;
    }

    // ── Anchor resolution (mutates the primitive's payload in-place) ──────────

    /// <summary>
    /// Resolves an <see cref="CoordinateSpace.EntityLocal"/> primitive's payload into absolute
    /// FDP world coordinates by composing it with its cached anchor. Mirrors
    /// <c>DebugPrimitiveRenderer2D</c>: the local offset is rotated by the anchor yaw (about the
    /// FDP Up/Z axis, the ground-plane heading) and translated by the anchor world position.
    /// For <see cref="DebugPrimitiveShape.SemanticShape"/> the full resolved transform is stamped
    /// into the Resolved* spare-payload fields, exactly like the 2-D renderer.
    /// </summary>
    private static void ResolveAgainstAnchor(ref DebugPrimitive prim, in SpatialAnchor3D anchor)
    {
        float cos = MathF.Cos(anchor.YawRad);
        float sin = MathF.Sin(anchor.YawRad);

        switch (prim.Shape)
        {
            case DebugPrimitiveShape.Line:
                prim.LineStart = ApplyAnchor(in anchor, cos, sin, prim.LineStart);
                prim.LineEnd   = ApplyAnchor(in anchor, cos, sin, prim.LineEnd);
                break;

            case DebugPrimitiveShape.Arrow:
                prim.ArrowFrom = ApplyAnchor(in anchor, cos, sin, prim.ArrowFrom);
                prim.ArrowTo   = ApplyAnchor(in anchor, cos, sin, prim.ArrowTo);
                break;

            case DebugPrimitiveShape.Sphere:
                prim.SphereCenter = ApplyAnchor(in anchor, cos, sin, prim.SphereCenter);
                break;

            case DebugPrimitiveShape.SemanticShape:
                // Stamp the resolved world transform into the spare payload (in-place), so the
                // emit step needs zero further lookups. Mirrors DebugPrimitiveRenderer2D.
                prim.ResolvedWorldX   = anchor.X;
                prim.ResolvedWorldY   = anchor.Y;
                prim.ResolvedYawRad   = anchor.YawRad;
                prim.ResolvedPitchRad = anchor.PitchRad;
                prim.ResolvedRollRad  = anchor.RollRad;
                break;

            default:
                // Other shapes are anchored at their 2-D payload position; resolve in the
                // ground plane (Box2D/Text/Icon). 3-D shapes above are the common gizmo case.
                break;
        }
    }

    /// <summary>
    /// Rotates a local FDP offset by the anchor yaw about the FDP Up (Z) axis and translates
    /// by the anchor world position. Pitch/roll are carried on the anchor for full 3-D shapes
    /// (SemanticShape) but the line/sphere/arrow ground-plane offset uses heading-only rotation,
    /// matching the 2-D renderer's <c>ApplyAnchor2D</c> so the two renderers agree.
    /// </summary>
    private static SNum.Vector3 ApplyAnchor(in SpatialAnchor3D a, float cos, float sin, SNum.Vector3 local)
    {
        // FDP X=East, Y=North, Z=Up. Heading rotates about Up (Z): standard 2-D rotation of X/Y.
        float wx = a.X + cos * local.X - sin * local.Y;
        float wy = a.Y + sin * local.X + cos * local.Y;
        float wz = a.Z + local.Z;
        return new SNum.Vector3(wx, wy, wz);
    }

    // ── Emit (swizzle FDP → Stride, dispatch to sink) ─────────────────────────

    /// <summary>
    /// Swizzles a world-space (FDP) primitive into Stride space and emits it to the sink.
    /// Returns true if a drawable shape/line was emitted.
    /// </summary>
    private bool EmitWorld(in DebugPrimitive prim, float semanticZ)
    {
        var color = ToStrideColor(prim.Color);

        switch (prim.Shape)
        {
            case DebugPrimitiveShape.Line:
            {
                _sink.DrawLine(new DebugDrawLine3D(
                    FdpStrideTransform.ToStridePosition(prim.LineStart),
                    FdpStrideTransform.ToStridePosition(prim.LineEnd),
                    color,
                    ToStrideColor(prim.EndColor)));
                return true;
            }

            case DebugPrimitiveShape.Arrow:
            {
                // No dedicated arrow primitive in the sink contract; an arrow is a line from→to.
                _sink.DrawLine(new DebugDrawLine3D(
                    FdpStrideTransform.ToStridePosition(prim.ArrowFrom),
                    FdpStrideTransform.ToStridePosition(prim.ArrowTo),
                    color,
                    color));
                return true;
            }

            case DebugPrimitiveShape.Sphere:
            {
                _sink.DrawShape(new DebugDrawShape3D(
                    DebugDrawShapeKind.Sphere,
                    FdpStrideTransform.ToStridePosition(prim.SphereCenter),
                    SMath.Quaternion.Identity,
                    new SMath.Vector3(prim.SphereRadius, prim.SphereRadius, prim.SphereRadius),
                    color));
                return true;
            }

            case DebugPrimitiveShape.SemanticShape:
            {
                // Build the FDP world transform from the stamped Resolved* fields (+ carried
                // altitude Z, which has no payload slot), then swizzle.
                var fdpPos = new SNum.Vector3(prim.ResolvedWorldX, prim.ResolvedWorldY, semanticZ);
                var fdpRot = SNum.Quaternion.CreateFromYawPitchRoll(
                    prim.ResolvedYawRad, prim.ResolvedPitchRad, prim.ResolvedRollRad);

                float len = prim.LengthMeters > 0f ? prim.LengthMeters : 5f;
                float wid = prim.WidthMeters > 0f ? prim.WidthMeters : len * 0.5f;

                _sink.DrawShape(new DebugDrawShape3D(
                    DebugDrawShapeKind.Box,
                    FdpStrideTransform.ToStridePosition(fdpPos),
                    FdpStrideTransform.ToStrideRotation(fdpRot),
                    // Box extents in Stride space: length along FDP-North (→ Stride Z),
                    // width along FDP-East (→ Stride X), thin in height (FDP-Up → Stride Y).
                    new SMath.Vector3(wid, 0.5f, len),
                    color));
                return true;
            }

            default:
                // Box2D / Text / Icon / EntityBadge / StructInspector / MilStd2525 are 2-D-screen
                // or text shapes with no 3-D world body; not drawn by the 3-D renderer.
                return false;
        }
    }

    private static SMath.Color ToStrideColor(Rgba32 c) => new SMath.Color(c.R, c.G, c.B, c.A);

    /// <summary>Cached anchor entry built from a <see cref="DebugPrimitiveShape.SpatialAnchor"/>.</summary>
    private struct SpatialAnchor3D
    {
        public float X;        // FDP East
        public float Y;        // FDP North
        public float Z;        // FDP Up
        public float YawRad;
        public float PitchRad;
        public float RollRad;
    }
}

// ── Sink contract (the GPU-deferred boundary) ─────────────────────────────────

/// <summary>
/// The kind of 3-D debug shape emitted by <see cref="DebugPrimitiveRenderer3D"/>.
/// </summary>
public enum DebugDrawShapeKind
{
    /// <summary>A sphere (uniform scale = radius).</summary>
    Sphere,
    /// <summary>An oriented box (scale = full extents along each Stride axis).</summary>
    Box,
}

/// <summary>
/// A resolved, swizzled 3-D debug shape in <b>Stride</b> world space, ready for the GPU sink.
/// </summary>
public readonly struct DebugDrawShape3D
{
    /// <summary>The shape kind.</summary>
    public readonly DebugDrawShapeKind Kind;
    /// <summary>Shape centre in Stride world space (already swizzled from FDP).</summary>
    public readonly SMath.Vector3 Position;
    /// <summary>Shape orientation in Stride space (already swizzled from FDP).</summary>
    public readonly SMath.Quaternion Rotation;
    /// <summary>Shape scale/extents in Stride space.</summary>
    public readonly SMath.Vector3 Scale;
    /// <summary>Shape colour.</summary>
    public readonly SMath.Color Color;

    /// <summary>Constructs a swizzled 3-D shape.</summary>
    public DebugDrawShape3D(DebugDrawShapeKind kind, SMath.Vector3 position, SMath.Quaternion rotation, SMath.Vector3 scale, SMath.Color color)
    {
        Kind = kind;
        Position = position;
        Rotation = rotation;
        Scale = scale;
        Color = color;
    }
}

/// <summary>
/// A resolved, swizzled 3-D debug line in <b>Stride</b> world space, ready for the GPU sink.
/// </summary>
public readonly struct DebugDrawLine3D
{
    /// <summary>Line start in Stride world space (already swizzled from FDP).</summary>
    public readonly SMath.Vector3 Start;
    /// <summary>Line end in Stride world space (already swizzled from FDP).</summary>
    public readonly SMath.Vector3 End;
    /// <summary>Start colour.</summary>
    public readonly SMath.Color StartColor;
    /// <summary>End colour (gradient end; equals start for a solid line).</summary>
    public readonly SMath.Color EndColor;

    /// <summary>Constructs a swizzled 3-D line.</summary>
    public DebugDrawLine3D(SMath.Vector3 start, SMath.Vector3 end, SMath.Color startColor, SMath.Color endColor)
    {
        Start = start;
        End = end;
        StartColor = startColor;
        EndColor = endColor;
    }
}

/// <summary>
/// Sink for resolved+swizzled 3-D debug shapes (the GPU-deferred boundary, design §11).
///
/// <para>
/// <see cref="DebugPrimitiveRenderer3D"/> does the headless-testable two-pass resolution +
/// swizzle and calls this sink for every drawable primitive. The live GPU implementation
/// (<see cref="PooledEntityDebugDrawSink3D"/>) is human-verified; tests use a capturing fake.
/// </para>
///
/// <para>
/// <b>Optional per-frame lifecycle.</b> <see cref="BeginFrame"/> and <see cref="EndFrame"/> have
/// no-op default implementations so existing sinks (logging, test captures) do not need to
/// implement them. The pooled GPU sink overrides both to manage entity visibility.
/// </para>
/// </summary>
public interface IDebugDrawSink3D
{
    /// <summary>
    /// Called once at the start of a frame, before any <see cref="DrawLine"/> /
    /// <see cref="DrawShape"/> calls. Default: no-op.
    /// </summary>
    void BeginFrame() { }

    /// <summary>
    /// Called once at the end of a frame, after all <see cref="DrawLine"/> /
    /// <see cref="DrawShape"/> calls. Default: no-op.
    /// </summary>
    void EndFrame() { }

    /// <summary>Draws a swizzled 3-D line.</summary>
    void DrawLine(in DebugDrawLine3D line);

    /// <summary>Draws a swizzled 3-D shape (sphere/box).</summary>
    void DrawShape(in DebugDrawShape3D shape);
}
