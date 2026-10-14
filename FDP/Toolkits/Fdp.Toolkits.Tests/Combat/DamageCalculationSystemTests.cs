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

        // ── CE-3071: ammunition vs armour (ArmorModel, design §9) ─────────────────────────────

        private const long TankType = 103;   // armour 500 front / 250 side / 150 rear

        /// <summary>A world with a TKB holding a T-72-shaped platform, and a tank facing east (+X) at the origin.</summary>
        private Entity SpawnArmouredTank(Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto? platform = null, params Fdp.Interfaces.TkbTemplate[] more)
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t  = new Fdp.Interfaces.TkbTemplate("Tank", TankType);
            t.AddDescriptor(platform ?? new Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto { MaxHealth = 2500, ArmorFront = 500, ArmorSide = 250, ArmorRear = 150 });
            db.Register(t);
            foreach (var m in more) db.Register(m);
            _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            var tank = SpawnTarget();
            _world.AddComponent(tank, new SimTransform { Position = System.Numerics.Vector3.Zero, Rotation = System.Numerics.Quaternion.Identity });
            _world.AddComponent(tank, new TkbIdentity { TkbType = TankType });
            return tank;
        }

        private Entity ShooterAt(float x, float y)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new System.Numerics.Vector3(x, y, 0), Rotation = System.Numerics.Quaternion.Identity });
            return e;
        }

        private float DamageOf(Entity shooter, Entity target, float penetration, float damage)
        {
            _world.Bus.Publish(new DetonationNotification { Shooter = shooter, Target = target, Penetration = penetration, Damage = damage });
            _world.Bus.SwapBuffers();
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();
            var events = _world.Bus.Read<DamageAssessedEvent>();
            Assert.Equal(1, events.Length);
            return events[0].TotalDamage;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3071</c> — the damage depends on the round and the FACE it strikes: a 125 mm (600 mm, 1100) defeats a
        /// T-72's side (250) and rear but at the front (500, r = 1.2) is just certain; a 25 mm (60 mm) does nothing to it from
        /// any side. ⛔ Was a flat 25 for every hit.
        /// </summary>
        [Fact]
        public void CE3071_TheDamage_DependsOnTheRound_AndTheFaceItStrikes()
        {
            var tank = SpawnArmouredTank();
            var front = ShooterAt(100, 0);    // the tank faces +X
            var side  = ShooterAt(0, 100);
            var rear  = ShooterAt(-100, 0);

            Assert.Equal(1100f, DamageOf(front, tank, 600, 1100), precision: 3);   // r = 1.2 ⇒ P 1
            Assert.Equal(1100f, DamageOf(side,  tank, 600, 1100), precision: 3);
            Assert.Equal(0f,    DamageOf(front, tank, 60, 60),    precision: 3);   // 25 mm: r = 0.12
            Assert.Equal(0f,    DamageOf(rear,  tank, 60, 60),    precision: 3);   // 150 mm rear: r = 0.4
            Assert.Equal(550f,  DamageOf(front, tank, 500, 1100), precision: 3);   // r = 1 ⇒ P 0.5 — expected value, no dice (A2)
            Assert.Equal(1100f, DamageOf(rear,  tank, 200, 1100), precision: 3);   // the weak rear: r = 1.33
        }

        /// <summary>
        /// ⭐ <c>CE-3071</c> — an UNKNOWN munition (penetration 0: an external detonation, or a weapon with no TKB numbers) keeps
        /// today's flat 25 even on armour — never 0, which would make every such shot harmless.
        /// </summary>
        [Fact]
        public void CE3071_AnUnknownMunition_KeepsTheFlatDamage_EvenOnArmour()
        {
            var tank = SpawnArmouredTank();
            Assert.Equal(CombatConstants.DefaultBulletDamage, DamageOf(ShooterAt(100, 0), tank, 0, 0), precision: 3);
        }

        /// <summary>⭐ <c>CE-3071</c> — the shooter gone (despawned before its round lands): the face comes from the hit point.</summary>
        [Fact]
        public void CE3071_WithTheShooterGone_TheFaceComesFromTheHitPoint()
        {
            var tank = SpawnArmouredTank();
            _world.Bus.Publish(new DetonationNotification { Shooter = default, Target = tank, HitX = -2, HitY = 0, HitZ = 1, Penetration = 200, Damage = 1100 });
            _world.Bus.SwapBuffers();
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();
            Assert.Equal(1100f, _world.Bus.Read<DamageAssessedEvent>()[0].TotalDamage, precision: 3);   // rear 150: defeated
        }

        // ── M1 / M2 (docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md §5) ─────────────────────────────

        private const long HeatType = 6009, ApType = 6006;

        /// <summary>The damage of a round TRAVELLING along <paramref name="travel"/> (the arrival the hit resolution carries).</summary>
        private float DamageArriving(Entity target, System.Numerics.Vector3 travel, float penetration, float damage, long ammo = 0)
        {
            _world.Bus.Publish(new DetonationNotification
            {
                Shooter = default, Target = target, Penetration = penetration, Damage = damage, Ammo = ammo,
                TravelX = travel.X, TravelY = travel.Y, TravelZ = travel.Z,
            });
            _world.Bus.SwapBuffers();
            _sys.Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();
            var events = _world.Bus.Read<DamageAssessedEvent>();
            Assert.Equal(1, events.Length);
            return events[0].TotalDamage;
        }

        private static Fdp.Interfaces.TkbTemplate Munition(long type, Fdp.Toolkit.Tkb.Domain.WarheadKind kind)
        {
            var t = new Fdp.Interfaces.TkbTemplate($"round {type}", type);
            if (kind != Fdp.Toolkit.Tkb.Domain.WarheadKind.Kinetic)
                t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WarheadDto { Kind = kind, ExplosiveKg = 0.7f });
            return t;
        }

        /// <summary>
        /// ⭐⭐ M1 (D-A) — a round from a window 12 m above the hull's middle: from 10 m away it arrives ~50° down and strikes the
        /// ROOF (40 mm: defeated); from 50 m, ~14° down, the SIDE (250 mm: not). ⛔ Before M1 the face came from the shooter's
        /// horizontal bearing — always the side.
        /// </summary>
        [Fact]
        public void M1_ARoundFromAFourthFloorWindow_HitsTheRoof_AtTenMetres_TheSideAtFifty()
        {
            var tank = SpawnArmouredTank(new Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto
                { MaxHealth = 2500, ArmorFront = 500, ArmorSide = 250, ArmorRear = 150, ArmorTop = 40 });
            var middle = new System.Numerics.Vector3(0, 0, 1.2f);
            Assert.Equal(1100f, DamageArriving(tank, middle - new System.Numerics.Vector3(0, 10, 13.2f), 200, 1100), precision: 3);
            Assert.Equal(0f,    DamageArriving(tank, middle - new System.Numerics.Vector3(0, 50, 13.2f), 200, 1100), precision: 3);
        }

        /// <summary>
        /// ⭐⭐ M2 (D-B) — a SHAPED CHARGE reads the face's …Ce column (front 800 vs a 600 mm jet: not defeated), and a face that
        /// declares none reads its kinetic value (side 250: defeated); a KINETIC round of the same 600 mm still reads the kinetic
        /// front (500: defeated).
        /// </summary>
        [Fact]
        public void M2_AShapedCharge_ReadsTheCeColumn_AbsentMeansTheKineticValue()
        {
            var tank = SpawnArmouredTank(new Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto
                { MaxHealth = 2500, ArmorFront = 500, ArmorSide = 250, ArmorRear = 150, ArmorFrontCe = 800 },
                Munition(HeatType, Fdp.Toolkit.Tkb.Domain.WarheadKind.ShapedCharge), Munition(ApType, Fdp.Toolkit.Tkb.Domain.WarheadKind.Kinetic));
            var fromFront = new System.Numerics.Vector3(-1, 0, 0);   // travelling west: arrives at the east-facing front
            var fromSide  = new System.Numerics.Vector3(0, -1, 0);
            Assert.Equal(0f,    DamageArriving(tank, fromFront, 600, 1100, HeatType), precision: 3);   // 600 / 800 = 0.75 ⇒ P 0
            Assert.Equal(1100f, DamageArriving(tank, fromSide,  600, 1100, HeatType), precision: 3);   // no SideCe ⇒ 250
            Assert.Equal(1100f, DamageArriving(tank, fromFront, 600, 1100, ApType),   precision: 3);   // kinetic: 500
        }

        /// <summary>⭐ M1 — a type that declares no roof (ArmorTop 0) reads its REAR there (150), never "no armour" (0 ⇒ certain).</summary>
        [Fact]
        public void M2_NoArmourTop_ReadsArmourRear()
        {
            var tank = SpawnArmouredTank();
            // straight down: 160 / 150 = 1.067 ⇒ P = 0.667
            Assert.Equal(1100f * (160f / 150f - 0.8f) / 0.4f, DamageArriving(tank, new System.Numerics.Vector3(0, 0, -1), 160, 1100), precision: 2);
        }
    }
}
