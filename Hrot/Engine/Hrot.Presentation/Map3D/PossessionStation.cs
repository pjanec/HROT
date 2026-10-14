using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Common.Orders;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D
{
    /// <summary>
    /// ⭐⭐ <c>CE-3174</c> (C-1, M-9; <c>docs/DESIGN_Entity_Control.md</c> §8d) — THIS map host as a first-person STATION. It never decides
    /// it is in FPS: it FOLLOWS the replicated <see cref="PossessedBy"/> — a unit held by this station (C-8: the node) puts the map into
    /// a first-person view on it (<see cref="FpsCameraDriver"/> on the 3-D camera, the cursor locked, the HUD on); losing the unit
    /// (released, taken over, killed) leaves it and restores the view the operator had, which is never saved (R-260). So a possess from
    /// the unit menu, from HTTP, or a take-over all land the same way. Attached on every map host by
    /// <c>MapInteractionPack.AttachMapLayers</c>.
    /// </summary>
    public sealed class PossessionStation : IMapLayer
    {
        private readonly MapViewSwitch _view;
        private readonly Func<EntityRepository?> _world;
        private FpsCameraDriver? _driver;
        private CameraPose _before;
        private bool _was3D;

        public PossessionStation(MapViewSwitch view, Func<EntityRepository?> world)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public string Name => "Possession (FPS)";
        public int LayerBitIndex => -1;
        public bool Has3D => true;

        /// <summary>True while this station drives a unit in first person.</summary>
        public bool InFps => _driver != null;

        /// <summary>The unit driven now (<see cref="Entity.Null"/> when not in FPS).</summary>
        public Entity Unit => _driver?.Unit ?? Entity.Null;

        /// <summary>The weapon mount the operator has selected (the wheel).</summary>
        public int SelectedMount => _driver?.Mount ?? 0;

        /// <summary>Frames sent this session (diagnostics, rails).</summary>
        public long FramesSent { get; private set; }

        public void Update(float dt)
        {
            var w = _world();
            var held = w is null ? Entity.Null : HeldHere(w, out _);
            if (_driver != null && (held.IsNull || held != _driver.Unit)) Leave();
            if (_driver == null && !held.IsNull && w is not null) Enter(w, held);
        }

        /// <summary>The unit this station holds in <paramref name="w"/> (its <see cref="PossessedBy"/> names this node), or null.</summary>
        public static Entity HeldHere(EntityRepository w, out long networkId)
        {
            networkId = 0;
            if (!w.IsComponentTypeRegistered<PossessedBy>() || !w.HasSingletonManaged<NetworkEntityMap>()) return Entity.Null;
            var map = w.GetSingletonManaged<NetworkEntityMap>()!;
            var orders = new EntityOrders(w, map, geo: null);
            foreach (var unit in w.Query().With<PossessedBy>().Build())
            {
                if (!w.IsAlive(unit) || w.GetComponentRO<PossessedBy>(unit).StationNodeId != orders.StationFor(unit)) continue;
                if (!map.TryGetNetworkId(unit, out networkId)) continue;
                return unit;
            }
            return Entity.Null;
        }

        private void Enter(EntityRepository w, Entity unit)
        {
            HeldHere(w, out long netId);
            _before = _view.CurrentPose;
            _was3D = _view.Is3D;
            _view.Set(true, animate: false);
            _driver = new FpsCameraDriver(this, _world, unit, netId);
            _view.Camera3D.Driver = _driver;
        }

        private void Leave()
        {
            _view.Camera3D.Driver = null;   // Detached unlocks the cursor
            _driver = null;
            _view.ShowPose(_before);        // the view the operator had — 2-D when it was a 2-D pose
            if (!_was3D) _view.Set(false, animate: false);
        }

        /// <summary>One control frame for the held unit (through <see cref="EntityOrders"/>, the one operator path — R-270).</summary>
        internal void Send(long netId, OperatorControlFrameEvent frame)
        {
            if (Orders() is { } orders && orders.Control(netId, frame).Accepted) FramesSent++;
        }

        /// <summary>The operator lets the unit go (Esc); the view leaves FPS when <see cref="PossessedBy"/> says so.</summary>
        internal void Release(long netId) => Orders()?.Unpossess(netId);

        private EntityOrders? Orders()
            => _world() is { } w && w.HasSingletonManaged<NetworkEntityMap>()
                ? new EntityOrders(w, w.GetSingletonManaged<NetworkEntityMap>()!, geo: null) : null;

        public void Draw(RenderContext ctx) { }
        public void Draw3D(RenderContext ctx) { }
        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;

        /// <summary>The HUD: a crosshair, health, ammunition per mount (the selected one marked) and the keys.</summary>
        public void DrawOverlay(RenderContext ctx)
        {
            if (_driver == null || _world() is not { } w) return;
            var size = _view.Camera3D.Viewport;
            int cx = (int)(size.X / 2), cy = (int)(size.Y / 2);
            Raylib.DrawLine(cx - 10, cy, cx - 3, cy, Color.White);
            Raylib.DrawLine(cx + 3, cy, cx + 10, cy, Color.White);
            Raylib.DrawLine(cx, cy - 10, cx, cy - 3, Color.White);
            Raylib.DrawLine(cx, cy + 3, cx, cy + 10, Color.White);
            int y = (int)size.Y - 24;
            var lines = Hud.Lines(w, _driver.Unit, _driver.Mount, _driver.IsVehicle, _driver.HullLocked);
            for (int i = lines.Length - 1; i >= 0; i--, y -= 20)
                Raylib.DrawText(lines[i], 12, y, 18, Color.RayWhite);
        }

        /// <summary>The HUD's text, apart from drawing (the rails read it).</summary>
        public static class Hud
        {
            public static string[] Lines(EntityRepository w, Entity unit, int mount, bool vehicle, bool hullLocked)
            {
                var lines = new System.Collections.Generic.List<string>();
                if (w.IsComponentTypeRegistered<Health>() && w.HasComponent<Health>(unit))
                {
                    var h = w.GetComponentRO<Health>(unit);
                    lines.Add($"HEALTH {h.Current:0}/{h.Max:0}");
                }
                if (w.IsComponentTypeRegistered<WeaponAmmo>() && w.HasComponent<WeaponAmmo>(unit))
                {
                    var a = w.GetComponentRO<WeaponAmmo>(unit);
                    for (int m = 0; m < Math.Max((int)a.Count, 1); m++)
                    {
                        int mag = a.MagazineOf(m);
                        lines.Add($"{(m == mount ? ">" : " ")} WEAPON {m}  {a.AmmoOf(m)}/{a.MaxOf(m)}{(mag >= 0 ? $"  [{mag}]" : "")}");
                    }
                }
                lines.Add(vehicle
                    ? $"W/S throttle  A/D steer  V {(hullLocked ? "free look" : "hull view")}  LMB fire  wheel weapon  Esc release"
                    : "W/A/S/D move  mouse look  Shift run  LMB fire  wheel weapon  Esc release");
                return lines.ToArray();
            }
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3174</c> (M-2, M-3, M-9; §8d) — the first-person camera on a possessed unit, and the operator's controls. Update puts the
    /// eye on the unit (a person: its posture's eye height — the eye perception uses; a vehicle: above the hull); HandleInput reads the
    /// mouse (look), W/A/S/D (a person: forward + strafe; a vehicle: throttle + steer), Shift (run), the wheel (weapon), LMB (fire),
    /// V (a vehicle's hull-locked view, where the mouse steers) and Esc (release), and sends ONE control frame per frame. It consumes
    /// the input, so no click or menu reaches the map's layers while the operator is in the unit.
    /// </summary>
    public sealed class FpsCameraDriver : ICameraDriver
    {
        /// <summary>Radians of look per pixel of mouse movement.</summary>
        public const float LookSensitivity = 0.0025f;
        /// <summary>Steer per pixel of mouse X in the hull-locked view.</summary>
        public const float MouseSteerPerPixel = 0.01f;
        /// <summary>A vehicle's eye above its origin (m) — v1: the hull, not yet the turret (§8d, deferred).</summary>
        public const float VehicleEyeHeight = 2.5f;
        /// <summary>⭐ CE-2168 — a flyer's camera sits on it (an FPV view).</summary>
        public const float RotorcraftEyeHeight = 0.2f;
        /// <summary>How far an aim with nothing under the crosshair reaches (m).</summary>
        public const float AimReach = 1000f;

        private readonly PossessionStation _station;
        private readonly Func<EntityRepository?> _world;
        private readonly long _netId;
        private IInputProvider? _locked;
        private float _yaw, _pitch, _mouseSteer;
        private bool _lookInitialised;
        private uint _seq;

        internal FpsCameraDriver(PossessionStation station, Func<EntityRepository?> world, Entity unit, long netId)
        {
            _station = station;
            _world = world;
            Unit = unit;
            _netId = netId;
        }

        public Entity Unit { get; }
        public int Mount { get; private set; }
        public bool HullLocked { get; private set; }
        public bool IsVehicle => Scheme == ControlScheme.Vehicle;

        /// <summary>⭐ CE-2168 (Motion Domains MD-D) — the control scheme, chosen by the body's locomotion class (not "vehicle or not"):
        /// a rotorcraft flies (W/S forward, A/D sideways, the mouse yaws, Space / C climb and descend), a ground vehicle drives, the
        /// rest walk.</summary>
        public ControlScheme Scheme => _world() is not { } w ? ControlScheme.Person
            : Fdp.Toolkit.Motion.Locomotions.Of(w, Unit) switch
            {
                Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft => ControlScheme.Rotorcraft,
                Fdp.Toolkit.Tkb.Domain.LocomotionClass.Wheeled or Fdp.Toolkit.Tkb.Domain.LocomotionClass.Tracked => ControlScheme.Vehicle,
                _ => ControlScheme.Person,
            };

        /// <summary>The look direction (radians): what the camera shows and, for a person, the body's yaw (M-2).</summary>
        public float Yaw => _yaw;
        public float Pitch => _pitch;

        public void Update(MapCamera3D camera, float dt)
        {
            if (_world() is not { } w || !w.IsAlive(Unit) || !w.HasComponent<SimTransform>(Unit)) return;
            var tf = w.GetComponentRO<SimTransform>(Unit);
            float hullYaw = HullYaw(tf.Rotation);
            if (!_lookInitialised) { _yaw = hullYaw; _pitch = 0f; _lookInitialised = true; }
            bool vehicle = IsVehicle;
            if (vehicle && HullLocked) _yaw = hullYaw;
            float eyeHeight = Scheme == ControlScheme.Rotorcraft ? RotorcraftEyeHeight : vehicle ? VehicleEyeHeight
                : Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeHeightFor(w, Unit,
                    Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(w, Unit));
            var eye = tf.Position + new Vector3(0f, 0f, eyeHeight);
            camera.Yaw = _yaw;
            camera.Pitch = _pitch;
            camera.Distance = camera.MinDistance;
            camera.LookAt = eye + camera.Forward * camera.Distance;
        }

        public bool HandleInput(MapCamera3D camera, IInputProvider input)
        {
            if (_locked == null) { input.SetCursorLocked(true); _locked = input; }
            if (input.IsKeyPressed(MapKeyboardKey.Escape)) { _station.Release(_netId); return true; }

            bool vehicle = IsVehicle;
            var delta = input.MouseDelta;
            if (vehicle && input.IsKeyPressed((MapKeyboardKey)KeyboardKey.V)) HullLocked = !HullLocked;
            if (vehicle && HullLocked) _mouseSteer = Math.Clamp(_mouseSteer - delta.X * MouseSteerPerPixel, -1f, 1f);
            else _yaw -= delta.X * LookSensitivity;
            _pitch = Math.Clamp(_pitch - delta.Y * LookSensitivity, -1.4f, 1.4f);

            float forward = (input.IsKeyDown((MapKeyboardKey)KeyboardKey.W) ? 1f : 0f) - (input.IsKeyDown((MapKeyboardKey)KeyboardKey.S) ? 1f : 0f);
            float side = (input.IsKeyDown((MapKeyboardKey)KeyboardKey.A) ? 1f : 0f) - (input.IsKeyDown((MapKeyboardKey)KeyboardKey.D) ? 1f : 0f);
            // ⭐ CE-2168 (MD-D) — a flyer's climb axis
            float vertical = Scheme != ControlScheme.Rotorcraft ? 0f
                : (input.IsKeyDown((MapKeyboardKey)KeyboardKey.Space) ? 1f : 0f) - (input.IsKeyDown((MapKeyboardKey)KeyboardKey.C) ? 1f : 0f);
            bool run = input.IsKeyDown(MapKeyboardKey.LeftShift) || input.IsKeyDown(MapKeyboardKey.RightShift);
            float wheel = input.MouseWheelMove;
            if (wheel != 0f) Mount = NextMount(Mount, wheel > 0f ? -1 : 1);

            var frame = new OperatorControlFrameEvent
            {
                Seq = ++_seq,
                Forward = forward,
                Lateral = vehicle ? 0f : side,
                Steer = vehicle ? (HullLocked ? _mouseSteer : side) : 0f,
                Yaw = _yaw,
                Vertical = vertical,
                Gait = run ? (byte)0 : (byte)1,
                Trigger = input.IsMouseButtonDown(MapMouseButton.Left) ? (byte)1 : (byte)0,
                Mount = (byte)Mount,
                AimPoint = AimPoint(camera),
            };
            _station.Send(_netId, frame);
            return true;
        }

        public void Detached(MapCamera3D camera)
        {
            _locked?.SetCursorLocked(false);
            _locked = null;
        }

        /// <summary>Where the crosshair rests: the terrain under the screen centre, else <see cref="AimReach"/> along the look.</summary>
        private static Vector3 AimPoint(MapCamera3D camera)
        {
            var hit = camera.Pick(camera.Viewport / 2f, PickFilter.Terrain);
            return hit.Kind != PickKind.None && float.IsFinite(hit.Distance) && hit.Distance <= AimReach
                ? hit.Point : camera.Eye + camera.Forward * AimReach;
        }

        private int NextMount(int mount, int step)
        {
            int count = _world() is { } w && w.IsComponentTypeRegistered<WeaponAmmo>() && w.HasComponent<WeaponAmmo>(Unit)
                ? Math.Max(1, (int)w.GetComponentRO<WeaponAmmo>(Unit).Count) : 1;
            return ((mount + step) % count + count) % count;
        }

        private static float HullYaw(Quaternion q)
            => MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.Y * q.Y + q.Z * q.Z));
    }

    /// <summary>⭐ CE-2168 (Motion Domains MD-D) — how the FPS station maps the keys and mouse onto a possessed body.</summary>
    public enum ControlScheme : byte { Person = 0, Vehicle = 1, Rotorcraft = 2 }
}
