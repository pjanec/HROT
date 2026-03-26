using System;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace FDP.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DamageCalculationSystem"/> (BS1-T012).
    /// </summary>
    public class DamageCalculationSystemTests : IDisposable
    {
        private readonly EntityRepository    _world;
        private readonly NetworkEntityMap    _entityMap;
        private readonly DamageCalculationSystem _sys;

        public DamageCalculationSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterEvent<DetonationNotification>();
            _world.RegisterEvent<DamageAssessedEvent>();

            _entityMap = new NetworkEntityMap();

            _sys = new DamageCalculationSystem(_entityMap);
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Entity SpawnTarget(long netId, bool authoritative = true)
        {
            var entity = _world.CreateEntity();
            if (authoritative)
                _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            else
                _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            _entityMap.Register(netId, entity);
            return entity;
        }

        private void PublishDetonation(long hitEntityId)
        {
            _world.Bus.Publish(new DetonationNotification
            {
                ShooterEntityId = 99L,
                HitEntityId     = hitEntityId,
                HitX            = 0f, HitY = 0f, HitZ = 0f,
            });
            _world.Bus.SwapBuffers();
        }

        // ── SC-1: Authority node → DamageAssessedEvent ────────────────────────

        /// <summary>
        /// BS1-T012 SC-1: An authoritative node receiving a <see cref="DetonationNotification"/>
        /// for a known entity must publish one <see cref="DamageAssessedEvent"/> with
        /// <c>TotalDamage == CombatConstants.DefaultBulletDamage</c>.
        /// </summary>
        [Fact]
        public void DamageCalculation_PublishesDamageAssessedEvent_WhenAuthoritative()
        {
            SpawnTarget(netId: 5L, authoritative: true);
            PublishDetonation(hitEntityId: 5L);

            _sys.Run();

            _world.Bus.SwapBuffers();
            var events = _world.Bus.Consume<DamageAssessedEvent>();

            Assert.Equal(1, events.Length);
            Assert.Equal(5L, events[0].HitEntityId);
            Assert.Equal(CombatConstants.DefaultBulletDamage, events[0].TotalDamage);
        }

        // ── SC-2: Non-authority → no DamageAssessedEvent ─────────────────────

        /// <summary>
        /// BS1-T012 SC-2: When the local node does not have authority over the target entity,
        /// no <see cref="DamageAssessedEvent"/> should be published.
        /// </summary>
        [Fact]
        public void DamageCalculation_DoesNotPublish_WhenNotAuthoritative()
        {
            SpawnTarget(netId: 5L, authoritative: false);
            PublishDetonation(hitEntityId: 5L);

            _sys.Run();

            _world.Bus.SwapBuffers();
            var events = _world.Bus.Consume<DamageAssessedEvent>();

            Assert.Equal(0, events.Length);
        }

        // ── SC-3: DamageCalculationSystem does not mutate Health ──────────────

        /// <summary>
        /// BS1-T012 SC-3: <see cref="DamageCalculationSystem"/> must not mutate any
        /// <c>Health</c> component directly; it only publishes events.
        /// </summary>
        [Fact]
        public void DamageCalculation_DoesNotMutateHealth()
        {
            // Register Health and add it to the target — system must not touch it.
            _world.RegisterComponent<FDP.Toolkit.Combat.Components.Health>();
            var entity = SpawnTarget(netId: 5L, authoritative: true);
            _world.AddComponent(entity, new FDP.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });

            PublishDetonation(hitEntityId: 5L);
            _sys.Run();

            var health = _world.GetComponent<FDP.Toolkit.Combat.Components.Health>(entity);
            Assert.Equal(100f, health.Current);
        }

        // ── Unknown entity → skipped gracefully ──────────────────────────────

        /// <summary>
        /// When the target entity ID is not in <see cref="NetworkEntityMap"/>, the event
        /// must be skipped silently.
        /// </summary>
        [Fact]
        public void DamageCalculation_SkipsUnknownEntity()
        {
            PublishDetonation(hitEntityId: 9999L);

            var ex = Record.Exception(() => _sys.Run());
            Assert.Null(ex);

            _world.Bus.SwapBuffers();
            var events = _world.Bus.Consume<DamageAssessedEvent>();
            Assert.Equal(0, events.Length);
        }
    }
}
