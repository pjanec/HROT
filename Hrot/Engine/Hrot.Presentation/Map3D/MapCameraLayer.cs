using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Core.Tkb;

namespace Hrot.UI.Common.Map3D;

/// <summary>Whether a map host may create and move the scenario's camera entity (R-260), or only adopts it.</summary>
public enum MapCameraOwnership
{
    /// <summary>Adopts the saved view when the scenario opens; never creates or writes the camera (IG — R-140 — and every
    /// host that does not save the scenario).</summary>
    AdoptOnly = 0,

    /// <summary>A host that SAVES the scenario: also creates the camera on the first camera move and keeps its pose.</summary>
    SavesScenario = 1,
}

/// <summary>
/// ⭐ CE-1033 S5 (docs/DESIGN_Map_3D_Mode.md §3.12, R-260) — the camera entity's pose in its ordinary <see cref="SimTransform"/>
/// (U7): the POSITION is the eye, the ROTATION turns +X onto the view direction with +Z as the camera's up. The look-at point and
/// the distance are recovered by casting the view ray onto the ground — the 3-D camera keeps its look-at ON the ground (R-248),
/// so nothing is lost for a camera that looks down. ⚠ Looking level or up there is no ground hit: the distance falls back.
/// </summary>
public static class MapCameraPose
{
    /// <summary>The distance used when the view ray never meets the ground (looking level or up).</summary>
    public const float FallbackDistance = 300f;

    public static SimTransform ToTransform(CameraPose pose)
    {
        var forward = Forward(pose.Yaw, pose.Pitch);
        var up = new Vector3(-MathF.Sin(pose.Pitch) * MathF.Cos(pose.Yaw), -MathF.Sin(pose.Pitch) * MathF.Sin(pose.Yaw), MathF.Cos(pose.Pitch));
        var left = Vector3.Cross(up, forward);
        // row-vector convention: the rows are the images of +X, +Y, +Z
        var m = new Matrix4x4(
            forward.X, forward.Y, forward.Z, 0f,
            left.X, left.Y, left.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            0f, 0f, 0f, 1f);
        return new SimTransform
        {
            Position = pose.LookAt - forward * pose.Distance,
            Rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m)),
        };
    }

    /// <param name="ground">The ground height at a point (null = 0).</param>
    public static CameraPose FromTransform(in SimTransform t, Func<float, float, float>? ground)
    {
        var f = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, t.Rotation));
        var u = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, t.Rotation));
        float pitch = MathF.Asin(Math.Clamp(f.Z, -1f, 1f));
        // the yaw from whichever axis still has a horizontal part: the view direction, or (looking almost straight down) the up
        float yaw = MathF.Cos(pitch) >= MathF.Abs(MathF.Sin(pitch))
            ? MathF.Atan2(f.Y, f.X)
            : MathF.Atan2(-MathF.Sign(MathF.Sin(pitch)) * u.Y, -MathF.Sign(MathF.Sin(pitch)) * u.X);
        var eye = t.Position;
        float distance = FallbackDistance;
        float down = -MathF.Sin(pitch);
        if (down > 0.02f)
        {
            // the first ground hit along the view ray: bracket it with doubling steps, then bisect (relief, R-248 — a fixed-point
            //   step converges slowly, or not at all, when the ray runs up a slope)
            float Above(float d) { var p = eye + f * d; return p.Z - (ground?.Invoke(p.X, p.Y) ?? 0f); }
            float lo = 0f, hi = 1f;
            while (hi < 60000f && Above(hi) > 0f) { lo = hi; hi *= 2f; }
            if (Above(hi) <= 0f)
            {
                for (int i = 0; i < 40; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (Above(mid) > 0f) lo = mid; else hi = mid;
                }
                distance = Math.Clamp(0.5f * (lo + hi), 2f, 30000f);
            }
        }
        var fwd = Forward(yaw, pitch);
        return new CameraPose(eye + fwd * distance, distance, yaw, pitch);
    }

    private static Vector3 Forward(float yaw, float pitch)
        => new(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
}

/// <summary>
/// ⭐ CE-1033 S5 (docs/DESIGN_Map_3D_Mode.md §3.12, R-260) — the scenario's CAMERA ENTITY on the map, on every host (added by
/// <c>MapInteractionPack.AttachMapLayers</c>). When the scenario's camera appears and the user has not moved the map since the
/// scenario opened, the map ADOPTS it — its pose, and 2-D or 3-D by its pitch. On a host that saves the scenario
/// (<see cref="MapCameraOwnership.SavesScenario"/>) the camera is created on the first camera move and its pose follows the map
/// once the view has been still for <see cref="SettleSeconds"/>. A layer, so the canvas' own <c>Update</c> drives it.
/// <para>⚠ A host that does not OWN the camera does not move it: a change-request would need a pitch path the attribute
/// vocabulary does not have (only geodetic position and heading) — named in CE-1047.</para>
/// </summary>
public sealed class MapCameraLayer : IMapLayer
{
    /// <summary>How long the view must be still before its pose is written to the camera entity.</summary>
    public const float SettleSeconds = 0.5f;

    private readonly MapViewSwitch _switch;
    private readonly Func<EntityRepository?> _world;
    private readonly MapCameraOwnership _ownership;

    private Entity? _camera;
    private bool _adopted, _userMoved, _dirty, _haveLast;
    private CameraPose _last;
    private float _still, _scanIn;
    private int _lastCount;

    public MapCameraLayer(MapViewSwitch viewSwitch, Func<EntityRepository?> world, MapCameraOwnership ownership)
    {
        _switch = viewSwitch ?? throw new ArgumentNullException(nameof(viewSwitch));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _ownership = ownership;
    }

    /// <summary>The camera entity this map follows, once found or created.</summary>
    public Entity? Camera => _camera;

    /// <summary>True once the saved view was adopted (or this map created the camera).</summary>
    public bool Adopted => _adopted;

    public string Name => "Map camera";
    public int LayerBitIndex => -1;

    public void Update(float dt)
    {
        var w = _world();
        if (w == null || !w.IsComponentTypeRegistered<TkbIdentity>() || !w.IsComponentTypeRegistered<SimTransform>()) return;

        // a scenario load / New empties the world: the next scenario's camera may be adopted again
        int count = w.EntityCount;
        if (count == 0 && _lastCount > 0) Reset();
        _lastCount = count;
        if (_camera is { } known && !w.IsAlive(known)) Reset();

        if (_camera == null && (_scanIn -= dt) <= 0f)
        {
            _camera = Find(w);
            _scanIn = 0.5f;
        }

        var now = _switch.CurrentPose;
        if (_switch.IsSwitching || _switch.Camera3D.IsAnimating)
        {
            _userMoved = _dirty = true;   // toggling 2-D / 3-D is a camera move; its end pose is written once still
            _still = 0f;
            _last = now; _haveLast = true;
            return;
        }

        if (_camera is { } cam && !_adopted && !_userMoved && w.HasComponent<SimTransform>(cam))
        {
            _switch.ShowPose(MapCameraPose.FromTransform(w.GetComponentRO<SimTransform>(cam), _switch.Camera3D.GroundHeight));
            _adopted = true;
            _last = _switch.CurrentPose; _haveLast = true;
            return;
        }

        if (!_haveLast) { _last = now; _haveLast = true; return; }   // the first frame is the baseline, not a move
        if (!Same(now, _last))
        {
            _userMoved = _dirty = true;
            _still = 0f;
            _last = now;
            return;
        }
        if (!_dirty) return;
        _still += dt;
        if (_still < SettleSeconds) return;
        _dirty = false;
        if (_ownership == MapCameraOwnership.SavesScenario) Commit(w, now);
    }

    private void Commit(EntityRepository w, CameraPose pose)
    {
        var tf = MapCameraPose.ToTransform(pose);
        if (_camera is { } cam)
        {
            if (!w.HasComponent<SimTransform>(cam) || !w.HasAuthority(cam)) return;   // not ours to move (CE-1047)
            w.GetComponentRW<SimTransform>(cam) = tf;
            return;
        }
        var e = w.CreateEntity();
        w.AddComponent(e, tf);
        w.AddComponent(e, new TkbIdentity { TkbType = MapCameraTkb.Type });
        _camera = e;
        _adopted = true;
    }

    private void Reset()
    {
        _camera = null;
        _adopted = _userMoved = _dirty = false;
        _still = _scanIn = 0f;
    }

    /// <summary>The scenario's camera: the camera-type entity with the lowest network id (two saving hosts may each have made
    /// one; every map then agrees on the same one), else the first found.</summary>
    public static Entity? Find(EntityRepository w)
    {
        Entity? best = null;
        long bestId = long.MaxValue;
        bool hasNet = w.IsComponentTypeRegistered<NetworkIdentity>();
        foreach (var e in w.Query().With<TkbIdentity>().With<SimTransform>().Build())
        {
            if (!MapCameraTkb.Is(w.GetComponentRO<TkbIdentity>(e).TkbType)) continue;
            long id = hasNet && w.HasComponent<NetworkIdentity>(e) && w.GetComponentRO<NetworkIdentity>(e).Value != 0
                ? w.GetComponentRO<NetworkIdentity>(e).Value : long.MaxValue - 1;
            if (best == null || id < bestId) { best = e; bestId = id; }
        }
        return best;
    }

    private static bool Same(in CameraPose a, in CameraPose b)
        => Vector3.DistanceSquared(a.LookAt, b.LookAt) < 1e-4f && MathF.Abs(a.Distance - b.Distance) < 1e-3f
           && MathF.Abs(a.Yaw - b.Yaw) < 1e-4f && MathF.Abs(a.Pitch - b.Pitch) < 1e-4f;

    public void Draw(RenderContext ctx) { }

    /// <summary>Nothing to draw in either mode.</summary>
    public bool Has3D => true;
    public void Draw3D(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}
