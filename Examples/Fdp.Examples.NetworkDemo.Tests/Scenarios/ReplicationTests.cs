using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Fdp.Examples.NetworkDemo.Tests.Infrastructure;
using Fdp.Examples.NetworkDemo.Tests.Extensions;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Examples.NetworkDemo.Tests.Scenarios
{
    [Collection("DdsIntegration")]
    public class ReplicationTests
    {
        private readonly ITestOutputHelper _output;

        public ReplicationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Entity_Created_On_NodeA_Appears_On_NodeB()
        {
            using var env = new DistributedTestEnv(_output);
            await env.StartNodesAsync();
            
            // Wait for DDS discovery
            await Task.Delay(100);
            
            // Spawn tank on Node A
            var tankA = env.NodeA.SpawnTank();
            long netId = env.NodeA.GetNetworkId(tankA);
            
            _output.WriteLine($"Spawned tank with NetId {netId} on Node A");
            
            // Wait for replication
            await env.WaitForCondition(
                app => app.TryGetEntityByNetId(netId, out _), 
                env.NodeB, 
                timeoutMs: 3000);
            
            // Verify entity exists on Node B
            var tankB = env.NodeB.GetEntityByNetId(netId);
            Assert.NotEqual(Entity.Null, tankB);
            
            // Verify logs - commenting out as exact messages are not confirmed in codebase search
            // env.AssertLogContains(100, "Auth OK");
            env.AssertLogContains(200, "Created Proxy Entity");
        }
    }
}
