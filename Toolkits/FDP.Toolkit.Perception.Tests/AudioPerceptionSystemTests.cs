using System.Numerics;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Systems;
using Fdp.Kernel;
using Xunit;

namespace FDP.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AudioPerceptionSystem"/>.
    ///
    /// Test pattern:
    ///   1. Build world via <see cref="PerceptionTestWorldFactory"/>.
    ///   2. <c>sys.Create(world); /* … setup … */ world.Bus.SwapBuffers(); sys.Run();</c>
    ///   3. Assert <see cref="TargetMemory"/> was updated as expected.
    ///
    /// The system falls back to a brute-force entity scan when no
    /// <c>SpatialGridData</c> singleton is present, which is always the case in these tests.
    /// </summary>
    public class AudioPerceptionSystemTests
    {
        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public unsafe void AudioPerception_UpdatesTargetMemory_WhenListenerWithinHearingRange()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var sys   = new AudioPerceptionSystem();
            sys.Create(world);

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
            world.AddComponent(listener, new Faction { FactionId = 1 });

            // Create a source entity (just needs to exist so its index is a valid entity index).
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
            // Swap so the event is readable by Consume<AudioStimulusEvent>() in OnUpdate.
            world.Bus.SwapBuffers();

            // Act
            sys.Run();

            // Assert — listener's TargetMemory must have one entry for the source entity.
            var mem = world.GetComponent<TargetMemory>(listener);
            Assert.Equal(1, mem.Count);
            Assert.Equal((long)source.Index, mem.EntityIds[0]);
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        [Fact]
        public unsafe void AudioPerception_DoesNotUpdate_WhenListenerOutsideHearingRange()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var sys   = new AudioPerceptionSystem();
            sys.Create(world);

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
            world.AddComponent(listener, new Faction { FactionId = 1 });

            // Source 50 m away; broadphase radius (Intensity) large enough to include the listener
            // in the fallback scan, but the per-receptor HearingRange check must exclude it.
            // Use a real entity for SourceEntityIndex — raw magic numbers like 99 mask stale-reference
            // bugs if this pattern were copied to a positive-path test (DEBT-014).
            var dummySource = world.CreateEntity();
            world.AddComponent(dummySource, new SimTransform
            {
                Position = new Vector3(50f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });

            world.Bus.Publish(new AudioStimulusEvent
            {
                Origin            = new Vector3(50f, 0f, 0f),
                Intensity         = 60f, // listener is within fallback radius…
                SourceEntityIndex = dummySource.Index,
            });
            world.Bus.SwapBuffers();

            // Act
            sys.Run();

            // Assert — HearingRange=30 < dist=50 → no update.
            var mem = world.GetComponent<TargetMemory>(listener);
            Assert.Equal(0, mem.Count);
        }

        // ── Test 3 ───────────────────────────────────────────────────────────────

        [Fact]
        public unsafe void AudioPerception_OnlyUpdatesNearbyListener_WhenTwoListenersExist()
        {
            // Arrange
            var world = PerceptionTestWorldFactory.Create();
            var sys   = new AudioPerceptionSystem();
            sys.Create(world);

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
            world.AddComponent(listenerA, new Faction { FactionId = 1 });

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
            world.AddComponent(listenerB, new Faction { FactionId = 1 });

            // Event at (50, 0, 0) with broadphase radius 100 — listenerA is inside, listenerB is not.
            world.Bus.Publish(new AudioStimulusEvent
            {
                Origin            = new Vector3(50f, 0f, 0f),
                Intensity         = 100f, // listenerB is 150 m away > 100 → not in fallback candidates
                SourceEntityIndex = 7,
            });
            world.Bus.SwapBuffers();

            // Act
            sys.Run();

            // Assert
            var memA = world.GetComponent<TargetMemory>(listenerA);
            var memB = world.GetComponent<TargetMemory>(listenerB);
            Assert.Equal(1, memA.Count);   // A is updated
            Assert.Equal(0, memB.Count);   // B is too far — not even a candidate
        }
    }
}
