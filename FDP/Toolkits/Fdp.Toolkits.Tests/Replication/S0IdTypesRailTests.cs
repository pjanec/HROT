using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Fdp.Toolkits.Tests.Replication
{
    /// <summary>
    /// ⭐⭐ CE-4200 SV-2 (<c>docs/DESIGN_Semantic_Value_Types.md</c>) — an ID says what it names by its TYPE. Every member of every component a
    /// scenario SAVES (<see cref="ComponentIdAttribute"/>, not <see cref="DataPolicy.NoScenario"/>) and of every params DTO fails on a bare
    /// <c>long</c> named like a TKB template id (<c>*Tkb*</c> / <c>*TemplateId*</c> — it is a <see cref="TkbTypeId"/>), unless
    /// <see cref="NotATemplateId"/> says what it is instead. ⇒ an entity id (now 32-bit) can never be swapped for a template id, and a tool shows the
    /// template NAME. 📌 Mirrors <see cref="S0ComponentTypesRailTests"/>' scan; the node-id rails (CE-4200 SV-3) live in this class too.
    /// </summary>
    public sealed class S0IdTypesRailTests
    {
        private static IEnumerable<Assembly> Assemblies() => new[]
        {
            typeof(SimTransform).Assembly,                                       // Fdp.Core
            typeof(EntityRef).Assembly,                                          // Fdp.Toolkits
            typeof(Hrot.Map.Definitions.Behavior.ParamsDtoRegistry).Assembly,   // Hrot.Core
            typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly,                 // Hrot.AI.Behaviors
        };

        /// <summary><c>Type.Member</c> → what it is instead of a template id. ⭐ A decision each; never a silencer.</summary>
        internal static readonly Dictionary<string, string> NotATemplateId = new(StringComparer.Ordinal)
        {
            // (filled from the first scan — every entry names the reason)
        };

        private static bool Saved(Type t)
            => t.GetCustomAttribute<ComponentIdAttribute>() != null
               && (t.GetCustomAttribute<DataPolicyAttribute>()?.Policy & DataPolicy.NoScenario) != DataPolicy.NoScenario;

        private static bool ParamsDto(Type t) => t.Name.EndsWith("Params", StringComparison.Ordinal) || t.Name.EndsWith("ParamsJsonDto", StringComparison.Ordinal);

        private static IEnumerable<Type> Scanned()
            => Assemblies().SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; } })
                           .Where(t => t != null && (Saved(t!) || ParamsDto(t!))).Select(t => t!).Distinct();

        private static IEnumerable<(string Owner, string Member, Type Type)> Members(Type t)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) yield return (t.Name, f.Name, f.FieldType);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) yield return (t.Name, p.Name, p.PropertyType);
        }

        private static bool LooksLikeATemplateId(string name)
            => name.Contains("Tkb", StringComparison.OrdinalIgnoreCase) || name.Contains("TemplateId", StringComparison.OrdinalIgnoreCase);

        private static Type Strip(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            return t.IsArray ? t.GetElementType()! : t;
        }

        [Fact]
        public void S0_EveryTemplateIdMember_IsATkbTypeId()
        {
            var types = Scanned().ToList();
            Assert.True(types.Count >= 150, $"only {types.Count} component / params types found — the scan lost its assemblies");
            var offences = new List<string>();
            foreach (var t in types)
                foreach (var (owner, member, type) in Members(t))
                    if (Strip(type) == typeof(long) && LooksLikeATemplateId(member) && !NotATemplateId.ContainsKey(owner + "." + member))
                        offences.Add($"{t.FullName}.{member} ({type.Name}) — a template id is a TkbTypeId (or say what it is)");
            var sb = new StringBuilder();
            foreach (var o in offences.OrderBy(s => s, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Count} saved / params members look like a template id but are a bare long:\n{sb}");
        }

        [Fact]
        public void S0_EveryNotATemplateIdEntry_StillNamesAMember()
        {
            var live = new HashSet<string>(Scanned().SelectMany(t => Members(t).Select(m => m.Owner + "." + m.Member)));
            var stale = NotATemplateId.Keys.Where(k => !live.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "NotATemplateId entries name no member any more: " + string.Join(", ", stale));
        }

        /// <summary>The type itself — a long's memory, a plain JSON number, implicit OUT to long, explicit IN (⛔ never from an int entity id).</summary>
        [Fact]
        public void TkbTypeId_IsALongsMemory_AndAPlainJsonNumber_AndExplicitIn()
        {
            Assert.Equal(8, System.Runtime.CompilerServices.Unsafe.SizeOf<TkbTypeId>());
            Assert.Equal("101", System.Text.Json.JsonSerializer.Serialize(new TkbTypeId(101)));
            Assert.Equal(new TkbTypeId(7202), System.Text.Json.JsonSerializer.Deserialize<TkbTypeId>("7202"));
            long asLong = new TkbTypeId(42);                      // implicit out
            Assert.Equal(42L, asLong);
            Assert.Equal(new TkbTypeId(9), (TkbTypeId)9L);        // explicit in
            Assert.Equal("{\"TkbType\":3}", System.Text.Json.JsonSerializer.Serialize(new { TkbType = new TkbTypeId(3) }));
            // ⛔ no implicit conversion from long and none from int: asserted by reflection (a compile-time property, pinned so an added operator is caught)
            var ops = typeof(TkbTypeId).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "op_Implicit").ToList();
            Assert.Single(ops);
            Assert.Equal(typeof(long), ops[0].ReturnType);
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        // ⭐⭐ CE-4200 SV-3 — a NODE id says what it names by its TYPE too: <see cref="NodeId"/>, an int's memory and JSON.
        // The scan is wider than the template rail's (a node id travels in events, commands and config as well as in saved components): every
        // public type of the four assemblies except the DDS WIRE types (their IDL has no wrapper — the id is a plain int there, and the type stops
        // at the topic boundary).
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary><c>Type.Member</c> → why it is NOT a cluster node id. ⭐ A decision each; never a silencer.</summary>
        internal static readonly Dictionary<string, string> NotANodeId = new(StringComparer.Ordinal)
        {
            ["RoadSegmentJson.StartNodeId"] = "a ROAD-GRAPH node (a junction in the car-kinematics network), not a cluster node",
            ["RoadSegmentJson.EndNodeId"] = "a ROAD-GRAPH node (a junction in the car-kinematics network), not a cluster node",
            ["FollowRoadGraphParams.TargetNodeId"] = "a ROAD-GRAPH node the follower heads for, not a cluster node",
            ["NavigationIntent.TargetNodeId"] = "a NAVIGATION-GRAPH node the unit heads for, not a cluster node",
        };

        /// <summary>A DDS wire type: marked <c>[DdsTopic]</c> / <c>[DdsStruct]</c>, or the codegen's <c>*_Native</c> / <c>*View</c> twin of one.</summary>
        private static bool Wire(Type t)
        {
            static bool Marked(Type x) => x.GetCustomAttributes(false).Any(a => a.GetType().Name is "DdsTopicAttribute" or "DdsStructAttribute");
            if (Marked(t)) return true;
            foreach (var suffix in new[] { "_Native", "View" })
                if (t.Name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    var twin = t.Assembly.GetType((t.Namespace is null ? "" : t.Namespace + ".") + t.Name[..^suffix.Length]);
                    if (twin != null && Marked(twin)) return true;
                }
            return false;
        }

        private static IEnumerable<Type> AllPublicNonWire()
            => Assemblies().SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; } })
                           .Where(t => t != null && (t.IsPublic || t.IsNestedPublic) && !Wire(t!) && t!.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() == null)
                           .Select(t => t!).Distinct();

        private static bool LooksLikeANodeId(string name) => name.EndsWith("NodeId", StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void S0_EveryNodeIdMember_IsANodeId()
        {
            var types = AllPublicNonWire().ToList();
            Assert.True(types.Count >= 1000, $"only {types.Count} public types found — the scan lost its assemblies");
            var offences = new List<string>();
            foreach (var t in types)
                foreach (var (owner, member, type) in Members(t))
                {
                    var u = Strip(type);
                    if ((u == typeof(int) || u == typeof(long)) && LooksLikeANodeId(member) && !NotANodeId.ContainsKey(owner + "." + member))
                        offences.Add($"{t.FullName}.{member} ({type.Name}) — a node id is a NodeId (or say what it is)");
                }
            var sb = new StringBuilder();
            foreach (var o in offences.OrderBy(s => s, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Count} members look like a cluster node id but are a bare int / long:\n{sb}");
        }

        [Fact]
        public void S0_EveryNotANodeIdEntry_StillNamesAMember()
        {
            var live = new HashSet<string>(AllPublicNonWire().SelectMany(t => Members(t).Select(m => m.Owner + "." + m.Member)));
            var stale = NotANodeId.Keys.Where(k => !live.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "NotANodeId entries name no member any more: " + string.Join(", ", stale));
        }

        /// <summary>The type itself — an int's memory, a plain JSON number, implicit OUT to int, explicit IN.</summary>
        [Fact]
        public void NodeId_IsAnIntsMemory_AndAPlainJsonNumber_AndExplicitIn()
        {
            Assert.Equal(4, System.Runtime.CompilerServices.Unsafe.SizeOf<NodeId>());
            Assert.Equal("300", System.Text.Json.JsonSerializer.Serialize(new NodeId(300)));
            Assert.Equal(new NodeId(7), System.Text.Json.JsonSerializer.Deserialize<NodeId>("7"));
            int asInt = new NodeId(42);                           // implicit out
            Assert.Equal(42, asInt);
            Assert.Equal(new NodeId(9), (NodeId)9);               // explicit in
            Assert.Equal("{\"NodeId\":3}", System.Text.Json.JsonSerializer.Serialize(new { NodeId = new NodeId(3) }));
            var ops = typeof(NodeId).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "op_Implicit").ToList();
            Assert.Single(ops);
            Assert.Equal(typeof(int), ops[0].ReturnType);
        }
    }
}
