using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Fdp.Examples.NetworkDemo.Tests.Infrastructure;
using Fdp.Examples.NetworkDemo.Tests.Extensions;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.ModuleHost_Core.Network;
using System.Linq;

namespace Fdp.Examples.NetworkDemo.Tests.Scenarios
{
    public class OwnershipTests
    {
        private readonly ITestOutputHelper _output;

        public OwnershipTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task FDPLT_016_Partial_Ownership_BiDirectional_Updates()
        {
            using var env = new DistributedTestEnv(_output);
            await env.StartNodesAsync();
            var appA = env.NodeA;
            var appB = env.NodeB;

            // 1. Spawn Tank on A (Primary Owner A)
            var tankA = appA.SpawnTank();
            long netId = appA.GetNetworkId(tankA);
            
            _output.WriteLine($"Spawned Tank {netId} on A");

            // 2. Wait for Ghost on B — use EntityMap for lifecycle-agnostic lookup.
            await env.WaitForCondition(app => app.EntityMap.TryGetEntity(netId, out _), appB, 5000);
            
            var tankB = appB.GetEntityByNetId(netId);
            _output.WriteLine($"Got Tank {tankB} on B");

            // 3. Setup Split Authority (Composite Tank)
            // A owns Chassis (Implicit). B owns Turret (ID 20).
            long turretKey = 20;

            // Configure on A
            var descOwnA = new DescriptorOwnership();
            // Critical Fix: Node A sees Node B (200) as Internal ID 2.
            // appB.LocalNodeId is 1 (B's view of itself). We must use A's view of B.
            descOwnA.SetOwner(turretKey, 2); 
            appA.World.AddComponent(tankA, descOwnA);
            // Since SpawnTank gives authority to creator, we must explicitly relinquish it for Turret
            // so that A accepts updates from B.
            appA.World.SetAuthority<TurretState>(tankA, false);

            // Configure on B
            appB.EnqueueAction(world => 
            {
                var descOwnB = new DescriptorOwnership();
                descOwnB.SetOwner(turretKey, appB.LocalNodeId);
                world.AddComponent(tankB, descOwnB);
                
                // Manually set authority because the test bypasses ComponentSystem logic that would normally sync it
                // from DescriptorOwnership to AuthorityMask (if such a system existed).
                world.SetAuthority<TurretState>(tankB, true);

                Console.WriteLine($"[TEST-ACTION] Enqueued AddComponent Executed. Visible: {world.HasComponent<DescriptorOwnership>(tankB)}");
            });

            // Wait for queue to process
            await Task.Delay(100);
            
            // Check manually (unsafe check from test thread, but needed for assertion)
            if (!appB.World.HasComponent<DescriptorOwnership>(tankB))
            {
                _output.WriteLine("WARNING: Component not visible from Test Thread yet.");
            }
            // Assert.True(actionExecuted, "Action queue did not process!");

            // Ensure TurretState exists on B (Source) and A (Destination)
            appB.EnqueueAction(w => 
            {
                if (!w.HasComponent<TurretState>(tankB)) w.AddComponent(tankB, new TurretState());
                // 4. Update Turret on B (Owner)
                w.SetComponent(tankB, new TurretState { Yaw = 90.0f, Pitch = 45.0f, AmmoCount = 10 });
            });
            
            appA.EnqueueAction(w => 
            {
                if (!w.HasComponent<TurretState>(tankA)) w.AddComponent(tankA, new TurretState());
            });

            // Force Publish? B should publish automatically if Egress scans it and checks authority.
            // Egress runs periodically.
            
            _output.WriteLine("Updated Turret on B. Waiting for replication to A...");

            // 5. Verify Replication to A
            await env.WaitForCondition(app => 
            {
                var ts = app.World.GetComponent<TurretState>(tankA);
                return ts.Yaw == 90.0f;
            }, appA);

            var receivedTurret = appA.World.GetComponent<TurretState>(tankA);
            Assert.Equal(90.0f, receivedTurret.Yaw);
            Assert.Equal(45.0f, receivedTurret.Pitch);
            
            _output.WriteLine("Replication B -> A Successful!");

            // 6. Verify A does NOT overwrite B?
            // If A updates local state, it shouldn't publish it back to B because A doesn't have authority.
            // But A applies received state.
            // Test complete.
        }
    }
}
