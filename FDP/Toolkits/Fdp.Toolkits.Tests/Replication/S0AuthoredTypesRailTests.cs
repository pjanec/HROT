using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ReplayBrowser.Search;
using Xunit;

namespace Fdp.Toolkits.Tests.Replication
{
    /// <summary>
    /// ⭐⭐ S0 item 8 (<c>docs/DESIGN_Scenario_Rules.md</c> SR-132 / SR-140 / SR-143) — what a PERSON authors uses the reference types BY
    /// CONSTRUCTION. Every member (nested types and lists included) of every <see cref="ParamsDtoAttribute"/> type in the shipped assemblies
    /// and of every <see cref="SearchPredicateDto"/> fails on: a bare <see cref="Vector2"/> / <see cref="Vector3"/> (→ <see cref="PointLink"/>),
    /// a latitude / longitude number (no geodetic form, SR-135), an internal <see cref="EntityRef"/> or a <c>*NetworkId</c> number
    /// (→ <see cref="EntityLink"/>), and a <c>float</c> / <c>double</c> named like a compass direction (→ <see cref="Bearing"/>) — unless
    /// <see cref="Allowed"/> says why it is not. ⛔ One rail per concept, not a generic sweep (CLAUDE.md).
    /// </summary>
    public sealed class S0AuthoredTypesRailTests
    {
        /// <summary>The assemblies that ship params DTOs (measured: Fdp.Toolkits 1 file, Hrot.Core 46, Hrot.AI.Behaviors 5).</summary>
        private static IEnumerable<Assembly> ParamsAssemblies() => new[]
        {
            typeof(EntityRef).Assembly,                                    // Fdp.Toolkits
            typeof(Hrot.Map.Definitions.Behavior.ParamsDtoRegistry).Assembly,   // Hrot.Core
            typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly,             // Hrot.AI.Behaviors
        };

        /// <summary>
        /// Members that LOOK like a place, a reference or a bearing and are not — each with its reason. Keyed <c>Type.Member</c>
        /// (the declaring type's name). ⭐ An entry is a decision; ⛔ never add one to make the rail pass without saying why.
        /// </summary>
        internal static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
        {
            ["T09Params.HomePosition"] = "test-only fixture (T09_BlackboardManaged): its byte offsets 8/4 are pinned by AGeneratedBehaviourAdvertisesItsManifestTests, and no scenario authors it",
            ["T41Params.Home"] = "test-only fixture generated from assets/behaviors/Params/Tests/T41Params.params.json (ParamsDtoAssetTests pins the Vector3 default [10,20,0]); no scenario authors it",
        };

        private static bool Authoring(Type t)
            => t.IsDefined(typeof(ParamsDtoAttribute), inherit: false) || typeof(SearchPredicateDto).IsAssignableFrom(t);

        private static IEnumerable<Type> AuthoredRoots()
            => ParamsAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; } })
                                 .Where(t => t != null && !t.IsAbstract && !t.IsGenericTypeDefinition && Authoring(t!))
                                 .Select(t => t!)
                                 .Distinct();

        private static Type? Element(Type t)
            => t.IsArray ? t.GetElementType() : t.IsGenericType && typeof(IList).IsAssignableFrom(t) ? t.GetGenericArguments()[0] : null;

        private static bool Walkable(Type t)
            => !t.IsPrimitive && !t.IsEnum && t != typeof(string) && t != typeof(decimal) && t != typeof(Guid)
               && !(t.Namespace?.StartsWith("System", StringComparison.Ordinal) ?? false)
               && t != typeof(EntityLink) && t != typeof(PointLink) && t != typeof(AreaLink) && t != typeof(EntityRef)
               && t != typeof(WorldPoint) && t != typeof(WorldRotation) && t != typeof(Bearing) && t != typeof(RefSpec);

        private static string? Offence(Type memberType, string name)
        {
            var t = Nullable.GetUnderlyingType(memberType) ?? memberType;
            if (t == typeof(Vector3) || t == typeof(Vector2)) return $"a bare {t.Name} — a place is a PointLink (an area an AreaLink)";
            if (t.Name == "PickableGeoPoint") return "a geodetic point — a place is a PointLink in local metres (SR-135)";
            if (t == typeof(EntityRef)) return "an internal EntityRef — an authored reference is an EntityLink (SR-140)";
            bool number = t == typeof(long) || t == typeof(int) || t == typeof(ulong) || t == typeof(uint);
            if (number && name.EndsWith("NetworkId", StringComparison.OrdinalIgnoreCase)) return "a raw network id — an authored reference is an EntityLink";
            bool real = t == typeof(double) || t == typeof(float);
            if (real && (name.Contains("Lat", StringComparison.Ordinal) || name.Contains("Lon", StringComparison.Ordinal)))
                return "a latitude / longitude — the engine is local metres (SR-135)";
            if (real && (name.Contains("Bearing", StringComparison.OrdinalIgnoreCase) || name.Contains("Heading", StringComparison.OrdinalIgnoreCase)
                      || name.Contains("Azimuth", StringComparison.OrdinalIgnoreCase) || name.Contains("Yaw", StringComparison.OrdinalIgnoreCase)))
                return "a compass direction as a number — a world bearing is a Bearing (SR-143)";
            return null;
        }

        private static void Walk(Type type, string path, HashSet<Type> seen, List<string> into)
        {
            if (!seen.Add(type)) return;
            const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var members = t.GetFields(Public).Select(f => (f.Name, f.FieldType, Decl: f.DeclaringType!))
                              .Concat(t.GetProperties(Public).Where(p => p.GetIndexParameters().Length == 0)
                                                             .Select(p => (p.Name, p.PropertyType, Decl: p.DeclaringType!)));
                foreach (var (name, mt, decl) in members)
                {
                    string key = decl.Name + "." + name;
                    var leafType = Element(mt) ?? mt;
                    if (Offence(leafType, name) is { } why && !Allowed.ContainsKey(key))
                        into.Add($"{decl.FullName}.{name} ({mt.Name}) — {why}");
                    if (Walkable(leafType) && !leafType.IsDefined(typeof(ParamsDtoAttribute), false))
                        Walk(leafType, path + "." + name, seen, into);
                }
            }
        }

        [Fact]
        public void S0_EveryAuthoredParamsAndPredicateMember_UsesTheReferenceTypes()
        {
            var roots = AuthoredRoots().ToList();
            Assert.True(roots.Count >= 40, $"only {roots.Count} authored types found — the scan lost its assemblies");
            var offences = new List<string>();
            var seen = new HashSet<Type>();
            foreach (var r in roots) Walk(r, r.Name, seen, offences);
            var sb = new StringBuilder();
            foreach (var o in offences.Distinct().OrderBy(s => s, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Distinct().Count()} authored members do not use the reference types (or need an Allowed entry WITH a reason):\n{sb}");
        }

        [Fact]
        public void S0_EveryAllowedEntry_StillNamesAMember()
        {
            var live = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<Type>();
            void Collect(Type type)
            {
                if (!seen.Add(type)) return;
                for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                    foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        live.Add(t.Name + "." + m.Name);
                        var mt = m is FieldInfo f ? f.FieldType : m is PropertyInfo p ? p.PropertyType : null;
                        if (mt != null && Walkable(Element(mt) ?? mt)) Collect(Element(mt) ?? mt);
                    }
            }
            foreach (var r in AuthoredRoots()) Collect(r);
            var stale = Allowed.Keys.Where(k => !live.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "allow-list entries name no member any more: " + string.Join(", ", stale));
        }
    }
}
