using System.Numerics;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.UI.Common.Effects;

/// <summary>
/// ⭐ CE-1105 (docs/DESIGN_Map_3D_Mode.md §6u) — a burst you can see: an AUTOMATIC weapon's shot (B-1) draws, for <see cref="Window"/>
/// seconds, a stream of short tracer dashes from its muzzle toward where it aimed, running outward at <see cref="Speed"/> (B-2). Drawn from
/// what every node already hears (the fire notification, <see cref="RecentShots"/>) — no entity, no wire. Pure — the rails call it.
/// </summary>
public static class FireStreams
{
    /// <summary>How long a stream shows after a shot (s), how far apart its dashes are and how long each is (m), how fast they run (m/s),
    /// and the furthest it reaches (m).</summary>
    public const float Window = 0.35f, Spacing = 5f, DashLength = 1.8f, Speed = 900f, MaxLength = 500f;

    /// <summary>⭐ B-1 — a machine gun or an autocannon (the M134, the M2, the GAU-8 …); a rifle, a launcher or a big gun is one round.</summary>
    public static bool IsAutomatic(WeaponMountDto? mount) => mount?.Category is WeaponCategory.MachineGun or WeaponCategory.Autocannon;

    /// <summary>
    /// ⭐ B-2 — the dashes of a stream from <paramref name="muzzle"/> toward <paramref name="toward"/>, <paramref name="since"/> seconds after
    /// the shot, into <paramref name="into"/> (cleared first); and how strong it still is (1 at the shot, 0 at the window's end). A
    /// zero-length aim draws straight along <paramref name="forward"/>.
    /// </summary>
    public static float Dashes(Vector3 muzzle, Vector3 toward, Vector3 forward, double since, List<(Vector3 From, Vector3 To)> into)
    {
        into.Clear();
        if (since < 0 || since > Window) return 0f;
        var line = toward == Vector3.Zero ? Vector3.Zero : toward - muzzle;   // ⭐ CE-1357 — no aim known: along the gun, never to (0,0,0)
        float reach = line.Length();
        var dir = reach > 0.5f ? line / reach : (forward.LengthSquared() > 1e-6f ? Vector3.Normalize(forward) : Vector3.UnitX);
        reach = reach > 0.5f ? MathF.Min(reach, MaxLength) : MaxLength;
        float front = MathF.Min(reach, (float)since * Speed + Spacing);   // the stream's head has flown this far
        float phase = ((float)since * Speed) % Spacing;
        for (float d = phase; d < front; d += Spacing)
        {
            float end = MathF.Min(d + DashLength, front);
            if (end > d) into.Add((muzzle + dir * d, muzzle + dir * end));
        }
        return 1f - (float)(since / Window);
    }
}
