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

        /// <param name="bodies">⭐ CE-1073 — the drawn figures: the eyes come from the drawn head, and the held unit's head is not
        /// drawn. Null (a host or rail without 3-D bodies) keeps the perception eye height above the unit's position.</param>
        public PossessionStation(MapViewSwitch view, Func<EntityRepository?> world, EntityBodyLayer3D? bodies = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            Bodies = bodies;
        }

        /// <summary>⭐ CE-1073 — the figures the eye is read from (the forwarding rail checks the pack passes them).</summary>
        public EntityBodyLayer3D? Bodies { get; }

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
            if (Bodies != null) Bodies.FirstPerson = unit;   // ⭐ CE-1073 — the camera is inside this head
        }

        private void Leave()
        {
            _view.Camera3D.Driver = null;   // Detached unlocks the cursor
            _driver = null;
            if (Bodies != null) Bodies.FirstPerson = Entity.Null;
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

        /// <summary>The HUD: a crosshair, health, the weapons (the selected one marked, its round), the locked shot and the keys.
        /// ⭐ CE-1078 — plus a bracket on the locked target and a ring where the gun points, so the operator sees the turret catch up.</summary>
        public void DrawOverlay(RenderContext ctx)
        {
            if (_driver == null || _world() is not { } w) return;
            var camera = _view.Camera3D;
            var size = camera.Viewport;
            int cx = (int)(size.X / 2), cy = (int)(size.Y / 2);
            Raylib.DrawLine(cx - 10, cy, cx - 3, cy, Color.White);
            Raylib.DrawLine(cx + 3, cy, cx + 10, cy, Color.White);
            Raylib.DrawLine(cx, cy - 10, cx, cy - 3, Color.White);
            Raylib.DrawLine(cx, cy + 3, cx, cy + 10, Color.White);
            if (_driver.ShotPending && OnScreen(camera, LockedWorldPoint(w, _driver), out var locked))
                Raylib.DrawRectangleLines((int)locked.X - 12, (int)locked.Y - 12, 24, 24, Color.Orange);
            if (BarrelPoint(w, _driver.Unit, _driver.Mount, _driver.AimAt) is { } barrel && OnScreen(camera, barrel, out var gun))
                Raylib.DrawCircleLines((int)gun.X, (int)gun.Y, 7f, Color.Yellow);
            int y = (int)size.Y - 24;
            var lines = Hud.Lines(w, _driver.Unit, _driver.Mount, _driver.IsVehicle, _driver.HullLocked, _driver.Stance, _driver.ShotPending);
            for (int i = lines.Length - 1; i >= 0; i--, y -= 20)
                Raylib.DrawText(lines[i], 12, y, 18, Color.RayWhite);
        }

        /// <summary>⭐ CE-1078 — where the locked shot is: its unit's middle while it stands, else the locked point.</summary>
        private static Vector3 LockedWorldPoint(EntityRepository w, FpsCameraDriver d)
            => d.LockedTarget != 0 && w.HasSingletonManaged<NetworkEntityMap>()
               && w.GetSingletonManaged<NetworkEntityMap>()!.TryGetEntity(d.LockedTarget, out var e) && w.IsAlive(e) && w.HasComponent<SimTransform>(e)
                ? Fdp.Toolkit.Combat.AimPoint.Middle(w, e, out _, out _) : d.LockedPoint;

        /// <summary>
        /// ⭐ CE-1078 — where the selected weapon's turret points: along its posed barrel (the hull's heading + <see cref="TurretPose"/>),
        /// as far out as the crosshair's point. Null for a weapon on no turret. ⚠ The pose carries the ballistic elevation, so at long
        /// range the ring sits a little above the target when the gun is laid.
        /// </summary>
        public static Vector3? BarrelPoint(EntityRepository w, Entity unit, int mount, Vector3 aimAt)
        {
            if (!w.IsComponentTypeRegistered<TurretPose>() || !w.HasComponent<SimTransform>(unit)) return null;
            var dto = Fdp.Toolkit.Combat.CombatTkb.MountOf(w, unit, mount);
            if (dto is not { TurretIndex: >= 0 } || Fdp.Toolkit.Combat.CombatTkb.TurretOf(w, unit, dto.TurretIndex) is not { } turret) return null;
            var part = Fdp.Toolkit.Combat.TurretParts.Find(w, unit, dto.TurretIndex);
            if (part.IsNull || !w.HasComponent<TurretPose>(part)) return null;
            var hull = w.GetComponentRO<SimTransform>(unit);
            var pose = w.GetComponentRO<TurretPose>(part);
            var pivot = Fdp.Toolkit.Combat.BallisticAim.PivotOf(hull, turret);
            float ce = MathF.Cos(pose.Elevation);
            var local = new Vector3(ce * MathF.Cos(pose.Azimuth), ce * MathF.Sin(pose.Azimuth), MathF.Sin(pose.Elevation));
            return pivot + Vector3.Transform(local, hull.Rotation) * MathF.Max(5f, Vector3.Distance(pivot, aimAt));
        }

        private static bool OnScreen(MapCamera3D camera, Vector3 world, out Vector2 screen)
        {
            screen = default;
            if (Vector3.Dot(world - camera.Eye, camera.Forward) <= 0.1f) return false;   // behind the eye
            screen = camera.WorldToScreen(world);
            return float.IsFinite(screen.X) && float.IsFinite(screen.Y);
        }

        /// <summary>The HUD's text, apart from drawing (the rails read it).</summary>
        public static class Hud
        {
            public static string[] Lines(EntityRepository w, Entity unit, int mount, bool vehicle, bool hullLocked,
                                         Fdp.Toolkit.Tkb.Domain.StanceId? stance = null, bool shotPending = false)
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
                    for (int m = 0; m < Math.Max((int)a.Count, 1); m++) lines.Add(WeaponLine(w, unit, a, m, m == mount));
                }
                if (shotPending) lines.Add("LOCKED - fires when the weapon bears (RMB cancels)");   // ⭐ CE-1078
                if (!vehicle && stance is { } st) lines.Add($"STANCE {st.ToString().ToUpperInvariant()}");   // ⭐ CE-1074
                lines.Add(vehicle
                    ? $"W/S throttle  A/D steer  V {(hullLocked ? "free look" : "hull view")}  LMB fire  RMB cancel  1-4 weapon  F ammo  Esc release"
                    : "W/A/S/D move  mouse look  Shift run  C stance  LMB fire  RMB cancel  1-4 weapon  F ammo  Esc release");
                return lines.ToArray();
            }

            /// <summary>
            /// ⭐ CE-1079 (§8e) — one weapon: <c>&gt; 1 CANNON  120mm APFSDS 19  41/42  F: HEAT-MP / tank HE-FRAG  LOADING</c> — its number
            /// key, its kind, the round in its breech (and how many of that round), its total, the other rounds <c>F</c> cycles to (the
            /// selected weapon only), and LOADING while it reloads or switches.
            /// </summary>
            public static string WeaponLine(EntityRepository w, Entity unit, in WeaponAmmo a, int m, bool selected)
            {
                var dto = Fdp.Toolkit.Combat.CombatTkb.MountOf(w, unit, m);
                var line = new System.Text.StringBuilder();
                line.Append(selected ? "> " : "  ").Append(m + 1).Append(' ').Append(KindOf(dto?.Category ?? Fdp.Toolkit.Tkb.Domain.WeaponCategory.Unknown));
                long loaded = a.LoadedOf(m) != 0 ? a.LoadedOf(m) : unchecked((long)(dto?.AmmoGuid ?? 0UL));
                if (RoundName(w, loaded) is { } round)
                {
                    line.Append("  ").Append(round);
                    if (a.LoadedRoundsOf(m) >= 0) line.Append(' ').Append(a.LoadedRoundsOf(m));
                }
                int mag = a.MagazineOf(m);
                line.Append("  ").Append(a.AmmoOf(m)).Append('/').Append(a.MaxOf(m));
                if (mag >= 0) line.Append("  [").Append(mag).Append(']');
                if (selected && dto?.Loadout is { Count: > 1 } loadout)
                {
                    var others = new System.Collections.Generic.List<string>();
                    foreach (var l in loadout)
                        if (unchecked((long)l.AmmoGuid) != loaded && RoundName(w, unchecked((long)l.AmmoGuid)) is { } name) others.Add(name);
                    if (others.Count > 0) line.Append("  F: ").Append(string.Join(" / ", others));
                }
                if (a.IsBusy(m)) line.Append("  LOADING");
                return line.ToString();
            }

            /// <summary>⭐ CE-1079 — a weapon's kind, as the HUD names it.</summary>
            public static string KindOf(Fdp.Toolkit.Tkb.Domain.WeaponCategory c) => c switch
            {
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms => "RIFLE",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun => "MG",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon => "AUTOCANNON",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.TankGun => "CANNON",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm => "MISSILE",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Mortar => "MORTAR",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Grenade => "GRENADE",
                _ => "WEAPON",
            };

            /// <summary>⭐ CE-1079 — a round's name from the TKB (its munition template; a trailing " round" dropped), or null.</summary>
            public static string? RoundName(EntityRepository w, long ammo)
            {
                if (ammo == 0 || !w.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()) return null;
                if (!w.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>()!.TryGetByType(ammo, out var t) || t == null) return null;
                return t.Name.EndsWith(" round", StringComparison.Ordinal) ? t.Name[..^6] : t.Name;
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
        private Fdp.Toolkit.Tkb.Domain.StanceId? _stance;   // ⭐ CE-1074 — the stance the operator asked for (null: not yet)
        private Vector3 _eye;                              // ⭐ CE-1073 — where the eye was placed this frame
        private bool _eyeKnown;
        private byte _shotSeq, _cancelSeq, _ammoSeq;       // ⭐ CE-1078 / CE-1079 — presses, counted (DESIGN_Entity_Control §8e)
        private long _shotTarget;
        private int _ammoAtPress;

        internal FpsCameraDriver(PossessionStation station, Func<EntityRepository?> world, Entity unit, long netId)
        {
            _station = station;
            _world = world;
            Unit = unit;
            _netId = netId;
        }

        public Entity Unit { get; }
        public int Mount { get; private set; }

        /// <summary>⭐ CE-1078 — a shot locked by the last press has not left yet, as this station sees it (the mount's count has not
        /// dropped since the press; RMB clears it). The HUD's LOCKED.</summary>
        public bool ShotPending { get; private set; }

        /// <summary>⭐ CE-1078 — the network id of the unit the last press locked on (0 = the ground point).</summary>
        public long LockedTarget => _shotTarget;

        /// <summary>⭐ CE-1078 — where the last press locked (the unit's box hit, or the ground).</summary>
        public Vector3 LockedPoint { get; private set; }

        /// <summary>⭐ CE-1078 / CE-1080 — what the crosshair rests on this frame: the point, and the unit's network id (0 = none).</summary>
        public Vector3 AimAt { get; private set; }
        public long AimTarget { get; private set; }
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

        /// <summary>⭐ CE-1074 — the stance the operator has asked for (a person; null for a vehicle or before the first frame).</summary>
        public Fdp.Toolkit.Tkb.Domain.StanceId? Stance => _stance;

        /// <summary>⭐ CE-1073 — where the eye is this frame (world metres).</summary>
        public Vector3 Eye => _eye;

        public void Update(MapCamera3D camera, float dt)
        {
            if (_world() is not { } w || !w.IsAlive(Unit) || !w.HasComponent<SimTransform>(Unit)) return;
            var tf = w.GetComponentRO<SimTransform>(Unit);
            float hullYaw = HullYaw(tf.Rotation);
            if (!_lookInitialised) { _yaw = hullYaw; _pitch = 0f; _lookInitialised = true; }
            bool vehicle = IsVehicle;
            if (vehicle && HullLocked) _yaw = hullYaw;
            _eye = EyeOf(w, tf, vehicle);
            _eyeKnown = true;
            Place(camera);
        }

        /// <summary>
        /// ⭐ CE-1073 (docs/DESIGN_Map_3D_Mode.md §6l) — the eye: a person's is the eyes of his DRAWN head (the body layer's last frame,
        /// in the body's frame, put on the unit's current position and heading — so the lean of a run, a crouch or a crawl moves the
        /// eye with the head instead of the head swinging in front of a camera fixed above the reference point); a vehicle's is above
        /// its hull. No figure drawn yet ⇒ the perception eye height, as before.
        /// </summary>
        private Vector3 EyeOf(EntityRepository w, in SimTransform tf, bool vehicle)
        {
            if (vehicle) return tf.Position + new Vector3(0f, 0f, Scheme == ControlScheme.Rotorcraft ? RotorcraftEyeHeight : VehicleEyeHeight);
            if (_station.Bodies is { } bodies && bodies.TryGetEyeLocal(Unit, out var local))
                return tf.Position + Vector3.Transform(local, tf.Rotation);
            return tf.Position + new Vector3(0f, 0f, Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeHeightFor(w, Unit,
                Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(w, Unit)));
        }

        /// <summary>The camera at the eye, looking along the operator's look.</summary>
        private void Place(MapCamera3D camera)
        {
            if (!_eyeKnown) return;
            camera.Yaw = _yaw;
            camera.Pitch = _pitch;
            camera.Distance = camera.MinDistance;
            camera.LookAt = _eye + camera.Forward * camera.Distance;
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
            // ⭐ CE-1079 (§8e D-2) — 1-4 pick a weapon directly; F moves the selected weapon to its next round
            for (int k = 0; k < 4; k++)
                if (input.IsKeyPressed((MapKeyboardKey)((int)KeyboardKey.One + k)) && k < MountCount()) Mount = k;
            if (input.IsKeyPressed((MapKeyboardKey)KeyboardKey.F)) unchecked { _ammoSeq++; }
            // ⭐ CE-1074 — C cycles a person's stance: standing → crouched → prone → standing (from the stance he has when first driven)
            if (!vehicle && _world() is { } sw)
            {
                _stance ??= Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(sw, Unit);
                if (input.IsKeyPressed((MapKeyboardKey)KeyboardKey.C)) _stance = NextStance(_stance.Value);
            }
            Place(camera);   // ⭐ CE-1073 — this frame's look shows this frame (Update ran before the input)

            // ⭐ CE-1078 / CE-1080 (§8e D-1, D-4) — the crosshair sees units too; a PRESS locks one shot on what it rests on, RMB drops it
            (var aimAt, long aimTarget) = Aim(camera);
            AimAt = aimAt;
            AimTarget = aimTarget;
            if (input.IsMouseButtonPressed(MapMouseButton.Left))
            {
                unchecked { _shotSeq++; }
                _shotTarget = aimTarget;
                LockedPoint = aimAt;
                _ammoAtPress = AmmoOf(Mount);
                ShotPending = true;
            }
            if (input.IsMouseButtonPressed(MapMouseButton.Right)) { unchecked { _cancelSeq++; } ShotPending = false; }
            if (ShotPending && AmmoOf(Mount) < _ammoAtPress) ShotPending = false;   // the round left

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
                AimPoint = aimAt,
                Stance = !vehicle && _stance is { } stance ? (byte)((byte)stance + 1) : (byte)0,   // ⭐ CE-1074
                ShotSeq = _shotSeq, CancelSeq = _cancelSeq, AmmoSeq = _ammoSeq, ShotTarget = _shotTarget,   // ⭐ CE-1078 / CE-1079
            };
            _station.Send(_netId, frame);
            return true;
        }

        public void Detached(MapCamera3D camera)
        {
            _locked?.SetCursorLocked(false);
            _locked = null;
        }

        /// <summary>
        /// Where the crosshair rests: ⭐ CE-1080 (§8e D-4) — the nearest of the terrain and the units' boxes under the screen centre (never
        /// the operator's own body — <see cref="EntityBoxPicker"/> skips it); a unit gives the point where the ray meets its box and its
        /// network id. Nothing within <see cref="AimReach"/> ⇒ that far along the look.
        /// </summary>
        public static (Vector3 Point, long Target) Aim(MapCamera3D camera)
        {
            var hit = camera.Pick(camera.Viewport / 2f, PickFilter.All);
            if (hit.Kind == PickKind.None || !float.IsFinite(hit.Distance) || hit.Distance > AimReach)
                return (camera.Eye + camera.Forward * AimReach, 0L);
            return hit.Kind == PickKind.Entity ? (camera.Eye + camera.Forward * hit.Distance, hit.NetworkId) : (hit.Point, 0L);
        }

        private int NextMount(int mount, int step)
        {
            int count = MountCount();
            return ((mount + step) % count + count) % count;
        }

        private int MountCount()
            => _world() is { } w && w.IsComponentTypeRegistered<WeaponAmmo>() && w.HasComponent<WeaponAmmo>(Unit)
                ? Math.Max(1, (int)w.GetComponentRO<WeaponAmmo>(Unit).Count) : 1;

        private int AmmoOf(int mount)
            => _world() is { } w && w.IsComponentTypeRegistered<WeaponAmmo>() && w.HasComponent<WeaponAmmo>(Unit)
                ? w.GetComponentRO<WeaponAmmo>(Unit).AmmoOf(mount) : 0;

        /// <summary>⭐ CE-1074 — the stance after <paramref name="s"/> in the cycle standing → crouched → prone → standing.</summary>
        public static Fdp.Toolkit.Tkb.Domain.StanceId NextStance(Fdp.Toolkit.Tkb.Domain.StanceId s) => s switch
        {
            Fdp.Toolkit.Tkb.Domain.StanceId.Standing => Fdp.Toolkit.Tkb.Domain.StanceId.Crouched,
            Fdp.Toolkit.Tkb.Domain.StanceId.Crouched => Fdp.Toolkit.Tkb.Domain.StanceId.Prone,
            _ => Fdp.Toolkit.Tkb.Domain.StanceId.Standing,
        };

        private static float HullYaw(Quaternion q)
            => MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.Y * q.Y + q.Z * q.Z));
    }

    /// <summary>⭐ CE-2168 (Motion Domains MD-D) — how the FPS station maps the keys and mouse onto a possessed body.</summary>
    public enum ControlScheme : byte { Person = 0, Vehicle = 1, Rotorcraft = 2 }
}
