using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Tkb.Reference;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ Tuning T-1 — <c>GET /tkb/resolve?ammo=&amp;weapon=</c>: what the generated reference library says about a launcher × ammo
    /// pair, with the formula and inputs behind each number (📄 docs/DESIGN_Terrain_Combat_Tuning.md §2a, §4). No ammo ⇒ the whole
    /// library (names, generic profiles, weapons). It reads the same <see cref="ReferenceLibrary.Shared"/> the resolver falls back to.
    /// </summary>
    internal static class ReferenceReport
    {
        public static JsonNode Resolve(string? ammo, string? weapon)
        {
            var lib = ReferenceLibrary.Shared;
            if (string.IsNullOrWhiteSpace(ammo))
            {
                var arr = new JsonArray();
                foreach (var a in lib.Ammo.OrderBy(a => a.Name, System.StringComparer.Ordinal)) arr.Add(Ammo(a));
                var weapons = new JsonArray();
                foreach (var w in lib.Weapons.OrderBy(w => w.Name, System.StringComparer.Ordinal))
                    weapons.Add(new JsonObject { ["name"] = w.Name, ["velocityFactor"] = w.VelocityFactor, ["ammo"] = new JsonArray(w.Ammo.Select(x => (JsonNode)x!).ToArray()) });
                return new JsonObject { ["source"] = "reference library (generated)", ["ammo"] = arr, ["weapons"] = weapons };
            }
            if (!lib.TryGetPair(ammo, weapon, out var pair))
                return new JsonObject { ["ammo"] = ammo, ["found"] = false, ["known"] = new JsonArray(lib.Ammo.Select(a => (JsonNode)a.Name!).ToArray()) };
            lib.TryGetAmmo(ammo, out var refAmmo);
            return new JsonObject
            {
                ["ammo"] = pair.Ammo, ["weapon"] = pair.Weapon, ["found"] = true,
                ["weaponMatched"] = weapon == null || pair.Weapon == weapon,
                ["provenance"] = "ReferenceByName",
                ["muzzleSpeed"] = pair.MuzzleSpeed,
                ["penetrationMm"] = pair.PenetrationMm, ["penetrationFormula"] = pair.PenetrationFormula,
                ["damage"] = pair.Damage, ["damageFormula"] = pair.DamageFormula,
                ["driving"] = Ammo(refAmmo),
            };
        }

        private static JsonObject Ammo(RefAmmo a) => new()
        {
            ["name"] = a.Name, ["type"] = a.Type, ["calibreMm"] = a.CalibreMm, ["massG"] = a.MassG, ["velocity"] = a.Velocity,
            ["explosiveKg"] = a.ExplosiveKg, ["genericPenetrationMm"] = a.Generic.PenetrationMm, ["genericDamage"] = a.Generic.Damage,
            ["blastLethalRadiusM"] = a.BlastLethalRadiusM, ["blastInjuryRadiusM"] = a.BlastInjuryRadiusM,
            ["warhead"] = a.Warhead is { } w ? new JsonObject       // ⭐ CE-1032 — what the area effect uses (ParameterResolver.Warhead)
            {
                ["kind"] = w.Dto.Kind.ToString(), ["fuze"] = w.Dto.Fuze.ToString(), ["fuzeDelayS"] = w.Dto.FuzeDelayS, ["indirect"] = w.Dto.Indirect,
                ["blastLethalDamage"] = w.Dto.BlastLethalDamage, ["blastFormula"] = w.BlastFormula,
                ["fragmentRadiusM"] = w.Dto.FragmentRadiusM, ["fragmentPenetrationMm"] = w.Dto.FragmentPenetrationMm,
                ["fragmentDamage"] = w.Dto.FragmentDamage, ["fragmentFormula"] = w.FragmentFormula,
            } : null,
        };
    }
}
