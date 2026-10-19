using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
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

        /// <summary>Seconds this station has run (the use key's hold and the verdict line time against it).</summary>
        public float Clock { get; private set; }

        /// <summary>⭐ CE-1158 — where the Drive hand-over stands (§8h driving sequence).</summary>
        public enum HandOverStep : byte
        {
            None = 0,
            /// <summary>Drive was pressed: waiting for the replicated passenger list to show the soldier in the driver seat.</summary>
            AwaitBoard,
            /// <summary>The soldier was asked to ride: waiting for his <see cref="PossessedBy"/> to say Riding.</summary>
            AwaitRiding,
            /// <summary>The vehicle was possessed: waiting for it to be held here.</summary>
            AwaitVehicle,
        }

        /// <summary>A hand-over step that has not moved on in this long is dropped (the press was refused, the seat went to someone else).</summary>
        public const float HandOverTimeout = 15f;

        private HandOverStep _step;
        private Entity _hoSoldier, _hoVehicle;
        private float _hoSince;
        private Entity _getOutRider;
        private float _getOutSince;
        private float _orphanCheckAt;
        private bool _softLeft;

        /// <summary>⭐ CE-1158 — the Drive hand-over's step now (rails).</summary>
        public HandOverStep Step => _step;

        public void Update(float dt)
        {
            Clock += dt;
            var w = _world();
            if (w is not null) { StepHandOver(w); ReleaseOrphanRiders(w); }
            var held = w is null ? Entity.Null : HeldHere(w, out _);
            bool handing = _step != HandOverStep.None || !_getOutRider.IsNull;
            if (_driver != null && (held.IsNull || held != _driver.Unit)) Leave(restore: !handing);
            if (_driver == null && !held.IsNull && w is not null) Enter(w, held);
            if (_driver == null && _softLeft && !handing) RestoreView();   // a hand-over that came to nothing
        }

        /// <summary>
        /// The unit this station DRIVES in <paramref name="w"/> (its <see cref="PossessedBy"/> names this node), or null. ⭐ CE-1158 — a unit
        /// held as <see cref="PossessionMode.Riding"/> is skipped (the soldier sitting in the vehicle the station drives), and of two Fps
        /// units (a moment during a take-over) the newest possession wins (<see cref="PossessedBy.Epoch"/>).
        /// </summary>
        public static Entity HeldHere(EntityRepository w, out long networkId)
        {
            networkId = 0;
            if (!w.IsComponentTypeRegistered<PossessedBy>() || !w.HasSingletonManaged<NetworkEntityMap>()) return Entity.Null;
            var map = w.GetSingletonManaged<NetworkEntityMap>()!;
            var orders = new EntityOrders(w, map, geo: null);
            Entity best = Entity.Null;
            uint bestEpoch = 0;
            foreach (var unit in w.Query().With<PossessedBy>().Build())
            {
                if (!w.IsAlive(unit)) continue;
                var held = w.GetComponentRO<PossessedBy>(unit);
                if (held.StationNodeId != orders.StationFor(unit) || held.Mode == PossessionMode.Riding) continue;
                if (!map.TryGetNetworkId(unit, out long id)) continue;
                if (best.IsNull || held.Epoch > bestEpoch) { best = unit; bestEpoch = held.Epoch; networkId = id; }
            }
            return best;
        }

        /// <summary>⭐ CE-1158 — the soldier this station holds as <see cref="PossessionMode.Riding"/> aboard <paramref name="vehicle"/>, or null.</summary>
        public static Entity RiderOf(EntityRepository w, Entity vehicle)
        {
            if (vehicle.IsNull || !w.IsComponentTypeRegistered<PossessedBy>() || !w.IsComponentTypeRegistered<IsEmbarkedTag>()
                || !w.HasSingletonManaged<NetworkEntityMap>()) return Entity.Null;
            var orders = new EntityOrders(w, w.GetSingletonManaged<NetworkEntityMap>()!, geo: null);
            foreach (var unit in w.Query().With<PossessedBy>().With<IsEmbarkedTag>().Build())
            {
                var held = w.GetComponentRO<PossessedBy>(unit);
                if (w.IsAlive(unit) && held.Mode == PossessionMode.Riding && held.StationNodeId == orders.StationFor(unit)
                    && w.GetComponentRO<IsEmbarkedTag>(unit).VehicleEntity == vehicle) return unit;
            }
            return Entity.Null;
        }

        /// <summary>⭐ CE-1158 — is <paramref name="soldier"/> in <paramref name="vehicle"/>'s DRIVER seat, by the replicated passenger list?</summary>
        public static bool InDriverSeat(EntityRepository w, Entity soldier, Entity vehicle)
        {
            if (!w.IsComponentTypeRegistered<IsEmbarkedTag>() || !w.HasComponent<IsEmbarkedTag>(soldier)) return false;
            var tag = w.GetComponentRO<IsEmbarkedTag>(soldier);
            if (tag.VehicleEntity != vehicle) return false;
            return !w.IsComponentTypeRegistered<CarrierSeats>() || !w.HasComponent<CarrierSeats>(vehicle)
                || w.GetComponentRO<CarrierSeats>(vehicle).Roles[tag.Seat] == SeatRole.Driver;
        }

        /// <summary>
        /// ⭐ CE-1158 (§8h U-4) — the driver sent a Drive / Pilot press: once the soldier shows in the driver seat, ride him and drive the
        /// vehicle. The STATION orchestrates it, from replicated state, because the soldier and the vehicle may be brained on different nodes.
        /// </summary>
        internal void ArmDrive(Entity soldier, Entity vehicle)
        {
            _step = HandOverStep.AwaitBoard;
            _hoSoldier = soldier;
            _hoVehicle = vehicle;
            _hoSince = Clock;
        }

        /// <summary>
        /// ⭐ CE-1158 (§8h U-4) — <c>E</c> (or Esc) in a vehicle with a rider: possess the rider as Fps — which releases the vehicle (one Fps
        /// unit per station) — and, once the view is back in him, send his Get-out press. False when <paramref name="vehicle"/> has no rider.
        /// </summary>
        internal bool TryGetOut(Entity vehicle)
        {
            if (_world() is not { } w || Orders() is not { } orders) return false;
            var rider = RiderOf(w, vehicle);
            if (rider.IsNull || !w.GetSingletonManaged<NetworkEntityMap>()!.TryGetNetworkId(rider, out long id)) return false;
            orders.Possess(id, PossessionMode.Fps);
            _getOutRider = rider;
            _getOutSince = Clock;
            return true;
        }

        private void StepHandOver(EntityRepository w)
        {
            if (!_getOutRider.IsNull && Clock - _getOutSince > HandOverTimeout) _getOutRider = Entity.Null;
            if (_step == HandOverStep.None) return;
            if (Clock - _hoSince > HandOverTimeout || !w.IsAlive(_hoSoldier) || !w.IsAlive(_hoVehicle) || Orders() is not { } orders)
            {
                _step = HandOverStep.None;
                return;
            }
            var map = w.GetSingletonManaged<NetworkEntityMap>()!;
            switch (_step)
            {
                case HandOverStep.AwaitBoard:
                    if (!HeldByThis(w, _hoSoldier, out _)) { _step = HandOverStep.None; break; }   // released meanwhile (Esc)
                    if (InDriverSeat(w, _hoSoldier, _hoVehicle) && map.TryGetNetworkId(_hoSoldier, out long soldierId))
                    {
                        orders.Possess(soldierId, PossessionMode.Riding);   // O-2 — his task stays paused
                        _step = HandOverStep.AwaitRiding;
                    }
                    break;
                case HandOverStep.AwaitRiding:
                    if (!HeldByThis(w, _hoSoldier, out var mode)) { _step = HandOverStep.None; break; }
                    // ⚠ only once he RIDES: possessing the vehicle as Fps releases this station's other Fps unit — which he still was
                    if (mode == PossessionMode.Riding && map.TryGetNetworkId(_hoVehicle, out long vehicleId))
                    {
                        orders.Possess(vehicleId, PossessionMode.Fps);
                        _step = HandOverStep.AwaitVehicle;
                    }
                    break;
                case HandOverStep.AwaitVehicle:
                    if (HeldHere(w, out _) == _hoVehicle) _step = HandOverStep.None;
                    break;
            }
        }

        /// <summary>
        /// ⭐ CE-1158 — a rider whose vehicle this station no longer drives (the operator possessed someone else from the menu, the vehicle
        /// was released or destroyed, he was thrown out) is let go, so his own task resumes. Checked once a second (a release takes a
        /// round trip to show).
        /// </summary>
        private void ReleaseOrphanRiders(EntityRepository w)
        {
            if (_step != HandOverStep.None || Clock < _orphanCheckAt || !w.IsComponentTypeRegistered<PossessedBy>()) return;
            _orphanCheckAt = Clock + 1f;
            if (Orders() is not { } orders) return;
            var map = w.GetSingletonManaged<NetworkEntityMap>()!;
            foreach (var unit in w.Query().With<PossessedBy>().Build())
            {
                if (unit == _getOutRider || !HeldByThis(w, unit, out var mode) || mode != PossessionMode.Riding) continue;
                var vehicle = w.IsComponentTypeRegistered<IsEmbarkedTag>() && w.HasComponent<IsEmbarkedTag>(unit)
                    ? w.GetComponentRO<IsEmbarkedTag>(unit).VehicleEntity : Entity.Null;
                if (!vehicle.IsNull && HeldByThis(w, vehicle, out var vehicleMode) && vehicleMode != PossessionMode.Riding) continue;
                if (map.TryGetNetworkId(unit, out long id)) orders.Unpossess(id);
            }
        }

        private static bool HeldByThis(EntityRepository w, Entity unit, out PossessionMode mode)
        {
            mode = PossessionMode.None;
            if (!w.IsComponentTypeRegistered<PossessedBy>() || !w.IsAlive(unit) || !w.HasComponent<PossessedBy>(unit)
                || !w.HasSingletonManaged<NetworkEntityMap>()) return false;
            var held = w.GetComponentRO<PossessedBy>(unit);
            mode = held.Mode;
            return held.StationNodeId == new EntityOrders(w, w.GetSingletonManaged<NetworkEntityMap>()!, geo: null).StationFor(unit);
        }

        private void Enter(EntityRepository w, Entity unit)
        {
            HeldHere(w, out long netId);
            if (!_softLeft)
            {
                _before = _view.CurrentPose;
                _was3D = _view.Is3D;
            }
            _softLeft = false;
            _view.Set(true, animate: false);
            _driver = new FpsCameraDriver(this, _world, unit, netId);
            _view.Camera3D.Driver = _driver;
            if (Bodies != null) Bodies.FirstPerson = unit;   // ⭐ CE-1073 — the camera is inside this head
            // ⭐ CE-1158 (§8h U-4) — back in the rider after Get out: his first frame carries the Get-out press (his only action aboard)
            if (unit == _getOutRider)
            {
                long vehicle = w.IsComponentTypeRegistered<IsEmbarkedTag>() && w.HasComponent<IsEmbarkedTag>(unit) && w.GetSingletonManaged<NetworkEntityMap>()!
                    .TryGetNetworkId(w.GetComponentRO<IsEmbarkedTag>(unit).VehicleEntity, out long v) ? v : 0L;
                _driver.QueueUse(0, vehicle);
                _getOutRider = Entity.Null;
            }
        }

        /// <param name="restore">False during a hand-over: the view stays in first person while possession moves to the other unit.</param>
        private void Leave(bool restore = true)
        {
            _view.Camera3D.Driver = null;   // Detached unlocks the cursor
            _driver = null;
            if (Bodies != null) Bodies.FirstPerson = Entity.Null;
            if (restore) RestoreView();
            else _softLeft = true;
        }

        private void RestoreView()
        {
            _softLeft = false;
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
            var font = HudText.Font();
            // ⭐ CE-1158 (§8h U-1, U-3, O-1, §8h.6) — the use prompt, the hold-use list and the verdict: under the crosshair, where the operator looks
            int uy = cy + 26;
            foreach (var use in _driver.UseLines(w))
            {
                int uw = (int)HudText.Measure(font, use.Text);
                Raylib.DrawRectangle(cx - uw / 2 - 6, uy - 3, uw + 12, HudLineHeight, new Color(0, 0, 0, 130));
                HudText.Draw(font, use.Text, cx - uw / 2, uy, use.Colour);
                uy += HudLineHeight;
            }
            // ⭐ CE-1213 (§8j W-1) — the command wheel round the crosshair while T is held; then what the order did, under it
            if (_driver.WheelOpen) DrawWheel(font, cx, cy);
            else if (_driver.WheelVerdict(Clock) is { } verdict)
            {
                int vw = (int)HudText.Measure(font, verdict);
                Raylib.DrawRectangle(cx - vw / 2 - 6, uy - 3, vw + 12, HudLineHeight, new Color(0, 0, 0, 130));
                HudText.Draw(font, verdict, cx - vw / 2, uy, Color.SkyBlue);
            }
            var lines = Hud.Lines(w, _driver.Unit, _driver.Mount, _driver.IsVehicle, _driver.HullLocked, _driver.Stance, _driver.ShotPending,
                                  _driver.Scheme, _driver.Running);
            // ⭐ CE-1140 — bottom-left of the VISIBLE map (out of the forward view), the labels' font (Roboto, smoothed), a colour per kind
            int width = 0;
            foreach (var line in lines) width = Math.Max(width, (int)HudText.Measure(font, line));
            var (x, y) = HudOrigin(size, lines.Length, width);
            Raylib.DrawRectangle(x - 8, y - 6, width + 16, lines.Length * HudLineHeight + 8, new Color(0, 0, 0, 150));
            for (int i = 0; i < lines.Length; i++)
            {
                HudText.Draw(font, lines[i], x, y, Hud.ColourOf(w, _driver.Unit, lines[i], i == lines.Length - 1));
                y += HudLineHeight;
            }
        }

        /// <summary>⭐ <c>CE-1213</c> — the wheel's ring radius (px): the labels sit on it.</summary>
        public const int WheelRadius = 130;

        /// <summary>⭐ <c>CE-1213</c> — the command wheel: a dark disc, one label per order on the ring (the one under the cursor lit), the
        /// cursor; "no men under your command" when the ring is empty.</summary>
        private void DrawWheel(Font? font, int cx, int cy)
        {
            var items = _driver!.WheelItems;
            Raylib.DrawCircle(cx, cy, WheelRadius + 60, new Color(0, 0, 0, 110));
            Raylib.DrawCircleLines(cx, cy, CommandWheel.DeadZonePx, new Color(255, 255, 255, 90));
            if (items.Count == 0)
            {
                const string none = "no men under your command";
                HudText.Draw(font, none, cx - (int)HudText.Measure(font, none) / 2, cy - HudLineHeight / 2, Color.LightGray);
            }
            int lit = CommandWheel.Pick(_driver.WheelCursor, items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var at = new Vector2(cx, cy) + CommandWheel.Direction(i, items.Count) * WheelRadius;
                int tw = (int)HudText.Measure(font, items[i].Name);
                Raylib.DrawRectangle((int)at.X - tw / 2 - 8, (int)at.Y - HudLineHeight / 2 - 2, tw + 16, HudLineHeight + 4,
                                     i == lit ? new Color(230, 150, 40, 220) : new Color(20, 20, 20, 200));
                HudText.Draw(font, items[i].Name, (int)at.X - tw / 2, (int)at.Y - HudLineHeight / 2 + 2, Color.White);
            }
            Raylib.DrawCircle(cx + (int)_driver.WheelCursor.X, cy + (int)_driver.WheelCursor.Y, 4f, Color.White);
        }

        /// <summary>The HUD's text size and line pitch (px).</summary>
        public const int HudFontSize = 20, HudLineHeight = 24;

        /// <summary>
        /// ⭐ CE-1140 — where the HUD block starts: the BOTTOM-LEFT corner of the visible map (<see cref="Fdp.Presentation.WindowManager.OpenMapArea"/>
        /// — the dockspace's open hole, so no docked panel covers it), out of the forward view. 🔒 User, 2026-10-16: <i>"the HUD could rather
        /// be in the left bottom side of the screen, not obstructing the forward view"</i>. ⛔ SUPERSEDED: CE-1079 put it under the crosshair
        /// because the window's corners were hidden by the panels — the open area answers that now.
        /// </summary>
        public static (int X, int Y) HudOrigin(Vector2 viewport, int lineCount, int width)
        {
            var at = Fdp.Presentation.WindowManager.OpenMapArea.Corner(viewport, left: true, top: false, width, lineCount * HudLineHeight, margin: 16f);
            return ((int)Math.Max(8f, at.X), (int)Math.Max(8f, at.Y));
        }

        /// <summary>⭐ CE-1078 — where the locked shot is: on its unit while it stands, else the locked point. ⭐ CE-1125 — the point the
        /// crosshair put on him, carried with him (the same rule the shot follows, <c>AimPoint.Operator</c>), not his middle.</summary>
        private static Vector3 LockedWorldPoint(EntityRepository w, FpsCameraDriver d)
            => d.LockedTarget != 0 && w.HasSingletonManaged<NetworkEntityMap>()
               && w.GetSingletonManaged<NetworkEntityMap>()!.TryGetEntity(d.LockedTarget, out var e) && w.IsAlive(e) && w.HasComponent<SimTransform>(e)
                ? Fdp.Toolkit.Combat.AimPoint.Operator(w, e, Fdp.Toolkit.Combat.AimPoint.FromUnitFrame(w, e, d.LockedLocal)) : d.LockedPoint;

        /// <summary>
        /// ⭐ CE-1078 — where the selected weapon's turret points: along its posed barrel (the hull's heading + <see cref="TurretPose"/>),
        /// as far out as the crosshair's point. Null for a weapon on no turret. ⚠ The pose carries the ballistic elevation, so at long
        /// range the ring sits a little above the target when the gun is laid.
        /// </summary>
        public static Vector3? BarrelPoint(EntityRepository w, Entity unit, int mount, Vector3 aimAt)
        {
            (unit, mount) = Hud.Resolve(w, unit, mount);   // ⭐ CE-1158 — a controlled weapon's own turret
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
            /// <summary>⭐ CE-1140 — each kind of line its colour, so the eye finds it: health green → yellow → red by how hurt; the
            /// selected weapon yellow, the others grey-white; LOCKED orange; the stance cyan; the flight line light blue; the keys grey.
            /// 🔒 User, 2026-10-16: <i>"colored texts (different kind of info in different color) to easily find the one the player is interested in"</i>.</summary>
            public static Color ColourOf(EntityRepository w, Entity unit, string line, bool keys)
            {
                if (keys || line.StartsWith("LMB", StringComparison.Ordinal) || line.Contains("Esc release", StringComparison.Ordinal))
                    return new Color(175, 175, 175, 255);
                if (line.StartsWith("HEALTH", StringComparison.Ordinal))
                {
                    float f = w.IsComponentTypeRegistered<Health>() && w.HasComponent<Health>(unit) && w.GetComponentRO<Health>(unit).Max > 0f
                        ? w.GetComponentRO<Health>(unit).Current / w.GetComponentRO<Health>(unit).Max : 1f;
                    return f > 0.6f ? new Color(120, 235, 120, 255) : f > 0.3f ? new Color(250, 210, 80, 255) : new Color(255, 90, 80, 255);
                }
                if (line.StartsWith("> ", StringComparison.Ordinal)) return new Color(255, 220, 90, 255);
                if (line.StartsWith("  ", StringComparison.Ordinal)) return new Color(215, 215, 215, 255);
                if (line.StartsWith("LOCKED", StringComparison.Ordinal)) return new Color(255, 150, 50, 255);
                if (line.StartsWith("STANCE", StringComparison.Ordinal)) return new Color(120, 220, 255, 255);
                if (line.StartsWith("SPD", StringComparison.Ordinal)) return new Color(150, 200, 255, 255);
                return Color.RayWhite;
            }

            public static string[] Lines(EntityRepository w, Entity unit, int mount, bool vehicle, bool hullLocked,
                                         Fdp.Toolkit.Tkb.Domain.StanceId? stance = null, bool shotPending = false,
                                         ControlScheme scheme = ControlScheme.Person, bool running = false)
            {
                var lines = new System.Collections.Generic.List<string>();
                bool flyer = scheme is ControlScheme.Rotorcraft or ControlScheme.FixedWing;
                if (flyer) lines.Add(FlightLine(w, unit));   // ⭐ CE-1095 (§8f P-4)
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
                // ⭐ CE-1158 (§8h U-5) — and the weapons he CONTROLS (a manned ZU-23: index 32+k), numbered as their keys
                Span<int> slots = stackalloc int[MaxSlots];
                int slotCount = Slots(w, unit, slots);
                for (int i = 0; i < slotCount; i++)
                {
                    if (!Fdp.Toolkit.Combat.WeaponSystems.IsControlledIndex(slots[i])) continue;
                    var weapon = Fdp.Toolkit.Combat.WeaponSystems.ControlledAt(w, unit, slots[i]);
                    if (w.IsComponentTypeRegistered<WeaponAmmo>() && w.HasComponent<WeaponAmmo>(weapon))
                        lines.Add(WeaponLine(w, weapon, w.GetComponentRO<WeaponAmmo>(weapon), 0, slots[i] == mount, key: i + 1));
                    else lines.Add($"{(slots[i] == mount ? "> " : "  ")}{i + 1} {TypeName(w, weapon)?.ToUpperInvariant() ?? "WEAPON"}");
                }
                if (shotPending) lines.Add("LOCKED - fires when the weapon bears (X cancels)");   // ⭐ CE-1078
                if (!vehicle && !flyer && stance is { } st)
                    lines.Add($"STANCE {st.ToString().ToUpperInvariant()}{(running ? "  RUN" : "")}");   // ⭐ CE-1074 / CE-1143
                lines.Add(scheme switch
                {
                    ControlScheme.FixedWing => "mouse: fly where you look  W/S throttle  Esc release",          // ⭐ CE-1095
                    ControlScheme.Rotorcraft => "W/S/A/D fly  mouse turn  Space/C climb  Esc release",
                    _ => vehicle
                        ? $"W/S throttle  A/D steer  V {(hullLocked ? "free look" : "hull view")}  Esc release"
                        : "W/A/S/D move  Shift run  Ctrl crouch (hold)  C crouch  Z prone  Esc release",
                });
                lines.Add("LMB fire  RMB zoom  X cancel  1-4 / wheel weapon  R ammo  F use (hold F: more)  Q/E lean  T orders");   // ⭐ CE-1078 / CE-1079 / CE-1158
                return lines.ToArray();
            }

            /// <summary>The most weapon slots a unit has (its own mounts, then the weapons it controls).</summary>
            public const int MaxSlots = 32;

            /// <summary>
            /// ⭐ CE-1158 (§8h U-5, module graph GAP1) — the station's weapon list, in key order: the unit's own mounts (<see cref="WeaponAmmo"/>,
            /// 0…n-1), then each weapon system it controls (<see cref="Fdp.Toolkit.Combat.WeaponSystems.Controlled"/>, mount index 32+k —
            /// the index the operator's fire path already takes). A unit with neither keeps mount 0.
            /// </summary>
            public static int Slots(EntityRepository w, Entity unit, Span<int> into)
            {
                int n = 0;
                int own = w.IsComponentTypeRegistered<WeaponAmmo>() && w.HasComponent<WeaponAmmo>(unit) ? w.GetComponentRO<WeaponAmmo>(unit).Count : 0;
                for (int m = 0; m < own && n < into.Length; m++) into[n++] = m;
                Span<Entity> weapons = stackalloc Entity[Fdp.Toolkit.Combat.WeaponSystems.MaxControlled];
                int controlled = Fdp.Toolkit.Combat.WeaponSystems.Controlled(w, unit, weapons);
                for (int k = 0; k < controlled && n < into.Length; k++) into[n++] = Fdp.Toolkit.Combat.WeaponSystems.ControlledMountBase + k;
                if (n == 0 && into.Length > 0) into[n++] = 0;
                return n;
            }

            /// <summary>⭐ CE-1158 — the entity and its own mount a slot fires: a controlled weapon's mount 0, else the unit's mount.</summary>
            public static (Entity Holder, int Mount) Resolve(EntityRepository w, Entity unit, int mount)
            {
                if (!Fdp.Toolkit.Combat.WeaponSystems.IsControlledIndex(mount)) return (unit, mount);
                var weapon = Fdp.Toolkit.Combat.WeaponSystems.ControlledAt(w, unit, mount);
                return weapon.IsNull ? (unit, mount) : (weapon, 0);
            }

            /// <summary>⭐ CE-1158 — a unit's type name from the TKB (<c>BMP-2</c>, <c>ZU-23-2</c>), or null.</summary>
            public static string? TypeName(EntityRepository w, Entity e)
            {
                if (e.IsNull || !w.IsAlive(e) || !w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()
                    || !w.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(e) || !w.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()) return null;
                long type = w.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType;
                return w.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>()!.TryGetByType(type, out var t) && t != null ? t.Name : null;
            }

            /// <summary>⭐ CE-1158 (§8h U-1) — an action as the HUD names it: <c>Open door</c>, <c>Drag</c>, <c>Drive BMP-2</c>, <c>Man ZU-23-2</c>.</summary>
            public static string Label(EntityRepository w, in UseAction a)
                => a.Verb is UseVerbKind.Drive or UseVerbKind.Pilot or UseVerbKind.Board or UseVerbKind.Man && TypeName(w, a.Target) is { } name
                    ? $"{a.Label} {name}" : a.Label;

            /// <summary>⭐ CE-1158 (§8h U-3) — why an action cannot be done, in the HUD's words.</summary>
            public static string ReasonText(UseReason r) => r switch
            {
                UseReason.OutOfReach => "too far",
                UseReason.Locked => "locked",
                UseReason.DoorRefuses => "will not move",
                UseReason.NotDown => "not down",
                UseReason.SeatTaken => "seat taken",
                UseReason.Unfit => "you are down",
                UseReason.Carrying => "hands full",
                UseReason.NothingToDo => "nothing to do",
                UseReason.Failed => "failed",
                _ => "",
            };

            /// <summary>⭐ CE-1158 (§8h U-1, U-3) — the prompt: <c>F  Open door</c>, or greyed with its reason <c>F  Open door (too far)</c> (⭐ CE-1212 — E before).</summary>
            public static string Prompt(EntityRepository w, in UseAction a)
                => a.Available ? $"F  {Label(w, a)}" : $"F  {Label(w, a)} ({ReasonText(a.Reason)})";   // ⭐ CE-1212 — F is the use key

            /// <summary>⭐ CE-1158 (§8h.6 ②) — the brain's verdict on the press of <paramref name="label"/>: done, refused with its reason, or
            /// nothing while it runs.</summary>
            public static string? Verdict(string label, in OperatorUseStatus s) => s.Outcome switch
            {
                UseOutcome.Done => $"{label}: done",
                UseOutcome.Refused => $"{label}: {(s.Reason == UseReason.Ok ? "refused" : ReasonText(s.Reason))}",
                _ => null,
            };

            /// <summary>The use lines' colours: an action that can be done, one greyed, the one picked in the list, a verdict.</summary>
            public static readonly Color UseReady = new(240, 240, 240, 255), UseGreyed = new(150, 150, 150, 255),
                UsePicked = new(255, 220, 90, 255), UseDone = new(120, 235, 120, 255), UseRefused = new(255, 120, 100, 255);

            /// <summary>
            /// ⭐ CE-1079 (§8e) — one weapon: <c>&gt; 1 CANNON  120mm APFSDS 19  41/42  R: HEAT-MP / tank HE-FRAG  LOADING</c> — its number
            /// key, its kind, the round in its breech (and how many of that round), its total, the other rounds <c>F</c> cycles to (the
            /// selected weapon only), and LOADING while it reloads or switches.
            /// </summary>
            /// <param name="key">⭐ CE-1158 — the number shown (its key), when it is not <paramref name="m"/> + 1 (a controlled weapon).</param>
            public static string WeaponLine(EntityRepository w, Entity unit, in WeaponAmmo a, int m, bool selected, int? key = null)
            {
                var dto = Fdp.Toolkit.Combat.CombatTkb.MountOf(w, unit, m);
                var line = new System.Text.StringBuilder();
                line.Append(selected ? "> " : "  ").Append(key ?? m + 1).Append(' ').Append(dto is { Hands: true } ? HandsKind : KindOf(dto?.Category ?? Fdp.Toolkit.Tkb.Domain.WeaponCategory.Unknown));
                long loaded = a.LoadedOf(m) != 0 ? a.LoadedOf(m) : unchecked((long)(dto?.AmmoGuid ?? 0UL));
                if (RoundName(w, loaded) is { } round)
                {
                    line.Append("  ").Append(round);
                    if (a.LoadedRoundsOf(m) >= 0) line.Append(' ').Append(a.LoadedRoundsOf(m));
                }
                int mag = a.MagazineOf(m);
                line.Append("  ").Append(a.AmmoOf(m));
                if (a.MaxOf(m) > 0) line.Append('/').Append(a.MaxOf(m));   // a weapon with no stated maximum shows its count only
                if (mag >= 0) line.Append("  [").Append(mag).Append(']');
                if (selected && dto?.Loadout is { Count: > 1 } loadout)
                {
                    var others = new System.Collections.Generic.List<string>();
                    foreach (var l in loadout)
                        if (unchecked((long)l.AmmoGuid) != loaded && RoundName(w, unchecked((long)l.AmmoGuid)) is { } name) others.Add(name);
                    if (others.Count > 0) line.Append("  R: ").Append(string.Join(" / ", others));   // ⭐ CE-1212 — R is the next round (F before)
                }
                if (a.IsBusy(m)) line.Append("  LOADING");
                return line.ToString();
            }

            /// <summary>
            /// ⭐ CE-1095 (§8f P-4) — a flyer's flight line: <c>SPD 152 m/s  ALT 312 m  HDG 045</c> — its speed (its replicated velocity), its
            /// height above the ground under it (the world query; its own Z where there is none) and its compass heading (0 = north,
            /// clockwise).
            /// </summary>
            public static string FlightLine(EntityRepository w, Entity unit)
            {
                if (!w.HasComponent<SimTransform>(unit)) return "SPD -  ALT -  HDG -";
                var tf = w.GetComponentRO<SimTransform>(unit);
                float speed = w.IsComponentTypeRegistered<SimVelocity>() && w.HasComponent<SimVelocity>(unit) ? w.GetComponentRO<SimVelocity>(unit).Linear.Length() : 0f;
                float ground = Fdp.Toolkit.World.WorldQuery.Of(w)?.GroundHeightAt(tf.Position.X, tf.Position.Y) ?? 0f;
                float yaw = Fdp.Toolkit.Motion.RotorcraftModel.YawOf(tf.Rotation) * 180f / MathF.PI;   // counter-clockwise from east
                int heading = ((int)MathF.Round(90f - yaw) % 360 + 360) % 360;
                return $"SPD {speed:0} m/s  ALT {tf.Position.Z - ground:0} m  HDG {heading:000}";
            }

            /// <summary>⭐ CE-1079 — a weapon's kind, as the HUD names it.</summary>
            /// <summary>⭐ CE-1168 — a person's HANDS mount (backend's B-8, <c>PersonHands</c>: every person ends with one, empty until he picks
            ///   something up) is shown as his hands, not as the grenade category it throws on.</summary>
            public const string HandsKind = "HANDS";

            public static string KindOf(Fdp.Toolkit.Tkb.Domain.WeaponCategory c) => c switch
            {
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms => "RIFLE",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun => "MG",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon => "AUTOCANNON",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.TankGun => "CANNON",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm => "MISSILE",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Mortar => "MORTAR",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Grenade => "GRENADE",
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb => "BOMB",        // ⭐ Air Ops (backend, cross-lane: a name only)
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Rocket => "ROCKETS",   // ⭐ Air Ops 6a (AW-3)
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Howitzer => "HOWITZER",   // ⭐ IF-1 (I8)
                Fdp.Toolkit.Tkb.Domain.WeaponCategory.Sam => "SAM",             // ⭐ CE-2187 (backend, cross-lane: a name only)
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
        /// <summary>⭐ CE-1095 (§8f P-3) — a plane's eye above its reference point (its CG): about the canopy.</summary>
        public const float FixedWingEyeHeight = 1.5f;
        /// <summary>⭐ CE-1095 (§8f P-2) — the look's pitch that asks for the plane's steepest climb or dive (<c>Vertical</c> = ±1) —
        /// the model's own <c>MaxPitchDeg</c>, so a look along the climb it can fly asks for exactly that.</summary>
        public static readonly float FullClimbLook = Fdp.Toolkit.Motion.FixedWingModel.MaxPitchDeg * MathF.PI / 180f;

        /// <summary>⭐ CE-1095 — is this a flyer (plane or helicopter)?</summary>
        public bool IsFlyer => Scheme is ControlScheme.Rotorcraft or ControlScheme.FixedWing;
        /// <summary>How far an aim with nothing under the crosshair reaches (m).</summary>
        public const float AimReach = 1000f;

        private readonly PossessionStation _station;
        private readonly Func<EntityRepository?> _world;
        private readonly long _netId;
        private IInputProvider? _locked;
        private float _yaw, _pitch, _mouseSteer, _roll;
        private bool _lookInitialised;
        private uint _seq;
        private Fdp.Toolkit.Tkb.Domain.StanceId? _stance;   // ⭐ CE-1074 — the stance the operator asked for (null: not yet)
        private bool _crouchHeld;                          // ⭐ CE-1143 — Ctrl held: crouched while held
        private Vector3 _eye;                              // ⭐ CE-1073 — where the eye was placed this frame
        private bool _eyeKnown;
        private byte _shotSeq, _cancelSeq, _ammoSeq;       // ⭐ CE-1078 / CE-1079 — presses, counted (DESIGN_Entity_Control §8e)
        private long _shotTarget;
        private int _ammoAtPress;
        // ⭐ CE-1158 (§8h U-1, U-2, O-1) — the use key: presses counted, the verb (0 = the primary) and the target they carry
        private byte _useSeq, _useVerb;
        private long _useTarget;
        private bool _eHolding, _usedHere, _queuedUse;
        private float _eSince, _verdictSince = -1f;
        private long _pressTarget, _queuedTarget;
        private int _listIndex;
        private byte _queuedVerb;
        private string _lastLabel = "Use";
        private readonly UseAction[] _actions = new UseAction[OperatorUseActions.MaxActions];

        /// <summary>⭐ CE-1158 (O-1) — how long the use key (<c>F</c>, CE-1212) is held before the list of the other verbs opens (s).</summary>
        public const float HoldSeconds = 0.3f;
        /// <summary>⭐ CE-1158 (§8h.6 ②) — how long the brain's verdict line stays (s).</summary>
        public const float VerdictSeconds = 2f;

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
        /// dropped since the press; X clears it — ⭐ CE-1211, RMB before). The HUD's LOCKED.</summary>
        public bool ShotPending { get; private set; }

        /// <summary>⭐ CE-1127 (§8g F-1) — how the selected weapon's trigger works (the brain's own rule, mirrored for the HUD).</summary>
        public Fdp.Toolkit.Combat.OperatorFireMode FireMode
            => _world() is { } w && w.IsAlive(Unit) && PossessionStation.Hud.Resolve(w, Unit, Mount) is var (holder, local)
                ? Fdp.Toolkit.Combat.OperatorFireModes.Of(w, holder, Fdp.Toolkit.Combat.CombatTkb.MountOf(w, holder, local))
                : Fdp.Toolkit.Combat.OperatorFireMode.Sight;

        /// <summary>⭐ CE-1078 — the network id of the unit the last press locked on (0 = the ground point).</summary>
        public long LockedTarget => _shotTarget;

        /// <summary>⭐ CE-1078 — where the last press locked (the unit's box hit, or the ground).</summary>
        public Vector3 LockedPoint { get; private set; }

        /// <summary>⭐ CE-1125 — <see cref="LockedPoint"/> in the locked unit's own frame (the brain holds it the same way), so the ring follows
        /// the point on him as he moves.</summary>
        public Vector3 LockedLocal { get; private set; }

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
                Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing => ControlScheme.FixedWing,   // ⭐ CE-1095 (§8f P-1)
                Fdp.Toolkit.Tkb.Domain.LocomotionClass.Wheeled or Fdp.Toolkit.Tkb.Domain.LocomotionClass.Tracked => ControlScheme.Vehicle,
                _ => ControlScheme.Person,
            };

        /// <summary>The look direction (radians): what the camera shows and, for a person, the body's yaw (M-2).</summary>
        public float Yaw => _yaw;
        public float Pitch => _pitch;

        /// <summary>⭐ CE-1074 — the stance the operator has asked for (a person; null for a vehicle or before the first frame).</summary>
        public Fdp.Toolkit.Tkb.Domain.StanceId? Stance
            => _crouchHeld && _stance is { } st && st != Fdp.Toolkit.Tkb.Domain.StanceId.Prone ? Fdp.Toolkit.Tkb.Domain.StanceId.Crouched : _stance;

        /// <summary>⭐ CE-1143 — Shift is held: the person runs (the frame's gait 0).</summary>
        public bool Running { get; private set; }

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
            // ⭐ CE-1095 (§8f P-3) — a flyer's view banks with the body (the horizon tilts into the turn); its pitch stays the look's
            _roll = IsFlyer ? Fdp.Toolkit.Motion.RotorcraftModel.TiltOf(tf.Rotation).Roll : 0f;
            Place(camera);
        }

        /// <summary>
        /// ⭐ CE-1073 (docs/DESIGN_Map_3D_Mode.md §6l) — the eye: a person's is the eyes of his DRAWN head (the body layer's last frame,
        /// in the body's frame, put on the unit's current position and heading — so the lean of a run, a crouch or a crawl moves the
        /// eye with the head instead of the head swinging in front of a camera fixed above the reference point); a vehicle's is above
        /// its hull, a flyer's at its rotorcraft eye. No figure drawn yet ⇒ the perception eye height, as before.
        /// </summary>
        private Vector3 EyeOf(EntityRepository w, in SimTransform tf, bool vehicle)
        {
            if (Scheme == ControlScheme.Rotorcraft) return tf.Position + new Vector3(0f, 0f, RotorcraftEyeHeight);   // ⭐ CE-2168 (MD-D)
            if (Scheme == ControlScheme.FixedWing) return tf.Position + new Vector3(0f, 0f, FixedWingEyeHeight);     // ⭐ CE-1095 (§8f P-3)
            if (vehicle) return tf.Position + new Vector3(0f, 0f, VehicleEyeHeight);
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
            camera.Roll = _roll;
            camera.Distance = camera.MinDistance;
            camera.LookAt = _eye + camera.Forward * camera.Distance;
            camera.FovyDegrees = ZoomFovDegrees;   // ⭐ CE-1211 (§8j Z-1) — narrower while RMB is held
        }

        /// <summary>⭐ <c>CE-1212</c> (§8j L-1, L-2; keys approved, R-1009) — hold to lean left / right; the use key; the next round.</summary>
        public const KeyboardKey LeanLeftKey = KeyboardKey.Q, LeanRightKey = KeyboardKey.E, UseKeyCode = KeyboardKey.F, NextRoundKey = KeyboardKey.R;

        /// <summary>⭐ <c>CE-1213</c> (§8j W-1; key approved, R-1009) — hold to open the command wheel.</summary>
        public const KeyboardKey CommandWheelKey = KeyboardKey.T;
        /// <summary>⭐ <c>CE-1213</c> — how long the wheel's verdict stays under the crosshair (s).</summary>
        public const float WheelVerdictSeconds = 2.5f;

        /// <summary>⭐ <c>CE-1213</c> — is the command wheel open (T held)?</summary>
        public bool WheelOpen { get; private set; }
        /// <summary>⭐ <c>CE-1213</c> — the ring, fixed when the wheel opened (<see cref="CommandWheel.Items"/>).</summary>
        public IReadOnlyList<Hrot.Common.Orders.OrderInfo> WheelItems { get; private set; } = Array.Empty<Hrot.Common.Orders.OrderInfo>();
        /// <summary>⭐ <c>CE-1213</c> — the wheel's cursor, px from its middle (the mouse moves it while the wheel is open).</summary>
        public Vector2 WheelCursor { get; private set; }
        private string? _wheelVerdict;
        private float _wheelVerdictAt;

        /// <summary>⭐ <c>CE-1213</c> — what the last wheel order did (<c>Move: 3 of 3 men</c>), for <see cref="WheelVerdictSeconds"/>.</summary>
        public string? WheelVerdict(float clock) => _wheelVerdict != null && clock - _wheelVerdictAt <= WheelVerdictSeconds ? _wheelVerdict : null;

        /// <summary>⭐ <c>CE-1213</c> (§8j W-1) — T pressed opens the wheel on his men's orders; held, the mouse moves its cursor; let go,
        /// the sector under the cursor is sent to his men at the crosshair (nothing inside the dead zone). True while it has the mouse.</summary>
        private bool CommandWheelInput(IInputProvider input, Vector2 delta)
        {
            bool held = input.IsKeyDown((MapKeyboardKey)CommandWheelKey);
            if (held && !WheelOpen && _world() is { } ow)
            {
                WheelItems = CommandWheel.Items(ow, Unit);
                WheelCursor = Vector2.Zero;
                WheelOpen = true;
            }
            if (!WheelOpen) return false;
            if (held)
            {
                var cursor = WheelCursor + delta;
                WheelCursor = cursor.Length() > CommandWheel.CursorReachPx ? Vector2.Normalize(cursor) * CommandWheel.CursorReachPx : cursor;
                return true;
            }
            WheelOpen = false;
            int pick = CommandWheel.Pick(WheelCursor, WheelItems.Count);
            if (pick >= 0 && _world() is { } sw)
            {
                var order = WheelItems[pick];
                int accepted = CommandWheel.Send(sw, Unit, order, AimAt, AimTarget);
                _wheelVerdict = $"{order.Name}: {accepted} of {CommandWheel.MenOf(sw, Unit).Count} men";
                _wheelVerdictAt = _station.Clock;
            }
            return true;
        }

        /// <summary>⭐ <c>CE-1212</c> — the lean held this frame (a person only; none in a vehicle).</summary>
        public Fdp.Toolkit.Combat.LeanSide Leaning { get; private set; }

        /// <summary>⭐ <c>CE-1211</c> (§8j Z-1) — is the right button held (looking down the sights or through the scope)?</summary>
        public bool Zooming { get; private set; }

        /// <summary>⭐ <c>CE-1211</c> — the field of view this frame: <see cref="FpsZoom.FovFor"/> the selected weapon while
        /// <see cref="Zooming"/>, else the map's own.</summary>
        public float ZoomFovDegrees => Zooming && _world() is { } zw ? FpsZoom.FovFor(zw, Unit, Mount) : FpsZoom.NormalFovDegrees;

        public bool HandleInput(MapCamera3D camera, IInputProvider input)
        {
            if (_locked == null) { input.SetCursorLocked(true); _locked = input; }
            if (input.IsKeyPressed(MapKeyboardKey.Escape))
            {
                // ⭐ CE-1158 (§8h U-4) — Esc in a vehicle with a rider is Get out (back into him), not a release that strands him aboard
                if (!_station.TryGetOut(Unit)) _station.Release(_netId);
                return true;
            }

            bool vehicle = IsVehicle;
            var delta = input.MouseDelta;
            // ⭐ CE-1213 (§8j W-1) — hold T: the command wheel; while it is open the mouse moves its cursor, not the look
            if (CommandWheelInput(input, delta)) delta = Vector2.Zero;
            // ⭐ CE-1211 (§8j Z-1) — hold RMB to zoom; the look turns slower in proportion to the narrower view
            Zooming = input.IsMouseButtonDown(MapMouseButton.Right);
            float look = FpsZoom.SensitivityFor(ZoomFovDegrees);
            if (vehicle && input.IsKeyPressed((MapKeyboardKey)KeyboardKey.V)) HullLocked = !HullLocked;
            if (vehicle && HullLocked) _mouseSteer = Math.Clamp(_mouseSteer - delta.X * MouseSteerPerPixel, -1f, 1f);
            else _yaw -= delta.X * look;
            _pitch = Math.Clamp(_pitch - delta.Y * look, -1.4f, 1.4f);

            float forward = (input.IsKeyDown((MapKeyboardKey)KeyboardKey.W) ? 1f : 0f) - (input.IsKeyDown((MapKeyboardKey)KeyboardKey.S) ? 1f : 0f);
            float side = (input.IsKeyDown((MapKeyboardKey)KeyboardKey.A) ? 1f : 0f) - (input.IsKeyDown((MapKeyboardKey)KeyboardKey.D) ? 1f : 0f);
            // ⭐ CE-2168 (MD-D) — a flyer's climb axis; ⭐ CE-1095 (§8f P-2) — a plane's is its look's pitch (mouse-aim), and it does not strafe
            var scheme = Scheme;
            float vertical = scheme switch
            {
                ControlScheme.Rotorcraft => (input.IsKeyDown((MapKeyboardKey)KeyboardKey.Space) ? 1f : 0f) - (input.IsKeyDown((MapKeyboardKey)KeyboardKey.C) ? 1f : 0f),
                ControlScheme.FixedWing => Math.Clamp(_pitch / FullClimbLook, -1f, 1f),
                _ => 0f,
            };
            if (scheme == ControlScheme.FixedWing) side = 0f;
            bool run = input.IsKeyDown(MapKeyboardKey.LeftShift) || input.IsKeyDown(MapKeyboardKey.RightShift);
            Running = run;
            float wheel = input.MouseWheelMove;
            Span<int> slots = stackalloc int[PossessionStation.Hud.MaxSlots];
            int slotCount = _world() is { } mw ? PossessionStation.Hud.Slots(mw, Unit, slots) : 0;
            if (slotCount == 0) slots[slotCount++] = 0;
            if (slots.Slice(0, slotCount).IndexOf(Mount) < 0) Mount = slots[0];   // ⭐ CE-1158 — a controlled weapon gone (he left the seat)
            if (UseListOpen)
            {
                // ⭐ CE-1158 (O-1) — while the hold-use list is open the wheel and 1-4 pick a verb, not a weapon
                int verbs = UseCount();
                if (wheel != 0f && verbs > 0) _listIndex = ((_listIndex + (wheel > 0f ? -1 : 1)) % verbs + verbs) % verbs;
                for (int k = 0; k < 4; k++)
                    if (input.IsKeyPressed((MapKeyboardKey)((int)KeyboardKey.One + k)) && k < verbs) _listIndex = k;
            }
            else
            {
                if (wheel != 0f)
                {
                    int at = Math.Max(0, slots.Slice(0, slotCount).IndexOf(Mount));
                    Mount = slots[((at + (wheel > 0f ? -1 : 1)) % slotCount + slotCount) % slotCount];
                }
                // ⭐ CE-1079 (§8e D-2) — 1-4 pick a weapon directly; ⭐ CE-1158 (U-5) the weapons he controls follow his own
                for (int k = 0; k < 4; k++)
                    if (input.IsKeyPressed((MapKeyboardKey)((int)KeyboardKey.One + k)) && k < slotCount) Mount = slots[k];
            }
            // R moves the selected weapon to its next round (⭐ CE-1212, §8j L-2 — F before; F is the use key now)
            if (input.IsKeyPressed((MapKeyboardKey)NextRoundKey)) unchecked { _ammoSeq++; }
            // ⭐ CE-1212 (§8j L-1) — a person leans while Q / E is held (both, or neither: upright); the sim's own rule ignores it prone
            bool leftHeld = input.IsKeyDown((MapKeyboardKey)LeanLeftKey), rightHeld = input.IsKeyDown((MapKeyboardKey)LeanRightKey);
            Leaning = Scheme != ControlScheme.Person || leftHeld == rightHeld ? Fdp.Toolkit.Combat.LeanSide.LeanNone
                    : leftHeld ? Fdp.Toolkit.Combat.LeanSide.LeanLeft : Fdp.Toolkit.Combat.LeanSide.LeanRight;
            // ⭐ CE-1074 — C cycles a person's stance: standing → crouched → prone → standing (from the stance he has when first driven)
            if (Scheme == ControlScheme.Person && _world() is { } sw)   // a person only: on a flyer C is the climb axis (CE-2168)
            {
                _stance ??= Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(sw, Unit);
                // ⭐ CE-1143 — a shooter's keys: hold Ctrl to crouch (letting go stands him back up), C toggles crouched, Z toggles prone.
                //   🔒 User, 2026-10-16: "CTRL when pressed should make him crouch (and releasing ctrl should make him stand again),
                //   special key toggle for prone mode would be better then the cycling today".
                if (input.IsKeyPressed((MapKeyboardKey)KeyboardKey.C)) _stance = ToggleCrouch(_stance.Value);
                if (input.IsKeyPressed((MapKeyboardKey)KeyboardKey.Z)) _stance = ToggleProne(_stance.Value);
                _crouchHeld = input.IsKeyDown(MapKeyboardKey.LeftControl) || input.IsKeyDown(MapKeyboardKey.RightControl);
            }
            Place(camera);   // ⭐ CE-1073 — this frame's look shows this frame (Update ran before the input)

            // ⭐ CE-1078 / CE-1080 (§8e D-1, D-4) — the crosshair sees units too; a PRESS locks one shot on what it rests on, X drops it (CE-1211)
            (var aimAt, long aimTarget) = Aim(camera);
            AimAt = aimAt;
            AimTarget = aimTarget;
            if (input.IsMouseButtonPressed(MapMouseButton.Left))
            {
                unchecked { _shotSeq++; }
                _shotTarget = aimTarget;
                LockedPoint = aimAt;
                LockedLocal = aimTarget != 0 && _world() is { } lw && lw.HasSingletonManaged<NetworkEntityMap>()
                              && lw.GetSingletonManaged<NetworkEntityMap>()!.TryGetEntity(aimTarget, out var lockedUnit)
                    ? Fdp.Toolkit.Combat.AimPoint.ToUnitFrame(lw, lockedUnit, aimAt) : aimAt;
                _ammoAtPress = AmmoOf(Mount);
                ShotPending = FireMode == Fdp.Toolkit.Combat.OperatorFireMode.Lock;   // ⭐ CE-1127 — a SIGHT shot holds nothing to show
            }
            // ⭐ CE-1211 (§8j Z-2) — the locked shot's cancel is X now (RMB zooms, as in every shooter)
            if (input.IsKeyPressed((MapKeyboardKey)KeyboardKey.X)) { unchecked { _cancelSeq++; } ShotPending = false; }
            if (ShotPending && AmmoOf(Mount) < _ammoAtPress) ShotPending = false;   // the round left
            UseKey(input, aimTarget);

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
                Stance = !vehicle && Stance is { } stance ? (byte)((byte)stance + 1) : (byte)0,   // ⭐ CE-1074 / CE-1143 (Ctrl held = crouched)
                ShotSeq = _shotSeq, CancelSeq = _cancelSeq, AmmoSeq = _ammoSeq, ShotTarget = _shotTarget,   // ⭐ CE-1078 / CE-1079
                Lean = (byte)Leaning,                                                                     // ⭐ CE-1212
                UseSeq = _useSeq, UseVerb = _useVerb, UseTarget = _useTarget,                              // ⭐ CE-1158 (U-2)
            };
            _station.Send(_netId, frame);
            return true;
        }

        public void Detached(MapCamera3D camera)
        {
            camera.Roll = 0f;   // ⭐ CE-1095 — the operator's own view is never banked
            camera.FovyDegrees = FpsZoom.NormalFovDegrees;   // ⭐ CE-1211 — never left zoomed
            _locked?.SetCursorLocked(false);
            _locked = null;
        }

        /// <summary>
        /// Where the crosshair rests: ⭐ CE-1080 (§8e D-4) — the nearest of the terrain and the units under the screen centre (never
        /// the operator's own body — <see cref="EntityPartPicker"/> skips it); ⭐ CE-1126 (§8g F-4) a unit by the PART the ray enters (the
        /// round's own boxes), giving that point and its network id. Nothing within <see cref="AimReach"/> ⇒ that far along the look.
        /// </summary>
        public static (Vector3 Point, long Target) Aim(MapCamera3D camera)
        {
            // ⭐ CE-1126 (§8g F-4) — the terrain and the entities' PARTS (the round's own boxes): the point is on his head, arm or hull
            var hit = camera.Pick(camera.Viewport / 2f, PickFilter.Aim);
            if (hit.Kind == PickKind.None || !float.IsFinite(hit.Distance) || hit.Distance > AimReach)
                return (camera.Eye + camera.Forward * AimReach, 0L);
            return hit.Kind == PickKind.Entity ? (camera.Eye + camera.Forward * hit.Distance, hit.NetworkId) : (hit.Point, 0L);
        }

        private int AmmoOf(int mount)
        {
            if (_world() is not { } w) return 0;
            var (holder, local) = PossessionStation.Hud.Resolve(w, Unit, mount);   // ⭐ CE-1158 — a controlled weapon counts its own
            return w.IsComponentTypeRegistered<WeaponAmmo>() && w.HasComponent<WeaponAmmo>(holder) ? w.GetComponentRO<WeaponAmmo>(holder).AmmoOf(local) : 0;
        }

        // ── the use key (CE-1158; docs/DESIGN_Entity_Control.md §8h U-1…U-4, O-1, §8h.6) ──────────────────────────────

        /// <summary>⭐ CE-1158 — the use presses sent (the frame's <c>UseSeq</c>), the last verb and target.</summary>
        public byte UseSeq => _useSeq;
        public byte UseVerb => _useVerb;
        public long UseTarget => _useTarget;

        /// <summary>⭐ CE-1158 (O-1) — <c>E</c> has been held long enough to show the other verbs.</summary>
        public bool UseListOpen => _eHolding && _station.Clock - _eSince >= HoldSeconds;

        /// <summary>⭐ CE-1158 — the verb picked in the open list (0 = the primary).</summary>
        public int UseListIndex => _listIndex;

        /// <summary>⭐ CE-1158 — the next frame carries a use press of <paramref name="verb"/> on <paramref name="target"/> (the station's Get out).</summary>
        internal void QueueUse(byte verb, long target)
        {
            _queuedUse = true;
            _queuedVerb = verb;
            _queuedTarget = target;
        }

        /// <summary>
        /// ⭐ CE-1158 (§8h U-1, O-1) — the use key (<see cref="UseKeyCode"/>, <c>F</c> since CE-1212; <c>E</c> before): pressing it fixes the target (what the crosshair rests on); a TAP sends the primary verb;
        /// held past <see cref="HoldSeconds"/> it opens the list, and letting go sends the verb picked there. Sent on release, as a
        /// counter (§8e D-1: a coalesced frame never loses one). ⭐ In a vehicle with a rider, the use key is Get out (U-4).
        /// </summary>
        private void UseKey(IInputProvider input, long aimTarget)
        {
            if (_queuedUse) { _queuedUse = false; Press(_queuedVerb, _queuedTarget); }
            var e = (MapKeyboardKey)UseKeyCode;   // ⭐ CE-1212 (§8j L-2) — F (E before; E leans now)
            if (!_eHolding && input.IsKeyPressed(e))
            {
                _eHolding = true;
                _eSince = _station.Clock;
                _pressTarget = aimTarget;
                _listIndex = 0;
                return;
            }
            if (!_eHolding || input.IsKeyDown(e)) return;
            int verb = UseListOpen ? _listIndex : 0;
            _eHolding = false;
            if (_station.TryGetOut(Unit)) return;
            var action = Press((byte)verb, _pressTarget);
            if (action is { Verb: UseVerbKind.Drive or UseVerbKind.Pilot } drive) _station.ArmDrive(Unit, drive.Target);
        }

        private UseAction? Press(byte verb, long target)
        {
            unchecked { _useSeq++; }
            _useVerb = verb;
            _useTarget = target;
            _usedHere = true;
            _verdictSince = -1f;
            UseAction? action = null;
            if (_world() is { } w)
            {
                int n = Actions(w, target);
                if (verb < n) action = _actions[verb];
            }
            _lastLabel = action is { } a && _world() is { } lw ? PossessionStation.Hud.Label(lw, a) : "Use";
            return action;
        }

        /// <summary>The actions on <paramref name="target"/> (a network id; 0 = nothing), primary first, into <see cref="_actions"/>.</summary>
        private int Actions(EntityRepository w, long target)
        {
            var entity = Entity.Null;
            if (target != 0 && w.HasSingletonManaged<NetworkEntityMap>()) w.GetSingletonManaged<NetworkEntityMap>()!.TryGetEntity(target, out entity);
            return OperatorUseActions.For(w, Unit, entity, _actions);
        }

        private int UseCount() => _world() is { } w ? Actions(w, _pressTarget) : 0;

        /// <summary>One line by the crosshair and its colour.</summary>
        public readonly record struct UseLine(string Text, Color Colour);

        /// <summary>
        /// ⭐ CE-1158 — what is drawn under the crosshair: the PROMPT (the primary action on what the crosshair rests on, from the same
        /// rule the brain decides with, on this node's own copy of the world — §8h.6 ①), or the hold-use LIST (the picked one marked); then
        /// the brain's VERDICT on our last press for <see cref="VerdictSeconds"/> once it is done or refused (§8h.6 ②).
        /// </summary>
        public System.Collections.Generic.List<UseLine> UseLines(EntityRepository w)
        {
            var lines = new System.Collections.Generic.List<UseLine>();
            if (!w.IsAlive(Unit)) return lines;
            if (UseListOpen)
            {
                int n = Actions(w, _pressTarget);
                for (int i = 0; i < n; i++)
                    lines.Add(new UseLine((i == _listIndex ? "> " : "  ") + (i + 1) + "  " + PossessionStation.Hud.Prompt(w, _actions[i])[3..],
                        i == _listIndex ? PossessionStation.Hud.UsePicked : _actions[i].Available ? PossessionStation.Hud.UseReady : PossessionStation.Hud.UseGreyed));
            }
            else if (Actions(w, _eHolding ? _pressTarget : AimTarget) > 0)
                lines.Add(new UseLine(PossessionStation.Hud.Prompt(w, _actions[0]),
                    _actions[0].Available ? PossessionStation.Hud.UseReady : PossessionStation.Hud.UseGreyed));
            if (VerdictLine(w) is { } verdict) lines.Add(verdict);
            return lines;
        }

        private UseLine? VerdictLine(EntityRepository w)
        {
            if (!_usedHere || !w.IsComponentTypeRegistered<OperatorUseStatus>() || !w.HasComponent<OperatorUseStatus>(Unit)) return null;
            var status = w.GetComponentRO<OperatorUseStatus>(Unit);
            if (status.UseSeq != _useSeq || PossessionStation.Hud.Verdict(_lastLabel, status) is not { } text) return null;
            if (_verdictSince < 0f) _verdictSince = _station.Clock;
            if (_station.Clock - _verdictSince > VerdictSeconds) return null;
            return new UseLine(text, status.Outcome == UseOutcome.Done ? PossessionStation.Hud.UseDone : PossessionStation.Hud.UseRefused);
        }

        /// <summary>⭐ CE-1143 — C: crouched ↔ standing (from prone it crouches).</summary>
        public static Fdp.Toolkit.Tkb.Domain.StanceId ToggleCrouch(Fdp.Toolkit.Tkb.Domain.StanceId s)
            => s == Fdp.Toolkit.Tkb.Domain.StanceId.Crouched ? Fdp.Toolkit.Tkb.Domain.StanceId.Standing : Fdp.Toolkit.Tkb.Domain.StanceId.Crouched;

        /// <summary>⭐ CE-1143 — Z: prone ↔ standing.</summary>
        public static Fdp.Toolkit.Tkb.Domain.StanceId ToggleProne(Fdp.Toolkit.Tkb.Domain.StanceId s)
            => s == Fdp.Toolkit.Tkb.Domain.StanceId.Prone ? Fdp.Toolkit.Tkb.Domain.StanceId.Standing : Fdp.Toolkit.Tkb.Domain.StanceId.Prone;

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
    /// <para>⭐ CE-1095 (DESIGN_Entity_Control §8f P-1) — and a <see cref="FixedWing"/> plane flies by its operator's LOOK (mouse-aim).</para>
    public enum ControlScheme : byte { Person = 0, Vehicle = 1, Rotorcraft = 2, FixedWing = 3 }
}
