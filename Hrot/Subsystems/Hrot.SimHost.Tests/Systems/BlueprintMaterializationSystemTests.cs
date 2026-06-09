using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Blueprints.Systems;
using Hrot.Common.Serializers;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="BlueprintMaterializationSystem"/> — BSA-203.
    /// </summary>
    public sealed class BlueprintMaterializationSystemTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly BlueprintRegistry _registry;

        public BlueprintMaterializationSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<BlueprintBlackboard1024>();
            _repo.RegisterComponent<BlueprintBlackboard4096>();
            _repo.RegisterComponent<BlueprintBlackboard16384>();
            _repo.RegisterManagedComponent<InitialBlueprintsIntent>();
            _registry = new BlueprintRegistry();
        }

        public void Dispose() => _repo.Dispose();

        // ── Helpers ────────────────────────────────────────────────────────────

        private BlueprintMaterializationSystem CreateSystem()
            => new BlueprintMaterializationSystem(_registry);

        /// <summary>
        /// Registers test Instance blueprints. All blueprints are staged in a single batch
        /// and committed atomically so that prior registrations are not lost.
        /// Returns the list of computed blueprint IDs in registration order.
        /// </summary>
        private static List<int> RegisterTestBlueprints(
            BlueprintRegistry registry,
            params (Guid AssetId, string Name, int StateSize, TickDelegate? Tick)[] blueprints)
        {
            var staging = registry.BeginStaging();
            var ids = new List<int>();
            foreach (var (assetId, name, stateSize, tick) in blueprints)
            {
                int bpId = BlueprintIdHash.Compute(assetId);
                var def = new BlueprintDefinition
                {
                    Name          = name,
                    Kind          = BlueprintDispatchKind.Instance,
                    StructureHash = (ulong)(bpId & 0x7FFFFFFF),
                    StateSize     = stateSize,
                    AssetId       = assetId,
                    InitDefault   = span => span.Clear(),
                    Tick          = tick,
                };
                staging.Add(bpId, def);
                ids.Add(bpId);
            }
            registry.CommitStaging(staging);
            return ids;
        }

        /// <summary>
        /// Convenience overload for blueprints without a Tick delegate.
        /// </summary>
        private static List<int> RegisterTestBlueprints(
            BlueprintRegistry registry,
            params (Guid AssetId, string Name, int StateSize)[] blueprints)
        {
            var converted = new (Guid, string, int, TickDelegate?)[blueprints.Length];
            for (int i = 0; i < blueprints.Length; i++)
                converted[i] = (blueprints[i].AssetId, blueprints[i].Name, blueprints[i].StateSize, null);
            return RegisterTestBlueprints(registry, converted);
        }

        private Entity CreateEntityWithIntent(IEnumerable<(Guid AssetId, int StateSize)> blueprints)
        {
            var entity = _repo.CreateEntity();
            var intent = new InitialBlueprintsIntent();
            foreach (var (assetId, _) in blueprints)
            {
                intent.Blueprints.Add(new BlueprintAssignmentDto { AssetId = assetId });
            }
            _repo.SetManagedComponent(entity, intent);
            return entity;
        }

        // ── Test 1: One-frame, single-tier ─────────────────────────────────────

        [Fact]
        public void Materialize_SmallBlueprints_AttachesToB1024AndRemovesIntent()
        {
            // Register 3 blueprints summing ≤ 928 bytes
            var g1 = new Guid("A0117E72-0000-0000-0000-000000000001");
            var g2 = new Guid("A0117E72-0000-0000-0000-000000000002");
            var g3 = new Guid("A0117E72-0000-0000-0000-000000000003");
            RegisterTestBlueprints(_registry,
                (g1, "TestBp1", 100),
                (g2, "TestBp2", 100),
                (g3, "TestBp3", 100)); // 300 bytes total ≤ 928, 3 slots ≤ 4

            var entity = CreateEntityWithIntent(new[]
            {
                (g1, 100), (g2, 100), (g3, 100),
            });

            var sys = CreateSystem();
            sys.Execute(_repo, 0f);

            // Intent removed
            Assert.False(_repo.HasManagedComponent<InitialBlueprintsIntent>(entity));

            // Correct tier chosen: B1024 (300 bytes ≤ 928, 3 slots ≤ 4)
            Assert.True(_repo.HasComponent<BlueprintBlackboard1024>(entity));
            Assert.False(_repo.HasComponent<BlueprintBlackboard4096>(entity));
            Assert.False(_repo.HasComponent<BlueprintBlackboard16384>(entity));

            // 3 slots occupied
            unsafe
            {
                ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                fixed (byte* mem = bb.Memory)
                {
                    ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                    Assert.Equal(BlueprintBlackboardHeader.MagicValue, header.MagicAndVersion);
                    Assert.Equal(3, header.SlotCount);
                }
            }
        }

        // ── Test 2: Correct tier from aggregate ────────────────────────────────

        [Fact]
        public void Materialize_MediumBlueprints_ChoosesB4096()
        {
            // Register 4 blueprints each 250 bytes → 1000 bytes > 928, ≤ 3936; 4 slots ≤ 8
            var blueprints = new (Guid, string, int)[4];
            for (int i = 0; i < 4; i++)
            {
                blueprints[i] = (new Guid($"B0117E72-0000-0000-0000-00000000000{i}"), $"MedBp{i}", 250);
            }
            RegisterTestBlueprints(_registry, blueprints);

            var bpList = new List<(Guid, int)>();
            foreach (var (g, _, sz) in blueprints)
                bpList.Add((g, sz));

            var entity = CreateEntityWithIntent(bpList);

            var sys = CreateSystem();
            sys.Execute(_repo, 0f);

            // Correct tier: B4096 (1000 bytes > 928 but ≤ 3936)
            Assert.False(_repo.HasComponent<BlueprintBlackboard1024>(entity));
            Assert.True(_repo.HasComponent<BlueprintBlackboard4096>(entity));
            Assert.False(_repo.HasComponent<BlueprintBlackboard16384>(entity));
        }

        // ── Test 3: Ceiling guard ──────────────────────────────────────────────

        [Fact]
        public void Materialize_ExceedsCeiling_TruncatesWithoutThrowing()
        {
            // Register 20 blueprints → exceeds 16 slot ceiling
            var blueprints = new (Guid, string, int)[20];
            for (int i = 0; i < 20; i++)
            {
                blueprints[i] = (new Guid($"C0117E72-0000-0000-0000-0000000000{i:X2}"), $"BigBp{i}", 50);
            }
            RegisterTestBlueprints(_registry, blueprints);

            var bpList = new List<(Guid, int)>();
            foreach (var (g, _, sz) in blueprints)
                bpList.Add((g, sz));

            var entity = CreateEntityWithIntent(bpList);

            var sys = CreateSystem();
            // Must not throw
            sys.Execute(_repo, 0f);

            // Entity has B16384 (max tier)
            Assert.True(_repo.HasComponent<BlueprintBlackboard16384>(entity));

            // Slot count ≤ 16 (truncated)
            unsafe
            {
                ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* mem = bb.Memory)
                {
                    ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                    Assert.Equal(BlueprintBlackboardHeader.MagicValue, header.MagicAndVersion);
                    Assert.True(header.SlotCount <= BlueprintBlackboard16384.MaxSlots);
                    Assert.True(header.SlotCount > 0); // at least some made it in
                }
            }

            // Intent is removed
            Assert.False(_repo.HasManagedComponent<InitialBlueprintsIntent>(entity));
        }

        // ── Test 4: Resilience (unregistered AssetId) ──────────────────────────

        [Fact]
        public void Materialize_UnregisteredAssetId_SkipsAndAttachesValid()
        {
            var validGuid = new Guid("D0117E72-0000-0000-0000-000000000001");
            var bogusGuid = new Guid("D0117E72-0000-0000-0000-000000000099"); // not registered
            RegisterTestBlueprints(_registry, (validGuid, "ValidBp", 80));

            var entity = _repo.CreateEntity();
            var intent = new InitialBlueprintsIntent();
            intent.Blueprints.Add(new BlueprintAssignmentDto { AssetId = validGuid });
            intent.Blueprints.Add(new BlueprintAssignmentDto { AssetId = bogusGuid }); // unregistered
            _repo.SetManagedComponent(entity, intent);

            var sys = CreateSystem();
            // Must not throw
            sys.Execute(_repo, 0f);

            // Valid blueprint attached (slot count == 1)
            Assert.True(_repo.HasComponent<BlueprintBlackboard1024>(entity));
            unsafe
            {
                ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                fixed (byte* mem = bb.Memory)
                {
                    ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(mem);
                    Assert.Equal(1, header.SlotCount);
                }
            }

            // Intent removed
            Assert.False(_repo.HasManagedComponent<InitialBlueprintsIntent>(entity));
        }

        // ── Test 5: Intent removed after materialization ───────────────────────

        [Fact]
        public void Materialize_IntentRemovedAfterExecute()
        {
            var g = new Guid("E0117E72-0000-0000-0000-000000000001");
            RegisterTestBlueprints(_registry, (g, "Bp1", 50));

            var entity = CreateEntityWithIntent(new[] { (g, 50) });

            var sys = CreateSystem();
            sys.Execute(_repo, 0f);

            // Confirm ECB-queued removal took effect
            Assert.False(_repo.HasManagedComponent<InitialBlueprintsIntent>(entity));
        }

        // ── Test 6: ECB removal (no iterator invalidation) ─────────────────────

        [Fact]
        public void Materialize_TwoEntities_BothIntentsRemoved()
        {
            var g = new Guid("F0117E72-0000-0000-0000-000000000001");
            RegisterTestBlueprints(_registry, (g, "Bp1", 50));

            var entity1 = CreateEntityWithIntent(new[] { (g, 50) });
            var entity2 = CreateEntityWithIntent(new[] { (g, 50) });

            var sys = CreateSystem();
            sys.Execute(_repo, 0f);

            // Both intents removed — ECB-queued removal doesn't invalidate iterator
            Assert.False(_repo.HasManagedComponent<InitialBlueprintsIntent>(entity1));
            Assert.False(_repo.HasManagedComponent<InitialBlueprintsIntent>(entity2));
        }

        // ── Test 7: Attached blueprints tick ───────────────────────────────────

        [Fact]
        public void Materialize_ThenTick_BlueprintExecutesAndCounterAdvances()
        {
            // Create a test blueprint with a ticking counter
            var counterGuid = new Guid("C0117E72-0000-0000-0000-000000000001");
            int counterStateSize = 8; // 8 bytes: cursor (4) + counter (4)
            const int countOffset = 4; // after cursor

            long lastTickCount = 0;
            RegisterTestBlueprints(_registry,
                (counterGuid, "CounterBp", counterStateSize,
                (TickDelegate)((span, view, ecb, self, time, dt, version) =>
                {
                    ref int count = ref Unsafe.As<byte, int>(
                        ref Unsafe.Add(ref MemoryMarshal.GetReference(span), countOffset));
                    count++;
                    lastTickCount = count;
                })));

            var entity = CreateEntityWithIntent(new[] { (counterGuid, counterStateSize) });

            // Materialize
            var matSys = CreateSystem();
            matSys.Execute(_repo, 0f);

            Assert.False(_repo.HasManagedComponent<InitialBlueprintsIntent>(entity));
            Assert.True(_repo.HasComponent<BlueprintBlackboard1024>(entity));

            // Tick the BlueprintTickSystem several times
            var tickSys = new BlueprintTickSystem(_registry);
            const int frameCount = 5;
            for (int i = 0; i < frameCount; i++)
            {
                tickSys.Execute(_repo, 0.016f);
            }

            // Counter should have advanced
            Assert.True(lastTickCount >= frameCount,
                $"Expected counter ≥ {frameCount} after {frameCount} ticks, got {lastTickCount}");
        }
    }
}
