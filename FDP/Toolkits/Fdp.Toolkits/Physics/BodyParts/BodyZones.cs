using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Tkb.Parameters;

namespace Fdp.Toolkit.Physics.BodyParts
{
    /// <summary>
    /// ⭐ <c>CE-3210</c> — one resolved zone of a body: its parts (a bit per part id), armour per face (front, side, rear, top — the
    /// <see cref="Facing"/> order), damage multiplier, wound effect and its threshold, and where each value came from.
    /// </summary>
    public sealed class ResolvedZone
    {
        public string Name { get; init; } = "";
        public uint PartMask { get; init; }
        public float[] Armour { get; } = new float[4];
        public float Multiplier { get; set; } = 1f;
        public WoundEffect Effect { get; set; }
        public int WoundedAt { get; set; } = 255;
        /// <summary>Where each value came from: "Armour.Front" → (provenance, source).</summary>
        public Dictionary<string, (ParameterProvenance Provenance, string Source)> Sources { get; } = new();

        /// <summary>The zone's armour on <paramref name="face"/>; ⭐ <c>CE-3211</c> (S2-7) — NaN when the zone INHERITS the platform's armour
        /// (a vehicle zone with no number of its own): the damage step then keeps <c>ArmorModel</c>'s, the CE armour included.</summary>
        public float ArmourOn(Facing face) => Armour[System.Math.Clamp((int)face, 0, 3)];
        public bool Has(byte part) => part < 32 && (PartMask & (1u << part)) != 0;
    }

    /// <summary>⭐ <c>CE-3210</c> — a type's resolved body: its plan, its zones, and the profile it matched.</summary>
    public sealed class ResolvedBody
    {
        public BodyPlan Plan { get; init; }
        public string Profile { get; init; } = "";
        public ParameterProvenance ProfileProvenance { get; init; }
        public IReadOnlyList<ResolvedZone> Zones { get; init; } = Array.Empty<ResolvedZone>();
        /// <summary>⭐ <c>CE-3211</c> (S2-5) — the vehicle kit its parts are shaped by (<see cref="VehicleBody"/>), null for a person.</summary>
        public string? Kit { get; init; }
        /// <summary>⭐ <c>CE-3211</c> (S2-4) — which end of the main hull box holds the engine.</summary>
        public EngineEnd EngineEnd { get; init; }

        /// <summary>The zone <paramref name="part"/> belongs to, or null.</summary>
        public ResolvedZone? ZoneOf(byte part)
        {
            foreach (var z in Zones) if (z.Has(part)) return z;
            return null;
        }

        /// <summary>The zone named <paramref name="name"/>, or null.</summary>
        public ResolvedZone? Zone(string name)
        {
            foreach (var z in Zones) if (string.Equals(z.Name, name, StringComparison.OrdinalIgnoreCase)) return z;
            return null;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3210</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-4, HL-12; 🔒 user: "not for each tkb type
    /// separately (class templates?), but customizable per tkb type if really needed (overrides?)") — a type's body ZONES, resolved through
    /// the chain the TKB already has (Terrain Combat Tuning §2): ① the type's own SPARSE <see cref="BodyZonesDto"/> ② its platform armour
    /// as the TORSO's (a person's explicit armour stays its own — no number is lost) ③ the body profile matched by DIS class in the
    /// engine's <c>bodies.json</c>, most specific first ④ the plan's engine fallback. Merged FIELD BY FIELD; every value carries its
    /// provenance. ⛔ A profile never ADDS combat: only a person (DIS kind 3, or a type that asks with its own zones) has a body here.
    /// </summary>
    public static class BodyZones
    {
        private sealed record Profile(string Name, BodyPlan Plan, int?[][] Dis, List<BodyZoneDto> Zones, string? Kit = null,
                                      EngineEnd EngineEnd = EngineEnd.None, bool InheritArmour = false);

        private static readonly Lazy<Profile[]> s_profiles = new(LoadProfiles);
        private static readonly ConditionalWeakTable<TkbTemplate, ResolvedBody?[]> s_cache = new();

        /// <summary>The resolved body of <paramref name="template"/>, or null when it has none (not a person, no zones).</summary>
        public static ResolvedBody? Of(TkbTemplate? template)
        {
            if (template == null) return null;
            var slot = s_cache.GetValue(template, _ => new ResolvedBody?[1]);
            return slot[0] ??= Resolve(template);
        }

        /// <summary>The resolved body of <paramref name="entity"/>'s type, or the PERSON fallback when it is a person with no type.</summary>
        public static ResolvedBody? Of(EntityRepository world, Entity entity)
        {
            if (world.IsComponentTypeRegistered<TkbIdentity>() && world.HasComponent<TkbIdentity>(entity)
                && world.HasSingletonManaged<ITkbDatabase>() && world.GetSingletonManaged<ITkbDatabase>() is { } db
                && db.TryGetByType(world.GetComponentRO<TkbIdentity>(entity).TkbType, out var t))
                return Of(t);   // ⛔ a typed non-person has no body — never the person fallback
            return PersonFallback.Value;
        }

        /// <summary>The engine's person body: the "Person" profile's zones, or the code's when the library lacks it.</summary>
        public static readonly Lazy<ResolvedBody> PersonFallback = new(() =>
            Build(BodyPlan.Person, FindProfile("Person")?.Zones ?? DefaultPersonZones(), FindProfile("Person") is { } p ? $"profile {p.Name}" : "engine",
                  FindProfile("Person") != null ? ParameterProvenance.ReferenceByDis : ParameterProvenance.EngineFallback, null, null, null));

        private static ResolvedBody? Resolve(TkbTemplate t)
        {
            var own = t.GetDescriptor<BodyZonesDto>();
            var dis = t.DisType;
            var (profile, pattern) = Match(dis);
            if (profile == null && dis.Kind != 3 && own == null) return null;   // ⛔ a profile never adds a body to a non-person
            var plan = profile?.Plan ?? BodyPlan.Person;
            var platform = t.GetDescriptor<CombatPlatformDefDto>();
            if (plan != BodyPlan.Person && platform == null) return null;   // ⛔ HL-12 / S2-5: a vehicle with no combat platform has no body
            var baseZones = profile?.Zones ?? DefaultPersonZones();
            string source = profile != null ? $"profile {profile.Name} ({string.Join('.', Array.ConvertAll(pattern!, d => d?.ToString() ?? "*"))})" : "engine";
            var prov = profile != null ? ParameterProvenance.ReferenceByDis : ParameterProvenance.EngineFallback;
            return Build(plan, baseZones, source, prov, own, platform, profile);
        }

        private static ResolvedBody Build(BodyPlan plan, List<BodyZoneDto> baseZones, string source, ParameterProvenance prov,
                                          BodyZonesDto? own, CombatPlatformDefDto? platform, Profile? profile)
        {
            // ⭐ CE-3211 (S2-7) — a vehicle profile INHERITS the platform's armour on every face it states no number for (NaN)
            float unstated = profile?.InheritArmour == true ? float.NaN : 0f;
            string unstatedSource = profile?.InheritArmour == true ? "inherited: the platform's armour (ArmorModel)" : $"none in {source}";
            var zones = new List<ResolvedZone>();
            foreach (var b in baseZones)
            {
                uint mask = 0;
                if (b.Parts != null) foreach (var p in b.Parts) if (p < 32) mask |= 1u << p;
                var z = new ResolvedZone { Name = b.Zone, PartMask = mask };
                Set(z, "Armour.Front", b.ArmourFront, v => z.Armour[0] = v, prov, source, unstated, unstatedSource);
                Set(z, "Armour.Side", b.ArmourSide, v => z.Armour[1] = v, prov, source, unstated, unstatedSource);
                Set(z, "Armour.Rear", b.ArmourRear, v => z.Armour[2] = v, prov, source, unstated, unstatedSource);
                Set(z, "Armour.Top", b.ArmourTop, v => z.Armour[3] = v, prov, source, unstated, unstatedSource);
                Set(z, "Multiplier", b.Multiplier, v => z.Multiplier = v, prov, source, 1f, $"none in {source}");
                z.Effect = b.Effect ?? WoundEffect.None;
                z.WoundedAt = b.WoundedAt ?? 255;
                z.Sources["Effect"] = (prov, source);

                // ② a person's own platform armour is its TORSO's (the type stated it; it stays its own)
                if (platform != null && plan == BodyPlan.Person && string.Equals(b.Zone, "Torso", StringComparison.OrdinalIgnoreCase))
                {
                    Explicit(z, "Armour.Front", platform.ArmorFront, 0, "Combat.PlatformDef.ArmorFront");
                    Explicit(z, "Armour.Side", platform.ArmorSide, 1, "Combat.PlatformDef.ArmorSide");
                    Explicit(z, "Armour.Rear", platform.ArmorRear, 2, "Combat.PlatformDef.ArmorRear");
                    Explicit(z, "Armour.Top", platform.ArmorTop, 3, "Combat.PlatformDef.ArmorTop");
                }
                // ① the type's sparse override, field by field
                if (own != null)
                    foreach (var o in own.Zones)
                    {
                        if (!string.Equals(o.Zone, b.Zone, StringComparison.OrdinalIgnoreCase)) continue;
                        if (o.ArmourFront is { } af) { z.Armour[0] = af; z.Sources["Armour.Front"] = (ParameterProvenance.Explicit, "Combat.BodyZones"); }
                        if (o.ArmourSide is { } asd) { z.Armour[1] = asd; z.Sources["Armour.Side"] = (ParameterProvenance.Explicit, "Combat.BodyZones"); }
                        if (o.ArmourRear is { } ar) { z.Armour[2] = ar; z.Sources["Armour.Rear"] = (ParameterProvenance.Explicit, "Combat.BodyZones"); }
                        if (o.ArmourTop is { } at) { z.Armour[3] = at; z.Sources["Armour.Top"] = (ParameterProvenance.Explicit, "Combat.BodyZones"); }
                        if (o.Multiplier is { } m) { z.Multiplier = m; z.Sources["Multiplier"] = (ParameterProvenance.Explicit, "Combat.BodyZones"); }
                        if (o.Effect is { } e) { z.Effect = e; z.Sources["Effect"] = (ParameterProvenance.Explicit, "Combat.BodyZones"); }
                        if (o.WoundedAt is { } w) z.WoundedAt = w;
                    }
                zones.Add(z);
            }
            return new ResolvedBody { Plan = plan, Profile = source, ProfileProvenance = prov, Zones = zones, Kit = profile?.Kit,
                                      EngineEnd = profile?.EngineEnd ?? EngineEnd.None };
        }

        private static void Set(ResolvedZone z, string key, float? value, Action<float> set, ParameterProvenance prov, string source, float dflt,
                                string dfltSource)
        {
            if (value is { } v) { set(v); z.Sources[key] = (prov, source); }
            else { set(dflt); z.Sources[key] = (ParameterProvenance.EngineFallback, dfltSource); }
        }

        private static void Explicit(ResolvedZone z, string key, float value, int index, string source)
        {
            if (value <= 0f) return;
            z.Armour[index] = value;
            z.Sources[key] = (ParameterProvenance.Explicit, source);
        }

        /// <summary>⭐ <c>CE-3210</c> — the resolved body as <c>/tkb/resolve</c> parameters: <c>Body.&lt;Zone&gt;.Armour.Front</c>, … with provenance.</summary>
        public static IEnumerable<ResolvedParameter> Parameters(TkbTemplate template)
        {
            if (Of(template) is not { } body) yield break;
            var platform = template.GetDescriptor<CombatPlatformDefDto>();
            foreach (var z in body.Zones)
            {
                string[] faces = { "Armour.Front", "Armour.Side", "Armour.Rear", "Armour.Top" };
                for (int i = 0; i < 4; i++)
                {
                    var (p, s) = z.Sources.TryGetValue(faces[i], out var v) ? v : (ParameterProvenance.EngineFallback, "engine");
                    float value = z.Armour[i];
                    if (float.IsNaN(value))   // ⭐ CE-3211 — an inherited face reports the platform's number (KE; ArmorModel picks CE for a HEAT round)
                    {
                        value = platform == null ? 0f : i switch { 0 => platform.ArmorFront, 1 => platform.ArmorSide, 2 => platform.ArmorRear, _ => platform.ArmorTop };
                        (p, s) = (ParameterProvenance.Explicit, $"inherited: Combat.PlatformDef.Armor{faces[i].Substring(7)}");
                    }
                    yield return new ResolvedParameter($"Body.{z.Name}.{faces[i]}", value, p, s);
                }
                var (mp, ms) = z.Sources.TryGetValue("Multiplier", out var mv) ? mv : (ParameterProvenance.EngineFallback, "engine");
                yield return new ResolvedParameter($"Body.{z.Name}.Multiplier", z.Multiplier, mp, ms);
            }
        }

        // ── the library ────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static Profile? FindProfile(string name)
        {
            foreach (var p in s_profiles.Value) if (p.Name == name) return p;
            return null;
        }

        /// <summary>The most specific profile whose DIS pattern matches <paramref name="dis"/>, or null.</summary>
        private static (Profile? Profile, int?[]? Pattern) Match(DISEntityType dis)
        {
            if (dis.Value == 0) return (null, null);
            int[] fields = { dis.Kind, dis.Domain, dis.Country, dis.Category, dis.Subcategory, dis.Specific, dis.Extra };
            Profile? best = null;
            int?[]? bestPattern = null;
            int bestSpec = -1;
            foreach (var p in s_profiles.Value)
                foreach (var pattern in p.Dis)   // ⭐ CE-3211 — a profile may name several classes (WheeledUtility: 1.1.*.3, 6, 7)
                {
                    bool ok = true;
                    int spec = 0;
                    for (int i = 0; i < pattern.Length && ok; i++)
                    {
                        if (pattern[i] is not { } want) continue;
                        ok = want == fields[i];
                        spec++;
                    }
                    if (ok && spec > bestSpec) { best = p; bestPattern = pattern; bestSpec = spec; }
                }
            return (best, bestPattern);
        }

        private static Profile[] LoadProfiles()
        {
            using var s = typeof(BodyZones).Assembly.GetManifestResourceStream("Fdp.Toolkit.Tkb.Reference.bodies.json")
                ?? throw new InvalidOperationException("Body profiles: the embedded Fdp.Toolkit.Tkb.Reference.bodies.json is missing.");
            using var r = new StreamReader(s);
            return Parse(r.ReadToEnd());
        }

        /// <summary>Parses a <c>bodies.json</c> (the test seam).</summary>
        public static int ParseCount(string json) => Parse(json).Length;

        private static Profile[] Parse(string json)
        {
            var root = JsonNode.Parse(json)!;
            var list = new List<Profile>();
            foreach (var n in root["profiles"]!.AsArray())
            {
                var patterns = new List<int?[]>();
                var disNode = n!["dis"];
                var texts = disNode is JsonArray arr ? arr.Select(x => x!.GetValue<string>()).ToArray() : new[] { disNode?.GetValue<string>() ?? "" };
                foreach (var text in texts)
                {
                    var dis = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    var pattern = new int?[dis.Length];
                    for (int i = 0; i < dis.Length; i++)
                        if (dis[i] != "*") pattern[i] = int.Parse(dis[i]);
                    patterns.Add(pattern);
                }
                var zones = new List<BodyZoneDto>();
                foreach (var z in n["zones"]!.AsArray())
                {
                    var parts = new List<byte>();
                    foreach (var p in z!["parts"]?.AsArray() ?? new JsonArray()) parts.Add((byte)p!.GetValue<int>());
                    var a = z["armour"];
                    zones.Add(new BodyZoneDto
                    {
                        Zone = z["zone"]!.GetValue<string>(),
                        Parts = parts,
                        ArmourFront = F(a?["front"]), ArmourSide = F(a?["side"]), ArmourRear = F(a?["rear"]), ArmourTop = F(a?["top"]),
                        Multiplier = F(z["multiplier"]),
                        Effect = z["effect"] is { } e ? Enum.Parse<WoundEffect>(e.GetValue<string>(), ignoreCase: true) : null,
                        WoundedAt = z["woundedAt"] is { } w ? w.GetValue<int>() : null,
                    });
                }
                list.Add(new Profile(n["name"]!.GetValue<string>(), Enum.Parse<BodyPlan>(n["plan"]!.GetValue<string>(), ignoreCase: true),
                                     patterns.ToArray(), zones, n["kit"]?.GetValue<string>(),
                                     n["engine"] is { } en ? Enum.Parse<EngineEnd>(en.GetValue<string>(), ignoreCase: true) : EngineEnd.None,
                                     n["inheritArmour"]?.GetValue<bool>() ?? false));
            }
            return list.ToArray();
        }

        private static float? F(JsonNode? n) => n == null ? null : (float)n.GetValue<double>();

        private static List<BodyZoneDto> DefaultPersonZones() => new()
        {
            new() { Zone = "Head", Parts = new() { PersonPart.Head }, Multiplier = 4f },
            new() { Zone = "Torso", Parts = new() { PersonPart.Torso }, Multiplier = 1f },
            new() { Zone = "Arm", Parts = new() { PersonPart.UpperArmL, PersonPart.ForearmL, PersonPart.UpperArmR, PersonPart.ForearmR }, Multiplier = 0.4f, Effect = WoundEffect.Aim, WoundedAt = 64 },
            new() { Zone = "Leg", Parts = new() { PersonPart.ThighL, PersonPart.ShinL, PersonPart.ThighR, PersonPart.ShinR }, Multiplier = 0.5f, Effect = WoundEffect.Slow, WoundedAt = 64 },
        };
    }
}
