using System;
using System.Numerics;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Systems;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace FDP.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="VisionBroadphaseSystem"/>.
    ///
    /// Test pattern (IModuleSystem):
    ///   1. Build world via <see cref="PerceptionTestWorldFactory"/>.
    ///   2. Cast to <see cref="ISimulationView"/> — EntityRepository implements it natively.
    ///   3. <c>sys.Execute(view, dt)</c>.
    ///   4. Flush the ECB: <c>((EntityCommandBuffer)view.GetCommandBuffer()).Playback(world)</c>.
    ///   5. Swap buffers to move published events to the readable slot.
    ///   6. Assert <c>world.Bus.Consume&lt;LosCheckRequestEvent&gt;()</c>.
    ///
    /// <b>Forward convention (critical):</b>
    ///   Forward direction is derived from <c>Vector3.Transform(Vector3.UnitX, tf.Rotation)</c>.
    ///   <c>Quaternion.Identity</c> → yaw=0 → facing east (+X). A target placed at (obsX+d, obsY, 0)
    ///   is directly ahead; a target at (obsX, obsY+d, 0) is 90° off-axis.
    /// </summary>
    public class VisionBroadphaseSystemTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Flushes the ECB written by <see cref="IModuleSystem.Execute"/> back to
        /// the live world, then swaps event buffers so <c>Bus.Consume</c> can read them.
        /// Must be called on the same thread as Execute.
        /// </summary>
        private static void FlushEcbAndSwap(ISimulationView view, EntityRepository world)
        {
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(world);
            world.Bus.SwapBuffers();
        }

        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public void VisionBroadphase_EmitsLosCheckRequest_ForEnemyInFOV()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;
            var sys    = new VisionBroadphaseSystem();

            // Observer: Blue, at origin, facing east (Identity = yaw 0 = east).
            // FOV half-cosine = cos(30°) ≈ 0.866 → 60° full FOV.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity, // facing east
            });
            world.AddComponent(observer, new Faction    { FactionId = 1 }); // Blue
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 6f), // cos 30° ≈ 0.866
            });
            world.AddComponent(observer, new TargetMemory());

            // Target: Red, directly east — exactly in the forward cone.
            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(100f, 0f, 0f), // due east of observer
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new Faction { FactionId = 2 }); // Red

            // Act
            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            // Assert — exactly one LOS request emitted.
            var events = world.Bus.Consume<LosCheckRequestEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(observer.Index, events[0].ObserverEntityIndex);
            Assert.Equal(target.Index,   events[0].TargetEntityIndex);
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        [Fact]
        public void VisionBroadphase_ExcludesSameFaction_EmitsNoEvent()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;
            var sys    = new VisionBroadphaseSystem();

            // Observer and target both Blue → same faction → excluded.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(observer, new Faction    { FactionId = 1 });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = 0.866f,
            });
            world.AddComponent(observer, new TargetMemory());

            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(50f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new Faction { FactionId = 1 }); // also Blue

            // Act
            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            // Assert — no event because target is friendly.
            var events = world.Bus.Consume<LosCheckRequestEvent>();
            Assert.Equal(0, events.Length);
        }

        // ── Test 3 ───────────────────────────────────────────────────────────────

        [Fact]
        public void VisionBroadphase_ExcludesTarget_BeyondVisionRange()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;
            var sys    = new VisionBroadphaseSystem();

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(observer, new Faction    { FactionId = 1 });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 50f,    // target is at 100 m — outside range
                HearingRange   = 50f,
                FieldOfViewCos = 0.866f,
            });
            world.AddComponent(observer, new TargetMemory());

            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(100f, 0f, 0f), // 100 m east — beyond 50 m range
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new Faction { FactionId = 2 });

            // Act
            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            // Assert — no event because target is beyond VisionRange.
            var events = world.Bus.Consume<LosCheckRequestEvent>();
            Assert.Equal(0, events.Length);
        }

        // ── Test 4 ───────────────────────────────────────────────────────────────

        [Fact]
        public void VisionBroadphase_ExcludesTarget_OutsideFOVCone()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;
            var sys    = new VisionBroadphaseSystem();

            // Observer facing east (Identity). FieldOfViewCos = cos(30°) ≈ 0.866.
            // A target due north has dot(forward=(1,0), dir=(0,1)) = 0 < 0.866 → outside cone.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity, // yaw=0 → east
            });
            world.AddComponent(observer, new Faction    { FactionId = 1 });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 6f), // 30° → 60° full FOV
            });
            world.AddComponent(observer, new TargetMemory());

            // Target directly north — 90° off observer's forward axis.
            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(0f, 100f, 0f), // due north
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new Faction { FactionId = 2 });

            // Act
            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            // Assert — dot(east, north) = 0 < 0.866 → outside FOV → no event.
            var events = world.Bus.Consume<LosCheckRequestEvent>();
            Assert.Equal(0, events.Length);
        }
    }
}
