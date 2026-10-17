using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3232</c> (B-10, AW-5; docs/DESIGN_Civilians_And_Crowd_Control.md §7.11) — a weapon LAID DOWN arms nobody, is never chosen,
    /// fires nothing (held "not deployed"), and crosses to every node through <see cref="WeaponAmmo"/>; the hands are never laid down.
    /// </summary>
    [Collection(ArmamentCounterCollection.Name)]   // ⭐ Armament.Judge moves the process-wide conduct counter
    public sealed class WeaponLayingTests : IDisposable
    {
        private const long RiflemanType = 9501, BulletAmmo = 9590;

        private readonly EntityRepository _world = new();
        private readonly TkbDatabase _db = new();

        public WeaponLayingTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<WeaponState>();
            _world.RegisterComponent<WeaponMountInfo>();
            _world.RegisterComponent<PartMetadata>();
            _world.RegisterComponent<AmmoLoadout>();
            _world.RegisterComponent<TkbIdentity>();
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<WeaponChannel>();
            _world.RegisterComponent<BehaviorState>();
            _world.RegisterComponent<LaidDownWeapons>();
            _world.RegisterEvent<WeaponFireIntent>();

            var rifleman = new TkbTemplate("Rifleman", RiflemanType);
            rifleman.AddDescriptor(new VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.Pedestrian });
            rifleman.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.SmallArms, AmmoGuid = (ulong)BulletAmmo, InitialAmmunition = 30 } } });
            _db.Register(rifleman);
            _db.Register(new TkbTemplate("Bullet", BulletAmmo));
            PersonHands.EnsureOnPersons(_db);
            _world.SetSingletonManaged<ITkbDatabase>(_db);
        }

        public void Dispose() => _world.Dispose();

        private Entity Rifleman(Vector3 at, bool brainHere = true)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new TkbIdentity { TkbType = RiflemanType });
            _world.AddComponent(e, new EntityInfo { ForceId = ForceId.Hostile });
            _world.AddComponent(e, default(BehaviorState));
            _world.SetAuthority<BehaviorState>(e, brainHere);
            _db.TryGetByType(RiflemanType, out var t);
            new CombatTkbTranslator().Inject(_world, e, t!);
            return e;
        }

        /// <summary>⭐ LD-4 / LD-5 — every weapon laid down: no status weapon, nothing lethal on him, no weapon to fire with, the figure
        /// unarmed; the HANDS are not laid down (one weapon mount here); taken up again, he is armed. ✅ Red-proof: the AllLaidDown test
        /// dropped from ArmedByStatus ⇒ red.</summary>
        [Fact]
        public void CE3232_EveryWeaponLaidDown_ArmsNobody_TakenUpHeIsArmedAgain()
        {
            var me = Rifleman(Vector3.Zero);
            var him = Rifleman(new Vector3(20f, 0f, 0f));
            Assert.True(Armament.ArmedByStatus(_world, him));
            Assert.True(Armament.LethalInHand(_world, him));

            Assert.Equal(1, WeaponLaying.LayDownAll(_world, him));   // the rifle — never the hands
            Assert.True(WeaponLaying.AllLaidDown(_world, him));
            Assert.False(WeaponLaying.IsLaidDown(_world, him, PersonHands.MountIndex(_world, him)));
            Assert.False(Armament.ArmedByStatus(_world, him));
            Assert.False(Armament.LethalInHand(_world, him));
            Assert.True(WeaponSystems.WeaponOf(_world, him).IsNull);
            Assert.NotEqual(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world, me, him, SeeingConditions.ClearDay));

            WeaponLaying.LayDownAll(_world, him, down: false);
            Assert.True(Armament.ArmedByStatus(_world, him));
            Assert.Equal(him, WeaponSystems.WeaponOf(_world, him));
        }

        /// <summary>⭐ LD-3 — the AI's FireAtPoint with a laid-down rifle sends no round: held, "not deployed".
        /// ✅ Red-proof: the executor gate dropped ⇒ an intent ⇒ red.</summary>
        [Fact]
        public unsafe void CE3232_ALaidDownRifle_FiresNothing_HeldNotDeployed()
        {
            var him = Rifleman(Vector3.Zero);
            WeaponLaying.LayDown(_world, him, 0);
            _world.AddComponent(him, new WeaponChannel());
            ref var channel = ref _world.GetComponentRW<WeaponChannel>(him);
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), new FireAtPointParams { Point = new Vector3(30f, 0f, 0f), Mount = 0, Rounds = 1 });
            var executor = new FireAtPointExecutor();
            executor.OnEnter(him, ref channel, _world);
            executor.Execute(him, ref channel, _world, 0.016f);
            _world.Bus.SwapBuffers();
            Assert.Empty(_world.Bus.Read<WeaponFireIntent>().ToArray());
            Assert.Equal(Fbt.NodeStatus.Running, channel.Status);
        }

        /// <summary>⭐ LD-2 — the brain's mirror puts the laid-down mounts into <see cref="WeaponAmmo"/>; a node that does NOT run his brain
        /// reads them there. ✅ Red-proof: the mirror's LaidDown copy dropped ⇒ the replica reads armed ⇒ red.</summary>
        [Fact]
        public void CE3232_TheLaidDownBit_IsMirroredIntoWeaponAmmo_AndAReplicaReadsIt()
        {
            var him = Rifleman(Vector3.Zero);
            WeaponLaying.LayDown(_world, him, 0);
            new WeaponAmmoMirrorSystem().Execute(_world, 0.016f);
            var ammo = _world.GetComponentRO<WeaponAmmo>(him);
            Assert.True(ammo.IsLaidDown(0));

            var replica = Rifleman(new Vector3(5f, 0f, 0f), brainHere: false);   // his brain is elsewhere: only the wire knows
            _world.AddComponent(replica, ammo);
            Assert.True(WeaponLaying.IsLaidDown(_world, replica, 0));
            Assert.False(Armament.ArmedByStatus(_world, replica));
        }

        /// <summary>⭐ <c>CE-2241</c> ① (AW-21, R-2027; §5h.5, behaviors) — SURRENDERED is out of the fight: every weapon laid down AND his
        /// hands up. Hands up alone (a feint, the rifle still up) is not; laid down alone is not; an unarmed man with his hands up is not
        /// (CV-13a's hold, not a surrender). ✅ Red-proof: the AllLaidDown term dropped from IsOutOfTheFight ⇒ the hands-up-alone line red;
        /// the ShowsHandsUp term dropped ⇒ the laid-down-alone line red.</summary>
        [Fact]
        public void CE2241_OutOfTheFight_OnlyWhenEveryWeaponIsLaidDown_AndHisHandsAreUp()
        {
            if (!_world.IsComponentTypeRegistered<Gesture>()) _world.RegisterComponent<Gesture>();
            var him = Rifleman(Vector3.Zero);
            Assert.False(Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(_world, him));

            Gestures.Set(_world, him, GestureKind.HandsUp);                  // hands up, the rifle up
            Assert.False(Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(_world, him));
            Gestures.Set(_world, him, GestureKind.None);
            WeaponLaying.LayDownAll(_world, him);                             // laid down, hands down
            Assert.False(Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(_world, him));
            Gestures.Set(_world, him, GestureKind.HandsUp);                  // both: surrendered
            Assert.True(Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(_world, him));

            const long CivilianType = 9502;                                   // a person with no weapon suite: only his hands
            var civ = new TkbTemplate("Civilian", CivilianType);
            civ.AddDescriptor(new VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.Pedestrian });
            _db.Register(civ);
            PersonHands.EnsureOnPersons(_db);
            var man = _world.CreateEntity();
            _world.AddComponent(man, new TkbIdentity { TkbType = CivilianType });
            _world.AddComponent(man, default(BehaviorState));
            _world.SetAuthority<BehaviorState>(man, true);
            _db.TryGetByType(CivilianType, out var ct);
            new CombatTkbTranslator().Inject(_world, man, ct!);
            Gestures.Set(_world, man, GestureKind.HandsUp);
            Assert.False(Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(_world, man));
        }
    }
}
