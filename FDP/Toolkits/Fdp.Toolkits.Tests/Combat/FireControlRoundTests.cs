using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// Simulated devices SD-S4 (docs/designs/devices/DESIGN_Simulated_Devices.md sec 3.13b D-85 / D-88 / D-95 / D-96 / D-99, sec 3.13g D-177) - the MUSCLE's half of the fire-control unit:
    /// the one resolver of a weapon's device (<see cref="FireControls.Of"/>), the launch that is the device's BY SEQ ECHO (<see cref="FireControls.ClaimLaunch"/>), the command round that
    /// flies the DEVICE's sight line (<see cref="GuidedRoundSystem"/>: not the gunner's eye onto the target; lost - NO LOCK - when the device is off or the line to the round is blocked),
    /// and the flight's end (<see cref="GuidedFlightSystem"/>). The device's own procedure is <c>FireControlUnitTests</c> (Hrot.SimHost.Tests).
    /// </summary>
    public sealed class FireControlRoundTests : IDisposable
    {
        private const float Dt = 1f / 60f;
        private readonly EntityRepository _w = new();
        private readonly GuidedRoundSystem _sys = new();

        public FireControlRoundTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<BallisticProjectile>();
            _w.RegisterComponent<GuidedRound>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterComponent<FireControl>();
            _w.RegisterComponent<GuidedFlight>();
            _w.RegisterComponent<PassengerBuffer>();
            _w.RegisterEvent<Fdp.Toolkit.Combat.Contracts.DetonationNotification>();
        }

        public void Dispose() => _w.Dispose();

        private Entity Unit(Vector3 at)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            return e;
        }

        private Entity Device(Vector3 origin, Vector3 dir, uint launchSeq = 0)
        {
            var d = _w.CreateEntity();
            _w.AddComponent(d, new FireControl { GuideOrigin = origin, GuideDir = dir, LaunchSeq = launchSeq });
            _w.AddComponent(d, new GuidedFlight());
            return d;
        }

        /// <summary>A weapon whose station seat 2 holds <paramref name="device"/> (what <c>DeviceTkbTranslator</c> + the boarding make).</summary>
        private Entity Weapon(Entity device)
        {
            var weapon = Unit(Vector3.Zero);
            var seats = new PassengerBuffer();
            seats.Passengers[2] = device;
            seats.Count = 1;
            _w.AddComponent(weapon, seats);
            return weapon;
        }

        private Entity Round(Vector3 at, Vector3 velocity, GuidedRound g)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity { Linear = velocity });
            _w.AddComponent(e, new BallisticProjectile { PreviousPosition = at });
            g.SpeedMps = velocity.Length();
            _w.AddComponent(e, g);
            return e;
        }

        private void Step(Entity round)
        {
            _sys.Execute(_w, Dt);
            ref var tf = ref _w.GetComponentRW<SimTransform>(round);
            tf.Position += _w.GetComponentRO<SimVelocity>(round).Linear * Dt;
        }

        /// <summary>
        /// D-96 - a COMMAND round launched through a device flies the DEVICE's sight line, never the target's: it starts 20 m off the line, closes onto it and stays, while the
        /// target stands 90 m off it (the AI line would have bent the round onto the target).
        /// Red-proof (compiles): the device branch in <c>GuidedRoundSystem.Steer</c> removed => the round flies eye -> the target's middle => ends ~90 m off the device's line => red.
        /// </summary>
        [Fact]
        public void SDS4_ACommandRoundWithADevice_FliesTheDevicesSightLine_NotTheTargets()
        {
            var gunner = Unit(Vector3.Zero);
            var target = Unit(new Vector3(600, 90, 0));
            var device = Device(new Vector3(0, 0, 1.6f), Vector3.UnitX);
            var round = Round(new Vector3(5, 20, 1.6f), new Vector3(200, 0, 0),
                new GuidedRound { Guidance = GuidanceKind.Command, Target = target, Gunner = gunner, Device = device });
            for (int f = 0; f < 150; f++) Step(round);   // 2.5 s
            var pos = _w.GetComponentRO<SimTransform>(round).Position;
            Assert.InRange(MathF.Abs(pos.Y), 0f, 2f);
            Assert.True(pos.X > 400f, $"flew on down the range, x {pos.X}");
            Assert.Equal(GuidedPhase.Cruise, _w.GetComponentRO<GuidedRound>(round).Phase);

            // the operator swings the sight 5 degrees left: the round follows the new line (SACLOS by hand)
            _w.SetComponent(device, new FireControl { GuideOrigin = new Vector3(0, 0, 1.6f), GuideDir = Vector3.Normalize(new Vector3(MathF.Cos(0.0873f), MathF.Sin(0.0873f), 0f)) });
            for (int f = 0; f < 120; f++) Step(round);
            pos = _w.GetComponentRO<SimTransform>(round).Position;
            float offLine = MathF.Abs(pos.Y - pos.X * MathF.Tan(0.0873f));
            Assert.True(offLine < 3f, $"{offLine:0.0} m off the swung line at {pos}");
        }

        /// <summary>
        /// D-177 - NO LOCK: a device round whose device has no sight line (POWER off writes none) or whose sight line to the ROUND is blocked is lost - the round flies on straight and
        /// the device's flight says <see cref="GuidedFlightState.NoLock"/>.
        /// Red-proof (compiles): <c>MarkFlight</c> dropped from the device branch => the state stays Line => red.
        /// </summary>
        [Fact]
        public void SDS4_ADeviceRound_IsLost_WhenTheDeviceIsOffOrTheSightToTheRoundIsBlocked()
        {
            var gunner = Unit(Vector3.Zero);
            var device = Device(new Vector3(0, 0, 1.6f), Vector3.UnitX);
            _w.SetComponent(device, new GuidedFlight { LaunchSeq = 1, Live = 1, State = GuidedFlightState.Line });
            var round = Round(new Vector3(5, 0, 1.6f), new Vector3(200, 0, 0), new GuidedRound { Guidance = GuidanceKind.Command, Gunner = gunner, Device = device });
            for (int f = 0; f < 30; f++) Step(round);
            Assert.Equal(GuidedPhase.Cruise, _w.GetComponentRO<GuidedRound>(round).Phase);
            Assert.Equal(GuidedFlightState.Line, _w.GetComponentRO<GuidedFlight>(device).State);

            _w.SetComponent(device, new FireControl());   // switched off: no line
            Step(round);
            Assert.Equal(GuidedPhase.Lost, _w.GetComponentRO<GuidedRound>(round).Phase);
            Assert.Equal(GuidedFlightState.NoLock, _w.GetComponentRO<GuidedFlight>(device).State);
            Assert.Equal(new Vector3(200, 0, 0), _w.GetComponentRO<SimVelocity>(round).Linear);   // flies on straight

            // a wall between the device and the round
            var device2 = Device(new Vector3(0, 0, 1.6f), Vector3.UnitX);
            _w.SetComponent(device2, new GuidedFlight { LaunchSeq = 1, Live = 1, State = GuidedFlightState.Line });
            var blind = Round(new Vector3(5, 0, 1.6f), new Vector3(200, 0, 0), new GuidedRound { Guidance = GuidanceKind.Command, Gunner = gunner, Device = device2 });
            _w.SetSingletonManaged(TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "wall", "height": 6, "thickness": 0.5 }, "geometry": { "type": "LineString", "coordinates": [[100,-200],[100,300]] } } ] }
            """));
            for (int f = 0; f < 60; f++) Step(blind);
            Assert.Equal(GuidedPhase.Lost, _w.GetComponentRO<GuidedRound>(blind).Phase);
            Assert.Equal(GuidedFlightState.NoLock, _w.GetComponentRO<GuidedFlight>(device2).State);
        }

        /// <summary>
        /// D-95 - THE LAUNCH IS THE DEVICE'S BY SEQ ECHO: a weapon's device (in its station seat) whose <c>LaunchSeq</c> is past the flight it last echoed claims the round just born
        /// - the flight is written on the device (the echo, the round, live, how it flies) - once; an AI shot (no seq moved) and a weapon with no device claim nothing and fly today's line.
        /// Red-proof (compiles): the seq comparison dropped (always claim) => the second shot claims too => red.
        /// </summary>
        [Fact]
        public void SDS4_TheLaunch_IsTheDevices_BySeqEcho_OnceAndOnlyForItsDevice()
        {
            var device = Device(new Vector3(0, 0, 1.6f), Vector3.UnitX);
            var weapon = Weapon(device);
            Assert.Equal(device, FireControls.Of(_w, weapon));
            Assert.True(FireControls.Of(_w, Unit(Vector3.Zero)).IsNull);   // a weapon with no device
            var r1 = _w.CreateEntity(); var r2 = _w.CreateEntity(); var r3 = _w.CreateEntity();

            Assert.True(FireControls.ClaimLaunch(_w, weapon, r1, GuidanceKind.Command).IsNull, "an AI shot: no seq moved");
            _w.GetComponentRW<FireControl>(device).LaunchSeq = 1;
            Assert.Equal(device, FireControls.ClaimLaunch(_w, weapon, r2, GuidanceKind.Command));
            var flight = _w.GetComponentRO<GuidedFlight>(device);
            Assert.Equal(1u, flight.LaunchSeq);
            Assert.Equal(r2, flight.Round);
            Assert.Equal(1, flight.Live);
            Assert.Equal(GuidedFlightState.Line, flight.State);
            Assert.True(FireControls.ClaimLaunch(_w, weapon, r3, GuidanceKind.Command).IsNull, "claimed once: the next shot is an AI's");
            Assert.True(FireControls.ClaimLaunch(_w, Unit(Vector3.Zero), r3, GuidanceKind.Command).IsNull);
        }

        /// <summary>
        /// D-99 - the flight ends when its round is gone (a hit, the lifetime): <see cref="GuidedFlight.Live"/> goes to 0 and the state to Ended; a live round leaves it alone; steady
        /// frames allocate nothing.
        /// Red-proof (compiles): <c>GuidedFlightSystem</c> not registered / its alive test inverted => Live never clears => red.
        /// </summary>
        [Fact]
        public void SDS4_TheFlight_EndsWhenItsRoundIsGone_AndAllocatesNothing()
        {
            var device = Device(Vector3.Zero, Vector3.UnitX);
            var round = _w.CreateEntity();
            _w.SetComponent(device, new GuidedFlight { LaunchSeq = 1, Round = round, Live = 1, State = GuidedFlightState.Line });
            var sys = new GuidedFlightSystem();
            for (int i = 0; i < 20; i++) sys.Execute(_w, Dt);
            Assert.Equal(1, _w.GetComponentRO<GuidedFlight>(device).Live);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) sys.Execute(_w, Dt);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            _w.DestroyEntity(round);
            sys.Execute(_w, Dt);
            var flight = _w.GetComponentRO<GuidedFlight>(device);
            Assert.Equal(0, flight.Live);
            Assert.Equal(GuidedFlightState.Ended, flight.State);
        }
    }
}
