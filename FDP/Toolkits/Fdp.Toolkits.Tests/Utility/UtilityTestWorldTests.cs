using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tests.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests
{
    /// <summary>
    /// Unit tests for <see cref="UtilityTestWorld"/> helper (P0.06 success criteria).
    /// </summary>
    public class UtilityTestWorldTests
    {
        // SC-P0-06-1: Construction and Dispose succeed without exception
        [Fact]
        public void UtilityTestWorld_ConstructAndDispose_DoesNotThrow()
        {
            var ex = Record.Exception(() =>
            {
                using var w = new UtilityTestWorld();
                Assert.NotNull(w.Repo);
            });
            Assert.Null(ex);
        }

        // SC-P0-06-2: SpawnAgent produces correct component set
        [Fact]
        public void SpawnAgent_ProducesCorrectComponents()
        {
            using var w = new UtilityTestWorld();
            var agent = w.SpawnAgent(health01: 1f, ammo01: 1f, initialAmmunition: 30);

            Assert.True(w.Repo.HasComponent<Health>(agent));
            Assert.True(w.Repo.HasComponent<WeaponState>(agent));
            Assert.True(w.Repo.HasComponent<Fdp.Modules.Geographic.Components.Position>(agent));
            Assert.True(w.Repo.HasComponent<TargetMemory>(agent));

            // WeaponState values
            ref readonly var ws = ref w.Repo.GetComponentRO<WeaponState>(agent);
            Assert.Equal(30, ws.MaxAmmo);
            Assert.Equal(30, ws.Ammo);
        }

        // SC-P0-06-3: SpawnWeaponMount produces WeaponMountInfo and PartMetadata
        [Fact]
        public void SpawnWeaponMount_ProducesCorrectComponents()
        {
            using var w = new UtilityTestWorld();
            var owner = w.SpawnAgent(1f, 1f);
            var child = w.SpawnWeaponMount(owner, mountIndex: 1, weaponGuid: 0xABC,
                effRange: 300f, ammo01: 0.5f, initialAmmunition: 20);

            Assert.True(w.Repo.HasComponent<WeaponMountInfo>(child));
            Assert.True(w.Repo.HasComponent<PartMetadata>(child));

            ref readonly var mi = ref w.Repo.GetComponentRO<WeaponMountInfo>(child);
            Assert.Equal(1, mi.MountIndex);

            ref readonly var pm = ref w.Repo.GetComponentRO<PartMetadata>(child);
            Assert.Equal(owner, pm.ParentEntity);
        }

        // SC-P0-06-4: SeedContact calls AddOrUpdateTarget and lands in TargetMemory
        [Fact]
        public unsafe void SeedContact_LandsInTargetMemory_AtSlot0()
        {
            using var w = new UtilityTestWorld();
            var self    = w.SpawnAgent(1f, 1f);
            var contact = w.Repo.CreateEntity();

            w.SeedContact(self, contact, distanceM: 50f, threatBoost: 100f, contactHealth01: 1f, hasLos: true);

            ref readonly var mem = ref w.Repo.GetComponentRO<TargetMemory>(self);
            Assert.Equal(1, mem.Count);
            Assert.Equal((long)contact.PackedValue, mem.EntityIds[0]);
            Assert.Equal(100f, mem.ThreatScores[0]);
        }

        // SC-P0-06-5: SpawnEqsSensor sets BlueprintId and seeds buffer correctly
        [Fact]
        public void SpawnEqsSensor_SetsBlueprintIdAndSeedsBuffer()
        {
            using var w = new UtilityTestWorld();
            var owner = w.SpawnAgent(1f, 1f);
            uint blueprintId = UtilityTestWorld.Fnv1a32("CoverQuery");

            var sensor = w.SpawnEqsSensor(owner, blueprintId, topScore: 0.85f, count: 2, instanceId: 0);

            Assert.True(w.Repo.HasComponent<EqsSensor>(sensor));
            Assert.Equal(blueprintId, w.Repo.GetComponentRO<EqsSensor>(sensor).BlueprintId);

            ref readonly var buf = ref w.Repo.GetComponentRO<EqsCognitiveBuffer>(sensor);
            Assert.Equal(2, buf.Count);
            Assert.Equal(0.85f, buf.GetSpanRO()[0].Score, precision: 3);
        }

        // SC-P0-06-6: Fnv1a32 produces a stable, non-zero uint (pin: "CoverQuery" → 0x9317A97B or similar)
        [Fact]
        public void Fnv1a32_CoverQuery_ProducesStableNonZeroValue()
        {
            uint hash1 = UtilityTestWorld.Fnv1a32("CoverQuery");
            uint hash2 = UtilityTestWorld.Fnv1a32("CoverQuery");

            Assert.NotEqual(0u, hash1);
            Assert.Equal(hash1, hash2); // Stable: same input → same output

            // Pinned value for "CoverQuery" using FNV-1a32 (basis=2166136261, prime=16777619):
            // C=0x43, o=0x6F, v=0x76, e=0x65, r=0x72, Q=0x51, u=0x75, e=0x65, r=0x72, y=0x79
            // Actual runtime value verified 2026-05-29. Change intentionally if algorithm changes.
            Assert.Equal(0x72BE4C04u, hash1); // Pinned: algorithm regression guard
        }

        // Additional: SpawnLeader produces correct components
        [Fact]
        public void SpawnLeader_ProducesUnitRosterAndBlackboard()
        {
            using var w = new UtilityTestWorld();
            var leader = w.SpawnLeader();

            Assert.True(w.Repo.HasComponent<UnitRoster>(leader));
            Assert.True(w.Repo.HasComponent<Blackboard1024>(leader));
            Assert.True(w.Repo.HasComponent<TargetMemory>(leader));
        }

        // Additional: SpawnSquadMember registers in roster and has UnitSubordinate
        [Fact]
        public unsafe void SpawnSquadMember_RegisteredInRosterAndLinkedToLeader()
        {
            using var w = new UtilityTestWorld();
            var leader = w.SpawnLeader();
            var member = w.SpawnSquadMember(leader, health01: 1f, ammo01: 1f);

            Assert.True(w.Repo.HasComponent<UnitSubordinate>(member));
            Assert.Equal(leader, w.Repo.GetComponentRO<UnitSubordinate>(member).Commander);

            ref var roster = ref w.Repo.GetComponentRW<UnitRoster>(leader);
            int slot = UnitRoster.IndexOf(ref roster, (long)member.PackedValue);
            Assert.True(slot >= 0, "Member should be in roster.");
        }
    }
}
