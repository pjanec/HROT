using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Tkb.Parameters;
using Fdp.Toolkit.Tkb.Reference;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests
{
    /// <summary>
    /// ⭐ Tuning T-1 — the generated reference library (📄 docs/DESIGN_Terrain_Combat_Tuning.md §2, §2a): the shipped data loads and
    /// is consistent, the generator's calibration anchors hold, and the resolver falls back to it only after explicit TKB data.
    /// ⚠ The anchors are calibration points of the COEFFICIENTS, deliberately loose (± 15 %) — the demos' outcomes are checked by
    /// the premise table, not here.
    /// </summary>
    public sealed class ReferenceLibraryTests
    {
        private static ReferenceLibrary Lib => ReferenceLibrary.Shared;

        private static float Pen(string ammo, string? weapon = null) { Assert.True(Lib.TryGetPair(ammo, weapon, out var p), ammo); return p.PenetrationMm; }

        [Fact]
        public void TheShippedLibrary_Loads_WithTheBreadthOfSection2_AndEveryWeaponFiresKnownAmmo()
        {
            Assert.True(Lib.Ammo.Count >= 19, $"{Lib.Ammo.Count} ammo types");
            foreach (var t in new[] { "ball", "ap", "apds", "apfsds", "heat", "hedp", "he", "frag" })
                Assert.Contains(Lib.Ammo, a => a.Type == t);
            foreach (var w in Lib.Weapons)
                foreach (var a in w.Ammo) Assert.True(Lib.TryGetAmmo(a, out _), $"{w.Name} fires {a}");
            Assert.All(Lib.Pairs, p => Assert.False(string.IsNullOrEmpty(p.PenetrationFormula)));
        }

        [Theory]
        [InlineData("5.56x45 ball", 5.9f)]     // the catalogs' rifle states 5
        [InlineData("12.7x99 AP", 26.7f)]
        [InlineData("25mm APDS", 59f)]         // the catalogs' M242 states 60
        [InlineData("120mm APFSDS", 655f)]     // the catalogs' M1 states 650
        [InlineData("PG-7V HEAT", 298f)]       // the catalogs' RPG states 300
        [InlineData("TOW-2 HEAT", 806f)]       // the catalogs' TOW states 800
        public void Calibration_TheGeneratedPenetration_IsNearTheCatalogsStatedNumbers(string ammo, float expected)
            => Assert.InRange(Pen(ammo), expected * 0.85f, expected * 1.15f);

        [Fact]
        public void Calibration_ARifleRound_DoesTheFlatDefaultDamage_AGrenadeHasAMetreScaleLethalBlast()
        {
            Assert.True(Lib.TryGetPair("5.56x45 ball", null, out var rifle));
            Assert.InRange(rifle.Damage, EngineFallbacks.BulletDamage * 0.9f, EngineFallbacks.BulletDamage * 1.1f);
            Assert.True(Lib.TryGetAmmo("M67 grenade", out var g));
            Assert.InRange(g.BlastLethalRadiusM, 1f, 1.5f);                // 2 · 0.24^⅓
            Assert.InRange(g.BlastInjuryRadiusM, 3f, 4.5f);
        }

        [Fact]
        public void APair_ShorterBarrel_LowerVelocity_LessPenetration_ShapedChargeIgnoresTheBarrel()
        {
            Assert.True(Lib.TryGetPair("5.56x45 ball", "M4_Carbine", out var m4));
            Assert.True(Lib.TryGetPair("5.56x45 ball", "M16A2", out var m16));
            Assert.Equal("M4_Carbine", m4.Weapon);
            Assert.True(m4.MuzzleSpeed < m16.MuzzleSpeed);
            Assert.True(m4.PenetrationMm < m16.PenetrationMm);
            Assert.True(Lib.TryGetPair("5.56x45 ball", "M2HB", out var notFired));   // M2HB does not fire it ⇒ the generic profile
            Assert.Null(notFired.Weapon);
            Assert.False(Lib.TryGetPair("no such round", null, out _));
        }

        [Fact]
        public void Generate_RejectsAWeaponFiringUnknownAmmo_AndAnAmmoWithNoCoefficient()
        {
            string coeff = """{ "kineticPenetration": { "k": { "ball": 0.1 } }, "shapedChargePenetration": { "coneFactor": { "heat": 5 } }, "highExplosivePenetration": { "factor": { "he": 0.2 } }, "damage": { "perSqrtJoule": 0.6, "perKgExplosive": 600 }, "blast": { "zLethal": 2, "zInjury": 6, "lethalDamage": 150 }, "fragments": { "sqrt2E": 2440, "fragmentMassG": 0.5, "kPenetration": 0.06, "radiusPerCbrtCasingKg": 28 } }""";
            string ammo = """{ "ammo": { "r": { "type": "ball", "calibreMm": 5.56, "massG": 4, "velocity": 930 } } }""";
            var ex = Assert.Throws<ArgumentException>(() => ReferenceLibrary.Generate(ammo, """{ "weapons": { "W": { "ammo": ["x"] } } }""", coeff));
            Assert.Contains("unknown ammo 'x'", ex.Message);
            ex = Assert.Throws<ArgumentException>(() => ReferenceLibrary.Generate("""{ "ammo": { "r": { "type": "slap", "calibreMm": 5, "massG": 4, "velocity": 900 } } }""", """{ "weapons": {} }""", coeff));
            Assert.Contains("'slap' has no coefficient", ex.Message);
        }

        /// <summary>⭐ The resolver chain: explicit TKB data wins; the library answers only for a NAMED ammo type with no numbers;
        /// no ammo type ⇒ unknown (0) — so the built-in catalogs, which name no ammo, resolve exactly as before.</summary>
        [Fact]
        public void Resolver_FallsBackToTheLibrary_OnlyAfterExplicitData_ByTheAmmoAndWeaponTypeNames()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("12.7x99 AP", 800));    // an ammo type stating no numbers
            db.Register(new TkbTemplate("M2HB", 801));          // the weapon type
            var m2 = new WeaponMountDto { WeaponGuid = 801, AmmoGuid = 800, DamagePerHit = 80f };

            var (v, source, prov) = ParameterResolver.MountPenetration(db, m2);
            Assert.Equal(ParameterProvenance.ReferenceByName, prov);
            Assert.Equal(Pen("12.7x99 AP", "M2HB"), v);
            Assert.Contains("12.7x99 AP from M2HB", source);
            Assert.Contains("k[ap]", source);

            Assert.Equal((5f, ParameterProvenance.Explicit),
                Pick(ParameterResolver.MountPenetration(db, m2 with { Penetration = 5f })));                // the mount's own number wins
            Assert.Equal((0f, ParameterProvenance.EngineFallback),
                Pick(ParameterResolver.MountPenetration(db, m2 with { AmmoGuid = 0 })));                    // no ammo named ⇒ unknown
            db.Register(new TkbTemplate("homemade slug", 802));
            Assert.Equal((0f, ParameterProvenance.EngineFallback),
                Pick(ParameterResolver.MountPenetration(db, m2 with { AmmoGuid = 802 })));                  // not in the library ⇒ unknown
        }

        /// <summary>
        /// ⭐ Stage 6 (<c>CE-1032</c>, W-1) — the generated warheads land where the real ones are: an M67's fragments reach ~15 m
        /// (its published casualty radius) and barely scratch a wall, it is a 4.5 s time fuze thrown at a point; an 81 mm bomb reaches
        /// tens of metres on an impact fuze; a rifle round has no warhead at all. ⚠ Loose bands — calibration points, not physics.
        /// </summary>
        [Fact]
        public void Calibration_TheGeneratedWarheads_GrenadeMortarAndKineticRound()
        {
            Assert.True(Lib.TryGetAmmo("M67 grenade", out var g));
            var w = g.Warhead!.Dto;
            Assert.Equal(WarheadKind.Fragmentation, w.Kind);
            Assert.Equal(FuzeKind.Time, w.Fuze);
            Assert.Equal(4.5f, w.FuzeDelayS);
            Assert.True(w.Indirect);
            Assert.InRange(w.FragmentRadiusM, 12f, 20f);
            Assert.InRange(w.FragmentPenetrationMm, 1f, 6f);
            Assert.InRange(w.FragmentDamage, 130f, 160f);
            Assert.Equal(g.BlastLethalRadiusM, w.BlastLethalRadiusM);
            Assert.Equal(150f, w.BlastLethalDamage);
            Assert.Contains("v₀ = √2E", g.Warhead.FragmentFormula);

            Assert.True(Lib.TryGetAmmo("81mm mortar HE", out var m));
            Assert.Equal(FuzeKind.Impact, m.Warhead!.Dto.Fuze);
            Assert.True(m.Warhead.Dto.Indirect);
            Assert.InRange(m.Warhead.Dto.FragmentRadiusM, 30f, 50f);
            Assert.True(m.Warhead.Dto.BlastInjuryRadiusM > w.BlastInjuryRadiusM);

            Assert.True(Lib.TryGetAmmo("5.56x45 ball", out var ball));
            Assert.Null(ball.Warhead);
        }

        /// <summary>⭐ Stage 6 (W-1) — the warhead chain: the type's own <c>Gen.Warhead</c> → the library by the type's NAME → none;
        /// <c>ResolveAll</c> lists it for a munition (DIS kind 2), so <c>GET /tkb/resolve</c> reports what the area effect uses.</summary>
        [Fact]
        public void Resolver_Warhead_TkbFirst_ThenTheLibraryByName_ThenNone()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("M67 grenade", 900) { DisType = new DISEntityType { Kind = 2 } });
            var own = new TkbTemplate("custom charge", 901);
            own.AddDescriptor(new WarheadDto { Kind = WarheadKind.HighExplosive, ExplosiveKg = 1f, FragmentRadiusM = 7f });
            db.Register(own);
            db.Register(new TkbTemplate("5.56x45 ball", 902));

            var (byName, source, prov) = ParameterResolver.Warhead(db, 900);
            Assert.Equal(ParameterProvenance.ReferenceByName, prov);
            Assert.Equal(Lib.Ammo.Single(a => a.Name == "M67 grenade").Warhead!.Dto, byName);
            Assert.Contains("reference library: M67 grenade", source);

            var (explicitW, _, p2) = ParameterResolver.Warhead(db, 901);
            Assert.Equal(ParameterProvenance.Explicit, p2);
            Assert.Equal(7f, explicitW!.FragmentRadiusM);

            Assert.Null(ParameterResolver.Warhead(db, 902).Warhead);                // a kinetic round
            Assert.Equal(ParameterProvenance.NotApplicable, ParameterResolver.Warhead(db, 0).Provenance);
            Assert.Null(ParameterResolver.Warhead(db, 12345).Warhead);              // not in the TKB

            Assert.True(db.TryGetByType(900, out var grenade));
            var rows = ParameterResolver.ResolveAll(grenade, db);
            var reach = rows.Single(r => r.Name == ParameterNames.WarheadFragmentRadius);
            Assert.Equal(byName!.FragmentRadiusM, reach.Value);
            Assert.Equal(ParameterProvenance.ReferenceByName, reach.Provenance);
            Assert.Equal(4.5f, rows.Single(r => r.Name == ParameterNames.WarheadFuzeDelay).Value);
            Assert.DoesNotContain(ParameterResolver.ResolveAll(db.GetAll().Single(t => t.TkbType == 902), db),
                r => r.Name.StartsWith("Warhead.", StringComparison.Ordinal));   // not a munition, no warhead ⇒ no rows
        }

        private static (float, ParameterProvenance) Pick((float Value, string Source, ParameterProvenance Provenance) r) => (r.Value, r.Provenance);
    }
}
