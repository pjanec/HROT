using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using MessagePack;

namespace Fdp.Tests
{
    public class LifeCycleSchemaTests : IDisposable
    {
        private readonly string _testFilePath;

        public LifeCycleSchemaTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"lifecycle_schema_{Guid.NewGuid()}.fdp");
        }

        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch { }
        }

        [MessagePackObject]
        [ComponentId(199)]
        public record CompA
        {
            [Key(0)] public int Value { get; set; }
        }

        [MessagePackObject]
        [ComponentId(204)]
        public record CompB
        {
            [Key(0)] public string? Name { get; set; }
        }

        [ComponentId(205)]
        struct UnmanagedA
        {
            public int X;
        }

        [ComponentId(206)]
        struct UnmanagedB
        {
            public float Y;
        }

        private class FrameData
        {
            public Entity EntityHandle;
            public bool IsAlive;
            // storing expectation of presence
            public bool HasUA;
            public bool HasUB;
            public bool HasMA;
            public bool HasMB;
            
            // storing values
            public UnmanagedA ValUA;
            public UnmanagedB ValUB;
            public int ValMA_Value;
            public string? ValMB_Name;
        }

        [Fact]
        public void EntityLifecycle_CreationDeletionRecreation_VerifiesSchemaAndState()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<UnmanagedA>();
            repo.RegisterComponent<UnmanagedB>();
            repo.RegisterComponent<CompA>();
            repo.RegisterComponent<CompB>();

            var expectedStates = new Dictionary<int, FrameData>();
            
            // We'll track the CURRENT active handle for the slot we are testing. 
            // When it's dead, we might keep the old handle or null.
            Entity currentHandle = default;

            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Frame 0: Initial state (empty)
                repo.Tick();
                CaptureExpectedState(0);
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);

                // Frame 1: Create Entity with UA and MA
                repo.Tick();
                currentHandle = repo.CreateEntity();
                repo.AddComponent(currentHandle, new UnmanagedA { X = 10 });
                repo.AddManagedComponent(currentHandle, new CompA { Value = 100 });
                
                CaptureExpectedState(1);
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);

                // Frame 2: Add UB, Remove MA
                repo.Tick();
                repo.AddComponent(currentHandle, new UnmanagedB { Y = 2.5f });
                repo.RemoveManagedComponent<CompA>(currentHandle);
                
                CaptureExpectedState(2);
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);

                // Frame 3: Destroy Entity
                repo.Tick();
                repo.DestroyEntity(currentHandle);
                
                CaptureExpectedState(3); // Captured using the now DEAD handle
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);

                // Frame 4: Empty frame
                repo.Tick();
                CaptureExpectedState(4);
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);

                // Frame 5: Re-create Entity (reuses slot, new generation) with UA, MA, MB
                repo.Tick();
                var e2 = repo.CreateEntity(); 
                // Ensure we reused the slot for the test's sake, though not strictly required for correctness 
                // (but required for the semantics of "re-creation in different frames" affecting the SAME slot/index usually implies reuse logic)
                Assert.Equal(currentHandle.Index, e2.Index); 
                currentHandle = e2;

                repo.AddComponent(e2, new UnmanagedA { X = 99 });
                repo.AddManagedComponent(e2, new CompA { Value = 999 });
                repo.AddManagedComponent(e2, new CompB { Name = "Reborn" });

                CaptureExpectedState(5);
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);

                // Frame 6: Modify State
                repo.Tick();
                repo.SetUnmanagedComponent(e2, new UnmanagedA { X = 88 });
                repo.GetComponentRW<CompA>(e2).Value = 888;
                
                CaptureExpectedState(6);
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frame 7: Destroy Again
                repo.Tick();
                repo.DestroyEntity(e2);
                CaptureExpectedState(7);
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);

            } // Recorder disposal finishes file write

            // Helper to capture state
            void CaptureExpectedState(int frame)
            {
                var data = new FrameData
                {
                    EntityHandle = currentHandle
                };

                if (currentHandle == default || !repo.IsAlive(currentHandle))
                {
                    data.IsAlive = false;
                }
                else
                {
                    data.IsAlive = true;
                    data.HasUA = repo.HasUnmanagedComponent<UnmanagedA>(currentHandle);
                    data.HasUB = repo.HasUnmanagedComponent<UnmanagedB>(currentHandle);
                    data.HasMA = repo.HasManagedComponent<CompA>(currentHandle);
                    data.HasMB = repo.HasManagedComponent<CompB>(currentHandle);

                    if (data.HasUA) data.ValUA = repo.GetComponentRO<UnmanagedA>(currentHandle);
                    if (data.HasUB) data.ValUB = repo.GetComponentRO<UnmanagedB>(currentHandle);
                    if (data.HasMA) data.ValMA_Value = repo.GetComponentRO<CompA>(currentHandle).Value;
                    if (data.HasMB) data.ValMB_Name = repo.GetComponentRO<CompB>(currentHandle).Name;
                }
                expectedStates[frame] = data;
            }

            // Playback and Verify
            using var playbackRepo = new EntityRepository();
            playbackRepo.RegisterComponent<UnmanagedA>();
            playbackRepo.RegisterComponent<UnmanagedB>();
            playbackRepo.RegisterComponent<CompA>();
            playbackRepo.RegisterComponent<CompB>();

            using var controller = new PlaybackController(_testFilePath);

            // Forward Verification
            for (int f = 0; f <= 7; f++)
            {
                controller.SeekToFrame(playbackRepo, f);
                VerifyFrame(f, playbackRepo);
            }

            // Reverse/Random Verification
            int[] seekPattern = new[] { 5, 2, 7, 0, 6, 1, 3, 4 };
            foreach(var f in seekPattern)
            {
                controller.SeekToFrame(playbackRepo, f);
                VerifyFrame(f, playbackRepo);
            }

            void VerifyFrame(int frame, EntityRepository r)
            {
                var expected = expectedStates[frame];
                
                if (!expected.IsAlive)
                {
                    // If expected matches our tracked handle, it should be dead.
                    if (expected.EntityHandle != default)
                    {
                        Assert.False(r.IsAlive(expected.EntityHandle), $"Frame {frame}: Entity {expected.EntityHandle} should be DEAD but is ALIVE.");
                    }
                    else
                    {
                        // If we didn't even have a handle (frame 0 start), ensure count is 0
                        // (Assuming we only created one entitiy ever in this test)
                        // Actually let's just assert the specific handle if it existed is not alive
                    }
                    return;
                }

                // If expected Alive
                Assert.True(r.IsAlive(expected.EntityHandle), $"Frame {frame}: Entity {expected.EntityHandle} should be ALIVE but is DEAD.");
                
                // Verify Schema (Component Presence)
                Assert.Equal(expected.HasUA, r.HasUnmanagedComponent<UnmanagedA>(expected.EntityHandle));
                Assert.Equal(expected.HasUB, r.HasUnmanagedComponent<UnmanagedB>(expected.EntityHandle));
                Assert.Equal(expected.HasMA, r.HasManagedComponent<CompA>(expected.EntityHandle));
                Assert.Equal(expected.HasMB, r.HasManagedComponent<CompB>(expected.EntityHandle));

                // Verify State (Values)
                if (expected.HasUA) Assert.Equal(expected.ValUA.X, r.GetComponentRO<UnmanagedA>(expected.EntityHandle).X);
                if (expected.HasUB) Assert.Equal(expected.ValUB.Y, r.GetComponentRO<UnmanagedB>(expected.EntityHandle).Y);
                if (expected.HasMA) Assert.Equal(expected.ValMA_Value, r.GetComponentRO<CompA>(expected.EntityHandle).Value);
                if (expected.HasMB) Assert.Equal(expected.ValMB_Name, r.GetComponentRO<CompB>(expected.EntityHandle).Name);
            }
        }
    }
}
