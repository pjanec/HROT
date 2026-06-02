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
    ///
    /// <para>
    /// <b>P2-T4 refactor (STR-D12).</b>
    /// The system now writes <b>only</b> <see cref="CrowdMotorIntent.Velocity"/> and
    /// no longer touches <see cref="SimTransform"/> or <see cref="SimVelocity"/>.
    /// The old position-integration tests are replaced by tests asserting position is
    /// UNCHANGED after a system execution.
    /// </para>
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
            repo.RegisterComponent<CrowdMotorIntent>();
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
            repo.AddComponent(entity, new CrowdMotorIntent());

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

        // ── T4-SC1: CrowdMotorIntent is written from GetAgentVelocity ─────────────

        /// <summary>
        /// When phase is Following, <see cref="CrowdAgentUpdateSystem"/> must write
        /// <see cref="CrowdMotorIntent.Velocity"/> equal to
        /// <see cref="IDtCrowdProvider.GetAgentVelocity"/> (STR-D12 fix).
        /// </summary>
        [Fact]
        public void Phase_Following_CrowdMotorIntentWritten()
        {
            using var repo = CreateWorld();
            var (entity, crowd, system) = CreateFollowingAgent(
                repo,
                startPos: new Vector3(0, 0, 0),
                target:   new Vector3(10, 0, 0));

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var intent = repo.GetComponent<CrowdMotorIntent>(entity);
            // After one Update tick aimed at (10,0,0), intent velocity must be non-zero in +X.
            Assert.True(intent.Velocity.X > 0f,
                $"Expected positive X velocity toward target in CrowdMotorIntent; got {intent.Velocity}");
        }

        /// <summary>
        /// The intent velocity set by the system must equal the value returned by
        /// <see cref="IDtCrowdProvider.GetAgentVelocity"/> for a known (overridden) velocity.
        /// </summary>
        [Fact]
        public void Phase_Following_IntentVelocityEqualsGetAgentVelocity()
        {
            using var repo = CreateWorld();
            var crowd = new FakeDtCrowdProvider();
            var system = new CrowdAgentUpdateSystem(crowd);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            repo.AddComponent(entity, new NavigationStatus { Phase = NavigationPhase.Following });
            repo.AddComponent(entity, default(CrowdAgent));
            repo.AddComponent(entity, new CrowdMotorIntent());

            var knownVelocity = new Vector3(3f, 1.5f, 0f);
            crowd.RegisterAgent(entity, new CrowdAgentParams
                { Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f });
            crowd.OverrideAgentVelocity(entity, knownVelocity);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var intent = repo.GetComponent<CrowdMotorIntent>(entity);
            Assert.Equal(knownVelocity.X, intent.Velocity.X, precision: 4);
            Assert.Equal(knownVelocity.Y, intent.Velocity.Y, precision: 4);
            Assert.Equal(knownVelocity.Z, intent.Velocity.Z, precision: 4);
        }

        // ── T4-SC2: SimTransform.Position is NOT mutated (STR-D12 fix) ────────────

        /// <summary>
        /// <see cref="CrowdAgentUpdateSystem"/> must NOT modify
        /// <see cref="SimTransform.Position"/> — position integration was removed in P2-T4.
        /// This test replaces the old position-integration test that verified
        /// <c>tf.Position += velocity * dt</c>.
        /// </summary>
        [Fact]
        public void Phase_Following_SimTransformPositionUnchanged()
        {
            using var repo = CreateWorld();
            var (entity, _, system) = CreateFollowingAgent(
                repo,
                startPos: new Vector3(5f, 3f, 1f),
                target:   new Vector3(10f, 0f, 0f));

            var positionBefore = repo.GetComponent<SimTransform>(entity).Position;

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.5f);   // dt = 0.5 s — would matter if integration still ran

            var positionAfter = repo.GetComponent<SimTransform>(entity).Position;

            Assert.Equal(positionBefore.X, positionAfter.X, precision: 6);
            Assert.Equal(positionBefore.Y, positionAfter.Y, precision: 6);
            Assert.Equal(positionBefore.Z, positionAfter.Z, precision: 6);
        }

        // ── T4-SC3: SimVelocity is NOT written ────────────────────────────────────

        /// <summary>
        /// <see cref="CrowdAgentUpdateSystem"/> must NOT modify <see cref="SimVelocity"/>.
        /// Under split-authority physics, SimVelocity is a result of the physics step.
        /// </summary>
        [Fact]
        public void Phase_Following_SimVelocityNotWritten()
        {
            using var repo = CreateWorld();
            var crowd = new FakeDtCrowdProvider();
            var system = new CrowdAgentUpdateSystem(crowd);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            var originalSimVel = new SimVelocity { Linear = new Vector3(99f, 0f, 0f) };
            repo.AddComponent(entity, originalSimVel);
            repo.AddComponent(entity, new NavigationStatus { Phase = NavigationPhase.Following });
            repo.AddComponent(entity, default(CrowdAgent));
            repo.AddComponent(entity, new CrowdMotorIntent());

            crowd.RegisterAgent(entity, new CrowdAgentParams
                { Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f });
            crowd.SetAgentTarget(entity, new Vector3(10f, 0f, 0f));

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // SimVelocity must retain its original value.
            var simVelAfter = repo.GetComponent<SimVelocity>(entity);
            Assert.Equal(99f, simVelAfter.Linear.X, precision: 3);
        }

        // ── T4-SC4: AwaitingTraversal suppresses CrowdMotorIntent write ───────────

        /// <summary>
        /// When Phase == AwaitingTraversal, <see cref="CrowdMotorIntent"/> must NOT be written.
        /// Animation owns locomotion during off-mesh traversal.
        /// </summary>
        [Fact]
        public void Phase_AwaitingTraversal_CrowdMotorIntentSuppressed()
        {
            using var repo = CreateWorld();
            var crowd = new FakeDtCrowdProvider();
            var system = new CrowdAgentUpdateSystem(crowd);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            repo.AddComponent(entity, new SimVelocity());
            repo.AddComponent(entity, new NavigationStatus
            {
                Phase = NavigationPhase.AwaitingTraversal,
            });
            repo.AddComponent(entity, default(CrowdAgent));
            // CrowdMotorIntent starts at zero velocity
            repo.AddComponent(entity, new CrowdMotorIntent { Velocity = Vector3.Zero });

            crowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
            });
            // Override with a non-zero velocity so we can detect if it bleeds through.
            crowd.SetAgentTarget(entity, new Vector3(10, 0, 0));

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var intent = repo.GetComponent<CrowdMotorIntent>(entity);
            // CrowdMotorIntent must NOT be overwritten — remains zero.
            Assert.Equal(0f, intent.Velocity.X, precision: 6);
            Assert.Equal(0f, intent.Velocity.Y, precision: 6);
            Assert.Equal(0f, intent.Velocity.Z, precision: 6);
        }

        // ── T4-SC5: entity without CrowdAgent tag is skipped ──────────────────────

        /// <summary>
        /// Entity without <see cref="CrowdAgent"/> tag is skipped; no intent change.
        /// </summary>
        [Fact]
        public void MissingCrowdAgentTag_EntitySkipped()
        {
            using var repo = CreateWorld();
            var crowd = new FakeDtCrowdProvider();
            var system = new CrowdAgentUpdateSystem(crowd);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(7, 0, 0) });
            repo.AddComponent(entity, new NavigationStatus { Phase = NavigationPhase.Following });
            // No CrowdAgent tag.
            var originalIntent = new CrowdMotorIntent { Velocity = new Vector3(7f, 0f, 0f) };
            repo.AddComponent(entity, originalIntent);

            crowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
            });
            crowd.SetAgentTarget(entity, new Vector3(10, 0, 0));

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // Entity has no CrowdAgent; system should skip it — intent must be unchanged.
            var intent = repo.GetComponent<CrowdMotorIntent>(entity);
            Assert.Equal(7f, intent.Velocity.X, precision: 3);
        }

        // ── T4-SC6: phase transitions — intent resumes on Following ───────────────

        /// <summary>
        /// After external code transitions Phase back to Following from AwaitingTraversal,
        /// the intent is written again on the next tick.
        /// </summary>
        [Fact]
        public void Phase_TransitionsFromAwaitingToFollowing_IntentResumes()
        {
            using var repo = CreateWorld();
            var (entity, crowd, system) = CreateFollowingAgent(
                repo,
                startPos: new Vector3(0, 0, 0),
                target:   new Vector3(10, 0, 0));

            // First tick: normal following — intent gets written.
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);
            var intentAfterFirst = repo.GetComponent<CrowdMotorIntent>(entity);
            Assert.True(intentAfterFirst.Velocity.X > 0f);

            // Simulate traversal: manually set Phase to AwaitingTraversal.
            var status = repo.GetComponent<NavigationStatus>(entity);
            status.Phase = NavigationPhase.AwaitingTraversal;
            repo.SetComponent(entity, status);
            // Zero out the intent to detect if it gets re-written.
            repo.SetComponent(entity, new CrowdMotorIntent { Velocity = Vector3.Zero });

            // Second tick: suppressed.
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);
            var intentDuringTraversal = repo.GetComponent<CrowdMotorIntent>(entity);
            Assert.Equal(0f, intentDuringTraversal.Velocity.X, precision: 6);

            // Simulate montage end: external code restores Phase to Following.
            status = repo.GetComponent<NavigationStatus>(entity);
            status.Phase = NavigationPhase.Following;
            repo.SetComponent(entity, status);

            // Third tick: intent must be written again.
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);
            var intentAfterResume = repo.GetComponent<CrowdMotorIntent>(entity);
            Assert.True(intentAfterResume.Velocity.X > 0f,
                $"Expected resumed intent after phase returns to Following; got {intentAfterResume.Velocity}");
        }
    }
}
