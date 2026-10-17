using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Tests.Utility;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3234</c> (civilians B-12, AW-14; docs/DESIGN_Civilians_And_Crowd_Control.md §7.14) — what is in his hand: a byte beside
    /// <see cref="WeaponPosture.ActiveMount"/>; a phone or binoculars only when his type carries them; binoculars up see further and narrower and
    /// identify further; a held phone is a small thing an observer sees.
    /// </summary>
    [Collection(ArmamentCounterCollection.Name)]   // ⭐ Judge moves the process-wide conduct counter
    public sealed class HeldItemsTests : IDisposable
    {
        private const long PhoneCivType = 9401, SpotterType = 9402, PistolCivType = 9403, BulletAmmo = 9490;
        private static readonly DISEntityType CivilianDis = new() { Kind = 3, Domain = 1, Category = 2 };

        private readonly UtilityTestWorld _world = new();

        public HeldItemsTests()
        {
            var repo = _world.Repo;
            repo.RegisterComponent<WeaponPosture>();
            repo.RegisterComponent<EntityInfo>();
            repo.RegisterComponent<PerceptionReceptor>();
            repo.RegisterComponent<SensorTag>();
            repo.RegisterManagedComponent<SensorCapability>();
            var phone = new Fdp.Interfaces.TkbTemplate("Civilian with a phone", PhoneCivType);
            phone.AddDescriptor(new PocketItemsDto { Phone = true });
            var spotter = new Fdp.Interfaces.TkbTemplate("Spotter", SpotterType);
            spotter.AddDescriptor(new PocketItemsDto { Binoculars = true });
            var pistol = new Fdp.Interfaces.TkbTemplate("Civilian with a pistol", PistolCivType);
            pistol.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.SmallArms, AmmoGuid = (ulong)BulletAmmo,
                                                                                      Appearance = new ItemAppearanceDto { Look = ItemLook.Pistol } } } });
            _world.UseTkb(phone, spotter, pistol, new Fdp.Interfaces.TkbTemplate("Bullet", BulletAmmo));
        }

        public void Dispose() => _world.Dispose();

        private Entity Man(long type, Vector3 at, bool civilian = true, bool weapon = false)
        {
            var e = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.Repo.AddComponent(e, new WeaponPosture());
            if (weapon) _world.Repo.AddComponent(e, new WeaponState { Ammo = 10, MaxAmmo = 10 });
            _world.SetType(e, type);
            if (civilian) _world.Repo.SetDisType(e, CivilianDis);
            return e;
        }

        /// <summary>
        /// ⭐ HI-2 — he holds only what he has: a civilian's phone yes, binoculars no; None empties his hand; no posture ⇒ nothing changes.
        /// ✅ Red-proof: <c>Set</c> without the <c>Has</c> check ⇒ the civilian holds binoculars ⇒ red.
        /// </summary>
        [Fact]
        public void CE3234_HeHoldsOnlyWhatHisTypeCarries()
        {
            var civ = Man(PhoneCivType, Vector3.Zero);
            Assert.Equal(HeldItemKind.None, HeldItems.Of(_world.Repo, civ));
            Assert.False(HeldItems.Set(_world.Repo, civ, HeldItemKind.Binoculars));
            Assert.False(HeldItems.Set(_world.Repo, civ, HeldItemKind.Weapon));        // he has no weapon
            Assert.True(HeldItems.Set(_world.Repo, civ, HeldItemKind.Phone));
            Assert.Equal(HeldItemKind.Phone, HeldItems.Of(_world.Repo, civ));
            WeaponPostureOf.Idle(_world.Repo, civ, 10f);                                  // lowering the weapon keeps the phone
            Assert.Equal(HeldItemKind.Phone, HeldItems.Of(_world.Repo, civ));
            Assert.True(HeldItems.Set(_world.Repo, civ, HeldItemKind.None));
            Assert.Equal(HeldItemKind.None, HeldItems.Of(_world.Repo, civ));

            var bare = _world.Repo.CreateEntity();
            Assert.False(HeldItems.Set(_world.Repo, bare, HeldItemKind.None));            // no posture
        }

        /// <summary>
        /// ⭐ HI-4 — binoculars UP: his visual sensor reaches twice as far through a 10° cone; down, his naked eye. And he identifies four times
        /// further: a civilian's pistol at 200 m by day (naked eye 60 m) is identified lethal through them. ✅ Red-proofs: <c>Optics</c>
        /// without the binoculars branch ⇒ 300 m ⇒ red; <c>SeeingConditions.Of</c> with optics 1 ⇒ Unidentified ⇒ red.
        /// </summary>
        [Fact]
        public void CE3234_BinocularsUp_SeeFurtherAndNarrower_AndIdentifyFurther()
        {
            var spotter = Man(SpotterType, Vector3.Zero, civilian: false);
            _world.Repo.AddComponent(spotter, new PerceptionReceptor { VisionRange = 300f, FieldOfViewCos = -1f });
            var sensor = _world.Repo.CreateEntity();
            var armed = Man(PistolCivType, new Vector3(200f, 0f, 0f), weapon: true);

            Assert.True(VisualPerception.Optics(_world.Repo, sensor, spotter, out float range, out float fovCos));
            Assert.Equal((300f, -1f), (range, fovCos));
            Assert.Equal(ArmedJudgement.Unidentified, Armament.Judge(_world.Repo, spotter, armed));

            Assert.True(HeldItems.Set(_world.Repo, spotter, HeldItemKind.Binoculars));
            Assert.True(VisualPerception.Optics(_world.Repo, sensor, spotter, out range, out fovCos));
            Assert.Equal(600f, range);
            Assert.Equal(MathF.Cos(5f * MathF.PI / 180f), fovCos, precision: 5);
            Assert.Equal(HeldItems.BinocularsOptics, SeeingConditions.Of(_world.Repo, spotter, armed).Optics);
            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world.Repo, spotter, armed));
        }

        /// <summary>
        /// ⭐ HI-5 — a phone in his hand is a small thing an observer SEES: far off (100 m, beyond the 45 m a 15 cm object is identified at) he
        /// reads Unidentified, near (10 m) Nothing — never lethal; in his pocket, Nothing at any range. ✅ Red-proof: <c>Judge</c> without the
        /// held item ⇒ Nothing at 100 m ⇒ red.
        /// </summary>
        [Fact]
        public void CE3234_APhoneInHisHand_IsSeen_NeverLethal()
        {
            var observer = Man(SpotterType, Vector3.Zero, civilian: false);
            var far = Man(PhoneCivType, new Vector3(100f, 0f, 0f));
            var near = Man(PhoneCivType, new Vector3(10f, 0f, 0f));
            Assert.Equal(ArmedJudgement.Nothing, Armament.Judge(_world.Repo, observer, far));

            Assert.True(HeldItems.Set(_world.Repo, far, HeldItemKind.Phone));
            Assert.True(HeldItems.Set(_world.Repo, near, HeldItemKind.Phone));
            Assert.Equal(ArmedJudgement.Unidentified, Armament.Judge(_world.Repo, observer, far));
            Assert.Equal(ArmedJudgement.Nothing, Armament.Judge(_world.Repo, observer, near));
        }
    }
}
