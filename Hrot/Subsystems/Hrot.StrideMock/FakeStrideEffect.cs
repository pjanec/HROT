using System.Numerics;
using Hrot.IG.Components;

namespace Hrot.StrideMock;

/// <summary>
/// Mutable runtime representation of a visual effect entity (explosion, tracer, fire).
/// Updated each frame by <see cref="SyncFdpToStrideScript"/> from the ECS
/// <see cref="VisualEffectState"/> component.
/// </summary>
public sealed class FakeStrideEffect
{
    /// <summary>Effect type: Explosion, Tracer, or Fire.</summary>
    public EffectType Type { get; set; }

    /// <summary>World-space position of the effect origin.</summary>
    public Vector3 Position { get; set; }

    /// <summary>World-space endpoint for tracer line rendering. Zero for non-tracer effects.</summary>
    public Vector3 TracerEnd { get; set; }

    /// <summary>Current visual scale of the effect.</summary>
    public float Scale { get; set; }

    /// <summary>Opacity in [0,1]. Decreases as the effect ages toward expiry.</summary>
    public float Alpha { get; set; }
}
