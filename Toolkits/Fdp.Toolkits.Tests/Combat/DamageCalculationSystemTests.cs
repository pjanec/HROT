using System;
using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DamageCalculationSystem"/> (BS1-T012).
    /// </summary>
    public class DamageCalculationSystemTests : IDisposable
    {
        private readonly EntityRepository    _world;
        private readonly DamageCalculationSystem _sys;

        public DamageCalculationSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            _world.RegisterEvent<DetonationNotification>();
            _world.RegisterEvent<DamageAssessedEvent>();

            _sys = new DamageCalculationSystem();
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Entity SpawnTarget(bool authoritative = true)
        {
            var entity = _world.CreateEntity();
            if (authoritative)
                _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            else
                _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            return entity;
        }

        private void PublishDetonation(Entity target)
        {
            _world.Bus.Publish(new DetonationNotification
            {
                Shooter = default,
                Target  = target,
                HitX    = 0f, HitY = 0f, HitZ = 0f,
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
            var target = SpawnTarget(authoritative: true);
            PublishDetonation(target: target);

            _sys.Execute(_world, 0.016f);

            _world.Bus.SwapBuffers();
            var events = _world.Bus.Read<DamageAssessedEvent>();

            Assert.Equal(1, events.Length);
            Assert.Equal(target, events[0].HitEntity);
            Assert.Equal(CombatConstants.DefaultBulletDamage, events[0].TotalDamage);
        }

        // ── SC-2: Non-authority → DamageAssessedEvent is still published ────────

        /// <summary>
        /// BS1-T012 SC-2: <see cref="DamageCalculationSystem"/> runs exclusively on the
        /// Muscle node and does not gate on entity ownership. When a
        /// <see cref="DetonationNotification"/> arrives for a live entity that is owned
        /// by a remote node (Brain), the Muscle still publishes
        /// <see cref="DamageAssessedEvent"/> so the Brain can apply the damage.
        /// </summary>
        [Fact]
        public void DamageCalculation_PublishesDamageAssessedEvent_EvenWhenNotAuthoritative()
        {
            var target2 = SpawnTarget(authoritative: false);
            PublishDetonation(target: target2);

            _sys.Execute(_world, 0.016f);

            _world.Bus.SwapBuffers();
            var events = _world.Bus.Read<DamageAssessedEvent>();

            Assert.Equal(1, events.Length);
            Assert.Equal(target2, events[0].HitEntity);
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
            var entity = SpawnTarget(authoritative: true);
            _world.AddComponent(entity, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });

            PublishDetonation(target: entity);
            _sys.Execute(_world, 0.016f);

            var health = _world.GetComponent<Fdp.Toolkit.Combat.Components.Health>(entity);
            Assert.Equal(100f, health.Current);
        }

        // ── Unknown entity → skipped gracefully ──────────────────────────────

        /// <summary>
        /// A dead entity (not alive in the world) must be skipped silently.
        /// </summary>
        [Fact]
        public void DamageCalculation_SkipsDeadEntity()
        {
            // Create and immediately destroy the entity before firing.
            var deadEntity = _world.CreateEntity();
            _world.AddComponent(deadEntity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            _world.DestroyEntity(deadEntity);
            PublishDetonation(target: deadEntity);

            var ex = Record.Exception(() => _sys.Execute(_world, 0.016f));
            Assert.Null(ex);

            _world.Bus.SwapBuffers();
            var events = _world.Bus.Read<DamageAssessedEvent>();
            Assert.Equal(0, events.Length);
        }
    }
}
