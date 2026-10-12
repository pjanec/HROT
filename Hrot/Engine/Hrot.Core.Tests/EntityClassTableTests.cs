using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Tkb;
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
    }
}
