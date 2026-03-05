using System.Numerics;
using Bagira.IG.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.IG.Tests
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
    }
}
