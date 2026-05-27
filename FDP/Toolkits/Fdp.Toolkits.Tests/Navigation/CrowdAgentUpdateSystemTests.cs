using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Navigation.Systems;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// DD-Tests-Nav §4.2 — <see cref="CrowdAgentUpdateSystem"/> unit tests.
    /// </summary>
    public class CrowdAgentUpdateSystemTests
    {
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<CrowdAgent>();
            return repo;
        }

        private static (Entity entity, FakeDtCrowdProvider crowd, CrowdAgentUpdateSystem system)
            CreateFollowingAgent(EntityRepository repo, Vector3 startPos, Vector3 target)
        {
            var crowd = new FakeDtCrowdProvider();
            var system = new CrowdAgentUpdateSystem(crowd);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = startPos });
            repo.AddComponent(entity, new SimVelocity());
            repo.AddComponent(entity, new NavigationStatus { Phase = NavigationPhase.Following });
            repo.AddComponent(entity, default(CrowdAgent));

            crowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius          = 0.4f,
                Height          = 1.8f,
                MaxSpeed        = 5f,
                MaxAcceleration = 20f,
                SeparationWeight = 2,
            });
            crowd.SetAgentTarget(entity, target);

            return (entity, crowd, system);
        }

        /// <summary>
        /// DD-Tests-Nav §4.2 row 1: Phase_Following_VelocityWritten.
        /// Normal path — SimVelocity gets the crowd output.
        /// </summary>
        [Fact]
        public void Phase_Following_VelocityWritten()
        {
            using var repo = CreateWorld();
            var (entity, crowd, system) = CreateFollowingAgent(
                repo,
                startPos: new Vector3(0, 0, 0),
                target:   new Vector3(10, 0, 0));

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var vel = repo.GetComponent<SimVelocity>(entity);
            // After one Update tick aimed at (10,0,0), velocity must be non-zero in +X.
            Assert.True(vel.Linear.X > 0f,
                $"Expected positive X velocity toward target; got {vel.Linear}");
        }

        /// <summary>
        /// DD-Tests-Nav §4.2 row 2: Phase_AwaitingTraversal_VelocitySuppressed.
        /// Phase set to AwaitingTraversal — SimVelocity must NOT be written.
        /// </summary>
        [Fact]
        public void Phase_AwaitingTraversal_VelocitySuppressed()
        {
            using var repo = CreateWorld();
            var crowd = new FakeDtCrowdProvider();
            var system = new CrowdAgentUpdateSystem(crowd);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            var originalVel = new SimVelocity { Linear = new Vector3(99, 0, 0) };
            repo.AddComponent(entity, originalVel);
            repo.AddComponent(entity, new NavigationStatus
            {
                Phase = NavigationPhase.AwaitingTraversal,
            });
            repo.AddComponent(entity, default(CrowdAgent));

            crowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
            });
            crowd.SetAgentTarget(entity, new Vector3(10, 0, 0));

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var vel = repo.GetComponent<SimVelocity>(entity);
            // SimVelocity must NOT be overwritten — original value preserved.
            Assert.Equal(99f, vel.Linear.X, precision: 3);
        }

        /// <summary>
        /// DD-Tests-Nav §4.2 row 3: MissingCrowdAgentTag_EntitySkipped.
        /// Entity without CrowdAgent tag is skipped; no velocity change.
        /// </summary>
        [Fact]
        public void MissingCrowdAgentTag_EntitySkipped()
        {
            using var repo = CreateWorld();
            var crowd = new FakeDtCrowdProvider();
            var system = new CrowdAgentUpdateSystem(crowd);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            var originalVel = new SimVelocity { Linear = new Vector3(7, 0, 0) };
            repo.AddComponent(entity, originalVel);
            repo.AddComponent(entity, new NavigationStatus { Phase = NavigationPhase.Following });
            // Deliberately NOT adding CrowdAgent tag.

            crowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
            });
            crowd.SetAgentTarget(entity, new Vector3(10, 0, 0));

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // Entity has no CrowdAgent; system should skip it entirely.
            var vel = repo.GetComponent<SimVelocity>(entity);
            Assert.Equal(7f, vel.Linear.X, precision: 3);
        }

        /// <summary>
        /// DD-Tests-Nav §4.2 row 4: Phase_TransitionsFromAwaitingToFollowing_VelocityResumes.
        /// After external code transitions Phase back to Following, velocity is written again.
        /// </summary>
        [Fact]
        public void Phase_TransitionsFromAwaitingToFollowing_VelocityResumes()
        {
            using var repo = CreateWorld();
            var (entity, crowd, system) = CreateFollowingAgent(
                repo,
                startPos: new Vector3(0, 0, 0),
                target:   new Vector3(10, 0, 0));

            // First tick: normal following — velocity gets written.
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);
            var velAfterFirst = repo.GetComponent<SimVelocity>(entity);
            Assert.True(velAfterFirst.Linear.X > 0f);

            // Simulate traversal: manually set Phase to AwaitingTraversal.
            var status = repo.GetComponent<NavigationStatus>(entity);
            status.Phase = NavigationPhase.AwaitingTraversal;
            repo.SetComponent(entity, status);

            // Second tick: suppressed.
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // Simulate montage end: external code restores Phase to Following.
            status = repo.GetComponent<NavigationStatus>(entity);
            status.Phase = NavigationPhase.Following;
            repo.SetComponent(entity, status);

            // Third tick: velocity must be written again.
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);
            var velAfterResume = repo.GetComponent<SimVelocity>(entity);
            Assert.True(velAfterResume.Linear.X > 0f,
                $"Expected resumed velocity after phase returns to Following; got {velAfterResume.Linear}");
        }
    }
}
