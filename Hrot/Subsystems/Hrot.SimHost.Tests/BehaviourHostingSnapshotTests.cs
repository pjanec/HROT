using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Fbt;
using Fdp.Toolkit.Behavior;
using Hrot.CGF.Configuration;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-1431</c> — ONE hosting rule for every behaviour tree, whatever surface authored it. For EVERY behaviour the real registry
    /// holds (<c>CgfBehaviorSetup.LoadFromAiAssembly</c>, the registry <c>ShippedTreesAreCSharpTests</c> builds) one line
    /// <c>name | hostedInput=&lt;FullName or -&gt; | inputBytes=&lt;RootParamsAccess.InputBytes&gt;</c> is pinned to
    /// <c>Snapshots/BehaviourHosting/hosting.txt</c>. Recorded at the code BEFORE CE-1431 (part A); any line that moves is named and argued.
    /// <para>Set <c>HROT_RECORD_HOSTING=1</c> to (re)write the golden instead of comparing; <c>HROT_BEHAVIOUR_TABLE=&lt;file&gt;</c> writes the
    /// informational per-behaviour table (not pinned).</para>
    /// </summary>
    public sealed class BehaviourHostingSnapshotTests
    {
        private static readonly Lazy<BehaviorRegistry> Registry = new(() =>
        {
            var r = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(r);
            return r;
        });

        private static string GoldenPath()
            => Path.Combine(ShippedTreeDump.RepoRoot(), "Hrot", "Subsystems", "Hrot.SimHost.Tests", "Snapshots", "BehaviourHosting", "hosting.txt");

        private static IEnumerable<(string Name, BehaviorDefinition Def)> All()
        {
            var r = Registry.Value;
            foreach (var name in r.GetRegisteredNames().OrderBy(n => n, StringComparer.Ordinal))
            {
                Assert.True(r.TryGetId(name, out int id));
                Assert.True(r.TryGetDefinition(id, out var def));
                yield return (name, def!);
            }
        }

        private static string TypeName(Type? t) => t == null ? "-" : t.FullName ?? t.Name;

        internal static string Dump()
        {
            var sb = new StringBuilder();
            foreach (var (name, def) in All())
            {
                string hosted = Registry.Value.TryGetHostedInputType(name, out var t) ? TypeName(t) : "-";
                sb.Append(name).Append(" | hostedInput=").Append(hosted)
                  .Append(" | inputBytes=").Append(RootParamsAccess.InputBytes(def)).Append('\n');
            }
            return sb.ToString();
        }

        [Fact]
        public void CE1431_EveryBehaviourHostsAsBefore()
        {
            string actual = Dump();
            if (Environment.GetEnvironmentVariable("HROT_RECORD_HOSTING") == "1") File.WriteAllText(GoldenPath(), actual);
            string expected = File.ReadAllText(GoldenPath()).Replace("\r\n", "\n");
            Assert.Equal(expected, actual);
        }

        /// <summary>The names of every method carrying <c>[BTreeDefinition(Curated = true)]</c> / <c>[HsmDefinition(Curated = true)]</c>.</summary>
        internal static (HashSet<string> Trees, HashSet<string> Machines) CuratedNames()
        {
            var trees = new HashSet<string>(StringComparer.Ordinal);
            var machines = new HashSet<string>(StringComparer.Ordinal);
            var asms = new[] { typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly, typeof(CgfBehaviorSetup).Assembly };
            foreach (var asm in asms.Distinct())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray()!; }
                foreach (var ty in types)
                    foreach (var m in ty.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        var b = m.GetCustomAttribute<BTreeDefinitionAttribute>();
                        if (b != null && b.Curated) trees.Add(b.TreeName);
                        var h = m.GetCustomAttribute<Fhsm.Kernel.Attributes.HsmDefinitionAttribute>();
                        if (h != null && h.Curated) machines.Add(h.MachineName);
                    }
            }
            return (trees, machines);
        }

        /// <summary>Informational: one row per registered behaviour (written only when <c>HROT_BEHAVIOUR_TABLE</c> names a file).</summary>
        [Fact]
        public void CE1431_PrintBehaviourTable()
        {
            string? path = Environment.GetEnvironmentVariable("HROT_BEHAVIOUR_TABLE");
            if (string.IsNullOrEmpty(path)) return;
            var (trees, machines) = CuratedNames();
            var sb = new StringBuilder("name | tier | curated | BlackboardLayoutType | JsonParamsDtoType | manifest | HasCuratedResolver\n");
            foreach (var (name, def) in All())
            {
                string tier = def.BrainTier == BehaviorConstants.BrainTierBTree ? "BTree" : def.BrainTier == BehaviorConstants.BrainTierHsm ? "HSM" : "Blueprint(" + def.BrainTier + ")";
                bool curated = trees.Contains(name) || machines.Contains(name);
                sb.Append(name).Append(" | ").Append(tier).Append(" | ").Append(curated ? "curated" : "-")
                  .Append(" | ").Append(TypeName(def.BlackboardLayoutType)).Append(" | ").Append(TypeName(def.JsonParamsDtoType))
                  .Append(" | ").Append(def.ManagedBlackboardVariables == null ? "none" : def.ManagedBlackboardVariables.Count.ToString())
                  .Append(" | ").Append(Registry.Value.HasCuratedResolver(name) ? "yes" : "no").Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
        }
    }
}
