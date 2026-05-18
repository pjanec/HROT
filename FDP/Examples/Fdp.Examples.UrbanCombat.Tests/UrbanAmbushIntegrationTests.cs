using System;
using System.IO;
using System.Numerics;
using Fdp.Examples.UrbanCombat;
using Fdp.Examples.UrbanCombat.Brains;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Navigation;
using Xunit;

namespace Fdp.Examples.UrbanCombat.Tests
{
    /// <summary>
    /// BCS-P7-T7  (4 unit tests)  — ScenarioDirector spawn and embark assertions.
    /// BCS-P7-T8  (3 unit tests)  — TelemetryReporterSystem gunfire / hit / flee detection.
    /// BCS-P7-T9  (2 integration) — Full 600-frame end-to-end scenario + APC northward.
    /// </summary>
    /// <remarks>
    /// Serialised with <see cref="SerialTestsCollection"/> to prevent parallel execution
    /// against other <see cref="HeadlessDemoApp"/>-based classes (<c>BlueprintTests</c>,
    /// <c>ApcBrainTests</c>).  Concurrent <c>HeadlessDemoApp.Initialize()</c> calls race on
    /// the global component-type registry and cause intermittent entity-count mismatches.
    /// </remarks>
    [Collection("SerialTests")]
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
            _director = new ScenarioDirector(
                _app.World, _app.Tkb, _app.Road, _app.BehaviorRegistry,
                entityMap: _app.EntityMap);
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
            var q = _app.World.Query().With<EntityInfo>().Build();
            foreach (var entity in q)
            {
                var info = _app.World.GetComponent<EntityInfo>(entity);
                if (info.ForceId == ForceId.Hostile)
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
        public void Telemetry_PrintsGunfireEvent_WhenWeaponFireIntentPublished()
        {
            var writer = new StringWriter();
            Console.SetOut(writer);

            // Publish a WeaponFireIntent (BS1-T004: AimAndFireExecutor now emits this instead
            // of FireRequestEvent). TelemetryReporterSystem consumes it for GUNFIRE logging.
            // PACK-P003: WeaponFireIntent carries local ECS Entity handles.
            var dummyShooter = _app.World.CreateEntity();
            var dummyTarget  = _app.World.CreateEntity();
            _app.World.Bus.Publish(new WeaponFireIntent
            {
                Shooter     = dummyShooter,
                Target      = dummyTarget,
                WeaponIndex = 0,
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
                HitEntity    = new Entity(),
                BulletEntity = new Entity(),
                HitT         = 0.5f,
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

            // ── Phase 1 milestones (T001–T004) ────────────────────────────────
            Assert.Contains("BEHAVIOR ASSIGNED", log);   // Frame 1 — initial behaviors applied
            Assert.Contains("GUNFIRE",           log);   // ~Frame 181 — insurgent fires (WeaponFireIntent)

            // ── Bullet-dependent milestones restored after BS1-T007 ───────────
            // FireProcessingSystem now consumes WeaponFireIntent and spawns bullet entities,
            // which unblocks the full hit→damage→capability-lost→HSM-transition chain.
            Assert.Contains("HIT",               log);   // Bullet hits APC
            Assert.Contains("CAPABILITY LOST",   log);   // APC loses capability after damage
            Assert.Contains("HSM TRANSITION",    log);   // APC HSM transitions (e.g. to Damaged state)
            Assert.Contains("INTERACTION",       log);   // EjectPassengers triggered
            Assert.Contains("FLEE",             log);   // Civilian perceives threat and flees
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
