using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Hrot.CGF.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MissionAdapterSystem"/> (TASK-TI004).
    /// Verifies that the system publishes <see cref="AssignTacticalIntentEvent"/> when a valid
    /// BehaviorId is present, and publishes nothing when BehaviorId is empty/whitespace.
    /// </summary>
    public class MissionAdapterSystemTests
    {
        // -- Helpers ----------------------------------------------------------

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<MissionPlanQueue>();
            repo.RegisterComponent<BehaviorState>();
            repo.RegisterComponent<Hrot.CGF.Components.MissionAdapterState>();
            repo.RegisterManagedComponent<ActiveMissionPlan>();
            return repo;
        }

        private static Entity CreateMissionEntity(EntityRepository repo, string behaviorId, string behaviorParams = "{}")
        {
            var entity = repo.CreateEntity();

            // Add the struct components
            var queue = new MissionPlanQueue { PhaseCount = 1, CurrentPhase = 0 };
            Span<MissionPhase> phases = queue.Phases;
            phases[0] = new MissionPhase { BehaviorId = 0 };
            repo.AddComponent(entity, queue);

            repo.AddComponent(entity, new BehaviorState());

            // Set the managed ActiveMissionPlan
            repo.SetManagedComponent(entity, new ActiveMissionPlan
            {
                Plan = new DomainMissionPlan
                {
                    Tasks = new List<DomainMissionTask>
                    {
                        new DomainMissionTask
                        {
                            BehaviorName     = behaviorId,
                            BehaviorParams = behaviorParams,
                        }
                    }
                }
            });

            return entity;
        }

        // -- SC-1: valid BehaviorId -> AssignTacticalIntentEvent published -----

        /// <summary>
        /// SC-1: Entity with a valid BehaviorId.
        /// After Execute, one <see cref="AssignTacticalIntentEvent"/> is published with the
        /// correct IntentId and Entity; no <see cref="AssignBehaviorEvent"/> is emitted.
        /// </summary>
        [Fact]
        public void Execute_ValidBehaviorId_PublishesAssignTacticalIntentEvent()
        {
            using var repo = CreateWorld();
            var entity = CreateMissionEntity(repo, "WanderMilitary");
            var system = new MissionAdapterSystem();

            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            var intentEvents   = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();
            var behaviorEvents = repo.Bus.ReadManaged<AssignBehaviorEvent>();

            Assert.Single(intentEvents);
            Assert.Equal("WanderMilitary", intentEvents[0].IntentId);
            Assert.Equal(entity, intentEvents[0].Entity);
            Assert.Empty(behaviorEvents);
        }

        // -- SC-3: empty BehaviorId -> no event published ----------------------

        /// <summary>
        /// SC-3: Entity with an empty BehaviorId.
        /// After Execute, no <see cref="AssignTacticalIntentEvent"/> is published.
        /// </summary>
        [Fact]
        public void Execute_EmptyBehaviorId_NoEventPublished()
        {
            using var repo = CreateWorld();
            CreateMissionEntity(repo, string.Empty);
            var system = new MissionAdapterSystem();

            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            var intentEvents = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();
            Assert.Empty(intentEvents);
        }
    }
}
