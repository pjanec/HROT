using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ SD-S2c (§3.13c D-104 / D-107) — the remote pilot's own state, a <c>[UnitMemory]</c> struct on the DRONE: survives a restart of its run, recorded,
    /// read by the scenario's <c>memory: RemotePilotState</c> cue. Written ONLY by <see cref="RemotePilotBehavior.Pilot"/>.
    /// </summary>
    [UnitMemory]
    [StructLayout(LayoutKind.Sequential)]
    public struct RemotePilotState
    {
        public PilotPhase Phase;
        /// <summary>1 once the type's link (<see cref="RangeM"/>, <see cref="FailsafeSeconds"/>) and the first pose were read.</summary>
        public byte Ready;
        /// <summary>The launch point (set at each take-off) — where RTH and the failsafe return to.</summary>
        public Vector3 Home;
        /// <summary>The heading it holds while the yaw stick is centred (radians).</summary>
        public float Heading;
        /// <summary>The station's command number it last acted on (<c>RemoteControl.Seq</c>).</summary>
        public uint LastSeq;
        /// <summary>When the failsafe hover ends (sim time).</summary>
        public SimInstant FailsafeUntil;
        public float RangeM, FailsafeSeconds;
        /// <summary>The station flying it this tick (<c>ControlledBy</c>; null: none) — this node's handle.</summary>
        public Entity Station;
    }

    /// <summary>
    /// ⭐⭐⭐ Simulated devices SD-S2c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-104 / D-106 / D-107) — <c>RemotePilot</c>, the brain of a drone
    /// FLOWN THROUGH A GROUND STATION (never possessed): its station is the device whose <see cref="ControlledEntities"/> names it (<see cref="ControlledBy"/>,
    /// the serial pre-pass index — D-147); it reads that station's <see cref="RemoteControl"/> (a declared tick-start read) and its own battery, writes its own
    /// <see cref="DroneLink"/> (range from the station's world pose, the last 20 % falling; a jammer's bubble — D-106) and its own <see cref="ManualDrive"/>.
    /// <para>The phases (<see cref="PilotPhase"/>): TAKEOFF climbs to <see cref="HoverHeightM"/> above its launch point, then MANUAL — the sticks through the ONE mapping
    /// (<see cref="ManualFlight"/>, shared with the possessed drone); LAND comes down where it is; RTH flies to the launch point and lands. ⭐ The FAILSAFE (D-107):
    /// the link lost ⇒ the sticks are ignored, it hovers <c>FailsafeSeconds</c>, then returns home and lands; the link back during the hover ⇒ the station's mode
    /// again; once the failsafe's return has begun it goes on until the station sends a NEW command (the proven GCS rule — RTH is not abandoned on a flicker of
    /// link). The battery empty ⇒ down where it is (the existing rule) and it stays down.</para>
    /// <para>⭐ Parallel (R-4212): it writes ONLY its own entity; the station's components are tick-start reads. ⭐ Everything is flown on the Manual axes the motion
    /// model already flies (take-off, hover hold, the landing onto its feet). 0 B per steady tick.</para>
    /// </summary>
    public static class RemotePilotBehavior
    {
        /// <summary>The height (m above its launch point) a take-off climbs to before the sticks fly it.</summary>
        public const float HoverHeightM = 15f;
        /// <summary>Within this (m, horizontal) of home the return becomes a landing.</summary>
        public const float ArriveM = 2f;
        /// <summary>The return home flies at this share of its top speed, easing in over the last <see cref="BrakeM"/>.</summary>
        public const float ReturnSpeedShare = 0.7f, BrakeM = 30f;
        /// <summary>The landing's descent: −1 … this share of the climb rate, slowed by the height (a gentle touch-down onto its feet).</summary>
        public const float LandMinShare = 0.25f, LandSlowBelowM = 5f;
        /// <summary>A yaw stick inside this is centred: it holds its heading.</summary>
        public const float YawCentred = 0.02f;

        [StructLayout(LayoutKind.Sequential)]
        public struct PilotParams { public byte Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard { public PilotParams Pilot; }

        [BehaviorResolver(BehaviorNames.RemotePilot)]
        public static void Resolve(in NoParams authored, ref Blackboard block, EntityRepository world, Entity self) => block = default;

        // ⭐ offered to a battery drone only (Requires DroneBattery); its station is the device that names it (ControlledEntities)
        [Behavior(BehaviorNames.RemotePilot, Params = typeof(NoParams), Requires = new[] { typeof(DroneBattery) },
                  Description = "Flown by a trainee through a drone ground station (the device naming it): take-off, the sticks, land, return home, the link-loss failsafe.")]
        [BTreeDefinition(BehaviorNames.RemotePilot, Curated = true, Group = "Library/Orders", ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<Blackboard, BTreeContext>()
                .Action(bb => bb.Pilot, Pilot);

        /// <summary>One tick (see the class summary). Never ends: Running.</summary>
        [SharedAiAction]
        public static NodeStatus Pilot(ref PilotParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Running;
            ref var st = ref UnitMemory.Ref<RemotePilotState>(world, self);
            ref readonly var tf = ref world.GetComponentRO<SimTransform>(self);
            var at = tf.Position;
            float heading = HeadingOf(tf.Rotation);
            var gt = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>() : default;
            if (st.Ready == 0) Begin(ref st, world, self, at, heading);

            // ① the station and the link (D-106): the station's command is a tick-start read; its world pose is where the radio is
            var station = ControlledBy.Of(world, self);
            st.Station = station;
            RemoteControl rc = default;
            Vector3? stationAt = null;
            if (!station.IsNull && world.IsAlive(station) && world.IsComponentTypeRegistered<RemoteControl>() && world.HasComponent<RemoteControl>(station))
            {
                rc = world.GetComponentRO<RemoteControl>(station);
                // the radio is where the station is (its SimTransform — placed at its carrier's station each frame; its look offset is centimetres)
                if (rc.Mode != RemoteMode.Off && world.HasComponent<SimTransform>(station)) stationAt = world.GetComponentRO<SimTransform>(station).Position;
            }
            var link = RemoteLinks.Of(world, self, at, stationAt, st.RangeM);
            bool lost = link.Lost != 0;
            // ② the phase — ⚠ "is it on the ground" asks the world query (not free): only where a decision needs it, never in a steady flying tick (R-2084)
            if (DroneBatteries.Empty(world, self) && !(st.Phase == PilotPhase.Grounded && FlightGround.OnTheGround(world, self))) st.Phase = PilotPhase.BatteryDown;
            else if (st.Phase != PilotPhase.BatteryDown)
            {
                if (lost)
                {
                    if (st.Phase is PilotPhase.TakeOff or PilotPhase.Manual or PilotPhase.Land or PilotPhase.ReturnHome)
                    {
                        st.Phase = PilotPhase.FailsafeHover;   // D-107: the sticks are ignored from now
                        st.FailsafeUntil = (SimInstant)(gt.TotalTime + st.FailsafeSeconds);
                    }
                }
                else
                {
                    bool fresh = rc.Seq != st.LastSeq;
                    if (st.Phase == PilotPhase.FailsafeHover) st.Phase = PhaseFor(rc.Mode, ref st, at, heading, FlightGround.OnTheGround(world, self), PilotPhase.Manual);   // a flicker: the mode again
                    else if (fresh) st.Phase = PhaseFor(rc.Mode, ref st, at, heading, FlightGround.OnTheGround(world, self), st.Phase);   // a new command (ends a failsafe return)
                    st.LastSeq = rc.Seq;
                }
                Advance(ref st, world, self, at, gt.TotalTime);
            }

            // ③ the drive (its own) and the link (its own)
            var drive = DriveFor(ref st, world, self, at, heading, in rc, gt.DeltaTime);
            WriteDrive(world, self, in drive);
            link.Phase = st.Phase;
            link.AckSeq = st.LastSeq;
            RemoteLinks.Write(world, self, in link);
            return NodeStatus.Running;
        }

        private static void Begin(ref RemotePilotState st, EntityRepository world, Entity self, Vector3 at, float heading)
        {
            var dto = LinkOf(world, self);
            st.RangeM = dto?.RangeM ?? 3000f;
            st.FailsafeSeconds = dto?.FailsafeSeconds ?? 5f;
            st.Home = at;
            st.Heading = heading;
            st.Phase = FlightGround.OnTheGround(world, self) ? PilotPhase.Grounded : PilotPhase.Manual;
            st.Ready = 1;
        }

        /// <summary>The drone type's radio link (<see cref="RemoteLinkDto"/>), or null (a type with none: the defaults).</summary>
        public static RemoteLinkDto? LinkOf(EntityRepository world, Entity self)
            => world.IsComponentTypeRegistered<TkbIdentity>() && world.HasComponent<TkbIdentity>(self) && world.HasSingletonManaged<ITkbDatabase>()
               && world.GetSingletonManaged<ITkbDatabase>() is { } db && db.TryGetByType((long)world.GetComponentRO<TkbIdentity>(self).TkbType, out var t)
                ? t?.GetDescriptor<RemoteLinkDto>() : null;

        /// <summary>The phase a command asks for: FLY from the ground takes off (the launch point and heading noted), in the air flies the sticks.</summary>
        private static PilotPhase PhaseFor(RemoteMode mode, ref RemotePilotState st, Vector3 at, float heading, bool onGround, PilotPhase otherwise)
        {
            switch (mode)
            {
                case RemoteMode.Fly:
                    if (!onGround) return PilotPhase.Manual;
                    st.Home = at; st.Heading = heading;
                    return PilotPhase.TakeOff;
                case RemoteMode.Land: return onGround ? PilotPhase.Grounded : PilotPhase.Land;
                case RemoteMode.ReturnHome: return onGround ? PilotPhase.Grounded : PilotPhase.ReturnHome;
                default: return onGround ? PilotPhase.Grounded : (otherwise is PilotPhase.FailsafeReturn or PilotPhase.FailsafeLand ? otherwise : PilotPhase.Manual);
            }
        }

        /// <summary>The phase's own progress: the take-off reaches its height, the hover's time ends, the return arrives, the landing touches down.</summary>
        private static void Advance(ref RemotePilotState st, EntityRepository world, Entity self, Vector3 at, double now)
        {
            switch (st.Phase)
            {
                case PilotPhase.TakeOff:
                    if (at.Z - st.Home.Z >= HoverHeightM) st.Phase = PilotPhase.Manual;   // above its launch point (no world query)
                    break;
                case PilotPhase.FailsafeHover:
                    if (now >= st.FailsafeUntil) st.Phase = PilotPhase.FailsafeReturn;
                    break;
                case PilotPhase.ReturnHome: case PilotPhase.FailsafeReturn:
                    if (new Vector2(st.Home.X - at.X, st.Home.Y - at.Y).Length() < ArriveM && HorizontalSpeed(world, self) < 1f)
                        st.Phase = st.Phase == PilotPhase.ReturnHome ? PilotPhase.Land : PilotPhase.FailsafeLand;
                    break;
                case PilotPhase.Land: case PilotPhase.FailsafeLand:
                    if (TouchedDown(world, self)) st.Phase = PilotPhase.Grounded;
                    break;
            }
        }

        private static ManualDrive DriveFor(ref RemotePilotState st, EntityRepository world, Entity self, Vector3 at, float heading, in RemoteControl rc, float dt)
        {
            switch (st.Phase)
            {
                case PilotPhase.TakeOff:
                    return ManualFlight.FromAxes(0f, 0f, st.Heading, 1f, seq: st.LastSeq);
                case PilotPhase.Manual:
                    if (rc.Mode != RemoteMode.Fly) return ManualFlight.FromAxes(0f, 0f, st.Heading, 0f, seq: st.LastSeq);   // not flown: it holds its hover
                    float yaw = st.Heading;
                    if (MathF.Abs(rc.Yaw) > YawCentred) { yaw = ManualFlight.Heading(heading, rc.Yaw); st.Heading = heading; }
                    return ManualFlight.FromAxes(rc.Forward, rc.Lateral, yaw, rc.Vertical, seq: st.LastSeq);
                case PilotPhase.Land: case PilotPhase.FailsafeLand: case PilotPhase.BatteryDown:
                {
                    if (TouchedDown(world, self)) return ManualFlight.FromAxes(0f, 0f, st.Heading, 0f, seq: st.LastSeq);
                    float agl = at.Z - GroundAt(world, at);
                    return ManualFlight.FromAxes(0f, 0f, st.Heading, -Math.Clamp(agl / LandSlowBelowM, LandMinShare, 1f), seq: st.LastSeq);
                }
                case PilotPhase.ReturnHome: case PilotPhase.FailsafeReturn:
                {
                    var to = new Vector2(st.Home.X - at.X, st.Home.Y - at.Y);
                    float h = to.Length();
                    float cruiseZ = MathF.Max(at.Z, st.Home.Z + HoverHeightM);
                    float vertical = Math.Clamp((cruiseZ - at.Z) / LandSlowBelowM, -1f, 1f);
                    if (h > 5f) st.Heading = MathF.Atan2(to.Y, to.X);   // it turns to face home (its camera looks the way it flies)
                    if (h < 1e-3f) return ManualFlight.FromAxes(0f, 0f, st.Heading, vertical, seq: st.LastSeq);
                    float share = Math.Clamp(h / BrakeM, 0f, 1f) * ReturnSpeedShare;
                    var dir = to / h;
                    float c = MathF.Cos(heading), s = MathF.Sin(heading);
                    return ManualFlight.FromAxes((dir.X * c + dir.Y * s) * share, (-dir.X * s + dir.Y * c) * share, st.Heading, vertical, seq: st.LastSeq);
                }
                default:   // Grounded, the failsafe hover: hold
                    return ManualFlight.FromAxes(0f, 0f, st.Heading, 0f, seq: st.LastSeq);
            }
        }

        /// <summary>Its own drive and the Manual motion mode (as <c>OperatorControlBehavior</c>): written on change, added WITH its value.</summary>
        private static void WriteDrive(EntityRepository world, Entity self, in ManualDrive drive)
        {
            if (world.IsComponentTypeRegistered<NavigationIntent>())
            {
                if (!world.HasComponent<NavigationIntent>(self)) world.AddComponent(self, new NavigationIntent { Mode = NavigationMode.Manual });
                else if (world.GetComponentRO<NavigationIntent>(self).Mode != NavigationMode.Manual)
                {
                    ref var intent = ref world.GetComponentRW<NavigationIntent>(self);
                    intent.Mode = NavigationMode.Manual;
                    intent.TargetSpeed = 0f;
                    unchecked { intent.IntentId++; }
                }
            }
            if (!world.IsComponentTypeRegistered<ManualDrive>()) return;
            if (world.HasComponent<ManualDrive>(self))
            {
                ref readonly var now = ref world.GetComponentRO<ManualDrive>(self);
                if (now.Forward == drive.Forward && now.Lateral == drive.Lateral && now.Yaw == drive.Yaw && now.Vertical == drive.Vertical && now.Seq == drive.Seq) return;
                world.SetComponent(self, drive);
            }
            else world.AddComponent(self, drive);
        }

        private static bool TouchedDown(EntityRepository world, Entity self)
            => world.IsComponentTypeRegistered<FlightState>() && world.HasComponent<FlightState>(self) && world.GetComponentRO<FlightState>(self).Airborne == 0;

        private static float GroundAt(EntityRepository world, Vector3 at) => Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(at.X, at.Y) ?? 0f;

        private static float HorizontalSpeed(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<SimVelocity>(self)) return 0f;
            var v = world.GetComponentRO<SimVelocity>(self).Linear;
            return new Vector2(v.X, v.Y).Length();
        }

        /// <summary>The body's heading (radians, X-forward, Z-up) — its nose projected on the ground, so a tilt does not move it.</summary>
        public static float HeadingOf(Quaternion rotation)
        {
            var f = Vector3.Transform(Vector3.UnitX, rotation);
            return MathF.Atan2(f.Y, f.X);
        }
    }
}
