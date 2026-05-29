using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tests.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests
{
    /// <summary>
    /// Phase-0 gate integration test (TASK-UAI-P0-07).
    /// Exercises all six prerequisite items (P0.01–P0.06) in a single cohesive scenario.
    /// Must pass before any Phase-1 Utility AI work may begin.
    /// </summary>
    public class Phase0IntegrationTests
    {
        [Fact]
        public unsafe void Phase0_Bundle_Integration()
        {
            using var w = new UtilityTestWorld();

            // ── P0.1 / P0.2: Multi-mount agent ─────────────────────────────────────
            var agent = w.SpawnAgent(health01: 1f, ammo01: 1f, initialAmmunition: 30);
            var mount1 = w.SpawnWeaponMount(agent, mountIndex: 1, weaponGuid: 0xABCDEF01, effRange: 300f, ammo01: 0.5f, initialAmmunition: 20);
            var mount2 = w.SpawnWeaponMount(agent, mountIndex: 2, weaponGuid: 0xABCDEF02, effRange: 600f, ammo01: 1.0f, initialAmmunition: 4);

            // 3 WeaponState components total
            Assert.True(w.Repo.HasComponent<WeaponState>(agent));
            Assert.True(w.Repo.HasComponent<WeaponState>(mount1));
            Assert.True(w.Repo.HasComponent<WeaponState>(mount2));

            // WeaponMountInfo on children only
            Assert.False(w.Repo.HasComponent<WeaponMountInfo>(agent));
            Assert.True(w.Repo.HasComponent<WeaponMountInfo>(mount1));
            Assert.Equal(1, w.Repo.GetComponentRO<WeaponMountInfo>(mount1).MountIndex);
            Assert.Equal(2, w.Repo.GetComponentRO<WeaponMountInfo>(mount2).MountIndex);

            // PartMetadata back-links
            Assert.Equal(agent, w.Repo.GetComponentRO<PartMetadata>(mount1).ParentEntity);
            Assert.Equal(agent, w.Repo.GetComponentRO<PartMetadata>(mount2).ParentEntity);

            // MaxAmmo cached correctly (P0.1)
            Assert.Equal(30, w.Repo.GetComponentRO<WeaponState>(agent).MaxAmmo);
            Assert.Equal(20, w.Repo.GetComponentRO<WeaponState>(mount1).MaxAmmo);

            // Independent ammo mutation (P0.2)
            w.SetWeaponAmmo(agent, mountIndex: 0, ammo01: 0f);
            Assert.Equal(0, w.Repo.GetComponentRO<WeaponState>(agent).Ammo);
            Assert.Equal(10, w.Repo.GetComponentRO<WeaponState>(mount1).Ammo); // 50% of 20

            // ── P0.3: Perception cap = 16 ────────────────────────────────────────────
            Assert.Equal(16, PerceptionConstants.MaxTrackedTargets);
            var contacts = new Entity[16];
            for (int i = 0; i < 16; i++)
            {
                contacts[i] = w.Repo.CreateEntity();
                w.SeedContact(agent, contacts[i], distanceM: 10f + i, threatBoost: (float)(i + 1),
                              contactHealth01: 1f, hasLos: true);
            }
            Assert.Equal(16, w.Repo.GetComponentRO<TargetMemory>(agent).Count);

            // ── P0.4: UnitRoster helpers ──────────────────────────────────────────────
            var leader = w.SpawnLeader();
            var m1 = w.SpawnSquadMember(leader, health01: 1f, ammo01: 1f);
            var m2 = w.SpawnSquadMember(leader, health01: 1f, ammo01: 1f);
            {
                ref var roster = ref w.Repo.GetComponentRW<UnitRoster>(leader);
                int slot1 = UnitRoster.IndexOf(ref roster, (long)m1.PackedValue);
                int slot2 = UnitRoster.IndexOf(ref roster, (long)m2.PackedValue);
                Assert.True(slot1 >= 0, "m1 should be in roster");
                Assert.True(slot2 >= 0, "m2 should be in roster");
                Assert.NotEqual(slot1, slot2);
            }

            // ── P0.5: Blackboard1024.Project<T> ─────────────────────────────────────
            {
                ref var bb = ref w.Repo.GetComponentRW<Blackboard1024>(leader);
                ref var proj = ref Blackboard1024.Project<TestProjectionStruct>(ref bb);
                proj.Value = 42;
                // Re-read via projection must see the mutation
                ref var reread = ref Blackboard1024.Project<TestProjectionStruct>(ref bb);
                Assert.Equal(42, reread.Value);
            }

            // ── P0.6: EQS sensor child entities ──────────────────────────────────────
            var coverSensor = w.SpawnEqsSensor(agent, Fnv1a32("CoverQuery"), topScore: 0.85f, count: 3, instanceId: 0);
            Assert.True(w.Repo.HasComponent<EqsSensor>(coverSensor));
            Assert.Equal(Fnv1a32("CoverQuery"), w.Repo.GetComponentRO<EqsSensor>(coverSensor).BlueprintId);
            Assert.Equal(3, w.Repo.GetComponentRO<EqsCognitiveBuffer>(coverSensor).Count);
            Assert.Equal(0.85f, w.Repo.GetComponentRO<EqsCognitiveBuffer>(coverSensor).GetSpanRO()[0].Score,
                         precision: 3);

            // ── Gate: all above pass means Phase-0 is complete ──────────────────────
        }

        // Small struct for Blackboard projection test
        private struct TestProjectionStruct { public int Value; }
        private static uint Fnv1a32(string name) => UtilityTestWorld.Fnv1a32(name);
    }
}
