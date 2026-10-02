using System;
using System.Threading;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Replication.Egress;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Integration tests for <see cref="EntityDamageEgressTranslator"/> (BS1-T015).
    /// Uses a live DDS participant/reader pair to verify end-to-end publication.
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("SimHostDds")]
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
        /// publish one <see cref="EntityDamage"/> DDS sample carrying Current and Max verbatim.
        /// ⭐ CE-196 — this used to assert a derived 30% damage level; the descriptor now ships the
        /// PAIR and each consumer derives its own fraction.
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
                    // The authority's own numbers travel unmodified — no percentage in between.
                    Assert.True(Math.Abs(sample.Data.Current - 70f) < 0.01f,
                        $"Expected Current ≈ 70 but got {sample.Data.Current}");
                    Assert.True(Math.Abs(sample.Data.Max - 100f) < 0.01f,
                        $"Expected Max ≈ 100 but got {sample.Data.Max}");
                    found = true;
                    break;
                }
            }

            Assert.True(found, "Expected EntityDamage DDS sample for entity 42.");
        }

        // ── CE-196: a Max-ONLY change must still reach the receivers ─────────

        /// <summary>
        /// ⭐⭐⭐ <b>CE-196 — the change-gate covers BOTH fields, not just <c>Current</c>.</b>
        ///
        /// <para>The translator republishes only when health changed. Before this rail the gate
        /// compared <c>Current</c> alone — which was correct while the wire carried a precomputed
        /// percentage, because <c>Max</c> never left this node. Now that <c>Max</c> travels, it is
        /// authored data: a scenario sets it, and an editor write can change it. A <c>Max</c>-only
        /// change that never publishes would pin every receiver to a stale maximum and silently
        /// mis-scale every health bar in the cluster.</para>
        ///
        /// <para>⚠ This is the ONLY rail that fails if the gate regresses to comparing <c>Current</c>:
        /// SC-1 changes both fields at once, and SC-2 changes neither.</para>
        /// </summary>
        [Fact]
        public void ScanAndPublish_Republishes_WhenOnlyMaxChanges()
        {
            const uint domainId = 213u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<EntityDamage>(participant, "EntityDamage");

            var entityMap  = new NetworkEntityMap();
            var translator = new EntityDamageEgressTranslator(participant, entityMap);

            var entity = SpawnEntity(netId: 77L, currentHealth: 50f, maxHealth: 50f);

            Thread.Sleep(200);
            translator.ScanAndPublish(_world);   // baseline publish
            Thread.Sleep(200);
            using (var drain = reader.Take()) { foreach (var _ in drain) { } }

            // Current is untouched; only the maximum moves.
            ref var health = ref _world.GetComponentRW<Health>(entity);
            health.Max = 200f;

            translator.ScanAndPublish(_world);
            Thread.Sleep(200);

            using var loan = reader.Take();
            bool found = false;
            foreach (var sample in loan)
            {
                if (!sample.IsValid || sample.Data.EntityId != 77) continue;
                Assert.True(Math.Abs(sample.Data.Current - 50f) < 0.01f,
                    $"Current must be unchanged, got {sample.Data.Current}");
                Assert.True(Math.Abs(sample.Data.Max - 200f) < 0.01f,
                    $"Expected the NEW Max of 200 but got {sample.Data.Max}");
                found = true;
                break;
            }

            Assert.True(found,
                "A Max-only change must republish — otherwise receivers keep a stale maximum forever.");
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
