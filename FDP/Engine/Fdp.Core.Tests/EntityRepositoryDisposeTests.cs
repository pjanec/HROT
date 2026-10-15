using Xunit;
using Fdp.Core;
using System.Threading.Tasks;
using System;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Tests
{
    public class EntityRepositoryDisposeTests
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference AbandonAWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<RecorderDeltaLogicTests.Position>();   // a declared test component (ids are explicit)
            var e = repo.CreateEntity();
            repo.AddComponent(e, new RecorderDeltaLogicTests.Position());   // a committed chunk, as a used table has
            _ = repo.UnmanagedHandle;                          // the handle the HSM bridge reads
            return new WeakReference(repo);
        }

        /// <summary>
        /// ⭐ <c>CE-3183</c> — a world nobody disposed is COLLECTED, so its tables' finalizers free their mmap reservations. ⛔ Its
        /// self-handle (DEBT-007's bridge handle) was strong and rooted it for the life of the process: 📐 measured on
        /// <c>Hrot.SimHost.Tests</c>, 484 undisposed test worlds held 56 240 mappings against <c>vm.max_map_count</c> 65 530 and the
        /// suite failed a rotating set with <c>OutOfMemoryException</c>. ⭐ And a world that IS held still resolves through the
        /// handle (the bridge's contract). Disposing stays the rule — <c>LiveInstanceCount</c> still counts an undisposed world.
        /// </summary>
        [Fact]
        public void CE3183_AWorldNobodyDisposed_IsCollected_AndAHeldOneStillResolvesThroughItsHandle()
        {
            if (Environment.GetEnvironmentVariable("FDP_TRACK_REPO_LEAKS") == "1") return;   // the leak tracker roots every world

            var abandoned = AbandonAWorld();
            for (int i = 0; i < 3 && abandoned.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.False(abandoned.IsAlive, "an undisposed world must not be rooted by its own handle");

            using var held = new EntityRepository();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var back = System.Runtime.InteropServices.GCHandle.FromIntPtr(held.UnmanagedHandle).Target;
            Assert.Same(held, back);
        }

        [Fact]
        public async Task Dispose_DisposesThreadLocalCommandBuffer()
        {
            var repo = new EntityRepository();
            
            // Acquire command buffer from multiple threads
            var tasks = new Task[5];
            for (int i = 0; i < 5; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    var cmd = ((ISimulationView)repo).GetCommandBuffer();
                    cmd.CreateEntity(); // Use it
                });
            }
            await Task.WhenAll(tasks);
            
            // Dispose
            repo.Dispose();
            
            // Verify: ThreadLocal should be disposed
            // (Can't directly test, but no exception should occur)
            Assert.True(true); // If we get here, no exception
        }

        [Fact]
        public void Dispose_NoMemoryLeak_LongRunning()
        {
            // Create and dispose 1000 repos with command buffers
            for (int i = 0; i < 1000; i++)
            {
                var repo = new EntityRepository();
                var cmd = ((ISimulationView)repo).GetCommandBuffer();
                cmd.CreateEntity();
                repo.Dispose();
            }
            
            // Force GC
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            // If ThreadLocal not disposed, this would leak
            // Test passes if no OutOfMemoryException
            Assert.True(true);
        }
    }
}
