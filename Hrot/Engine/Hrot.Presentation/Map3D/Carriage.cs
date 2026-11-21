using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1084 (docs/DESIGN_Map_3D_Mode.md §6m C-4) — the JUMP DOWN, a drawing: while a man rides an exposed seat the layer tells it where
/// his hips are in the world; the first frame he is free again, if they were more than <see cref="MinDrop"/> above where his standing
/// hips now are, he is drawn that much higher, falling (in free fall, at least <see cref="MinSeconds"/>), and lands crouched for
/// <see cref="LandSeconds"/>. A man who was not seen riding just before (a jump in time) simply stands there.
/// </summary>
public sealed class DismountDrop
{
    /// <summary>Drops shorter than this are a step down, not a jump (m).</summary>
    public const float MinDrop = 0.5f;
    /// <summary>The shortest fall drawn, and how long he stays crouched after landing (s, sim time).</summary>
    public const float MinSeconds = 0.3f, LandSeconds = 0.3f;
    /// <summary>A ride further back than this was not seen ending (s).</summary>
    public const double SeenWithin = 0.5;

    private readonly Dictionary<Entity, (double At, float HipsZ)> _riding = new();
    private readonly Dictionary<Entity, (double At, float Height)> _drops = new();
    private readonly List<Entity> _gone = new();

    /// <summary>He rides this frame, his hips at <paramref name="hipsZ"/> (world).</summary>
    public void Riding(Entity e, float hipsZ, double now)
    {
        _riding[e] = (now, hipsZ);
        _drops.Remove(e);
    }

    /// <summary>He is free this frame, his standing hips at <paramref name="standingHipsZ"/>: how much higher to draw him (m) and whether
    /// he is in the jump (falling or just landed — drawn crouched).</summary>
    public (float Lift, bool Jumping) Free(Entity e, float standingHipsZ, double now)
    {
        if (_riding.Remove(e, out var ride) && now >= ride.At && now - ride.At <= SeenWithin)
        {
            float height = ride.HipsZ - standingHipsZ;
            if (height > MinDrop) _drops[e] = (now, height);
        }
        if (!_drops.TryGetValue(e, out var drop)) return (0f, false);
        float fall = FallSeconds(drop.Height);
        float t = (float)((now - drop.At) / fall);
        if (t < 1f) return (drop.Height * (1f - t * t), true);   // free fall: the height left goes as 1 − t²
        if (now - drop.At <= fall + LandSeconds) return (0f, true);
        _drops.Remove(e);
        return (0f, false);
    }

    /// <summary>How long a fall of <paramref name="height"/> metres takes (free fall, at least <see cref="MinSeconds"/>).</summary>
    public static float FallSeconds(float height) => System.MathF.Max(MinSeconds, System.MathF.Sqrt(2f * System.MathF.Max(0f, height) / 9.81f));

    /// <summary>Forgets everyone not drawn this frame.</summary>
    public void EndFrame(HashSet<Entity> drawn)
    {
        _gone.Clear();
        foreach (var e in _riding.Keys) if (!drawn.Contains(e)) _gone.Add(e);
        foreach (var e in _drops.Keys) if (!drawn.Contains(e)) _gone.Add(e);
        foreach (var e in _gone) { _riding.Remove(e); _drops.Remove(e); }
    }
}
