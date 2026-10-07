using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Tkb.Reference
{
    /// <summary>A weapon (launcher) of the reference library: its velocity relative to an ammo's nominal, and the ammo it fires.</summary>
    public sealed record RefWeapon(string Name, float VelocityFactor, IReadOnlyList<string> Ammo);

    /// <summary>
    /// One launcher × ammo pair, GENERATED: muzzle speed, penetration (mm RHA) and damage, each with the formula and inputs that
    /// produced it (its provenance on <c>GET /tkb/resolve</c>). <see cref="Weapon"/> null = the ammo's generic profile (nominal velocity).
    /// </summary>
    public sealed record RefPair(string Ammo, string? Weapon, float MuzzleSpeed, float PenetrationMm, float Damage,
        string PenetrationFormula, string DamageFormula);

    /// <summary>An ammunition of the reference library: its driving parameters and what the generator derived from them.</summary>
    public sealed record RefAmmo(string Name, string Type, float CalibreMm, float MassG, float Velocity, float ExplosiveKg,
        RefPair Generic, float BlastLethalRadiusM, float BlastInjuryRadiusM);

    /// <summary>
    /// ⭐⭐ Buildings programme Stage 3 / tuning T-1 — THE REFERENCE LIBRARY, generated at load from a few driving parameters per
    /// type through ONE coefficients file (📄 <c>docs/DESIGN_Terrain_Combat_Tuning.md</c> §2, §2a). It is the resolver step after
    /// explicit TKB data: <c>ParameterResolver.MountPenetration</c> falls back to the pair of the mount's ammo (by the ammo TKB
    /// type's NAME) and weapon (by the weapon type's name) when the TKB states no number.
    /// <para>Formulas (deliberately simple engineering approximations — real data overrides):
    /// kinetic <c>pen = k[type] · v · √m</c> · shaped charge <c>pen = coneFactor · calibre</c> · HE <c>pen = factor · calibre</c> ·
    /// damage <c>perSqrtJoule · √(½ m v²)</c> (kinetic) or <c>perKgExplosive · kg</c> · blast <c>R = Z · W^⅓</c> (for <c>CE-1032</c>).</para>
    /// <para>⚠ NOT TKB templates: a file-loaded TKB clears the database, and every catalog template is a spawnable entity type
    /// (<c>HrotEnvironmentTests</c>). The library is engine data, embedded like the terrain materials, present on every node.</para>
    /// </summary>
    public sealed class ReferenceLibrary
    {
        private static readonly Lazy<ReferenceLibrary> s_shared = new(LoadShared);
        private readonly Dictionary<string, RefAmmo> _ammo;
        private readonly Dictionary<string, RefWeapon> _weapons;
        private readonly Dictionary<(string Ammo, string Weapon), RefPair> _pairs;

        private ReferenceLibrary(Dictionary<string, RefAmmo> ammo, Dictionary<string, RefWeapon> weapons,
            Dictionary<(string, string), RefPair> pairs)
        {
            _ammo = ammo; _weapons = weapons; _pairs = pairs;
        }

        /// <summary>The library shipped with the engine (embedded <c>ammo.json</c>, <c>weapons.json</c>, <c>coefficients.json</c>).</summary>
        public static ReferenceLibrary Shared => s_shared.Value;

        public IReadOnlyCollection<RefAmmo> Ammo => _ammo.Values;
        public IReadOnlyCollection<RefWeapon> Weapons => _weapons.Values;
        public IReadOnlyCollection<RefPair> Pairs => _pairs.Values;

        public bool TryGetAmmo(string name, out RefAmmo ammo) => _ammo.TryGetValue(name, out ammo!);
        public bool TryGetWeapon(string name, out RefWeapon weapon) => _weapons.TryGetValue(name, out weapon!);

        /// <summary>
        /// The pair of <paramref name="ammo"/> fired from <paramref name="weapon"/>: the generated pair when the weapon is a library
        /// weapon that fires this ammo, else the ammo's generic profile; false when the ammo is not in the library.
        /// </summary>
        public bool TryGetPair(string ammo, string? weapon, out RefPair pair)
        {
            pair = null!;
            if (!_ammo.TryGetValue(ammo, out var a)) return false;
            pair = weapon != null && _pairs.TryGetValue((ammo, weapon), out var p) ? p : a.Generic;
            return true;
        }

        /// <summary>Generates a library from driving-parameter JSON (the three files' contents).</summary>
        public static ReferenceLibrary Generate(string ammoJson, string weaponsJson, string coefficientsJson)
        {
            var c = Coefficients.Parse(coefficientsJson);
            var ammo = new Dictionary<string, RefAmmo>(StringComparer.Ordinal);
            foreach (var (name, o) in Entries(ammoJson, "ammo", "ammo.json"))
            {
                var d = AmmoDriving.Parse(name, o, c);
                ammo[name] = new RefAmmo(name, d.Type, d.CalibreMm, d.MassG, d.Velocity, d.ExplosiveKg,
                    Pair(d, null, 1f, c), c.ZLethal * Cbrt(d.ExplosiveKg), c.ZInjury * Cbrt(d.ExplosiveKg));
            }

            var weapons = new Dictionary<string, RefWeapon>(StringComparer.Ordinal);
            var pairs = new Dictionary<(string, string), RefPair>();
            foreach (var (name, o) in Entries(weaponsJson, "weapons", "weapons.json"))
            {
                float factor = o["velocityFactor"] is JsonNode f ? (float)f.GetValue<double>() : 1f;
                if (factor <= 0f) throw new ArgumentException($"Reference library weapons.json: '{name}'.velocityFactor must be > 0.");
                var list = (o["ammo"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList() ?? new List<string>();
                foreach (var a in list)
                {
                    if (!ammo.ContainsKey(a))
                        throw new ArgumentException($"Reference library weapons.json: '{name}' fires unknown ammo '{a}' (known: {string.Join(", ", ammo.Keys)}).");
                    pairs[(a, name)] = Pair(AmmoDriving.Parse(a, AmmoNode(ammoJson, a), c), name, factor, c);
                }
                weapons[name] = new RefWeapon(name, factor, list);
            }
            return new ReferenceLibrary(ammo, weapons, pairs);
        }

        private static RefPair Pair(AmmoDriving d, string? weapon, float velocityFactor, Coefficients c)
        {
            float v = d.Velocity * velocityFactor;
            float m = d.MassG / 1000f;
            string vText = velocityFactor == 1f ? $"v = {F(v)}" : $"v = {F(d.Velocity)} × {F(velocityFactor)} = {F(v)}";
            float pen; string penFormula;
            switch (d.Type)
            {
                case "ball": case "ap": case "apds": case "apfsds":
                    float k = c.Kinetic[d.Type];
                    pen = k * v * MathF.Sqrt(m);
                    penFormula = $"k[{d.Type}] · v · √m (k = {F(k)}, {vText} m/s, m = {F(m)} kg)";
                    break;
                case "heat": case "hedp":
                    float cone = d.ConeFactor ?? c.Cone[d.Type];
                    pen = cone * d.CalibreMm;
                    penFormula = $"coneFactor · calibre (coneFactor = {F(cone)}{(d.ConeFactor.HasValue ? " — this ammo's" : "")}, calibre = {F(d.CalibreMm)} mm)";
                    break;
                default:   // he, frag
                    float f = c.HighExplosive[d.Type];
                    pen = f * d.CalibreMm;
                    penFormula = $"factor[{d.Type}] · calibre (factor = {F(f)}, calibre = {F(d.CalibreMm)} mm)";
                    break;
            }
            float dmg; string dmgFormula;
            if (d.ExplosiveKg > 0f)
            {
                dmg = c.PerKgExplosive * d.ExplosiveKg;
                dmgFormula = $"perKgExplosive · explosive (= {F(c.PerKgExplosive)} · {F(d.ExplosiveKg)} kg)";
            }
            else
            {
                float joules = 0.5f * m * v * v;
                dmg = c.PerSqrtJoule * MathF.Sqrt(joules);
                dmgFormula = $"perSqrtJoule · √(½ m v²) (= {F(c.PerSqrtJoule)} · √{F(joules)} J)";
            }
            return new RefPair(d.Name, weapon, v, pen, dmg, penFormula, dmgFormula);
        }

        private static float Cbrt(float x) => x > 0f ? MathF.Cbrt(x) : 0f;
        private static string F(float x) => x.ToString("0.###", CultureInfo.InvariantCulture);

        private static IEnumerable<(string Name, JsonObject Node)> Entries(string json, string key, string source)
        {
            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch (JsonException ex) { throw new ArgumentException($"Reference library '{source}' is not valid JSON: {ex.Message}", ex); }
            if (root?[key] is not JsonObject obj) throw new ArgumentException($"Reference library '{source}' has no '{key}' object.");
            foreach (var (name, node) in obj)
                yield return (name, node as JsonObject ?? throw new ArgumentException($"Reference library '{source}': '{name}' is not an object."));
        }

        private static JsonObject AmmoNode(string ammoJson, string name)
            => Entries(ammoJson, "ammo", "ammo.json").First(e => e.Name == name).Node;

        private sealed record AmmoDriving(string Name, string Type, float CalibreMm, float MassG, float Velocity, float ExplosiveKg, float? ConeFactor)
        {
            public static AmmoDriving Parse(string name, JsonObject o, Coefficients c)
            {
                string type = o["type"]?.GetValue<string>() ?? throw new ArgumentException($"Reference library ammo.json: '{name}' has no 'type'.");
                if (!c.KnowsType(type))
                    throw new ArgumentException($"Reference library ammo.json: '{name}'.type '{type}' has no coefficient (ball, ap, apds, apfsds, heat, hedp, he, frag).");
                float G(string key, bool required)
                    => o[key] is JsonNode n ? (float)n.GetValue<double>()
                       : required ? throw new ArgumentException($"Reference library ammo.json: '{name}' has no '{key}'.") : 0f;
                return new AmmoDriving(name, type, G("calibreMm", true), G("massG", true), G("velocity", true), G("explosiveKg", false),
                    o["coneFactor"] is JsonNode cf ? (float)cf.GetValue<double>() : null);
            }
        }

        private sealed record Coefficients(Dictionary<string, float> Kinetic, Dictionary<string, float> Cone,
            Dictionary<string, float> HighExplosive, float PerSqrtJoule, float PerKgExplosive, float ZLethal, float ZInjury)
        {
            public bool KnowsType(string t) => Kinetic.ContainsKey(t) || Cone.ContainsKey(t) || HighExplosive.ContainsKey(t);

            public static Coefficients Parse(string json)
            {
                var root = JsonNode.Parse(json) ?? throw new ArgumentException("Reference library 'coefficients.json' is empty.");
                Dictionary<string, float> Map(string section, string key)
                    => (root[section]?[key] as JsonObject ?? throw new ArgumentException($"Reference library coefficients.json: no '{section}.{key}'."))
                        .ToDictionary(p => p.Key, p => (float)p.Value!.GetValue<double>(), StringComparer.Ordinal);
                float V(string section, string key)
                    => root[section]?[key] is JsonNode n ? (float)n.GetValue<double>()
                       : throw new ArgumentException($"Reference library coefficients.json: no '{section}.{key}'.");
                return new Coefficients(Map("kineticPenetration", "k"), Map("shapedChargePenetration", "coneFactor"),
                    Map("highExplosivePenetration", "factor"), V("damage", "perSqrtJoule"), V("damage", "perKgExplosive"),
                    V("blast", "zLethal"), V("blast", "zInjury"));
            }
        }

        private static ReferenceLibrary LoadShared()
        {
            static string Read(string file)
            {
                using var s = typeof(ReferenceLibrary).Assembly.GetManifestResourceStream("Fdp.Toolkit.Tkb.Reference." + file)
                    ?? throw new InvalidOperationException($"Reference library: the embedded Fdp.Toolkit.Tkb.Reference.{file} is missing.");
                using var r = new StreamReader(s);
                return r.ReadToEnd();
            }
            return Generate(Read("ammo.json"), Read("weapons.json"), Read("coefficients.json"));
        }
    }
}
