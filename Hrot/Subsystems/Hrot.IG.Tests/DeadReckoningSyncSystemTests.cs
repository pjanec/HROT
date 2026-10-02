using System.Numerics;
using Hrot.Common.Systems;
using Fdp.Core;
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

        /// <summary>
        /// Puts the world's simulation clock at <paramref name="totalTimeSeconds"/>.
        ///
        /// <para>
        /// Dead reckoning ages a sample as <c>simNow - NetworkTransform.SimStamp</c> (CE-211), so a
        /// rail that wants to observe any extrapolation has to say how far the clock has moved since
        /// the sample was stamped. Before CE-211 the system used the frame delta as a stand-in for
        /// that age, which is why these rails never needed a clock — and also why two nodes at
        /// different frame rates disagreed.
        /// </para>
        /// </summary>
        private static void AdvanceSimClockTo(EntityRepository repo, double totalTimeSeconds)
        {
            repo.SetSingletonUnmanaged(new GlobalTime
            {
                TotalTime = totalTimeSeconds,
                DeltaTime = 1f / 60f,
                TimeScale = 1f,
            });
        }

        private static void PlaybackCommands(EntityRepository repo)
        {
            var view = (ISimulationView)repo;
            if (view.GetCommandBuffer() is EntityCommandBuffer ecb)
                ecb.Playback(repo);
        }

        /// <summary>
        /// CE-211 rule R2 — the anchor must NOT move between samples.
        ///
        /// <para>
        /// This assertion is the inverse of the one it replaces. The old rail asserted that after
        /// one <c>Execute</c> the <c>NetworkTransform</c> had advanced to 0.5 — i.e. it pinned the
        /// accumulator write as correct behaviour. That write is what made the extrapolation
        /// frame-rate dependent: each frame projected from the previous frame's guess rather than
        /// from the received sample, so a node at 120 fps drifted away from one at 30 fps and
        /// neither matched the publisher. Only ingress may write this component now, so the rail
        /// asserts it is untouched.
        /// </para>
        /// </summary>
        [Fact]
        public void Execute_DoesNotAdvanceTheNetworkTransformAnchor()
        {
            using var repo = CreateRepo();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            repo.AddComponent(entity, new NetworkTransform { LastPosition = Vector3.Zero, SimStamp = 0.0 });
            repo.AddComponent(entity, new NetworkVelocity { Value = new Vector3(0f, 5f, 0f) });
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));

            // A full second of sim time since the sample — plenty of age to project through.
            AdvanceSimClockTo(repo, 1.0);

            var system = new DeadReckoningSyncSystem();
            system.Execute(repo, 0.1f);
            PlaybackCommands(repo);
            system.Execute(repo, 0.1f);
            PlaybackCommands(repo);

            var netTf = repo.GetComponent<NetworkTransform>(entity);
            Assert.Equal(Vector3.Zero, netTf.LastPosition);
            Assert.Equal(0.0, netTf.SimStamp);
        }

        /// <summary>
        /// CE-211 rules R3+R4 — the target is <c>LastPosition + Vel * (simNow - SimStamp)</c>, so the
        /// projection depends on the sample's AGE and not on how many frames have been rendered.
        /// </summary>
        [Fact]
        public void Execute_ProjectsBySampleAge_NotByFrameCount()
        {
            using var repo = CreateRepo();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            repo.AddComponent(entity, new NetworkTransform { LastPosition = Vector3.Zero, SimStamp = 1.0 });
            repo.AddComponent(entity, new NetworkVelocity { Value = new Vector3(0f, 10f, 0f) });
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));

            // 0.5 s of age => the target is 5 m north of the sample, whatever the frame rate.
            AdvanceSimClockTo(repo, 1.5);

            // A full blend (deltaTime * SmoothingRate == 1) lands SimTransform exactly on the target,
            // which lets the rail assert the projection itself rather than a point along the way.
            var system = new DeadReckoningSyncSystem(driveFromNetwork: true, smoothingRate: 10f);
            system.Execute(repo, 0.1f);
            PlaybackCommands(repo);

            var tf = repo.GetComponent<SimTransform>(entity);
            Assert.Equal(5f, tf.Position.Y, 3);
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

            // Both samples are 0.5 s old, so both have a target to move toward (CE-211 R3).
            AdvanceSimClockTo(repo, 0.5);

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

            // Both samples are 0.5 s old; the lifecycle filter — not the age — is what this rail tests.
            AdvanceSimClockTo(repo, 0.5);

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
