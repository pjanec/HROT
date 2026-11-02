using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Fdp.Toolkits.Tests.Replication
{
    /// <summary>
    /// ⭐⭐ S0 item 8 (<c>docs/DESIGN_Scenario_Rules.md</c> SR-132 ②, SR-141, SR-142, SR-143) — what CODE writes into the world says what it
    /// MEANS by its type. Every member of every component (<see cref="ComponentIdAttribute"/>) that a scenario SAVES (not
    /// <see cref="DataPolicy.NoScenario"/>) fails on a bare <see cref="Vector3"/> / <see cref="Vector2"/> (a place is a <see cref="WorldPoint"/>),
    /// a bare <see cref="Quaternion"/> (a world orientation is a <see cref="WorldRotation"/>), and a <c>float</c> / <c>double</c> named like a
    /// compass direction (a world bearing is a <see cref="Bearing"/>) — unless <see cref="NotSpatial"/> says what it is instead (a direction,
    /// a velocity, a body-relative offset, a relative angle …). ⇒ a new position cannot be added unrelocatable and invisible to tools.
    /// ⛔ <see cref="EntityLink"/> / <see cref="PointLink"/> / <see cref="AreaLink"/> never belong in a component (they would always be empty).
    /// </summary>
    public sealed class S0ComponentTypesRailTests
    {
        /// <summary>⭐ Every engine assembly this test can see — the four it names, plus every <c>Fdp.*</c> / <c>Hrot.*</c> assembly in its output
        /// folder — so a component assembly this project comes to reference is scanned by construction, not by remembering to list it.</summary>
        private static IEnumerable<Assembly> ComponentAssemblies()
        {
            var named = new[]
            {
                typeof(SimTransform).Assembly,                                       // Fdp.Core
                typeof(EntityRef).Assembly,                                          // Fdp.Toolkits
                typeof(Hrot.Map.Definitions.Behavior.ParamsDtoRegistry).Assembly,   // Hrot.Core
                typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly,                 // Hrot.AI.Behaviors
            };
            var found = System.IO.Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
                .Where(f => { var n = System.IO.Path.GetFileName(f); return (n.StartsWith("Fdp.", StringComparison.Ordinal) || n.StartsWith("Hrot.", StringComparison.Ordinal))
                                                                       && !n.Contains(".Tests", StringComparison.Ordinal); })
                .Select(f => { try { return Assembly.LoadFrom(f); } catch (Exception) { return null; } })
                .Where(a => a != null).Select(a => a!);
            return named.Concat(found).Distinct();
        }

        /// <summary><c>Type.Member</c> → what it is instead of a place / world orientation / world bearing. ⭐ A decision each; never a silencer.</summary>
        internal static readonly Dictionary<string, string> NotSpatial = new(StringComparer.Ordinal)
        {
            // ── velocities and rates (a direction of travel, never a place) ──
            ["SimVelocity.Linear"] = "linear velocity, m/s",
            ["SimVelocity.Angular"] = "angular velocity, rad/s about the body axes",
            ["Velocity.Value"] = "the geographic module's velocity, m/s",
            ["NetworkVelocity.Value"] = "the wire velocity, m/s",
            ["NetworkTransform.LastVelocity"] = "the last wire velocity, m/s",
            ["CrowdMotorIntent.Velocity"] = "the desired velocity the crowd agent steers by, m/s",
            ["FakeCrowdAgentState.Velocity"] = "the fake agent's velocity, m/s",
            // ── relative angle / direction ──
            ["TurretPose.Azimuth"] = "the traverse, radians counter-clockwise from the HULL's forward — relative to the hull, not a world bearing",
            ["FormationTarget.TargetHeading"] = "a desired forward direction vector (unit), not a place",
            // ── a world XY place, no 2-D point type yet ──
            ["FormationTarget.TargetPosition"] = "world XY place, no 2-D point type yet (S3 relocator) — not invented here",
        };

        /// <summary>S9' (c) — components saved through a GUID-keyed translator (DESIGN_Scenario_Entity_Keys §0a): their entity references are written as GUIDs, so an
        /// <c>EntityRef</c> / id field in them is not a network id in the file.</summary>
        internal static readonly HashSet<string> GuidKeyedTranslators = new(StringComparer.Ordinal)
        {
            "UnitSubordinate", "PassengerBuffer", "VisHierarchyNode", "TargetMemory", "IsEmbarkedTag", "PersonalRouteRef",
        };

        /// <summary>S9' (c) — an int / long member whose NAME says it holds an entity id.</summary>
        internal static bool NamesAnEntityId(string name)
            => name.EndsWith("Unit", StringComparison.Ordinal) || name.EndsWith("NetworkId", StringComparison.Ordinal)
               || (name.Contains("Entity", StringComparison.Ordinal) && name.EndsWith("Id", StringComparison.Ordinal))
               || (name.StartsWith("Target", StringComparison.Ordinal) && name.EndsWith("Id", StringComparison.Ordinal));

        internal static string? IdOffence(Type componentType, Type t, string name)
        {
            if (GuidKeyedTranslators.Contains(componentType.Name)) return null;
            if (t == typeof(EntityRef)) return "an EntityRef in an auto-serialized saved component — the file would hold a network id (S9': GUID-keyed translator, or an EntityLink the loader resolves)";
            if ((t == typeof(int) || t == typeof(long)) && NamesAnEntityId(name)) return "an int/long named like an entity id in an auto-serialized saved component (S9')";
            return null;
        }

        private static bool Saved(Type t)
            => t.GetCustomAttribute<ComponentIdAttribute>() != null
               && (t.GetCustomAttribute<DataPolicyAttribute>()?.Policy & DataPolicy.NoScenario) != DataPolicy.NoScenario;

        private static IEnumerable<Type> SavedComponents()
            => ComponentAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; } })
                                    .Where(t => t != null && Saved(t!)).Select(t => t!).Distinct();

        private static string? Offence(Type t, string name)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t == typeof(Vector3) || t == typeof(Vector2)) return $"a bare {t.Name} — a place is a WorldPoint (or say what it is)";
            if (t == typeof(Quaternion)) return "a bare Quaternion — a world orientation is a WorldRotation (or say it is body-local)";
            if (t == typeof(EntityLink) || t == typeof(PointLink) || t == typeof(AreaLink)) return "an AUTHORED type in a component (SR-140)";
            bool real = t == typeof(float) || t == typeof(double);
            if (real && (name.Contains("Bearing", StringComparison.OrdinalIgnoreCase) || name.Contains("Heading", StringComparison.OrdinalIgnoreCase)
                      || name.Contains("Azimuth", StringComparison.OrdinalIgnoreCase) || name.Contains("Yaw", StringComparison.OrdinalIgnoreCase)))
                return "a compass direction as a number — a world bearing is a Bearing (or say it is relative)";
            return null;
        }

        /// <summary>A struct of the engine's own (not a primitive, an enum, a BCL / numerics type or one of the S0 types) — its members are the
        /// component's members too, so the scan walks into it (<c>WeaponPosture.Point</c> sits inside a saved component).</summary>
        private static bool Nested(Type t)
            => t.IsValueType && !t.IsPrimitive && !t.IsEnum && Offence(t, "") == null
               && t.Assembly.GetName().Name is { } a && (a.StartsWith("Fdp.", StringComparison.Ordinal) || a.StartsWith("Hrot.", StringComparison.Ordinal))
               && t != typeof(WorldPoint) && t != typeof(WorldRotation) && t != typeof(Bearing);

        /// <summary>Every public instance field of <paramref name="t"/>, nested structs walked, as (<c>Comp.Field[.Inner…]</c>, field type).</summary>
        private static IEnumerable<(string Key, Type Type)> Members(Type t, string prefix, int depth = 0)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var ft = f.FieldType.IsArray ? f.FieldType.GetElementType()! : f.FieldType;
                string key = prefix + "." + f.Name;
                yield return (key, ft);
                if (depth < 4 && Nested(ft) && ft != t)
                    foreach (var inner in Members(ft, key, depth + 1)) yield return inner;
            }
        }

        [Fact]
        public void S0_EverySavedComponentMember_SaysWhatItMeansByItsType()
        {
            var comps = SavedComponents().ToList();
            // measured 2026-11-01: 94 saved components in these four assemblies (Hrot.Traffic / Hrot.IG / Hrot.CGF components are out of this project's reach)
            Assert.True(comps.Count >= 90, $"only {comps.Count} saved components found — the scan lost its assemblies");
            var offences = new List<string>();
            foreach (var c in comps)
                foreach (var (key, t) in Members(c, c.Name))
                    if (Offence(t, key.Substring(key.LastIndexOf('.') + 1)) is { } why && !NotSpatial.ContainsKey(key))
                        offences.Add($"{c.Namespace}.{key} ({t.Name}) — {why}");
            var sb = new StringBuilder();
            foreach (var o in offences.OrderBy(s => s, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Count} saved component members do not say what they mean:\n{sb}");
        }

        /// <summary>S9' (c) — measured 2026-11-02: runtime-state members the rule flags that are NOT file references; each is a decision, never a silencer.</summary>
        internal static readonly Dictionary<string, string> NotAFileReference = new(StringComparer.Ordinal)
        {
            ["NavigationIntent.TargetNodeId"] = "a navigation-graph node id, not an entity",
            ["NavigationIntent.RouteEntityId"] = "runtime navigation state (CE-4153 S8 review: becomes an EntityRef-free GUID key or NoScenario)",
            ["EpisodeReplayTag.OriginalEntityId"] = "replay bookkeeping of a recording, not a scenario file reference",
            ["SensorEvalState.LastPublishedTopEntityId"] = "a per-frame sensor cache (CE-4153 S8 review: NoScenario)",
        };

        /// <summary>Members of the element structs of a managed component's list members (<c>RoutePlan.Waypoints[].HoldUnit</c>) — the field walk cannot see them.</summary>
        private static IEnumerable<(string Key, Type Type)> ListElementMembers(Type c)
        {
            foreach (var p in c.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var pt = p.PropertyType;
                Type? el = pt.IsArray ? pt.GetElementType()
                         : pt.IsGenericType && pt.GetGenericArguments().Length == 1 && typeof(System.Collections.IEnumerable).IsAssignableFrom(pt) ? pt.GetGenericArguments()[0] : null;
                if (el is null || !Nested(el)) continue;
                foreach (var m in Members(el, c.Name + "." + p.Name + "[]")) yield return m;
            }
        }

        private static List<string> IdOffences()
        {
            var offences = new List<string>();
            foreach (var c in SavedComponents())
                foreach (var (key, t) in Members(c, c.Name).Concat(ListElementMembers(c)))
                    if (IdOffence(c, t, key.Substring(key.LastIndexOf('.') + 1)) is { } why && !NotAFileReference.ContainsKey(key))
                        offences.Add($"{c.Namespace}.{key} ({t.Name}) — {why}");
            return offences;
        }

        [Fact]
        public void S9Prime_NoSavedComponentHoldsAnEntityIdUnlessAGuidKeyedTranslatorWritesIt()
        {
            var offences = IdOffences();
            var sb = new StringBuilder();
            foreach (var o in offences.OrderBy(x => x, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Count} saved component members hold an entity id:\n{sb}");
        }

        [Fact]
        public void S9Prime_EveryNotAFileReferenceEntry_StillNamesAMember()
        {
            var live = new HashSet<string>(SavedComponents().SelectMany(c => Members(c, c.Name).Select(m => m.Key)));
            var stale = NotAFileReference.Keys.Where(k => !live.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "NotAFileReference entries name no member any more: " + string.Join(", ", stale));
        }

        [Fact]
        public void S9Prime_TheScanReachesRoutePlansHoldUnit_AsAnAuthoredLink()
        {
            var plan = SavedComponents().SingleOrDefault(t => t.Name == "RoutePlan");
            Assert.NotNull(plan);   // else the scan lost the component and the rule passes on nothing
            var hold = ListElementMembers(plan!).Single(m => m.Key == "RoutePlan.Waypoints[].HoldUnit");
            Assert.Equal(typeof(EntityLink), hold.Type);                 // reverting it to an int/long makes IdOffence flag it (next test)
            Assert.Null(IdOffence(plan!, hold.Type, "HoldUnit"));
        }

        [Fact]
        public void S9Prime_TheIdRule_SeesEachShape()
        {
            Assert.NotNull(IdOffence(typeof(object), typeof(EntityRef), "Anything"));
            Assert.NotNull(IdOffence(typeof(object), typeof(int), "HoldUnit"));
            Assert.NotNull(IdOffence(typeof(object), typeof(long), "TargetId"));
            Assert.Null(IdOffence(typeof(object), typeof(int), "Count"));
            Assert.Null(IdOffence(typeof(Fdp.Toolkit.Replication.Components.PartMetadata), typeof(float), "Unit"));
        }

        [Fact]
        public void S0_EveryNotSpatialEntry_StillNamesAComponentMember()
        {
            var live = new HashSet<string>(SavedComponents().SelectMany(c => Members(c, c.Name).Select(m => m.Key)));
            var stale = NotSpatial.Keys.Where(k => !live.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "NotSpatial entries name no member any more: " + string.Join(", ", stale));
        }
    }
}
