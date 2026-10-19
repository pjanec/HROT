using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3230</c> (B-8, AW-7 / AW-24; docs/DESIGN_Civilians_And_Crowd_Control.md §7.9) — every person has HANDS: a mount appended last,
    /// empty at start, filled by <see cref="PersonHands.Give"/>, thrown with <c>FireAtPoint</c> on the arc; an empty hand is no weapon.
    /// </summary>
    [Collection(ArmamentCounterCollection.Name)]   // ⭐ Armament.Judge moves the process-wide conduct counter
    public sealed class PersonHandsTests : IDisposable
    {
        private const long CivilianType = 9301, RiflemanType = 9302, CarType = 9303, StoneAmmo = 9390, GrenadeAmmo = 9391, BulletAmmo = 9392;

        private readonly EntityRepository _world = new();
        private readonly TkbDatabase _db = new();

        public PersonHandsTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<WeaponState>();
            _world.RegisterComponent<WeaponMountInfo>();
            _world.RegisterComponent<PartMetadata>();
            _world.RegisterComponent<AmmoLoadout>();
            _world.RegisterComponent<TkbIdentity>();
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<WeaponChannel>();
            _world.RegisterEvent<WeaponFireIntent>();

            _db.Register(Person("Civilian", CivilianType));
            var rifleman = Person("Rifleman", RiflemanType);
            rifleman.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.SmallArms, AmmoGuid = (ulong)BulletAmmo, InitialAmmunition = 30 } } });
            _db.Register(rifleman);
            var car = new TkbTemplate("Car", CarType);
            car.AddDescriptor(new VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.PersonalCar });
            _db.Register(car);
            _db.Register(Round("Stone", StoneAmmo, new WarheadDto { Kind = WarheadKind.Blunt, Indirect = true }));
            _db.Register(Round("Grenade", GrenadeAmmo, new WarheadDto { Kind = WarheadKind.Fragmentation, Indirect = true }));
            _db.Register(Round("Bullet", BulletAmmo, null));
            PersonHands.EnsureOnPersons(_db);
            _world.SetSingletonManaged<ITkbDatabase>(_db);
        }

        public void Dispose() => _world.Dispose();

        private static TkbTemplate Person(string name, long type)
        {
            var t = new TkbTemplate(name, type);
            t.AddDescriptor(new VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.Pedestrian });
            return t;
        }

        private static TkbTemplate Round(string name, long type, WarheadDto? warhead)
        {
            var t = new TkbTemplate(name, type);
            if (warhead != null) t.AddDescriptor(warhead);
            return t;
        }

        private Entity Spawn(long type, Vector3 at, bool civilianClass = false)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new TkbIdentity { TkbType = type });
            _world.AddComponent(e, new EntityInfo { ForceId = ForceId.Hostile });
            if (civilianClass) _world.SetDisType(e, new DISEntityType { Kind = 3, Domain = 1, Category = 2 });
            _db.TryGetByType(type, out var t);
            new CombatTkbTranslator().Inject(_world, e, t!);
            return e;
        }

        /// <summary>⭐ H-1 — every PERSON type ends with ONE hands mount (a man with no suite gets one holding only the hands); a vehicle gets
        /// none; the pass is idempotent. ✅ Red-proof: the Pedestrian test dropped ⇒ the car gets hands ⇒ red.</summary>
        [Fact]
        public void CE3230_EveryPersonEndsWithHands_AVehicleDoesNot_AndTheSecondPassChangesNothing()
        {
            _db.TryGetByType(CivilianType, out var civ);
            _db.TryGetByType(RiflemanType, out var rifle);
            _db.TryGetByType(CarType, out var car);
            Assert.True(PersonHands.IsHands(Assert.Single(civ!.GetDescriptor<WeaponSuiteDto>()!.Mounts)));
            var mounts = rifle!.GetDescriptor<WeaponSuiteDto>()!.Mounts;
            Assert.Equal(2, mounts.Count);
            Assert.False(mounts[0].Hands);
            Assert.True(mounts[1].Hands);
            Assert.Null(car!.GetDescriptor<WeaponSuiteDto>());
            Assert.Equal(0, PersonHands.EnsureOnPersons(_db));
        }

        /// <summary>⭐ H-2 / H-3 — a civilian's hands are his mount 0, EMPTY at spawn (WeaponState 0/0, an empty loadout) and NO weapon to any
        /// "armed" reader: no primary weapon, not armed by status, nothing lethal in hand, nothing to see.</summary>
        [Fact]
        public void CE3230_ACivilianWithEmptyHands_HasNoWeapon()
        {
            var me = Spawn(RiflemanType, Vector3.Zero);
            var civ = Spawn(CivilianType, new Vector3(10f, 0f, 0f), civilianClass: true);
            Assert.Equal(0, _world.GetComponent<WeaponState>(civ).Ammo);
            Assert.Equal(0, _world.GetComponent<AmmoLoadout>(civ).Count);
            Assert.True(PersonHands.HandsOnly(_world, civ));
            Assert.True(WeaponSystems.WeaponOf(_world, civ).IsNull);
            Assert.False(Armament.ArmedByStatus(_world, civ));
            Assert.False(Armament.LethalInHand(_world, civ));
            Assert.Equal(ArmedJudgement.Nothing, Armament.Judge(_world, me, civ, SeeingConditions.ClearDay));
            Assert.Equal(0, PersonHands.MountIndex(_world, civ));
            Assert.Equal(1, PersonHands.MountIndex(_world, me));
        }

        /// <summary>⭐⭐ The frame's acceptance (B-8): a man given 3 stones throws one ON THE ARC and has 2 left — the intent carries the STONE
        /// (the loaded round), and he is still unarmed. ✅ Red-proof: FireAtPoint's <c>ammo: loadedAmmo</c> dropped ⇒ the intent carries 0
        /// and the stones stay 3 ⇒ red.</summary>
        [Fact]
        public unsafe void CE3230_AManGivenThreeStones_ThrowsOne_AndHasTwoLeft_StillUnarmed()
        {
            var civ = Spawn(CivilianType, Vector3.Zero, civilianClass: true);
            Assert.True(PersonHands.Give(_world, civ, StoneAmmo, 3));
            Assert.Equal(3, PersonHands.Rounds(_world, civ, StoneAmmo));
            Assert.Equal(3, _world.GetComponent<WeaponState>(civ).Ammo);

            _world.AddComponent(civ, new WeaponChannel());
            ref var channel = ref _world.GetComponentRW<WeaponChannel>(civ);
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), new FireAtPointParams { Point = new Vector3(12f, 0f, 0f), Mount = 0, Rounds = 1 });
            var executor = new FireAtPointExecutor();
            executor.OnEnter(civ, ref channel, _world);
            executor.Execute(civ, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();

            var intent = Assert.Single(_world.Bus.Read<WeaponFireIntent>().ToArray());
            Assert.Equal(StoneAmmo, intent.AmmoGuid);
            Assert.True(intent.AtPoint);
            Fdp.Toolkit.Combat.Tests.TestMuscle.FireAll(_world);   // ⭐ Q94 A-3 — the muscle's answer spends the stone
            Assert.Equal(2, PersonHands.Rounds(_world, civ, StoneAmmo));
            Assert.Equal(2, _world.GetComponent<WeaponState>(civ).Ammo);
            Assert.False(Armament.LethalInHand(_world, civ));
        }

        /// <summary>⭐ AW-9 — a picked-up LIVE GRENADE is a lethal round in hand: armed in truth, and identified lethal up close by an observer
        /// (a small object: 45 m by day); stones are never lethal, only "something" from afar.</summary>
        [Fact]
        public void CE3230_AGrenadeInHand_IsLethal_StonesAreNot()
        {
            var me = Spawn(RiflemanType, Vector3.Zero);
            var stones = Spawn(CivilianType, new Vector3(10f, 0f, 0f), civilianClass: true);
            var grenade = Spawn(CivilianType, new Vector3(0f, 10f, 0f), civilianClass: true);
            PersonHands.Give(_world, stones, StoneAmmo, 5);
            PersonHands.Give(_world, grenade, GrenadeAmmo, 1);
            Assert.False(Armament.LethalInHand(_world, stones));
            Assert.True(Armament.LethalInHand(_world, grenade));
            Assert.Equal(ArmedJudgement.Nothing, Armament.Judge(_world, me, stones, SeeingConditions.ClearDay));
            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world, me, grenade, SeeingConditions.ClearDay));
            _world.GetComponentRW<SimTransform>(stones).Position = new Vector3(100f, 0f, 0f);
            Assert.Equal(ArmedJudgement.Unidentified, Armament.Judge(_world, me, stones, SeeingConditions.ClearDay));
        }

        /// <summary>⭐ <c>CE-2256</c> (TC-7, §5o) — a live grenade in the hand is lethal only within his THROW (the hands mount's 25 m): at
        /// 10 m identified lethal, at 30 m — still seen (a small object is identified to 45 m by day) — Unidentified: warn, not shoot.
        /// Armed in truth either way (<c>LethalInHand</c>).</summary>
        [Fact]
        public void CE2256_AGrenadeInHand_IsLethalOnlyWithinHisThrow()
        {
            var me = Spawn(RiflemanType, Vector3.Zero);
            var man = Spawn(CivilianType, new Vector3(10f, 0f, 0f), civilianClass: true);
            PersonHands.Give(_world, man, GrenadeAmmo, 1);
            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world, me, man, SeeingConditions.ClearDay));
            _world.GetComponentRW<SimTransform>(man).Position = new Vector3(PersonHands.ThrowRange - 1f, 0f, 0f);
            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world, me, man, SeeingConditions.ClearDay));
            _world.GetComponentRW<SimTransform>(man).Position = new Vector3(30f, 0f, 0f);
            Assert.Equal(ArmedJudgement.Unidentified, Armament.Judge(_world, me, man, SeeingConditions.ClearDay));
            Assert.True(Armament.LethalInHand(_world, man));
        }

        /// <summary>⭐ H-2 / H-5 — the hands hold up to four KINDS; a kind already held is topped up; a fifth kind is refused; when the loaded
        /// kind runs out the next one is ready at once (a hand has no breech).</summary>
        [Fact]
        public void CE3230_FourKindsAtMost_AndTheNextKindIsReadyAtOnce()
        {
            var civ = Spawn(CivilianType, Vector3.Zero, civilianClass: true);
            Assert.True(PersonHands.Give(_world, civ, StoneAmmo, 1));
            Assert.True(PersonHands.Give(_world, civ, StoneAmmo, 2));
            Assert.Equal(3, PersonHands.Rounds(_world, civ, StoneAmmo));
            Assert.True(PersonHands.Give(_world, civ, GrenadeAmmo, 1));
            Assert.True(PersonHands.Give(_world, civ, 9401, 1));
            Assert.True(PersonHands.Give(_world, civ, 9402, 1));
            Assert.False(PersonHands.Give(_world, civ, 9403, 1));
            Assert.Equal(StoneAmmo, _world.GetComponent<AmmoLoadout>(civ).LoadedAmmo);

            ref var load = ref _world.GetComponentRW<AmmoLoadout>(civ);
            load.SetRounds(0, 0);   // the stones are thrown
            Assert.Equal(GrenadeAmmo, PersonHands.ReadyNext(_world, civ));
            Assert.Equal(0f, _world.GetComponent<AmmoLoadout>(civ).SwitchRemaining);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3248</c> (TC-6; Civilians §7.17a) — a THROW is no gunshot: the rifleman who throws a stone from his hands makes no shot
        /// report and heats no barrel; the one who fires his rifle does both. ✅ Red-proofs: <c>Threw</c> dropped from
        /// <c>SoundEmissionSystem</c> ⇒ the thrower has a report ⇒ red; dropped from <c>ThermalHeatSystem</c> ⇒ his barrel warms ⇒ red.
        /// </summary>
        [Fact]
        public void CE3248_AThrow_MakesNoShotReport_AndHeatsNoBarrel_ARifleShotDoes()
        {
            _world.RegisterComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>();
            _world.RegisterComponent<Fdp.Toolkit.Perception.Signatures.ThermalState>();
            _world.RegisterEvent<WeaponFireNotification>();
            _world.RegisterEvent<Fdp.Toolkit.Combat.Contracts.DetonationNotification>();
            Entity Man(float x)
            {
                var e = Spawn(RiflemanType, new Vector3(x, 0f, 0f));
                _world.AddComponent(e, new Fdp.Toolkit.Perception.Signatures.AcousticEmitter { FiringAudibleRange = 800f });
                _world.AddComponent(e, new Fdp.Toolkit.Perception.Signatures.ThermalState { FiringHeatPerShot = 0.2f });
                return e;
            }
            var thrower = Man(0f);
            var shooter = Man(10f);
            int hands = PersonHands.MountIndex(_world, thrower);
            Assert.Equal(1, hands);

            var thrown = new WeaponFireNotification { Shooter = thrower, WeaponIndex = hands, AmmoGuid = StoneAmmo, Quantity = 1 };
            var shot = new WeaponFireNotification { Shooter = shooter, WeaponIndex = 0, AmmoGuid = BulletAmmo, Quantity = 1 };
            Assert.True(PersonHands.Threw(_world, in thrown));
            Assert.False(PersonHands.Threw(_world, in shot));

            _world.Bus.Publish(thrown);
            _world.Bus.Publish(shot);
            _world.Bus.SwapBuffers();
            new Fdp.Toolkit.Perception.Signatures.SoundEmissionSystem().Execute(_world, 0.1f);
            new Fdp.Toolkit.Perception.Signatures.ThermalHeatSystem().Execute(_world, 0.1f);

            Assert.Equal(0f, _world.GetComponentRO<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>(thrower).ShotTimeLeft);
            Assert.True(_world.GetComponentRO<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>(shooter).ShotTimeLeft > 0f);
            Assert.Equal(0f, _world.GetComponentRO<Fdp.Toolkit.Perception.Signatures.ThermalState>(thrower).Heat);
            Assert.True(_world.GetComponentRO<Fdp.Toolkit.Perception.Signatures.ThermalState>(shooter).Heat > 0f);
        }
    }
}
