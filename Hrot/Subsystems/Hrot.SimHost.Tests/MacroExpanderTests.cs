using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Testing;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Orchestration;
using Hrot.Common.Macros;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ Macros S3a ④ (docs/DESIGN_Scenario_Macros.md §15, SR-146 / SR-155 / SR-167 / SR-169 / SR-149 / SR-156) — the expander turns ONE
    /// instance into every child request from the FILES: transient, owned by the instance's owner, GUIDs derived per instance, relocated from
    /// the authored anchor to the instance's (rotated too), bound to the instance's args, nested instances flattened with their transforms
    /// composed — and a bad file / arg / cycle / pinned move fails naming the path, with nothing to create.
    /// </summary>
    public sealed class MacroExpanderTests
    {
        private const string FireTeam = "test/fire-team";
        private static readonly NodeId Owner = (NodeId)3;

        static MacroExpanderTests()
        {
            // the staging repo registers every globally known type: make the CGF host's set known, as a running Brain has (ScenarioEntityKeySweepTests)
            using var repo = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(repo);
            Hrot.CGF.CgfComponentRegistry.RegisterAll(repo);
        }

        private static MacroExpander Expander(IMacroLibrary library)
        {
            var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(
                new Fdp.Toolkit.Behavior.BehaviorRegistry(), null, UnknownComponentPolicy.WarnAndSkip);
            return new MacroExpander(library, new StagingEntityExtractor(), serializer);
        }

        private static string FireTeamJson() => File.ReadAllText(Path.Combine(TestRepo.Root, "macros", "test", "fire-team", "scenario.json"));

        private static InMemoryMacroLibrary Library(params (string Path, string Json)[] files)
        {
            var lib = new InMemoryMacroLibrary();
            lib.Files[FireTeam] = FireTeamJson();
            foreach (var (p, j) in files) lib.Files[p] = j;
            return lib;
        }

        private static SimTransform At(float x, float y, float headingDeg)
            => new SimTransform { Position = new WorldPoint(x, y, 0), Rotation = WorldRotation.FromHeadingDeg(headingDeg) };

        private static MacroExpander.Result Expand(MacroExpander x, string macro, Guid guid, SimTransform at, string args = "{}", int instanceId = 500)
            => x.Expand(new MacroInstance { Macro = macro, ArgsJson = args }, instanceId, guid, Owner, at, outside: null, new SequentialIdAllocator());

        private static T One<T>(EntityCreationRequest r) => r.InitialComponents!.OfType<T>().Single();

        private static Dictionary<Guid, WorldPoint> AuthoredPositions()
        {
            var d = new Dictionary<Guid, WorldPoint>();
            foreach (var (k, e) in JsonNode.Parse(FireTeamJson())!["entities"]!.AsObject())
            {
                var p = e!["SimTransform"]!["Position"]!.AsArray();
                d[Guid.Parse(k)] = new WorldPoint(p[0]!.GetValue<float>(), p[1]!.GetValue<float>(), p[2]!.GetValue<float>());
            }
            return d;
        }

        [Fact]
        public void TheFireTeam_ExpandsIntoItsFourMen_TransientOwnedDerivedAndMarked()
        {
            var guid = Guid.NewGuid();
            var r = Expand(Expander(Library()), FireTeam, guid, At(100, 100, 0));

            Assert.True(r.Ok, r.Failure);
            Assert.Equal(4, r.Children.Count);
            var authored = AuthoredPositions();
            foreach (var c in r.Children)
            {
                Assert.True(c.IsTransient);                                           // SR-169
                Assert.Equal((int)Owner, c.OwnerAppInstanceId);
                Assert.NotEqual(0, c.PreAllocatedNetworkId);                          // SR-151: a fresh id each
                var origin = One<MacroOrigin>(c);
                Assert.Equal(500, origin.Instance.NetworkId);                         // SR-170
                Assert.True(authored.ContainsKey(origin.Local));
                Assert.Equal(EntityGuidDerivation.Derive(guid, "macro:" + origin.Local.ToString("D")), One<EntityGuid>(c).Value);   // SR-167
                Assert.Empty(c.InitialComponents!.OfType<ParamBindings>());           // SR-168: a transient child drops them
                Assert.Equal(authored[origin.Local], One<SimTransform>(c).Position);  // placed at its own authored anchor: unmoved
            }
        }

        [Fact]
        public void TwoInstances_GetDistinctGuids_AndOneInstanceTheSameGuidsEveryTime()
        {
            var x = Expander(Library());
            Guid a = Guid.NewGuid(), b = Guid.NewGuid();
            var ga = Expand(x, FireTeam, a, At(100, 100, 0)).Children.Select(c => One<EntityGuid>(c).Value).ToHashSet();
            var gb = Expand(x, FireTeam, b, At(100, 100, 0)).Children.Select(c => One<EntityGuid>(c).Value).ToHashSet();
            var ga2 = Expand(x, FireTeam, a, At(900, 900, 45)).Children.Select(c => One<EntityGuid>(c).Value).ToHashSet();

            Assert.Empty(ga.Intersect(gb));      // SR-187
            Assert.True(ga.SetEquals(ga2));      // stable: the same instance (another place) gives the same identities (SR-150's reconcile key)
        }

        [Fact]
        public void PlacedElsewhere_AndTurned_EveryManMovesWithTheAnchor()
        {
            var r = Expand(Expander(Library()), FireTeam, Guid.NewGuid(), At(1100, 100, 90));
            Assert.True(r.Ok, r.Failure);
            var authored = AuthoredPositions();
            foreach (var c in r.Children)
            {
                var a = authored[One<MacroOrigin>(c).Local];
                float dx = a.X - 100, dy = a.Y - 100;
                var p = One<SimTransform>(c).Position;
                // +90° (clockwise): east → south — x' = 1100 + dy, y' = 100 − dx
                Assert.Equal(1100 + dy, p.X, 3);
                Assert.Equal(100 - dx, p.Y, 3);
                // each man turned with it: authored facing east (an identity rotation is compass 90°), now south (180°)
                Assert.Equal(180f, One<SimTransform>(c).Rotation.HeadingDeg, 1);
            }
        }

        [Fact]
        public void AnArg_OverridesTheDefault_ThroughTheFilesBindings()
        {
            var x = Expander(Library());
            var dflt = Expand(x, FireTeam, Guid.NewGuid(), At(100, 100, 0));
            var friend = Expand(x, FireTeam, Guid.NewGuid(), At(100, 100, 0), """{"side": "Friend"}""");

            Assert.All(dflt.Children, c => Assert.Equal(ForceId.Hostile, One<EntityInfo>(c).ForceId));
            Assert.All(friend.Children, c => Assert.Equal(ForceId.Friend, One<EntityInfo>(c).ForceId));
        }

        private static string Wrap(string kind, string entitiesJson, bool relocatable = true) => $$"""
            { "$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
              "header": {"subsystemType": "Hrot.Scenario", "Kind": "{{kind}}", "Anchor": {"Position": [0, 0, 0], "Heading": 0}{{(relocatable ? "" : ", \"Relocatable\": false")}}},
              "entities": { {{entitiesJson}} } }
            """;

        // ⚠ an identity rotation faces EAST (compass 90°): an instance authored "unturned" must say north explicitly
        private static string NorthFacing()
        {
            var q = (System.Numerics.Quaternion)WorldRotation.FromHeadingDeg(0);
            return FormattableString.Invariant($"[{q.X:R}, {q.Y:R}, {q.Z:R}, {q.W:R}]");
        }

        private static string InstanceEntity(Guid key, string macro, float x, float y) => $$$"""
            "{{{key:D}}}": { "SimTransform": {"Position": [{{{x}}}, {{{y}}}, 0], "Rotation": {{{NorthFacing()}}}},
                           "TkbIdentity": {"TkbType": 8813},
                           "MacroInstance": {"Macro": "{{{macro}}}", "Args": {}} }
            """;

        [Fact]
        public void ANestedInstance_IsFlattened_ItsMenUnderIt_TheTransformsComposed()
        {
            var nestedKey = Guid.NewGuid();
            var outer = Wrap("macro", InstanceEntity(nestedKey, FireTeam, 200, 0));
            var r = Expand(Expander(Library(("test/outer", outer))), "test/outer", Guid.NewGuid(), At(1000, 1000, 0));

            Assert.True(r.Ok, r.Failure);
            Assert.Equal(5, r.Children.Count);
            var nested = r.Children.Single(c => c.InitialComponents!.OfType<MacroInstance>().Any());
            Assert.Equal(MacroExpansionState.Done, One<MacroExpansionStatus>(nested).State);      // E-2: never expanded again
            Assert.Equal(new WorldPoint(1200, 1000, 0), One<SimTransform>(nested).Position);    // moved by the outer anchor

            var authored = AuthoredPositions();
            foreach (var man in r.Children.Where(c => c != nested))
            {
                Assert.Equal(nested.PreAllocatedNetworkId, One<MacroOrigin>(man).Instance.NetworkId);   // under the NESTED instance
                var a = authored[One<MacroOrigin>(man).Local];
                // the fire team's anchor (100,100) → the nested instance's place (1200,1000): SR-155, the transforms compose
                Assert.Equal(new WorldPoint(1200 + (a.X - 100), 1000 + (a.Y - 100), 0), One<SimTransform>(man).Position);
            }
        }

        [Fact]
        public void AMissingMacro_AnUnknownArg_ACycle_AndAPinnedMove_EachFailNamingIt_AndCreateNothing()
        {
            var selfKey = Guid.NewGuid();
            var cyclic = Wrap("macro", InstanceEntity(selfKey, "test/cyclic", 10, 0));
            var pinned = Wrap("macro", "", relocatable: false);
            var x = Expander(Library(("test/cyclic", cyclic), ("test/pinned", pinned), ("test/not-a-macro", Wrap("scenario", ""))));

            void Fails(MacroExpander.Result r, string expected)
            {
                Assert.False(r.Ok);
                Assert.Empty(r.Children);
                Assert.Contains(expected, r.Failure);
            }

            Fails(Expand(x, "test/nowhere", Guid.NewGuid(), At(0, 0, 0)), "'test/nowhere' not found");
            Fails(Expand(x, FireTeam, Guid.NewGuid(), At(100, 100, 0), """{"colour": "red"}"""), "arg 'colour' is not a param");
            Fails(Expand(x, "test/cyclic", Guid.NewGuid(), At(0, 0, 0)), "macro cycle");
            Fails(Expand(x, "test/pinned", Guid.NewGuid(), At(50, 0, 0)), "is pinned at its anchor");
            Fails(Expand(x, "test/not-a-macro", Guid.NewGuid(), At(0, 0, 0)), "not a macro");
            Assert.True(Expand(x, "test/pinned", Guid.NewGuid(), At(0, 0, 0)).Ok);   // a pinned macro AT its anchor expands
        }
    }
}
