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
        private static IEnumerable<Assembly> ComponentAssemblies() => new[]
        {
            typeof(SimTransform).Assembly,                                       // Fdp.Core
            typeof(EntityRef).Assembly,                                          // Fdp.Toolkits
            typeof(Hrot.Map.Definitions.Behavior.ParamsDtoRegistry).Assembly,   // Hrot.Core
            typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly,                 // Hrot.AI.Behaviors
        };

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

        [Fact]
        public void S0_EverySavedComponentMember_SaysWhatItMeansByItsType()
        {
            var comps = SavedComponents().ToList();
            // measured 2026-11-01: 94 saved components in these four assemblies (Hrot.Traffic / Hrot.IG / Hrot.CGF components are out of this project's reach)
            Assert.True(comps.Count >= 90, $"only {comps.Count} saved components found — the scan lost its assemblies");
            var offences = new List<string>();
            foreach (var c in comps)
                foreach (var f in c.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var t = f.FieldType.IsArray ? f.FieldType.GetElementType()! : f.FieldType;
                    if (Offence(t, f.Name) is { } why && !NotSpatial.ContainsKey(c.Name + "." + f.Name))
                        offences.Add($"{c.FullName}.{f.Name} ({f.FieldType.Name}) — {why}");
                }
            var sb = new StringBuilder();
            foreach (var o in offences.OrderBy(s => s, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Count} saved component members do not say what they mean:\n{sb}");
        }

        [Fact]
        public void S0_EveryNotSpatialEntry_StillNamesAComponentMember()
        {
            var live = new HashSet<string>(SavedComponents().SelectMany(c => c.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => c.Name + "." + f.Name)));
            var stale = NotSpatial.Keys.Where(k => !live.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "NotSpatial entries name no member any more: " + string.Join(", ", stale));
        }
    }
}
