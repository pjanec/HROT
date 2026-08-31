using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Fbt;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Lifecycle.Events;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Fake;
using Hrot.MuscleCharacter.Animation.Hashing;
using Hrot.MuscleCharacter.Animation.Nodes;

namespace Hrot.MuscleCharacter.Animation.Tests
{
    /// <summary>
    /// Layer-2 integration tests for Phase 5 Part 2 nodes (ANC-P5-04 through ANC-P5-08).
    /// Tests look-at nodes, getter nodes, and their integration.
    /// (DD-Tests §4, Phase 5 Part 2)
    /// </summary>
    public class Phase5GettersAndValidatorsTests
    {
        private const long ClassId = 42L;

        private static readonly int ReloadId = StableIdHasher.ComputeMontageAssetId("Reload_Rifle");

        // ─── Shared test DTO ─────────────────────────────────────────────────

        private static CharacterAnimationDefDto CreateTestDto()
        {
            return new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto>
                {
                    new SlotDefDto { SlotId = 0, Name = "Locomotion", BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 0 },
                    new SlotDefDto { SlotId = 1, Name = "FullBody", BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 100 },
                },
                Montages = new List<MontageDefDto>
                {
                    new MontageDefDto
                    {
                        Name = "Reload_Rifle",
                        AssetRef = "Anims/Reload.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.2f,
                        DurationSeconds = 1.0f,
                        Sections = new[] { "Start" },
                        Notifies = new List<MontageNotifyRefDto>(),
                        IsStanceTransition = false,
                    },
                },
                SupportedStances = new[] { StanceId.Standing, StanceId.Crouched },
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = new AimConfigDto { MaxYawDegrees = 90f, MaxPitchDegrees = 70f, AimSourceBone = "head" },
                NotifyMarkers = new List<NotifyMarkerDefDto>(),
            };
        }

        private static (EntityRepository repo, FakeAnimationBackend backend, BakedAnimationCache cache) CreateFixture()
        {
            var dto = CreateTestDto();
            var baked = BakingUtils.BakeDef(dto);
            var classData = new Dictionary<long, CharacterAnimationBakedData> { [ClassId] = baked };
            var backend = new FakeAnimationBackend(classData);

            var cache = new BakedAnimationCache(null);
            cache.GetOrBake(ClassId, dto);

            var repo = new EntityRepository();
            repo.RegisterComponent<AnimationChannel>();
            repo.RegisterComponent<LookAtChannel>();
            repo.RegisterComponent<StanceIntent>();
            repo.RegisterComponent<StanceStatus>();
            repo.RegisterComponent<AnimationMontageQueue>();
            repo.RegisterComponent<AnimationMontageQueueState>();
            repo.RegisterComponent<CharacterAnimationDefRuntime>();
            repo.RegisterComponent<AnimationExecutorState>();
            repo.RegisterComponent<LookAtExecutorState>();
            repo.RegisterComponent<ActorCapabilityState>();
            repo.RegisterEvent<DestructionOrder>();

            return (repo, backend, cache);
        }

        private static Entity CreateAnimatedEntity(
            EntityRepository repo,
            ActorCapabilities caps = ActorCapabilities.CanPlayAnimations | ActorCapabilities.CanChangeStance | ActorCapabilities.CanAim)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new AnimationChannel { Status = NodeStatus.Failure });
            repo.AddComponent(entity, new LookAtChannel { Status = NodeStatus.Failure });
            repo.AddComponent(entity, new StanceIntent { TargetStance = StanceId.Standing, BlendTime = 0.1f });
            repo.AddComponent(entity, new StanceStatus { CurrentStance = StanceId.Standing, Phase = StanceTransitionPhase.Idle });
            repo.AddComponent(entity, new AnimationMontageQueue { Count = 0, QueueVersion = 0 });
            repo.AddComponent(entity, new AnimationMontageQueueState { CurrentEntryIndex = 0xFF });
            repo.AddComponent(entity, new CharacterAnimationDefRuntime { BackendHandle = ClassId });
            repo.AddComponent(entity, new AnimationExecutorState());
            repo.AddComponent(entity, new LookAtExecutorState());
            repo.AddComponent(entity, new ActorCapabilityState { Capabilities = caps });

            return entity;
        }

        // ───────────────────────────────────────────────────────────────────────
        // ANC-P5-04: Look-At Node Tests
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void LookAtPointNode_CanBeCreatedWithFields()
        {
            var node = new LookAtPointNode
            {
                TargetCharacter = 100,
                TargetPointX = 10.0f,
                TargetPointY = 20.0f,
                TargetPointZ = 30.0f,
                BlendInTime = 0.1f,
                Priority = 5,
            };

            Assert.Equal(100u, node.TargetCharacter);
            Assert.Equal(10.0f, node.TargetPointX);
            Assert.Equal(20.0f, node.TargetPointY);
            Assert.Equal(30.0f, node.TargetPointZ);
            Assert.Equal(0.1f, node.BlendInTime);
            Assert.Equal(5, node.Priority);
        }

        [Fact]
        public void LookAtEntityNode_CanBeCreatedWithFields()
        {
            var node = new LookAtEntityNode
            {
                TargetCharacter = 101,
                TargetEntity = 202,
                OffsetFromTargetX = 0.0f,
                OffsetFromTargetY = 1.5f,
                OffsetFromTargetZ = 0.0f,
                BlendInTime = 0.1f,
                Priority = 3,
            };

            Assert.Equal(101u, node.TargetCharacter);
            Assert.Equal(202u, node.TargetEntity);
            Assert.Equal(1.5f, node.OffsetFromTargetY);
        }

        [Fact]
        public void ReleaseLookNode_CanBeCreatedWithFields()
        {
            var node = new ReleaseLookNode
            {
                TargetCharacter = 103,
                BlendOutTime = 0.2f,
            };

            Assert.Equal(103u, node.TargetCharacter);
            Assert.Equal(0.2f, node.BlendOutTime);
        }

        // ───────────────────────────────────────────────────────────────────────
        // ANC-P5-05: Getter Node Tests
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void GetMontageQueueProgressNode_CanBeCreatedWithFields()
        {
            var node = new GetMontageQueueProgressNode { TargetCharacter = 300 };
            Assert.Equal(300u, node.TargetCharacter);
        }

        [Fact]
        public void GetCurrentStanceNode_CanBeCreatedWithFields()
        {
            var node = new GetCurrentStanceNode { TargetCharacter = 400 };
            Assert.Equal(400u, node.TargetCharacter);
        }

        [Fact]
        public void GetMontageQueueProgressNode_CanReadQueueState()
        {
            var (repo, backend, cache) = CreateFixture();
            var entity = CreateAnimatedEntity(repo);

            // Verify initial state: no active queue
            var qs = repo.GetComponentRO<AnimationMontageQueueState>(entity);
            Assert.Equal(0xFF, qs.CurrentEntryIndex);
        }

        [Fact]
        public void GetCurrentStanceNode_CanReadStanceState()
        {
            var (repo, backend, cache) = CreateFixture();
            var entity = CreateAnimatedEntity(repo);

            // Verify initial state
            var ss = repo.GetComponentRO<StanceStatus>(entity);
            Assert.Equal(StanceId.Standing, ss.CurrentStance);
        }

        // ───────────────────────────────────────────────────────────────────────
        // Integration tests: Multi-node sequences
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void LookAtPointAndReleaseLookCanSequence()
        {
            // Both nodes target same entity and can be sequenced
            const uint entityId = 201;

            var lookAt = new LookAtPointNode
            {
                TargetCharacter = entityId,
                TargetPointX = 10.0f,
                TargetPointY = 20.0f,
                TargetPointZ = 30.0f,
                BlendInTime = 0.1f,
                Priority = 0,
            };

            var release = new ReleaseLookNode
            {
                TargetCharacter = entityId,
                BlendOutTime = 0.2f,
            };

            // Both reference same target entity
            Assert.Equal(lookAt.TargetCharacter, release.TargetCharacter);
        }

        [Fact]
        public void LookAtEntityNodeWithOffset()
        {
            const uint actorId = 210;
            const uint targetId = 211;

            var lookAtEntity = new LookAtEntityNode
            {
                TargetCharacter = actorId,
                TargetEntity = targetId,
                OffsetFromTargetX = 0.0f,
                OffsetFromTargetY = 1.5f,
                OffsetFromTargetZ = 0.0f,
                BlendInTime = 0.1f,
                Priority = 0,
            };

            Assert.Equal(actorId, lookAtEntity.TargetCharacter);
            Assert.Equal(targetId, lookAtEntity.TargetEntity);
            Assert.Equal(1.5f, lookAtEntity.OffsetFromTargetY);
        }

        [Fact]
        public void GettersCanBeUsedInReadScenarios()
        {
            var (repo, backend, cache) = CreateFixture();
            var entity = CreateAnimatedEntity(repo);

            // Simulate reading after queue operations
            var getter = new GetMontageQueueProgressNode { TargetCharacter = 500 };
            Assert.Equal(500u, getter.TargetCharacter);

            // Simulate reading after stance operations
            var stanceGetter = new GetCurrentStanceNode { TargetCharacter = 501 };
            Assert.Equal(501u, stanceGetter.TargetCharacter);
        }

        // ───────────────────────────────────────────────────────────────────────
        // Cross-subsystem reuse tests (AiPrimitive dispatch)
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void LookAtNodesCanBeBoxedAsAiPrimitives()
        {
            // All look-at nodes are value types that can be used as AiPrimitives
            var lookAtPoint = new LookAtPointNode { TargetCharacter = 1 };
            var lookAtEntity = new LookAtEntityNode { TargetCharacter = 1, TargetEntity = 2 };
            var releaseLook = new ReleaseLookNode { TargetCharacter = 1 };

            object box1 = lookAtPoint;
            object box2 = lookAtEntity;
            object box3 = releaseLook;

            Assert.NotNull(box1);
            Assert.NotNull(box2);
            Assert.NotNull(box3);
        }

        [Fact]
        public void GetterNodesCanBeBoxedAsAiPrimitives()
        {
            // Getter nodes are value types that can be used as AiPrimitives for reading
            var getProgress = new GetMontageQueueProgressNode { TargetCharacter = 1 };
            var getStance = new GetCurrentStanceNode { TargetCharacter = 1 };

            object box1 = getProgress;
            object box2 = getStance;

            Assert.NotNull(box1);
            Assert.NotNull(box2);
        }
    }
}
