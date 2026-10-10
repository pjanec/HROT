using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Map.Common.Tests
{
    public class NedTkbBuilderCombatTests
    {
        [Fact]
        public void WithCombat_StoresWeaponCapabilitiesDescriptor()
        {
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
            Assert.True(template.HasDescriptor<WeaponCapabilitiesDto>());
        }

        [Fact]
        public void WithCombat_WeaponCapabilities_HasExpectedEffectiveRange()
        {
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
            var dto = template.GetDescriptor<WeaponCapabilitiesDto>()!;
            Assert.Equal(3000f, dto.EffectiveRange);
        }

        [Fact]
        public void WithCombat_WeaponCapabilities_HasExpectedRateOfFire()
        {
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
            var dto = template.GetDescriptor<WeaponCapabilitiesDto>()!;
            Assert.Equal(6f, dto.RateOfFire);
        }

        [Fact]
        public void WithCombat_WeaponCapabilities_HasExpectedMagazineCapacity()
        {
            var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
            var dto = template.GetDescriptor<WeaponCapabilitiesDto>()!;
            Assert.Equal(42, dto.MagazineCapacity);
        }

        // ── ⭐ Buildings programme Stage 0 — the resolver reports what the catalog's numbers ARE and where they came from;
        //    the numbers themselves are UNCHANGED (📄 docs/DESIGN_Terrain_Combat_Tuning.md §2, §6 T-0). ──────────────

        private static Fdp.Toolkit.Tkb.Parameters.ResolvedParameter Get(long type, string name)
            => System.Linq.Enumerable.Single(
                Fdp.Toolkit.Tkb.Parameters.ParameterResolver.ResolveAll(BuildDatabase().GetByType(type)!), p => p.Name == name);

        [Fact]
        public void Stage0_M1_DerivedNumbersAreGenerated_StatedOnesExplicit_EyesOnEngineFallbacks_ValuesUnchanged()
        {
            const long m1 = TkbEntityTypes.Tank_M1Abrams;
            var health = Get(m1, Fdp.Toolkit.Tkb.Parameters.ParameterNames.MaxHealth);
            Assert.Equal(3000f, health.Value);                                   // 600 mm × 5 — as before Stage 0
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Generated, health.Provenance);
            Assert.Contains("armourFront = 600", health.Source);

            var mv = Get(m1, "Weapon[0].MuzzleVelocity");
            Assert.Equal(1500f, mv.Value);                                       // range 3000 × 0.5 — as before
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Generated, mv.Provenance);

            var dmg = Get(m1, "Weapon[0].DamagePerHit");
            Assert.Equal(1200f, dmg.Value);
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Explicit, dmg.Provenance);
            Assert.Equal(650f, Get(m1, "Weapon[0].Penetration").Value);

            var eye = Get(m1, Fdp.Toolkit.Tkb.Parameters.ParameterNames.EyeStanding);
            Assert.Equal(1.7f, eye.Value);
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.EngineFallback, eye.Provenance);
        }

        [Fact]
        public void Stage0_Rifleman_HealthGeneratedFromBodyArmour_DamageExplicit()
        {
            const long rifleman = TkbEntityTypes.Infantry_Rifleman;
            Assert.Equal(25f, Get(rifleman, Fdp.Toolkit.Tkb.Parameters.ParameterNames.MaxHealth).Value);   // 5 mm × 5
            Assert.Equal(25f, Get(rifleman, "Weapon[0].DamagePerHit").Value);
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Explicit, Get(rifleman, "Weapon[0].DamagePerHit").Provenance);
            Assert.Equal(150f, Get(rifleman, "Weapon[0].MuzzleVelocity").Value);                             // 300 m × 0.5
        }

        private static TkbDatabase BuildDatabase()
        {
            var db = new TkbDatabase();
            NedTkbCatalog.RegisterAll(db);
            return db;
        }
    }
}

