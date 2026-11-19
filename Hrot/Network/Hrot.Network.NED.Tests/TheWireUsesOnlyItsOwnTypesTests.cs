using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CycloneDDS.Schema;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// ⭐⭐ R-4295 — THE NETWORK LAYER STAYS SEPARATE FROM HROT INTERNALS, checked by machine. 🔒 User, `2026-11-18`: *"keep in mind that network layer
    /// needs to be separated from hrot internals - even enums are duplicated"*. Every field of every <c>[DdsTopic]</c> type — and of the wire structs it
    /// nests — is a primitive, a system value type (<see cref="Guid"/>, <see cref="DateTime"/>), or the WIRE's own type: a <c>[DdsStruct]</c> /
    /// <c>[DdsTopic]</c> / <c>[DdsUnion]</c> struct, or an enum declared beside the wire types (same assembly, same namespace). ⛔ Never an internal
    /// component, enum or struct — the translators convert (the dual-enum pattern, MOD1-DESIGN §2.5).
    ///
    /// <para>📐 Measured `2026-11-19` before this rail existed: all 154 topic structs in the repo were clean (an audit by hand) — the rail keeps it so.
    /// ⚠ It checks FIELDS: a method on a wire type that converts to an internal type (e.g. <c>SwitchTimeModeWireDto.ToEvent</c>) is not seen.</para>
    /// </summary>
    public class TheWireUsesOnlyItsOwnTypesTests
    {
        private static readonly HashSet<Type> SystemValues = new()
        {
            typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(string), typeof(Guid), typeof(DateTime),
        };

        private static bool IsDdsType(Type t)
            => t.GetCustomAttribute<DdsTopicAttribute>() != null || t.GetCustomAttribute<DdsStructAttribute>() != null
            || t.GetCustomAttribute<DdsUnionAttribute>() != null;

        /// <summary>The (assembly, namespace) pairs that hold wire types — an enum declared there is the wire's own copy.</summary>
        private static HashSet<(Assembly, string?)> WireHomes(IEnumerable<Type> topics)
        {
            var homes = new HashSet<(Assembly, string?)>();
            foreach (var asm in topics.Select(t => t.Assembly).Distinct())
            {
                Type[] all;
                try { all = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { all = e.Types.Where(x => x != null).ToArray()!; }
                foreach (var t in all) if (IsDdsType(t)) homes.Add((asm, t.Namespace));
            }
            return homes;
        }

        /// <summary>The fields of <paramref name="root"/> (and of the wire structs it nests) whose type is NOT the wire's own. Pure, so the rail is
        /// red-proved on a struct that does break the rule.</summary>
        internal static IReadOnlyList<string> Violations(Type root, HashSet<(Assembly, string?)> homes)
        {
            var bad = new List<string>();
            var seen = new HashSet<Type>();
            var todo = new Stack<Type>();
            todo.Push(root);
            while (todo.Count > 0)
            {
                var owner = todo.Pop();
                if (!seen.Add(owner)) continue;
                foreach (var f in owner.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var t = f.FieldType;
                    if (t.IsArray) t = t.GetElementType()!;
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) t = t.GetGenericArguments()[0];
                    if (Nullable.GetUnderlyingType(t) is { } u) t = u;
                    if (SystemValues.Contains(t)) continue;
                    if (f.GetCustomAttribute<System.Runtime.CompilerServices.FixedBufferAttribute>() != null) continue;   // a fixed buffer of a primitive
                    if (IsDdsType(t)) { todo.Push(t); continue; }
                    if (t.IsEnum && homes.Contains((t.Assembly, t.Namespace))) continue;
                    bad.Add($"{owner.FullName}.{f.Name} : {t.FullName} ({t.Assembly.GetName().Name})");
                }
            }
            return bad;
        }

        [Fact]
        public void EveryWireFieldIsAPrimitiveOrTheWiresOwnType()
        {
            var topics = TheTopicNamesAreDerivedFromTypesTests.TopicTypes.Value;
            Assert.True(topics.Count >= 114, $"only {topics.Count} topic types — the walk did not reach the production assemblies");
            var homes = WireHomes(topics);
            var bad = topics.SelectMany(t => Violations(t, homes)).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
            Assert.True(bad.Count == 0, "R-4295 — wire fields typed with HROT internals (convert in the translator; give the wire its own copy):\n  "
                                        + string.Join("\n  ", bad));
        }

        // ── the red proof: a struct that DOES carry an internal type is caught ─────────────────────────────────────────

        private struct LeakyWire
        {
            public int Fine;
            public Fdp.Core.Entity Leak;   // an ECS handle — HROT internals on the wire
        }

        [Fact]
        public void AStructCarryingAnInternalTypeIsCaught()
        {
            var homes = WireHomes(TheTopicNamesAreDerivedFromTypesTests.TopicTypes.Value);
            var bad = Violations(typeof(LeakyWire), homes);
            Assert.Single(bad);
            Assert.Contains("Leak", bad[0]);
        }
    }
}
