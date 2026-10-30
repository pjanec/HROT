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
    /// definition, printed canonically (<see cref="ShippedTreeDump"/>: definition facts, the variables manifest (CE-1412: lines added from the
    /// JSON registrars' manifests, values equal to the JSON defaults), the parse of four probes, every node's kind / id /
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

        /// <summary>
        /// ⭐⭐ <c>CE-1412</c> — each tree publishes the variables manifest its JSON asset's registrar did: the same variables, in order, of
        /// the same TYPES — recorded from the generated registrars at <c>f72d799c3^</c> (the last commit with the JSON assets,
        /// <c>Snapshots/Golden/Generated/BTree/{name}.Registrar.g.cs.txt</c>) — and the three trees that published none (CE-1431: now an EMPTY manifest, not a missing one) still declare no variable.
        /// The values (the JSON <c>DefaultValueJson</c> / <c>ParamsDefaultJson</c>, measured equal 2026-10-27) are pinned by the snapshot's
        /// <c>variable</c> lines above. ⚠ A NAME is the C# field's: equal to the JSON's up to its first letter (<c>underFire</c> → <c>UnderFire</c>);
        /// the reserved <c>Params</c> is identical.
        /// <para>✅ Red-proof: <c>SharedNodeBinder.VariablesOf</c> keeps working-state fields ⇒ TakeCover "Expected 1, Actual 2".</para>
        /// </summary>
        [Theory]
        [MemberData(nameof(Trees))]
        public void TheCSharpTree_PublishesTheVariablesItsJsonAssetDid(string name)
        {
            Assert.True(Registry.Value.TryGetId(name, out int id));
            Assert.True(Registry.Value.TryGetDefinition(id, out var def));
            var json = JsonManifests[name];
            if (json == null) { Assert.NotNull(def!.ManagedBlackboardVariables); Assert.Empty(def.ManagedBlackboardVariables!); return; }   // CE-1431: published, and empty
            var actual = def!.ManagedBlackboardVariables;
            Assert.NotNull(actual);
            Assert.Equal(json.Length, actual!.Count);
            for (int i = 0; i < json.Length; i++)
            {
                Assert.Equal(json[i].Name, actual[i].Name, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(json[i].Type, actual[i].Type);
            }
        }

        private static readonly Dictionary<string, (string Name, Type Type)[]?> JsonManifests = new()
        {
            ["BasicInfantrySop"] = new[] { ("airSense", typeof(SopSenseParams)), ("hitSense", typeof(SopSenseParams)), ("threatSense", typeof(SopSenseParams)),
                                           ("holdFire", typeof(SopRoeParams)), ("airDefence", typeof(Hrot.AI.Behaviors.Brains.EngageAircraftParams)),
                                           ("nearMissSense", typeof(SopSenseParams)) },
            ["BasicSquadSop"] = new[] { ("underFire", typeof(SopSenseParams)), ("leaderDown", typeof(SopSenseParams)), ("contact", typeof(SopSenseParams)),
                                        ("memberDown", typeof(SopSenseParams)), ("areaAhead", typeof(SopAreaParams)) },
            ["BasicPlatoonSop"] = new[] { ("reportReceived", typeof(SopSenseParams)), ("enemyNear", typeof(Fdp.Toolkit.Squad.Command.ElementReportedParams)) },
            ["BasicVehicleSop"] = new[] { ("airSense", typeof(SopSenseParams)), ("hitSense", typeof(SopSenseParams)), ("threatSense", typeof(SopSenseParams)),
                                          ("airDefence", typeof(Hrot.AI.Behaviors.Brains.EngageAircraftParams)) },
            ["CivilianSop"] = null,
            ["InertSop"] = null,
            ["PilotSop"] = null,
            ["TakeCover"] = new[] { ("Params", typeof(Hrot.AI.Behaviors.Brains.EqsTacticsParams)) },
            ["FallBack"] = new[] { ("Params", typeof(Hrot.AI.Behaviors.Brains.EqsTacticsParams)) },
            ["Flank"] = new[] { ("tactics", typeof(Hrot.AI.Behaviors.Brains.EqsTacticsParams)), ("engage", typeof(Hrot.AI.Behaviors.Brains.EngageParams)) },
            ["FiringPosition"] = new[] { ("tactics", typeof(Hrot.AI.Behaviors.Brains.EqsTacticsParams)), ("engage", typeof(Hrot.AI.Behaviors.Brains.EngageParams)) },
            ["Sentry"] = new[] { ("Params", typeof(ThreatCountParams)) },
            ["DangerCrossing"] = new[] { ("Params", typeof(Hrot.AI.Behaviors.Brains.DangerCrossingConfig)) },
            ["HullDownAttackRun"] = new[] { ("Params", typeof(Hrot.AI.Behaviors.Brains.HullDownRunConfig)) },
            ["PlatoonHillAttack"] = new[] { ("Params", typeof(Hrot.AI.Behaviors.Brains.PlatoonHillAttackParams)) },
        };

        /// <summary>⭐ And no JSON asset of the same name is left to win the editor's AssetId collision (AiAssetCatalogBuilder: JSON wins).</summary>
        [Fact]
        public void NoShippedTree_IsStillAJsonAsset()
        {
            var root = Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!, Fdp.Toolkit.Orchestration.ContentLayout.BTreesFolder);
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
