using System;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace FDP.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="HealthApplicationSystem"/> (BS1-T014).
    /// </summary>
    public class HealthApplicationSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;
        private readonly HealthApplicationSystem _sys;

        public HealthApplicationSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterEvent<DamageAssessedEvent>();

            _entityMap = new NetworkEntityMap();

            _sys = new HealthApplicationSystem(_entityMap);
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Entity SpawnTarget(long netId, float currentHealth, float maxHealth = 100f,
            bool authoritative = true, bool addCapabilities = false)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Health { Current = currentHealth, Max = maxHealth });
            _world.AddComponent(entity, new NetworkAuthority(
                primaryOwnerId: authoritative ? 1 : 2,
                localNodeId: 1));

            if (addCapabilities)
                _world.AddComponent(entity, new ActorCapabilityState
                {
                    Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
                });

            _entityMap.Register(netId, entity);
            return entity;
        }

        private void PublishEvent(long hitEntityId, float totalDamage)
        {
            _world.Bus.Publish(new DamageAssessedEvent
            {
                HitEntityId = hitEntityId,
                TotalDamage = totalDamage,
            });
            _world.Bus.SwapBuffers();
        }

        // ── SC-1: Health decremented ──────────────────────────────────────────

        /// <summary>
        /// BS1-T014 SC-1: When an authoritative node receives a <see cref="DamageAssessedEvent"/>
        /// for a known entity, <see cref="Health.Current"/> must be reduced by the given amount.
        /// </summary>
        [Fact]
        public void HealthApplication_DecrementsHealth_WhenAuthoritative()
        {
            var entity = SpawnTarget(netId: 1L, currentHealth: 100f);

            PublishEvent(hitEntityId: 1L, totalDamage: 30f);
            _sys.Run();

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(70f, health.Current);
        }

        // ── SC-2: Health cannot go below zero ─────────────────────────────────

        /// <summary>
        /// BS1-T014 SC-2: <see cref="Health.Current"/> must be clamped to 0 and never go negative.
        /// </summary>
        [Fact]
        public void HealthApplication_ClampsHealthToZero_WhenDamageExceedsCurrentHP()
        {
            var entity = SpawnTarget(netId: 1L, currentHealth: 10f);

            PublishEvent(hitEntityId: 1L, totalDamage: 50f);
            _sys.Run();

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(0f, health.Current);
        }

        // ── SC-3: Zero HP strips capabilities ────────────────────────────────

        /// <summary>
        /// BS1-T014 SC-3: When <see cref="Health.Current"/> reaches 0, both
        /// <see cref="ActorCapabilities.CanMove"/> and <see cref="ActorCapabilities.CanShoot"/>
        /// must be cleared from <see cref="ActorCapabilityState"/>.
        /// </summary>
        [Fact]
        public void HealthApplication_StripsCapabilities_WhenHealthReachesZero()
        {
            var entity = SpawnTarget(netId: 1L, currentHealth: 10f, addCapabilities: true);

            PublishEvent(hitEntityId: 1L, totalDamage: 50f);
            _sys.Run();

            var caps = _world.GetComponent<ActorCapabilityState>(entity);
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanMove));
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanShoot));
        }

        // ── SC-4: Non-authority → no health change ────────────────────────────

        /// <summary>
        /// BS1-T014 SC-4: A non-authoritative node must not alter the health of the entity.
        /// </summary>
        [Fact]
        public void HealthApplication_SkipsUpdate_WhenNotAuthoritative()
        {
            var entity = SpawnTarget(netId: 1L, currentHealth: 100f, authoritative: false);

            PublishEvent(hitEntityId: 1L, totalDamage: 30f);
            _sys.Run();

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(100f, health.Current);
        }
    }
}
