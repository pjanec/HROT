using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Testing;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
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
            /// <summary>⭐ Macros debt A1 (SR-65, SR-188) — an <c>@Key</c> resolves against the LIVE world the production registry builds (it registered
        /// <c>RefKey</c> only on the staging repo, so a runtime instance's <c>"@Bridge"</c> arg never resolved); a key two entities carry resolves to
        /// the first and is reported.</summary>
        [Fact]
        public void A1_AnAtKey_ResolvesAgainstTheLiveWorld_TheFirstOfADuplicateWins()
        {
            using var repo = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(repo);
            Assert.True(repo.IsComponentTypeRegistered<RefKey>());   // 🔴 the defect: false before A1
            var map = new NetworkEntityMap();
            Entity Keyed(int id, string key, float x)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new RefKey(key));
                repo.AddComponent(e, At(x, 0, 0));
                repo.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = id });   // the live scope reads ids off the entity
                map.Register(id, e);
                return e;
            }
            Keyed(900, "Bridge", 10);
            Keyed(901, "Ford", 20);
            Keyed(902, "Ford", 30);

            var scope = Fdp.Toolkit.Scenario.LiveRefScope.Of(repo);   // ⭐ the ONE live-world scope
            Assert.True(scope.TryResolve(RefSpec.ByKey("Bridge"), out var bridge));
            Assert.Equal(900, bridge.NetworkId);
            Assert.True(scope.TryPositionOf(bridge, out var at));
            Assert.Equal(10f, at.X);
            Assert.True(scope.TryResolve(RefSpec.ByKey("Ford"), out var ford));
            Assert.Equal(901, ford.NetworkId);
            Assert.True(scope.Ambiguous.ContainsKey("Ford"));
            Assert.False(scope.TryResolve(RefSpec.ByKey("Nowhere"), out _));
        }

        // ════════ ⭐ ⑤ placement, driven through MacroExpansionSystem (§15.2: ask once, park, apply; any Failed ⇒ nothing; no answer ⇒ E-6) ════════

        private static void Ensure<T>(EntityRepository repo) where T : class
        {
            if (!repo.Bus.IsRegisteredManaged<T>()) repo.RegisterManagedEvent<T>();
        }

        private static (EntityRepository Repo, MacroExpansionSystem System, Entity Instance) ExpandingWorld()
        {
            var (repo, sys, inst, _) = ExpandingWorldWithMap();
            return (repo, sys, inst);
        }

        private static (EntityRepository Repo, MacroExpansionSystem System, Entity Instance, NetworkEntityMap Map) ExpandingWorldWithMap(
            string macro = FireTeam, IMacroLibrary? library = null)
        {
            var repo = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(repo);
            Hrot.CGF.CgfComponentRegistry.RegisterAll(repo);
            Ensure<EntityCreationRequest>(repo);
            Ensure<Fdp.Toolkit.Navigation.PlacementRequestBatch>(repo);
            Ensure<Fdp.Toolkit.Navigation.PlacementResponseBatch>(repo);
            var map = new NetworkEntityMap();
            var inst = repo.CreateEntity();
            repo.AddComponent(inst, At(100, 100, 0));
            repo.SetManagedComponent(inst, new MacroInstance { Macro = macro });
            repo.AddComponent(inst, new MacroExpansionStatus { State = MacroExpansionState.Expanding });
            repo.SetManagedComponent(inst, new MacroExpansionIntent());
            map.Register(500, inst);
            return (repo, new MacroExpansionSystem(Expander(library ?? Library()), map, new SequentialIdAllocator(), Owner), inst, map);
        }

        private static T[] Frame<T>(EntityRepository repo, MacroExpansionSystem sys) where T : class
        {
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            return ((ISimulationView)repo).ReadManagedEvents<T>().ToArray();
        }

        /// <summary>The creation path, in miniature: each request becomes an entity with its GUID, origin, place and identity, in the map.</summary>
        private static Dictionary<Guid, Entity> Create(EntityRepository repo, NetworkEntityMap map, IEnumerable<EntityCreationRequest> made)
        {
            var byGuid = new Dictionary<Guid, Entity>();
            foreach (var r in made)
            {
                var e = repo.CreateEntity();
                foreach (var c in r.InitialComponents!)
                    switch (c)
                    {
                        case EntityGuid g: repo.AddComponent(e, g); byGuid[g.Value] = e; break;
                        case MacroOrigin o: repo.AddComponent(e, o); break;
                        case SimTransform t: repo.AddComponent(e, t); break;
                        case EntityInfo i: repo.AddComponent(e, i); break;
                    }
                repo.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = r.PreAllocatedNetworkId });
                map.Register(r.PreAllocatedNetworkId, e);
            }
            return byGuid;
        }

        private static Fdp.Toolkit.Navigation.PlacementResponseBatch AnswerAll(Fdp.Toolkit.Navigation.PlacementRequestBatch ask, System.Numerics.Vector3 shift,
                                                                               Fdp.Toolkit.Navigation.PlacementOutcome lastOutcome = Fdp.Toolkit.Navigation.PlacementOutcome.Shifted)
        {
            var answer = new Fdp.Toolkit.Navigation.PlacementResponseBatch { RequestId = ask.RequestId, SourceNodeId = Owner };
            foreach (var slot in ask.Slots)
                answer.Answers.Add(new Fdp.Toolkit.Navigation.PlacementAnswer
                {
                    Key = slot.Key, Point = slot.Point + shift, Reason = "no walkable ground within 30 m",
                    Outcome = slot.Key == ask.Slots[^1].Key ? lastOutcome : Fdp.Toolkit.Navigation.PlacementOutcome.Shifted,
                });
            return answer;
        }

        private static (EntityRepository Repo, MacroExpansionSystem System, Entity Instance, Dictionary<Guid, Entity> Men) ExpandedTeam(
            string macro = FireTeam, IMacroLibrary? library = null)
        {
            var (repo, sys, inst, map) = ExpandingWorldWithMap(macro, library);
            var ask = Assert.Single(Frame<Fdp.Toolkit.Navigation.PlacementRequestBatch>(repo, sys));
            repo.Bus.PublishManaged(AnswerAll(ask, System.Numerics.Vector3.Zero));
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var men = Create(repo, map, ((ISimulationView)repo).ReadManagedEvents<EntityCreationRequest>().ToArray());
            sys.Execute(repo, 0f);   // every child exists ⇒ Done + the reconcile baseline
            Assert.Equal(MacroExpansionState.Done, repo.GetComponentRO<MacroExpansionStatus>(inst).State);
            return (repo, sys, inst, men);
        }

        /// <summary>⭐ Macros debt A6 (SR-180: "the layer comes from the child's template mobility") — a child whose FILE carries no DIS type
        /// (0 ⇒ it used to be guessed infantry) is placed on the layer its TKB TEMPLATE states: a wheeled template ⇒ the vehicle layer, its
        /// radius half the larger footprint side; a pedestrian one ⇒ infantry; no template ⇒ the DIS guess. 🔴 Red-proof: ignore the template ⇒
        /// the vehicle child lands on the infantry layer.</summary>
        [Fact]
        public void A6_AVehicleChild_IsPlacedOnTheVehicleLayer_FromItsTemplate()
        {
            const long Truck = 7701, Man = 7702;
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var truck = new Fdp.Interfaces.TkbTemplate("Truck", Truck);
            truck.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.LocomotionDto { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Wheeled });
            truck.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.VehicleParametersDto { Length = 7f, Width = 2.5f });
            db.Register(truck);
            var man = new Fdp.Interfaces.TkbTemplate("Man", Man);
            man.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.LocomotionDto { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Pedestrian });
            db.Register(man);

            var sys = new MacroExpansionSystem(null, new NetworkEntityMap(), new SequentialIdAllocator(), Owner, db);
            EntityCreationRequest Child(long tkb) => new()
            {
                TkbType = (TkbTypeId)tkb, DisType = 0,   // ⚠ no DIS type in the file — what used to make every child "infantry"
                InitialComponents = new List<object> { At(10, 10, 0) },
            };
            var ask = MacroExpansionSystem.BuildPlacement(new[] { Child(Truck), Child(Man), Child(9999) }, 30f, null, sys.LayerOf)!;

            Assert.Equal((Fdp.Toolkit.Navigation.PlacementLayer.Vehicle, 3.5f), (ask.Slots[0].Layer, ask.Slots[0].Radius));
            Assert.Equal(Fdp.Toolkit.Navigation.PlacementLayer.Infantry, ask.Slots[1].Layer);
            Assert.Equal(Fdp.Toolkit.Navigation.PlacementLayer.Infantry, ask.Slots[2].Layer);   // no template ⇒ the DIS guess (0 ⇒ infantry)
        }

        /// <summary>⭐ Macros debt A4 (SR-150) — after a RESTORE the transient baseline is gone; it is rebuilt from the instance as it stands, so the
        /// next args edit still reconciles (here: <c>side</c> → every man's <c>ForceId</c>). 🔴 Red-proof: skip the rebuild ⇒ the men keep Hostile.</summary>
        [Fact]
        public void A4_AfterARestore_AnArgsEdit_StillReconciles()
        {
            var (repo, sys, inst, men) = ExpandedTeam();
            repo.RemoveComponent<MacroExpandedWith>(inst);                                       // what a restore leaves (the baseline is transient)

            sys.Execute(repo, 0f);
            Assert.True(repo.HasManagedComponent<MacroExpandedWith>(inst));                     // rebuilt from the instance as it stands

            repo.SetManagedComponent(inst, new MacroInstance { Macro = FireTeam, ArgsJson = """{"side": "Friend"}""" });   // an edit: a NEW record
            sys.Execute(repo, 0f);

            Assert.All(men.Values, m => Assert.Equal("Friend", repo.GetComponentRO<EntityInfo>(m).ForceId.ToString()));
        }

        /// <summary>
        /// ⭐⭐ CE-4326 P2 (📄 docs/UX/DESIGN_Scenario_Presets.md §9a.3) — a param bound into a component OTHER than the transform and the identity (here
        /// <c>Health.Max</c>, standing in for a behaviour's args) reaches the KEPT men when the instance's args change — what a Preview's preset does to a
        /// macro in place. Only what the args changed is applied: a component the old args made the same (<c>TkbIdentity</c>) is not written.
        /// 🔴 Red-proof (compiles): <c>ArgsChanged</c> returning false ⇒ the men keep 100 ⇒ red.
        /// </summary>
        [Fact]
        public void CE4326_P2_AnArgBoundIntoAKeptChildsComponent_ReachesIt_AndNothingElseIsWritten()
        {
            const string HpTeam = "test/hp-team";
            var file = JsonNode.Parse(FireTeamJson())!.AsObject();
            ((JsonArray)file["header"]!["Params"]!).Add(new JsonObject { ["Name"] = "hp", ["TypeId"] = "float", ["Default"] = 100 });
            foreach (var (_, man) in file["entities"]!.AsObject())
                man!["ParamBindings"]!["Bindings"]!["Health.Max"] = "hp";
            var lib = Library((HpTeam, file.ToJsonString()));

            var (repo, sys, inst, men) = ExpandedTeam(HpTeam, lib);
            Assert.All(men.Values, m => Assert.False(repo.HasComponent<Fdp.Toolkit.Combat.Components.Health>(m)));   // the miniature creation made them bare

            repo.SetManagedComponent(inst, new MacroInstance { Macro = HpTeam, ArgsJson = """{"hp": 250}""" });   // the preset's args: a NEW record
            sys.Execute(repo, 0f);

            Assert.All(men.Values, m => Assert.Equal(250f, repo.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(m).Max));
            Assert.All(men.Values, m => Assert.False(repo.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(m)));   // unchanged by the args ⇒ not written
        }

        /// <summary>⭐ Macros debt A3 (SR-157 / SR-180) — MOVING an instance re-runs placement for its children: one request, every slot naming
        /// the man it moves (so his old spot is not an obstacle), and the men stand where the solver said. 🔴 Red-proof: apply the moved transform
        /// without asking ⇒ no request is published.</summary>
        [Fact]
        public void A3_MovingAnInstance_RePlacesItsMen_ThroughTheSolver()
        {
            var (repo, sys, inst, men) = ExpandedTeam();
            var before = men.ToDictionary(kv => kv.Key, kv => repo.GetComponentRO<SimTransform>(kv.Value).Position);

            var moved = repo.GetComponentRO<SimTransform>(inst);
            moved.Position = new WorldPoint(moved.Position.X + 50, moved.Position.Y, 0);
            repo.SetComponent(inst, moved);

            var ask = Assert.Single(Frame<Fdp.Toolkit.Navigation.PlacementRequestBatch>(repo, sys));
            Assert.Equal(4, ask.Slots.Count);
            Assert.All(ask.Slots, s => Assert.NotEqual(0, s.Self));                    // each slot moves an existing man
            Assert.All(men.Values, m => Assert.Equal(before[repo.GetComponentRO<EntityGuid>(m).Value], repo.GetComponentRO<SimTransform>(m).Position));   // nothing moved yet

            repo.Bus.PublishManaged(AnswerAll(ask, new System.Numerics.Vector3(0, 3, 0)));
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);

            foreach (var (g, m) in men)
            {
                var p = repo.GetComponentRO<SimTransform>(m).Position;
                Assert.Equal(before[g].X + 50, p.X, 3);
                Assert.Equal(before[g].Y + 3, p.Y, 3);                                  // where the SOLVER said, not just the relocated point
            }
            Assert.Equal(MacroExpansionState.Done, repo.GetComponentRO<MacroExpansionStatus>(inst).State);
        }

        /// <summary>⭐ A3, found LIVE — under SPLIT AUTHORITY the men's body belongs to the simulation node, so the instance's node must ASK the
        /// owner (the engine's one attribute-request route), never write its replica: 📌 live, "4 kept" and the men never moved. 🔴 Red-proof:
        /// write the transform locally regardless of ownership ⇒ no request, and the replica is written.</summary>
        [Fact]
        public void A3_UnderSplitAuthority_TheMovedMen_AreRequestedFromTheirOwner_NotWrittenHere()
        {
            var (repo, sys, inst, men) = ExpandedTeam();
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkAuthority>()) repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>();
            if (!repo.Bus.IsRegisteredManaged<Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand>())
                repo.Bus.RegisterManaged<Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand>();
            foreach (var m in men.Values)
            {
                repo.AddComponent(m, new Fdp.Toolkit.Replication.Components.NetworkAuthority { PrimaryOwnerId = 400, LocalNodeId = (Fdp.Core.NodeId)400 });
                repo.SetAuthority(m, ComponentType<SimTransform>.ID, false);   // the body is the simulation node's
            }
            var before = men.ToDictionary(kv => kv.Key, kv => repo.GetComponentRO<SimTransform>(kv.Value).Position);
            var moved = repo.GetComponentRO<SimTransform>(inst);
            moved.Position = new WorldPoint(moved.Position.X + 50, moved.Position.Y, 0);
            repo.SetComponent(inst, moved);

            var ask = Assert.Single(Frame<Fdp.Toolkit.Navigation.PlacementRequestBatch>(repo, sys));
            repo.Bus.PublishManaged(AnswerAll(ask, System.Numerics.Vector3.Zero));
            repo.Bus.SwapBuffers();
            var asked = Frame<Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand>(repo, sys);

            Assert.Equal(4, asked.Length);
            foreach (var (g, m) in men)
            {
                Assert.Equal(before[g], repo.GetComponentRO<SimTransform>(m).Position);   // the replica is not written
                int id = repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(m).Value;
                var cmd = Assert.Single(asked, c => c.NetworkId == id);
                Assert.Contains($"\"X\":{(before[g].X + 50).ToString(System.Globalization.CultureInfo.InvariantCulture)}", cmd.AttributePatchJson);
                Assert.Contains("\"Heading\":", cmd.AttributePatchJson);
            }
        }

        /// <summary>⭐ A3 — a failed RE-placement fails loudly and leaves the old placement.</summary>
        [Fact]
        public void A3_AFailedRePlacement_IsLoud_AndLeavesTheOldPlacement()
        {
            var (repo, sys, inst, men) = ExpandedTeam();
            var before = men.ToDictionary(kv => kv.Key, kv => repo.GetComponentRO<SimTransform>(kv.Value).Position);
            var moved = repo.GetComponentRO<SimTransform>(inst);
            moved.Position = new WorldPoint(moved.Position.X + 50, moved.Position.Y, 0);
            repo.SetComponent(inst, moved);

            var ask = Assert.Single(Frame<Fdp.Toolkit.Navigation.PlacementRequestBatch>(repo, sys));
            repo.Bus.PublishManaged(AnswerAll(ask, System.Numerics.Vector3.Zero, Fdp.Toolkit.Navigation.PlacementOutcome.Failed));
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);

            foreach (var (g, m) in men) Assert.Equal(before[g], repo.GetComponentRO<SimTransform>(m).Position);
            var status = repo.GetComponentRO<MacroExpansionStatus>(inst);
            Assert.Equal(MacroExpansionState.Failed, status.State);
            Assert.Contains("old placement kept", status.Reason.ToString());
        }

        [Fact]
        public void Placement_IsAskedOnce_ChildrenWait_ThenStandWhereTheSolverSaid()
        {
            var (repo, sys, inst) = ExpandingWorld();

            var ask = Assert.Single(Frame<Fdp.Toolkit.Navigation.PlacementRequestBatch>(repo, sys));
            Assert.Equal(4, ask.Slots.Count);                                           // SR-185: one batch for the tree
            Assert.Equal(ScenarioHeader.DefaultMaxShift, ask.MaxShift);
            Assert.Empty(((ISimulationView)repo).ReadManagedEvents<EntityCreationRequest>());   // nothing created before the answer

            var answer = new Fdp.Toolkit.Navigation.PlacementResponseBatch { RequestId = ask.RequestId, SourceNodeId = Owner };
            foreach (var slot in ask.Slots)
                answer.Answers.Add(new Fdp.Toolkit.Navigation.PlacementAnswer
                {
                    Key = slot.Key, Outcome = Fdp.Toolkit.Navigation.PlacementOutcome.Shifted, Point = slot.Point + new System.Numerics.Vector3(7, 0, 0),
                });
            repo.Bus.PublishManaged(answer);
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var made = ((ISimulationView)repo).ReadManagedEvents<EntityCreationRequest>().ToArray();
            Assert.Equal(4, made.Length);
            var wanted = ask.Slots.Select(s => s.Point.X + 7).OrderBy(x => x).ToArray();
            Assert.Equal(wanted, made.Select(c => One<SimTransform>(c).Position.X).OrderBy(x => x).ToArray());
            Assert.True(((ISimulationView)repo).GetManagedComponentRO<MacroExpansionIntent>(inst)!.Published);
        }

        [Fact]
        public void Placement_OneFailedSlot_FailsTheWholeExpansion_NamingTheMan_AndCreatesNothing()
        {
            var (repo, sys, inst) = ExpandingWorld();
            var ask = Assert.Single(Frame<Fdp.Toolkit.Navigation.PlacementRequestBatch>(repo, sys));

            var answer = new Fdp.Toolkit.Navigation.PlacementResponseBatch { RequestId = ask.RequestId, SourceNodeId = Owner };
            foreach (var slot in ask.Slots)
                answer.Answers.Add(new Fdp.Toolkit.Navigation.PlacementAnswer
                {
                    Key = slot.Key, Point = slot.Point, Reason = "no walkable ground within 30 m",
                    Outcome = slot.Key == ask.Slots[^1].Key ? Fdp.Toolkit.Navigation.PlacementOutcome.Failed : Fdp.Toolkit.Navigation.PlacementOutcome.Exact,
                });
            repo.Bus.PublishManaged(answer);
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            Assert.Empty(((ISimulationView)repo).ReadManagedEvents<EntityCreationRequest>());   // SR-183: all or nothing
            var status = repo.GetComponentRO<MacroExpansionStatus>(inst);
            Assert.Equal(MacroExpansionState.Failed, status.State);
            Assert.Contains("no walkable ground", status.Reason.ToString());
            Assert.False(repo.HasManagedComponent<MacroExpansionIntent>(inst));
        }

        [Fact]
        public void Placement_NoSolverAnswers_PlacesOnThisNode_AndSaysSo()
        {
            var (repo, sys, inst) = ExpandingWorld();
            Assert.Single(Frame<Fdp.Toolkit.Navigation.PlacementRequestBatch>(repo, sys));

            EntityCreationRequest[] made = Array.Empty<EntityCreationRequest>();
            for (int i = 0; i <= MacroExpansionSystem.PlacementWaitFrames && made.Length == 0; i++)
                made = Frame<EntityCreationRequest>(repo, sys);

            Assert.Equal(4, made.Length);                                                       // E-6: the load is not stuck
            Assert.Contains("placed unchecked", repo.GetComponentRO<MacroExpansionStatus>(inst).Reason.ToString());
        }

        /// <summary>⭐ Macros S3b B3 (SR-99) — the counter-attack DIRECTOR is a macro (no director entity): the real
        /// <c>macros/control/counter-attack</c> prepared with two different <c>delay</c> args (the ONE routine the expander uses) gives its trigger each
        /// scenario's own fire time through the <c>ParamBindings</c> path into the trigger's params JSON; no args ⇒ the declared default.
        /// 🔴 Red-proof: drop the binding from the file ⇒ every fire time is the authored 60.</summary>
        [Fact]
        public void B3_TheDirectorMacro_UsedTwice_WithDifferentParams()
        {
            string file = File.ReadAllText(Path.Combine(TestRepo.Root, "macros", "control", "counter-attack", "scenario.json"));
            double FireTime(string args)
            {
                var dom = (JsonObject)JsonNode.Parse(file)!;
                ScenarioParams.Prepare(dom, ScenarioHeaderJson.Read(dom), args, null, "macro 'control/counter-attack'", 0, Guid.NewGuid());
                var trig = (JsonObject)((JsonObject)dom["entities"]!).Single().Value!;
                var p = JsonNode.Parse(Fdp.Core.Serialization.Converters.JsonTextConverter.TextOf(trig["Behavior"]!["Params"])!)!;
                Assert.Equal("Trigger", trig["Behavior"]!["Name"]!.GetValue<string>());
                return p["when"]!["fromSeconds"]!.GetValue<double>();
            }
            Assert.Equal(4, FireTime("""{"delay": 4}"""));
            Assert.Equal(9, FireTime("""{"delay": 9}"""));
            Assert.Equal(60, FireTime(""));
        }

        private sealed class OutsideScope : IRefScope
        {
            public string Describe => "outside";
            public bool TryResolve(in RefSpec spec, out EntityRef entity)
            { entity = spec.KeyText == "Evaluation" ? new EntityRef(77) : spec.KeyText == "Exercise" ? new EntityRef(78) : EntityRef.None; return !entity.IsNone; }
            public bool TryPositionOf(EntityRef entity, out WorldPoint position) { position = default; return false; }
        }

        /// <summary>⭐ S4 ① live gate — the SHIPPED <c>macros/evaluation/selection-mean</c> through the REAL expander + <see cref="FileMacroLibrary"/> with the args
        /// <c>MemberEvaluation</c> passes: the behaviour's params read the given title / group / by / key (strings: bound through <c>ParamBindings</c> — a
        /// <c>"^title"</c> string leaf is NOT bound by the walker, it stayed the literal text live) and the scope / parent links resolve.
        /// 🔴 Red-proof: drop the file's ParamBindings (a temp copy) ⇒ the title stays the declared default.</summary>
        [Fact]
        public void TheShippedSelectionMean_ExpandsThroughTheRealExpander_WithItsStringsAndLinksBound()
        {
            const string Args = """{"by": "standard", "key": "No fratricide", "scope": "@Evaluation", "parent": "@Exercise", "title": "Mean of No fratricide", "group": "Standards"}""";
            var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(registry, null, UnknownComponentPolicy.WarnAndSkip);
            var expander = new MacroExpander(new FileMacroLibrary(TestRepo.Root), new StagingEntityExtractor(), serializer,
                                          Hrot.CGF.Configuration.CgfBehaviorSetup.CreateBehaviorRemapper(registry));

            var r = expander.Expand(new MacroInstance { Macro = "evaluation/selection-mean", ArgsJson = Args }, 500, Guid.NewGuid(), Owner, At(0, 0, 0),
                                    new OutsideScope(), new SequentialIdAllocator());
            Assert.True(r.Ok, r.Failure);
            var child = Assert.Single(r.Children);
            var p = ParamsOf(child);

            Assert.Equal("Mean of No fratricide", (string)p["common"]!["title"]!);
            Assert.Equal("Standards", (string)p["common"]!["group"]!);
            Assert.Equal("standard", (string)p["by"]!);
            Assert.Equal("No fratricide", (string)p["key"]!);
            var typed = System.Text.Json.JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.SelectionMeanParamsJsonDto>(p.ToJsonString(), Fdp.Toolkit.Behavior.BehaviorParams.JsonOptions);
            Assert.Equal(77, typed.Scope.Ref.NetworkId);
            Assert.Equal(78, typed.Common.Parent.Ref.NetworkId);
        }

        private static JsonNode ParamsOf(EntityCreationRequest child)
        {
            var slot = One<Fdp.Toolkit.Behavior.Components.InitialBrainIntent>(child).Behavior;
            Assert.Equal("SelectionMean", slot?.Name);
            return JsonNode.Parse(slot!.Params)!;
        }
    }
}
