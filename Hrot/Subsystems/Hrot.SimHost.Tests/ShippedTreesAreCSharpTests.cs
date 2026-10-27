using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Fbt;
using Fdp.Toolkit.Behavior;
using Hrot.CGF.Configuration;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-1410</c> (R-1042) — the shipped library trees are C# (<c>BTreeBuilder</c>) and BEHAVE AS THEIR JSON ASSETS DID. Each PRODUCTION
    /// definition, printed canonically (<see cref="ShippedTreeDump"/>: definition facts, the parse of four probes, every node's kind / id /
    /// title / comment / bound method / param type and value / state type), equals <c>Snapshots/ShippedTrees/{name}.tree.txt</c> — recorded
    /// from the JSON registration while both were registered side by side (the C# tree under <c>{name}_Cs</c>; all 15 equal, 2026-10-27).
    /// 🔴 Red-proof (measured): <c>BasicSquadSop.Blackboard.UnderFire.Seconds</c> 3 → 4 ⇒ BasicSquadSop fails, the other 14 pass.
    /// </summary>
    public sealed class ShippedTreesAreCSharpTests
    {
        public static IEnumerable<object[]> Trees() => ShippedTrees.All.Select(t => new object[] { t.Name });

        private static readonly Lazy<BehaviorRegistry> Registry = new(() =>
        {
            var r = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(r);
            return r;
        });

        [Theory]
        [MemberData(nameof(Trees))]
        public void TheCSharpTree_IsTheTreeItsJsonAssetWas(string name)
        {
            var t = ShippedTrees.All.Single(x => x.Name == name);
            string expected = File.ReadAllText(ShippedTreeDump.SnapshotPath(name)).Replace("\r\n", "\n");
            string actual = ShippedTreeDump.Of(Registry.Value, name, CSharpAssetId(name), t.Probes);
            Assert.Equal(expected, actual);
        }

        /// <summary>⭐ And no JSON asset of the same name is left to win the editor's AssetId collision (AiAssetCatalogBuilder: JSON wins).</summary>
        [Fact]
        public void NoShippedTree_IsStillAJsonAsset()
        {
            var root = Path.Combine(ShippedTreeDump.RepoRoot(), "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "BTrees");
            var left = Directory.GetFiles(root, "*.btree.json", SearchOption.AllDirectories)
                .Select(p => Path.GetFileName(p).Replace(".btree.json", ""))
                .Intersect(ShippedTrees.All.Select(t => t.Name)).ToList();
            Assert.Empty(left);
        }

        internal static string CSharpAssetId(string treeName)
            => typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly.GetTypes()
                .SelectMany(ty => ty.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Select(m => m.GetCustomAttribute<BTreeDefinitionAttribute>())
                .Where(a => a != null && a.TreeName == treeName && a.AssetId != null)
                .Select(a => a!.AssetId!).Single();
    }

    /// <summary>⭐ <c>CE-1410</c> — the converted trees and the per-tree parse probe (their contract's own fields).</summary>
    internal static class ShippedTrees
    {
        public sealed record Tree(string Name, string[] Probes);

        public static readonly Tree[] All =
        {
            new("BasicInfantrySop", Array.Empty<string>()),
            new("BasicSquadSop", Array.Empty<string>()),
            new("BasicPlatoonSop", Array.Empty<string>()),
            new("BasicVehicleSop", Array.Empty<string>()),
            new("CivilianSop", Array.Empty<string>()),
            new("InertSop", Array.Empty<string>()),
            new("PilotSop", Array.Empty<string>()),
            new("TakeCover", new[] { "{\"Speed\":7,\"SearchRadius\":33}" }),
            new("FallBack", new[] { "{\"Speed\":7,\"MinRepositionMetres\":2}" }),
            new("Flank", Array.Empty<string>()),
            new("FiringPosition", Array.Empty<string>()),
            new("Sentry", new[] { "{\"WithinMetres\":33,\"Count\":2}" }),
            new("DangerCrossing", new[] { "{\"Objective\":[10,20,0],\"WalkSpeed\":2,\"RushSpeed\":5,\"CrossWithin\":9}" }),
            new("HullDownAttackRun",
                new[] { "{\"BaselineX\":523,\"BaselineY\":401,\"SlotX\":530,\"SlotY\":450,\"AttackDirX\":0,\"AttackDirY\":2,\"ApproachSpeed\":15,\"CreepSpeed\":5}" }),
            new("PlatoonHillAttack", Array.Empty<string>()),
        };
    }
}
