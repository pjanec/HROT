using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Tests.Utility;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Combat
{
    /// <summary>⭐ The process-wide <see cref="Armament.ConductJudgements"/> counter is read by these rails: run them alone, so no other
    /// class's civilians move it mid-test.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class ArmamentCounterCollection { public const string Name = "Armament counter (CE-3229)"; }

    /// <summary>
    /// ⭐⭐ <c>CE-3229</c> (B-7; docs/DESIGN_Civilians_And_Crowd_Control.md §5h AW-15…AW-24, §7.8) — THE armed rule: a combatant on STATUS,
    /// exactly as before; a civilian by what the observer can IDENTIFY (appearance × range × light × optics). Numbers: identification =
    /// 300 m per metre of size by day (a rifle 300 m, a pistol 60 m), a tenth of that at night.
    /// </summary>
    [Collection(ArmamentCounterCollection.Name)]
    public sealed class ArmamentTests : IDisposable
    {
        private const long RiflemanType = 9201, PistolCivType = 9202, ToyGunCivType = 9203, PhoneCivType = 9204, StonesCivType = 9205,
                           KnifeCivType = 9206, BatonLauncherType = 9207, FlaresOnlyType = 9208;
        private const long BulletAmmo = 9290, BatonRoundAmmo = 9291, StoneAmmo = 9292, FlareAmmo = 9293;

        private static readonly DISEntityType CivilianDis = new() { Kind = 3, Domain = 1, Category = 2 };

        private static readonly SeeingConditions Day = new(1f, float.PositiveInfinity, 1f);
        private static readonly SeeingConditions Night = new(0.001f, float.PositiveInfinity, 1f);
        private static readonly SeeingConditions DayBinoculars = new(1f, float.PositiveInfinity, 4f);

        private readonly UtilityTestWorld _world = new();

        public ArmamentTests()
        {
            _world.Repo.RegisterComponent<EntityInfo>();
            _world.UseTkb(
                Typed("Rifleman", RiflemanType, new WeaponMountDto { Category = WeaponCategory.SmallArms, AmmoGuid = (ulong)BulletAmmo }),
                Typed("Civilian with a pistol", PistolCivType, new WeaponMountDto
                {
                    Category = WeaponCategory.SmallArms, AmmoGuid = (ulong)BulletAmmo, Appearance = new ItemAppearanceDto { Look = ItemLook.Pistol },
                }),
                Typed("Civilian with a toy gun", ToyGunCivType, new WeaponMountDto
                {
                    Category = WeaponCategory.SmallArms, Appearance = new ItemAppearanceDto { Look = ItemLook.Rifle, Lethal = true },
                }),
                Typed("Civilian with a phone", PhoneCivType, new WeaponMountDto
                {
                    Category = WeaponCategory.Unknown, Appearance = new ItemAppearanceDto { Look = ItemLook.SmallObject, Lethal = false },
                }),
                Typed("Civilian with stones", StonesCivType, new WeaponMountDto { Category = WeaponCategory.Grenade, AmmoGuid = (ulong)StoneAmmo }),
                Typed("Civilian with a knife", KnifeCivType, new WeaponMountDto
                {
                    Category = WeaponCategory.Unknown, Appearance = new ItemAppearanceDto { Look = ItemLook.SmallObject, SizeM = 0.3f, Lethal = true, ReachM = 2f },
                }),
                Typed("Police, baton launcher only", BatonLauncherType, new WeaponMountDto { Category = WeaponCategory.Grenade, AmmoGuid = (ulong)BatonRoundAmmo }),
                Typed("Flares only", FlaresOnlyType, new WeaponMountDto { Category = WeaponCategory.Countermeasure, AmmoGuid = (ulong)FlareAmmo }),
                Round("Bullet", BulletAmmo, null),
                Round("Baton round", BatonRoundAmmo, WarheadKind.Blunt),
                Round("Stone", StoneAmmo, WarheadKind.Blunt),
                Round("Flare", FlareAmmo, null));
        }

        public void Dispose() => _world.Dispose();

        private static Fdp.Interfaces.TkbTemplate Typed(string name, long type, WeaponMountDto mount)
        {
            var t = new Fdp.Interfaces.TkbTemplate(name, type);
            t.AddDescriptor(new WeaponSuiteDto { Mounts = { mount } });
            return t;
        }

        private static Fdp.Interfaces.TkbTemplate Round(string name, long type, WarheadKind? kind)
        {
            var t = new Fdp.Interfaces.TkbTemplate(name, type);
            if (kind is { } k) t.AddDescriptor(new WarheadDto { Kind = k });
            return t;
        }

        private Entity Man(long type, Vector3 at, ForceId? side = null, bool civilian = false)
        {
            var e = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.Repo.AddComponent(e, new Health { Current = 100, Max = 100 });
            _world.Repo.AddComponent(e, new WeaponState { Ammo = 10, MaxAmmo = 10 });
            _world.SetType(e, type);
            if (side is { } s) _world.Repo.AddComponent(e, new EntityInfo { ForceId = s });
            if (civilian) _world.Repo.SetDisType(e, CivilianDis);
            return e;
        }

        private Entity Observer() => Man(RiflemanType, Vector3.Zero, ForceId.Friend);

        /// <summary>⭐⭐ AW-21 / AW-24 — <c>ua-armed-status</c>'s in-process twin: an enemy rifleman AT 300 m AT NIGHT is armed by STATUS (danger
        /// 1, exactly as before), and a whole military read — every soldier on every soldier — enters the conduct path ZERO times (the counter
        /// rail). ✅ Red-proof: <see cref="Armament.IsCombatant"/> returning false ⇒ the night rifleman reads Unidentified and the counter moves.</summary>
        [Fact]
        public void CE3229_AnEnemyRiflemanAtNight_IsArmedByStatus_AndTheConductPathIsNeverEntered()
        {
            var me = Observer();
            var enemy = Man(RiflemanType, new Vector3(300f, 0f, 0f), ForceId.Hostile);
            var unsided = Man(RiflemanType, new Vector3(0f, 300f, 0f));   // no EntityInfo — a combatant, as ThreatDanger read it before
            long before = Armament.ConductJudgements;

            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world.Repo, me, enemy, Night));
            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world.Repo, me, unsided, Night));
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, me, enemy));
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, enemy, me));
            Assert.Equal(1f, ThreatDanger.OwnStrength(_world.Repo, me));

            Assert.Equal(before, Armament.ConductJudgements);
        }

        /// <summary>⭐⭐ AW-15 (note 1's acceptance) — a pistol is identified lethal at 20 m by day, UNIDENTIFIED at 200 m at night, and
        /// identified again at 200 m by day through binoculars (×4 ⇒ 240 m). Every one of those judgements goes through the conduct path.</summary>
        [Fact]
        public void CE3229_APistol_IsIdentifiedAt20m_UnidentifiedAt200mAtNight_AndIdentifiedThroughBinoculars()
        {
            var me = Observer();
            var near = Man(PistolCivType, new Vector3(20f, 0f, 0f), ForceId.Hostile, civilian: true);
            var far = Man(PistolCivType, new Vector3(200f, 0f, 0f), ForceId.Hostile, civilian: true);
            long before = Armament.ConductJudgements;

            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world.Repo, me, near, Day));
            Assert.Equal(ArmedJudgement.Unidentified, Armament.Judge(_world.Repo, me, far, Night));
            Assert.Equal(ArmedJudgement.Unidentified, Armament.Judge(_world.Repo, me, far, Day));   // 60 m by day, unaided
            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world.Repo, me, far, DayBinoculars));

            Assert.Equal(before + 4, Armament.ConductJudgements);
        }

        /// <summary>⭐ AW-15 — a toy gun LOOKS like a rifle and is identified lethal at 20 m (the training outcome); a phone at 150 m is
        /// "something" (unidentified) and at 20 m nothing — never lethal at any range.</summary>
        [Fact]
        public void CE3229_AToyGunIsLethalToTheEye_APhoneIsNeverLethal()
        {
            var me = Observer();
            Assert.Equal(ArmedJudgement.IdentifiedLethal,
                Armament.Judge(_world.Repo, me, Man(ToyGunCivType, new Vector3(20f, 0f, 0f), ForceId.Hostile, civilian: true), Day));
            Assert.Equal(ArmedJudgement.Unidentified,
                Armament.Judge(_world.Repo, me, Man(PhoneCivType, new Vector3(150f, 0f, 0f), ForceId.Hostile, civilian: true), Day));
            Assert.Equal(ArmedJudgement.Nothing,
                Armament.Judge(_world.Repo, me, Man(PhoneCivType, new Vector3(20f, 0f, 0f), ForceId.Hostile, civilian: true), Day));
        }

        /// <summary>⭐ AW-2 / note 2 — a Hostile-flipped civilian holding only STONES is unarmed: nothing up close, "something" far off — and
        /// to <see cref="ThreatDanger"/> both read <see cref="ThreatDanger.Unarmed"/> (A-6). A pistol at 20 m reads 1.</summary>
        [Fact]
        public void CE3229_StonesArmNobody_AndThreatDangerReadsTheJudgement()
        {
            var me = Observer();
            var stonesNear = Man(StonesCivType, new Vector3(10f, 0f, 0f), ForceId.Hostile, civilian: true);
            var stonesFar = Man(StonesCivType, new Vector3(100f, 0f, 0f), ForceId.Hostile, civilian: true);
            Assert.Equal(ArmedJudgement.Nothing, Armament.Judge(_world.Repo, me, stonesNear, Day));
            Assert.Equal(ArmedJudgement.Unidentified, Armament.Judge(_world.Repo, me, stonesFar, Day));

            // no World Environment in this world ⇒ ThreatDanger judges under a clear day (A-4)
            Assert.Equal(ThreatDanger.Unarmed, ThreatDanger.Of(_world.Repo, me, stonesNear));
            Assert.Equal(ThreatDanger.Unarmed, ThreatDanger.Of(_world.Repo, me, stonesFar));
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, me, Man(PistolCivType, new Vector3(20f, 0f, 0f), ForceId.Hostile, civilian: true)));
            Assert.Equal(ThreatDanger.Unarmed, ThreatDanger.Of(_world.Repo, me, Man(PistolCivType, new Vector3(200f, 0f, 0f), ForceId.Hostile, civilian: true)));
        }

        /// <summary>⭐ AW-19 — a knife is lethal only within its reach (2 m): at 1.5 m identified lethal, at 10 m identified but out of reach
        /// ⇒ "warn" (Unidentified).</summary>
        [Fact]
        public void CE3229_AKnifeIsLethalOnlyWithinItsReach()
        {
            var me = Observer();
            Assert.Equal(ArmedJudgement.IdentifiedLethal,
                Armament.Judge(_world.Repo, me, Man(KnifeCivType, new Vector3(1.5f, 0f, 0f), ForceId.Hostile, civilian: true), Day));
            Assert.Equal(ArmedJudgement.Unidentified,
                Armament.Judge(_world.Repo, me, Man(KnifeCivType, new Vector3(10f, 0f, 0f), ForceId.Hostile, civilian: true), Day));
        }

        /// <summary>⭐ A-2 — a NEUTRAL non-civilian is judged by conduct (an unidentified neutral soldier at night is "something"); a civilian
        /// class man of the FRIENDLY side is judged by conduct too ("the Civilian class of any side").</summary>
        [Fact]
        public void CE3229_ANeutralSoldierAndAFriendlyCivilian_AreJudgedByConduct()
        {
            var me = Observer();
            var neutralSoldier = Man(RiflemanType, new Vector3(400f, 0f, 0f), ForceId.Neutral);
            Assert.False(Armament.IsCombatant(_world.Repo, neutralSoldier));
            Assert.Equal(ArmedJudgement.Unidentified, Armament.Judge(_world.Repo, me, neutralSoldier, Night));
            var friendlyCivilian = Man(PistolCivType, new Vector3(20f, 0f, 0f), ForceId.Friend, civilian: true);
            Assert.False(Armament.IsCombatant(_world.Repo, friendlyCivilian));
        }

        /// <summary>⭐ A-7 — the TRUTH form: a rifleman is armed; a police unit with ONLY a baton launcher, and a flares-only aircraft, are not
        /// (AW-2, CE-2188) — so their own strength is health × <see cref="ThreatDanger.Unarmed"/>. ✅ Red-proof: <see cref="ItemAppearances.FiresLethal"/>
        /// returning true ⇒ the baton launcher reads armed ⇒ red.</summary>
        [Fact]
        public void CE3229_LethalInHand_TheBatonLauncherAndTheFlaresArmNobody()
        {
            var rifleman = Man(RiflemanType, Vector3.Zero, ForceId.Friend);
            var police = Man(BatonLauncherType, Vector3.Zero, ForceId.Friend);
            var flares = Man(FlaresOnlyType, Vector3.Zero, ForceId.Friend);
            Assert.True(Armament.LethalInHand(_world.Repo, rifleman));
            Assert.False(Armament.LethalInHand(_world.Repo, police));
            Assert.False(Armament.LethalInHand(_world.Repo, flares));
            Assert.Equal(ThreatDanger.Unarmed, ThreatDanger.OwnStrength(_world.Repo, police), precision: 4);
            Assert.Equal(1f, ThreatDanger.OwnStrength(_world.Repo, rifleman), precision: 4);
        }

        /// <summary>⭐ A-4 — the identification range: a rifle 300 m and a pistol 60 m by day, a pistol 6 m at night, ×4 through binoculars,
        /// capped by the visibility (fog).</summary>
        [Fact]
        public void CE3229_IdentificationRange_SizeLightOpticsAndVisibility()
        {
            Assert.Equal(300f, Identification.RangeM(1.0f, Day), precision: 3);
            Assert.Equal(60f, Identification.RangeM(0.2f, Day), precision: 3);
            Assert.Equal(6f, Identification.RangeM(0.2f, new SeeingConditions(0f, float.PositiveInfinity, 1f)), precision: 3);
            Assert.Equal(240f, Identification.RangeM(0.2f, DayBinoculars), precision: 3);
            Assert.Equal(50f, Identification.RangeM(1.0f, new SeeingConditions(1f, 50f, 1f)), precision: 3);
        }

        /// <summary>⭐ AW-24 — an item's appearance DEFAULTS from its weapon category: a rifle-class weapon is a 1 m rifle, a thrown one a small
        /// object, a dispenser nothing; lethality from its rounds.</summary>
        [Fact]
        public void CE3229_AppearanceDefaultsFromTheCategory()
        {
            var rifle = ItemAppearances.Of(_world.Repo, new WeaponMountDto { Category = WeaponCategory.SmallArms, AmmoGuid = (ulong)BulletAmmo });
            Assert.Equal(ItemLook.Rifle, rifle.Look); Assert.Equal(1f, rifle.SizeM); Assert.True(rifle.Lethal);
            var stone = ItemAppearances.Of(_world.Repo, new WeaponMountDto { Category = WeaponCategory.Grenade, AmmoGuid = (ulong)StoneAmmo });
            Assert.Equal(ItemLook.SmallObject, stone.Look); Assert.False(stone.Lethal);
            Assert.Equal(ItemLook.None, ItemAppearances.Of(_world.Repo, new WeaponMountDto { Category = WeaponCategory.Countermeasure }).Look);
        }
    }
}
