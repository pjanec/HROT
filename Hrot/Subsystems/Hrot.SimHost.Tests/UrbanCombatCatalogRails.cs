using System;
using System.Linq;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <b>Pack step 4 — ONE catalogue content set.</b> Rails for
    /// <see cref="UrbanCombatTkbCatalog"/> and its seeding from <see cref="HrotEnvironment.CreateTkb"/>.
    ///
    /// <para>🔒 <b>The ruling these enforce</b> (user, <c>2026-08-30</c>): <i>"if editor builds UrbanCombat
    /// stuff then everyone should, editor is the most advanced in that matter."</i> Before this, the
    /// templates lived in <c>Fdp.Examples.Scenarios</c>, referenced by exactly two production projects, so
    /// a scenario referencing TkbType 1001 resolved in the Editor and failed on SimHost or CGF.</para>
    ///
    /// <para>⭐ <b>Acceptance ⑦ demands these go through the shared factory.</b> A rail that called
    /// <c>RegisterAll</c> on a bare database would be vacuous — it would prove the method works, not that
    /// every host's catalogue carries the content. So the first two rails start from
    /// <c>HrotEnvironment.CreateTkb()</c>, which is what the hosts call.
    /// 📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.3, §6 ⑦.</para>
    /// </summary>
    public class UrbanCombatCatalogRails
    {
        // The catalogue owns these codes now; naming them here keeps the rail readable.
        public static TheoryData<long, string> AllFive => new()
        {
            // ⛔ CE-3220 part 2 — CivilianPedestrian (1001) retired; people are CivilianTkbCatalog's family
            { UrbanCombatTkbCatalog.TkbCivilianCar,        "CivilianCar"        },
            { UrbanCombatTkbCatalog.TkbMilitaryApc,        "MilitaryAPC"        },
            { UrbanCombatTkbCatalog.TkbInfantrySoldier,    "InfantrySoldier"    },
            { UrbanCombatTkbCatalog.TkbInsurgent,          "Insurgent"          },
            { UrbanCombatTkbCatalog.TkbJavelinTeam,        "JavelinTeam"        },   // ⭐ M5
            { UrbanCombatTkbCatalog.TkbNlawGunner,         "NlawGunner"         },   // ⭐ M5
        };

        /// <summary>
        /// ⭐⭐ <b>Acceptance ⑦.</b> Every host's catalogue resolves TkbTypes 1001–2003, because they all
        /// build it with <see cref="HrotEnvironment.CreateTkb"/>. ⛔ This is the rail that would have caught
        /// the original defect: a scenario referencing 1001 loading in the Editor and failing on a node.
        /// </summary>
        [Theory]
        [MemberData(nameof(AllFive))]
        public void CreateTkb_ResolvesEveryUrbanCombatType(long tkbType, string expectedName)
        {
            ITkbDatabase tkb = HrotEnvironment.CreateTkb();

            Assert.True(tkb.TryGetByType(tkbType, out var template),
                $"HrotEnvironment.CreateTkb() did not register TkbType {tkbType} ({expectedName}). " +
                "Every host builds its catalogue here, so a missing type fails on some hosts and not others.");
            Assert.Equal(expectedName, template!.Name);
        }

        /// <summary>
        /// 🔴🔴 <b>The regression guard for the DUPLICATE-COPY COLLAPSE (2026-08-31).</b>
        ///
        /// <para>📐 <c>UrbanCombatNewScenario</c> used to carry <b>two</b> copies of these templates: five
        /// private per-template methods used by the scenario's own run, and the public one the Editor
        /// called. They were identical <b>except</b> that the private copy omitted
        /// <see cref="StrideRenderModelDefDto"/> from all five — so entities spawned through the
        /// scenario's own path had no render model and no collider. The Editor's copy was authoritative
        /// and the private one was deleted.</para>
        ///
        /// <para>⛔ If this rail reddens, the drifted (render-less) variant has come back.</para>
        /// </summary>
        [Theory]
        [MemberData(nameof(AllFive))]
        public void EveryUrbanCombatTemplate_CarriesTheRenderModelDescriptor(long tkbType, string expectedName)
        {
            ITkbDatabase tkb = HrotEnvironment.CreateTkb();
            Assert.True(tkb.TryGetByType(tkbType, out var template));

            var render = template!.GetDescriptor<StrideRenderModelDefDto>();
            Assert.NotNull(render);
            Assert.False(string.IsNullOrWhiteSpace(render!.ModelAssetRef),
                $"{expectedName} carries a StrideRenderModelDefDto with no ModelAssetRef — the render-less " +
                "duplicate that was deleted on 2026-08-31 may have returned.");
        }

        /// <summary>
        /// ⭐ <b>The animation descriptor survives the assembly move.</b> <c>CharacterAnimationDefDto</c>
        /// moved from <c>Hrot.MuscleCharacter.Animation</c> into <c>Fdp.Toolkits/Tkb/Domain</c> precisely so
        /// <c>Hrot.Core</c> could host this catalogue without referencing a character-animation subsystem.
        /// ⛔ This rail is what proves the move actually delivered that: it reads the descriptor off a
        /// template built by <c>Hrot.Core</c>. 📄 §3.3, <c>CE-145</c> for the pending namespace rename.
        /// </summary>
        [Theory]
        [InlineData(2002)]   // InfantrySoldier
        [InlineData(2003)]   // Insurgent
        public void HumanoidTemplates_CarryTheMannequinAnimationDescriptor(long tkbType)
        {
            ITkbDatabase tkb = HrotEnvironment.CreateTkb();
            Assert.True(tkb.TryGetByType(tkbType, out var template));

            var anim = template!.GetDescriptor<CharacterAnimationDefDto>();
            Assert.NotNull(anim);
            Assert.NotEmpty(anim!.Montages);
            Assert.NotEmpty(anim.Slots);
        }

        /// <summary>
        /// ⚠⚠ <b>Documents the constraint that removed two production call sites.</b>
        /// <c>TkbDatabase.Register</c> THROWS on a duplicate name or type, so a host that uses
        /// <see cref="HrotEnvironment.CreateTkb"/> must NOT also register the UrbanCombat templates.
        /// <c>EditorSubsystem</c> did exactly that four lines apart, and would have crashed on startup.
        /// ⛔ If this rail reddens, <c>Register</c> became lenient and the "call it exactly once"
        /// constraint documented on <c>CreateTkb</c> and <c>RegisterAll</c> is now wrong.
        /// </summary>
        [Fact]
        public void RegisteringTheCatalogueTwice_Throws()
        {
            var tkb = HrotEnvironment.CreateTkb();

            Assert.Throws<InvalidOperationException>(() => UrbanCombatTkbCatalog.RegisterAll(tkb));
        }

        /// <summary>
        /// ⭐ The catalogue does not disturb what <c>NedTkbCatalog</c> and the route extensions already
        /// seed — <c>CreateTkb</c> composes three contributors and all of them must survive.
        /// </summary>
        [Fact]
        public void CreateTkb_StillCarriesTheNedCatalogueContent()
        {
            ITkbDatabase seeded = HrotEnvironment.CreateTkb();

            ITkbDatabase urbanCombatOnly = new TkbDatabase();
            UrbanCombatTkbCatalog.RegisterAll(urbanCombatOnly);

            int seededCount = seeded.GetAll().Count();
            int urbanOnly   = urbanCombatOnly.GetAll().Count();

            Assert.Equal(13, urbanOnly);         // ⛔ CE-3220 part 2: 1001 and 1003 retired, ⭐ UnarmedFighter (2008) added (was 14) — the four remaining templates + ⭐ CE-1032's Grenadier (2004) and MortarTeam (2005) + ⭐ M5's JavelinTeam (2006) and NlawGunner (2007) + ⭐ AQ93 T1's four traffic cars (1050–1053)
            Assert.True(seededCount > urbanOnly,
                $"CreateTkb() carries {seededCount} templates and UrbanCombat alone is {urbanOnly}. " +
                "CreateTkb() must ALSO carry NedTkbCatalog's content — equal counts mean a contributor " +
                "was dropped from CreateTkb().");
        }

        /// <summary>
        /// ⭐⭐ M5 / M6 (DESIGN_Missiles_And_Ammo §5, DESIGN_Armour_Hit_Model §2) — the anti-armour men and the armoured APC: the RPG man
        /// carries four rockets, lays 2 s, reloads 6 s and stands to fire, with a rifle on mount 1 (the weapon brain picks); the Javelin
        /// team and the NLAW gunner each carry their launcher (its penetration is the munition's own) and a rifle, and see as far as the
        /// launcher reaches; the APC is armoured against rifles — a 5 mm rifle round no longer scratches it — not against a rocket.
        /// </summary>
        [Fact]
        public void M5_M6_TheRpgMan_TheAntiTankTeams_AndTheArmouredApc()
        {
            ITkbDatabase tkb = HrotEnvironment.CreateTkb();

            var rpgMan = tkb.GetByType(UrbanCombatTkbCatalog.TkbInsurgent).GetDescriptor<WeaponSuiteDto>()!.Mounts;
            Assert.Equal(3, rpgMan.Count);
            Assert.True(rpgMan[2].Hands);   // ⭐ CE-3230 (H-1) — every person ends with his hands
            Assert.Equal((ulong)MunitionTkbCatalog.TkbRpg7Rocket, rpgMan[0].AmmoGuid);
            Assert.Equal(4, rpgMan[0].InitialAmmunition);
            Assert.Equal(2f, rpgMan[0].AimSeconds);
            Assert.Equal(6f, rpgMan[0].ReloadSeconds);
            Assert.False(rpgMan[0].FireOnMove);
            Assert.Equal(WeaponCategory.SmallArms, rpgMan[1].Category);

            foreach (var (type, missile, range) in new[] { (UrbanCombatTkbCatalog.TkbJavelinTeam, MunitionTkbCatalog.TkbJavelinMissile, 2500f),
                                                            (UrbanCombatTkbCatalog.TkbNlawGunner, MunitionTkbCatalog.TkbNlawMissile, 800f) })
            {
                var t = tkb.GetByType(type);
                var m = t.GetDescriptor<WeaponSuiteDto>()!.Mounts;
                Assert.Equal((ulong)missile, m[0].AmmoGuid);
                Assert.Equal(WeaponCategory.Atgm, m[0].Category);   // ⇒ it stands to fire (D-L)
                Assert.Equal(0f, m[0].Penetration);                 // the munition states it
                Assert.True(Fdp.Toolkit.Tkb.Parameters.ParameterResolver.MountPenetration(tkb, m[0]).Value > 0f);
                Assert.Equal(WeaponCategory.SmallArms, m[1].Category);
                Assert.Equal(range, t.GetDescriptor<SensorCapabilitiesDto>()!.VisionRange);
                Assert.NotNull(t.GetDescriptor<StrideRenderModelDefDto>());
            }

            var apc = tkb.GetByType(UrbanCombatTkbCatalog.TkbMilitaryApc).GetDescriptor<CombatPlatformDefDto>()!;
            Assert.Equal(500f, apc.MaxHealth);
            Assert.True(apc.ArmorFront > 5f && apc.ArmorRear > 5f, "a rifle round (5 mm) must not defeat the APC");
            Assert.True(apc.ArmorFront < 300f, "an RPG (300 mm) must");
        }
    }
}
