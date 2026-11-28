using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.AI.Behaviors.Devices.FireControlUnit;
using Hrot.CGF.Configuration;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Simulated devices SD-S4 (docs/designs/devices/DESIGN_Simulated_Devices.md sec 3.13b D-92 - D-96 / D-112, sec 3.13g D-172) - THE FIRE-CONTROL UNIT's procedure, mounted in a weapon's
    /// station (the launcher sight unit): <c>OFF -> TEST -> STANDBY -> ARMED -> GUIDING -> ARMED / STANDBY</c>, ARM needing a round, FIRE refused in pause and never remembered, the guide
    /// line written on live frames only, the view of the round only while it flies, and 0 B per steady tick. The world is the production registries with the real catalogue; the
    /// muscle's half (the flight echo, the round) is played by hand here - it has its own rails (<c>FireControlRoundTests</c>) and the scenarios.
    /// </summary>
    public sealed class FireControlUnitTests
    {
        private const float Dt = 1f / 60f;

        private sealed class Rig : IDisposable
        {
            public readonly EntityRepository W = new();
            public readonly Entity Weapon, Device;
            public FireControlUnitBehavior.FireControlUnitParams P;
            public FireControlUnitBehavior.FireControlUnitWork Ws;
            public double Time;
            private readonly byte[] _presses = new byte[16];

            public Rig(int rounds = 1, bool deployed = true)
            {
                SimHostComponentRegistry.RegisterAll(W);
                CognitiveComponentRegistry.RegisterAll(W);
                var tkb = HrotEnvironment.CreateTkb();
                W.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
                BrainPhase.Prepare(W);

                Weapon = W.CreateEntity();
                W.AddComponent(Weapon, new SimTransform { Position = new Vector3(10f, 0f, 0f), Rotation = Quaternion.Identity });
                W.AddComponent(Weapon, new WeaponState { Ammo = rounds, MaxAmmo = 4, MagazineSize = 1, MagazineRounds = rounds > 0 ? 1 : 0 });
                W.AddComponent(Weapon, new WeaponSystemState { Deploy = deployed ? WeaponDeploy.Deployed : WeaponDeploy.Stowed });

                Device = W.CreateEntity();
                W.AddComponent(Device, new SimTransform { Position = new Vector3(10f, 0f, 0.7f), Rotation = Quaternion.Identity });
                W.AddComponent(Device, new DeviceInfo { DeviceType = TkbEntityTypes.Device_AtgmSight, Behavior = new FixedString64(BehaviorNames.DeviceFireControlUnit) });
                W.AddComponent(Device, new FireControl());      // as DeviceTkbTranslator creates a fire-control device
                W.AddComponent(Device, new GuidedFlight());
                W.AddComponent(Device, new IsEmbarkedTag { VehicleEntity = Weapon, Seat = 2 });
                var seats = new PassengerBuffer();
                seats.Passengers[2] = Device;
                seats.Count = 1;
                W.AddComponent(Weapon, seats);
                Input();
            }

            public FireControlUnitState St => UnitMemory.Get<FireControlUnitState>(W, Device);
            public FireControl Fc => W.GetComponentRO<FireControl>(Device);
            public GuidedFlight Gf => W.GetComponentRO<GuidedFlight>(Device);

            public void Press(int button) { _presses[button]++; Input(); }

            private void Input()
            {
                var c = new DeviceControl();
                for (int i = 0; i < 16; i++) c.Presses[i] = _presses[i];
                var input = new DeviceInput { Control = c };
                if (W.HasComponent<DeviceInput>(Device)) W.SetComponent(Device, input); else W.AddComponent(Device, input);
            }

            /// <summary>One frame of the device tick (a live frame, or a paused one with <paramref name="dt"/> 0).</summary>
            public void Frame(float dt = Dt)
            {
                Time += dt;
                W.SetSingleton(new GlobalTime { TotalTime = Time, DeltaTime = dt });
                if (FireControlUnitBehavior.Tick(ref P, ref Ws, Device, W) != Fbt.NodeStatus.Running) throw new InvalidOperationException("the unit ended");
            }

            public void Run(float seconds, float dt = Dt) { for (int i = 0, n = (int)MathF.Round(seconds / (dt > 0f ? dt : Dt)); i < n; i++) Frame(dt); }

            /// <summary>POWER and the self-test: STANDBY.</summary>
            public void Standby() { Run(0.1f); Press(FireControlUnitChannels.Power); Run(FireControlUnitBehavior.TestSeconds + 0.2f); }

            /// <summary>The muscle's half by hand: the weapon takes the shot, the round flies (D-95).</summary>
            public Entity Launched()
            {
                var round = W.CreateEntity();
                W.SetComponent(Device, new GuidedFlight { LaunchSeq = Fc.LaunchSeq, Round = round, Live = 1, State = GuidedFlightState.Line });
                W.GetComponentRW<WeaponState>(Weapon).Ammo--;
                return round;
            }

            public void Dispose() => W.Dispose();
        }

        /// <summary>
        /// D-92 / D-142 - the sight unit's TKB type names its behaviour and its elements in the wire order the behaviour reads, is a fire-control device with the self-test and optic the
        /// behaviour mirrors; the trainee's TOW carries it in a device station its Gunner seat operates (the AI TOW is untouched).
        /// </summary>
        [Fact]
        public void SDS4_TheSightType_NamesItsElementsInWireOrder_AndTheTowCarriesItInAStation()
        {
            var tkb = HrotEnvironment.CreateTkb();
            var d = tkb.GetByType(TkbEntityTypes.Device_AtgmSight)!.GetDescriptor<DeviceDefDto>()!;
            Assert.Equal(BehaviorNames.DeviceFireControlUnit, d.Behavior);
            Assert.True(d.FireControl);
            Assert.Equal(FireControlUnitBehavior.TestSeconds, d.TestSeconds);
            Assert.Equal(FireControlUnitBehavior.Magnification, d.Magnifications[0]);
            Assert.Equal(FireControlUnitChannels.Count, d.CountOf(DeviceElementKind.Button));
            Assert.Equal(FireControlUnitChannels.Power, d.ButtonOf("POWER"));
            Assert.Equal(FireControlUnitChannels.Arm, d.ButtonOf("ARM"));
            Assert.Equal(FireControlUnitChannels.Fire, d.ButtonOf("FIRE"));
            Assert.Equal(FireControlUnitChannels.View, d.ButtonOf("VIEW"));

            var tow = tkb.GetByType(TkbEntityTypes.WeaponSystem_TowTripodSight)!.GetDescriptor<CarrierDefDto>()!;
            Assert.Equal(Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, tow.Seats[0].Role);
            var station = tow.Seats[2];
            Assert.Equal(Fdp.Toolkit.Behavior.Components.SeatRole.Station, station.Role);
            Assert.Equal(MountKinds.Device, station.Accepts);
            Assert.Equal(0, station.OperatedBy);
            Assert.Equal(TkbEntityTypes.Device_AtgmSight, station.StartWith);
            var ai = tkb.GetByType(TkbEntityTypes.WeaponSystem_TowTripod)!.GetDescriptor<CarrierDefDto>()!;
            Assert.Equal(2, ai.Seats.Count);   // the AI TOW has no device station: no golden moves
        }

        /// <summary>
        /// D-93 - THE PROCEDURE: OFF => (POWER) TEST for the self-test's SIM seconds => STANDBY => (ARM) ARMED with READY => (FIRE) GUIDING with the launch seq moved and READY off => the
        /// flight ends => STANDBY "NO MISSILE" (the launcher is empty); FIRE before ARM and ARM before the test ends do nothing; POWER from any phase is OFF.
        /// Red-proof (compiles): the self-test skipped (POWER straight to STANDBY) => ARM during TEST acts => red.
        /// </summary>
        [Fact]
        public void SDS4_TheProcedure_OffTestStandbyArmedGuidingStandby()
        {
            using var r = new Rig(rounds: 1);
            r.Run(0.1f);
            Assert.Equal(FireControlPhase.Off, r.St.Phase);
            r.Press(FireControlUnitChannels.Fire); r.Press(FireControlUnitChannels.Arm); r.Run(0.1f);   // off: nothing
            Assert.Equal(FireControlPhase.Off, r.St.Phase);
            Assert.Equal(0u, r.Fc.LaunchSeq);

            r.Press(FireControlUnitChannels.Power); r.Run(0.1f);
            Assert.Equal(FireControlPhase.Test, r.St.Phase);
            r.Press(FireControlUnitChannels.Arm); r.Run(1.5f);                                           // the test is not over: ARM refused
            Assert.Equal(FireControlPhase.Test, r.St.Phase);
            r.Run(FireControlUnitBehavior.TestSeconds);
            Assert.Equal(FireControlPhase.Standby, r.St.Phase);
            Assert.Equal(0, r.Fc.Ready);
            Assert.Equal(1, r.St.Missiles);
            r.Press(FireControlUnitChannels.Fire); r.Run(0.2f);                                          // standby: FIRE refused
            Assert.Equal(FireControlPhase.Standby, r.St.Phase);
            Assert.Equal(0u, r.Fc.LaunchSeq);

            r.Press(FireControlUnitChannels.Arm); r.Run(0.1f);
            Assert.Equal(FireControlPhase.Armed, r.St.Phase);
            Assert.Equal(1, r.Fc.Ready);
            r.Press(FireControlUnitChannels.Fire); r.Frame();
            Assert.Equal(FireControlPhase.Guiding, r.St.Phase);
            Assert.Equal(1u, r.Fc.LaunchSeq);
            Assert.Equal(1, r.Fc.Ready);                           // the launch tick still says READY: the controller fires on the seq AND Ready (D-94)
            Assert.Equal(0, r.St.Ready);                           // the lamp is off: the unit is GUIDING
            var round = r.Launched();
            r.Run(1f);
            Assert.Equal(0, r.Fc.Ready);                           // and only that tick
            Assert.Equal(FireControlPhase.Guiding, r.St.Phase);
            Assert.Equal(1, r.St.Flying);
            r.W.DestroyEntity(round);
            r.W.GetComponentRW<GuidedFlight>(r.Device).Live = 0;   // the muscle's GuidedFlightSystem
            r.Run(0.1f);
            Assert.Equal(FireControlPhase.Standby, r.St.Phase);    // the launcher is empty: NO MISSILE
            Assert.Equal(0, r.St.Missiles);
            r.Press(FireControlUnitChannels.Arm); r.Run(0.1f);
            Assert.Equal(FireControlPhase.Standby, r.St.Phase);    // D-112: ARM needs a round

            r.Press(FireControlUnitChannels.Power); r.Run(0.1f);
            Assert.Equal(FireControlPhase.Off, r.St.Phase);
        }

        /// <summary>
        /// D-112 (user: "ARM should not work with empty launcher") - an EMPTY launcher stays in STANDBY whatever is pressed, a round taken out while ARMED drops the unit to STANDBY, and a
        /// loaded one arms again.
        /// Red-proof (compiles): the <c>loaded</c> gate on ARM removed => the empty launcher arms => red.
        /// </summary>
        [Fact]
        public void SDS4_ARM_NeedsARound_AndAnEmptiedLauncherDropsToStandby()
        {
            using var r = new Rig(rounds: 0);
            r.Standby();
            Assert.Equal(FireControlPhase.Standby, r.St.Phase);
            r.Press(FireControlUnitChannels.Arm); r.Run(0.2f);
            Assert.Equal(FireControlPhase.Standby, r.St.Phase);
            Assert.Equal(0, r.St.Missiles);
            r.Press(FireControlUnitChannels.Fire); r.Run(0.2f);
            Assert.Equal(0u, r.Fc.LaunchSeq);

            r.W.GetComponentRW<WeaponState>(r.Weapon).Ammo = 2;   // loaded: ARM acts - the press made while empty was NOT remembered
            r.Run(0.2f);
            Assert.Equal(FireControlPhase.Standby, r.St.Phase);
            r.Press(FireControlUnitChannels.Arm); r.Run(0.1f);
            Assert.Equal(FireControlPhase.Armed, r.St.Phase);
            r.W.GetComponentRW<WeaponState>(r.Weapon).Ammo = 0;   // a round taken out while armed
            r.Run(0.1f);
            Assert.Equal(FireControlPhase.Standby, r.St.Phase);
            Assert.Equal(0, r.Fc.Ready);
        }

        /// <summary>
        /// D-78 / D-83 / D-93 - PAUSE: POWER and ARM act (the device's own state) but the self-test stands still (it counts SIM time); READY is off and FIRE is REFUSED - never carried
        /// to the first live frame; the guide line is frozen (it follows the look only on live frames).
        /// Red-proof (compiles): the <c>live</c> term dropped from <c>ready</c> => FIRE launches in pause => red.
        /// </summary>
        [Fact]
        public void SDS4_Pause_ActsOnPowerAndArm_RefusesFire_AndFreezesTheTestAndTheLine()
        {
            using var r = new Rig(rounds: 2);
            r.Run(0.1f);
            r.Press(FireControlUnitChannels.Power); r.Run(0.5f);
            r.Run(5f, dt: 0f);                                                         // 5 paused seconds of frames: the test has not advanced
            Assert.Equal(FireControlPhase.Test, r.St.Phase);
            Assert.InRange(r.St.TestLeft, FireControlUnitBehavior.TestSeconds - 0.7f, FireControlUnitBehavior.TestSeconds - 0.4f);
            r.Run(FireControlUnitBehavior.TestSeconds);
            Assert.Equal(FireControlPhase.Standby, r.St.Phase);

            r.Run(0.2f, dt: 0f);
            r.Press(FireControlUnitChannels.Arm); r.Run(0.2f, dt: 0f);                  // ARM acts in pause
            Assert.Equal(FireControlPhase.Armed, r.St.Phase);
            Assert.Equal(0, r.Fc.Ready);                                               // ... but READY is off
            var line = r.Fc.GuideDir;
            r.Press(FireControlUnitChannels.Fire); r.Run(0.2f, dt: 0f);                 // FIRE in pause: refused
            Assert.Equal(FireControlPhase.Armed, r.St.Phase);
            Assert.Equal(0u, r.Fc.LaunchSeq);
            r.W.SetComponent(r.Device, new SimTransform { Position = new Vector3(10f, 0f, 0.7f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1f) });
            r.Run(0.2f, dt: 0f);
            Assert.Equal(line, r.Fc.GuideDir);                                         // the command does not move in pause
            r.Run(0.2f);                                                               // resume: READY, and the refused press is gone
            Assert.Equal(FireControlPhase.Armed, r.St.Phase);
            Assert.Equal(0u, r.Fc.LaunchSeq);
            Assert.Equal(1, r.Fc.Ready);
            Assert.NotEqual(line, r.Fc.GuideDir);                                      // live again: it follows the look
        }

        /// <summary>
        /// D-96 - the guide line is the device's WORLD pose: from its place (the station's, the optic's), along where it points; written only on live frames; none once it is off (the
        /// round then flies on unguided).
        /// Red-proof (compiles): the guide written from the identity pose => the turned device's line is wrong => red.
        /// </summary>
        [Fact]
        public void SDS4_TheGuideLine_IsTheDevicesWorldPose_AndNoneWhenOff()
        {
            using var r = new Rig(rounds: 1);
            r.W.SetComponent(r.Device, new SimTransform { Position = new Vector3(10f, 5f, 0.7f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f) });
            r.Standby();
            Assert.Equal(new Vector3(10f, 5f, 0.7f).X, r.Fc.GuideOrigin.X, 3);
            Assert.Equal(5f, r.Fc.GuideOrigin.Y, 3);
            Assert.True(Vector3.Distance(r.Fc.GuideDir, Vector3.UnitY) < 1e-3f, $"north: {r.Fc.GuideDir}");
            r.Press(FireControlUnitChannels.Power); r.Run(0.1f);
            Assert.Equal(FireControlPhase.Off, r.St.Phase);
            Assert.Equal(Vector3.Zero, r.Fc.GuideDir);
        }

        /// <summary>
        /// D-178 - VIEW toggles the round's picture ONLY while it flies; it asks the station's <see cref="DeviceView"/> for the round in the chase mode; the flight's end gives the
        /// picture back by itself.
        /// Red-proof (compiles): the <c>flightLive</c> gate on the VIEW press removed => VIEW before launch sets it => red.
        /// </summary>
        [Fact]
        public void SDS4_View_ToggleOnlyWhileTheRoundFlies_AndEndsWithTheFlight()
        {
            using var r = new Rig(rounds: 2);
            r.Standby();
            r.Press(FireControlUnitChannels.Arm); r.Run(0.1f);
            r.Press(FireControlUnitChannels.View); r.Run(0.1f);                          // nothing flies: refused
            Assert.Equal(0, r.Fc.ViewFromRound);
            r.Press(FireControlUnitChannels.Fire); r.Frame();
            var round = r.Launched();
            r.Run(0.2f);
            r.Press(FireControlUnitChannels.View); r.Run(0.1f);
            Assert.Equal(1, r.Fc.ViewFromRound);
            var view = r.W.GetComponentRO<DeviceView>(r.Device);
            Assert.Equal(round, view.Entity);
            Assert.Equal(DeviceViewMode.Chase, view.Mode);
            r.Press(FireControlUnitChannels.View); r.Run(0.1f);                          // and back
            Assert.Equal(0, r.Fc.ViewFromRound);
            Assert.True(r.W.GetComponentRO<DeviceView>(r.Device).Entity.IsNull);
            r.Press(FireControlUnitChannels.View); r.Run(0.1f);
            Assert.Equal(1, r.Fc.ViewFromRound);
            r.W.DestroyEntity(round);
            r.W.GetComponentRW<GuidedFlight>(r.Device).Live = 0;
            r.Run(0.1f);
            Assert.Equal(0, r.Fc.ViewFromRound);                                         // the flight ended: the sight again, without a press
            Assert.Equal(FireControlPhase.Armed, r.St.Phase);                            // a second round is left
        }

        /// <summary>
        /// D-112 / R-4300 - a FIRE the weapon never took (no controller, a refusal) is given up after <see cref="FireControlUnitBehavior.LaunchGraceSeconds"/> of sim time: the unit
        /// returns to ARMED, never GUIDING for ever.
        /// </summary>
        [Fact]
        public void SDS4_AShotTheWeaponNeverTook_IsGivenUp()
        {
            using var r = new Rig(rounds: 2);
            r.Standby();
            r.Press(FireControlUnitChannels.Arm); r.Run(0.1f);
            r.Press(FireControlUnitChannels.Fire); r.Run(1f);
            Assert.Equal(FireControlPhase.Guiding, r.St.Phase);
            r.Run((float)FireControlUnitBehavior.LaunchGraceSeconds);
            Assert.Equal(FireControlPhase.Armed, r.St.Phase);
        }

        /// <summary>R-2084 - a steady tick of the unit (armed, then in flight) allocates nothing.</summary>
        [Fact]
        public void SDS4_ASteadyTick_AllocatesNothing()
        {
            using var r = new Rig(rounds: 2);
            r.Standby();
            r.Press(FireControlUnitChannels.Arm); r.Run(0.2f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            r.Run(1f);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            r.Press(FireControlUnitChannels.Fire); r.Frame(); r.Launched(); r.Run(0.2f);
            before = GC.GetAllocatedBytesForCurrentThread();
            r.Run(1f);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
