using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.Translators;
using Hrot.MuscleCharacter.Animation.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.CGF.Configuration;
using Hrot.Map.Common;
using Hrot.SimHost.Serializers;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ rules S1 ① (📄 docs/DESIGN_Scenario_Rules.md §00.8) — the foundation: an objective is an ENTITY (TKB type 8860) that runs a
    /// brain with no body. The rail follows the load path as far as a unit test can: the production catalogue's template, its
    /// <see cref="BehaviorTkbTranslator"/> (the creation step), the scenario record's <c>"Behavior"</c> read by the production
    /// <see cref="BrainSnapshotTranslator"/>, the materialization and ingress, and the real <see cref="BrainTickSystem"/>.
    /// </summary>
    public sealed partial class ScenarioRulesTests
    {
        /// <summary>The Objective type is in the catalogue as a brain with no body (cannot move, shoot or interact), and a scenario entity of
        /// that type whose record names <c>HoldStance</c> gets a ticking brain. ✅ Red-proof: the Objective template's
        /// <c>BehaviorProfileDto</c> removed from <c>NedTkbCatalog</c> ⇒ no <c>BehaviorState</c>, the behaviour never starts ⇒ red.</summary>
        [Fact]
        public void ObjectiveEntity_FromAScenarioRecord_GetsABrainThatTicks()
        {
            var db = new TkbDatabase();
            Hrot.Map.Definitions.Tkb.NedTkbCatalog.RegisterAll(db);
            var template = db.GetByType(TkbEntityTypes.Objective);
            var profile = template.GetDescriptor<BehaviorProfileDto>();
            Assert.NotNull(profile);
            Assert.True(profile!.BrainTier != 0);
            Assert.False(profile.CanMove || profile.CanShoot || profile.CanInteract);

            var repo = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(repo);
            CognitiveComponentRegistry.RegisterAll(repo);
            BlueprintTierTable.RegisterAll(repo);
            var geo = new WGS84Transform();
            geo.SetOrigin(0.0, 0.0, 0.0);
            repo.SetSingletonManaged<IGeographicTransform>(geo);
            if (!repo.IsComponentTypeRegistered<StanceIntent>()) repo.RegisterComponent<StanceIntent>();   // the probe: HoldStance writes it when present

            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var ingress = new BehaviorIngressSystem(registry);
            var brain = new BrainTickSystem(registry);
            var materialize = new InitialBrainMaterializationSystem();

            // the creation step: the entity of the type, its template's translators, then the record's "Behavior"
            var objective = repo.CreateEntity();
            repo.AddComponent(objective, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Objective });
            new BehaviorTkbTranslator().Inject(repo, objective, template);
            repo.AddComponent(objective, new StanceIntent());
            new BrainSnapshotTranslator(registry).Inject(repo, objective,
                new Dictionary<string, object> { [BrainSnapshotTranslator.BehaviorKey] = JsonNode.Parse("{\"Name\":\"HoldStance\",\"Params\":\"{\\\"stance\\\":\\\"Prone\\\"}\"}")! },
                guidResolver: null!);
            Assert.True(repo.HasManagedComponent<InitialBrainIntent>(objective));

            for (int i = 0; i < 5; i++)
            {
                repo.Bus.SwapBuffers();
                materialize.Execute(repo, 0.016f);
                ingress.Execute(repo, 0.016f);
                brain.Execute(repo, 0.016f);
                repo.FlushCommandBuffers();
            }

            Assert.True(registry.TryGetName(repo.GetComponent<BehaviorState>(objective).ActiveBehaviorHash, out var name));
            Assert.Equal("HoldStance", name);
            Assert.Equal(StanceId.Prone, repo.GetComponentRO<StanceIntent>(objective).TargetStance);   // the tree TICKED: it wrote its stance
        }

        /// <summary>The two components are what the design says: ids 425 / 426, runtime-only (NoScenario), unmanaged (recordable), and a
        /// state is final exactly when it is Achieved, Failed or Skipped (SR-117). ✅ Red-proof: <c>IsFinal</c> counting Inactive ⇒ red.</summary>
        [Fact]
        public void TheComponents_AreRuntimeOnly_Unmanaged_AndFinalMeansAchievedFailedOrSkipped()
        {
            Assert.Equal(425, GlobalComponentIds.ObjectiveStatus);
            Assert.Equal(426, GlobalComponentIds.ObjectiveLinks);
            foreach (var t in new[] { typeof(ObjectiveStatus), typeof(ObjectiveLinks) })
            {
                Assert.True(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<DataPolicyAttribute>(t)!.Policy.HasFlag(DataPolicy.NoScenario), t.Name);
                Assert.False(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ObjectiveStatus>() && t == typeof(ObjectiveStatus));
            }
            var expected = new Dictionary<ObjectiveState, bool>
            {
                [ObjectiveState.Pending] = false, [ObjectiveState.Active] = false, [ObjectiveState.Achieved] = true,
                [ObjectiveState.Failed] = true, [ObjectiveState.Inactive] = false, [ObjectiveState.Skipped] = true,
            };
            foreach (var (state, final) in expected) Assert.Equal(final, new ObjectiveStatus { State = state }.IsFinal);
        }

        // ═════════════════════════════════════════ rules S1 ② — the kinds and the aggregators ═════════════════════════════════════════

        private sealed class ObjWorld
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            private readonly Hrot.AI.Behaviors.Brains.Objectives.RuleEditSystem _ruleEdit;   // ⭐ rules ② — the live edit, before the brain tick (as CgfLogicPack orders it)
            private readonly Hrot.CGF.Systems.TacticalIntentResolutionSystem _intents;
            private readonly Hrot.AI.Behaviors.Brains.Objectives.ExercisePauseOnEndSystem _pause = new();
            private readonly TkbDatabase _db = new();
            private int _net = 100;
            public double Time;

            public ObjWorld()
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                if (!Repo.IsComponentTypeRegistered<NetworkIdentity>()) Repo.RegisterComponent<NetworkIdentity>();
                if (!Repo.Bus.IsRegistered<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>()) Repo.Bus.Register<Fdp.Toolkit.Combat.Events.DamageAssessedEvent>();
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain = new BrainTickSystem(Registry);
                _ruleEdit = new Hrot.AI.Behaviors.Brains.Objectives.RuleEditSystem(_ingress);
                _intents = new Hrot.CGF.Systems.TacticalIntentResolutionSystem(new Fdp.Toolkit.Behavior.TacticalOrderMapper.TacticalIntentMapperRegistry(), Registry);
                Hrot.Map.Definitions.Tkb.NedTkbCatalog.RegisterAll(_db);
            }

            public int Net(Entity e) => Repo.GetComponentRO<NetworkIdentity>(e).Value;

            public Entity Man(Vector3 at, ForceId side, float health = 0f)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                Repo.AddComponent(e, new EntityInfo { ForceId = side });
                Repo.AddComponent(e, new NetworkIdentity { Value = _net++ });
                if (health > 0f) Repo.AddComponent(e, new Health { Current = health, Max = health });
                return e;
            }

            /// <summary>A body-less squad commander over <paramref name="men"/>.</summary>
            public Entity Squad(ForceId side, params Entity[] men)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new UnitEntity());
                Repo.AddComponent(e, new EntityInfo { ForceId = side });
                Repo.AddComponent(e, new NetworkIdentity { Value = _net++ });
                var roster = new UnitRoster();
                foreach (var m in men) roster.SubordinateEntities[roster.Count++] = m;
                Repo.AddComponent(e, roster);
                return e;
            }

            public void Move(Entity e, Vector3 to) => Repo.GetComponentRW<SimTransform>(e).Position = to;

            /// <summary>An Objective entity (TKB 8860) running <paramref name="behavior"/> with <paramref name="json"/> params.</summary>
            public Entity Objective(string behavior, string json)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Objective });
                Repo.AddComponent(e, new NetworkIdentity { Value = _net++ });
                new BehaviorTkbTranslator().Inject(Repo, e, _db.GetByType(TkbEntityTypes.Objective));
                Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = behavior, JsonParams = json, Origin = BehaviorOrigin.Superior });
                return e;
            }

            /// <summary>⭐ rules S2-c — A Trigger entity (TKB 8861) running the <c>Trigger</c> behaviour with <paramref name="json"/> params.</summary>
            public Entity Trigger(string json)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Trigger });
                Repo.AddComponent(e, new NetworkIdentity { Value = _net++ });
                new BehaviorTkbTranslator().Inject(Repo, e, _db.GetByType(TkbEntityTypes.Trigger));
                Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = "Trigger", JsonParams = json, Origin = BehaviorOrigin.Superior });
                return e;
            }

            public void Run(double seconds, double step = 0.5) { for (double t = 0; t < seconds - 1e-9; t += step) Tick(step); }

            public void Tick(double step = 0.5)
            {
                Time += step;
                Repo.SetSingleton(new GlobalTime { TotalTime = Time, DeltaTime = (float)step, TimeScale = 1f, FrameNumber = (long)(Time / step) });
                Repo.SetSimulationTime((float)Time);
                Repo.Bus.SwapBuffers();
                if (Repo.Bus.IsRegistered<ExerciseEndedEvent>()) Ended += Repo.ReadEvents<ExerciseEndedEvent>().Length;
                if (Repo.Bus.IsRegisteredManaged<PlayerMessageEvent>()) foreach (var m in ((Fdp.ModuleHost.Abstractions.ISimulationView)Repo).ReadManagedEvents<PlayerMessageEvent>()) Messages.Add(m);
                // ⭐ macros S3b B1 — what InsertMacro asked the creation path for (the rail plays the creating node)
                if (Repo.Bus.IsRegisteredManaged<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest>())
                    foreach (var r in ((Fdp.ModuleHost.Abstractions.ISimulationView)Repo).ReadManagedEvents<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest>()) Created.Add(r);
                // ⭐ rules S2 ⑨ — the mission-control seam: record what a verb asked, and answer it like MissionControlExecutionSystem would (the rail picks the code)
                if (Repo.Bus.IsRegisteredManaged<Hrot.Common.Events.MissionControlIntent>())
                    foreach (var mi in ((Fdp.ModuleHost.Abstractions.ISimulationView)Repo).ReadManagedEvents<Hrot.Common.Events.MissionControlIntent>())
                    {
                        MissionIntents.Add(mi);
                        if (MissionAnswer is { } code)
                        {
                            if (!Repo.Bus.IsRegistered<Hrot.Common.Events.MissionControlAckEvent>()) Repo.Bus.Register<Hrot.Common.Events.MissionControlAckEvent>();
                            Repo.Bus.Publish(new Hrot.Common.Events.MissionControlAckEvent { RequestId = mi.RequestId, ErrorCode = code });
                        }
                    }
                if (Repo.Bus.IsRegisteredManaged<Fdp.Toolkit.Time.Domain.PauseTimeIntent>()) Pauses += ((Fdp.ModuleHost.Abstractions.ISimulationView)Repo).ReadManagedEvents<Fdp.Toolkit.Time.Domain.PauseTimeIntent>().Count;
                if (ResolveIntents) _intents.Execute(Repo, (float)step);
                _ingress.Execute(Repo, (float)step);
                _ruleEdit.Execute(Repo, (float)step);
                _brain.Execute(Repo, (float)step);
                _pause.Execute(Repo, (float)step);
                Repo.FlushCommandBuffers();
            }

            /// <summary>⭐ rules S2 ⑨ — a rail that wants an order to go UNANSWERED (nothing turns the intent into an assignment) switches this off.</summary>
            public bool ResolveIntents = true;
            /// <summary>⭐ rules S2 ⑨ — the <c>MissionControlIntent</c>s verbs published, and the <c>ErrorCode</c> the stand-in executor answers with (null = never answers).</summary>
            public readonly List<Hrot.Common.Events.MissionControlIntent> MissionIntents = new();
            public int? MissionAnswer;
            /// <summary>⭐ rules S2 ⑨ — the <c>PauseTimeIntent</c>s published so far (read after each swap).</summary>
            public int Pauses;

            /// <summary>⭐ rules S2 ⑨ — an entity with a brain (the Objective template's, no body) that an <c>Order</c> can start a behaviour on; carries the stance a <c>HoldStance</c> writes.</summary>
            public Entity Unit()
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Objective });
                Repo.AddComponent(e, new NetworkIdentity { Value = _net++ });
                new BehaviorTkbTranslator().Inject(Repo, e, _db.GetByType(TkbEntityTypes.Objective));
                if (!Repo.IsComponentTypeRegistered<StanceIntent>()) Repo.RegisterComponent<StanceIntent>();
                Repo.AddComponent(e, new StanceIntent());
                Repo.SetAuthority<BehaviorState>(e, true);   // the order's resolver acts on the owner's node only
                return e;
            }

            public ObjectiveStatus S(Entity o) => Repo.GetComponentRO<ObjectiveStatus>(o);

            /// <summary>⭐ rules S2 ⑧ — the exercise-ended events and player messages seen so far (read after each swap, so each is counted once).</summary>
            public int Ended;
            public readonly List<PlayerMessageEvent> Messages = new();
            public readonly List<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest> Created = new();

            /// <summary>⭐ rules S2 ⑧ — the World Environment: the positionless entity carrying the scenario clock (what <c>EndExercise</c> finds).</summary>
            public Entity Environment()
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new NetworkIdentity { Value = _net++ });
                Repo.AddComponent(e, Fdp.Toolkit.Weather.ScenarioClock.Default);
                return e;
            }

            /// <summary>⭐ CE-3364 — a hit takes <paramref name="victim"/> out of the fight: Health to 0 and ONE <c>ManDownedEvent</c> through the production helper (<c>ManDowned</c>), as <c>HealthApplicationSystem</c> does.</summary>
            public void Kill(Entity victim, Entity source, Entity original = default)
            {
                bool before = Fdp.Toolkit.Combat.ManDowned.Before(Repo, victim);
                Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(victim).Current = 0f;
                Fdp.Toolkit.Combat.ManDowned.PublishIfDowned(Repo, victim, before, source, original);
            }

            public void Harm(Entity victim, Entity source, float damage = 5f, Entity original = default)
                => Repo.Bus.Publish(new Fdp.Toolkit.Combat.Events.DamageAssessedEvent { HitEntity = victim, Source = source, TotalDamage = damage, OriginalSource = original });
        }

        private const string Circle0 = "{\"circle\":{\"center\":[0,0,0],\"radius\":10}}";
        private static readonly Vector3 Inside = new(2, 2, 0), Outside = new(100, 100, 0);

        private static string Common(ObjWorld w, Entity subject, string extra = "")
            => $"\"common\":{{\"title\":\"T\",\"subject\":{w.Net(subject)}{extra}}}";

        /// <summary>Rail 1 — ReachArea: a 2-man squad outside → Active "0/1"; a man inside → Achieved; with a deadline of 5 s and nobody inside → Failed
        /// after 5 s. ✅ Red-proof: the deadline test removed ⇒ red.</summary>
        [Fact]
        public void ReachArea_AchievesWhenAManIsInside_AndFailsAtTheDeadline()
        {
            var w = new ObjWorld();
            var a = w.Man(Outside, ForceId.Friend); var b = w.Man(Outside, ForceId.Friend);
            var squad = w.Squad(ForceId.Friend, a, b);
            var reach = w.Objective("ReachArea", $"{{{Common(w, squad)},\"area\":{Circle0}}}");
            var late = w.Objective("ReachArea", $"{{{Common(w, squad, ",\"deadlineSeconds\":5")},\"area\":{Circle0}}}");
            w.Run(2);
            Assert.Equal((ObjectiveState.Active, "0/1 men inside"), (w.S(reach).State, w.S(reach).Reason.ToString()));
            Assert.Equal("ReachArea", w.S(reach).Kind.ToString());
            w.Move(a, Inside);
            w.Run(1);
            Assert.Equal((ObjectiveState.Achieved, 1f), (w.S(reach).State, w.S(reach).Score));
            Assert.Equal(ObjectiveState.Achieved, w.S(late).State);   // the man got there before 5 s

            var w2 = new ObjWorld();
            var squad2 = w2.Squad(ForceId.Friend, w2.Man(Outside, ForceId.Friend), w2.Man(Outside, ForceId.Friend));
            var o = w2.Objective("ReachArea", $"{{{Common(w2, squad2, ",\"deadlineSeconds\":5")},\"area\":{Circle0}}}");
            w2.Run(4); Assert.Equal(ObjectiveState.Active, w2.S(o).State);
            w2.Run(3); Assert.Equal(ObjectiveState.Failed, w2.S(o).State);
        }

        /// <summary>Rail 2 — HoldArea: 4 men inside, holdSeconds 10 → Achieved after 10 s; an enemy entering at 5 s resets the hold; capture flips the
        /// Owner. ✅ Red-proof: the hold NOT reset when the enemy enters ⇒ red.</summary>
        [Fact]
        public void HoldArea_AchievesAfterTheHold_AnEnemyResetsIt_CaptureFlipsTheOwner()
        {
            var w = new ObjWorld();
            var men = new[] { w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend) };
            var squad = w.Squad(ForceId.Friend, men);
            var hold = w.Objective("HoldArea", $"{{{Common(w, squad)},\"area\":{Circle0},\"holdSeconds\":10}}");
            w.Run(5.5);
            Assert.Equal(ObjectiveState.Active, w.S(hold).State);
            Assert.InRange(w.S(hold).ProgressCur, 4, 5);
            var enemy = w.Man(new Vector3(1, 1, 0), ForceId.Hostile);
            w.Run(1);
            Assert.Equal(0, w.S(hold).ProgressCur);                 // broken: the timer restarts
            w.Run(8);
            Assert.Equal(ObjectiveState.Active, w.S(hold).State);   // only ~8 s since it broke, not 10
            w.Move(enemy, Outside);
            w.Run(11);
            Assert.Equal(ObjectiveState.Achieved, w.S(hold).State);
            Assert.Contains("men", w.S(hold).Reason.ToString());

            // capture: never ends; the Owner is the state
            var c = new ObjWorld();
            var blue = new[] { c.Man(Inside, ForceId.Friend), c.Man(Inside, ForceId.Friend) };
            var bsquad = c.Squad(ForceId.Friend, blue);
            var cap = c.Objective("HoldArea", $"{{{Common(c, bsquad)},\"area\":{Circle0},\"minMen\":2,\"capture\":true}}");
            c.Run(3);
            Assert.Equal((ObjectiveState.Active, ForceId.Friend), (c.S(cap).State, c.S(cap).Owner));
            foreach (var m in blue) c.Move(m, Outside);
            var red = new[] { c.Man(Inside, ForceId.Hostile), c.Man(Inside, ForceId.Hostile) };
            c.Run(3);
            Assert.Equal((ObjectiveState.Active, ForceId.Hostile), (c.S(cap).State, c.S(cap).Owner));
        }

        /// <summary>Rail 3 — ClearArea: an enemy inside → Active; killed (Health 0) → Achieved after forSeconds.</summary>
        [Fact]
        public void ClearArea_AchievesForSecondsAfterTheEnemyIsKilled()
        {
            var w = new ObjWorld();
            var squad = w.Squad(ForceId.Friend, w.Man(Outside, ForceId.Friend));
            var enemy = w.Man(Inside, ForceId.Hostile, health: 10f);
            var clear = w.Objective("ClearAreaObjective", $"{{{Common(w, squad)},\"area\":{Circle0},\"forSeconds\":3}}");
            w.Run(5);
            Assert.Equal(ObjectiveState.Active, w.S(clear).State);
            w.Repo.GetComponentRW<Health>(enemy).Current = 0f;
            w.Run(2);
            Assert.Equal(ObjectiveState.Active, w.S(clear).State);
            w.Run(3);
            Assert.Equal(ObjectiveState.Achieved, w.S(clear).State);
        }

        /// <summary>Rail 4 — DestroyTargets and Protect with 2 targets.</summary>
        [Fact]
        public void DestroyTargets_AndProtect_CountTheTwoTargets()
        {
            var w = new ObjWorld();
            var t1 = w.Man(Inside, ForceId.Hostile, 10f); var t2 = w.Man(Inside, ForceId.Hostile, 10f);
            var p1 = w.Man(Inside, ForceId.Friend, 10f); var p2 = w.Man(Inside, ForceId.Friend, 10f);
            string Targets(params Entity[] es) => "[" + string.Join(",", es.Select(w.Net)) + "]";
            var destroy = w.Objective("DestroyTargets", $"{{\"common\":{{\"title\":\"D\"}},\"targets\":{Targets(t1, t2)}}}");
            var protectAll = w.Objective("Protect", $"{{\"common\":{{\"title\":\"P\"}},\"targets\":{Targets(p1, p2)}}}");
            var protectOne = w.Objective("Protect", $"{{\"common\":{{\"title\":\"P1\",\"deadlineSeconds\":8}},\"targets\":{Targets(p1, p2)},\"minSurvivors\":1}}");
            w.Run(2);
            Assert.Equal((ObjectiveState.Active, 0, 2), (w.S(destroy).State, w.S(destroy).ProgressCur, w.S(destroy).ProgressNeed));
            Assert.Equal((ObjectiveState.Active, 1f), (w.S(protectAll).State, w.S(protectAll).Score));   // judged at the end: 1 while within
            w.Repo.GetComponentRW<Health>(t1).Current = 0f;
            w.Run(1);
            Assert.Equal((ObjectiveState.Active, 1), (w.S(destroy).State, w.S(destroy).ProgressCur));
            w.Repo.DestroyEntity(t2);
            w.Run(1);
            Assert.Equal(ObjectiveState.Achieved, w.S(destroy).State);

            w.Repo.GetComponentRW<Health>(p1).Current = 0f;
            w.Run(1);
            Assert.Equal((ObjectiveState.Failed, 0f), (w.S(protectAll).State, w.S(protectAll).Score));   // fewer survive: failed at once
            Assert.Equal(ObjectiveState.Active, w.S(protectOne).State);                                   // one is enough
            w.Run(8);
            Assert.Equal(ObjectiveState.Achieved, w.S(protectOne).State);                                 // within at the deadline
        }

        /// <summary>Rail 5 — NoHarm: a Hostile shooter with bySide Friend leaves it Active (Score 1); a Friend shooter on a Neutral fails it.
        /// ✅ Red-proof: the by-side test removed ⇒ red (the Hostile shooter fails it).</summary>
        [Fact]
        public void NoHarm_FailsOnHarmByTheSide_NotByAnother()
        {
            var w = new ObjWorld();
            var friend = w.Man(Outside, ForceId.Friend); var hostile = w.Man(Outside, ForceId.Hostile); var civilian = w.Man(Outside, ForceId.Neutral);
            var squad = w.Squad(ForceId.Friend, friend);
            var noHarm = w.Objective("NoHarm", $"{{{Common(w, squad)},\"protected\":\"Neutral\",\"bySide\":\"Friend\"}}");
            w.Run(1);
            w.Harm(civilian, hostile);
            w.Run(1);
            Assert.Equal((ObjectiveState.Active, 1f), (w.S(noHarm).State, w.S(noHarm).Score));
            w.Harm(civilian, friend, 0f);                    // no damage: not harm
            w.Run(1);
            Assert.Equal(ObjectiveState.Active, w.S(noHarm).State);
            w.Harm(civilian, friend);
            w.Run(1);
            Assert.Equal((ObjectiveState.Failed, 0f, 1), (w.S(noHarm).State, w.S(noHarm).Score, w.S(noHarm).ProgressCur));
        }

        /// <summary>⭐ <c>CE-4155</c> (SR-82; §10.10) — NoHarm <c>attribute</c> says which source of a RETHROWN round's harm is judged. A Hostile man threw a grenade
        /// first, a Friend threw it back (Source = Friend, OriginalSource = Hostile): <c>last</c> (the default) counts it against the Friend side,
        /// <c>original</c> does not; the other way round (Friend first, Hostile last) <c>original</c> counts it against Friend and <c>last</c> does not;
        /// <c>both</c> counts either. A harm with no OriginalSource is the same under every attribute. ✅ Red-proof: <c>Attributed</c> ignoring the attribute
        /// (always last) ⇒ the <c>original</c> / <c>both</c> rows red.</summary>
        [Theory]
        [InlineData("last", true, false, true)]       // Friend threw last: counts; Hostile threw last: the Friend-first case does not
        [InlineData("original", false, true, true)]
        [InlineData("both", true, true, true)]
        public void CE4155_NoHarm_Attribute_JudgesTheLastThrower_TheFirst_OrBoth(string attribute, bool friendLastCounts, bool friendFirstCounts, bool plainCounts)
        {
            Assert.Equal((friendLastCounts, friendFirstCounts, plainCounts),
                (FailsWith(attribute, friendLast: true, original: true), FailsWith(attribute, friendLast: false, original: true),
                 FailsWith(attribute, friendLast: true, original: false)));
        }

        private bool FailsWith(string attribute, bool friendLast, bool original)
        {
            var w = new ObjWorld();
            var friend = w.Man(Outside, ForceId.Friend); var hostile = w.Man(Outside, ForceId.Hostile); var civilian = w.Man(Outside, ForceId.Neutral);
            var squad = w.Squad(ForceId.Friend, friend);
            var noHarm = w.Objective("NoHarm", $"{{{Common(w, squad)},\"protected\":\"Neutral\",\"bySide\":\"Friend\",\"attribute\":\"{attribute}\"}}");
            w.Run(1);
            var last = friendLast ? friend : hostile; var first = friendLast ? hostile : friend;
            w.Harm(civilian, last, 5f, original ? first : default);
            w.Run(1);
            return w.S(noHarm).State == ObjectiveState.Failed;
        }

        /// <summary>Rail 6 — WeightedMean over 3 children incl. a required NoHarm that fails → Failed, Score 0; all achieved → Achieved, Score 1.
        /// AllMustPass likewise. ✅ Red-proof: the required-failed rule removed ⇒ red.</summary>
        [Fact]
        public void TheComposites_FailOnARequiredChild_AndAchieveWhenAllPass()
        {
            foreach (var (kind, json) in new[] { ("WeightedMean", "\"passScore\":0.6"), ("AllMustPass", "") })
            {
                foreach (bool harm in new[] { true, false })
                {
                    var w = new ObjWorld();
                    var friend = w.Man(Inside, ForceId.Friend); var civilian = w.Man(Outside, ForceId.Neutral);
                    var squad = w.Squad(ForceId.Friend, friend);
                    var comp = w.Objective(kind, $"{{\"common\":{{\"title\":\"C\"}}{(json.Length > 0 ? "," + json : "")}}}");
                    string Child(string k, string extra) => w.Net(comp) is var p ? $"{{\"common\":{{\"title\":\"c\",\"subject\":{w.Net(squad)},\"parent\":{p}{extra}}},\"area\":{Circle0}" : "";
                    var c1 = w.Objective("ReachArea", Child("ReachArea", "") + "}");
                    var c2 = w.Objective("ReachArea", Child("ReachArea", ",\"weight\":2") + "}");
                    var c3 = w.Objective("NoHarm", $"{{\"common\":{{\"title\":\"No harm\",\"subject\":{w.Net(squad)},\"parent\":{w.Net(comp)},\"required\":true,\"deadlineSeconds\":4}},\"protected\":\"Neutral\"}}");
                    w.Run(1);
                    if (harm) w.Harm(civilian, friend);
                    w.Run(6);
                    var s = w.S(comp);
                    if (harm) Assert.Equal((ObjectiveState.Failed, 0f), (s.State, s.Score));
                    else Assert.Equal((ObjectiveState.Achieved, 1f), (s.State, s.Score));
                }
            }
        }

        /// <summary>A composite stays Pending until a child is active (SR-115).</summary>
        [Fact]
        public void AComposite_IsPendingUntilAChildIsActive()
        {
            var w = new ObjWorld();
            var comp = w.Objective("AllMustPass", "{\"common\":{\"title\":\"C\"}}");
            w.Run(2);
            Assert.Equal(ObjectiveState.Pending, w.S(comp).State);
            var squad = w.Squad(ForceId.Friend, w.Man(Outside, ForceId.Friend));
            w.Objective("ReachArea", $"{{\"common\":{{\"subject\":{w.Net(squad)},\"parent\":{w.Net(comp)}}},\"area\":{Circle0}}}");
            w.Run(2);
            Assert.Equal(ObjectiveState.Active, w.S(comp).State);
        }

        /// <summary>⭐ 2b — a composite that starts Inactive is frozen; switched on later (what S2's SetObjectiveActive does: the State written
        /// directly) it aggregates its children — for BOTH composites. ✅ Red-proof: the StartActive term back in the condition ⇒ red.</summary>
        [Theory]
        [InlineData("AllMustPass")]
        [InlineData("WeightedMean")]
        public void AComposite_StartedInactive_IsFrozen_ThenAggregatesOnceSwitchedOn(string kind)
        {
            var w = new ObjWorld();
            var squad = w.Squad(ForceId.Friend, w.Man(Inside, ForceId.Friend));
            var comp = w.Objective(kind, "{\"common\":{\"title\":\"C\",\"startActive\":false}}");
            w.Objective("ReachArea", $"{{\"common\":{{\"subject\":{w.Net(squad)},\"parent\":{w.Net(comp)}}},\"area\":{Circle0}}}");
            w.Run(3);
            Assert.Equal(ObjectiveState.Inactive, w.S(comp).State);                     // frozen though its child is done
            w.Repo.GetComponentRW<ObjectiveStatus>(comp).State = ObjectiveState.Active; // what the action will do
            w.Run(2);
            Assert.Equal(ObjectiveState.Achieved, w.S(comp).State);
        }

        /// <summary>⭐ S1-g — the hand-authored <c>scenarios/sr-objectives-march</c>: its five Objective entities are TKB 8860, name a registered
        /// objective behaviour, and every Params string parses into that kind's DTO with the squad as Subject (its GUID), the root as Parent and the
        /// authored circles. (The editor verifier needs a checks.json — the coordinator's.)</summary>
        [Fact]
        public void TheObjectivesScenarioFile_HasFiveObjectiveEntities_WhoseParamsParseIntoTheirDtos()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "scenarios", "sr-objectives-march", "scenario.json"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var doc = JsonNode.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "scenarios", "sr-objectives-march", "scenario.json")))!;
            var entities = doc["entities"]!.AsObject();
            var objectives = entities.Where(kv => (long?)kv.Value!["TkbIdentity"]?["TkbType"] == TkbEntityTypes.Objective).ToList();
            Assert.Equal(5, objectives.Count);
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            var opts = BehaviorParams.JsonOptions;
            string squad = entities.First(kv => (long?)kv.Value!["TkbIdentity"]?["TkbType"] == 302).Key;
            string root = objectives.First(kv => (string)kv.Value!["Behavior"]!["Name"]! == "WeightedMean").Key;
            foreach (var (guid, e) in objectives)
            {
                string name = (string)e!["Behavior"]!["Name"]!, json = (string)e["Behavior"]!["Params"]!;
                Assert.True(registry.TryGetId(name, out _), name);
                ObjectiveCommon common = name switch
                {
                    "WeightedMean" => JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.WeightedMeanParamsJsonDto>(json, opts).Common,
                    "ReachArea" => JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.ReachAreaParamsJsonDto>(json, opts).Common,
                    "HoldArea" => JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.HoldAreaParamsJsonDto>(json, opts).Common,
                    "NoHarm" => JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.NoHarmParamsJsonDto>(json, opts).Common,
                    _ => throw new Xunit.Sdk.XunitException("unexpected kind " + name),
                };
                Assert.Equal(System.Guid.Parse(squad), common.Subject.Spec.Exact);
                if (guid != root) Assert.Equal(System.Guid.Parse(root), common.Parent.Spec.Exact);
                if (name is "ReachArea" or "HoldArea")
                {
                    var area = name == "ReachArea" ? JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.ReachAreaParamsJsonDto>(json, opts).Area
                                                   : JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.HoldAreaParamsJsonDto>(json, opts).Area;
                    Assert.Equal(RefForm.Circle, area.Spec.Form);
                }
            }
        }

        /// <summary>Rail 7 — the SAME kind twice with different params side by side: independent states.</summary>
        [Fact]
        public void TwoHoldAreas_WithDifferentAreas_RunIndependently()
        {
            var w = new ObjWorld();
            var men = new[] { w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend) };
            var squad = w.Squad(ForceId.Friend, men);
            var near = w.Objective("HoldArea", $"{{{Common(w, squad)},\"area\":{Circle0},\"minMen\":2,\"holdSeconds\":4}}");
            var far = w.Objective("HoldArea", $"{{{Common(w, squad)},\"area\":{{\"circle\":{{\"center\":[500,500,0],\"radius\":10}}}},\"minMen\":2,\"holdSeconds\":4}}");
            w.Run(8);
            Assert.Equal((ObjectiveState.Achieved, ObjectiveState.Active), (w.S(near).State, w.S(far).State));
        }

        /// <summary>Rail 8 — the params DTOs round-trip through <c>BehaviorParams.JsonOptions</c> with a nested <c>"common"</c>, a circle and an
        /// <c>"@Village"</c> area; the zero defaults stay zero.</summary>
        [Fact]
        public void TheParamsDtos_RoundTrip_WithCommonAndAreaForms()
        {
            var opts = BehaviorParams.JsonOptions;
            const string circle = "{\"common\":{\"title\":\"Hold\",\"subject\":77,\"required\":true,\"weight\":2,\"deadlineSeconds\":90,\"visibleSides\":6},\"area\":{\"circle\":{\"center\":[1,2,0],\"radius\":25}},\"minMen\":5,\"capture\":true}";
            var dto = JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.HoldAreaParamsJsonDto>(circle, opts);
            Assert.Equal(("Hold", 77, true, 2f, 90f, (byte)6), (dto.Common.Title, dto.Common.Subject.Ref.NetworkId, dto.Common.Required, dto.Common.Weight, dto.Common.DeadlineSeconds, dto.Common.VisibleSides));
            Assert.Equal((RefForm.Circle, 25f, 5, true), (dto.Area.Spec.Form, dto.Area.Size.X, dto.MinMen, dto.Capture));
            Assert.Null(dto.Common.Scored); Assert.Null(dto.Common.StartActive);                  // absent = the default (true), applied by the block
            var block = ObjectiveCommonBlock.From(in dto.Common);
            Assert.Equal((1, 1, 6), ((int)block.Scored, (int)block.StartActive, (int)block.VisibleSides));

            var back = JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.HoldAreaParamsJsonDto>(JsonSerializer.Serialize(dto, opts), opts);
            Assert.Equal(dto.Area, back.Area); Assert.Equal(dto.Common.Subject.Ref, back.Common.Subject.Ref); Assert.Equal(dto.Common.Title, back.Common.Title);

            var village = JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.ReachAreaParamsJsonDto>("{\"common\":{\"title\":\"R\"},\"area\":\"@Village\"}", opts);
            Assert.Equal((RefForm.Key, "Village", 0), (village.Area.Spec.Form, village.Area.Spec.KeyText, village.MinMen));
        }
    }
}
