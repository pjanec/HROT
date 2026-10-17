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

        // ── ⭐ CE-2238 part 3 (AW-22, R-2028; §5h.6) — the KNOWN combatant, per unit ──────────────────────────────────────────────────

        /// <summary>A unit with a memory that perceived <paramref name="contact"/> <paramref name="ticksAgo"/> ticks ago.</summary>
        private unsafe Entity Watcher(Vector3 at, Entity contact, uint ticksAgo)
        {
            var e = Observer();
            _world.Repo.GetComponentRW<SimTransform>(e).Position = at;
            var mem = new Fdp.Toolkit.Perception.Components.TargetMemory();
            if (!contact.IsNull)
                Fdp.Toolkit.Perception.Components.TargetMemory.AddOrUpdateTarget(ref mem, (long)contact.PackedValue, 150f, 0f, 1f,
                    _world.Repo.SimulationTick - ticksAgo, Fdp.Toolkit.Perception.Components.SensorModality.Visual);
            _world.Repo.AddComponent(e, mem);
            return e;
        }

        private void Squad(Entity commander, params Entity[] members)
        {
            var roster = new Fdp.Core.CommandHierarchy.UnitRoster();
            foreach (var m in members)
            {
                roster.SubordinateEntities[roster.Count++] = m;
                _world.Repo.AddComponent(m, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = commander });
            }
            _world.Repo.AddComponent(commander, roster);
        }

        /// <summary>⭐ KC-2/KC-3/KC-4 — a civilian 150 m off with a pistol (by day it identifies at 60 m ⇒ Unidentified to everyone) commits a
        /// LETHAL act: the rifleman who SAW it (perceived 10 ticks ago) and his squad-mate (who never saw the man) now read him
        /// IdentifiedLethal; a patrol that last saw him 10 s ago, and a stranger, still see a civilian. ✅ Red-proof: the Knows line dropped
        /// from Judge ⇒ the witness reads Unidentified ⇒ red; the squad read dropped from Knows ⇒ the mate reads Unidentified ⇒ red.</summary>
        [Fact]
        public void CE2238_ALethalAct_MakesHimKnown_ToItsWitnessAndHisSquad_NotToAStalePatrolOrAStranger()
        {
            for (int i = 0; i < 1000; i++) _world.Repo.Tick();
            var civ = Man(PistolCivType, new Vector3(150f, 0f, 0f), ForceId.Neutral, civilian: true);
            var witness = Watcher(Vector3.Zero, civ, ticksAgo: 10);
            var mate = Watcher(new Vector3(0f, 5f, 0f), Entity.Null, 0);
            var leader = Watcher(new Vector3(0f, -5f, 0f), Entity.Null, 0);
            Squad(leader, witness, mate);
            var stale = Watcher(new Vector3(0f, 10f, 0f), civ, ticksAgo: 600);
            var stranger = Watcher(new Vector3(0f, 15f, 0f), Entity.Null, 0);
            foreach (var o in new[] { witness, mate, stale, stranger })
                Assert.Equal(ArmedJudgement.Unidentified, Armament.Judge(_world.Repo, o, civ, Day));

            Assert.Equal(1, KnownCombatants.MarkWitnesses(_world.Repo, civ));
            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world.Repo, witness, civ, Day));
            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world.Repo, mate, civ, Day));    // his squad, through the mate's list
            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world.Repo, leader, civ, Day));
            Assert.Equal(ArmedJudgement.Unidentified, Armament.Judge(_world.Repo, stale, civ, Day));       // saw him 10 s ago, not the act
            Assert.Equal(ArmedJudgement.Unidentified, Armament.Judge(_world.Repo, stranger, civ, Day));
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, witness, civ));                                   // the one production reader
        }

        /// <summary>⭐ KC-1 — the knowledge is STICKY: the witness forgets the contact (out of sight long enough) and still knows him; a
        /// known combatant with nothing lethal on him reads Nothing (no rifle to fear), and the list keeps the newest
        /// <see cref="KnownCombatants.Max"/>. ✅ Red-proof: the knowledge kept in the slot (cleared by Forget) ⇒ red.</summary>
        [Fact]
        public unsafe void CE2238_AKnownCombatant_StaysKnownAfterHisContactIsForgotten_AndReadsOnWhatHeCarries()
        {
            for (int i = 0; i < 100; i++) _world.Repo.Tick();
            var civ = Man(PistolCivType, new Vector3(150f, 0f, 0f), ForceId.Neutral, civilian: true);
            var phone = Man(PhoneCivType, new Vector3(20f, 0f, 0f), ForceId.Neutral, civilian: true);
            var witness = Watcher(Vector3.Zero, civ, ticksAgo: 0);
            Assert.True(KnownCombatants.Mark(_world.Repo, witness, civ));
            Assert.True(KnownCombatants.Mark(_world.Repo, witness, phone));
            ref var mem = ref _world.Repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(witness);
            Fdp.Toolkit.Perception.Components.TargetMemory.Forget(ref mem, 0);
            Assert.Equal(0, mem.Count);
            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world.Repo, witness, civ, Day));
            Assert.Equal(ArmedJudgement.Nothing, Armament.Judge(_world.Repo, witness, phone, Day));       // a phone is nothing lethal

            for (int i = 0; i < KnownCombatants.Max; i++)
                KnownCombatants.Mark(_world.Repo, witness, Man(PhoneCivType, new Vector3(30f + i, 0f, 0f), ForceId.Neutral, civilian: true));
            Assert.False(KnownCombatants.Knows(_world.Repo, witness, civ));                                // the oldest went
            Assert.Equal(KnownCombatants.Max, (int)_world.Repo.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(witness).KnownCombatantCount);
        }

        /// <summary>⭐ KC-5 — the act's KIND decides: a NON-lethal act (a stone) marks nobody; a LETHAL one by the same man marks his witness
        /// even though he already committed an act (HA-2's upgrade). ✅ Red-proof: MarkWitnesses moved inside Commit's first-time guard ⇒ red.</summary>
        [Fact]
        public void CE2238_OnlyALethalAct_MarksTheWitnesses_EvenAfterANonLethalOne()
        {
            for (int i = 0; i < 100; i++) _world.Repo.Tick();
            if (!_world.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Civilians.CivilianState>()) _world.Repo.RegisterComponent<Fdp.Toolkit.Civilians.CivilianState>();
            var civ = Man(StonesCivType, new Vector3(150f, 0f, 0f), ForceId.Neutral, civilian: true);
            _world.Repo.AddComponent(civ, new Fdp.Toolkit.Civilians.CivilianState());
            var witness = Watcher(Vector3.Zero, civ, ticksAgo: 0);
            Fdp.Toolkit.Civilians.HostileActs.Commit(_world.Repo, civ, Fdp.Toolkit.Civilians.HostileActKind.NonLethal);
            Assert.False(KnownCombatants.Knows(_world.Repo, witness, civ));
            Fdp.Toolkit.Civilians.HostileActs.Commit(_world.Repo, civ, Fdp.Toolkit.Civilians.HostileActKind.Lethal);
            Assert.True(KnownCombatants.Knows(_world.Repo, witness, civ));
        }
    }
}
