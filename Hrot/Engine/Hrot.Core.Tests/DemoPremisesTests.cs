using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Tkb.Parameters;
using Fdp.Toolkit.Tkb.Reference;
using Hrot.Map.Common;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Core.Tests
{
    /// <summary>
    /// ⭐⭐ Tuning T-2 — THE DEMO PREMISE TABLE (📄 docs/DESIGN_Terrain_Combat_Tuning.md §3 layer 2). Every <c>bt-*</c> demo's
    /// premises live as data beside the terrain they run on (<c>assets/terrain/bt-range/premises.json</c>); this theory evaluates
    /// each row against what the simulation will actually use — the shipped TKB (<see cref="HrotEnvironment.CreateTkb"/>) through
    /// <see cref="ParameterResolver"/>, the generated <see cref="ReferenceLibrary"/>, the material library, and the
    /// <c>bt-range</c> GEOMETRY — and requires a MARGIN from every threshold, so a demo that would pass by luck fails here.
    /// <para>⭐ A tuning change that breaks a demo fails in milliseconds, naming the demo, the row, the round and every crossing —
    /// not five minutes into a scenario run with "target still alive".</para>
    /// </summary>
    public sealed class DemoPremisesTests
    {
        /// <summary>A fire crossing must be ≥ this (passes) or ≤ <see cref="StopsBelow"/> (stops): the 0.8–1.2 ramp ± 25 % of its width.</summary>
        public const float PassesAbove = 1.3f, StopsBelow = 0.7f;
        /// <summary>Sight must be ≥ this (sees through) or ≤ <see cref="BlocksBelow"/>: the 0.5 threshold ± 0.1.</summary>
        public const float SeesAbove = 0.6f, BlocksBelow = 0.4f;

        private readonly ITestOutputHelper _out;
        public DemoPremisesTests(ITestOutputHelper output) => _out = output;

        private static string TerrainFolder(string name)
        {
            return Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), name);
        }

        private static (JsonObject Demos, TerrainWorld World, Fdp.Toolkit.Tkb.TkbDatabase Tkb) Load(string terrain)
        {
            var folder = TerrainFolder(terrain);
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "premises.json")))!;
            var world = TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, terrain + ".world.geojson")), terrain,
                TerrainAssets.ForFolder(folder));
            return ((JsonObject)root["demos"]!, world, HrotEnvironment.CreateTkb());
        }

        private static readonly Lazy<(JsonObject Demos, TerrainWorld World, Fdp.Toolkit.Tkb.TkbDatabase Tkb)> s_fixture = new(() => Load("bt-range"));

        /// <summary>⭐ UI-12 (HANDOFF_Behaviors_To_Ui_Demo_City_Quarters) — demo-city's premises: the urban battle's stage
        /// (DESIGN_Urban_Battle_Demo §4a) — the battle street seen from upper windows and two roofs, the landing square from an office
        /// roof, a compound's yard hidden from the street.</summary>
        private static readonly Lazy<(JsonObject Demos, TerrainWorld World, Fdp.Toolkit.Tkb.TkbDatabase Tkb)> s_demoCity = new(() => Load("demo-city"));

        /// <summary>⭐ UI-4 (HANDOFF_Behaviors_To_Ui_Platoon_Demos, CE-2290) — cqb-village's premises: the platoon demos' stage
        /// (DESIGN_Platoon_Attack_Defend_Clear §12.1, PC-12).</summary>
        private static readonly Lazy<(JsonObject Demos, TerrainWorld World, Fdp.Toolkit.Tkb.TkbDatabase Tkb)> s_cqbVillage = new(() => Load("cqb-village"));

        public static IEnumerable<object[]> CqbVillagePremises()
        {
            foreach (var (demo, node) in s_cqbVillage.Value.Demos)
                foreach (var p in (JsonArray)node!["premises"]!)
                    yield return new object[] { demo, (string)p!["id"]! };
        }

        [Fact]
        public void UI4_CqbVillage_TheTable_HoldsTheStagesSightLines()
        {
            Assert.Contains("pc-stage", s_cqbVillage.Value.Demos.Select(d => d.Key));
            Assert.True(CqbVillagePremises().Count() >= 6, "cqb-village's premise table is near-empty");
        }

        [Theory]
        [MemberData(nameof(CqbVillagePremises))]
        public void UI4_ACqbVillagePremise_Holds_WithAMarginFromEveryThreshold(string demo, string premise)
            => Check(s_cqbVillage.Value, demo, premise);

        public static IEnumerable<object[]> DemoCityPremises()
        {
            foreach (var (demo, node) in s_demoCity.Value.Demos)
                foreach (var p in (JsonArray)node!["premises"]!)
                    yield return new object[] { demo, (string)p!["id"]! };
        }

        [Fact]
        public void UI12_DemoCity_TheTable_HoldsTheStagesSightLines()
        {
            Assert.Contains("urban-battle-street", s_demoCity.Value.Demos.Select(d => d.Key));
            Assert.True(DemoCityPremises().Count() >= 6, "demo-city's premise table is near-empty");
        }

        [Theory]
        [MemberData(nameof(DemoCityPremises))]
        public void UI12_ADemoCityPremise_Holds_WithAMarginFromEveryThreshold(string demo, string premise)
            => Check(s_demoCity.Value, demo, premise);

        public static IEnumerable<object[]> Premises()
        {
            foreach (var (demo, node) in s_fixture.Value.Demos)
                foreach (var p in (JsonArray)node!["premises"]!)
                    yield return new object[] { demo, (string)p!["id"]! };
        }

        [Fact]
        public void TheTable_CoversTheDemosOfThisStage_AndEveryRowHasAnExpectation()
        {
            var demos = s_fixture.Value.Demos.Select(d => d.Key).ToList();
            foreach (var d in new[] { "bt-wall-vs-fence", "bt-weapon-pair", "bt-window" }) Assert.Contains(d, demos);
            Assert.True(Premises().Count() >= 10, "the premise table is near-empty — the theory below would pass vacuously");
        }

        [Theory]
        [MemberData(nameof(Premises))]
        public void APremise_Holds_WithAMarginFromEveryThreshold(string demo, string premise) => Check(s_fixture.Value, demo, premise);

        private void Check((JsonObject Demos, TerrainWorld World, Fdp.Toolkit.Tkb.TkbDatabase Tkb) fixture, string demo, string premise)
        {
            var (demos, world, tkb) = fixture;
            var d = (JsonObject)demos[demo]!;
            var row = ((JsonArray)d["premises"]!).Single(p => (string)p!["id"]! == premise)!;
            string kind = (string)row["kind"]!, expect = (string)row["expect"]!;
            var from = Vec((JsonArray)row["from"]!);
            var to = Vec((JsonArray)row["to"]!);
            var report = new StringBuilder($"[{demo}] '{premise}' ({kind}, expect {expect}) {from} → {to}\n");

            if (kind == "sight")
            {
                var q = world.QuerySight(from, to);
                foreach (var c in q.Crossed) report.Append($"  crossed {c.Kind} '{c.Label}' {c.Material}: transmittance {c.Transmittance}\n");
                report.Append($"  total transmittance {q.Transmittance}\n");
                bool ok = expect switch
                {
                    "seesThrough" => q.Transmittance >= SeesAbove,
                    "blocks"      => q.Transmittance <= BlocksBelow,
                    _ => throw new ArgumentException($"sight expect '{expect}' (seesThrough | blocks)"),
                };
                if (!ok) Assert.Fail(report.Append($"  ⛔ needs {(expect == "seesThrough" ? $"≥ {SeesAbove}" : $"≤ {BlocksBelow}")}").ToString());
                _out.WriteLine(report.ToString());
                return;
            }

            Assert.Equal("fire", kind);
            var (pen, roundText) = Round((JsonObject)d["rounds"]![(string)row["round"]!]!, tkb);
            report.Append($"  round {(string)row["round"]!}: {roundText}\n");
            bool stopped = false;
            foreach (var c in world.QueryFire(from, to))
            {
                float effective = EngineFallbacks.TerrainPenetrationOrFallback(pen);
                float ratio = effective / c.ResistanceMmRha;
                report.Append($"  crossed {c.Kind} '{c.Label}' {c.Material}: {c.PathMetres:0.###} m = {c.ResistanceMmRha:0.##} mm RHA; round {effective:0.##} mm ⇒ ratio {ratio:0.##}\n");
                if (ratio > StopsBelow && ratio < PassesAbove)
                    Assert.Fail(report.Append($"  ⛔ ratio {ratio:0.##} is inside the ramp's margin ({StopsBelow}–{PassesAbove}) — the outcome would ride on tuning").ToString());
                if (ratio <= StopsBelow) { stopped = true; break; }
                if (pen > 0f) pen = MathF.Max(pen - c.ResistanceMmRha, 1e-3f);
            }
            if (!(expect switch { "stops" => stopped, "passes" => !stopped, _ => throw new ArgumentException($"fire expect '{expect}' (stops | passes)") }))
                Assert.Fail(report.Append($"  ⛔ the round {(stopped ? "stopped" : "got through")}").ToString());
            _out.WriteLine(report.ToString());
        }

        private static (float Pen, string Text) Round(JsonObject r, Fdp.Toolkit.Tkb.TkbDatabase tkb)
        {
            if (r["penetrationMm"] is JsonNode lit) return ((float)lit.GetValue<double>(), "literal");
            if (r["fragmentsOf"] is JsonObject frag)   // ⭐ CE-1032 — ONE fragment of the warhead that mount fires (ParameterResolver.Warhead)
            {
                long ft = (long)frag["tkbType"]!; int fm = (int)frag["mount"]!;
                Assert.True(tkb.TryGetByType(ft, out var ftt), $"TKB type {ft} is not in the shipped catalog");
                var fs = ftt.GetDescriptor<WeaponSuiteDto>();
                Assert.True(fs != null && fm < fs.Mounts.Count, $"{ftt.Name} has no mount {fm}");
                var (w, wsrc, _) = ParameterResolver.Warhead(tkb, unchecked((long)fs!.Mounts[fm].AmmoGuid));
                Assert.True(w != null && w.FragmentPenetrationMm > 0f, $"{ftt.Name} mount {fm} fires no fragmenting warhead ({wsrc})");
                return (w!.FragmentPenetrationMm, $"{w.FragmentPenetrationMm:0.##} mm — one fragment of {ftt.Name} mount {fm}: {wsrc}");
            }
            if (r["ammo"] is JsonNode ammo)
            {
                string? weapon = (string?)r["weapon"];
                Assert.True(ReferenceLibrary.Shared.TryGetPair((string)ammo!, weapon, out var pair), $"ammo '{ammo}' is not in the reference library");
                Assert.True(weapon == null || pair.Weapon == weapon, $"'{weapon}' does not fire '{ammo}' in the reference library");
                return (pair.PenetrationMm, $"{pair.PenetrationMm:0.##} mm — reference library {pair.Ammo} from {pair.Weapon ?? "generic"}: {pair.PenetrationFormula}");
            }
            long type = (long)r["tkbType"]!; int mount = (int)r["mount"]!;
            Assert.True(tkb.TryGetByType(type, out var t), $"TKB type {type} is not in the shipped catalog");
            var suite = t.GetDescriptor<WeaponSuiteDto>();
            Assert.True(suite != null && mount < suite.Mounts.Count, $"{t.Name} has no mount {mount}");
            var (v, source, prov) = ParameterResolver.MountPenetration(tkb, suite!.Mounts[mount]);
            return (v, $"{v:0.##} mm — {t.Name} mount {mount}, {prov}: {source}");
        }

        private static Vector3 Vec(JsonArray a) => new((float)a[0]!.GetValue<double>(), (float)a[1]!.GetValue<double>(), (float)a[2]!.GetValue<double>());
    }
}
