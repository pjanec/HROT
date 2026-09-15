using System.Runtime.InteropServices;
using Fdp.Core;

// ST-027: this file lives in Hrot.Core, not Hrot.IG, while keeping the Hrot.IG.Components NAMESPACE --
// exactly as its four siblings already do (CullingState, IgHealthState, MapOverlayStyle, SelectionState in
// Hrot/Engine/Hrot.Core/Components/Map/). It was the ONLY one of the 15 gizmo-projector component types
// that sat in the Hrot.IG assembly, and MapSchemaPack (Hrot.Common) cannot reach it there: Hrot.IG already
// references Hrot.Common, so the reverse edge is a cycle. Uniform gizmo membership needs every host able to
// register all 15, so the outlier moved to where the others were. Namespace unchanged => no using changed,
// and the [ComponentId] values are unchanged, so nothing on disk or on the wire moves.
namespace Hrot.IG.Components;

/// <summary>Identifies the visual rendering type of a temporary effect entity.</summary>
public enum EffectType : byte
{
    /// <summary>Expanding fading circle representing a detonation or impact.</summary>
    Explosion = 0,

    /// <summary>Short-lived line segment drawn from a shooter to its target.</summary>
    Tracer = 1,
}

/// <summary>
/// ECS component holding the lifecycle and rendering state of a temporary visual
/// effect entity spawned by <c>Hrot.IG.Systems.EventToEffectSystem</c>.
///
/// <c>Hrot.IG.Systems.VisualEffectCleanupSystem</c> increments
/// <see cref="ElapsedTime"/> each frame and destroys the entity once
/// <see cref="IsExpired"/> returns <c>true</c>.
///
/// All duration and colour constants are from <see cref="VisualEffectStateConstants"/>
/// (§CODE-STANDARDS §1).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[ComponentId(GlobalComponentIds.VisualEffectState)]
public struct VisualEffectState
{
    /// <summary>Rendering type that drives effect visuals.</summary>
    public EffectType Type;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Total active lifetime of this effect in seconds.</summary>
    public float Duration;

    /// <summary>Seconds elapsed since this effect was spawned.</summary>
    public float ElapsedTime;

    // ── Colour (RGBA) ─────────────────────────────────────────────────────────

    /// <summary>Red channel of the base effect colour.</summary>
    public byte ColorR;

    /// <summary>Green channel of the base effect colour.</summary>
    public byte ColorG;

    /// <summary>Blue channel of the base effect colour.</summary>
    public byte ColorB;

    /// <summary>Alpha channel of the base effect colour at spawn (fades linearly over lifetime).</summary>
    public byte ColorA;

    // ── Scaling ───────────────────────────────────────────────────────────────

    /// <summary>Initial radius (world units) for explosions; line width multiplier for tracers.</summary>
    public float Scale;

    // ── Derived state ──────────────────────────────────────────────────────────

    /// <summary>
    /// <c>true</c> when <see cref="ElapsedTime"/> has reached or exceeded
    /// <see cref="Duration"/>; the entity should be destroyed this frame.
    /// </summary>
    public readonly bool IsExpired => ElapsedTime >= Duration;

    /// <summary>
    /// Linear fade factor in [0, 1]: 1.0 = fully opaque (just spawned),
    /// approaching 0 as the effect ages.
    /// </summary>
    public readonly float Alpha => Duration > 0f
        ? 1.0f - ElapsedTime / Duration
        : 0f;
}

/// <summary>
/// Companion component attached to <see cref="EffectType.Tracer"/> effect entities.
/// Stores the world-space end-point of the tracer line so the renderer can draw it.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[ComponentId(GlobalComponentIds.TracerTarget)]
public struct TracerTarget
{
    /// <summary>World-space X coordinate of the tracer line endpoint (target position).</summary>
    public float EndX;

    /// <summary>World-space Y coordinate of the tracer line endpoint (target position).</summary>
    public float EndY;
}
