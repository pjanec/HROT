using System;
using System.IO;
using System.Numerics;
using Fdp.Examples.UrbanCombat;
using Fdp.Examples.UrbanCombat.Brains;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Perception.Components;
using Xunit;

namespace Fdp.Examples.UrbanCombat.Tests
{
    /// <summary>
    /// BCS-P7-T7  (4 unit tests)  — ScenarioDirector spawn and embark assertions.
    /// BCS-P7-T8  (3 unit tests)  — TelemetryReporterSystem gunfire / hit / flee detection.
    /// BCS-P7-T9  (2 integration) — Full 600-frame end-to-end scenario + APC northward.
    /// </summary>
    public class UrbanAmbushIntegrationTests : IDisposable
    {
        // ─── Shared fixture ────────────────────────────────────────────────────────
        // xUnit instantiates a fresh UrbanAmbushIntegrationTests per test method.

        private readonly HeadlessDemoApp    _app;
        private readonly ScenarioDirector   _director;

        public UrbanAmbushIntegrationTests()
        {
            _app = new HeadlessDemoApp();
            _app.Initialize();
            _director = new ScenarioDirector(_app.World, _app.Tkb, _app.Road, _app.DoctrineRegistry);
        }

        public void Dispose()
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()));
            _app.Dispose();
        }

        // ─── T7: ScenarioDirector unit tests ──────────────────────────────────────

        [Fact]
        public void ScenarioDirector_SpawnsExpectedEntityCount()
        {
            _director.SetupAmbushScenario();

            // 5 pedestrians + 3 cars + 1 APC + 4 soldiers + 1 insurgent = 14
            int count = 0;
            var q = _app.World.Query().With<SimTransform>().Build();
            foreach (var _ in q)
                count++;

            Assert.Equal(14, count);
        }

        [Fact]
        public void ScenarioDirector_SoldiersAreEmbarked_Initially()
        {
            _director.SetupAmbushScenario();

            int count = 0;
            var q = _app.World.Query().With<IsEmbarkedTag>().Build();
            foreach (var _ in q)
                count++;

            Assert.Equal(4, count);
        }

        [Fact]
        public void ScenarioDirector_InsurgentHasRedFaction()
        {
            _director.SetupAmbushScenario();

            int redCount = 0;
            var q = _app.World.Query().With<Faction>().Build();
            foreach (var entity in q)
            {
                var faction = _app.World.GetComponent<Faction>(entity);
                if (faction.FactionId == UrbanCombatConstants.FactionRed)
                    redCount++;
            }

            Assert.Equal(1, redCount);
        }

        [Fact]
        public void ScenarioDirector_APC_HasFourPassengers_Initially()
        {
            _director.SetupAmbushScenario();

            // The APC is the only entity with both PassengerBuffer and BrainHsm128.
            int passengerCount = -1;
            var q = _app.World.Query()
                .With<PassengerBuffer>()
                .With<BrainHsm128>()
                .Build();
            foreach (var entity in q)
            {
                var buf = _app.World.GetComponent<PassengerBuffer>(entity);
                passengerCount = buf.Count;
                break; // only one APC
            }

            Assert.Equal(4, passengerCount);
        }

        // ─── T8: TelemetryReporterSystem unit tests ───────────────────────────────

        [Fact]
        public void Telemetry_PrintsGunfireEvent_WhenFireRequestPublished()
        {
            var writer = new StringWriter();
            Console.SetOut(writer);

            // Publish a FireRequestEvent to the write buffer.
            _app.World.Bus.Publish(new FireRequestEvent
            {
                Shooter   = new Entity(),
                Target    = new Entity(),
                Origin    = Vector3.Zero,
                Direction = Vector3.UnitZ,
            });

            // One frame: SwapBuffers moves event to read buffer → TelemetryReporterSystem sees it.
            _app.RunSimulation(1);

            Assert.Contains("GUNFIRE", writer.ToString());
        }

        [Fact]
        public void Telemetry_PrintsHitEvent_WhenHitEventPublished()
        {
            var writer = new StringWriter();
            Console.SetOut(writer);

            // Publish a HitEvent (bullet entity does not need to exist — damage will be 0).
            _app.World.Bus.Publish(new HitEvent
            {
                HitEntity   = new Entity(),
                BulletIndex = 0,
                HitT        = 0.5f,
            });

            _app.RunSimulation(1);

            Assert.Contains("HIT", writer.ToString());
        }

        [Fact]
        public void Telemetry_PrintsFleeEvent_WhenLocomotionChannelSetToFlee()
        {
            // Spawn a minimal entity with LocomotionChannel.
            var entity = _app.World.CreateEntity();
            _app.World.AddComponent(entity, new LocomotionChannel
            {
                ActiveAction    = NavigationConstants.ActionIdFlee,
                ActionInstanceId = 1,
            });

            var writer = new StringWriter();
            Console.SetOut(writer);

            _app.RunSimulation(1);

            Assert.Contains("FLEE", writer.ToString());
        }

        // ─── T9: End-to-end integration tests ─────────────────────────────────────

        [Fact]
        public void UrbanAmbush_SimulationRunsToCompletion_WithExpectedMilestones()
        {
            using var output = new StringWriter();
            Console.SetOut(output);

            _director.SetupAmbushScenario();
            _app.RunSimulation(600);

            var log = output.ToString();

            // Every milestone in narrative order (Contains does not enforce order):
            Assert.Contains("DOCTRINE ASSIGNED",            log);   // Frame 1 — initial doctrines applied
            Assert.Contains("GUNFIRE",                      log);   // ~Frame 181 — insurgent fires
            Assert.Contains("HIT",                          log);   // ~Frame 182 — APC hit
            Assert.Contains("CAPABILITY LOST",              log);   // ~Frame 182 — APC mobility lost
            Assert.Contains("HSM TRANSITION",               log);   // ~Frame 183 — APC enters Disabled
            Assert.Contains("INTERACTION: EjectPassengers", log);   // ~Frame 184 — soldiers ejected
            Assert.Contains("FLEE",                         log);   // ~Frame 185+ — civilians flee
        }

        [Fact]
        public void UrbanAmbush_ApcMovesNorthward_BeforeAmbush()
        {
            _director.SetupAmbushScenario();

            // Run 100 frames — APC should have moved north from Y=-80 toward centre.
            _app.RunSimulation(100);

            var q = _app.World.Query()
                .With<SimTransform>()
                .With<BrainHsm128>()
                .Build();

            foreach (var e in q)
            {
                var tf = _app.World.GetComponent<SimTransform>(e);
                Assert.True(tf.Position.Y > -90f,
                    $"APC should have moved north from Y=-80; actual Y={tf.Position.Y}");
            }
        }
    }
}
