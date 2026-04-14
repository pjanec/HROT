using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Fdp.Examples.NetworkDemo.Tests.Infrastructure;
using Fdp.Examples.NetworkDemo.Tests.Extensions;
using Fdp.Kernel;
using Fdp.Toolkit.Lifecycle;

namespace Fdp.Examples.NetworkDemo.Tests.Scenarios
{
    [Collection("DdsIntegration")]
    public class LifecycleTests
    {
        private readonly ITestOutputHelper _output;

        public LifecycleTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Entity_Lifecycle_Creation_Activation_Destruction()
        {
            using var env = new DistributedTestEnv(_output);
            await env.StartNodesAsync();
            await Task.Delay(100);
            
            // 1. CREATION
            var tankA = env.NodeA.SpawnTank();
            long netId = env.NodeA.GetNetworkId(tankA);
            
            // 2. GHOST DETECTION
            await env.WaitForCondition(
                app => app.TryGetEntityByNetId(netId, out _),
                env.NodeB);
            
            var tankB = env.NodeB.GetEntityByNetId(netId);
            _output.WriteLine($"Node B Tank obtained: {tankB}");
            
            // 3. ACTIVATION (wait for mandatory data)
            // Note: GhostPromotionSystem handles transition to Active
            await env.WaitForCondition(
                app => app.World.GetLifecycleState(tankB) == EntityLifecycle.Active,
                env.NodeB);
            
            Assert.Equal(EntityLifecycle.Active, env.NodeB.World.GetLifecycleState(tankB));
            
            // 4. DESTRUCTION
            env.NodeA.World.DestroyEntity(tankA);
            await env.RunFrames(200); // Give it some time
            
            // DEBUG: Verify detection
            env.AssertLogContains(100, "Detected entity destruction");
            
            // Wait a bit more for replication
            await env.RunFrames(10);
            
            // DEBUG: Verify reception
            env.AssertLogContains(200, "Received Death Note");
            env.AssertLogContains(200, "Destroying...");

            // 5. VERIFY DELETION PROPAGATED
            await env.WaitForCondition(
                app => !app.World.IsAlive(tankB),
                env.NodeB);
                
            Assert.False(env.NodeB.World.IsAlive(tankB));
        }
    }
}
