using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Tkb.Parameters;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests
{
    /// <summary>
    /// ⭐ Buildings programme Stage 0 — <see cref="ParameterResolver"/>: every value says where it came from, and the runtime
    /// uses the same rules (📄 docs/DESIGN_Terrain_Combat_Tuning.md §2, §4, §6 T-0).
    /// </summary>
    public sealed class ParameterResolverTests
    {
        private static ResolvedParameter Get(TkbTemplate t, string name) => ParameterResolver.ResolveAll(t).Single(p => p.Name == name);

        [Fact]
        public void EmptyType_HasNoCombatParameters_AndTheSoldierEyeFallbacks()
        {
            var t = new TkbTemplate("Bare", 1);
            Assert.Equal(ParameterProvenance.NotApplicable, Get(t, ParameterNames.MaxHealth).Provenance);
            Assert.Null(Get(t, ParameterNames.MaxHealth).Value);
            Assert.Equal(ParameterProvenance.NotApplicable, Get(t, ParameterNames.ColliderRadius).Provenance);
            var eye = Get(t, ParameterNames.EyeCrouched);
            Assert.Equal(EngineFallbacks.EyeHeightCrouched, eye.Value);
            Assert.Equal(ParameterProvenance.EngineFallback, eye.Provenance);
            Assert.Equal("EngineFallbacks.EyeHeightCrouched", eye.Source);
        }

        [Fact]
        public void UnstatedMountNumbers_FallBack_StatedOnesAreExplicit()
        {
            var t = new TkbTemplate("Gun", 2);
            t.AddDescriptor(new WeaponSuiteDto { Mounts = new List<WeaponMountDto>
            {
                new() { InitialAmmunition = 10 },                                                     // nothing stated
                new() { MuzzleVelocity = 900f, DamagePerHit = 40f, Penetration = 12f, Range = 600f },
            } });

            Is(Get(t, "Weapon[0].MuzzleVelocity"), 800f, ParameterProvenance.EngineFallback);
            Is(Get(t, "Weapon[0].DamagePerHit"), 25f, ParameterProvenance.EngineFallback);
            Is(Get(t, "Weapon[0].Penetration"), 0f, ParameterProvenance.EngineFallback);
            Is(Get(t, "Weapon[1].MuzzleVelocity"), 900f, ParameterProvenance.Explicit);
            Is(Get(t, "Weapon[1].DamagePerHit"), 40f, ParameterProvenance.Explicit);
            Assert.Equal("WeaponSuiteDto.Mounts[1].Penetration", Get(t, "Weapon[1].Penetration").Source);
        }

        /// <summary>⭐ Buildings §3d P1/P1b (R-217) — penetration comes from the launcher × ammo pair: (ammo, this weapon) →
        /// (ammo, generic) → the mount's own value → 0; the fire chain and the resolver read one rule.</summary>
        [Fact]
        public void R217_Penetration_PairThenGenericThenMount_TheRuntimeReadsTheSameRule()
        {
            var db = new TkbDatabase();
            var ammo = new TkbTemplate("5.56 ball", 900);
            ammo.AddDescriptor(new AmmoWeaponBallisticsDto { WeaponGuid = 0,   PenetrationMm = 6f }, partId: 0);   // generic
            ammo.AddDescriptor(new AmmoWeaponBallisticsDto { WeaponGuid = 501, PenetrationMm = 8f }, partId: 1);   // from the long barrel
            ammo.AddDescriptor(new AmmoWeaponBallisticsDto { WeaponGuid = 502, PenetrationMm = 0f }, partId: 2);   // states nothing
            db.Register(ammo);

            var t = new TkbTemplate("Rifleman", 2);
            t.AddDescriptor(new WeaponSuiteDto { Mounts = new List<WeaponMountDto>
            {
                new() { WeaponGuid = 501, AmmoGuid = 900, Penetration = 5f, DamagePerHit = 25f },   // the pair
                new() { WeaponGuid = 503, AmmoGuid = 900, Penetration = 5f, DamagePerHit = 25f },   // no profile for 503 ⇒ generic
                new() { WeaponGuid = 502, AmmoGuid = 900, Penetration = 5f, DamagePerHit = 25f },   // 502 states nothing ⇒ generic
                new() { WeaponGuid = 501, AmmoGuid = 0,   Penetration = 5f, DamagePerHit = 25f },   // no loaded ammo ⇒ the mount
                new() { WeaponGuid = 501, AmmoGuid = 777, Penetration = 5f, DamagePerHit = 25f },   // unknown ammo type ⇒ the mount
            } });
            db.Register(t);

            float[] expected = { 8f, 6f, 6f, 5f, 5f };
            var all = ParameterResolver.ResolveAll(t, db);
            for (int i = 0; i < expected.Length; i++)
            {
                var p = all.Single(x => x.Name == $"Weapon[{i}].Penetration");
                Assert.Equal(expected[i], p.Value);
                Assert.Equal(expected[i], ParameterResolver.MountPenetration(db, t.GetDescriptor<WeaponSuiteDto>()!.Mounts[i]).Value);
            }
            Assert.Equal("5.56 ball: Gen.AmmoWeaponBallistics#1.PenetrationMm (this weapon)", all.Single(x => x.Name == "Weapon[0].Penetration").Source);
            Assert.Contains("(generic)", all.Single(x => x.Name == "Weapon[1].Penetration").Source);
            Assert.Equal("WeaponSuiteDto.Mounts[3].Penetration", all.Single(x => x.Name == "Weapon[3].Penetration").Source);
            // without a database the mount's own value is reported (as before R-217)
            Assert.Equal(5f, ParameterResolver.ResolveAll(t).Single(x => x.Name == "Weapon[0].Penetration").Value);
        }

        [Fact]
        public void ARecordedFormula_MakesAStatedValueGenerated()
        {
            var t = new TkbTemplate("Tank", 3);
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 3000f, ArmorFront = 600f });
            var gen = new TkbGeneratedValuesDto();
            gen.Formulas[ParameterNames.MaxHealth] = "armourFront × 5 (armourFront = 600)";
            t.AddDescriptor(gen);

            var h = Get(t, ParameterNames.MaxHealth);
            Is(h, 3000f, ParameterProvenance.Generated);
            Assert.Equal("armourFront × 5 (armourFront = 600)", h.Source);
            Is(Get(t, ParameterNames.ArmourFront), 600f, ParameterProvenance.Explicit);
            Is(Get(t, ParameterNames.ColliderRadius), EngineFallbacks.ColliderRadius, ParameterProvenance.EngineFallback);
        }

        [Fact]
        public void Collider_FromVehicleSize_IsGenerated_EyesFromAStandingOnlySensor_CopyStanding()
        {
            var t = new TkbTemplate("Truck", 4);
            t.AddDescriptor(new VehicleParametersDto { Length = 6f, Width = 2.5f });
            t.AddDescriptor(new SensorCapabilitiesDto { EyeHeightStanding = 2.4f });

            Is(Get(t, ParameterNames.ColliderRadius), 3f, ParameterProvenance.Generated);
            Is(Get(t, ParameterNames.EyeStanding), 2.4f, ParameterProvenance.Explicit);
            Is(Get(t, ParameterNames.EyeProne), 2.4f, ParameterProvenance.Generated);   // = standing (the translator's rule)
        }

        /// <summary>
        /// ⭐⭐ The API cannot disagree with the simulation: what <see cref="CombatTkbTranslator"/> stamps equals what the resolver
        /// reports, for stated AND fallback values.
        /// </summary>
        [Fact]
        public void TheTranslatorStamps_ExactlyWhatTheResolverReports()
        {
            var t = new TkbTemplate("Platform", 5);
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 250f });
            t.AddDescriptor(new WeaponSuiteDto { Mounts = new List<WeaponMountDto> { new() { InitialAmmunition = 5 } } });

            using var repo = new EntityRepository();
            repo.RegisterComponent<Health>();
            repo.RegisterComponent<PhysicsCollider>();
            repo.RegisterComponent<WeaponState>();
            repo.RegisterComponent<WeaponMountInfo>();
            repo.RegisterComponent<PartMetadata>();
            var e = repo.CreateEntity();
            new CombatTkbTranslator().Inject(repo, e, t);

            Assert.Equal(Get(t, ParameterNames.MaxHealth).Value, repo.GetComponent<Health>(e).Max);
            Assert.Equal(Get(t, ParameterNames.ColliderRadius).Value, repo.GetComponent<PhysicsCollider>(e).Radius);
            Assert.Equal(Get(t, "Weapon[0].MuzzleVelocity").Value, repo.GetComponent<WeaponState>(e).MuzzleVelocity);
        }

        private static void Is(ResolvedParameter p, float value, ParameterProvenance provenance)
        {
            Assert.Equal(value, p.Value);
            Assert.Equal(provenance, p.Provenance);
        }
    }
}
