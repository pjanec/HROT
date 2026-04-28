using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Modules;
using Fdp.Toolkit.Perception.Systems;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// Integration tests proving that the networkless (Editor-style) perception pipeline
    /// fills <see cref="TargetMemory"/> via the internal <see cref="SensorTrackStateEvent"/>
    /// without any DDS transport layer.
    ///
    /// <para>
    /// This mirrors the production Editor flow:
    /// <list type="number">
    ///   <item><see cref="AutonomousPerceptionModule.Tick"/> runs the vision + LOS pipeline and
    ///     bridges <see cref="SensorTrackStateEvent"/> to the global world bus.</item>
    ///   <item><see cref="ActiveSensorTracksUpdateSystem"/> consumes the event and populates
    ///     <see cref="ActiveSensorTracks"/> on the observer.</item>
    ///   <item><see cref="ThreatEvaluationSystem"/> reads <see cref="ActiveSensorTracks"/> and
    ///     boosts <see cref="TargetMemory"/> scores.</item>
    /// </list>
    /// No network, no DDS, no manual injection of SensorContactList.
    /// </para>
    /// </summary>
    public class NetworklessPerceptionIntegrationTests
    {
        // Minimal registry that returns every system as-is.
        private sealed class PassthroughRegistry : ISystemRegistry
        {
            public void RegisterSystem<T>(T system) where T : IEcsModuleSystem { }
            public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem => system;
        }

        private static void FlushEcbAndSwap(ISimulationView view, EntityRepository world)
        {
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(world);
            world.Bus.SwapBuffers();
        }

        /// <summary>
        /// Verifies the full networkless perception pipeline end-to-end:
        /// AutonomousPerceptionModule (vision + LOS + debounce + bridge) ->
        /// SensorTrackStateEvent on global bus ->
        /// ActiveSensorTracksUpdateSystem -> ActiveSensorTracks ->
        /// ThreatEvaluationSystem -> TargetMemory populated with positive score.
        ///
        /// This is exactly how the Editor should work after the refactor.
        /// </summary>
        [Fact]
        public unsafe void NetworklessPerception_TargetInFov_FillsTargetMemoryViaSensorTrackStateEvent()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            ISimulationView view = world;

            using var module = new AutonomousPerceptionModule(
                colliderRadiusReader: null); // no physics colliders needed for this test
            module.RegisterSystems(new PassthroughRegistry());

            var activeSys = new ActiveSensorTracksUpdateSystem();
            var threatSys = new ThreatEvaluationSystem();

            // Observer -- facing east (Identity rotation = yaw 0 -> east), enemy in front.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(observer, new EntityInfo { ForceId = ForceId.Friend });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 4f), // 90 degree total FOV
            });
            world.AddComponent(observer, new TargetMemory());

            // Target -- directly east, inside FOV and vision range.
            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(50f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new EntityInfo { ForceId = ForceId.Hostile });

            // Act: run one perception tick.
            // dt > 0 so the module does not bail out early.
            const float dt = 1.0f / 10.0f;
            module.Tick(view, dt);

            // Flush ECB so SensorContactList writes and bridged SensorTrackStateEvents land
            // on the world; swap bus so they become readable in the next pass.
            FlushEcbAndSwap(view, world);

            // Assert: SensorTrackStateEvent must be on the world bus (bridged from scoped bus).
            var trackEvents = world.Bus.Read<SensorTrackStateEvent>();
            Assert.False(trackEvents.IsEmpty,
                "AutonomousPerceptionModule must bridge at least one SensorTrackStateEvent " +
                "to the global world bus when a hostile target is in the observer's FOV.");

            bool acquiredEventFound = false;
            foreach (ref readonly var evt in trackEvents)
            {
                if (evt.Observer == observer &&
                    evt.Target   == target   &&
                    evt.State    == SensorTrackStatus.Acquired)
                {
                    acquiredEventFound = true;
                    break;
                }
            }
            Assert.True(acquiredEventFound,
                "A SensorTrackStateEvent(Acquired) for the hostile target must appear on the " +
                "global world bus after AutonomousPerceptionModule.Tick.");

            // Step 2: run ActiveSensorTracksUpdateSystem -- it consumes the event and writes ActiveSensorTracks.
            activeSys.Execute(view, dt);
            FlushEcbAndSwap(view, world);

            Assert.True(world.HasComponent<ActiveSensorTracks>(observer),
                "ActiveSensorTracksUpdateSystem must add ActiveSensorTracks to the observer " +
                "after consuming a SensorTrackStateEvent(Acquired).");

            var tracks = world.GetComponent<ActiveSensorTracks>(observer);
            Assert.True(tracks.Count > 0,
                "ActiveSensorTracks must contain at least one entry after the Acquired event.");

            // Step 3: run ThreatEvaluationSystem -- it reads ActiveSensorTracks and boosts TargetMemory.
            threatSys.Execute(view, dt);
            FlushEcbAndSwap(view, world);

            Assert.True(world.HasComponent<TargetMemory>(observer),
                "Observer must still have TargetMemory after ThreatEvaluationSystem runs.");

            var mem = world.GetComponent<TargetMemory>(observer);
            Assert.True(mem.Count > 0,
                "TargetMemory must have at least one entry after ThreatEvaluationSystem boosts " +
                "from ActiveSensorTracks in the networkless Editor setup.");
            Assert.True(mem.ThreatScores[0] > 0f,
                "ThreatMemory[0].ThreatScore must be positive after ThreatEvaluationSystem " +
                "applies a 50 * deltaTime boost from the ActiveSensorTracks buffer.");
        }

        /// <summary>
        /// Verifies that when simulation time is frozen (dt == 0) the perception module
        /// skips execution and no SensorTrackStateEvent is published on the global bus.
        /// This matches the Editor's behaviour during paused (non-preview) time.
        /// </summary>
        [Fact]
        public unsafe void NetworklessPerception_FrozenTime_DoesNotPopulateTargetMemory()
        {
            var world = PerceptionTestWorldFactory.Create();
            ISimulationView view = world;

            using var module = new AutonomousPerceptionModule();
            module.RegisterSystems(new PassthroughRegistry());

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            world.AddComponent(observer, new EntityInfo { ForceId = ForceId.Friend });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 4f),
            });
            world.AddComponent(observer, new TargetMemory());

            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform { Position = new Vector3(50f, 0f, 0f), Rotation = Quaternion.Identity });
            world.AddComponent(target, new EntityInfo { ForceId = ForceId.Hostile });

            // Act: dt == 0 simulates frozen simulation time (Editor pause).
            module.Tick(view, 0f);
            FlushEcbAndSwap(view, world);

            // Assert: no events should have been published.
            var trackEvents = world.Bus.Read<SensorTrackStateEvent>();
            Assert.True(trackEvents.IsEmpty,
                "When dt == 0 (simulation frozen), AutonomousPerceptionModule must skip execution " +
                "and not publish any SensorTrackStateEvent.");
        }
    }
}
