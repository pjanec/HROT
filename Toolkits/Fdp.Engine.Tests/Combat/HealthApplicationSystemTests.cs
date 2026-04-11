using System;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Replication.Components;
using Xunit;

namespace FDP.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="HealthApplicationSystem"/> (BS1-T014).
    /// </summary>
    public class HealthApplicationSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly HealthApplicationSystem _sys;

        public HealthApplicationSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterEvent<DamageAssessedEvent>();

            _sys = new HealthApplicationSystem();
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();
            _world.Dispose();
        }

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private Entity SpawnTarget(float currentHealth, float maxHealth = 100f,
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

            return entity;
        }

        private void PublishEvent(Entity hitEntity, float totalDamage)
        {
            _world.Bus.Publish(new DamageAssessedEvent
            {
                HitEntity   = hitEntity,
                TotalDamage = totalDamage,
            });
            _world.Bus.SwapBuffers();
        }

        // â”€â”€ SC-1: Health decremented â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// BS1-T014 SC-1: When an authoritative node receives a <see cref="DamageAssessedEvent"/>
        /// for a known entity, <see cref="Health.Current"/> must be reduced by the given amount.
        /// </summary>
        [Fact]
        public void HealthApplication_DecrementsHealth_WhenAuthoritative()
        {
            var entity = SpawnTarget(currentHealth: 100f);

            PublishEvent(hitEntity: entity, totalDamage: 30f);
            _sys.Run();

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(70f, health.Current);
        }

        // â”€â”€ SC-2: Health cannot go below zero â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// BS1-T014 SC-2: <see cref="Health.Current"/> must be clamped to 0 and never go negative.
        /// </summary>
        [Fact]
        public void HealthApplication_ClampsHealthToZero_WhenDamageExceedsCurrentHP()
        {
            var entity = SpawnTarget(currentHealth: 10f);

            PublishEvent(hitEntity: entity, totalDamage: 50f);
            _sys.Run();

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(0f, health.Current);
        }

        // â”€â”€ SC-3: Zero HP strips capabilities â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// BS1-T014 SC-3: When <see cref="Health.Current"/> reaches 0, both
        /// <see cref="ActorCapabilities.CanMove"/> and <see cref="ActorCapabilities.CanShoot"/>
        /// must be cleared from <see cref="ActorCapabilityState"/>.
        /// </summary>
        [Fact]
        public void HealthApplication_StripsCapabilities_WhenHealthReachesZero()
        {
            var entity = SpawnTarget(currentHealth: 10f, addCapabilities: true);

            PublishEvent(hitEntity: entity, totalDamage: 50f);
            _sys.Run();

            var caps = _world.GetComponent<ActorCapabilityState>(entity);
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanMove));
            Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanShoot));
        }

        // â”€â”€ SC-4: Non-authority â†’ no health change â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// BS1-T014 SC-4: A non-authoritative node must not alter the health of the entity.
        /// </summary>
        [Fact]
        public void HealthApplication_SkipsUpdate_WhenNotAuthoritative()
        {
            var entity = SpawnTarget(currentHealth: 100f, authoritative: false);

            PublishEvent(hitEntity: entity, totalDamage: 30f);
            _sys.Run();

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(100f, health.Current);
        }

        // â”€â”€ PACK-M002: Non-lethal hit strips CanMove only â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

            // Non-lethal hit: 100 damage out of 500 max.
            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = entity, TotalDamage = 100f });
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
        /// PACK-M002 SC-2 (regression guard): A lethal hit (HP â†’ 0) must not throw
        /// and must also strip CanMove (both CanMove and CanShoot are cleared at zero HP â€”
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

            // Lethal hit: exactly 500 damage.
            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = entity, TotalDamage = 500f });
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

            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = entity, TotalDamage = 50f });
            _world.Bus.SwapBuffers();

            var ex = Record.Exception(() => _sys.Run());
            Assert.Null(ex);

            var health = _world.GetComponent<Health>(entity);
            Assert.Equal(150f, health.Current);
        }
    }
}
