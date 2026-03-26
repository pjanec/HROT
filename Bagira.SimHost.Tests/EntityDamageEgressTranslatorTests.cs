using System;
using System.Threading;
using Bagira.BDC.SSTD;
using Bagira.Map.Common.Replication.Egress;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Integration tests for <see cref="EntityDamageEgressTranslator"/> (BS1-T015).
    /// Uses a live DDS participant/reader pair to verify end-to-end publication.
    /// </summary>
    [Trait("Category", "Integration")]
    public class EntityDamageEgressTranslatorTests : IDisposable
    {
        private readonly EntityRepository _world;

        public EntityDamageEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<NetworkIdentity>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterComponent<Health>();
        }

        public void Dispose() => _world.Dispose();

        private Entity SpawnEntity(long netId, float currentHealth, float maxHealth = 100f,
            bool authoritative = true)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new NetworkIdentity(netId));
            _world.AddComponent(entity, new NetworkAuthority(
                primaryOwnerId: authoritative ? 1 : 2,
                localNodeId: 1));
            _world.AddComponent(entity, new Health { Current = currentHealth, Max = maxHealth });
            return entity;
        }

        // ── SC-1: Health change → EntityDamage published ──────────────────────

        /// <summary>
        /// BS1-T015 SC-1: When <see cref="Health.Current"/> changes, the translator must
        /// publish one <see cref="EntityDamage"/> DDS sample with the derived damage level.
        /// </summary>
        [Fact]
        public void ScanAndPublish_PublishesEntityDamage_WhenHealthChanges()
        {
            const uint domainId = 210u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<EntityDamage>(participant, "EntityDamage");

            var entityMap  = new NetworkEntityMap();
            var translator = new EntityDamageEgressTranslator(participant, entityMap);

            var entity = SpawnEntity(netId: 42L, currentHealth: 70f, maxHealth: 100f);

            Thread.Sleep(200);
            translator.ScanAndPublish(_world);
            Thread.Sleep(200);

            using var loan = reader.Take();
            bool found = false;
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                if (sample.Data.EntityId == 42)
                {
                    // 70/100 health → 30% damage level
                    Assert.True(Math.Abs(sample.Data.Damage - 30f) < 0.01f,
                        $"Expected Damage ≈ 30 but got {sample.Data.Damage}");
                    found = true;
                    break;
                }
            }

            Assert.True(found, "Expected EntityDamage DDS sample for entity 42.");
        }

        // ── SC-2: No change → no re-publish ──────────────────────────────────

        /// <summary>
        /// BS1-T015 SC-2: When <see cref="Health.Current"/> has not changed since the last
        /// publish, <see cref="ScanAndPublish"/> must not write a new sample.
        /// </summary>
        [Fact]
        public void ScanAndPublish_DoesNotRepublish_WhenHealthUnchanged()
        {
            const uint domainId = 211u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<EntityDamage>(participant, "EntityDamage");

            var entityMap  = new NetworkEntityMap();
            var translator = new EntityDamageEgressTranslator(participant, entityMap);

            SpawnEntity(netId: 43L, currentHealth: 80f, maxHealth: 100f);

            Thread.Sleep(200);
            // First publish — establishes baseline.
            translator.ScanAndPublish(_world);
            Thread.Sleep(200);

            // Consume the first sample.
            using (var loan = reader.Take())
            {
                foreach (var s in loan) { }  // drain
            }

            // Second publish with same health — should not write again.
            Thread.Sleep(200);
            translator.ScanAndPublish(_world);
            Thread.Sleep(200);

            // No new samples should arrive.
            using var loan2 = reader.Take();
            int count = 0;
            foreach (var sample in loan2)
            {
                if (sample.IsValid && sample.Data.EntityId == 43)
                    count++;
            }
            Assert.Equal(0, count);
        }

        // ── SC-3: Non-authoritative entity not published ──────────────────────

        /// <summary>
        /// BS1-T015: Entities not owned by the local node must not be published.
        /// </summary>
        [Fact]
        public void ScanAndPublish_DoesNotPublish_ForNonAuthoritativeEntity()
        {
            const uint domainId = 212u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<EntityDamage>(participant, "EntityDamage");

            var entityMap  = new NetworkEntityMap();
            var translator = new EntityDamageEgressTranslator(participant, entityMap);

            SpawnEntity(netId: 44L, currentHealth: 50f, authoritative: false);

            Thread.Sleep(200);
            translator.ScanAndPublish(_world);
            Thread.Sleep(200);

            using var loan = reader.Take();
            int count = 0;
            foreach (var sample in loan)
            {
                if (sample.IsValid && sample.Data.EntityId == 44)
                    count++;
            }
            Assert.Equal(0, count);
        }
    }
}
