using System.Numerics;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AudioPerceptionSystem"/> (PACK-A001).
    ///
    /// After PACK-A001 the system publishes <see cref="TargetHeardEvent"/> onto
    /// the ECS bus instead of directly mutating <see cref="TargetMemory"/>.
    ///
    /// Test pattern:
    ///   1. Build world via <see cref="PerceptionTestWorldFactory"/>.
    ///   2.  setup; world.Bus.SwapBuffers(); sys.Execute(world, 0f);
    ///   3. world.Bus.SwapBuffers() — exposing the system's output events.
    ///   4. Assert bus events / TargetMemory unchanged.
    ///
    /// The system falls back to a brute-force entity scan when no
    /// <c>SpatialGridData</c> singleton is present, which is always the case in these tests.
    /// </summary>
    public class AudioPerceptionSystemTests
    {
        // ── Test 1 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// PACK-A001 SC-3: AudioPerceptionSystem must publish one <see cref="TargetHeardEvent"/>
        /// when a listener is within hearing range, and must NOT write to <see cref="TargetMemory"/>.
        /// </summary>
        [Fact]
        public unsafe void AudioPerception_PublishesTargetHeardEvent_WhenListenerWithinHearingRange()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var sys   = new AudioPerceptionSystem();
            

            // Create a listener entity at the origin with generous ranges.
            var listener = world.CreateEntity();
            world.AddComponent(listener, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(listener, new PerceptionReceptor
            {
                HearingRange   = 100f,
                VisionRange    = 50f,
                FieldOfViewCos = 0.5f,
            });
            world.AddComponent(listener, new TargetMemory());
            world.AddComponent(listener, new EntityInfo { ForceId = ForceId.Friend });

            // Create a source entity.
            var source = world.CreateEntity();
            world.AddComponent(source, new SimTransform
            {
                Position = new Vector3(50f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });

            // Publish the audio stimulus (intensity = broadphase radius = 100).
            world.Bus.Publish(new AudioStimulusEvent
            {
                Origin            = new Vector3(50f, 0f, 0f),
                Intensity         = 100f,
                SourceEntityIndex = source.Index,
            });
            world.Bus.SwapBuffers();

            // Act
            sys.Execute(world, 0f);
            // Swap again to make the system's output events readable.
            world.Bus.SwapBuffers();

            // Assert — TargetHeardEvent published for the listener.
            var events = world.Bus.Read<TargetHeardEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(listener, events[0].Listener);
            Assert.Equal(source.Index, events[0].SourceEntityIndex);
            Assert.Equal(new Vector3(50f, 0f, 0f), events[0].Origin);

            // Assert — TargetMemory was NOT mutated by AudioPerceptionSystem (PACK-A001).
            var mem = world.GetComponent<TargetMemory>(listener);
            Assert.Equal(0, mem.Count);
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// PACK-A001: When the listener is outside hearing range, no <see cref="TargetHeardEvent"/>
        /// must be published.
        /// </summary>
        [Fact]
        public unsafe void AudioPerception_DoesNotPublish_WhenListenerOutsideHearingRange()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var sys   = new AudioPerceptionSystem();
            

            // Listener at origin with a short hearing range (30 m).
            var listener = world.CreateEntity();
            world.AddComponent(listener, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(listener, new PerceptionReceptor
            {
                HearingRange   = 30f, // event origin is 50 m away — outside range
                VisionRange    = 50f,
                FieldOfViewCos = 0.5f,
            });
            world.AddComponent(listener, new TargetMemory());
            world.AddComponent(listener, new EntityInfo { ForceId = ForceId.Friend });

            var dummySource = world.CreateEntity();
            world.AddComponent(dummySource, new SimTransform
            {
                Position = new Vector3(50f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });

            world.Bus.Publish(new AudioStimulusEvent
            {
                Origin            = new Vector3(50f, 0f, 0f),
                Intensity         = 60f,
                SourceEntityIndex = dummySource.Index,
            });
            world.Bus.SwapBuffers();

            // Act
            sys.Execute(world, 0f);
            world.Bus.SwapBuffers();

            // Assert — HearingRange=30 < dist=50 → no event published.
            var events = world.Bus.Read<TargetHeardEvent>();
            Assert.True(events.IsEmpty, "No TargetHeardEvent must be published when listener is outside hearing range.");
        }

        // ── Test 3 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// PACK-A001: Only the listener within hearing range receives a <see cref="TargetHeardEvent"/>.
        /// </summary>
        [Fact]
        public unsafe void AudioPerception_OnlyPublishesForNearbyListener_WhenTwoListenersExist()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var sys   = new AudioPerceptionSystem();
            

            // Listener A at origin (50 m from event — within broadphase AND hearing range).
            var listenerA = world.CreateEntity();
            world.AddComponent(listenerA, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(listenerA, new PerceptionReceptor
            {
                HearingRange   = 100f,
                VisionRange    = 50f,
                FieldOfViewCos = 0.5f,
            });
            world.AddComponent(listenerA, new TargetMemory());
            world.AddComponent(listenerA, new EntityInfo { ForceId = ForceId.Friend });

            // Listener B far away (200 m from event — beyond broadphase radius).
            var listenerB = world.CreateEntity();
            world.AddComponent(listenerB, new SimTransform
            {
                Position = new Vector3(200f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(listenerB, new PerceptionReceptor
            {
                HearingRange   = 100f,
                VisionRange    = 50f,
                FieldOfViewCos = 0.5f,
            });
            world.AddComponent(listenerB, new TargetMemory());
            world.AddComponent(listenerB, new EntityInfo { ForceId = ForceId.Friend });

            // Event at (50, 0, 0) with broadphase radius 100 — listenerA is inside, listenerB not.
            world.Bus.Publish(new AudioStimulusEvent
            {
                Origin            = new Vector3(50f, 0f, 0f),
                Intensity         = 100f,
                SourceEntityIndex = 7,
            });
            world.Bus.SwapBuffers();

            // Act
            sys.Execute(world, 0f);
            world.Bus.SwapBuffers();

            // Assert — exactly one event, for listenerA.
            var events = world.Bus.Read<TargetHeardEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(listenerA, events[0].Listener);
        }
    }
}


