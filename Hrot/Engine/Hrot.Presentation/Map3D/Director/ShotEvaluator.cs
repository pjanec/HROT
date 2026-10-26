using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis3D;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>What a shot needs to know about the world: which network id a name is, and where a unit stands.</summary>
public interface IShotWorld
{
    /// <summary>The network id of <paramref name="who"/> (a name, or a network id as text); null when no such unit is here.</summary>
    long? Resolve(string who);

    /// <summary>Where the unit stands; null when it is not here.</summary>
    Vector3? PositionOf(long networkId);

    /// <summary>Every unit's name here (for "*part" and for listing a misspelt name's neighbours).</summary>
    IReadOnlyCollection<string> Names { get; }
}

/// <summary>
/// ⭐ <c>CE-4038</c> — the units of a world by name (<see cref="EntityInfo.Name"/>) and network id. The name index is built on the first
/// lookup and again when a name is not found (a unit spawned since); positions are read live.
/// </summary>
public sealed class WorldUnits : IShotWorld
{
    private readonly Func<EntityRepository?> _world;
    private Dictionary<string, long> _byName = new(StringComparer.Ordinal);
    private EntityRepository? _indexed;

    public WorldUnits(Func<EntityRepository?> world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    public IReadOnlyCollection<string> Names { get { Index(force: false); return _byName.Keys; } }

    public long? Resolve(string who)
    {
        if (string.IsNullOrWhiteSpace(who)) return null;
        if (long.TryParse(who, out var id)) return id;
        Index(force: false);
        if (_byName.TryGetValue(who, out id)) return id;
        Index(force: true);
        return _byName.TryGetValue(who, out id) ? id : null;
    }

    public Vector3? PositionOf(long networkId)
    {
        var world = _world();
        var e = NetworkIdResolver.ResolveNetworkId(world, networkId);
        return world != null && !e.IsNull && world.HasComponent<SimTransform>(e) ? world.GetComponentRO<SimTransform>(e).Position : null;
    }

    /// <summary>Forgets the index (a new scenario was loaded).</summary>
    public void Reset() { _byName = new(StringComparer.Ordinal); _indexed = null; }

    private void Index(bool force)
    {
        var world = _world();
        if (world == null) { _byName.Clear(); _indexed = null; return; }
        if (!force && ReferenceEquals(world, _indexed) && _byName.Count > 0) return;
        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in world.Query().With<EntityInfo>().With<NetworkIdentity>().Build())
        {
            var name = world.GetComponentRO<EntityInfo>(e).Name.ToString();
            if (name.Length > 0) map.TryAdd(name, world.GetComponentRO<NetworkIdentity>(e).Value);
        }
        _byName = map;
        _indexed = world;
    }
}

/// <summary>
/// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §2, D-9) — the ONE copy of the shot math: a <see cref="ShotSpec"/> at a moment of it, as the
/// <see cref="FilmShot"/> the film camera holds. ⭐ Ported from <c>scripts/film.py</c>'s <c>_shot_for</c> (follow · over-toward · lookAt ·
/// orbit), plus eye-target, <c>to</c> with its ease, and the lens — film.py drives <c>/director/*</c> now and keeps no copy.
/// </summary>
public static class ShotEvaluator
{
    private const float Deg = MathF.PI / 180f;

    /// <summary>
    /// The shot <paramref name="t"/> sim seconds into <paramref name="shot"/>; null when a unit it names is not here (the camera then stays
    /// where it was, as a held shot whose unit is gone).
    /// </summary>
    public static FilmShot? Evaluate(ShotSpec shot, double t, IShotWorld world)
    {
        var c = At(shot, t);
        float yaw = c.Yaw + c.Orbit * (float)t;
        FilmShot? result = null;
        if (c.Follow != null)
        {
            if (world.Resolve(c.Follow) is { } id)
                result = new FilmShot { Follow = id, Distance = c.Distance, Yaw = yaw * Deg, Pitch = c.Pitch * Deg, Lift = c.Lift };
        }
        else if (c.Over != null)
        {
            if (c.Toward != null && world.Resolve(c.Over) is { } over && world.Resolve(c.Toward) is { } toward
                && world.PositionOf(over) is { } a && world.PositionOf(toward) is { } b)
            {
                float dx = b.X - a.X, dy = b.Y - a.Y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (!(d > 0f)) d = 1f;
                float ux = dx / d, uy = dy / d;
                var eye = new Vector3(a.X - ux * c.Back, a.Y - uy * c.Back, a.Z + c.Height);
                var target = new Vector3(a.X + ux * d * c.Ahead, a.Y + uy * d * c.Ahead, a.Z + c.Lift);
                result = FilmShot.Looking(eye, target);
            }
        }
        else if (c.LookAt is { } at)
            result = new FilmShot { Pose = new CameraPose(at, c.Distance, yaw * Deg, c.Pitch * Deg) };
        else if (c.Eye is { } eye && c.Target is { } target)
            result = FilmShot.Looking(eye, target);

        return result is not null && c.Fov is { } fov ? result with { FovDeg = fov } : result;
    }

    /// <summary>
    /// ⭐ <c>T4</c> — the film shot of one <see cref="TakeTimeline.ProgramAt"/> frame. Not flying: the key's camera, <see cref="ProgramFrame.Since"/>
    /// into it. Flying between two cameras of the SAME kind (the same unit, the same look-at / eye-target form): the leaving camera with its
    /// numbers eased toward the arriving one's by <see cref="ProgramFrame.Blend"/> — exactly what <see cref="At"/> does for a shot's <c>to</c>.
    /// ⚠ Between cameras of DIFFERENT kinds there is nothing to blend: the leaving camera stays until the fly ends, then the arriving one cuts in.
    /// </summary>
    public static FilmShot? Evaluate(ProgramFrame frame, IShotWorld world)
    {
        CameraSpec camera;
        if (frame.From is null) camera = frame.Key.Camera;
        else if (!SameKind(frame.From.Camera, frame.Key.Camera)) camera = frame.Blend >= 1f ? frame.Key.Camera : frame.From.Camera;
        else camera = Blend(frame.From.Camera, frame.Key.Camera, frame.Blend);
        return Evaluate(new ShotSpec { Camera = camera }, frame.Since, world);
    }

    private static bool SameKind(CameraSpec a, CameraSpec b)
        => a.Follow == b.Follow && a.Over == b.Over && a.Toward == b.Toward
           && (a.LookAt != null) == (b.LookAt != null) && (a.Eye != null) == (b.Eye != null) && (a.Target != null) == (b.Target != null);

    /// <summary>Numbers from <paramref name="a"/> toward <paramref name="b"/> by <paramref name="u"/> (already eased); the same arithmetic as <see cref="At"/>, so a number two cameras share stays exactly itself.</summary>
    private static CameraSpec Blend(CameraSpec a, CameraSpec b, float u)
    {
        float L(float from, float to) => from + (to - from) * u;
        Vector3? V(Vector3? from, Vector3? to) => from is { } f && to is { } t ? new Vector3(L(f.X, t.X), L(f.Y, t.Y), L(f.Z, t.Z)) : from;
        return a with
        {
            Distance = L(a.Distance, b.Distance), Yaw = L(a.Yaw, b.Yaw), Pitch = L(a.Pitch, b.Pitch), Lift = L(a.Lift, b.Lift),
            Back = L(a.Back, b.Back), Height = L(a.Height, b.Height), Ahead = L(a.Ahead, b.Ahead),
            LookAt = V(a.LookAt, b.LookAt), Eye = V(a.Eye, b.Eye), Target = V(a.Target, b.Target),
            Fov = a.Fov is null && b.Fov is null ? null : (a.Fov ?? 45f) + ((b.Fov ?? 45f) - (a.Fov ?? 45f)) * u,
        };
    }

    /// <summary>The camera <paramref name="t"/> sim seconds into the shot: its numeric keys eased toward <see cref="ShotSpec.To"/>.</summary>
    public static CameraSpec At(ShotSpec shot, double t)
    {
        var c = shot.Camera;
        if (shot.To is not { Count: > 0 } to) return c;
        float u = Eased(shot.Ease, shot.Seconds > 0 ? (float)Math.Clamp(t / shot.Seconds, 0d, 1d) : 1f);
        float L(string key, float from) => to.TryGetValue(key, out var end) ? from + (end - from) * u : from;
        return c with
        {
            Distance = L("distance", c.Distance), Yaw = L("yaw", c.Yaw), Pitch = L("pitch", c.Pitch), Lift = L("lift", c.Lift),
            Back = L("back", c.Back), Height = L("height", c.Height), Ahead = L("ahead", c.Ahead),
            Fov = to.TryGetValue("fov", out var fovEnd) ? (c.Fov ?? 45f) + (fovEnd - (c.Fov ?? 45f)) * u : c.Fov,
        };
    }

    /// <summary>0…1 through the ease (smoothstep for in-out).</summary>
    public static float Eased(Ease ease, float u) => ease switch
    {
        Ease.Linear => u,
        Ease.In => u * u,
        Ease.Out => 1f - (1f - u) * (1f - u),
        _ => u * u * (3f - 2f * u),
    };
}
