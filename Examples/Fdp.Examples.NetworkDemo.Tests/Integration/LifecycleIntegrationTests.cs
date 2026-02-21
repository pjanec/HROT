using System.Text;
using System.Threading.Tasks;
using Xunit;
using Fdp.Examples.NetworkDemo;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Network;

namespace Fdp.Examples.NetworkDemo.Tests.Integration
{
    /// <summary>
    /// Integration tests verifying the Entity Lifecycle Management (ELM) flow.
    ///
    /// A locally spawned entity must remain in <see cref="EntityLifecycle.Constructing"/>
    /// until every peer in the topology has acknowledged it (or a timeout fires),
    /// after which it transitions to <see cref="EntityLifecycle.Active"/>.
    /// </summary>
    public class LifecycleIntegrationTests
    {
        [Fact(Timeout = 120_000)]
        public async Task Nodes_WaitForPeers_BeforeBecomingActive()
        {
            // ── Step 1: Boot Node A ──────────────────────────────────────────
            using var nodeA = new NetworkDemoApp();
            await nodeA.InitializeAsync(100, false, autoSpawn: true, enableNetwork: true);

            // Run a short burst so the spawn command and BeginConstruction events are processed.
            for (int i = 0; i < 20; i++)
            {
                nodeA.Update(0.1f);
                await Task.Delay(10);
            }

            // ── Step 2: Verify Node A entity exists and is stuck in Constructing ──
            // NOTE: newly spawned entities are in EntityLifecycle.Constructing (header
            // state). The default QueryBuilder filter is Active-only, so we must use
            // WithLifecycle(All) to find entities that are still Constructing.
            AssertTankSpawned(nodeA);

            var tankA = GetTankByOwner(nodeA, nodeA.LocalNodeId);
            Assert.NotEqual(Entity.Null, tankA);

            Assert.Equal(EntityLifecycle.Constructing, nodeA.World.GetLifecycleState(tankA));
            Assert.True(nodeA.World.HasComponent<PendingNetworkAck>(tankA),
                "Locally spawned entity must carry PendingNetworkAck until all peers ACK it.");

            // ── Step 3: Boot Node B ──────────────────────────────────────────
            using var nodeB = new NetworkDemoApp();
            await nodeB.InitializeAsync(200, false, autoSpawn: true, enableNetwork: true);

            // ── Step 4: Drive both nodes until both local entities become Active ──
            // The entity transitions to Active when all topology peers have ACK'd, or
            // when the NetworkGateway reliable-init timeout fires (~300 frames).
            bool aIsActive = false;
            bool bIsActive = false;

            for (int i = 0; i < 500; i++)   // 5-second budget at 10 ms ticks
            {
                nodeA.Update(0.1f);
                nodeB.Update(0.1f);
                await Task.Delay(10);

                // Guard: entity should not be destroyed during lifecycle transition.
                if (!nodeA.World.IsAlive(tankA))
                    Assert.Fail("Node A tank entity was unexpectedly destroyed during lifecycle wait.");

                var stateA = nodeA.World.GetLifecycleState(tankA);

                var tankB = GetTankByOwner(nodeB, nodeB.LocalNodeId);
                var stateB = tankB != Entity.Null
                    ? nodeB.World.GetLifecycleState(tankB)
                    : EntityLifecycle.Constructing;

                if (stateA == EntityLifecycle.Active && stateB == EntityLifecycle.Active)
                {
                    aIsActive = true;
                    bIsActive = true;
                    break;
                }
            }

            Assert.True(aIsActive,
                "Node A entity should become Active once the peer has joined and ACK'd " +
                "(or after the reliable-init timeout).");
            Assert.True(bIsActive,
                "Node B entity should become Active once Node A has ACK'd " +
                "(or after the reliable-init timeout).");
        }

        // ── Diagnostic helpers ───────────────────────────────────────────────

        /// <summary>
        /// Asserts that at least one entity owned by this node exists in the world,
        /// searching across ALL lifecycle states so that Constructing entities are
        /// also visible (the default query only returns Active entities).
        /// </summary>
        private static void AssertTankSpawned(NetworkDemoApp node)
        {
            var sb = new StringBuilder();
            // Must use WithLifecycle(All) — newly spawned tanks are Constructing and
            // would be invisible to the default Active-only query.
            var query = node.World.Query()
                             .WithLifecycle(EntityLifecycle.All)
                             .Build();
            int total = 0;
            bool found = false;

            foreach (var e in query)
            {
                total++;
                sb.AppendLine($"  Entity[{e.Index}] LifecycleHeader={node.World.GetLifecycleState(e)}:");

                if (node.World.HasComponent<NetworkOwnership>(e))
                {
                    var own = node.World.GetComponent<NetworkOwnership>(e);
                    sb.AppendLine($"    NetworkOwnership: PrimaryOwner={own.PrimaryOwnerId}  LocalNode={own.LocalNodeId}");
                    if (own.PrimaryOwnerId == node.LocalNodeId) found = true;
                }
                else
                {
                    sb.AppendLine("    (no NetworkOwnership)");
                }

                if (node.World.HasComponent<LifecycleDescriptor>(e))
                {
                    var lc = node.World.GetComponent<LifecycleDescriptor>(e);
                    sb.AppendLine($"    LifecycleDescriptor: State={lc.State}  Required=0x{lc.RequiredModulesMask:X}  Acked=0x{lc.AckedModulesMask:X}");
                }
                else
                {
                    sb.AppendLine("    (no LifecycleDescriptor)");
                }
            }

            Assert.True(found,
                $"No entity with LocalNodeId={node.LocalNodeId} found in Node {node.InstanceId}.\n" +
                $"Total entities visible (All lifecycle states): {total}\n" +
                sb.ToString());
        }

        // ── Query helper ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the first entity whose <see cref="NetworkOwnership.PrimaryOwnerId"/>
        /// matches <paramref name="ownerId"/>, searching across ALL lifecycle states.
        /// </summary>
        private static Entity GetTankByOwner(NetworkDemoApp node, int ownerId)
        {
            // WithLifecycle(All) so we find the tank while it is still Constructing
            // AND after it has been promoted to Active.
            var query = node.World.Query()
                             .With<NetworkOwnership>()
                             .WithLifecycle(EntityLifecycle.All)
                             .Build();
            foreach (var e in query)
            {
                if (node.World.GetComponent<NetworkOwnership>(e).PrimaryOwnerId == ownerId)
                    return e;
            }
            return Entity.Null;
        }
    }
}
