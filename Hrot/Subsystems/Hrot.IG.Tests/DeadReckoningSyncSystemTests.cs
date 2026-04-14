using System.Numerics;
using Hrot.Common.Systems;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Hrot.IG.Tests
{
    public class DeadReckoningSyncSystemTests
    {
        private static EntityRepository CreateRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<NetworkTransform>();
            repo.RegisterComponent<NetworkVelocity>();
            repo.RegisterComponent<NetworkAuthority>();
            return repo;
        }

        private static void PlaybackCommands(EntityRepository repo)
        {
            var view = (ISimulationView)repo;
            if (view.GetCommandBuffer() is EntityCommandBuffer ecb)
                ecb.Playback(repo);
        }

        [Fact]
        public void Execute_GhostEntity_ProjectsNetworkPosition()
        {
            using var repo = CreateRepo();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            repo.AddComponent(entity, new NetworkTransform { LastPosition = Vector3.Zero });
            repo.AddComponent(entity, new NetworkVelocity { Value = new Vector3(0f, 5f, 0f) });
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));

            var system = new DeadReckoningSyncSystem();
            system.Execute(repo, 0.1f);
            PlaybackCommands(repo);

            var netTf = repo.GetComponent<NetworkTransform>(entity);
            Assert.Equal(0f, netTf.LastPosition.X, 3);
            Assert.Equal(0.5f, netTf.LastPosition.Y, 3);
            Assert.Equal(0f, netTf.LastPosition.Z, 3);
        }

        [Fact]
        public void Execute_GhostEntity_BlendsSimTransform()
        {
            using var repo = CreateRepo();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = new Vector3(0f, 10f, 0f), Rotation = Quaternion.Identity });
            repo.AddComponent(entity, new NetworkTransform { LastPosition = Vector3.Zero });
            repo.AddComponent(entity, new NetworkVelocity { Value = new Vector3(0f, 5f, 0f) });
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));

            var system = new DeadReckoningSyncSystem();
            system.Execute(repo, 0.05f);
            PlaybackCommands(repo);

            var tf = repo.GetComponent<SimTransform>(entity);
            Assert.InRange(tf.Position.Y, 0.25f, 10f);
            Assert.NotEqual(10f, tf.Position.Y);
        }

        [Fact]
        public void Execute_AuthorityEntity_IsSkipped()
        {
            using var repo = CreateRepo();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = new Vector3(1f, 2f, 3f), Rotation = Quaternion.Identity });
            repo.AddComponent(entity, new NetworkTransform { LastPosition = new Vector3(4f, 5f, 6f) });
            repo.AddComponent(entity, new NetworkVelocity { Value = new Vector3(0f, 5f, 0f) });
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));

            var system = new DeadReckoningSyncSystem();
            system.Execute(repo, 0.1f);
            PlaybackCommands(repo);

            var netTf = repo.GetComponent<NetworkTransform>(entity);
            Assert.Equal(new Vector3(4f, 5f, 6f), netTf.LastPosition);
        }

        // ── MODINIT-S101 Success Condition 3 ─────────────────────────────────
        // driveFromNetwork=true → default Active lifecycle query → all non-authority
        // Active entities are updated regardless of their conceptual role.

        [Fact]
        public void Execute_DriveFromNetworkTrue_UpdatesBothActiveEntities()
        {
            using var repo = CreateRepo();

            // Entity A — represents a "locally-present" Active entity (e.g. IG node's own data)
            var entityA = repo.CreateEntity();
            repo.AddComponent(entityA, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            repo.AddComponent(entityA, new NetworkTransform { LastPosition = Vector3.Zero });
            repo.AddComponent(entityA, new NetworkVelocity { Value = new Vector3(1f, 0f, 0f) });
            repo.AddComponent(entityA, new NetworkAuthority(primaryOwnerId: 99, localNodeId: 1));  // no authority

            // Entity B — represents a promoted ghost entity (also Active, HasAuthority=false)
            var entityB = repo.CreateEntity();
            repo.AddComponent(entityB, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            repo.AddComponent(entityB, new NetworkTransform { LastPosition = Vector3.Zero });
            repo.AddComponent(entityB, new NetworkVelocity { Value = new Vector3(2f, 0f, 0f) });
            repo.AddComponent(entityB, new NetworkAuthority(primaryOwnerId: 88, localNodeId: 1));  // no authority

            var system = new DeadReckoningSyncSystem(driveFromNetwork: true);
            system.Execute(repo, 0.1f);
            PlaybackCommands(repo);

            // Both Active entities should have their SimTransform updated
            var tfA = repo.GetComponent<SimTransform>(entityA);
            var tfB = repo.GetComponent<SimTransform>(entityB);
            Assert.NotEqual(Vector3.Zero, tfA.Position);
            Assert.NotEqual(Vector3.Zero, tfB.Position);
        }

        // ── MODINIT-S101 Success Condition 4 ─────────────────────────────────
        // driveFromNetwork=false → Ghost lifecycle filter → only Ghost-lifecycle entities updated;
        // Active-lifecycle entities are excluded from the query.

        [Fact]
        public void Execute_DriveFromNetworkFalse_UpdatesOnlyGhostLifecycleEntity()
        {
            using var repo = CreateRepo();

            // Active entity — represents a locally-promoted or Muscle-owned entity
            var activeEntity = repo.CreateEntity();
            repo.AddComponent(activeEntity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            repo.AddComponent(activeEntity, new NetworkTransform { LastPosition = Vector3.Zero });
            repo.AddComponent(activeEntity, new NetworkVelocity { Value = new Vector3(1f, 0f, 0f) });
            repo.AddComponent(activeEntity, new NetworkAuthority(primaryOwnerId: 99, localNodeId: 1));  // no authority

            // Ghost lifecycle entity — represents an incoming remote replica not yet promoted
            var ghostEntity = repo.CreateEntity();
            repo.AddComponent(ghostEntity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            repo.AddComponent(ghostEntity, new NetworkTransform { LastPosition = Vector3.Zero });
            repo.AddComponent(ghostEntity, new NetworkVelocity { Value = new Vector3(2f, 0f, 0f) });
            repo.AddComponent(ghostEntity, new NetworkAuthority(primaryOwnerId: 88, localNodeId: 1));  // no authority
            repo.SetLifecycleState(ghostEntity, EntityLifecycle.Ghost);

            var system = new DeadReckoningSyncSystem(driveFromNetwork: false);
            system.Execute(repo, 0.1f);
            PlaybackCommands(repo);

            // Active entity must NOT be updated (excluded by Ghost lifecycle filter)
            var activeTf = repo.GetComponent<SimTransform>(activeEntity);
            Assert.Equal(Vector3.Zero, activeTf.Position);

            // Ghost lifecycle entity MUST be updated
            var ghostTf = repo.GetComponent<SimTransform>(ghostEntity);
            Assert.NotEqual(Vector3.Zero, ghostTf.Position);
        }
    }
}
