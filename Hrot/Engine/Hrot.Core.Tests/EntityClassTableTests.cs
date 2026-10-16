using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Tkb;
using Xunit;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐ CE-2138 (C2, docs/DESIGN_Orders_Params_Entity_Classes.md §2 rule K1, R-279 / R-280 (behaviors)) — the ONE hard-coded
    /// class table. Its D2b worry ("names in code rot silently") gets these rails instead of a data file.
    /// </summary>
    public class EntityClassTableTests
    {
        private static TkbDatabase BuiltIn()
        {
            var db = new TkbDatabase();
            NedTkbCatalog.RegisterAll(db);
            UrbanCombatTkbCatalog.RegisterAll(db);
            return db;
        }

        /// <summary>No two classes of equal specificity overlap, so "the most specific class" is never a tie.</summary>
        [Fact]
        public void CE2138_TheBuiltInTable_HasNoTies()
        {
            var ties = EntityClassTable.Default.Ties().Select(t => $"{t.A.Name} {t.A.Mask} / {t.B.Name} {t.B.Mask}").ToList();
            Assert.True(ties.Count == 0, string.Join(", ", ties));
        }

        /// <summary>A duplicate name or an identical mask refuses to build (the default table built, so it has neither).</summary>
        [Fact]
        public void CE2138_ADuplicateNameOrMask_IsRefused()
        {
            var tank = new EntityClass("Tank", "Tank", DisTypeMask.Parse("1.1.*.1"));
            var ex = Assert.Throws<ArgumentException>(() => new EntityClassTable(new[] { tank, tank with { Mask = DisTypeMask.Parse("1.1.*.2") } }));
            Assert.Contains("Tank", ex.Message);
            ex = Assert.Throws<ArgumentException>(() => new EntityClassTable(new[] { tank, tank with { Name = "Mbt" } }));
            Assert.Contains("1.1.*.1", ex.Message);
            Assert.NotEmpty(EntityClassTable.Default.Classes);
        }

        /// <summary>Every built-in type with a DIS type has a class, and its picker path names every level it pins.</summary>
        [Fact]
        public void CE2138_EveryBuiltInType_ResolvesToANamedClassPath()
        {
            var unnamed = new System.Collections.Generic.List<string>();
            foreach (var t in BuiltIn().GetAll().Where(t => t.DisType.Value != 0))
            {
                if (EntityClassTable.Default.MostSpecific(t.DisType) is null) unnamed.Add($"{t.Name}: no class");
                foreach (var level in DisNameTable.Default.Path(t.DisType))
                    if (level.StartsWith("Kind ") || level.StartsWith("Domain ") || level.StartsWith("Country ") || level.StartsWith("Category "))
                        unnamed.Add($"{t.Name}: {level}");
            }
            Assert.True(unnamed.Count == 0, string.Join(", ", unnamed));
        }

        /// <summary>⭐ Weapon systems WS-1 (R-2016, DESIGN_Weapon_Systems §5) — a weapon system is Kind 1 · Domain 1 · Category 50 on land,
        /// its kind the subcategory; the air FPV drone's Category 50 stays a Jet.</summary>
        [Fact]
        public void WS1_AWeaponSystem_IsLandCategory50_ItsKindTheSubcategory()
        {
            var hmg = new DISEntityType { Kind = 1, Domain = 1, Country = CountryTable.UnitedStates, Category = 50, Subcategory = 1 };
            Assert.Equal(new[] { "Platform", "LandPlatform", "WeaponSystem", "HmgWeaponSystem" },
                         EntityClassTable.Default.Chain(hmg).Select(c => c.Name));
            Assert.Equal("SentryWeaponSystem", EntityClassTable.Default.MostSpecific(hmg with { Subcategory = 5 })!.Name);
            Assert.Equal("WeaponSystem", EntityClassTable.Default.MostSpecific(hmg with { Subcategory = 0 })!.Name);
            Assert.NotEqual("WeaponSystem", EntityClassTable.Default.MostSpecific(hmg with { Domain = 2 })!.Name);
        }

        /// <summary>⭐ <c>CE-3220</c> (civilians B-2, CV-1, R-3013; DESIGN_Civilians_And_Crowd_Control §7.2) — every civilian type's most specific
        /// class is <c>Civilian</c>, every security-force type's is <c>SecurityForce</c>, under <c>LandPerson</c> — the class the picker, the
        /// 3-D model and the behaviour lookup share (R-280).</summary>
        [Fact]
        public void CE3220_TheCivilianFamily_AndTheSecurityForces_HaveTheirOwnClasses()
        {
            var db = BuiltIn();
            CivilianTkbCatalog.RegisterAll(db);
            foreach (var (type, cls) in new[]
            {
                (CivilianTkbCatalog.TkbCivilianAdultMale, "Civilian"), (CivilianTkbCatalog.TkbCivilianAdultFemale, "Civilian"),
                (CivilianTkbCatalog.TkbCivilianChild, "Civilian"), (CivilianTkbCatalog.TkbCivilianElderly, "Civilian"),
                (CivilianTkbCatalog.TkbSecurityShield, "SecurityForce"), (CivilianTkbCatalog.TkbSecurityBaton, "SecurityForce"),
                (CivilianTkbCatalog.TkbSecurityLauncher, "SecurityForce"),
            })
            {
                var dis = db.GetByType(type)!.DisType;
                Assert.Equal(new[] { "Person", "LandPerson", cls }, EntityClassTable.Default.Chain(dis).Select(c => c.Name));
            }
        }

        /// <summary>The chain runs general → specific; a grouping class (a list) sits between its level and its member.</summary>
        [Fact]
        public void CE2138_TheChain_RunsGeneralToSpecific()
        {
            var t72 = BuiltIn().GetByType(TkbEntityTypes.Tank_T72)!.DisType;
            Assert.Equal(new[] { "Platform", "LandPlatform", "Armor", "Tank" },
                         EntityClassTable.Default.Chain(t72).Select(c => c.Name));
            Assert.Equal("Tank", EntityClassTable.Default.MostSpecific(t72)!.Name);

            var uh60 = BuiltIn().GetByType(TkbEntityTypes.Heli_UH60)!.DisType;
            Assert.Equal(new[] { "Platform", "AirPlatform", "Helicopter", "UtilityHelicopter" },
                         EntityClassTable.Default.Chain(uh60).Select(c => c.Name));

            Assert.Null(EntityClassTable.Default.MostSpecific(default));
            Assert.Equal("United States", DisNameTable.Default.CountryName(CountryTable.UnitedStates));
        }

        /// <summary>Only a class pinning exactly one DIS level is a picker folder; a list or range class is not.</summary>
        [Fact]
        public void CE2138_OnlyExactLevelClasses_NameALevel()
        {
            Assert.True(EntityClassTable.Default.Find("Tank")!.NamesALevel);
            Assert.True(EntityClassTable.Default.Find("LandPlatform")!.NamesALevel);
            Assert.True(EntityClassTable.Default.Find("Platform")!.NamesALevel);
            Assert.False(EntityClassTable.Default.Find("Armor")!.NamesALevel);
            Assert.False(EntityClassTable.Default.Find("Helicopter")!.NamesALevel);
        }

        // ── CE-2139 (C3, rule K2, R-278) — units have their own DIS Kind ───────────────────────────────────────────────────

        /// <summary>Each built-in unit is Kind 11, land, its country kept, Category = arm, Subcategory = the echelon its
        /// composition names; its most specific class is its own, and no platform class (tank masks included) matches it.</summary>
        [Theory]
        [InlineData(TkbEntityTypes.Unit_TankPlatoon, UnitArm.Armor, "TankPlatoon", 1)]
        [InlineData(TkbEntityTypes.Unit_TankPlatoon_Auto, UnitArm.Armor, "TankPlatoon", 2)]
        [InlineData(TkbEntityTypes.Unit_InfantrySquad, UnitArm.Infantry, "InfantrySquad", 1)]
        public void CE2139_AUnit_HasItsOwnKind_ArmEchelonAndVariant(long type, UnitArm arm, string cls, int variant)
        {
            var t = BuiltIn().GetByType(type)!;
            var dis = t.DisType;
            Assert.Equal(UnitDis.Kind, dis.Kind);
            Assert.Equal((byte)1, dis.Domain);
            Assert.Equal(CountryTable.UnitedStates, dis.Country);
            Assert.Equal((byte)arm, dis.Category);
            Assert.Equal((byte)UnitDis.EchelonOf(t.GetDescriptor<TkbCompositionDef>()!.Echelon), dis.Subcategory);
            Assert.Equal((byte)variant, dis.Specific);
            Assert.Equal(cls, EntityClassTable.Default.MostSpecific(dis)!.Name);
            Assert.DoesNotContain(EntityClassTable.Default.Chain(dis), c => c.Mask.ExactValue(0) != UnitDis.Kind);
            Assert.False(EntityClassTable.Default.Find("Tank")!.Mask.Matches(dis));
            Assert.Equal(t.DisType.ToString(), t.GetDescriptor<TkbMasterDto>()!.DisType);
        }

        /// <summary>The reverse: a tank's chain holds no unit class.</summary>
        [Fact]
        public void CE2139_ATank_MatchesNoUnitClass()
        {
            var t72 = BuiltIn().GetByType(TkbEntityTypes.Tank_T72)!.DisType;
            Assert.DoesNotContain(EntityClassTable.Default.Chain(t72), c => c.Mask.ExactValue(0) == UnitDis.Kind);
        }

        /// <summary>The builder reads the echelon from the composition (one source) and refuses a non-composite or a misspelt echelon.</summary>
        [Fact]
        public void CE2139_AsLandUnit_ReadsTheEchelon_AndRefusesWhatItCannotType()
        {
            var db = new TkbDatabase();
            var b = new NedTkbBuilder(db);
            b.DefineVehicle(9001, "Coy").AsComposite(9001, c => c.Echelon = "company").AsLandUnit(9001, UnitArm.MechanizedInfantry, CountryTable.Germany);
            var dis = db.GetByType(9001)!.DisType;
            Assert.Equal((byte)UnitEchelon.Company, dis.Subcategory);
            Assert.Equal((byte)UnitArm.MechanizedInfantry, dis.Category);
            Assert.Equal("MechanizedInfantryUnit", EntityClassTable.Default.MostSpecific(dis)!.Name);

            b.DefineVehicle(9002, "Tank");
            Assert.Throws<InvalidOperationException>(() => b.AsLandUnit(9002, UnitArm.Armor, CountryTable.UnitedStates));
            b.DefineVehicle(9003, "Typo").AsComposite(9003, c => c.Echelon = "Platon");
            var ex = Assert.Throws<ArgumentException>(() => b.AsLandUnit(9003, UnitArm.Armor, CountryTable.UnitedStates));
            Assert.Contains("Platon", ex.Message);
        }
    }
}
