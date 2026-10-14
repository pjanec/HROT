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
            // ⭐ M6 (D-F) — RE-PINNED: the M1 now STATES its health (the same 3000), so it reads Explicit, no longer Generated
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Explicit, health.Provenance);

            var mv = Get(m1, "Weapon[0].MuzzleVelocity");
            Assert.Equal(1500f, mv.Value);                                       // range 3000 × 0.5 — as before
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Generated, mv.Provenance);

            var dmg = Get(m1, "Weapon[0].DamagePerHit");
            Assert.Equal(1200f, dmg.Value);
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Explicit, dmg.Provenance);
            Assert.Equal(650f, Get(m1, "Weapon[0].Penetration").Value);

            // ⭐ M5 — RE-PINNED: the M1 now states where its crew sees from (its sight, 2.5 m), no longer a standing man's 1.7
            var eye = Get(m1, Fdp.Toolkit.Tkb.Parameters.ParameterNames.EyeStanding);
            Assert.Equal(2.5f, eye.Value);
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Explicit, eye.Provenance);
        }

        /// <summary>
        /// ⭐⭐ M5 / M6 (DESIGN_Missiles_And_Ammo §5, DESIGN_Armour_Hit_Model §2) — the builder carries the new combat data through:
        /// the tanks' stated health (= the old front × 5, so no fight moves on it), roof and shaped-charge armour, the main gun's
        /// loadout (APFSDS / HEAT-MP / HE, summing to the old ammunition), the coaxial MG on the turret, the TOW's lay time and
        /// every vehicle's eye height.
        /// </summary>
        [Fact]
        public void M5_M6_TheTanks_CarryLoadoutCoaxRoofAndShapedChargeArmour_TheirHealthUnchanged()
        {
            var db = BuildDatabase();
            foreach (var (type, health, top, frontCe, apfsds, eye) in new[]
                     {
                         (TkbEntityTypes.Tank_M1Abrams, 3000f, 60f, 900f, (long)Hrot.Core.Tkb.MunitionTkbCatalog.Tkb120mmApfsds, 2.5f),
                         (TkbEntityTypes.Tank_T72, 2500f, 40f, 700f, (long)Hrot.Core.Tkb.MunitionTkbCatalog.Tkb125mmApfsds, 2.2f),
                     })
            {
                var t = db.GetByType(type)!;
                var p = t.GetDescriptor<CombatPlatformDefDto>()!;
                Assert.Equal(health, p.MaxHealth);
                Assert.Equal(top, p.ArmorTop);
                Assert.Equal(frontCe, p.ArmorFrontCe);
                Assert.Equal(eye, t.GetDescriptor<SensorCapabilitiesDto>()!.EyeHeightStanding);

                var mounts = t.GetDescriptor<WeaponSuiteDto>()!.Mounts;
                Assert.Equal(2, mounts.Count);
                var gun = mounts[0];
                Assert.Equal(unchecked((ulong)apfsds), gun.AmmoGuid);
                Assert.Equal(gun.InitialAmmunition, System.Linq.Enumerable.Sum(gun.Loadout, l => l.Rounds));   // the same rounds, now sorted
                Assert.Equal(new[] { unchecked((ulong)apfsds), (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbTankHeatMp, (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbTankHeFrag },
                             System.Linq.Enumerable.Select(gun.Loadout, l => l.AmmoGuid));

                var coax = mounts[1];
                Assert.Equal(WeaponCategory.MachineGun, coax.Category);
                Assert.Equal(0, coax.TurretIndex);
                Assert.Equal((ulong)Hrot.Core.Tkb.MunitionTkbCatalog.Tkb762Ball, coax.AmmoGuid);
                Assert.Equal(200, coax.MagazineSize);
            }

            var bradley = db.GetByType(TkbEntityTypes.IFV_Bradley)!;
            Assert.Equal(500f, bradley.GetDescriptor<CombatPlatformDefDto>()!.MaxHealth);   // = 100 × 5, stated
            Assert.Equal(1.5f, bradley.GetDescriptor<WeaponSuiteDto>()!.Mounts[1].AimSeconds);   // the TOW's lay
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

