using System;
using System.Linq;
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

        /// <summary>⭐ <c>CE-1201</c> (docs/DESIGN_Entity_Control.md §8p OS-1) — the view is over his shoulder (a vehicle: a chase camera)
        /// instead of from his eyes. A station setting: View › First Person: Over the Shoulder, or O while in first person; it stays for
        /// the next possession. ⭐ <c>CE-1293</c> (docs/DESIGN_Unit_Camera.md UC-3) — ON by default: move over the shoulder, aim from the
        /// eyes (RMB).</summary>
        public bool OverShoulder { get; set; } = true;

        /// <summary>⭐ <c>CE-1293</c> (docs/DESIGN_Unit_Camera.md UC-11, R-1033) — the shoulder view's camera distance per kind (a person,
        /// a vehicle, a flyer), set with the wheel (R-1034); a station setting, so it stays for the next possession.</summary>
        public float ShoulderDistance(ControlScheme scheme)
            => _shoulderDistances.TryGetValue(Kind(scheme), out var d) ? d : UnitCameraRig.DefaultDistance(Kind(scheme));

        /// <summary>⭐ <c>CE-1293</c> — sets <see cref="ShoulderDistance"/>, kept within 0 … the kind's playing maximum.</summary>
        public void SetShoulderDistance(ControlScheme scheme, float distance)
            => _shoulderDistances[Kind(scheme)] = Math.Clamp(distance, 0f, UnitCameraRig.MaxPlayDistance(Kind(scheme)));

        private static ControlScheme Kind(ControlScheme scheme) => scheme == ControlScheme.FixedWing ? ControlScheme.Rotorcraft : scheme;
        private readonly Dictionary<ControlScheme, float> _shoulderDistances = new();

        /// <summary>⭐ <c>CE-1293</c> (UC-6) — which shoulder the camera is over: +1 right (the default), −1 left; Q / E set it.</summary>
        public int ShoulderSide { get; set; } = 1;

        public string Name => "Possession (FPS)";
        public int LayerBitIndex => -1;
        public bool Has3D => true;

        /// <summary>True while this station drives a unit in first person.</summary>
        public bool InFps => _driver != null;

        /// <summary>The unit driven now (<see cref="Entity.Null"/> when not in FPS).</summary>
        public Entity Unit => _driver?.Unit ?? Entity.Null;

        /// <summary>The weapon mount the operator has selected (1-4).</summary>
        public int SelectedMount => _driver?.Mount ?? 0;

        /// <summary>⭐ <c>CE-1262</c> (AV-1) — what the crosshair rests on (network id, 0 = none) while in first person.</summary>
        public long AimTarget => _driver?.AimTarget ?? 0L;

        /// <summary>⭐ <c>CE-1262</c> — the zoomed field of view (binoculars / weapon zoom) while zooming, else null.</summary>
        public float? ZoomFovDegrees => _driver is { Zooming: true } d ? d.ZoomFovDegrees : null;

        /// <summary>Frames sent this session (diagnostics, rails).</summary>
        public long FramesSent { get; private set; }

        /// <summary>Seconds this station has run (the use key's hold and the verdict line time against it).</summary>
        public float Clock { get; private set; }

        /// <summary>⭐ <c>CE-1204</c> (§8l LK-4, R-1017) — the operator's choice for a person's look: free of his body (V) or locked (the
        /// default). The station's, not the driver's, so it stays across possessions and Tab.</summary>
        public bool PersonLookFree { get; internal set; }

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
            // ⭐ CE-1216 (§8k S-2) — a round passing his ear (the audio's own rule, RoundPasses) shocks the view; it fades in ShockSeconds
            Shock = MathF.Max(0f, Shock - dt / ShockSeconds);
            if (_driver != null && w is not null)
                Shock = MathF.Min(1f, Shock + ShockPerPass * _passes.Scan(w, _driver.Unit, _view.Camera3D.Eye));
            var held = w is null ? Entity.Null : HeldHere(w, out _);
            bool handing = _step != HandOverStep.None || !_getOutRider.IsNull;
            if (MapPeek && _view.Is3D && !_view.IsSwitching) MapPeek = false;   // ⭐ CE-1218 — something else brought the 3-D view back
            // ⭐ CE-1218 — M on the map ends the peek. ⭐ CE-1295 — but NOT on the frame that opened it: the canvas runs the camera driver's input
            //   (which opens the peek on that M) BEFORE its layers' Update (this), and IsKeyPressed still reads true — so the same press used to
            //   end the peek it had just begun (a one-frame flash back to 3-D). One press = one toggle.
            if (MapPeek && _peekJustBegun) _peekJustBegun = false;
            else if (MapPeek && _driver?.Input is { } peekInput && peekInput.IsKeyPressed((MapKeyboardKey)FpsCameraDriver.MapKey)) EndMapPeek();
            if (_driver != null && (held.IsNull || held != _driver.Unit))
            {
                // ⭐ CE-1220 (§8k K-1) — released because he FELL: the fall screen holds the view; a release just after a hit waits a
                //   moment for his death to arrive (the release and the health may replicate in either order)
                var gone = _driver.Unit;
                if (held.IsNull && w is not null && Fdp.Toolkit.Combat.CombatLife.IsDestroyed(w, gone)) BeginFall(w, gone);
                else if (held.IsNull && w is not null && !handing && JustHit(w, gone) && WaitingForDeath()) { /* keep the view a moment */ }
                else Leave(restore: !handing);
            }
            else _releasedAt = -1f;
            if (_driver == null && !held.IsNull && w is not null) Enter(w, held);
            if (_driver == null && _softLeft && !handing && Fall == null) RestoreView();   // a hand-over that came to nothing
            if (Fall != null && w is not null) Fall.Step(w, dt);
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
                // ⭐ CE-1210 (§8n MS-7) — the station plays a GUN of his vehicle (seat switching): he stays held for the way back
                if (!vehicle.IsNull && HeldHere(w, out _) is var fps && !fps.IsNull && SeatSwitch.VehicleOf(w, fps) == vehicle) continue;
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
            Fall = null;   // ⭐ CE-1220 — a squad mate taken from the fall screen
            _releasedAt = -1f;
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
            MapPeek = false;                // ⭐ CE-1218 — a peek ends with the possession
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

        /// <summary>⭐ <c>CE-1214</c> (§8j T-1) — the operator jumps to <paramref name="next"/>: one possession, which also lets the unit he
        /// leaves go back to his brain (<see cref="EntityOrders.Possess(long, PossessionMode)"/> releases the station's other Fps unit —
        /// C-8); the view follows <see cref="PossessedBy"/>.</summary>
        internal void SwitchTo(Entity next)
        {
            if (Orders() is { } orders && _world() is { } w && w.GetSingletonManaged<NetworkEntityMap>()!.TryGetNetworkId(next, out long id))
                orders.Possess(id, PossessionMode.Fps);
        }

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
            if (Fall != null && _world() is { } fw) { DrawFall(fw); return; }   // ⭐ CE-1220 (§8k K-1)
            if (_driver == null || _world() is not { } w) return;
            if (MapPeek) { DrawPeekHint(); return; }   // ⭐ CE-1218 (§8k M-1) — the 2-D map, not his view
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
            // ⭐ CE-1293 (docs/DESIGN_Unit_Camera.md UC-9) — the round would stop short of the crosshair (a wall the camera sees past): an X
            //   where it would really hit
            if (BlockedShot(w, _driver.Unit, _driver.Mount, _driver.AimAt) is { } stop && OnScreen(camera, stop, out var hit)
                && Vector2.Distance(hit, new Vector2(cx, cy)) > 4f)
            {
                Raylib.DrawLine((int)hit.X - 5, (int)hit.Y - 5, (int)hit.X + 5, (int)hit.Y + 5, Color.Red);
                Raylib.DrawLine((int)hit.X - 5, (int)hit.Y + 5, (int)hit.X + 5, (int)hit.Y - 5, Color.Red);
            }
            if (!_driver.BinocularsUp) DrawSpreadAndShock(w, camera, cx, cy);   // ⭐ CE-1216 (§8k S-1, S-2) — not through binoculars
            else DrawBinoculars(camera, cx, cy);                             // ⭐ CE-1219 (§8k B-1)
            DrawHitFeedback(w, camera, cx, cy);   // ⭐ CE-1215 (§8j H-1, H-2)
            DrawDown(w, camera);                  // ⭐ CE-1220 (§8k K-1)
            var font = HudText.Font();
            DrawFriends(w, camera, font);   // ⭐ CE-1217 (§8k C-2)
            DrawCompass(camera, font, cx);  // ⭐ CE-1217 (§8k C-1)
            // ⭐ CE-1158 (§8h U-1, U-3, O-1, §8h.6) — the use prompt, the hold-use list and the verdict: under the crosshair, where the operator looks
            int uy = cy + 26;
            foreach (var use in _driver.UseLines(w))
            {
                int uw = (int)HudText.Measure(font, use.Text);
                Raylib.DrawRectangle(cx - uw / 2 - 6, uy - 3, uw + 12, HudLineHeight, new Color(0, 0, 0, 130));
                HudText.Draw(font, use.Text, cx - uw / 2, uy, use.Colour);
                uy += HudLineHeight;
            }
            // ⭐ CE-1359 (W-8c) — the gun cannot turn to the crosshair: say so, where he looks
            if (BeyondTraverse(w, _driver.Unit, _driver.Mount, _driver.AimAt))
            {
                int bw = (int)HudText.Measure(font, BeyondArcText);
                Raylib.DrawRectangle(cx - bw / 2 - 6, uy - 3, bw + 12, HudLineHeight, new Color(0, 0, 0, 130));
                HudText.Draw(font, BeyondArcText, cx - bw / 2, uy, Color.Orange);
                uy += HudLineHeight;
            }
            // ⭐ CE-1213 (§8j W-1) — the command wheel round the crosshair while T is held; then what the order did, under it
            if (_driver.WheelOpen) DrawWheel(font, cx, cy);
            else if ((_driver.NoBinoculars ? "no binoculars" : _driver.WheelVerdict(Clock)) is { } verdict)
            {
                int vw = (int)HudText.Measure(font, verdict);
                Raylib.DrawRectangle(cx - vw / 2 - 6, uy - 3, vw + 12, HudLineHeight, new Color(0, 0, 0, 130));
                HudText.Draw(font, verdict, cx - vw / 2, uy, Color.SkyBlue);
            }
            var lines = Hud.Lines(w, _driver.Unit, _driver.Mount, _driver.IsVehicle, _driver.HullLocked, _driver.Stance, _driver.ShotPending,
                                  _driver.Scheme, _driver.Running, _driver.LookFree, Hud.SeatLine(w, _driver.Unit));
            if (OverShoulder && lines.Length > 0) lines = [.. lines[..^1], Hud.ShoulderLine, lines[^1]];   // ⭐ CE-1201 (§8p OS-1)
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

        /// <summary>
        /// ⭐ <c>CE-1215</c> (§8j H-1, H-2) — what <see cref="Hrot.UI.Common.Effects.HitFeedback"/> says about the possessed unit: a HIT
        /// MARKER (a cross round the crosshair, white; red when the one he hit is down) for <see cref="Hrot.UI.Common.Effects.HitFeedback.MarkerSeconds"/>
        /// after he hit someone; a red DAMAGE ARC at the screen's edge toward where each hit on him came from, fading over
        /// <see cref="Hrot.UI.Common.Effects.HitFeedback.ArcSeconds"/>, the thicker the more damage. Each read once a frame from sim time.
        /// </summary>
        private void DrawHitFeedback(EntityRepository w, MapCamera3D camera, int cx, int cy)
        {
            if (Hrot.UI.Common.Effects.HitFeedback.Of(w) is not { } feedback) return;
            var unit = _driver!.Unit;
            double now = Fdp.Toolkit.Time.SimClock.Of(w).TotalTime;
            foreach (var hit in feedback.Recent(now, Hrot.UI.Common.Effects.HitFeedback.MarkerSeconds))
            {
                if (hit.Shooter != unit) continue;
                var colour = Fdp.Toolkit.Combat.CombatLife.IsDestroyed(w, hit.Target) ? Color.Red : Color.White;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        Raylib.DrawLineEx(new Vector2(cx + sx * 7, cy + sy * 7), new Vector2(cx + sx * 14, cy + sy * 14), 2f, colour);
                break;
            }
            float radius = MathF.Min(camera.Viewport.X, camera.Viewport.Y) * 0.38f;
            foreach (var hit in feedback.Recent(now, Hrot.UI.Common.Effects.HitFeedback.ArcSeconds))
            {
                if (hit.Target != unit) continue;
                Vector3? shooterAt = !hit.Shooter.IsNull && w.IsAlive(hit.Shooter) && w.HasComponent<SimTransform>(hit.Shooter)
                    ? w.GetComponentRO<SimTransform>(hit.Shooter).Position : null;
                if (Hrot.UI.Common.Effects.HitFeedback.FromWhere(hit, shooterAt) is not { } from
                    || Hrot.UI.Common.Effects.HitFeedback.ScreenAngle(camera.Forward, from) is not { } angle) continue;
                float fade = 1f - (float)((now - hit.Time) / Hrot.UI.Common.Effects.HitFeedback.ArcSeconds);
                float thick = HitArcThickness(hit.Damage);
                Raylib.DrawRing(new Vector2(cx, cy), radius, radius + thick, angle - 22f, angle + 22f, 24,
                                new Color(220, 30, 30, (int)(200 * Math.Clamp(fade, 0f, 1f))));
            }
        }

        /// <summary>⭐ <c>CE-1219</c> (§8k B-1) — through the binoculars: the two eyepieces' rims, the range to the point under the crosshair
        /// (the eye to it, m), and the click's meaning.</summary>
        private void DrawBinoculars(MapCamera3D camera, int cx, int cy)
        {
            float r = camera.Viewport.Y * 0.42f;
            var rim = new Color(0, 0, 0, 200);
            Raylib.DrawRing(new Vector2(cx - r * 0.55f, cy), r, r + 600f, 0f, 360f, 64, rim);
            Raylib.DrawRing(new Vector2(cx + r * 0.55f, cy), r, r + 600f, 0f, 360f, 64, rim);
            var font = HudText.Font();
            string range = $"RANGE {Vector3.Distance(camera.Eye, _driver!.AimAt):0} m   LMB designate";
            int tw = (int)HudText.Measure(font, range);
            Raylib.DrawRectangle(cx - tw / 2 - 6, cy + 40, tw + 12, HudLineHeight, new Color(0, 0, 0, 160));
            HudText.Draw(font, range, cx - tw / 2, cy + 42, Color.Gold);
        }

        /// <summary>⭐ <c>CE-1220</c> (§8k K-1) — how long the fall screen fades to its colour (s), how long a kill cam runs (s), and how long a
        /// release just after a hit waits for the death (s).</summary>
        public const float FallFadeSeconds = 1f, KillCamSeconds = 3f, FallGraceSeconds = 0.5f;
        private float _releasedAt = -1f;

        /// <summary>⭐ <c>CE-1220</c> — the world this station reads (the fall screen's camera reads it too).</summary>
        internal EntityRepository? World => _world();

        /// <summary>⭐ <c>CE-1220</c> — the fall screen while one is up (he was killed while held), else null.</summary>
        public FallScreen? Fall { get; private set; }

        /// <summary>⭐ <c>CE-1220</c> — true for <see cref="FallGraceSeconds"/> after the release was first seen.</summary>
        private bool WaitingForDeath()
        {
            if (_releasedAt < 0f) _releasedAt = Clock;
            return Clock - _releasedAt < FallGraceSeconds;
        }

        private static bool JustHit(EntityRepository w, Entity unit)
            => Hrot.UI.Common.Effects.HitFeedback.Of(w) is { } fb
               && fb.Recent(Fdp.Toolkit.Time.SimClock.Of(w).TotalTime, 1.0).Any(h => h.Target == unit);

        /// <summary>⭐ <c>CE-1220</c> (§8k K-1) — he fell while held: drop the first-person driver but keep the 3-D view (the fall screen's
        /// camera holds it); who hit him is the last shooter <c>HitFeedback</c> saw on him.</summary>
        private void BeginFall(EntityRepository w, Entity dead)
        {
            var feedback = Hrot.UI.Common.Effects.HitFeedback.Of(w);
            var killer = Entity.Null;
            if (feedback != null)
                foreach (var hit in feedback.Recent(Fdp.Toolkit.Time.SimClock.Of(w).TotalTime, Hrot.UI.Common.Effects.HitFeedback.KeepSeconds))
                    if (hit.Target == dead && !hit.Shooter.IsNull) killer = hit.Shooter;
            Leave(restore: false);
            Fall = new FallScreen(this, dead, killer, Clock);
            _view.Camera3D.Driver = Fall;
        }

        /// <summary>⭐ <c>CE-1220</c> — Esc on the fall screen: the map as the operator had it before he took the man.</summary>
        internal void EndFall()
        {
            Fall = null;
            _view.Camera3D.Driver = null;
            RestoreView();
        }

        /// <summary>⭐ <c>CE-1220</c> — Tab on the fall screen: the first living man of the fallen man's squad (his leader, the men naming that
        /// leader, his own men — <see cref="SquadRoster"/>); nothing when none is left.</summary>
        internal void FallToSquadMate()
        {
            if (Fall == null || _world() is not { } w) return;
            var roster = SquadRoster.Of(w, Fall.Dead);
            if (roster.Count > 0) SwitchTo(roster[0].Unit);
        }

        /// <summary>⭐ <c>CE-1220</c> — the fall screen's shade and lines (and the kill cam's label).</summary>
        private void DrawFall(EntityRepository w)
        {
            var size = _view.Camera3D.Viewport;
            var font = HudText.Font();
            var fall = Fall!;
            bool cam = fall.KillCamOn(Clock);
            float fade = Math.Clamp((Clock - fall.Since) / FallFadeSeconds, 0f, 1f);
            Raylib.DrawRectangle(0, 0, (int)size.X, (int)size.Y, new Color(70, 0, 0, (int)((cam ? 60 : 170) * fade)));
            if (fade < 1f && !cam) return;
            string head = cam ? $"KILL CAM - {fall.KillerName(w)}" : $"KILLED - by {fall.KillerName(w)}";
            var options = new List<string>();
            if (SquadRoster.Of(w, fall.Dead).Count > 0) options.Add("Tab  a squad mate");
            if (fall.CanKillCam(w)) options.Add("K  who hit you");
            options.Add("Esc  the map");
            string opts = string.Join("     ", options);
            int hw = (int)HudText.Measure(font, head), ow = (int)HudText.Measure(font, opts);
            int cy = cam ? 60 : (int)size.Y / 2 - HudLineHeight;
            HudText.Draw(font, head, (int)size.X / 2 - hw / 2, cy, cam ? Color.Gold : new Color(255, 120, 120, 255));
            HudText.Draw(font, opts, (int)size.X / 2 - ow / 2, cy + HudLineHeight + 8, Color.White);
        }

        /// <summary>⭐ <c>CE-1220</c> (§8k K-1) — a possessed man knocked DOWN (alive, <c>Effects.IsKnockedDown</c>): the view dims, "DOWN".</summary>
        private void DrawDown(EntityRepository w, MapCamera3D camera)
        {
            if (!Fdp.Toolkit.Combat.Effects.IsKnockedDown(w, _driver!.Unit)) return;
            var size = camera.Viewport;
            Raylib.DrawRectangle(0, 0, (int)size.X, (int)size.Y, new Color(0, 0, 0, 120));
            var font = HudText.Font();
            const string down = "DOWN";
            HudText.Draw(font, down, (int)size.X / 2 - (int)HudText.Measure(font, down) / 2, (int)size.Y / 3, new Color(255, 170, 60, 255));
        }

        /// <summary>⭐ <c>CE-1218</c> (§8k M-1) — how far the 2-D map's camera stands off him when it opens (m): a few hundred metres across.</summary>
        public const float PeekDistance = 400f;
        private float _peekEndedAt = -1f;
        private bool _peekJustBegun;   // ⭐ CE-1295

        /// <summary>⭐ <c>CE-1218</c> (§8k M-1) — is the operator looking at the 2-D map (M) while still holding his unit?</summary>
        public bool MapPeek { get; private set; }

        /// <summary>⭐ <c>CE-1218</c> — the clock of the frame the peek ended (the driver ignores that frame's M, the same press).</summary>
        internal float PeekEndedAt => _peekEndedAt;

        /// <summary>⭐ <c>CE-1218</c> (§8k M-1) — the 2-D map, centred on him; possession kept (the driver stays, so the map's own shortcuts stay
        /// silent and nothing re-enters first person); the cursor freed to pan.</summary>
        internal void BeginMapPeek(FpsCameraDriver driver)
        {
            if (_world() is not { } w || !w.IsAlive(driver.Unit) || !w.HasComponent<SimTransform>(driver.Unit)) return;
            var at = w.GetComponentRO<SimTransform>(driver.Unit).Position;
            MapPeek = true;
            _peekJustBegun = true;   // ⭐ CE-1295 — this frame's M opened it; the end check skips that frame
            driver.Input?.SetCursorLocked(false);
            _view.ShowPose(new CameraPose(at, PeekDistance, MapCamera3D.NorthUpYaw, MapCamera3D.OverheadPitch));
        }

        /// <summary>⭐ <c>CE-1218</c> — back to his eyes (M on the map): the 3-D view again, the driver places it next frame; the cursor locked.</summary>
        internal void EndMapPeek()
        {
            MapPeek = false;
            _peekJustBegun = false;
            _peekEndedAt = Clock;
            if (_driver == null) return;
            _driver.Input?.SetCursorLocked(true);
            _view.ShowPose(_view.Camera3D.Pose with { Pitch = 0f });   // any non-overhead pose is the 3-D view; the driver places it
        }

        /// <summary>⭐ <c>CE-1218</c> — the one line on the map while he is held: how to come back.</summary>
        private void DrawPeekHint()
        {
            var font = HudText.Font();
            const string hint = "M - back to first person (he is holding still)";
            var size = _view.Camera3D.Viewport;
            int w = (int)HudText.Measure(font, hint);
            Raylib.DrawRectangle((int)size.X / 2 - w / 2 - 8, 8, w + 16, HudLineHeight + 4, new Color(0, 0, 0, 160));
            HudText.Draw(font, hint, (int)size.X / 2 - w / 2, 10, Color.Gold);
        }

        /// <summary>⭐ <c>CE-1217</c> (§8k C-1) — the compass strip at the top: ±60° of heading, a tick every 15°, the eight points named, the
        /// heading's number in a box in the middle; every scheme (the flyer's HDG line stays).</summary>
        private static void DrawCompass(MapCamera3D camera, Font? font, int cx)
        {
            float heading = FpsCompass.Heading(camera.Forward);
            int half = (int)MathF.Min(camera.Viewport.X * 0.3f, 360f), top = 12, height = 30;
            Raylib.DrawRectangle(cx - half - 8, top - 4, 2 * half + 16, height + 8, new Color(0, 0, 0, 110));
            foreach (var (at, degrees, label) in FpsCompass.Ticks(heading))
            {
                int x = cx + (int)(at * half);
                bool major = label.Length > 0;
                Raylib.DrawLine(x, top, x, top + (major ? 10 : 6), new Color(255, 255, 255, major ? 230 : 150));
                if (major) HudText.Draw(font, label, x - (int)HudText.Measure(font, label) / 2, top + 10, Color.White);
            }
            string mid = ((int)MathF.Round(heading) % 360).ToString("000");
            int mw = (int)HudText.Measure(font, mid);
            Raylib.DrawRectangle(cx - mw / 2 - 4, top + height + 2, mw + 8, HudLineHeight, new Color(0, 0, 0, 150));
            HudText.Draw(font, mid, cx - mw / 2, top + height + 4, Color.Gold);
        }

        /// <summary>⭐ <c>CE-1217</c> (§8k C-2) — the names of his side's units within 50 m over their heads (the top of the drawn body, else
        /// a man's 1.9 m), small and green — his own men brighter.</summary>
        private void DrawFriends(EntityRepository w, MapCamera3D camera, Font? font)
        {
            var map = w.HasSingletonManaged<NetworkEntityMap>() ? w.GetSingletonManaged<NetworkEntityMap>() : null;
            foreach (var friend in FriendMarks.Of(w, _driver!.Unit, camera.Eye))
            {
                float top = map != null && map.TryGetNetworkId(friend.Unit, out long id) && Bodies?.TopAbove(id) is { } t ? t : 1.9f;
                if (!OnScreen(camera, friend.At + new Vector3(0f, 0f, top + 0.1f), out var screen)) continue;
                int tw = (int)HudText.Measure(font, friend.Name);
                HudText.Draw(font, friend.Name, (int)screen.X - tw / 2, (int)screen.Y - HudFontSize,
                             friend.Mine ? new Color(140, 255, 140, 255) : new Color(90, 200, 90, 210));
            }
        }

        /// <summary>⭐ <c>CE-1216</c> (§8k S-2) — how long a near miss darkens the edges (s), and how much one pass adds (they stack to 1).</summary>
        public const float ShockSeconds = 0.4f, ShockPerPass = 0.5f;
        private readonly Hrot.UI.Common.Audio.RoundPasses _passes = new();

        /// <summary>⭐ <c>CE-1216</c> (§8k S-2) — 0 … 1: how hard the last near misses darkened the view's edges.</summary>
        public float Shock { get; private set; }

        /// <summary>⭐ <c>CE-1216</c> (§8k S-1) — the sim's own spread of his shots (<see cref="Fdp.Toolkit.Combat.HitModel.Sigma"/>, radians,
        /// the half-width) with the selected weapon, on this node's copy of him; 0 when the weapon states none.</summary>
        public static float SpreadOf(EntityRepository w, Entity unit, int slot)
        {
            var (platform, mount) = FpsZoom.PlatformOf(w, unit, slot);
            if (platform.IsNull || !w.IsAlive(platform)) return 0f;
            return Fdp.Toolkit.Combat.HitModel.Sigma(w, platform, Fdp.Toolkit.Combat.CombatTkb.MountOf(w, platform, mount),
                                                     Fdp.Toolkit.Time.SimClock.Of(w).TotalTime);
        }

        /// <summary>⭐ <c>CE-1216</c> (§8k S-1) — the spread ring's radius on the screen (px): σ at the view's vertical field of view.</summary>
        public static float SpreadRadiusPx(float sigma, float fovDegrees, float viewportHeight)
            => sigma <= 0f ? 0f : MathF.Tan(sigma) / MathF.Tan(fovDegrees * MathF.PI / 360f) * viewportHeight / 2f;

        /// <summary>⭐ <c>CE-1216</c> (§8k S-1, S-2) — the spread ring round the crosshair, and the edges darkened by near misses.</summary>
        private void DrawSpreadAndShock(EntityRepository w, MapCamera3D camera, int cx, int cy)
        {
            float r = SpreadRadiusPx(SpreadOf(w, _driver!.Unit, _driver.Mount), camera.FovyDegrees, camera.Viewport.Y);
            if (r >= 4f) Raylib.DrawCircleLines(cx, cy, MathF.Min(r, camera.Viewport.Y / 2f), new Color(255, 255, 255, 110));
            if (Shock <= 0f) return;
            int width = (int)camera.Viewport.X, height = (int)camera.Viewport.Y, band = (int)(MathF.Min(width, height) * 0.18f);
            var dark = new Color(0, 0, 0, (int)(170 * Shock));
            var clear = new Color(0, 0, 0, 0);
            Raylib.DrawRectangleGradientH(0, 0, band, height, dark, clear);
            Raylib.DrawRectangleGradientH(width - band, 0, band, height, clear, dark);
            Raylib.DrawRectangleGradientV(0, 0, width, band, dark, clear);
            Raylib.DrawRectangleGradientV(0, height - band, width, band, clear, dark);
        }

        /// <summary>⭐ <c>CE-1215</c> (H-2) — the damage arc's thickness (px): 6 for an unknown damage (a remote hit, a blow), growing with
        /// the damage up to 18.</summary>
        public static float HitArcThickness(float damage) => damage <= 0f ? 6f : Math.Clamp(6f + damage * 0.2f, 6f, 18f);

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
        /// <summary>⭐ <c>CE-1359</c> (docs/DESIGN_Weapon_Layers.md W-8c) — the words under the crosshair while the selected gun cannot traverse
        /// to it (a door gun's arc): its fire is held, so the operator is told why.</summary>
        public const string BeyondArcText = "OUT OF ARC - the gun cannot turn there, fire held";

        /// <summary>⭐ <c>CE-1359</c> — the crosshair's point is beyond the selected gun's limited traverse (<c>BallisticAim.BeyondTraverse</c>,
        /// the rule its weapon brain holds the fire by). False for a full-circle turret, a mount with no turret, or out of elevation only.</summary>
        public static bool BeyondTraverse(EntityRepository w, Entity unit, int mount, Vector3 aimAt)
        {
            (unit, mount) = Hud.Resolve(w, unit, mount);
            if (!w.HasComponent<SimTransform>(unit)) return false;
            var dto = Fdp.Toolkit.Combat.CombatTkb.MountOf(w, unit, mount);
            return dto is { TurretIndex: >= 0 } && Fdp.Toolkit.Combat.CombatTkb.TurretOf(w, unit, dto.TurretIndex) is { } turret
                && Fdp.Toolkit.Combat.BallisticAim.BeyondTraverse(w, unit, turret, dto, aimAt);
        }

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

        /// <summary>
        /// ⭐ <c>CE-1293</c> (docs/DESIGN_Unit_Camera.md UC-9) — where the selected weapon's round would STOP on its way to the crosshair's
        /// point, when that is short of it: from where the round leaves (<c>ShotOrigin</c> — the barrel, or his eye), stopped by the one
        /// penetration rule (<c>TerrainPenetration.Carry</c>). Null when it reaches (within <see cref="BlockedMargin"/>) or no terrain.
        /// </summary>
        public static Vector3? BlockedShot(EntityRepository w, Entity unit, int mount, Vector3 aimAt)
        {
            var (holder, local) = Hud.Resolve(w, unit, mount);
            if (Fdp.Toolkit.World.WorldQuery.Of(w) is not { } terrain || !w.IsAlive(holder) || !w.HasComponent<SimTransform>(holder)) return null;
            var eye = Fdp.Toolkit.Combat.ShotOrigin.Eye(w, holder, out _);
            var origin = Fdp.Toolkit.Combat.ShotOrigin.From(w, holder, local, eye, aimAt, out _);
            float length = Vector3.Distance(origin, aimAt);
            if (length < BlockedMargin * 2f) return null;
            var dto = Fdp.Toolkit.Combat.CombatTkb.MountOf(w, holder, local);
            long ammo = dto != null ? unchecked((long)dto.AmmoGuid) : 0L;
            var (penetration, _) = Fdp.Toolkit.Combat.CombatTkb.RoundOf(w, dto, ammo);
            float damage = 1f;
            if (!Fdp.Toolkit.Combat.TerrainPenetration.Carry(terrain, origin, aimAt, ref damage, ref penetration, out float stopT)) return null;
            return stopT * length < length - BlockedMargin ? Vector3.Lerp(origin, aimAt, stopT) : null;
        }

        /// <summary>⭐ <c>CE-1293</c> (UC-9) — a stop this close to the crosshair's point is the point itself (m).</summary>
        public const float BlockedMargin = 0.5f;

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
                if (line == LookFreeLine) return new Color(255, 220, 120, 255);   // ⭐ CE-1204
                if (line.StartsWith("SPD", StringComparison.Ordinal)) return new Color(150, 200, 255, 255);
                return Color.RayWhite;
            }

            public static string[] Lines(EntityRepository w, Entity unit, int mount, bool vehicle, bool hullLocked,
                                         Fdp.Toolkit.Tkb.Domain.StanceId? stance = null, bool shotPending = false,
                                         ControlScheme scheme = ControlScheme.Person, bool running = false, bool lookFree = false,
                                         string? seat = null)
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
                    lines.Add($"STANCE {st.ToString().ToUpperInvariant()}{(running ? "  RUN" : "")}"   // ⭐ CE-1074 / CE-1143
                              // ⭐ CE-1297 — an order the unit would ignore says so (it used to show the stance and do nothing)
                              + (st != Fdp.Toolkit.Tkb.Domain.StanceId.Standing && Hrot.MuscleCharacter.Animation.Components.LogicalStance.Refuses(w, unit)
                                  ? $"  - {st.ToString().ToUpperInvariant()}: not for this unit" : ""));
                if (lookFree && !vehicle && !flyer) lines.Add(LookFreeLine);   // ⭐ CE-1204 (§8l LK-4)
                if (seat != null) lines.Add(seat);                             // ⭐ CE-1210 (§8n MS-3)
                lines.Add(scheme switch
                {
                    ControlScheme.FixedWing => $"W/S throttle  A/D turn  Q/Z climb/dive  mouse look  V {(hullLocked ? "free look" : "hull view")}  Esc release",   // ⭐ CE-1351
                    ControlScheme.Rotorcraft => $"W/S fly  A/D turn  Q/Z up/down  mouse look  V {(hullLocked ? "free look" : "hull view")}  Esc release",
                    _ => vehicle
                        ? $"W/S throttle  A/D steer  V {(hullLocked ? "free look" : "hull view")}  Esc release"
                        : lookFree
                            ? "W/S move  A/D turn  Shift run  Ctrl crouch (hold)  C crouch  Z prone  V lock look  Esc release"   // ⭐ CE-1204
                            : "W/A/S/D move  Shift run  Ctrl crouch (hold)  C crouch  Z prone  V free look  Esc release",
                });
                lines.Add("LMB fire  RMB zoom  X cancel  1-4 weapon  R ammo  F use (hold: more)  Q/E lean  T orders  Tab next man  M map  B binoculars");   // ⭐ CE-1078 / CE-1079 / CE-1158
                return lines.ToArray();
            }

            /// <summary>⭐ <c>CE-1210</c> (§8n MS-3) — the seat line of a vehicle with manned guns: <c>SEAT driver  (G: next seat)</c> or
            /// <c>SEAT M134  (G: next seat)</c>; null when there is no other seat to go to.</summary>
            /// <summary>⭐ <c>CE-1201</c> (§8p OS-1) — the HUD says the view is over his shoulder, and how back.</summary>
            public const string ShoulderLine = "VIEW  over the shoulder  (O: his eyes · wheel: distance · Q/E: shoulder · RMB: aim)";

            public static string? SeatLine(EntityRepository w, Entity unit)
            {
                if (SeatSwitch.Seats(w, SeatSwitch.VehicleOf(w, unit)).Count < 2) return null;
                string name = SeatSwitch.StationOf(w, unit, out _, out _, out var weapon) ? TypeName(w, weapon)?.ToUpperInvariant() ?? "GUN" : "DRIVER";
                return $"SEAT {name}  (G: next seat)";
            }

            /// <summary>⭐ <c>CE-1204</c> (§8l LK-4) — the HUD's line while a person's look is free of his body.</summary>
            public const string LookFreeLine = "LOOK FREE (V)";

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
                => a.Verb is UseVerbKind.Drive or UseVerbKind.Pilot or UseVerbKind.Board or UseVerbKind.Man
                         or UseVerbKind.Take or UseVerbKind.Drop or UseVerbKind.PickUpRound && TypeName(w, a.Target) is { } name   // ⭐ CE-1207
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
                // ⭐ CE-1358 (DESIGN_Weapon_Layers §5k) — the barrel: held while too hot, its heat once it is worth watching
                if (a.IsHot(m)) line.Append("  OVERHEATED ").Append(a.HeatPercentOf(m)).Append('%');
                else if (a.HeatPercentOf(m) >= HeatShownFromPercent) line.Append("  HEAT ").Append(a.HeatPercentOf(m)).Append('%');
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

            /// <summary>⭐ CE-1358 — the weapon line shows the barrel's heat from this percent (below it the barrel is not worth a word).</summary>
            public const int HeatShownFromPercent = 25;

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
    /// mouse (look), W/A/S/D (a person: forward + strafe; a vehicle: throttle + steer), Shift (run), 1-4 (weapon), the wheel (camera distance, R-1034), LMB (fire),
    /// V (a vehicle's hull-locked view, where the mouse steers) and Esc (release), and sends ONE control frame per frame. It consumes
    /// the input, so no click or menu reaches the map's layers while the operator is in the unit.
    /// </summary>
    public sealed class FpsCameraDriver : ICameraDriver
    {
        /// <summary>Radians of look per pixel of mouse movement.</summary>
        public const float LookSensitivity = 0.0025f;
        /// <summary>⭐ <c>CE-1351</c> — how far ahead of a flyer's nose A/D put the heading it turns to (rad): held, it turns at its own rate
        /// (a helicopter) or banks into the turn (a plane); released, it holds its heading.</summary>
        public const float FlyerTurnLead = 0.8f;
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
        private float _yaw, _pitch, _roll;
        private float _hullLook, _hullYaw;   // ⭐ CE-1351 — the look off the hull while the view is hull-locked (V); the hull's heading
        private float? _heldHeading;         // ⭐ CE-1354 — the heading a flyer holds since A/D was let go (taken once, never re-read)
        private float _bodyYaw, _dt;   // ⭐ CE-1204 (§8l LK-2, LK-4) — a person's heading while his look is free; the last frame's dt
        private bool _lookInitialised;
        private uint _seq;
        private int _seatedGun = -1;   // ⭐ CE-1307 — the controlled weapon his seat gave him, as last seen
        private Fdp.Toolkit.Tkb.Domain.StanceId? _stance;   // ⭐ CE-1074 — the stance the operator asked for (null: not yet)
        private bool _crouchHeld;                          // ⭐ CE-1143 — Ctrl held: crouched while held
        private Vector3 _eye;                              // ⭐ CE-1073 — where the eye was placed this frame
        private bool _eyeKnown;
        private Vector3 _centre;                           // ⭐ CE-1293 — the unit's middle (the far end of the camera continuum)
        private float _arm;                                // ⭐ CE-1293 (UC-4) — the spring arm's length this frame
        private readonly List<Fdp.Toolkit.World.TraceCrossing> _armTrace = new();
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

        /// <summary>⭐ <c>CE-1204</c> (§8l LK-4, R-1017) — how fast A/D turn a person's body while his look is free (rad/s, 120°/s).</summary>
        public static readonly float FreeLookTurnRate = 120f * MathF.PI / 180f;

        /// <summary>⭐ <c>CE-1204</c> (§8l LK-4) — is a PERSON's look free of his body (V)? Then the mouse turns his head within his neck
        /// (<see cref="Fdp.Toolkit.Perception.Look.LookOf.HeadLimit"/>), dragging the body past it, and A/D turn the body. Locked (the
        /// default): the mouse turns the body. The choice is the station's, so it stays across possessions and Tab.</summary>
        public bool LookFree => Scheme == ControlScheme.Person && _station.PersonLookFree;

        /// <summary>⭐ <c>CE-1204</c> — the heading the frame asks for: the body's own while the look is free, else the look's.</summary>
        public float BodyYaw => LookFree || ShoulderBody ? _bodyYaw : _yaw;

        /// <summary>⭐ <c>CE-1293</c> (UC-5) — a person on foot in the shoulder view (locked look): his body lags the aim.</summary>
        public bool ShoulderBody => Scheme == ControlScheme.Person && _station.OverShoulder && !_station.PersonLookFree && !Aboard;

        /// <summary>⭐ <c>CE-1293</c> (UC-5) — standing, the body follows the aim only past this (rad, 45°).</summary>
        public static readonly float ShoulderLag = 45f * MathF.PI / 180f;

        /// <summary>⭐ <c>CE-1293</c> (UC-5) — moving, the body turns to the aim this fast (rad/s, 540°/s).</summary>
        public static readonly float BodyTurnRate = 540f * MathF.PI / 180f;

        /// <summary>⭐ <c>CE-1293</c> — <paramref name="from"/> turned toward <paramref name="to"/> by at most <paramref name="step"/> (radians).</summary>
        public static float TurnToward(float from, float to, float step)
        {
            float off = Fdp.Toolkit.Perception.Look.LookOf.Wrap(to - from);
            return MathF.Abs(off) <= step ? to : from + MathF.CopySign(step, off);
        }

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

        /// <summary>The look direction (radians): what the camera shows and, for a person with a locked look, the body's yaw (M-2;
        /// free look — <see cref="BodyYaw"/>, CE-1204).</summary>
        public float Yaw => _yaw;
        public float Pitch => _pitch;

        /// <summary>⭐ CE-1074 — the stance the operator has asked for (a person; null for a vehicle or before the first frame).</summary>
        public Fdp.Toolkit.Tkb.Domain.StanceId? Stance
            => _crouchHeld && _stance is { } st && st != Fdp.Toolkit.Tkb.Domain.StanceId.Prone ? Fdp.Toolkit.Tkb.Domain.StanceId.Crouched : _stance;

        /// <summary>⭐ CE-1143 — Shift is held: the person runs (the frame's gait 0).</summary>
        public bool Running { get; private set; }

        /// <summary>⭐ <c>CE-1210</c> — is he in a seat (a crew seat of a vehicle)? Then W/A/S/D move nothing (MS-1) and his eye is the seat's.</summary>
        public bool Aboard => Scheme == ControlScheme.Person && _world() is { } aw && aw.IsComponentTypeRegistered<IsEmbarkedTag>()
                              && aw.HasComponent<IsEmbarkedTag>(Unit);

        private static Fdp.Interfaces.ITkbDatabase? Tkb(EntityRepository w)
            => w.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() ? w.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() : null;

        /// <summary>⭐ <c>CE-1210</c> (§8n MS-5, LK-6) — the look held in the arc of the gun his seat operates (off the hull's heading).</summary>
        private void ClampToGun()
        {
            if (_world() is not { } w || !SeatSwitch.StationOf(w, Unit, out var vehicle, out var gun, out _) || !w.HasComponent<SimTransform>(vehicle)) return;
            (_yaw, _pitch) = SeatSwitch.Clamp(gun, HullYaw(w.GetComponentRO<SimTransform>(vehicle).Rotation), _yaw, _pitch);
        }

        /// <summary>⭐ CE-1073 — where the eye is this frame (world metres).</summary>
        public Vector3 Eye => _eye;

        public void Update(MapCamera3D camera, float dt)
        {
            if (_world() is not { } w || !w.IsAlive(Unit) || !w.HasComponent<SimTransform>(Unit)) return;
            var tf = w.GetComponentRO<SimTransform>(Unit);
            float hullYaw = HullYaw(tf.Rotation);
            if (!_lookInitialised)
            {
                _yaw = hullYaw; _bodyYaw = hullYaw; _pitch = 0f; _lookInitialised = true;
                // ⭐ CE-1210 (§8n MS-5) — a gunner starts looking down the middle of his gun's arc
                if (SeatSwitch.StationOf(w, Unit, out _, out var gun, out _) && gun.HasArc != 0)
                    _yaw = hullYaw + (gun.TraverseMin + gun.TraverseMax) / 2f;
            }
            _dt = dt;
            bool vehicle = IsVehicle;
            _hullYaw = hullYaw;
            if (HullLocked && (vehicle || IsFlyer)) _yaw = hullYaw + _hullLook;   // ⭐ CE-1351 — V: the view turns with the hull, the mouse looks off it
            _eye = SeatSwitch.TryEye(w, Unit, Tkb(w), out var seatEye) ? seatEye : EyeOf(w, tf, vehicle);   // ⭐ CE-1210 (MS-5) — at his seat
            _eyeKnown = true;
            _centre = new Vector3(_eye.X, _eye.Y, (tf.Position.Z + _eye.Z) * 0.5f);   // ⭐ CE-1293 — the far end of the continuum
            StepKick(w, dt);   // ⭐ CE-1216 (§8k S-3)
            // ⭐ CE-1095 (§8f P-3) — a flyer's view banks with the body (the horizon tilts into the turn); its pitch stays the look's
            _roll = IsFlyer ? Fdp.Toolkit.Motion.RotorcraftModel.TiltOf(tf.Rotation).Roll : 0f;
            // ⭐ CE-1293 (UC-7) — a driver's look left alone eases back behind the vehicle (not an armed turret's: Recentres)
            _lookIdle += dt;
            if (_lookIdle > RecentreAfter && Recentres)
            {
                _yaw = TurnToward(_yaw, hullYaw, RecentreRate * dt);
                _pitch = TurnToward(_pitch, 0f, RecentreRate * dt);
            }
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
            // ⭐ CE-1200 (§8o CR-2) — a vehicle's or a flyer's CREW eye, as its kit was drawn (inside the cab / canopy, or above the turret)
            if (Scheme != ControlScheme.Person && _station.Bodies is { } drawn && drawn.TryGetEyeLocal(Unit, out var crew))
                return tf.Position + Vector3.Transform(crew, tf.Rotation);
            if (Scheme == ControlScheme.Rotorcraft) return tf.Position + new Vector3(0f, 0f, RotorcraftEyeHeight);   // ⭐ CE-2168 (MD-D)
            if (Scheme == ControlScheme.FixedWing) return tf.Position + new Vector3(0f, 0f, FixedWingEyeHeight);     // ⭐ CE-1095 (§8f P-3)
            if (vehicle) return tf.Position + new Vector3(0f, 0f, VehicleEyeHeight);
            if (_station.Bodies is { } bodies && bodies.TryGetEyeLocal(Unit, out var local))
                return tf.Position + Vector3.Transform(local, tf.Rotation);
            return tf.Position + new Vector3(0f, 0f, Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeHeightFor(w, Unit,
                Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(w, Unit)));
        }

        /// <summary>
        /// ⭐ <c>CE-1293</c> (docs/DESIGN_Unit_Camera.md UC-7, UC-8) — the kind of camera the continuum is for: his own, or — a man in a
        /// seat (a gunner at his gun, a passenger) — the vehicle's he sits in, so the camera is the vehicle's chase camera hung from his
        /// seat (the gunner's seat is at the gun: the camera stands behind the gun, looking along it).
        /// </summary>
        public ControlScheme RigScheme
        {
            get
            {
                if (!Aboard || _world() is not { } w) return Scheme;
                var vehicle = SeatSwitch.VehicleOf(w, Unit);
                return vehicle.IsNull ? ControlScheme.Vehicle : Fdp.Toolkit.Motion.Locomotions.Of(w, vehicle) switch
                {
                    Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft or Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing => ControlScheme.Rotorcraft,
                    _ => ControlScheme.Vehicle,
                };
            }
        }

        private bool _armPlaced;

        /// <summary>⭐ <c>CE-1293</c> (UC-7) — a driver's look left alone this long (s) eases back to the hull's heading.</summary>
        public const float RecentreAfter = 1.5f;

        /// <summary>⭐ <c>CE-1293</c> (UC-7) — how fast it eases back (rad/s, 90°/s).</summary>
        public static readonly float RecentreRate = 90f * MathF.PI / 180f;

        private float _lookIdle;

        /// <summary>
        /// ⭐ <c>CE-1293</c> (UC-7) — the driver's look re-centres: a ground vehicle in the chase camera whose selected weapon lays no
        /// turret. ⚠ Not an armed turret: there the look IS the turret's order (§8p OS-4), and easing it back would swing the gun.
        /// </summary>
        public bool Recentres => IsVehicle && !HullLocked && ShoulderViewOn && !LaysATurret;

        private bool LaysATurret => _world() is { } w && PossessionStation.Hud.Resolve(w, Unit, Mount) is var (holder, local)
                                    && Fdp.Toolkit.Combat.CombatTkb.MountOf(w, holder, local) is { TurretIndex: >= 0 };

        /// <summary>The camera at the eye, looking along the operator's look — ⭐ CE-1201 (§8p) or over his shoulder.</summary>
        /// <summary>
        /// ⭐ <c>CE-1352</c> — just before the frame is drawn, the camera is put back on the unit as the sim has moved it THIS frame (its eye,
        /// its hull's heading when the view is hull-locked, a flyer's bank), with no easing (dt 0: the arm, the recoil and the re-centre move
        /// once a frame, in <see cref="Update"/>). 🔒 User, 2026-10-25: "camera is jumping/stuttering behing a possessed vehicle … making sure
        /// camera is synced with the vehicle".
        /// </summary>
        public void BeforeDraw(MapCamera3D camera)
        {
            if (!_eyeKnown || _world() is not { } w || !w.IsAlive(Unit) || !w.HasComponent<SimTransform>(Unit)) return;
            var tf = w.GetComponentRO<SimTransform>(Unit);
            bool vehicle = IsVehicle;
            _hullYaw = HullYaw(tf.Rotation);
            if (HullLocked && (vehicle || IsFlyer)) _yaw = _hullYaw + _hullLook;
            _eye = SeatSwitch.TryEye(w, Unit, Tkb(w), out var seatEye) ? seatEye : EyeOf(w, tf, vehicle);
            _centre = new Vector3(_eye.X, _eye.Y, (tf.Position.Z + _eye.Z) * 0.5f);
            _roll = IsFlyer ? Fdp.Toolkit.Motion.RotorcraftModel.TiltOf(tf.Rotation).Roll : 0f;
            float dt = _dt;
            _dt = 0f;
            Place(camera);
            _dt = dt;
        }

        private void Place(MapCamera3D camera)
        {
            if (!_eyeKnown) return;
            camera.Yaw = _yaw;
            camera.Pitch = _pitch + Kick;   // ⭐ CE-1216 (§8k S-3) — the recoil kick, on the view only (his look is _pitch)
            camera.Roll = _roll;
            camera.Distance = camera.MinDistance;
            // ⭐ CE-1293 (docs/DESIGN_Unit_Camera.md §4, UC-1/UC-4/UC-11) — one continuum: the shoulder view stands ShoulderDistance back
            //   (the wheel, R-1034), RMB aims from the eyes (UC-11), and the spring arm pulls the camera in front of a wall
            var scheme = RigScheme;   // ⭐ UC-7 / UC-8 — a man in a seat: the vehicle's chase camera from his seat
            float distance = ShoulderViewOn ? _station.ShoulderDistance(scheme) : 0f;
            var centre = scheme == Scheme ? _centre : _eye;
            var pivot = UnitCameraRig.Pivot(centre, _eye, camera.Forward, distance, scheme, _station.ShoulderSide);
            var wanted = UnitCameraRig.Camera(pivot, camera.Forward, distance, orbitPitch: false);
            var free = UnitCameraRig.SpringArm(pivot, wanted, FirstHit);
            // ⭐ CE-1293 — the first frame stands at full length (not sliding out of his eyes); after a wall it eases out at the kind's pace
            float reach = Vector3.Distance(pivot, free);
            _arm = distance <= 0f ? 0f : _armPlaced ? UnitCameraRig.EaseArm(_arm, reach, _dt, UnitCameraRig.ScaleOf(scheme)) : reach;
            _armPlaced = true;
            var at = distance > 0f && Vector3.DistanceSquared(wanted, pivot) > 1e-6f
                ? pivot + Vector3.Normalize(wanted - pivot) * _arm : _eye;
            if (distance > 0f && camera.GroundHeight is { } ground) at.Z = MathF.Max(at.Z, ground(at.X, at.Y) + CameraAboveGround);
            camera.LookAt = at + camera.Forward * camera.Distance;
            camera.FovyDegrees = ZoomFovDegrees;   // ⭐ CE-1211 (§8j Z-1) — narrower while RMB is held
            if (_station.Bodies is { } bodies) bodies.ShowFirstPersonHead = distance > 0.5f;   // ⭐ CE-1201 (OS-5) — he is seen whole
        }

        /// <summary>⭐ <c>CE-1201</c> (§8p OS-1) — O toggles the over-the-shoulder view while in first person.</summary>
        public const KeyboardKey ShoulderKey = KeyboardKey.O;

        /// <summary>⭐ <c>CE-1293</c> (UC-11) — the camera stands back this frame: the shoulder view is on and RMB is not aiming from the eyes.</summary>
        public bool ShoulderViewOn => _station.OverShoulder && !Zooming;

        /// <summary>⭐ <c>CE-1293</c> (UC-4) — the first wall, roof or ground between the pivot and the camera, as a fraction of the line.</summary>
        private float? FirstHit(Vector3 from, Vector3 to)
        {
            if (_world() is not { } w || Fdp.Toolkit.World.WorldQuery.Of(w) is not { } query) return null;
            query.Trace(from, to, Fdp.Toolkit.World.TracePurpose.Sight, _armTrace);
            foreach (var c in _armTrace)
                if (c.ClosedBarrier || c.Loss < ArmOpaque) return c.T;
            return null;
        }

        /// <summary>⭐ <c>CE-1293</c> — a crossing letting less light through than this stops the camera (glass does not).</summary>
        public const float ArmOpaque = 0.5f;

        /// <summary>⭐ <c>CE-1293</c> (R-1034) — the wheel moves the camera along the continuum: out of the eyes switches the shoulder view
        /// on, all the way in switches it off; a man in a seat sets the vehicle's distance (UC-8).</summary>
        private void WheelDistance(float wheel)
        {
            var rig = RigScheme;
            float now = _station.OverShoulder ? _station.ShoulderDistance(rig) : 0f;
            float farther = Math.Clamp(now - wheel * DistanceStep * UnitCameraRig.ScaleOf(rig), 0f, UnitCameraRig.MaxPlayDistance(rig));
            if (farther < DistanceStep * 0.5f) _station.OverShoulder = false;
            else { _station.SetShoulderDistance(rig, farther); _station.OverShoulder = true; }
        }

        /// <summary>⭐ <c>CE-1293</c> (R-1034) — the wheel moves the camera this far per notch (a person; a vehicle × its scale).</summary>
        public const float DistanceStep = 0.5f;

        /// <summary>⭐ <c>CE-1201</c> (§8p OS-2) — a person's over-the-shoulder camera: behind his eye, to his right, a little up (m).</summary>
        public const float ShoulderBack = 2.2f, ShoulderSide = 0.55f, ShoulderLift = 0.2f;

        /// <summary>⭐ <c>CE-1201</c> (§8p OS-4) — the chase camera behind a vehicle's crew eye and above it (m); a flyer's further.</summary>
        public const float ChaseBack = 9f, ChaseLift = 2.5f, FlyerChaseBack = 18f, FlyerChaseLift = 4.5f;

        /// <summary>The camera never goes nearer the ground than this (m).</summary>
        public const float CameraAboveGround = 0.4f;

        /// <summary>
        /// ⭐ <c>CE-1201</c> (docs/DESIGN_Entity_Control.md §8p OS-2, OS-4) — where the camera stands for the over-the-shoulder view: a person
        /// <see cref="ShoulderBack"/> behind his eye along the LEVEL look, <see cref="ShoulderSide"/> to his right, <see cref="ShoulderLift"/>
        /// up; a vehicle or a flyer a chase camera straight behind and above; never under the ground. The crosshair ray starts here (OS-3:
        /// the round still flies from the muzzle to where it hits). Pure — the rail runs it.
        /// </summary>
        public static Vector3 ShoulderCamera(Vector3 eye, Vector3 forward, ControlScheme scheme, Func<float, float, float>? ground)
        {
            // ⭐ CE-1293 — the continuum at the kind's default distance (UnitCameraRig): the same point §8p placed by hand
            float distance = UnitCameraRig.DefaultDistance(scheme == ControlScheme.FixedWing ? ControlScheme.Rotorcraft : scheme);
            var pivot = UnitCameraRig.Pivot(eye, eye, forward, distance, scheme, side: 1);
            var at = UnitCameraRig.Camera(pivot, forward, distance, orbitPitch: false);
            if (ground != null) at.Z = MathF.Max(at.Z, ground(at.X, at.Y) + CameraAboveGround);
            return at;
        }

        /// <summary>⭐ <c>CE-1212</c> (§8j L-1, L-2; keys approved, R-1009) — hold to lean left / right; the use key; the next round.</summary>
        public const KeyboardKey LeanLeftKey = KeyboardKey.Q, LeanRightKey = KeyboardKey.E, UseKeyCode = KeyboardKey.F, NextRoundKey = KeyboardKey.R;

        /// <summary>⭐ <c>CE-1216</c> (§8k S-3) — how long a recoil kick takes to settle (s).</summary>
        public const float KickSeconds = 0.15f;
        private double _lastShotAt = double.NaN;

        /// <summary>⭐ <c>CE-1216</c> (§8k S-3) — the view's recoil kick now (radians up), settling over <see cref="KickSeconds"/>.</summary>
        public float Kick { get; private set; }

        /// <summary>⭐ <c>CE-1216</c> (§8k S-3) — one shot's kick (radians) by the weapon's class: a pistol or rifle a little, a machine gun
        /// less per round, a cannon or a launcher hard; nothing for a throw or a weapon with no class.</summary>
        public static float KickRadians(Fdp.Toolkit.Tkb.Domain.WeaponCategory category) => category switch
        {
            Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms => 0.012f,
            Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun => 0.007f,
            Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon => 0.015f,
            Fdp.Toolkit.Tkb.Domain.WeaponCategory.TankGun or Fdp.Toolkit.Tkb.Domain.WeaponCategory.Howitzer => 0.04f,
            Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm or Fdp.Toolkit.Tkb.Domain.WeaponCategory.Rocket
                or Fdp.Toolkit.Tkb.Domain.WeaponCategory.Sam or Fdp.Toolkit.Tkb.Domain.WeaponCategory.Mortar => 0.025f,
            _ => 0f,
        };

        /// <summary>⭐ <c>CE-1216</c> (§8k S-3) — his own new shot (the effects spawner's <c>RecentShots</c>) kicks the view; the kick settles.</summary>
        private void StepKick(EntityRepository w, float dt)
        {
            Kick = Kick * MathF.Exp(-3f * dt / KickSeconds) is var k && k > 1e-4f ? k : 0f;   // ~95 % settled in KickSeconds
            if (Hrot.UI.Common.Effects.RecentShots.Of(w) is not { } shots || !shots.TryGet(Unit, out int mount, out double at)) return;
            if (at == _lastShotAt) return;
            bool first = double.IsNaN(_lastShotAt);
            _lastShotAt = at;
            if (first) return;   // a shot from before he was held
            var (platform, index) = FpsZoom.PlatformOf(w, Unit, mount);
            if (Fdp.Toolkit.Combat.CombatTkb.MountOf(w, platform, index) is { } dto) Kick += KickRadians(dto.Category);
        }

        /// <summary>⭐ <c>CE-1213</c> (§8j W-1; key approved, R-1009) — hold to open the command wheel.</summary>
        public const KeyboardKey CommandWheelKey = KeyboardKey.T;
        /// <summary>⭐ <c>CE-1214</c> (§8j T-1; key approved, R-1009) — the next man of his squad (Shift: the previous).</summary>
        public const KeyboardKey TeamSwitchKey = KeyboardKey.Tab;
        /// <summary>⭐ <c>CE-1218</c> (§8k M-1; key approved, R-1010) — the 2-D map and back.</summary>
        public const KeyboardKey MapKey = KeyboardKey.M;
        /// <summary>⭐ <c>CE-1210</c> (§8n MS-3, R-1018) — the next seat of the vehicle: driver / pilot, then each manned gun.</summary>
        public const KeyboardKey SeatKey = KeyboardKey.G;

        /// <summary>⭐ <c>CE-1264</c> (AV-5, R-1029) — I: SELECT the unit under the crosshair ("identify"), the one way to point at a unit in
        /// first person (LMB fires here). The selection is the pointer an AI agent reads (<c>GET /selection</c>) and it is recorded.</summary>
        public const KeyboardKey SelectKey = KeyboardKey.I;

        /// <summary>⭐ <c>CE-1218</c> — the input this driver reads (its cursor lock), for the station to watch M while the 2-D map is up.</summary>
        internal IInputProvider? Input => _locked;

        /// <summary>⭐ <c>CE-1218</c> (§8k M-1) — one frame with every axis at rest, the trigger up and no lean (the counters unchanged, so no
        /// press is repeated): the brain keeps the newest frame, so he stands still while the map is up.</summary>
        private void SendStill()
        {
            Leaning = Fdp.Toolkit.Combat.LeanSide.LeanNone;
            BinocularsHeld = false;   // ⭐ CE-1219 — the binoculars go down with him (Binoculars = 0 below)
            _station.Send(_netId, new OperatorControlFrameEvent
            {
                Seq = ++_seq, Yaw = BodyYaw, Gait = 1, Mount = (byte)Mount, AimPoint = AimAt,
                Stance = !IsVehicle && Stance is { } stance ? (byte)((byte)stance + 1) : (byte)0,
                ShotSeq = _shotSeq, CancelSeq = _cancelSeq, AmmoSeq = _ammoSeq, ShotTarget = _shotTarget,
                UseSeq = _useSeq, UseVerb = _useVerb, UseTarget = _useTarget,
            });
        }
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
        public float ZoomFovDegrees => BinocularsUp ? Fdp.Toolkit.Combat.HeldItems.BinocularsFovDegrees   // ⭐ CE-1219 — the sim's own 10°
            : Zooming && _world() is { } zw ? FpsZoom.FovFor(zw, Unit, Mount) : FpsZoom.NormalFovDegrees;

        /// <summary>⭐ <c>CE-1219</c> (§8k B-1; key approved, R-1010) — hold to raise his binoculars.</summary>
        public const KeyboardKey BinocularsKey = KeyboardKey.B;

        /// <summary>⭐ <c>CE-1219</c> — is B held (the frame asks his brain to raise them)?</summary>
        public bool BinocularsHeld { get; private set; }

        /// <summary>⭐ <c>CE-1219</c> — are his binoculars UP in the sim (<see cref="Fdp.Toolkit.Combat.HeldItems.BinocularsUp"/>, the
        /// replicated posture)? The view narrows only then — never on the key alone, so it never shows what the sim does not see.</summary>
        public bool BinocularsUp => _world() is { } bw && Fdp.Toolkit.Combat.HeldItems.BinocularsUp(bw, Unit);

        /// <summary>⭐ <c>CE-1219</c> — B held by a man who carries none (his type's pockets): the HUD says so.</summary>
        public bool NoBinoculars => BinocularsHeld && _world() is { } nw
            && !Fdp.Toolkit.Combat.HeldItems.Has(nw, Unit, Fdp.Toolkit.Combat.Components.HeldItemKind.Binoculars);

        /// <summary>
        /// ⭐ <c>CE-1219</c> (§8k B-1) — with his binoculars up, LMB DESIGNATES for his men (<see cref="CommandWheel.MenOf"/>) in the station's
        /// <see cref="Hrot.Common.Orders.OrderDesignations"/>: the unit under the crosshair as their Target, else the point as their MapPoint —
        /// so the wheel's Attack / Move (and any order draft) then complete without a pick. Returns how many men got it.
        /// </summary>
        public int Designate(Vector3 aimAt, long aimTarget)
        {
            if (_world() is not { } w) return 0;
            var store = Hrot.Common.Orders.OrderDesignations.For(w);
            var map = w.HasSingletonManaged<NetworkEntityMap>() ? w.GetSingletonManaged<NetworkEntityMap>()! : null;
            var kind = aimTarget != 0 ? Hrot.Common.Orders.DesignationKind.Target : Hrot.Common.Orders.DesignationKind.MapPoint;
            System.Text.Json.Nodes.JsonNode value = aimTarget != 0 ? System.Text.Json.Nodes.JsonValue.Create(aimTarget)
                : Hrot.ScenarioEditor.Map.RtsCommands.OrderPoint(w, new Vector2(aimAt.X, aimAt.Y));
            int count = 0;
            foreach (var man in CommandWheel.MenOf(w, Unit))
                if (map != null && map.TryGetNetworkId(man, out long id)) { store.Set(id, kind, value); count++; }
            _wheelVerdict = count == 0 ? "no men to designate for"
                : $"{(aimTarget != 0 ? "Target" : "Point")} designated for {count} {(count == 1 ? "man" : "men")}";
            _wheelVerdictAt = _station.Clock;
            return count;
        }

        public bool HandleInput(MapCamera3D camera, IInputProvider input)
        {
            if (_locked == null) { input.SetCursorLocked(true); _locked = input; }
            if (input.IsKeyPressed(MapKeyboardKey.Escape))
            {
                // ⭐ CE-1158 (§8h U-4) — Esc in a vehicle with a rider is Get out (back into him), not a release that strands him aboard
                if (!_station.TryGetOut(Unit)) _station.Release(_netId);
                return true;
            }

            // ⭐ CE-1218 (§8k M-1) — M: the 2-D map centred on him; he holds still (one still frame) until M brings the operator back
            if (input.IsKeyPressed((MapKeyboardKey)MapKey) && _station.PeekEndedAt != _station.Clock)
            {
                SendStill();
                _station.BeginMapPeek(this);
                return true;
            }
            // ⭐ CE-1214 (§8j T-1) — Tab / Shift+Tab: the next / previous man of his squad (SquadRoster); the one he leaves goes back
            //   to his brain (the possess releases him)
            if (input.IsKeyPressed((MapKeyboardKey)TeamSwitchKey) && _world() is { } tw
                && SquadRoster.Next(tw, Unit, input.IsKeyDown(MapKeyboardKey.LeftShift) || input.IsKeyDown(MapKeyboardKey.RightShift) ? -1 : 1) is { IsNull: false } next)
            {
                _station.SwitchTo(next);
                return true;
            }
            // ⭐ CE-1210 (§8n MS-3) — G: the next seat (the vehicle's own seat, then each manned gun); one possession, as Tab
            if (input.IsKeyPressed((MapKeyboardKey)SeatKey) && _world() is { } gw && SeatSwitch.Next(gw, Unit) is { IsNull: false } seat)
            {
                _station.SwitchTo(seat);
                return true;
            }
            bool vehicle = IsVehicle;
            var delta = input.MouseDelta;
            // ⭐ CE-1213 (§8j W-1) — hold T: the command wheel; while it is open the mouse moves its cursor, not the look
            if (CommandWheelInput(input, delta)) delta = Vector2.Zero;
            // ⭐ CE-1211 (§8j Z-1) — hold RMB to zoom; the look turns slower in proportion to the narrower view
            Zooming = input.IsMouseButtonDown(MapMouseButton.Right);
            float look = FpsZoom.SensitivityFor(ZoomFovDegrees);
            if (delta != Vector2.Zero) _lookIdle = 0f;   // ⭐ CE-1293 (UC-7) — the mouse moved: no re-centre yet
            // ⭐ CE-1351 — V locks the view to the hull, looking forward (a vehicle or a flyer); the keys still steer and the mouse still looks.
            //   🔒 User, 2026-10-25: "V key switches to mouse steering - i would expect normal A-D key steering and the mouse is still the
            //   look-around camera and aim point" · "for aircraft - mouse steering feels unintuitive, classic WASD migth feel better, V locks view
            //   to forward hull view". ⛔ SUPERSEDED: hull-locked, the mouse X steered (MouseSteerPerPixel).
            if ((vehicle || IsFlyer) && input.IsKeyPressed((MapKeyboardKey)KeyboardKey.V)) { HullLocked = !HullLocked; _hullLook = 0f; }
            // ⭐ CE-1204 (§8l LK-4) — V frees a person's look from his body, or locks it back (his body turns to the look)
            if (Scheme == ControlScheme.Person && input.IsKeyPressed((MapKeyboardKey)KeyboardKey.V))
            {
                if (!_station.PersonLookFree) _bodyYaw = _yaw;
                _station.PersonLookFree = !_station.PersonLookFree;
            }
            bool lookFree = LookFree;
            _yaw -= delta.X * look;
            if (HullLocked && (vehicle || IsFlyer)) _hullLook = Fdp.Toolkit.Perception.Look.LookOf.Wrap(_hullLook - delta.X * look);
            _pitch = Math.Clamp(_pitch - delta.Y * look, -1.4f, 1.4f);
            ClampToGun();   // ⭐ CE-1210 (§8n MS-5) — a gunner's look stays in his gun's arc

            float forward = (input.IsKeyDown((MapKeyboardKey)KeyboardKey.W) ? 1f : 0f) - (input.IsKeyDown((MapKeyboardKey)KeyboardKey.S) ? 1f : 0f);
            float side = (input.IsKeyDown((MapKeyboardKey)KeyboardKey.A) ? 1f : 0f) - (input.IsKeyDown((MapKeyboardKey)KeyboardKey.D) ? 1f : 0f);
            if (Aboard) forward = side = 0f;   // ⭐ CE-1210 (§8n MS-1) — a man in a seat is held by it
            if (lookFree)
            {
                // ⭐ CE-1204 (LK-2, LK-4) — A/D turn him (his view turns with him); the look stays within his neck, dragging the body past it
                float turn = side * FreeLookTurnRate * _dt;
                _bodyYaw += turn;
                _yaw += turn;
                side = 0f;
                _bodyYaw = DragBody(_bodyYaw, _yaw, _world() is { } nw ? Fdp.Toolkit.Perception.Look.LookOf.HeadLimit(nw, Unit)
                                                                       : Fdp.Toolkit.Perception.Look.LookOf.MaxHeadYaw);
            }
            else if (ShoulderBody)
            {
                // ⭐ CE-1293 (UC-5) — over the shoulder: the upper body is drawn turned to the aim at once (the twist); the body itself lags —
                //   standing, it follows only past ShoulderLag; moving, it turns to the look at BodyTurnRate — and W / A / S / D move
                //   relative to the CAMERA (the frame's axes turned from the look into the body's)
                bool moving = forward != 0f || side != 0f;
                _bodyYaw = moving ? TurnToward(_bodyYaw, _yaw, BodyTurnRate * _dt) : DragBody(_bodyYaw, _yaw, ShoulderLag);
                float off = Fdp.Toolkit.Perception.Look.LookOf.Wrap(_yaw - _bodyYaw);
                (forward, side) = (forward * MathF.Cos(off) - side * MathF.Sin(off), forward * MathF.Sin(off) + side * MathF.Cos(off));
            }
            else _bodyYaw = _yaw;
            // ⭐ CE-2168 (MD-D) — a flyer's climb axis; ⭐ CE-1095 (§8f P-2) — a plane's is its look's pitch (mouse-aim), and it does not strafe
            var scheme = Scheme;
            // ⭐ CE-1351 — a flyer climbs on Q (or Space) and descends on Z (or C), a plane's nose too; A/D TURN it (the heading led ahead of
            //   the nose), never strafe; the mouse is a free look. 🔒 User, 2026-10-25: "possessed helicopter needs some key to go up and
            //   down vertically, maybe Q/Z". ⛔ SUPERSEDED: a flyer turned to where the camera looked and a plane climbed on the look's pitch.
            float climbKeys = (input.IsKeyDown((MapKeyboardKey)KeyboardKey.Q) || input.IsKeyDown((MapKeyboardKey)KeyboardKey.Space) ? 1f : 0f)
                            - (input.IsKeyDown((MapKeyboardKey)KeyboardKey.Z) || input.IsKeyDown((MapKeyboardKey)KeyboardKey.C) ? 1f : 0f);
            float vertical = IsFlyer ? climbKeys : 0f;
            // ⭐ CE-1354 — held A/D leads the nose; released, the heading at that moment is HELD, never re-read from the airframe. 🔴 Re-read
            //   every frame, it was a feedback loop with a one-frame delay (the sim applies a frame on the next tick): a helicopter turns a
            //   small gap in ONE tick, so after a turn left a gap it chased last frame's heading and back again, one turn step (~1.2°) each
            //   frame, for ever — measured live, the sent yaw alternating 0.9566 / 0.9774 frame by frame. 🔒 User, 2026-10-25: "the shivering
            //   starts when i start turning the helicopter" · "the yello circle aiming point is jumping from left to right (one frame left from
            //   center, other frame right)".
            float flyerHeading;
            if (side != 0f) { flyerHeading = FlyerHeading(_hullYaw, side); _heldHeading = null; }
            else flyerHeading = _heldHeading ??= _hullYaw;
            if (IsFlyer) side = 0f;
            bool run = input.IsKeyDown(MapKeyboardKey.LeftShift) || input.IsKeyDown(MapKeyboardKey.RightShift);
            Running = run;
            float wheel = input.MouseWheelMove;
            Span<int> slots = stackalloc int[PossessionStation.Hud.MaxSlots];
            int slotCount = _world() is { } mw ? PossessionStation.Hud.Slots(mw, Unit, slots) : 0;
            if (slotCount == 0) slots[slotCount++] = 0;
            if (slots.Slice(0, slotCount).IndexOf(Mount) < 0) Mount = slots[0];   // ⭐ CE-1158 — a controlled weapon gone (he left the seat)
            // ⭐ CE-1307 — a man who takes a seat that works a weapon starts ON that weapon (a gunner's job is the gun): the first controlled
            //   index, once per seat; 1 still picks his own. 📌 Live: the APC gunner's frames said Mount 0, his rifle — the M2 never moved.
            int controlled = -1;
            for (int k = 0; k < slotCount && controlled < 0; k++)
                if (Fdp.Toolkit.Combat.WeaponSystems.IsControlledIndex(slots[k])) controlled = slots[k];
            if (controlled != _seatedGun) { if (controlled >= 0) Mount = controlled; _seatedGun = controlled; }
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
                // ⭐ CE-1293 (R-1034, superseding R-1033's wheel) — the wheel is the camera distance, possessed as when watching; out of the
                //   eyes switches the shoulder view on, all the way in switches it off. Weapons are 1-4 only.
                if (wheel != 0f) WheelDistance(wheel);
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
            // ⭐ CE-1293 (UC-6) — the lean also puts the camera over that shoulder (it stays there)
            if (Leaning == Fdp.Toolkit.Combat.LeanSide.LeanLeft) _station.ShoulderSide = -1;
            else if (Leaning == Fdp.Toolkit.Combat.LeanSide.LeanRight) _station.ShoulderSide = 1;
            BinocularsHeld = Scheme == ControlScheme.Person && input.IsKeyDown((MapKeyboardKey)BinocularsKey);   // ⭐ CE-1219 (§8k B-1)
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
            if (input.IsKeyPressed((MapKeyboardKey)ShoulderKey)) _station.OverShoulder = !_station.OverShoulder;   // ⭐ CE-1201 (§8p OS-1)
            Place(camera);   // ⭐ CE-1073 — this frame's look shows this frame (Update ran before the input)

            // ⭐ CE-1078 / CE-1080 (§8e D-1, D-4) — the crosshair sees units too; a PRESS locks one shot on what it rests on, X drops it (CE-1211)
            (var aimAt, long aimTarget) = Aim(camera);
            AimAt = aimAt;
            AimTarget = aimTarget;
            // ⭐ CE-1264 (AV-5) — I selects what the crosshair rests on (the selection's one writer; nothing when it rests on no unit)
            if (input.IsKeyPressed((MapKeyboardKey)SelectKey) && aimTarget != 0 && _world() is { } selectWorld
                && selectWorld.Bus.IsRegistered<Hrot.Common.Events.SelectEntityCommand>())
                selectWorld.Bus.Publish(new Hrot.Common.Events.SelectEntityCommand { NetworkId = aimTarget });
            bool glassing = BinocularsUp;   // ⭐ CE-1219 (§8k B-1) — binoculars up: LMB designates, the trigger stays up
            if (glassing && input.IsMouseButtonPressed(MapMouseButton.Left)) Designate(aimAt, aimTarget);
            else if (input.IsMouseButtonPressed(MapMouseButton.Left))
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
            UseAim = _world() is { } uw ? UseTargetFor(uw, aimTarget, aimAt) : aimTarget;   // ⭐ CE-1207 (§8m PK-3)
            UseKey(input, UseAim);

            var frame = new OperatorControlFrameEvent
            {
                Seq = ++_seq,
                Forward = forward,
                Lateral = vehicle ? 0f : side,
                Steer = vehicle ? side : 0f,   // ⭐ CE-1351 — A/D steer, hull-locked or not
                Yaw = IsFlyer ? flyerHeading : BodyYaw,   // ⭐ CE-1204 — the body's heading (the look's while it is locked); ⭐ CE-1351 a flyer's from A/D
                Vertical = vertical,
                Gait = run ? (byte)0 : (byte)1,
                Trigger = !glassing && input.IsMouseButtonDown(MapMouseButton.Left) ? (byte)1 : (byte)0,
                Mount = (byte)Mount,
                AimPoint = aimAt,
                Stance = !vehicle && Stance is { } stance ? (byte)((byte)stance + 1) : (byte)0,   // ⭐ CE-1074 / CE-1143 (Ctrl held = crouched)
                ShotSeq = _shotSeq, CancelSeq = _cancelSeq, AmmoSeq = _ammoSeq, ShotTarget = _shotTarget,   // ⭐ CE-1078 / CE-1079
                Lean = (byte)Leaning,                                                                     // ⭐ CE-1212
                Binoculars = BinocularsHeld ? (byte)1 : (byte)0,                                          // ⭐ CE-1219
                UseSeq = _useSeq, UseVerb = _useVerb, UseTarget = _useTarget,                              // ⭐ CE-1158 (U-2)
            };
            _station.Send(_netId, frame);
            return true;
        }

        /// <summary>⭐ <c>CE-1351</c> — the heading a flyer is told to turn to: its own, led <see cref="FlyerTurnLead"/> to the side A/D hold
        /// (<paramref name="side"/> +1 = A, left / counter-clockwise).</summary>
        public static float FlyerHeading(float hullYaw, float side) => hullYaw + Math.Clamp(side, -1f, 1f) * FlyerTurnLead;

        /// <summary>⭐ <c>CE-1204</c> (§8l LK-2) — the body's heading after a free look to <paramref name="look"/>: unchanged while the look
        /// is within <paramref name="neck"/> of it, else dragged so the look sits on the neck's limit.</summary>
        public static float DragBody(float body, float look, float neck)
        {
            float off = Fdp.Toolkit.Perception.Look.LookOf.Wrap(look - body);
            return MathF.Abs(off) <= neck ? body : look - MathF.CopySign(neck, off);
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
            // ⭐ CE-1207 (§8m PK-6) — a tap never sends a hold-only verb (Drop): with nothing else on offer it does nothing
            if (!UseListOpen && _world() is { } tw && Actions(tw, _pressTarget) > 0 && _actions[0].HoldOnly) return;
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

        /// <summary>⭐ <c>CE-1207</c> (§8m PK-3) — what the use key acts on this frame: the unit under the crosshair when it offers a verb, else
        /// the lying stone / round nearest the crosshair's point (a round has no pick box, LR-3).</summary>
        public long UseAim { get; private set; }

        /// <summary>⭐ <c>CE-1207</c> (PK-3) — how far from the crosshair's point a lying round may be to be the use target (m).</summary>
        public const float RoundUnderCrosshairM = 1f;

        /// <summary>⭐ <c>CE-1207</c> (PK-3) — the use target for <paramref name="aimTarget"/> at <paramref name="aimAt"/>: see <see cref="UseAim"/>.</summary>
        public long UseTargetFor(EntityRepository w, long aimTarget, Vector3 aimAt)
        {
            if (aimTarget != 0 && w.HasSingletonManaged<NetworkEntityMap>() && w.GetSingletonManaged<NetworkEntityMap>()!.TryGetEntity(aimTarget, out var under))
            {
                int n = OperatorUseActions.For(w, Unit, under, _actions);
                for (int i = 0; i < n; i++) if (_actions[i].Target == under) return aimTarget;
            }
            return RoundNear(w, aimAt) is long round ? round : aimTarget;
        }

        /// <summary>⭐ <c>CE-1207</c> (PK-3) — the network id of the lying hand-item round within <see cref="RoundUnderCrosshairM"/> of
        /// <paramref name="point"/> and within his reach, nearest the point; null when none.</summary>
        public long? RoundNear(EntityRepository w, Vector3 point)
        {
            if (!w.HasSingletonManaged<NetworkEntityMap>()) return null;
            var map = w.GetSingletonManaged<NetworkEntityMap>()!;
            float best = RoundUnderCrosshairM * RoundUnderCrosshairM;
            long? found = null;
            void Consider(Entity e)
            {
                var at = w.GetComponentRO<SimTransform>(e).Position;
                float d2 = (at.X - point.X) * (at.X - point.X) + (at.Y - point.Y) * (at.Y - point.Y);
                if (d2 > best || !Fdp.Toolkit.Combat.RoundPickUp.IsLying(w, e) || !Fdp.Toolkit.Combat.RoundPickUp.IsHandItem(w, e)
                    || !Fdp.Toolkit.Combat.RoundPickUp.InReach(w, Unit, e) || !map.TryGetNetworkId(e, out long id)) return;
                best = d2;
                found = id;
            }
            if (w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.LightweightRound>())
                foreach (var e in w.Query().With<Fdp.Toolkit.Replication.Components.LightweightRound>().With<SimTransform>().Build()) Consider(e);
            if (w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.BallisticProjectile>())
                foreach (var e in w.Query().With<Fdp.Toolkit.Combat.Components.BallisticProjectile>().With<SimTransform>().Build()) Consider(e);
            return found;
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
            else if (Actions(w, _eHolding ? _pressTarget : UseAim) > 0)
                lines.Add(_actions[0].HoldOnly   // ⭐ CE-1207 (PK-6) — only hold-list verbs (Drop): say so, a tap does nothing
                    ? new UseLine("hold F  " + PossessionStation.Hud.Label(w, _actions[0]), PossessionStation.Hud.UseGreyed)
                    : new UseLine(PossessionStation.Hud.Prompt(w, _actions[0]),
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
