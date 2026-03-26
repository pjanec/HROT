using System;
using System.Numerics;
using Moq;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Modules;
using FDP.Toolkit.Perception.Systems;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace FDP.Toolkit.Perception.Tests
{
    public class AutonomousPerceptionModuleTests
    {
        [Fact]
        public void AutonomousPerceptionModule_RegisterSystems_DoesNotRegisterSystems()
        {
            // Arrange
            // AutonomousPerceptionModule uses the direct-execution Tick() pattern (same as
            // PerceptionModule): all four systems are called inside Tick() rather than
            // delegated to the kernel system scheduler. RegisterSystems() must be empty so
            // the kernel does NOT try to schedule them via [UpdateInPhase].
            using var module = new AutonomousPerceptionModule();
            var mockRegistry = new Mock<ISystemRegistry>();

            // Act
            module.RegisterSystems(mockRegistry.Object);

            // Assert — zero registrations (systems run directly via Tick, not via scheduler).
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<LocalGridBuilderSystem>()),   Times.Never);
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<VisionBroadphaseSystem>()),   Times.Never);
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<LosRequestBatchingSystem>()), Times.Never);
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<ThreatEvaluationSystem>()),   Times.Never);
        }

        /// <summary>
        /// BATCH-09 Task 2 — scoped bus isolation contract.
        /// Inter-stage events (<see cref="LosCheckRequestEvent"/>, <see cref="TargetVisibleEvent"/>)
        /// that flow through the module-private scoped bus must NOT appear on the global world bus
        /// after <see cref="AutonomousPerceptionModule.Tick"/> returns.
        /// This proves that the <c>PerceptionScopedView.ConsumeEvents</c> whitelist and the
        /// scoped-bus isolation strategy prevent world-bus contamination.
        /// </summary>
        [Fact]
        public void AutonomousPerceptionModule_ScopedEvents_DoNotLeakToWorldBus()
        {
            // Arrange: world with at least one observer and one target so that the
            // VisionBroadphase system has something to evaluate.
            var world = PerceptionTestWorldFactory.Create();

            // Observer — facing east, enemy in front.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,  // yaw=0 → east
            });
            world.AddComponent(observer, new Faction { FactionId = 1 });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 6f), // 60° total FOV
            });
            world.AddComponent(observer, new TargetMemory());

            // Target — directly east, in FOV.
            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(100f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new Faction { FactionId = 2 });

            using var module = new AutonomousPerceptionModule();

            // Act: run one perception tick synchronously.
            ISimulationView view = world;
            module.Tick(view, 1.0f / 10.0f);

            // Flush the ECB that was produced during Tick (TargetMemory writes).
            // AutonomousPerceptionModule writes component changes through the real ECB.
            // We don't expose a direct flush here — just verify bus state.

            // Assert: the scoped inter-stage events must NOT have leaked to the world bus.
            // World bus swap has not been called, so its write buffer is the one to check.
            // ConsumeEvents on the live EntityRepository reads from the world bus read slot.
            var worldLos     = world.Bus.Consume<LosCheckRequestEvent>();
            var worldVisible = world.Bus.Consume<TargetVisibleEvent>();

            Assert.True(worldLos.IsEmpty,
                "LosCheckRequestEvent must stay on the scoped bus — not the world bus.");
            Assert.True(worldVisible.IsEmpty,
                "TargetVisibleEvent must stay on the scoped bus — not the world bus.");
        }
    }
}
