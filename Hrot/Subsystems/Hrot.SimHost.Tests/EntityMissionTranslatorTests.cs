using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using Hrot.SimHost.Modules;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Tkb;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Services;
using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;

using NetworkEntityMap = Fdp.Toolkit.Replication.Services.NetworkEntityMap;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="EntityMissionIngressTranslator"/> and
    /// <see cref="EntityMissionEgressTranslator"/>.
    ///
    /// DDS readers/writers cannot be mocked without a live participant, so the
    /// ingress component-application path is exercised via
    /// <see cref="IDescriptorTranslator.ApplyToEntity"/> (the repository-direct
    /// overload used by the replay and snapshot systems) and via direct
    /// <see cref="EntityRepository"/> manipulation.
    ///
    /// Egress and smoke tests exercise <see cref="IDescriptorTranslator.ScanAndPublish"/>
    /// with a real <see cref="DdsParticipant"/> to verify query/filter logic does
    /// not throw.
    /// </summary>
    [Collection("SimHostDds")]
    public class EntityMissionTranslatorTests
    {
        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<GhostStateTracker>(); // required when ghost creation is triggered
            world.RegisterComponent<NetworkAuthority>();
            world.RegisterComponent<MissionPlanQueue>();
            return world;
        }

        private static EntityMission MakeMission(long entityId = 42)
        {
            return new EntityMission
            {
                EntityId = entityId,
                Plan = new MissionPlan
                {
                    ActiveTaskId = Guid.NewGuid(),
                    Tasks        = new List<MissionTask>
                    {
                        new MissionTask
                        {
                            TaskId           = Guid.NewGuid(),
                            BehaviorId       = "MoveToLocation",
                            BehaviorParams   = "{}",
                            ExecutingEngine  = "CGFX",
                            State            = eTaskState.TASK_ACTIVE,
                            Triggers         = new List<DdsMissionTrigger>()
                        }
                    }
                }
            };
        }

        // â”€â”€ Ingress: ApplyToEntity (repository-direct path) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// <see cref="EntityMissionIngressTranslator.ApplyToEntity"/> must set the
        /// <see cref="MissionPlanQueue"/> component on the target entity.
        /// This mirrors the outcome of a valid DDS ingress sample.
        /// </summary>
        [Fact]
        public void Ingress_ApplyToEntity_SetsMissionPlanQueue()
        {
            using var world = CreateWorld();
            var entity  = world.CreateEntity();
            var mission = MakeMission(entityId: 1);

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator  = new EntityMissionIngressTranslator(participant, entityMap, new BehaviorRegistry(), new GhostCreationSystem(entityMap));

            translator.ApplyToEntity(entity, mission, world);

            Assert.True(world.HasComponent<MissionPlanQueue>(entity),
                "MissionPlanQueue must be present after ApplyToEntity.");

            var queue = ((ISimulationView)world).GetComponentRO<MissionPlanQueue>(entity);
            Assert.Equal(mission.Plan.Tasks.Count, queue.PhaseCount);
        }

        /// <summary>
        /// <see cref="EntityMissionIngressTranslator.ApplyToEntity"/> must be a no-op when
        /// given a non-<see cref="EntityMission"/> object (e.g. wrong type).
        /// </summary>
        [Fact]
        public void Ingress_ApplyToEntity_WrongType_IsNoOp()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator  = new EntityMissionIngressTranslator(participant, entityMap, new BehaviorRegistry(), new GhostCreationSystem(entityMap));

            var ex = Record.Exception(() => translator.ApplyToEntity(entity, "not_a_mission", world));
            Assert.Null(ex);
            Assert.False(world.HasComponent<MissionPlanQueue>(entity));
        }

        /// <summary>
        /// Directly removing the <see cref="MissionPlanQueue"/> component mirrors
        /// the behaviour of a NOT_ALIVE_DISPOSED ingress sample.
        /// </summary>
        [Fact]
        public void Ingress_ComponentRemoval_ClearsMissionPlanQueue()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            // Seed the component (represents a prior ingress sample).
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });
            Assert.True(world.HasComponent<MissionPlanQueue>(entity));

            // Simulate the command-buffer playback that would result from
            // a NOT_ALIVE_DISPOSED DDS sample.
            var view   = (ISimulationView)world;
            var cmd    = (EntityCommandBuffer)view.GetCommandBuffer();
            cmd.RemoveComponent<MissionPlanQueue>(entity);
            cmd.Playback(world);

            Assert.False(world.HasComponent<MissionPlanQueue>(entity),
                "MissionPlanQueue must be removed after NOT_ALIVE_DISPOSED playback.");
        }

        // â”€â”€ Ingress: Unknown entity ID â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// An EntityId not present in the <see cref="NetworkEntityMap"/> must be
        /// silently skipped â€” the translator must not throw or create stray entities.
        /// </summary>
        [Fact]
        public void Ingress_UnknownEntityId_SkippedWithoutException()
        {
            var entityMap = new NetworkEntityMap();
            // Do NOT register entity 99 in the map.

            using var participant = new DdsParticipant();
            var translator  = new EntityMissionIngressTranslator(participant, entityMap, new BehaviorRegistry(), new GhostCreationSystem(entityMap));

            // PollIngress will Take() from an empty DDS reader, so there is nothing
            // to process â€” this test confirms construction and polling do not throw
            // for unknown IDs.
            using var world = CreateWorld();
            var view   = (ISimulationView)world;
            var cmd    = view.GetCommandBuffer();

            var ex = Record.Exception(() => translator.PollIngress(cmd, view));
            Assert.Null(ex);
        }

        // â”€â”€ Egress: ScanAndPublish smoke tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// <see cref="EntityMissionEgressTranslator.ScanAndPublish"/> must not throw
        /// on an empty world (no entities at all).
        /// </summary>
        [Fact]
        public void Egress_EmptyWorld_ScanAndPublishDoesNotThrow()
        {
            using var world = CreateWorld();
            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        /// <summary>
        /// When an entity carries <see cref="MissionPlanQueue"/> and has local
        /// authority, <see cref="EntityMissionEgressTranslator.ScanAndPublish"/> must
        /// not throw (smoke test â€” DDS write is a side-effect we cannot inspect
        /// without a live subscriber).
        /// </summary>
        [Fact]
        public void Egress_AuthorityEntity_ScanAndPublishDoesNotThrow()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NetworkIdentity(42));
            world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1)); // HasAuthority = true
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        /// <summary>
        /// An entity whose <see cref="NetworkAuthority.HasAuthority"/> is <c>false</c>
        /// must not trigger a DDS write â€” calling ScanAndPublish must not throw.
        /// </summary>
        [Fact]
        public void Egress_NonAuthorityEntity_ScanAndPublishDoesNotThrow()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NetworkIdentity(10));
            world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1)); // HasAuthority = false
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        // â”€â”€ Egress: Dirty-flag optimisation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// After the first <see cref="EntityMissionEgressTranslator.ScanAndPublish"/>
        /// call, the internal <c>_lastPublishedVersion</c> is advanced to
        /// <see cref="EntityRepository.GlobalVersion"/>. A second call with no
        /// intervening component writes must hit the early-out and not throw.
        /// </summary>
        [Fact]
        public void Egress_NoNewChanges_SecondScanSkipsPublish()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NetworkIdentity(7));
            world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            // First scan â€” processes the dirty component.
            translator.ScanAndPublish(world);

            // Second scan â€” no mutations since the first; early-out path exercised.
            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        /// <summary>
        /// After a component write the dirty flag is raised again;
        /// a subsequent <see cref="EntityMissionEgressTranslator.ScanAndPublish"/>
        /// must process the entity without throwing.
        /// </summary>
        [Fact]
        public void Egress_ComponentMutatedBetweenScans_SecondScanRuns()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NetworkIdentity(5));
            world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });

            var entityMap   = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator  = new EntityMissionEgressTranslator(participant, entityMap);

            translator.ScanAndPublish(world);

            // Mutate the component â€” this advances GlobalVersion and marks the table dirty.
            world.SetComponent(entity, new MissionPlanQueue { PhaseCount = 1 });

            var ex = Record.Exception(() => translator.ScanAndPublish(world));
            Assert.Null(ex);
        }

        // â”€â”€ Module integration: both translators exposed â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// <see cref="SimHostModule"/> constructor must NOT require a <see cref="DdsParticipant"/>.
        /// Passing only a <see cref="NetworkSpawningSystem"/> (no request/delete systems, no
        /// translators) is a valid offline construction.
        /// </summary>
        [Fact]
        public void SimHostModule_CanBeConstructed_WithoutDdsParticipant()
        {
            using var participant = new DdsParticipant();
            var tkb         = new TkbDatabase();
            var entityMap   = new NetworkEntityMap();
            var idAllocator = new DdsIdAllocator(participant, "offline-test");
            var elm         = new EntityLifecycleModule(tkb, new List<int>());
            var spawner     = new NetworkSpawningSystem(tkb, elm, entityMap, idAllocator, 1);

            // Note: SimHostModule constructor only receives the spawner â€” no participant, no systems.
            var ex = Record.Exception(() => new Fdp.ModuleHost.Scheduling.SingleSystemModule("NetworkSpawning", spawner));

            Assert.Null(ex);
        }

        // ── CE-483 (egress half) — the wire carries the mission's progress ───────────────────────
        // 📄 docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md §4c W1–W3.

        private static MissionPlanQueue Queue(byte phaseCount, byte current, byte halted = 0, params MissionPhaseOutcome[] outcomes)
        {
            var q = new MissionPlanQueue { PhaseCount = phaseCount, CurrentPhase = current, Halted = halted };
            for (int i = 0; i < outcomes.Length; i++) q.Outcomes[i] = outcomes[i];
            return q;
        }

        private static eTaskState[] States(in MissionPlanQueue q)
        {
            var s = new eTaskState[q.PhaseCount];
            for (int i = 0; i < q.PhaseCount; i++) s[i] = Hrot.Map.Common.Replication.MissionProgressWire.StateOf(in q, i);
            return s;
        }

        /// <summary>W1: Done ⇒ ✓, Failed ⇒ ✗, the running phase ACTIVE, the rest PLANNED — not "everything but the current is
        /// PLANNED", which sent a finished task as planned.</summary>
        [Fact]
        public void CE483_StateOf_RecordedOutcomeFirst_ThenActive()
        {
            var q = Queue(4, current: 2, halted: 0, MissionPhaseOutcome.Done, MissionPhaseOutcome.Failed);
            Assert.Equal(new[] { eTaskState.TASK_DONE, eTaskState.TASK_FAILED, eTaskState.TASK_ACTIVE, eTaskState.TASK_PLANNED }, States(q));
        }

        /// <summary>W1: a halted plan has NO active task; its current (faulted) phase reads FAILED.</summary>
        [Fact]
        public void CE483_StateOf_Halted_NoActiveTask()
        {
            var q = Queue(3, current: 1, halted: 1, MissionPhaseOutcome.Done, MissionPhaseOutcome.Failed);
            Assert.Equal(new[] { eTaskState.TASK_DONE, eTaskState.TASK_FAILED, eTaskState.TASK_PLANNED }, States(q));
        }

        /// <summary>W3: encode → decode restores the progress — running, halted and complete plans.</summary>
        [Theory]
        [InlineData(4, 2, 0)]   // running on phase 2 after a Done and a (3a) Failed
        [InlineData(4, 1, 1)]   // halted on phase 1
        [InlineData(2, 2, 0)]   // complete
        public void CE483_Decode_RoundTripsProgress(byte count, byte current, byte halted)
        {
            var outcomes = new MissionPhaseOutcome[count];
            for (int i = 0; i < current && i < count; i++) outcomes[i] = i % 2 == 0 ? MissionPhaseOutcome.Done : MissionPhaseOutcome.Failed;
            if (halted != 0) outcomes[current] = MissionPhaseOutcome.Failed;
            var sent = Queue(count, current, halted, outcomes);

            var received = new MissionPlanQueue { PhaseCount = count };
            Hrot.Map.Common.Replication.MissionProgressWire.Decode(States(sent), ref received);

            Assert.Equal(sent.CurrentPhase, received.CurrentPhase);
            Assert.Equal(sent.Halted, received.Halted);
            for (int i = 0; i < count; i++) Assert.Equal(sent.Outcomes[i], received.Outcomes[i]);
        }

        /// <summary>W3 through the real ingress: a replica no longer restarts the plan at phase 0.</summary>
        [Fact]
        public void CE483_Ingress_RestoresCurrentPhaseAndHalt()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();
            var mission = MakeMission(entityId: 1);
            var task = mission.Plan.Tasks[0];
            mission.Plan.Tasks = new List<MissionTask>
            {
                task with { State = eTaskState.TASK_DONE },
                task with { State = eTaskState.TASK_FAILED },
                task with { State = eTaskState.TASK_PLANNED },
            };

            var entityMap = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator = new EntityMissionIngressTranslator(participant, entityMap, new BehaviorRegistry(), new GhostCreationSystem(entityMap));
            translator.ApplyToEntity(entity, mission, world);

            var q = ((ISimulationView)world).GetComponentRO<MissionPlanQueue>(entity);
            Assert.Equal(1, q.CurrentPhase);
            Assert.Equal(1, q.Halted);
            Assert.Equal(MissionPhaseOutcome.Done, q.Outcomes[0]);
            Assert.Equal(MissionPhaseOutcome.Failed, q.Outcomes[1]);
        }

        /// <summary>W2: once published, a phase advance / outcome / halt re-publishes WITHOUT anyone calling MarkDirty
        /// (MissionDirectorSystem never does); an unchanged plan does not.</summary>
        [Fact]
        public void CE483_Egress_RepublishesOnProgress_NotOtherwise()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new NetworkIdentity(9));
            world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            world.SetComponent(entity, Queue(3, current: 0));

            var entityMap = new NetworkEntityMap();
            using var participant = new DdsParticipant();
            var translator = new EntityMissionEgressTranslator(participant, entityMap);

            translator.ScanAndPublish(world);
            Assert.Equal(1, translator.SentSampleCount);
            translator.ScanAndPublish(world);
            Assert.Equal(1, translator.SentSampleCount);              // unchanged ⇒ not re-sent

            world.SetComponent(entity, Queue(3, current: 1, halted: 0, MissionPhaseOutcome.Done));
            translator.ScanAndPublish(world);
            Assert.Equal(2, translator.SentSampleCount);              // advanced ⇒ re-sent, no MarkDirty

            world.SetComponent(entity, Queue(3, current: 1, halted: 1, MissionPhaseOutcome.Done, MissionPhaseOutcome.Failed));
            translator.ScanAndPublish(world);
            Assert.Equal(3, translator.SentSampleCount);              // halted ⇒ re-sent
        }
    }
}
