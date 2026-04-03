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

        // ── PACK-M002: Non-lethal hit strips CanMove only ─────────────────────

        /// <summary>
        /// PACK-M002 SC-1: A non-lethal <see cref="DamageAssessedEvent"/> must reduce HP
        /// and strip <see cref="ActorCapabilities.CanMove"/> while preserving all other
        /// capabilities (e.g. <see cref="ActorCapabilities.CanInteract"/>).
        /// </summary>
        [Fact]
        public void HealthApplication_NonLethalHit_StripsCanMove_PreservesOtherCapabilities()
        {
            // Entity at max health with both CanMove and CanInteract.
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Health { Current = 500f, Max = 500f });
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            _world.AddComponent(entity, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanInteract,
            });
            _entityMap.Register(10L, entity);

            // Non-lethal hit: 100 damage out of 500 max.
            _world.Bus.Publish(new DamageAssessedEvent { HitEntityId = 10L, TotalDamage = 100f });
            _world.Bus.SwapBuffers();
            _sys.Run();

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(400f, health.Current);  // HP reduced

            var caps = _world.GetComponent<ActorCapabilityState>(entity);
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanMove),
                "CanMove must be stripped on a non-lethal hit.");
            Assert.True(caps.Capabilities.HasFlag(ActorCapabilities.CanInteract),
                "CanInteract must NOT be stripped on a non-lethal hit.");
        }

        /// <summary>
        /// PACK-M002 SC-2 (regression guard): A lethal hit (HP → 0) must not throw
        /// and must also strip CanMove (both CanMove and CanShoot are cleared at zero HP —
        /// the existing behaviour is preserved).
        /// </summary>
        [Fact]
        public void HealthApplication_LethalHit_DoesNotThrow_CanMoveAlsoStripped()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Health { Current = 500f, Max = 500f });
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            _world.AddComponent(entity, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
            });
            _entityMap.Register(11L, entity);

            // Lethal hit: exactly 500 damage.
            _world.Bus.Publish(new DamageAssessedEvent { HitEntityId = 11L, TotalDamage = 500f });
            _world.Bus.SwapBuffers();

            var ex = Record.Exception(() => _sys.Run());
            Assert.Null(ex);

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(0f, health.Current);

            var caps = _world.GetComponent<ActorCapabilityState>(entity);
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanMove));
        }

        /// <summary>
        /// PACK-M002 SC-1b: When the entity does NOT have an
        /// <see cref="ActorCapabilityState"/> component, a non-lethal hit must still
        /// reduce HP without throwing (skip-if-absent contract).
        /// </summary>
        [Fact]
        public void HealthApplication_NonLethalHit_NoCapabilityState_SkipsSilently()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new Health { Current = 200f, Max = 200f });
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            // No ActorCapabilityState registered.
            _entityMap.Register(12L, entity);

            _world.Bus.Publish(new DamageAssessedEvent { HitEntityId = 12L, TotalDamage = 50f });
            _world.Bus.SwapBuffers();

            var ex = Record.Exception(() => _sys.Run());
            Assert.Null(ex);

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(150f, health.Current);
        }
    }
}
