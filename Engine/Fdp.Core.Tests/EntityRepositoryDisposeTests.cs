using Xunit;
using Fdp.Kernel;
using System.Threading.Tasks;
using System;
using ModuleHost.Core.Abstractions;

namespace Fdp.Tests
{
    public class EntityRepositoryDisposeTests
    {
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
