using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.Devices.DroneStation;
using Hrot.CGF.Configuration;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices SD-S2c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-104 – D-110, §3.13f D-147) — THE DRONE GROUND STATION and the drone
    /// flown THROUGH it: the pairing (<see cref="ControlledEntities"/> + the pre-pass index <see cref="ControlledBy"/>), the link (<see cref="DroneLink"/>: range falloff,
    /// a jammer's bubble, lost), the station's modes and the press queued in pause, the remote pilot's phases and its failsafe, and the camera's
    /// <see cref="DeviceView"/>. The world is the production registries with the real catalogue (the recon / bomber types, the jammer), flown by the real
    /// <see cref="AirMotionSystem"/>; the last rails run the brain tick and the device tick themselves (the parallel checks, 0 B).
    /// </summary>
    public sealed class DroneStationTests
    {
        private const float Dt = 1f / 60f;
        private const int DroneNetId = 5001;

        private sealed class Rig : IDisposable
        {
            public readonly EntityRepository W = new();
            public readonly Entity Station, Drone;
            public readonly AirMotionSystem Air = new();
            public readonly DroneImpactSystem Impact = new();   // ⭐ the take-off from its feet and the touch-down are not crashes (ManualTouch)
            public RemotePilotBehavior.PilotParams Pp;
            public DroneStationBehavior.DroneStationParams Sp;
            public DroneStationBehavior.DroneStationWork Sw;
            public double Time;
            public readonly Vector3 Launch;
            private readonly byte[] _presses = new byte[16];
            private readonly float[] _axes = new float[4];

            public Rig(long droneType = TkbEntityTypes.Drone_Recon, float battery = 1f)
            {
                SimHostComponentRegistry.RegisterAll(W);
                CognitiveComponentRegistry.RegisterAll(W);
                BlueprintTierTable.RegisterAll(W);
                var tkb = HrotEnvironment.CreateTkb();
                W.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
                BrainPhase.Prepare(W);   // the types brain code needs (ManualDrive …)
                // a flat ground (the terrain the impact system meets, the pilot's height above it)
                W.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainWorld { BoundsMin = new Vector2(-7000f, -7000f), BoundsMax = new Vector2(7000f, 7000f) });

                Launch = new Vector3(4f, 0f, 0.08f);
                Drone = W.CreateEntity();
                var north = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
                W.AddComponent(Drone, new SimTransform { Position = Launch, Rotation = north });
                W.AddComponent(Drone, new SimVelocity());
                W.AddComponent(Drone, new TkbIdentity { TkbType = (TkbTypeId)droneType });
                W.AddComponent(Drone, new Locomotion { Class = LocomotionClass.Rotorcraft });
                W.AddComponent(Drone, FlightParams.From(tkb.GetByType(droneType)!.GetDescriptor<FlightParamsDto>()!));
                W.AddComponent(Drone, new FlightState { Airborne = 1 });
                W.AddComponent(Drone, new NavigationIntent());
                W.AddComponent(Drone, new NavigationStatus());
                W.AddComponent(Drone, new Health { Current = 10, Max = 10 });
                W.AddComponent(Drone, new EntityInfo { ForceId = ForceId.Friend });
                W.AddComponent(Drone, new DroneBattery { Remaining01 = battery });
                W.AddComponent(Drone, new DroneLink { Lost = 1 });   // as FlightTkbTranslator creates a radio-flown type
                W.SetAuthority<SimTransform>(Drone, true);
                var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
                W.SetSingletonManaged(map);
                W.AddComponent(Drone, new NetworkIdentity { Value = DroneNetId });
                map.Register(DroneNetId, Drone);

                Station = W.CreateEntity();
                W.AddComponent(Station, new SimTransform { Position = new Vector3(0f, 0f, 1.2f), Rotation = Quaternion.Identity });
                W.AddComponent(Station, new DeviceInfo { DeviceType = TkbEntityTypes.Device_DroneStation, Behavior = new FixedString64(BehaviorNames.DeviceDroneStation) });
                W.AddComponent(Station, ControlledEntities.Of(DroneNetId));   // authored as the drone's GUID, resolved to its network id by the loader
                W.AddComponent(Station, new RemoteControl());          // as DeviceTkbTranslator creates a ground station
                Input();
            }

            public DroneStationState Gs => UnitMemory.Get<DroneStationState>(W, Station);
            public RemotePilotState Pilot => UnitMemory.Get<RemotePilotState>(W, Drone);
            public DroneLink Link => W.HasComponent<DroneLink>(Drone) ? W.GetComponentRO<DroneLink>(Drone) : default;
            public Vector3 At => W.GetComponentRO<SimTransform>(Drone).Position;

            /// <summary>Press the station's button (its counter moves, as the merged device input holds it).</summary>
            public void Press(int button) { _presses[button]++; Input(); }

            /// <summary>The sticks (Mode 2 axes 0-3).</summary>
            public void Sticks(float yaw = 0f, float throttle = 0f, float roll = 0f, float pitch = 0f)
            {
                _axes[0] = yaw; _axes[1] = throttle; _axes[2] = roll; _axes[3] = pitch;
                Input();
            }

            private void Input()
            {
                var c = new DeviceControl();
                for (int i = 0; i < 16; i++) c.Presses[i] = _presses[i];
                for (int i = 0; i < 4; i++) c.Axes[i] = _axes[i];
                var input = new DeviceInput { Control = c };
                if (W.HasComponent<DeviceInput>(Station)) W.SetComponent(Station, input); else W.AddComponent(Station, input);
            }

            /// <summary>One frame: the brains' pre-pass and the drone's brain on a live frame, the station every frame (paused or not), then the muscle.</summary>
            public void Frame(float dt = Dt)
            {
                Time += dt;
                W.SetSingleton(new GlobalTime { TotalTime = Time, DeltaTime = dt });
                if (dt > 0f)
                {
                    BrainPhase.Prepare(W);
                    Assert.Equal(Fbt.NodeStatus.Running, RemotePilotBehavior.Pilot(ref Pp, Drone, W));
                }
                Assert.Equal(Fbt.NodeStatus.Running, DroneStationBehavior.Tick(ref Sp, ref Sw, Station, W));
                if (dt > 0f) { Air.Execute(W, dt); Impact.Execute(W, dt); DroneBatterySystem.Drain(W, Drone, dt); }
            }

            public void Run(float seconds, float dt = Dt) { for (int i = 0, n = (int)MathF.Round(seconds / dt); i < n; i++) Frame(dt); }

            /// <summary>POWER, ARM, TAKEOFF and the climb to the hover.</summary>
            public void Launch_()
            {
                Run(0.1f);
                Press(DroneStationChannels.Power); Run(0.2f);
                Press(DroneStationChannels.Arm); Run(0.2f);
                Press(DroneStationChannels.TakeOff); Run(5f);
            }

            public void Dispose() => W.Dispose();
        }

        /// <summary>⭐ D-110 / D-142 — the ground station's TKB type names its behaviour and its elements in the wire order the behaviour reads; the drone types
        /// carry their radio link (D-106: recon 5 km, FPV and bomber 3 km, 5 s).</summary>
        [Fact]
        public void SDS2c_TheStationType_NamesItsElementsInWireOrder_AndTheDronesCarryTheirLink()
        {
            var tkb = HrotEnvironment.CreateTkb();
            var gs = tkb.GetByType(TkbEntityTypes.Device_DroneStation)!.GetDescriptor<DeviceDefDto>()!;
            Assert.Equal(BehaviorNames.DeviceDroneStation, gs.Behavior);
            Assert.Equal(DroneStationChannels.Count, gs.CountOf(DeviceElementKind.Button));
            Assert.Equal(DroneStationChannels.Power, gs.ButtonOf("POWER"));
            Assert.Equal(DroneStationChannels.Arm, gs.ButtonOf("ARM"));
            Assert.Equal(DroneStationChannels.TakeOff, gs.ButtonOf("TAKEOFF"));
            Assert.Equal(DroneStationChannels.Land, gs.ButtonOf("LAND"));
            Assert.Equal(DroneStationChannels.Rth, gs.ButtonOf("RTH"));
            Assert.Equal(DroneStationChannels.CamUp, gs.ButtonOf("CAM UP"));
            Assert.Equal(DroneStationChannels.CamDown, gs.ButtonOf("CAM DOWN"));
            Assert.True(gs.TryElement("YAW", out var k, out int yaw) && k == DeviceElementKind.Axis && yaw == DroneStationChannels.AxisYaw);
            Assert.True(gs.TryElement("THROTTLE", out _, out int thr) && thr == DroneStationChannels.AxisThrottle);
            Assert.True(gs.TryElement("ROLL", out _, out int roll) && roll == DroneStationChannels.AxisRoll);
            Assert.True(gs.TryElement("PITCH", out _, out int pitch) && pitch == DroneStationChannels.AxisPitch);
            Assert.Equal(5000f, tkb.GetByType(TkbEntityTypes.Drone_Recon)!.GetDescriptor<RemoteLinkDto>()!.RangeM);
            Assert.Equal(3000f, tkb.GetByType(TkbEntityTypes.Drone_Fpv)!.GetDescriptor<RemoteLinkDto>()!.RangeM);
            Assert.Equal(3000f, tkb.GetByType(TkbEntityTypes.Drone_Bomber)!.GetDescriptor<RemoteLinkDto>()!.RangeM);
            Assert.Equal(5f, tkb.GetByType(TkbEntityTypes.Drone_Bomber)!.GetDescriptor<RemoteLinkDto>()!.FailsafeSeconds);
        }

        /// <summary>
        /// ⭐⭐ D-147 — THE PAIRING INDEX: built in the brains' serial pre-pass from every device's <see cref="ControlledEntities"/>; an entity no device names, a
        /// removed pairing and a dead device answer nothing; a steady frame allocates nothing.
        /// 🔴 Red-proof (compiles): <c>ControlledBy.PrepareFrame</c> dropped from <c>BrainPhase.PrepareToolkitFrame</c> ⇒ the drone has no station ⇒ red.
        /// </summary>
        [Fact]
        public void SDS2c_ControlledBy_IsTheSerialPrePassIndex_AndAllocatesNothing()
        {
            using var r = new Rig();
            var other = r.W.CreateEntity();
            BrainPhase.Prepare(r.W);
            Assert.Equal(r.Station, ControlledBy.Of(r.W, r.Drone));
            Assert.True(ControlledBy.Of(r.W, other).IsNull);
            for (int i = 0; i < 20; i++) BrainPhase.Prepare(r.W);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) { BrainPhase.Prepare(r.W); _ = ControlledBy.Of(r.W, r.Drone); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            r.W.RemoveComponent<ControlledEntities>(r.Station);
            BrainPhase.Prepare(r.W);
            Assert.True(ControlledBy.Of(r.W, r.Drone).IsNull);
        }

        /// <summary>
        /// ⭐⭐ D-106 — THE LINK: quality 1 inside 80 % of the range, falling linearly to 0 at it; no station ⇒ lost; inside the bubble of a jammer of another
        /// force ⇒ jammed and lost — ⭐ whatever <see cref="DroneJamming.DronesUnjammable"/> says (a radio link, not the AI's optical fibre).
        /// 🔴 Red-proof (compiles): <c>RemoteLinks.Of</c> reading <c>DroneJamming.Jammed</c> (the unjammable switch) instead of <c>InBubble</c> ⇒ never jammed ⇒ red.
        /// </summary>
        [Fact]
        public void SDS2c_TheLink_FallsOverTheLastFifth_IsLostWithNoStation_AndAJammerCutsIt()
        {
            Assert.Equal(1f, RemoteLinks.Quality(3999f, 5000f));
            Assert.Equal(0.5f, RemoteLinks.Quality(4500f, 5000f), 4);
            Assert.Equal(0f, RemoteLinks.Quality(5000f, 5000f));
            using var r = new Rig();
            Assert.Equal(1, RemoteLinks.Of(r.W, r.Drone, Vector3.Zero, null, 5000f).Lost);
            Assert.Equal(0, RemoteLinks.Of(r.W, r.Drone, Vector3.Zero, new Vector3(100f, 0f, 0f), 5000f).Lost);
            var jammer = r.W.CreateEntity();
            r.W.AddComponent(jammer, new SimTransform { Position = new Vector3(0f, 300f, 0f), Rotation = Quaternion.Identity });
            r.W.AddComponent(jammer, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Ew_DroneJammer });
            r.W.AddComponent(jammer, new EntityInfo { ForceId = ForceId.Neutral });
            BrainPhase.Prepare(r.W);
            Assert.True(DroneJamming.DronesUnjammable);
            var inBubble = RemoteLinks.Of(r.W, r.Drone, new Vector3(0f, 0f, 20f), new Vector3(1f, 0f, 0f), 5000f);
            Assert.Equal(1, inBubble.Jammed);
            Assert.Equal(1, inBubble.Lost);
            Assert.Equal(0, RemoteLinks.Of(r.W, r.Drone, new Vector3(0f, -200f, 20f), new Vector3(1f, 0f, 0f), 5000f).Jammed);
        }

        /// <summary>
        /// ⭐⭐⭐ D-104 / D-110 — THE STATION FLIES THE DRONE: POWER ⇒ LINKED (the drone answers), ARM ⇒ ARMED, TAKEOFF ⇒ it climbs to its hover height (HOVER),
        /// the right stick flies it north (FLYING — through the one mapping, <see cref="ManualFlight"/>), LAND ⇒ it touches down (LANDED). The drone is never
        /// possessed: the station's command is on the station, the drone writes only itself.
        /// 🔴 Red-proof (compiles): the station's <c>WriteCommand</c> skipped ⇒ the drone never hears it ⇒ never LINKED ⇒ red; and
        /// <c>DroneImpactSystem.ManualTouch</c> false ⇒ the take-off from its feet is a ground impact (found by <c>dv-drone-fly</c>) ⇒ red.
        /// </summary>
        [Fact]
        public void SDS2c_TheStation_FliesTheDrone_OffLinkedArmedHoverFlyingLanded()
        {
            using var r = new Rig();
            r.Run(0.2f);
            Assert.Equal(DroneStationMode.Off, r.Gs.Mode);
            Assert.Equal(PilotPhase.Grounded, r.Link.Phase);
            r.Press(DroneStationChannels.Power); r.Run(0.2f);
            Assert.Equal(DroneStationMode.Linked, r.Gs.Mode);
            Assert.Equal(0, r.Link.Lost);
            r.Press(DroneStationChannels.TakeOff); r.Run(0.5f);                     // not armed: refused
            Assert.Equal(PilotPhase.Grounded, r.Link.Phase);
            r.Press(DroneStationChannels.Arm); r.Run(0.2f);
            Assert.Equal(DroneStationMode.Armed, r.Gs.Mode);
            r.Press(DroneStationChannels.TakeOff);
            for (int i = 0; i < 300; i++)                                             // 5 s: off its feet through its 0.8 m contact band
            {
                r.Frame();
                Assert.Equal(1, r.W.GetComponentRO<FlightState>(r.Drone).Airborne);   // ⭐ never read as a ground impact (DroneImpactSystem.ManualTouch)
            }
            Assert.Equal(PilotPhase.Manual, r.Link.Phase);
            Assert.InRange(r.At.Z, RemotePilotBehavior.HoverHeightM - 0.5f, RemotePilotBehavior.HoverHeightM + 3f);
            Assert.Equal(DroneStationMode.Hover, r.Gs.Mode);
            Assert.InRange(r.At.Y, -0.5f, 0.5f);                                      // a vertical climb
            r.Sticks(pitch: 1f); r.Run(4f);
            Assert.Equal(DroneStationMode.Flying, r.Gs.Mode);
            Assert.True(r.At.Y > 60f, $"flew north: y {r.At.Y}");
            Assert.InRange(r.At.X, 2f, 6f);                                           // straight: its nose north, no drift
            r.Sticks(); r.Run(2f);
            float y = r.At.Y;
            r.Run(1f);
            Assert.InRange(r.At.Y - y, -0.3f, 0.3f);                                  // released: it holds its hover
            r.Press(DroneStationChannels.Land); r.Run(10f);
            Assert.Equal(PilotPhase.Grounded, r.Link.Phase);
            Assert.Equal(0, r.W.GetComponentRO<FlightState>(r.Drone).Airborne);
            Assert.Equal(DroneStationMode.Landed, r.Gs.Mode);
            Assert.InRange(r.At.Y - y, -1f, 1f);                                      // where it was
            Assert.True(r.W.GetComponentRO<DroneBattery>(r.Drone).Remaining01 < 1f);
            Assert.False(r.W.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(r.Drone));
            Assert.Equal(10f, r.W.GetComponentRO<Health>(r.Drone).Current);                // ⭐ the take-off and the touch-down on its feet: no crash
        }

        /// <summary>
        /// ⭐⭐⭐ D-107 — THE FAILSAFE: the station out of range ⇒ lost: the sticks are ignored, it hovers <c>FailsafeSeconds</c>, then flies home and lands — and the
        /// link coming back during the RETURN does not stop it (no new command); a link back during the HOVER gives the station's mode again; RTH flies home.
        /// 🔴 Red-proof (compiles): the lost branch leaves the phase Manual ⇒ the held stick flies it on ⇒ red.
        /// </summary>
        [Fact]
        public void SDS2c_LinkLost_HoversThenReturnsHomeAndLands_TheSticksIgnored_ALinkBackInTheHoverResumes()
        {
            using var r = new Rig();
            r.Launch_();
            r.Sticks(pitch: 1f); r.Run(3f);
            var out1 = r.At;
            ref var st = ref r.W.GetComponentRW<SimTransform>(r.Station);
            st.Position = new Vector3(0f, -6000f, 1.2f);                                // the station beyond 5 km: lost
            r.Run(2.5f);                                                              // it brakes out of its 30 m/s (2 s at 15 m/s²)
            Assert.Equal(PilotPhase.FailsafeHover, r.Link.Phase);
            Assert.Equal(1, r.Link.Lost);
            Assert.Equal(DroneStationMode.Failsafe, r.Gs.Mode);
            float y = r.At.Y;
            r.Run(2f);
            Assert.InRange(r.At.Y - y, -0.5f, 0.5f);                                  // the stick still forward: ignored
            r.W.GetComponentRW<SimTransform>(r.Station).Position = new Vector3(0f, 0f, 1.2f);   // back in range during the hover
            r.Run(0.5f);
            Assert.Equal(PilotPhase.Manual, r.Link.Phase);                            // the station's mode again
            Assert.True(r.At.Y > y + 1f);                                             // and its stick flies it

            r.Sticks();
            r.W.GetComponentRW<SimTransform>(r.Station).Position = new Vector3(0f, -6000f, 1.2f);
            r.Run(6f);                                                                // 5 s hover, then home
            Assert.Equal(PilotPhase.FailsafeReturn, r.Link.Phase);
            r.W.GetComponentRW<SimTransform>(r.Station).Position = new Vector3(0f, 0f, 1.2f);   // the link back: the return goes on
            r.Run(20f);
            Assert.Equal(PilotPhase.Grounded, r.Link.Phase);
            Assert.InRange(new Vector2(r.At.X - r.Launch.X, r.At.Y - r.Launch.Y).Length(), 0f, RemotePilotBehavior.ArriveM + 0.5f);
            Assert.True(out1.Y > 30f);
        }

        /// <summary>
        /// ⭐ SD-S2c — <see cref="DroneImpactSystem.ManualTouch"/>: a live flyer with gear on the Manual axes, climbing or coming down no faster than a landing and
        /// nearly still, meets the ground ON PURPOSE; a fast descent, a drifting one, an autonomous flyer or a dead one is still an impact.
        /// 🔴 Red-proof (compiles): <c>ManualTouch</c> always false ⇒ red.
        /// </summary>
        [Fact]
        public void SDS2c_AManualTakeOffOrTouchDown_IsNotAGroundImpact()
        {
            using var r = new Rig();
            r.W.GetComponentRW<NavigationIntent>(r.Drone).Mode = NavigationMode.Manual;
            Assert.True(DroneImpactSystem.ManualTouch(r.W, r.Drone, new Vector3(0f, 0f, 0.25f), alive: true));                       // climbing off its feet
            Assert.True(DroneImpactSystem.ManualTouch(r.W, r.Drone, new Vector3(0.5f, 0f, -2f), alive: true));                       // a touch-down
            Assert.False(DroneImpactSystem.ManualTouch(r.W, r.Drone, new Vector3(0f, 0f, -8f), alive: true));                        // a plunge
            Assert.False(DroneImpactSystem.ManualTouch(r.W, r.Drone, new Vector3(10f, 0f, -1f), alive: true));                       // flying into it
            Assert.False(DroneImpactSystem.ManualTouch(r.W, r.Drone, new Vector3(0f, 0f, 0.25f), alive: false));                     // dead
            r.W.GetComponentRW<NavigationIntent>(r.Drone).Mode = NavigationMode.None;
            Assert.False(DroneImpactSystem.ManualTouch(r.W, r.Drone, new Vector3(0f, 0f, 0.25f), alive: true));                      // autonomous: unchanged
        }

        /// <summary>⭐ D-110 — RTH any time: it flies to the launch point and lands; ⭐ D-108 / D-107 the battery out ⇒ down WHERE IT IS (not home) and it stays down.</summary>
        [Fact]
        public void SDS2c_Rth_FliesHomeAndLands_TheBatteryOut_DownWhereItIs()
        {
            using (var r = new Rig())
            {
                r.Launch_();
                r.Sticks(pitch: 1f); r.Run(3f); r.Sticks();
                r.Press(DroneStationChannels.Rth); r.Run(1f);
                Assert.Equal(DroneStationMode.Returning, r.Gs.Mode);
                r.Run(25f);
                Assert.Equal(PilotPhase.Grounded, r.Link.Phase);
                Assert.InRange(new Vector2(r.At.X - r.Launch.X, r.At.Y - r.Launch.Y).Length(), 0f, RemotePilotBehavior.ArriveM + 0.5f);
            }
            using (var r = new Rig(TkbEntityTypes.Drone_Bomber, battery: 0.01f))      // 12 s of flight
            {
                r.Launch_();
                r.Sticks(pitch: 1f); r.Run(2f); r.Sticks();
                r.Run(12f);
                Assert.Equal(0f, r.W.GetComponentRO<DroneBattery>(r.Drone).Remaining01);
                Assert.Equal(PilotPhase.BatteryDown, r.Link.Phase);
                Assert.Equal(0, r.W.GetComponentRO<FlightState>(r.Drone).Airborne);
                Assert.True(r.At.Y > 20f);                                            // came down where it was, not at home
                r.Press(DroneStationChannels.TakeOff); r.Run(2f);
                Assert.Equal(0, r.W.GetComponentRO<FlightState>(r.Drone).Airborne);   // and it stays down
            }
        }

        /// <summary>
        /// ⭐⭐ R-4300 / D-110 — IN PAUSE the station answers: CAM DOWN tilts the gimbal (the view's pitch) at once, LAND is QUEUED (the command unchanged, nothing
        /// written) and becomes the command on the first live frame; nothing moves.
        /// 🔴 Red-proof (compiles): the pause branch applying the press at once ⇒ Command Land inside the pause ⇒ red.
        /// </summary>
        [Fact]
        public void SDS2c_InPause_TheGimbalTilts_AModePressIsQueuedForTheFirstLiveFrame()
        {
            using var r = new Rig();
            r.Launch_();
            var at = r.At;
            uint seq = r.W.GetComponentRO<RemoteControl>(r.Station).Seq;
            r.Press(DroneStationChannels.CamDown); r.Frame(0f);
            Assert.Equal(-DroneStationBehavior.PitchStep, r.W.GetComponentRO<DeviceView>(r.Station).Pitch, 4);
            Assert.Equal(r.Drone, r.W.GetComponentRO<DeviceView>(r.Station).Entity);
            r.Press(DroneStationChannels.Land);
            for (int i = 0; i < 30; i++) r.Frame(0f);
            Assert.Equal(DroneStationChannels.Bit(DroneStationChannels.Land), r.Gs.Queued);
            Assert.Equal(RemoteMode.Fly, r.Gs.Command);
            Assert.Equal(seq, r.W.GetComponentRO<RemoteControl>(r.Station).Seq);
            Assert.Equal(at, r.At);
            r.Frame();                                                                // the first live frame
            Assert.Equal(RemoteMode.Land, r.W.GetComponentRO<RemoteControl>(r.Station).Mode);
            Assert.Equal(seq + 1, r.W.GetComponentRO<RemoteControl>(r.Station).Seq);
            Assert.Equal(0, r.Gs.Queued);
            r.Run(8f);
            Assert.Equal(PilotPhase.Grounded, r.Link.Phase);
        }

        /// <summary>
        /// ⭐ D-109 — the station's VIEW: off ⇒ nothing; powered ⇒ its drone at its eye; CAM UP / DOWN step the pitch 15° between level and straight down.
        /// </summary>
        [Fact]
        public void SDS2c_ThePoweredStation_FilmsThroughItsDrone_CamUpDownStepThePitch()
        {
            using var r = new Rig();
            r.Run(0.1f);
            Assert.False(DeviceViews.TryTarget(r.W, r.Station, out _, out _, out _));
            r.Press(DroneStationChannels.Power); r.Run(0.1f);
            Assert.True(DeviceViews.TryTarget(r.W, r.Station, out var e, out var mode, out float pitch));
            Assert.Equal(r.Drone, e); Assert.Equal(DeviceViewMode.Eyes, mode); Assert.Equal(0f, pitch);
            r.Press(DroneStationChannels.CamUp); r.Run(0.1f);
            Assert.Equal(0f, r.W.GetComponentRO<DeviceView>(r.Station).Pitch);         // level is the top
            for (int i = 0; i < 8; i++) { r.Press(DroneStationChannels.CamDown); r.Frame(); }
            Assert.Equal(DroneStationBehavior.PitchMin, r.W.GetComponentRO<DeviceView>(r.Station).Pitch, 4);
            r.Press(DroneStationChannels.Power); r.Run(0.1f);
            Assert.False(DeviceViews.TryTarget(r.W, r.Station, out _, out _, out _));
        }

        // ── end to end: the brain tick and the device tick themselves (the parallel checks run them under HROT_PARALLEL_SIM / HROT_READ_CHECK) ─────────

        /// <summary>
        /// ⭐⭐ The production ticks: the brain tick runs the drone's RemotePilot (assigned like any task), the device tick the station — paired, it takes off, the
        /// stick flies it — through the possession system's rule that Manual motion lives only under a possession (a device-commanded unit is its second
        /// owner) — and a steady flying frame of both ticks (and the pre-pass) allocates NOTHING. Run with the parallel checks on (R-4212):
        /// <c>HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1</c> — an undeclared cross-entity read throws.
        /// 🔴 Red-proof (compiles): the <c>ControlledBy</c> skip removed from <c>PossessionSystem.LetGoOfManual</c> ⇒ the take-off is undone every frame ⇒ red
        /// (found by <c>dv-drone-fly</c>, not by a rail — this one now pins it).
        /// </summary>
        [Fact]
        public void SDS2c_EndToEnd_TheTicksFlyIt_AndASteadyFrameAllocatesNothing()
        {
            using var r = new Rig();
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var ingress = new BehaviorIngressSystem(registry);
            var brains = new BrainTickSystem(registry);
            var devices = new DeviceTickSystem(registry, ingress);
            var possession = new Fdp.Toolkit.Behavior.Possession.PossessionSystem(BehaviorNames.OperatorControl);   // its "Manual only under a possession" rule (LetGoOfManual)
            r.W.AddComponent(r.Drone, new BehaviorState());
            r.W.AddComponent(r.Station, new BehaviorState());
            r.W.SetAuthority<BehaviorState>(r.Drone, true);   // this node runs the drone's brain (the possession system's OwnsBrain)
            Assert.True(ingress.AssignNow(r.W, r.Drone, BehaviorNames.RemotePilot, "{}", BehaviorOrigin.Superior));
            long measured = 0;
            void Frame(bool measure)
            {
                r.Time += Dt;
                r.W.SetSingleton(new GlobalTime { TotalTime = r.Time, DeltaTime = Dt });
                r.W.Tick();                                       // a new tick (as the kernel): the tick-start view is taken again
                r.W.Bus.SwapBuffers();
                possession.Execute(r.W, Dt);                      // before the brains, as the CGF pack (⭐ a paired drone keeps its Manual axes)
                long before = GC.GetAllocatedBytesForCurrentThread();
                brains.Execute(r.W, Dt);
                devices.Execute(r.W, Dt);
                if (measure) measured += GC.GetAllocatedBytesForCurrentThread() - before;
                r.Air.Execute(r.W, Dt);
                r.Impact.Execute(r.W, Dt);
                DroneBatterySystem.Drain(r.W, r.Drone, Dt);
            }
            void Run(float s) { for (int i = 0; i < (int)(s / Dt); i++) Frame(false); }
            Run(0.2f);
            r.Press(DroneStationChannels.Power); Run(0.3f);
            Assert.Equal(DroneStationMode.Linked, r.Gs.Mode);
            r.Press(DroneStationChannels.Arm); Run(0.3f);
            r.Press(DroneStationChannels.TakeOff); Run(5f);
            Assert.Equal(PilotPhase.Manual, r.Link.Phase);
            r.Sticks(pitch: 0.5f); Run(2f);
            Assert.True(r.At.Y > 10f, $"flew north: y {r.At.Y}");
            Assert.Equal(DroneStationMode.Flying, r.Gs.Mode);
            for (int i = 0; i < 60; i++) Frame(true);
            Assert.Equal(0, measured);
        }
    }
}
