using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fdp.Examples.NetworkDemo;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Events;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using System.Numerics;
using ModuleHost.Core.Network; // Added
using ModuleHost.Core.Abstractions;

namespace Fdp.Examples.NetworkDemo.Tests.Integration
{
    public class CombatSystemTests
    {
        [Fact]
        public async Task TwoNodes_FireEvent_DamageApplied()
        {
            // 1. Setup two nodes
            using var nodeA = new NetworkDemoApp();
            using var nodeB = new NetworkDemoApp();
            
            await nodeA.InitializeAsync(100, false, autoSpawn: true, enableNetwork: true);
            await nodeB.InitializeAsync(200, false, autoSpawn: true, enableNetwork: true);
            
            // 2. Let entities discover
            for (int i = 0; i < 50; i++)
            {
                nodeA.Update(0.1f);
                nodeB.Update(0.1f);
                await Task.Delay(10);
            }
            
            // 3. Fire from Node A
            var tankA = GetTankByOwner(nodeA, nodeA.LocalNodeId);
            Assert.NotEqual(Entity.Null, tankA);
            
            // Allow discovery (Bidirectional)
            Entity targetB_onA = Entity.Null;
            Entity attackerA_onB = Entity.Null;
            int retries = 0;
            while (retries < 100)
            {
                if (targetB_onA == Entity.Null)
                    targetB_onA = FindRemoteEntity(nodeA, nodeA.LocalNodeId);
                
                if (attackerA_onB == Entity.Null)
                    attackerA_onB = FindRemoteEntity(nodeB, nodeB.LocalNodeId);

                if (targetB_onA != Entity.Null && attackerA_onB != Entity.Null) break;

                nodeA.Update(0.1f);
                nodeB.Update(0.1f);
                await Task.Delay(10);
                retries++;
            }
            Assert.NotEqual(Entity.Null, targetB_onA);
            Assert.NotEqual(Entity.Null, attackerA_onB);

            // Wait for Topic Discovery (Reader/Writer matching) allows reliable connection
            for(int i=0; i<50; i++) 
            {
                 nodeA.Update(0.1f);
                 nodeB.Update(0.1f);
                 await Task.Delay(10);
            }

            ((ISimulationView)nodeA.World).GetCommandBuffer().PublishEvent(new FireInteractionEvent
            {
                AttackerRoot = tankA,
                TargetRoot = targetB_onA,
                WeaponInstanceId = 1,
                Damage = 25
            });
            
            // 4. Process event (Wait longer)
            for (int i = 0; i < 200; i++)
            {
                nodeA.Update(0.1f);
                nodeB.Update(0.1f);
                await Task.Delay(10); 
            }
            
            // 5. Verify damage on Node B
            var tankB = GetTankByOwner(nodeB, nodeB.LocalNodeId);
            Assert.NotEqual(Entity.Null, tankB);
            
            var health = nodeB.World.GetComponent<Health>(tankB);
            
            Assert.Equal(75, health.Value); // 100 - 25
        }

        // WithLifecycle(All): local tanks are in Constructing state until the
        // NetworkGateway ACK fires; we must find them regardless of lifecycle state.
        private Entity FindRemoteEntity(NetworkDemoApp node, int localNodeId)
        {
            var query = node.World.Query()
                             .With<NetworkOwnership>()
                             .WithLifecycle(EntityLifecycle.All)
                             .Build();
            foreach (var e in query)
            {
                var own = node.World.GetComponent<NetworkOwnership>(e);
                if (own.PrimaryOwnerId != localNodeId)
                    return e;
            }
            return Entity.Null;
        }

        private Entity GetTankByOwner(NetworkDemoApp app, int ownerId)
        {
            var query = app.World.Query()
                             .With<NetworkOwnership>()
                             .WithLifecycle(EntityLifecycle.All)
                             .Build();
            foreach (var e in query)
            {
                var own = app.World.GetComponent<NetworkOwnership>(e);
                if (own.PrimaryOwnerId == ownerId) return e;
            }
            return Entity.Null;
        }
    }
}
