using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
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
            // Hostile, as his act leaves him (CV-4) — a Neutral is no danger before Judge is asked (CE-2233)
            var civ = Man(PistolCivType, new Vector3(150f, 0f, 0f), ForceId.Hostile, civilian: true);
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
        /// known combatant with nothing lethal on him (stones) reads Nothing, and the list keeps the newest
        /// <see cref="KnownCombatants.Max"/>. ✅ Red-proof: the knowledge kept in the slot (cleared by Forget) ⇒ red.</summary>
        [Fact]
        public unsafe void CE2238_AKnownCombatant_StaysKnownAfterHisContactIsForgotten_AndReadsOnWhatHeCarries()
        {
            for (int i = 0; i < 100; i++) _world.Repo.Tick();
            var civ = Man(PistolCivType, new Vector3(150f, 0f, 0f), ForceId.Neutral, civilian: true);
            var stones = Man(StonesCivType, new Vector3(20f, 0f, 0f), ForceId.Neutral, civilian: true);
            var witness = Watcher(Vector3.Zero, civ, ticksAgo: 0);
            Assert.True(KnownCombatants.Mark(_world.Repo, witness, civ));
            Assert.True(KnownCombatants.Mark(_world.Repo, witness, stones));
            ref var mem = ref _world.Repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(witness);
            Fdp.Toolkit.Perception.Components.TargetMemory.Forget(ref mem, 0);
            Assert.Equal(0, mem.Count);
            Assert.Equal(ArmedJudgement.IdentifiedLethal, Armament.Judge(_world.Repo, witness, civ, Day));
            Assert.Equal(ArmedJudgement.Nothing, Armament.Judge(_world.Repo, witness, stones, Day));      // stones are nothing lethal

            for (int i = 0; i < KnownCombatants.Max; i++)
                KnownCombatants.Mark(_world.Repo, witness, Man(PhoneCivType, new Vector3(30f + i, 0f, 0f), ForceId.Neutral, civilian: true));
            Assert.False(KnownCombatants.Knows(_world.Repo, witness, civ));                                // the oldest went
            Assert.Equal(KnownCombatants.Max, (int)_world.Repo.GetComponentRO<KnownCombatantList>(witness).Count);
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

        /// <summary>⭐ <c>CE-2238</c> part 4 (KC-1 revised; measured on <c>ua-armed-blend-back</c>) — a mark survives the memory's own
        /// write-back in the SAME frame: <c>ThreatEvaluationSystem</c> copies the witness's memory and queues the copy, the act marks him,
        /// then the queue plays back. ✅ Red-proof: the list kept inside <c>TargetMemory</c> (the copy is from before the mark) ⇒ red.</summary>
        [Fact]
        public void CE2238_AMark_SurvivesTheMemorysWriteBack_InTheSameFrame()
        {
            for (int i = 0; i < 100; i++) _world.Repo.Tick();
            var civ = Man(PistolCivType, new Vector3(20f, 0f, 0f), ForceId.Neutral, civilian: true);
            var witness = Watcher(Vector3.Zero, civ, ticksAgo: 0);
            ISimulationView view = _world.Repo;
            new Fdp.Toolkit.Perception.Systems.ThreatEvaluationSystem().Execute(view, 0.1f);   // copies the memory, queues the decayed copy
            Assert.Equal(1, KnownCombatants.MarkWitnesses(_world.Repo, civ));
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world.Repo);
            Assert.True(KnownCombatants.Knows(_world.Repo, witness, civ));
        }

        /// <summary>⭐ <c>CE-2238</c> part 4 (AW-21/AW-22, R-2027/R-2028; §5h.7b CH-1) — the CONDUCT hold: a LETHAL round at a Hostile civilian
        /// who shows nothing lethal is held for a unit that does not know him, and fired by one that does (it saw his act); a non-lethal round
        /// is never held; a soldier target is never held (status); a civilian aiming a pistol at the unit lifts it. ✅ Red-proofs: the
        /// combatant shortcut removed ⇒ an unarmed soldier target is held ⇒ red; the Judge line removed ⇒ the witness is held ⇒ red; the
        /// aim line removed ⇒ the aiming civilian is held ⇒ red.</summary>
        [Fact]
        public void CE2238_TheConductHold_NoLethalRoundAtAnUnarmedCivilian_UnlessKnownOrAimingAtMe()
        {
            for (int i = 0; i < 100; i++) _world.Repo.Tick();
            if (!_world.Repo.IsComponentTypeRegistered<WeaponPosture>()) _world.Repo.RegisterComponent<WeaponPosture>();
            var civ = Man(StonesCivType, new Vector3(20f, 0f, 0f), ForceId.Hostile, civilian: true);   // Hostile, nothing lethal on him
            var stranger = Observer();
            var witness = Watcher(new Vector3(0f, 5f, 0f), civ, ticksAgo: 0);
            Assert.False(ForceLevels.ConductPermits(_world.Repo, stranger, civ, lethal: true));
            Assert.True(ForceLevels.ConductPermits(_world.Repo, stranger, civ, lethal: false));            // non-lethal force only
            Assert.True(KnownCombatants.Mark(_world.Repo, witness, civ));
            Assert.False(ForceLevels.ConductPermits(_world.Repo, witness, civ, lethal: true));             // he knows him — but stones are nothing lethal

            var hid = Man(PistolCivType, new Vector3(150f, 5f, 0f), ForceId.Hostile, civilian: true);   // 150 m by day: his pistol unidentified
            Assert.False(ForceLevels.ConductPermits(_world.Repo, stranger, hid, lethal: true));
            Assert.True(KnownCombatants.Mark(_world.Repo, witness, hid));
            Assert.True(ForceLevels.ConductPermits(_world.Repo, witness, hid, lethal: true));              // known: read on what he carries

            var soldier = Man(RiflemanType, new Vector3(30f, 0f, 0f), ForceId.Hostile);
            Assert.True(ForceLevels.ConductPermits(_world.Repo, stranger, soldier, lethal: true));         // status
            var unarmedEnemy = _world.Repo.CreateEntity();                                                // a Hostile of no Civilian class, nothing in hand
            _world.Repo.AddComponent(unarmedEnemy, new SimTransform { Position = new Vector3(35f, 0f, 0f), Rotation = Quaternion.Identity });
            _world.Repo.AddComponent(unarmedEnemy, new EntityInfo { ForceId = ForceId.Hostile });
            Assert.Equal(ArmedJudgement.Nothing, Armament.Judge(_world.Repo, stranger, unarmedEnemy, Day));
            Assert.True(ForceLevels.ConductPermits(_world.Repo, stranger, unarmedEnemy, lethal: true));    // a combatant: status, never the conduct hold (AW-24)

            var aiming = Man(PistolCivType, new Vector3(150f, 0f, 0f), ForceId.Hostile, civilian: true);  // 150 m: his pistol unidentified
            Assert.False(ForceLevels.ConductPermits(_world.Repo, stranger, aiming, lethal: true));
            _world.Repo.AddComponent(aiming, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = stranger });
            Assert.True(ForceLevels.ConductPermits(_world.Repo, stranger, aiming, lethal: true));           // he aims at me
        }
        private static readonly DISEntityType PoliceDis = new() { Kind = 3, Domain = 1, Category = Armament.SecurityForceCategory };

        private Entity Police(Vector3 at)
        {
            var e = Man(RiflemanType, at, ForceId.Friend);
            _world.Repo.SetDisType(e, PoliceDis);
            return e;
        }

        private void Pose(Entity man, WeaponPostureKind kind, Entity target = default)
        {
            if (!_world.Repo.IsComponentTypeRegistered<WeaponPosture>()) _world.Repo.RegisterComponent<WeaponPosture>();
            if (!_world.Repo.HasComponent<WeaponPosture>(man)) _world.Repo.AddComponent(man, new WeaponPosture());
            ref var p = ref _world.Repo.GetComponentRW<WeaponPosture>(man);
            p.Kind = kind;
            p.Target = target;
        }

        /// <summary>⭐ <c>CE-2239</c> part 3 (AW-10, AW-18, §5k EL-1/EL-2) — the LADDER, rung by rung, for a police unit at a Hostile civilian
        /// with a pistol 20 m off (identified by day): AT EASE ⇒ Warn (no round, not even a baton round); READY ⇒ NonLethal; still ready
        /// 5 s after the warning ⇒ Lethal; at ease again ⇒ Warn; AIMED at the policeman ⇒ Lethal at once (self-defence). An UNARMED one ⇒
        /// NonLethal; a SOLDIER never runs it (his cap); a NonLethal cap stays the ceiling. ✅ Red-proofs: the at-ease rung removed ⇒ red; the
        /// ignored-warning clock removed (Lethal from ready) ⇒ red; Applies without the SecurityForce test ⇒ the soldier is laddered ⇒ red;
        /// the cap not applied to the rung ⇒ red.</summary>
        [Fact]
        public void CE2239_TheLadder_WarnAtEase_NonLethalWhenReady_LethalAfterAnIgnoredWarning_SelfDefenceAtOnce()
        {
            for (int i = 0; i < 100; i++) _world.Repo.Tick();
            _world.Repo.SetSimulationTime(10f);
            var cop = Police(Vector3.Zero);
            var soldier = Observer();
            var armed = Man(PistolCivType, new Vector3(20f, 0f, 0f), ForceId.Hostile, civilian: true);
            var unarmed = Man(StonesCivType, new Vector3(15f, 0f, 0f), ForceId.Hostile, civilian: true);
            RoeForceLevel Level(Entity unit, Entity man) => ForceLevels.LevelFor(_world.Repo, unit, man);

            Assert.Equal(RoeForceLevel.Warn, Level(cop, armed));                                           // armed at ease
            Assert.False(ForceLevels.Permits(_world.Repo, cop, armed, BatonRoundAmmo));
            Assert.Equal(RoeForceLevel.NonLethal, Level(cop, unarmed));
            Assert.Equal(RoeForceLevel.Lethal, Level(soldier, armed));                                     // AW-24: no ladder for a soldier

            Pose(armed, WeaponPostureKind.Ready);
            Assert.Equal(RoeForceLevel.NonLethal, Level(cop, armed));                                      // ready, not yet warned
            Assert.True(EscalationLadder.WarnIfDue(_world.Repo, cop, armed));
            Assert.Equal(10.0, EscalationLadder.WarnedAt(_world.Repo, cop, armed), 3);
            _world.Repo.SetSimulationTime(13f);
            Assert.Equal(RoeForceLevel.NonLethal, Level(cop, armed));                                      // 3 s — not yet ignored
            Assert.True(EscalationLadder.WarnIfDue(_world.Repo, cop, armed));
            Assert.Equal(10.0, EscalationLadder.WarnedAt(_world.Repo, cop, armed), 3);                     // a repeated shout keeps the clock
            Assert.Equal(12.0, _world.Repo.GetComponentRO<WarningLog>(cop).ShowUntil, 3);                  // …and does not raise the cue again
            _world.Repo.SetSimulationTime(15.5f);
            Assert.Equal(RoeForceLevel.Lethal, Level(cop, armed));                                         // ignored ⇒ lethal
            Assert.True(ForceLevels.Permits(_world.Repo, cop, armed, BulletAmmo));

            Pose(armed, WeaponPostureKind.AtEase);
            Assert.Equal(RoeForceLevel.Warn, Level(cop, armed));                                           // lowered: back down by posture alone
            Pose(armed, WeaponPostureKind.Aimed, cop);
            Assert.Equal(RoeForceLevel.Lethal, Level(cop, armed));                                         // aimed at me: self-defence

            if (!_world.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.Roe>()) _world.Repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.Roe>();
            _world.Repo.AddComponent(cop, new Fdp.Toolkit.Behavior.Components.Roe { ForceLevel = RoeForceLevel.NonLethal });
            Pose(armed, WeaponPostureKind.Ready);
            Assert.Equal(RoeForceLevel.NonLethal, Level(cop, armed));                                      // the cap is the ceiling
        }

        /// <summary>⭐ <c>CE-2239</c> part 3 (AW-16, §5k EL-3/EL-5) — the WARNING: the police watch warns an identified-armed man it remembers
        /// within reach — a NEUTRAL too (drawn ⇒ warn) — and shows the cue for 2 s; not one beyond reach, not an unarmed one; firing into the
        /// air (aimed at a point) is the ready rung, never self-defence. ✅ Red-proofs: the reach test removed ⇒ the far man is warned ⇒ red;
        /// the cue never lowered ⇒ red; <c>ForceLevels.AimsAt</c> without its <c>Target == unit</c> test ⇒ the volley reads self-defence ⇒ red.</summary>
        [Fact]
        public unsafe void CE2239_TheWarning_ArmedInReach_ANeutralToo_CueShowsTwoSeconds_AndFireIntoTheAirReadsReady()
        {
            for (int i = 0; i < 100; i++) _world.Repo.Tick();
            _world.Repo.SetSimulationTime(20f);
            if (!_world.Repo.IsComponentTypeRegistered<Gesture>()) _world.Repo.RegisterComponent<Gesture>();
            var cop = Police(Vector3.Zero);
            var near = Man(PistolCivType, new Vector3(20f, 0f, 0f), ForceId.Neutral, civilian: true);
            var far = Man(PistolCivType, new Vector3(55f, 0f, 0f), ForceId.Neutral, civilian: true);
            var stones = Man(StonesCivType, new Vector3(10f, 0f, 0f), ForceId.Hostile, civilian: true);
            var mem = new Fdp.Toolkit.Perception.Components.TargetMemory();
            foreach (var (e, x) in new[] { (near, 20f), (far, 55f), (stones, 10f) })
                Fdp.Toolkit.Perception.Components.TargetMemory.AddOrUpdateTarget(ref mem, (long)e.PackedValue, x, 0f, 1f, _world.Repo.SimulationTick,
                    Fdp.Toolkit.Perception.Components.SensorModality.Visual);
            _world.Repo.AddComponent(cop, mem);

            EscalationLadder.Watch(_world.Repo, cop, 40f);
            Assert.True(EscalationLadder.WarnedAt(_world.Repo, cop, near) > 0);
            Assert.Equal(0.0, EscalationLadder.WarnedAt(_world.Repo, cop, far));                           // beyond reach
            Assert.Equal(0.0, EscalationLadder.WarnedAt(_world.Repo, cop, stones));                        // nothing lethal on him
            Assert.Equal(GestureKind.Warning, Gestures.Of(_world.Repo, cop));
            _world.Repo.SetSimulationTime(21f);
            EscalationLadder.Watch(_world.Repo, cop, 40f);
            Assert.Equal(GestureKind.Warning, Gestures.Of(_world.Repo, cop));
            _world.Repo.SetSimulationTime(22.5f);
            EscalationLadder.Watch(_world.Repo, cop, 40f);
            Assert.Equal(GestureKind.None, Gestures.Of(_world.Repo, cop));                                 // the cue shown, lowered

            var volley = Man(PistolCivType, new Vector3(25f, 5f, 0f), ForceId.Hostile, civilian: true);
            Pose(volley, WeaponPostureKind.Aimed);                                                          // aimed at a POINT — into the air
            Assert.Equal(RoeForceLevel.NonLethal, ForceLevels.LevelFor(_world.Repo, cop, volley));          // ready, not yet warned: never lethal
        }

        /// <summary>⭐ <c>CE-2238</c> part 4 (AW-16, §5h.7 CO-1) — AIMING at the forces is the hostile act, drawing is not: a civilian whose
        /// weapon is READY at a soldier commits nothing; aimed at another civilian, nothing; aimed at the SOLDIER — a LETHAL act; an enemy
        /// soldier aiming at him (a combatant: his status already says it) commits none. ✅ Red-proof: the <c>AimedAtTheForces</c> call removed
        /// from <c>WeaponPostureOf.Report</c> ⇒ red; its combatant guard removed ⇒ red (the enemy soldier "acts"); the target's combatant test
        /// removed ⇒ red (a Friend-side CIVILIAN is not the forces).</summary>
        [Fact]
        public void CE2238_AimingAtTheForces_IsALethalAct_ForACivilian_NotForASoldier_NorAimedAtACivilian()
        {
            if (!_world.Repo.IsComponentTypeRegistered<WeaponPosture>()) _world.Repo.RegisterComponent<WeaponPosture>();
            if (!_world.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Civilians.HostileAct>()) _world.Repo.RegisterComponent<Fdp.Toolkit.Civilians.HostileAct>();
            var soldier = Observer();
            var civ = Man(PistolCivType, new Vector3(20f, 0f, 0f), ForceId.Neutral, civilian: true);
            var bystander = Man(PhoneCivType, new Vector3(25f, 0f, 0f), ForceId.Neutral, civilian: true);
            var enemy = Man(RiflemanType, new Vector3(30f, 0f, 0f), ForceId.Hostile);
            foreach (var e in new[] { civ, enemy }) _world.Repo.AddComponent(e, new WeaponPosture());
            Fdp.Toolkit.Civilians.HostileActKind Act(Entity e) => Fdp.Toolkit.Civilians.HostileActs.Of(_world.Repo, e);

            WeaponPostureOf.Report(_world.Repo, civ, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldNeutral, soldier);   // drawn, ready
            Assert.Equal(Fdp.Toolkit.Civilians.HostileActKind.None, Act(civ));
            WeaponPostureOf.Report(_world.Repo, civ, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Aiming, bystander);      // at a civilian
            Assert.Equal(Fdp.Toolkit.Civilians.HostileActKind.None, Act(civ));
            var friendlyCiv = Man(PhoneCivType, new Vector3(26f, 0f, 0f), ForceId.Friend, civilian: true);                  // the forces' side, not the forces
            WeaponPostureOf.Report(_world.Repo, civ, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Aiming, friendlyCiv);
            Assert.Equal(Fdp.Toolkit.Civilians.HostileActKind.None, Act(civ));
            WeaponPostureOf.Report(_world.Repo, civ, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Aiming, soldier);        // at the forces
            Assert.Equal(Fdp.Toolkit.Civilians.HostileActKind.Lethal, Act(civ));
            WeaponPostureOf.Report(_world.Repo, enemy, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Aiming, soldier);      // a combatant
            Assert.Equal(Fdp.Toolkit.Civilians.HostileActKind.None, Act(enemy));
        }
    }
}
