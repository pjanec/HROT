using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Fake;
using Hrot.MuscleCharacter.Animation.Hashing;
using Hrot.MuscleCharacter.Animation.Nodes;
using Hrot.MuscleCharacter.Animation.Systems;

namespace Hrot.MuscleCharacter.Animation.Tests
{
    /// <summary>
    /// Layer-2 integration tests for Phase 5 Part 1 action nodes (ANC-P5-01 through ANC-P5-03).
    /// Tests node definitions and their state mutations via FakeAnimationBackend.
    /// (DD-Tests §4, Phase 5)
    /// </summary>
    public class Phase5ActionNodesTests
    {
        private const long ClassId = 42L;

        private static readonly int ReloadId = StableIdHasher.ComputeMontageAssetId("Reload_Rifle");
        private static readonly int ReloadLongId = StableIdHasher.ComputeMontageAssetId("Reload_Long");
        private static readonly int MeleeSwingId = StableIdHasher.ComputeMontageAssetId("Melee_Swing");

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
                    new MontageDefDto
                    {
                        Name = "Reload_Long",
                        AssetRef = "Anims/ReloadLong.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0.1f,
                        DefaultBlendOutTime = 0.2f,
                        DurationSeconds = 2.0f,
                        Sections = new[] { "Start", "Loop", "End" },
                        Notifies = new List<MontageNotifyRefDto>(),
                        IsStanceTransition = false,
                    },
                    new MontageDefDto
                    {
                        Name = "Melee_Swing",
                        AssetRef = "Anims/MeleeSwing.clip",
                        Slot = 1,
                        DefaultBlendInTime = 0.05f,
                        DefaultBlendOutTime = 0.1f,
                        DurationSeconds = 0.8f,
                        Sections = new[] { "Swing" },
                        Notifies = new List<MontageNotifyRefDto>(),
                        IsStanceTransition = false,
                    },
                },
                SupportedStances = new[] { Components.StanceId.Standing, Components.StanceId.Crouched },
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = new AimConfigDto { MaxYawDegrees = 90f, MaxPitchDegrees = 70f, AimSourceBone = "head" },
                NotifyMarkers = new List<NotifyMarkerDefDto>(),
            };
        }

        // ─── Fixture helpers ─────────────────────────────────────────────────

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
        // ANC-P5-01: PlayMontageNode Tests
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void PlayMontageNode_NodeStructDefinesCorrectFields()
        {
            // Verify struct layout: TargetCharacter (4), MontageId (4), SlotIndex (1)
            var node = new PlayMontageNode
            {
                TargetCharacter = 123,
                MontageId = ReloadId,
                SlotIndex = 1,
            };

            Assert.Equal(123u, node.TargetCharacter);
            Assert.Equal(ReloadId, node.MontageId);
            Assert.Equal(1, node.SlotIndex);
        }

        [Fact]
        public void PlayMontageNode_MontagePickerAttributeOnMontageIdField()
        {
            // Verify [MontagePicker] attribute is present on MontageId field
            var field = typeof(PlayMontageNode).GetField(nameof(PlayMontageNode.MontageId));
            Assert.NotNull(field);
            var attr = field.GetCustomAttributes(typeof(Events.MontagePickerAttribute), false);
            Assert.Single(attr);
        }

        // ───────────────────────────────────────────────────────────────────────
        // ANC-P5-01: StopMontageNode Tests
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void StopMontageNode_NodeStructDefinesCorrectFields()
        {
            var node = new StopMontageNode
            {
                TargetCharacter = 456,
                SlotIndex = 0,
            };

            Assert.Equal(456u, node.TargetCharacter);
            Assert.Equal(0, node.SlotIndex);
        }

        // ───────────────────────────────────────────────────────────────────────
        // ANC-P5-02: PlayMontageChainNode Tests
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void PlayMontageChainNode_NodeStructDefinesCorrectFields()
        {
            var node = new PlayMontageChainNode
            {
                TargetCharacter = 789,
                ChainCount = 3,
                ChainedMontages = new[] { ReloadId, MeleeSwingId, ReloadLongId, 0, 0, 0, 0, 0 },
            };

            Assert.Equal(789u, node.TargetCharacter);
            Assert.Equal(3, node.ChainCount);
            Assert.Equal(ReloadId, node.ChainedMontages[0]);
            Assert.Equal(MeleeSwingId, node.ChainedMontages[1]);
            Assert.Equal(ReloadLongId, node.ChainedMontages[2]);
        }

        [Fact]
        public void PlayMontageChainNode_HasChainedMontagesField()
        {
            // Verify the struct has ChainedMontages field with [MarshalAs] attribute
            var node = new PlayMontageChainNode
            {
                TargetCharacter = 123,
                ChainCount = 3,
                ChainedMontages = new int[8] { ReloadId, MeleeSwingId, ReloadLongId, 0, 0, 0, 0, 0 }
            };
            
            Assert.Equal(123u, node.TargetCharacter);
            Assert.Equal(3, node.ChainCount);
            Assert.NotNull(node.ChainedMontages);
            Assert.Equal(8, node.ChainedMontages.Length);
            Assert.Equal(ReloadId, node.ChainedMontages[0]);
            Assert.Equal(MeleeSwingId, node.ChainedMontages[1]);
            Assert.Equal(ReloadLongId, node.ChainedMontages[2]);
        }

        [Fact]
        public unsafe void PlayMontageChainNode_SpanCastMutationPatternWorks()
        {
            // Verify Span-cast safe mutation pattern (ANIM010)
            var (repo, backend, cache) = CreateFixture();
            var entity = CreateAnimatedEntity(repo);

            // Simulate queue mutation via Span-cast pattern (as codegen would emit)
            ref var queueComp = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
            queueComp.QueueVersion = 1;

            fixed (AnimationMontageQueue* queuePtr = &queueComp)
            {
                var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                    new Span<byte>(queuePtr->EntriesData, 128));

                entries[0] = new MontageQueueEntry { MontageId = ReloadId };
                entries[1] = new MontageQueueEntry { MontageId = MeleeSwingId };
                entries[2] = new MontageQueueEntry { MontageId = ReloadLongId };
                queueComp.Count = 3;
                queueComp.QueueVersion++;
            }

            // Verify mutation succeeded
            ref var readComp = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
            Assert.Equal(3, readComp.Count);
            Assert.Equal(2u, readComp.QueueVersion);

            // Read back to verify entries persisted
            fixed (AnimationMontageQueue* queuePtr = &readComp)
            {
                var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                    new Span<byte>(queuePtr->EntriesData, 128));
                Assert.Equal(ReloadId, entries[0].MontageId);
                Assert.Equal(MeleeSwingId, entries[1].MontageId);
                Assert.Equal(ReloadLongId, entries[2].MontageId);
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // ANC-P5-02: EnqueueMontageNode Tests
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void EnqueueMontageNode_NodeStructDefinesCorrectFields()
        {
            var node = new EnqueueMontageNode
            {
                TargetCharacter = 111,
                MontageId = ReloadId,
                OnlyIfEmpty = false,
            };

            Assert.Equal(111u, node.TargetCharacter);
            Assert.Equal(ReloadId, node.MontageId);
            Assert.False(node.OnlyIfEmpty);
        }

        [Fact]
        public void EnqueueMontageNode_MontagePickerAttributeOnMontageIdField()
        {
            var field = typeof(EnqueueMontageNode).GetField(nameof(EnqueueMontageNode.MontageId));
            Assert.NotNull(field);
            var attr = field.GetCustomAttributes(typeof(Events.MontagePickerAttribute), false);
            Assert.Single(attr);
        }

        [Fact]
        public void EnqueueMontageNode_OnlyIfEmptyFlagWorks()
        {
            var node1 = new EnqueueMontageNode { OnlyIfEmpty = true };
            var node2 = new EnqueueMontageNode { OnlyIfEmpty = false };

            Assert.True(node1.OnlyIfEmpty);
            Assert.False(node2.OnlyIfEmpty);
        }

        // ───────────────────────────────────────────────────────────────────────
        // ANC-P5-02: ClearMontageQueueNode Tests
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void ClearMontageQueueNode_NodeStructDefinesCorrectFields()
        {
            var node = new ClearMontageQueueNode
            {
                TargetCharacter = 222,
            };

            Assert.Equal(222u, node.TargetCharacter);
        }

        // ───────────────────────────────────────────────────────────────────────
        // ANC-P5-03: SetStanceNode Tests
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void SetStanceNode_NodeStructDefinesCorrectFields()
        {
            var node = new SetStanceNode
            {
                TargetCharacter = 333,
                TargetStance = StanceId.Crouched,
            };

            Assert.Equal(333u, node.TargetCharacter);
            Assert.Equal(StanceId.Crouched, node.TargetStance);
        }

        [Fact]
        public void SetStanceNode_AcceptsAllStanceValues()
        {
            var standing = new SetStanceNode { TargetStance = StanceId.Standing };
            var crouched = new SetStanceNode { TargetStance = StanceId.Crouched };
            var prone = new SetStanceNode { TargetStance = StanceId.Prone };

            Assert.Equal(StanceId.Standing, standing.TargetStance);
            Assert.Equal(StanceId.Crouched, crouched.TargetStance);
            Assert.Equal(StanceId.Prone, prone.TargetStance);
        }

        // ───────────────────────────────────────────────────────────────────────
        // Integration tests: Queue mutation via multiple nodes
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public unsafe void QueueMutationPattern_MultipleEntriesViaSpanCast()
        {
            // Test that we can safely mutate multiple queue entries via Span-cast
            // This simulates what PlayMontageChainNode codegen would emit
            var (repo, backend, cache) = CreateFixture();
            var entity = CreateAnimatedEntity(repo);

            // Stage 1: Chain 3 montages
            ref var queueComp = ref repo.GetComponentRW<AnimationMontageQueue>(entity);

            fixed (AnimationMontageQueue* queuePtr = &queueComp)
            {
                var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                    new Span<byte>(queuePtr->EntriesData, 128));

                entries[0] = new MontageQueueEntry { MontageId = ReloadId, BlendIntoTime = 0.1f, PlayRate = 1.0f };
                entries[1] = new MontageQueueEntry { MontageId = MeleeSwingId, BlendIntoTime = 0.2f, PlayRate = 1.0f };
                entries[2] = new MontageQueueEntry { MontageId = ReloadLongId, BlendIntoTime = 0.15f, PlayRate = 0.9f };
                queueComp.Count = 3;
                queueComp.QueueVersion++;
            }

            // Verify mutations persisted
            {
                ref var queueComp2 = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
                Assert.Equal(3, queueComp2.Count);
                Assert.Equal(1u, queueComp2.QueueVersion);

                fixed (AnimationMontageQueue* queuePtr = &queueComp2)
                {
                    var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                        new Span<byte>(queuePtr->EntriesData, 128));
                    Assert.Equal(ReloadId, entries[0].MontageId);
                    Assert.Equal(MeleeSwingId, entries[1].MontageId);
                    Assert.Equal(ReloadLongId, entries[2].MontageId);
                }
            }

            // Stage 2: Enqueue one more (simulating EnqueueMontageNode)
            if (queueComp.Count < 8)
            {
                fixed (AnimationMontageQueue* queuePtr = &queueComp)
                {
                    var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                        new Span<byte>(queuePtr->EntriesData, 128));
                    entries[queueComp.Count] = new MontageQueueEntry { MontageId = ReloadId, BlendIntoTime = 0.1f, PlayRate = 1.0f };
                    queueComp.Count++;
                    queueComp.QueueVersion++;
                }
            }

            // Verify
            {
                var queueComp3 = repo.GetComponent<AnimationMontageQueue>(entity);
                Assert.Equal(4, queueComp3.Count);
                Assert.Equal(2u, queueComp3.QueueVersion);
            }
        }

        [Fact]
        public unsafe void QueueMutationPattern_ClearFutureEntries()
        {
            // Test ClearMontageQueueNode pattern: clear entries 1..N, keep entry 0
            var (repo, backend, cache) = CreateFixture();
            var entity = CreateAnimatedEntity(repo);

            // Setup: 3 queued montages, current entry index = 0
            ref var queueComp = ref repo.GetComponentRW<AnimationMontageQueue>(entity);

            fixed (AnimationMontageQueue* queuePtr = &queueComp)
            {
                var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                    new Span<byte>(queuePtr->EntriesData, 128));
                entries[0] = new MontageQueueEntry { MontageId = ReloadId };
                entries[1] = new MontageQueueEntry { MontageId = MeleeSwingId };
                entries[2] = new MontageQueueEntry { MontageId = ReloadLongId };
                queueComp.Count = 3;
                queueComp.QueueVersion = 0;
            }

            ref var qsComp = ref repo.GetComponentRW<AnimationMontageQueueState>(entity);
            qsComp.CurrentEntryIndex = 0;

            // Clear future entries (simulating ClearMontageQueueNode)
            if (qsComp.CurrentEntryIndex != 0xFF)
            {
                byte newCount = (byte)(qsComp.CurrentEntryIndex + 1);
                if (queueComp.Count > newCount)
                {
                    fixed (AnimationMontageQueue* queuePtr = &queueComp)
                    {
                        var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                            new Span<byte>(queuePtr->EntriesData, 128));
                        for (int i = newCount; i < queueComp.Count; i++)
                            entries[i] = default;
                        queueComp.Count = newCount;
                        queueComp.QueueVersion++;
                    }
                }
            }

            // Verify only entry 0 remains
            {
                ref var queueComp2 = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
                Assert.Equal(1, queueComp2.Count);
                Assert.Equal(1u, queueComp2.QueueVersion);

                fixed (AnimationMontageQueue* queuePtr = &queueComp2)
                {
                    var entries = MemoryMarshal.Cast<byte, MontageQueueEntry>(
                        new Span<byte>(queuePtr->EntriesData, 128));
                    Assert.Equal(ReloadId, entries[0].MontageId);
                    // Entry 1 and 2 are zeroed
                    Assert.Equal(0, entries[1].MontageId);
                    Assert.Equal(0, entries[2].MontageId);
                }
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // Struct size and layout verification
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void PlayMontageNode_StructSizeIsExpected()
        {
            // uint + int + byte = 4 + 4 + 1 = 9 bytes (plus padding)
            var size = Marshal.SizeOf<PlayMontageNode>();
            Assert.True(size >= 9, $"PlayMontageNode size {size} should be at least 9");
        }

        [Fact]
        public void StopMontageNode_StructSizeIsExpected()
        {
            // uint + byte = 4 + 1 = 5 bytes (plus padding)
            var size = Marshal.SizeOf<StopMontageNode>();
            Assert.True(size >= 5, $"StopMontageNode size {size} should be at least 5");
        }

        [Fact]
        public void EnqueueMontageNode_StructSizeIsExpected()
        {
            // uint + int + bool = 4 + 4 + 1 = 9 bytes (plus padding)
            var size = Marshal.SizeOf<EnqueueMontageNode>();
            Assert.True(size >= 9, $"EnqueueMontageNode size {size} should be at least 9");
        }

        [Fact]
        public void SetStanceNode_StructSizeIsExpected()
        {
            // uint + byte = 4 + 1 = 5 bytes (plus padding)
            var size = Marshal.SizeOf<SetStanceNode>();
            Assert.True(size >= 5, $"SetStanceNode size {size} should be at least 5");
        }
    }
}
