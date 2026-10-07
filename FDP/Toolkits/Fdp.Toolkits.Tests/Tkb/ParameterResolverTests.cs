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
