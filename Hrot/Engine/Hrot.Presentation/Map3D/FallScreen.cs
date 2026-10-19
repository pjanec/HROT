using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-1220</c> (docs/DESIGN_Entity_Control.md §8k K-1) — WHEN HE FALLS: the possessed man was killed, so the possession ended; instead of
/// cutting back to the map, the station keeps the 3-D view where his eyes were, fades it (the station draws the shade and the lines) and
/// offers: <b>Tab</b> a squad mate, <b>K</b> a <see cref="PossessionStation.KillCamSeconds"/> kill cam from the shooter's eye toward the body
/// (when the shooter is known and still there), <b>Esc</b> the map. A camera driver, so the map's own camera keys stay off while it is up.
/// </summary>
public sealed class FallScreen : ICameraDriver
{
    /// <summary>The kill cam's eye height over the shooter's reference point (m).</summary>
    public const float KillCamEyeHeight = 1.7f;

    private readonly PossessionStation _station;
    private float _killCamUntil = -1f;

    public FallScreen(PossessionStation station, Entity dead, Entity killer, float since)
    {
        _station = station;
        Dead = dead;
        Killer = killer;
        Since = since;
    }

    /// <summary>The man who fell.</summary>
    public Entity Dead { get; }
    /// <summary>Who hit him last (<c>HitFeedback</c>), or null when unknown.</summary>
    public Entity Killer { get; }
    /// <summary>The station clock when he fell.</summary>
    public float Since { get; }

    /// <summary>Is the kill cam running at <paramref name="clock"/>?</summary>
    public bool KillCamOn(float clock) => clock < _killCamUntil;

    /// <summary>Can the kill cam show the shooter: known, still in the world, placed?</summary>
    public bool CanKillCam(EntityRepository w) => !Killer.IsNull && w.IsAlive(Killer) && w.HasComponent<SimTransform>(Killer)
                                                  && w.IsAlive(Dead) && w.HasComponent<SimTransform>(Dead);

    /// <summary>The shooter as the screen names him: his name, else "an unseen shooter".</summary>
    public string KillerName(EntityRepository w)
    {
        if (!Killer.IsNull && w.IsAlive(Killer) && w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(Killer)
            && w.GetComponentRO<EntityInfo>(Killer).Name.ToString() is { Length: > 0 } name) return name;
        return Killer.IsNull ? "an unseen shooter" : "an enemy";
    }

    /// <summary>The station's per-frame step (nothing yet: the camera follows in <see cref="Update"/>).</summary>
    public void Step(EntityRepository w, float dt) { }

    /// <summary>The kill cam: the shooter's eye, looking at the body; otherwise the view stays where his eyes were.</summary>
    public void Update(MapCamera3D camera, float dt)
    {
        if (!KillCamOn(_station.Clock) || _station.World is not { } w || !CanKillCam(w)) return;
        var eye = w.GetComponentRO<SimTransform>(Killer).Position + new Vector3(0f, 0f, KillCamEyeHeight);
        var body = w.GetComponentRO<SimTransform>(Dead).Position + new Vector3(0f, 0f, 0.4f);
        var d = body - eye;
        float flat = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        camera.Yaw = MathF.Atan2(d.Y, d.X);
        camera.Pitch = MathF.Atan2(d.Z, MathF.Max(flat, 1e-3f));
        camera.Distance = MathF.Max(d.Length(), camera.MinDistance);
        camera.LookAt = eye + camera.Forward * camera.Distance;
    }

    public bool HandleInput(MapCamera3D camera, IInputProvider input)
    {
        if (input.IsKeyPressed(MapKeyboardKey.Escape)) { _station.EndFall(); return true; }
        if (input.IsKeyPressed((MapKeyboardKey)KeyboardKey.Tab)) { _station.FallToSquadMate(); return true; }
        if (input.IsKeyPressed((MapKeyboardKey)KeyboardKey.K) && _station.World is { } w && CanKillCam(w))
            _killCamUntil = _station.Clock + PossessionStation.KillCamSeconds;
        return true;
    }
}
