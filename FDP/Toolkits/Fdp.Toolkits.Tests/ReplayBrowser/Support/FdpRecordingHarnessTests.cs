using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Xunit;
// ReSharper disable AccessToDisposedClosure

namespace Fdp.Toolkit.ReplayBrowser.Support
{
    // Event ID 99003 reserved for this file (Fdp.Toolkits.Tests/ReplayBrowser/Support)
    [EventId(99003)]
    internal struct HarnessTestEventA { public int Payload; }

    // Managed event — no [EventId] required (managed events use hash of type name)
    internal sealed class HarnessTestManagedEvent { public string Tag { get; set; } = ""; }

    public class FdpRecordingHarnessTests : IDisposable
    {
        public FdpRecordingHarnessTests()
        {
            ComponentTypeRegistry.Clear();
        }

        public void Dispose() { }

        [Fact]
        public void HarnessSelfTest_FrameContent_DestructionLogAndEvents()
        {
            string path;
            using var harness = new FdpRecordingHarness();

            harness.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            var entityA = harness.LastSpawned;
            harness.SpawnEntity().WithComponent(new HarnessPosition { X = 2f, Y = 0f, Z = 0f });
            harness.SpawnEntity().WithComponent(new HarnessPosition { X = 3f, Y = 0f, Z = 0f });
            var entityToDestroy = harness.LastSpawned;

            harness.Tick().RecordKeyframe(100_000L);                          // frame 0
            harness.MutateComponent<HarnessPosition>(entityA, p => new HarnessPosition { X = p.X + 1f, Y = p.Y, Z = p.Z });
            harness.Tick().RecordDelta(200_000L);                             // frame 1
            harness.Tick().RecordDelta(300_000L);                             // frame 2
            harness.DestroyEntity(entityToDestroy);
            harness.Tick().RecordDelta(400_000L);                             // frame 3 - destruction
            harness.FireUnmanagedEvent(new HarnessTestEventA { Payload = 99 });
            harness.FireManagedEvent(new HarnessTestManagedEvent { Tag = "test" });
            harness.Tick().RecordDelta(500_000L);                             // frame 4 - events
            harness.BuildToTempFile(out path);

            // Step through frames with a fresh sandbox repo (must register same component types)
            var sandboxRepo = new EntityRepository();
            sandboxRepo.RegisterComponent<HarnessPosition>();
            sandboxRepo.RegisterComponent<HarnessVelocity>();
            var sandboxBus = new FdpEventBus();

            using var playback = new PlaybackController(path);
            playback.EventBus = sandboxBus;

            Assert.True(playback.StepForward(sandboxRepo));  // frame 0 (keyframe)
            Assert.True(playback.StepForward(sandboxRepo));  // frame 1
            Assert.True(playback.StepForward(sandboxRepo));  // frame 2

            // Frame 3: destruction
            Assert.True(playback.StepForward(sandboxRepo));  // frame 3
            var destructionLog = sandboxRepo.GetDestructionLog();
            Assert.Contains(destructionLog, e => e.Index == entityToDestroy.Index);
            sandboxRepo.ClearDestructionLog();

            // Frame 4: events
            Assert.True(playback.StepForward(sandboxRepo));  // frame 4
            var unmanagedEvents = sandboxBus.Read<HarnessTestEventA>();
            Assert.Equal(1, unmanagedEvents.Length);
            Assert.Equal(99, unmanagedEvents[0].Payload);
            var managedEvents = sandboxBus.ReadManaged<HarnessTestManagedEvent>();
            Assert.Equal(1, managedEvents.Count);
            Assert.Equal("test", managedEvents[0].Tag);

            // End of recording
            Assert.False(playback.StepForward(sandboxRepo));

            harness.Dispose();
        }

        [Fact]
        public void HarnessSelfTest_ProducesReadableRecording()
        {
            string path;

            using var harness = new FdpRecordingHarness();

            // Spawn 3 entities with HarnessPosition components
            harness.SpawnEntity().WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f });
            var entityA = harness.LastSpawned;

            harness.SpawnEntity().WithComponent(new HarnessPosition { X = 2f, Y = 0f, Z = 0f });
            var entityB = harness.LastSpawned;

            harness.SpawnEntity().WithComponent(new HarnessPosition { X = 3f, Y = 0f, Z = 0f });
            var entityToDestroy = harness.LastSpawned;

            // Frame 0: Keyframe
            harness.Tick().RecordKeyframe(100_000L);

            // Frame 1: Delta — small mutation
            harness.MutateComponent<HarnessPosition>(entityA, p => new HarnessPosition { X = p.X + 1f, Y = p.Y, Z = p.Z });
            harness.Tick().RecordDelta(200_000L);

            // Frame 2: Delta — another mutation
            harness.MutateComponent<HarnessPosition>(entityB, p => new HarnessPosition { X = p.X, Y = p.Y + 1f, Z = p.Z });
            harness.Tick().RecordDelta(300_000L);

            // Frame 3: Delta — destroy one entity
            harness.DestroyEntity(entityToDestroy);
            harness.Tick().RecordDelta(400_000L);

            // Frame 4: Delta — fire events
            harness.FireUnmanagedEvent(new HarnessTestEventA { Payload = 99 });
            harness.FireManagedEvent(new HarnessTestManagedEvent { Tag = "test" });
            harness.Tick().RecordDelta(500_000L);

            harness.BuildToTempFile(out path);

            Assert.True(File.Exists(path), "Recording .fdp file should exist after BuildToTempFile.");
            Assert.True(File.Exists(path + ".meta.json"), "Companion .meta.json should exist after BuildToTempFile.");

            // Verify the recording is readable and has the expected frame structure
            using (var playback = new PlaybackController(path))
            {
                Assert.Equal(5, playback.TotalFrames);

                var meta0 = playback.GetFrameMetadata(0);
                Assert.Equal(FrameType.Keyframe, meta0.FrameType);
                Assert.Equal(100_000L, meta0.WallClockTicks);

                for (int i = 1; i < 5; i++)
                {
                    var meta = playback.GetFrameMetadata(i);
                    Assert.Equal(FrameType.Delta, meta.FrameType);
                }

                Assert.Equal(200_000L, playback.GetFrameMetadata(1).WallClockTicks);
                Assert.Equal(300_000L, playback.GetFrameMetadata(2).WallClockTicks);
                Assert.Equal(400_000L, playback.GetFrameMetadata(3).WallClockTicks);
                Assert.Equal(500_000L, playback.GetFrameMetadata(4).WallClockTicks);
            }

            // Dispose the harness and verify temp files are cleaned up
            harness.Dispose();
            Assert.False(File.Exists(path), "Harness.Dispose() should delete the .fdp file.");
        }
    }
}
