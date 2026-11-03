using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Tkb;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ rules S4 E-1 (📄 docs/DESIGN_Scenario_Rules.md §00.11; SR-72 / SR-72a / SR-81) — <c>MemberEvaluation</c>: one macro per roster member, picked by
    /// key → role → class → Default (overrides first at each step), inserted through the ONE <c>MacroInsertion</c> request, following the chain live.</summary>
    public sealed partial class ScenarioRulesTests
    {
        private const string EvalLibrary = "macros/evaluation/infantry";
        private const string EvalFolder = "evaluation/infantry/";

        private static InMemoryMacroLibrary Lib(params string[] paths)
        {
            var l = new InMemoryMacroLibrary();
            foreach (var p in paths) l.Files[p.Contains('/') ? p : EvalFolder + p] = "{}";
            return l;
        }

        private static ObjWorld EvalWorld(IMacroLibrary? lib)
        {
            var w = new ObjWorld();
            AllowCreation(w);
            if (lib != null) w.Repo.SetSingletonManaged<IMacroLibrary>(lib);
            if (!w.Repo.IsComponentTypeRegistered<RefKey>()) w.Repo.RegisterComponent<RefKey>();
            return w;
        }

        /// <summary>A man with a stable id, a role under his commander and (optionally) a key.</summary>
        private static Entity Soldier(ObjWorld w, Vector3 at, TacticalDesignation role = TacticalDesignation.Undefined, string? key = null)
        {
            var e = w.Man(at, ForceId.Friend);
            w.Repo.AddComponent(e, new EntityGuid(Guid.NewGuid()));
            if (role != TacticalDesignation.Undefined) w.Repo.AddComponent(e, new UnitSubordinate { Designation = role });
            if (key != null) w.Repo.AddComponent(e, new RefKey(key));
            return e;
        }

        private static Entity Evaluation(ObjWorld w, Entity commander, string? overrides = null, string library = EvalLibrary)
        {
            var e = w.Objective("MemberEvaluation",
                $"{{\"common\":{{\"title\":\"E\",\"subject\":{w.Net(commander)}}},\"library\":\"{library}\"{(overrides != null ? ",\"overrides\":" + overrides : "")}}}");
            w.Repo.AddComponent(e, new EntityGuid(Guid.NewGuid()));
            return e;
        }

        private static string MacroOf(EntityCreationRequest r) => r.InitialComponents!.OfType<MacroInstance>().Single().Macro;
        private static JsonObject ArgsOf(EntityCreationRequest r) => JsonNode.Parse(r.InitialComponents!.OfType<MacroInstance>().Single().ArgsJson)!.AsObject();
        private static Guid GuidOf(ObjWorld w, Entity e) => w.Repo.GetComponentRO<EntityGuid>(e).Value;

        /// <summary>⭐ E-1 — a platoon commander's evaluation inserts ONE macro per member of his roster, chosen by role (the squad leaders get <c>SquadLeader</c>, the man with
        /// no role <c>Default</c>), at the member, through the ONE request (transient, derived distinct GUIDs), args <c>{subject: member, parent: the evaluation}</c>; and asks
        /// ONCE (more ticks add nothing). 🔴 Red-proof: <c>IsServed</c> returning false ⇒ the count grows every tick.</summary>
        [Fact]
        public void MemberEvaluation_InsertsOneMacroPerMember_ByRole_AtTheMember_AndAsksOnce()
        {
            var w = EvalWorld(Lib("SquadLeader", "Default"));
            var l1 = Soldier(w, new Vector3(10, 20, 0), TacticalDesignation.SquadLeader);
            var l2 = Soldier(w, new Vector3(50, 60, 0), TacticalDesignation.SquadLeader);
            var rifle = Soldier(w, new Vector3(5, 5, 0));
            var platoon = w.Squad(ForceId.Friend, l1, l2, rifle);
            var eval = Evaluation(w, platoon);
            w.Run(4);
            Assert.Equal(3, w.Created.Count);
            Assert.Equal(new[] { EvalFolder + "Default", EvalFolder + "SquadLeader", EvalFolder + "SquadLeader" }, w.Created.Select(MacroOf).OrderBy(m => m, StringComparer.Ordinal).ToArray());
            Assert.All(w.Created, r => Assert.True(r.IsTransient));
            Assert.Equal(3, w.Created.Select(r => r.InitialComponents!.OfType<EntityGuid>().Single().Value).Distinct().Count());   // SR-167: derived per member, never one id
            var forL1 = w.Created.Single(r => ArgsOf(r)["subject"]!.GetValue<string>() == GuidOf(w, l1).ToString("D"));
            Assert.Equal(GuidOf(w, eval).ToString("D"), ArgsOf(forL1)["parent"]!.GetValue<string>());
            var at = forL1.InitialComponents!.OfType<SimTransform>().Single();
            Assert.Equal((10f, 20f), (at.Position.X, at.Position.Y));
            Assert.Equal("MemberEvaluation", w.S(eval).Kind.ToString());
            Assert.Equal(ObjectiveRole.Objective, w.S(eval).Role);
            w.Run(6);
            Assert.Equal(3, w.Created.Count);
        }

        /// <summary>⭐ E-1 / SR-72 — it follows the chain LIVE: a man added to the roster later gets one (and only the new one); a man who dies keeps his instance (nothing is
        /// removed, nothing re-asked) and the evaluation still counts what that instance reported. 🔴 Red-proof: serving only on the first tick ⇒ no instance for the late man.</summary>
        [Fact]
        public void MemberEvaluation_AMemberAddedLater_GetsOne_ADeadMembersInstanceStays()
        {
            var w = EvalWorld(Lib("Default"));
            var a = Soldier(w, new Vector3(1, 1, 0)); var b = Soldier(w, new Vector3(2, 2, 0));
            var squad = w.Squad(ForceId.Friend, a, b);
            var eval = Evaluation(w, squad);
            w.Run(3);
            Assert.Equal(2, w.Created.Count);
            // the creating node, in miniature: the instance's root objective, parented to the evaluation, settles for the man who will die
            var open = w.Objective("ReachArea", $"{{\"common\":{{\"title\":\"a\",\"subject\":{w.Net(a)},\"parent\":{w.Net(eval)}}},\"area\":{Circle0},\"minMen\":9}}");   // a's standard is still running (never met)
            var forB = w.Objective("ReachArea", $"{{\"common\":{{\"title\":\"b\",\"subject\":{w.Net(b)},\"parent\":{w.Net(eval)}}},\"area\":{Circle0}}}");
            w.Move(b, Inside);
            w.Run(2);
            Assert.Equal(ObjectiveState.Achieved, w.S(forB).State);

            var late = Soldier(w, new Vector3(9, 9, 0));
            ref var roster = ref w.Repo.GetComponentRW<UnitRoster>(squad);
            UnitRoster.Add(ref roster, late);
            w.Run(3);
            Assert.Equal(3, w.Created.Count);
            Assert.Equal(GuidOf(w, late).ToString("D"), ArgsOf(w.Created[2])["subject"]!.GetValue<string>());

            w.Repo.DestroyEntity(b);
            w.Run(4);
            Assert.Equal(3, w.Created.Count);                                    // nobody re-asked for the dead man
            Assert.Equal(ObjectiveState.Achieved, w.S(forB).State);              // his instance's result stays
            Assert.Equal(1, w.S(eval).ProgressCur);                              // …and the evaluation still counts it
        }

        /// <summary>⭐ E-1 / SR-81 — the lookup ORDER, first that EXISTS wins: <c>@RefKey</c> → role → entity class (the member's TKB template name) → <c>Default</c>. Each file is
        /// removed in turn and the next one is picked. 🔴 Red-proof: the order of <c>Names</c> changed (role before key) ⇒ the first row reds.</summary>
        [Fact]
        public void MemberEvaluation_TheLookupOrder_KeyThenRoleThenClassThenDefault()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("Sniper", 9001));
            var chain = new[] { "PL-1", "SquadLeader", "Sniper", "Default" };
            for (int cut = 0; cut < chain.Length; cut++)
            {
                var w = EvalWorld(Lib(chain.Skip(cut).ToArray()));
                w.Repo.SetSingletonManaged<ITkbDatabase>(db);
                var m = Soldier(w, new Vector3(1, 1, 0), TacticalDesignation.SquadLeader, "PL-1");
                w.Repo.AddComponent(m, new TkbIdentity { TkbType = (TkbTypeId)9001 });
                Evaluation(w, w.Squad(ForceId.Friend, m));
                w.Run(3);
                Assert.Equal(EvalFolder + chain[cut], MacroOf(Assert.Single(w.Created)));
            }
        }

        /// <summary>⭐ E-1 / SR-81 — <c>overrides</c> win: <c>"@Key"</c> over the library's key file, a role name over the library's role file; an override naming a macro the library
        /// does NOT have falls through to the library's own order. 🔴 Red-proof: overrides not read ⇒ the library's macros are picked.</summary>
        [Fact]
        public void MemberEvaluation_OverridesWin_AMissingOverrideFallsThrough()
        {
            var w = EvalWorld(Lib("SquadLeader", "Medic", "Default", "evaluation/special/PL", "evaluation/special/Medic"));
            var pl = Soldier(w, new Vector3(1, 1, 0), TacticalDesignation.SquadLeader, "PL");
            var medic = Soldier(w, new Vector3(2, 2, 0), TacticalDesignation.Medic);
            var sl = Soldier(w, new Vector3(3, 3, 0), TacticalDesignation.SquadLeader);
            var gone = Soldier(w, new Vector3(4, 4, 0), TacticalDesignation.TeamLeader);
            Evaluation(w, w.Squad(ForceId.Friend, pl, medic, sl, gone),
                "{\"@PL\":\"macros/evaluation/special/PL\",\"Medic\":\"evaluation/special/Medic\",\"TeamLeader\":\"evaluation/special/Nowhere\"}");
            w.Run(3);
            string For(Entity e) => MacroOf(w.Created.Single(r => ArgsOf(r)["subject"]!.GetValue<string>() == GuidOf(w, e).ToString("D")));
            Assert.Equal("evaluation/special/PL", For(pl));
            Assert.Equal("evaluation/special/Medic", For(medic));
            Assert.Equal(EvalFolder + "SquadLeader", For(sl));
            Assert.Equal(EvalFolder + "Default", For(gone));
        }

        /// <summary>⭐ E-1 / SR-81 — a member no file covers (not even Default) is a REASON on the evaluation, not an error: nothing is inserted for him, the rest are, and he
        /// is not asked about again every tick. A node with no macro library says so.</summary>
        [Fact]
        public void MemberEvaluation_AMemberWithNoMacro_IsAReasonLine_NotAnError()
        {
            var w = EvalWorld(Lib("SquadLeader"));
            var sl = Soldier(w, new Vector3(1, 1, 0), TacticalDesignation.SquadLeader);
            var plain = Soldier(w, new Vector3(2, 2, 0));
            var eval = Evaluation(w, w.Squad(ForceId.Friend, sl, plain));
            w.Run(4);
            Assert.Equal(EvalFolder + "SquadLeader", MacroOf(Assert.Single(w.Created)));
            Assert.Contains("1 member(s) without a macro", w.S(eval).Reason.ToString());
            Assert.NotEqual(ObjectiveState.Failed, w.S(eval).State);

            var bare = EvalWorld(null);
            var eval2 = Evaluation(bare, bare.Squad(ForceId.Friend, Soldier(bare, new Vector3(1, 1, 0))));
            bare.Run(3);
            Assert.Empty(bare.Created);
            Assert.Contains("no macro library", bare.S(eval2).Reason.ToString());
        }

        /// <summary>⭐ E-1 — the verdict is the ONE weighted-mean code: an evaluation and a <c>WeightedMean</c> over the same children report the same state, score and progress.
        /// 🔴 Red-proof: <c>MemberEvaluation</c> aggregating with its own sum ⇒ the scores differ.</summary>
        [Fact]
        public void MemberEvaluation_AggregatesLikeAWeightedMean()
        {
            var w = EvalWorld(Lib("Default"));
            var squad = w.Squad(ForceId.Friend, Soldier(w, new Vector3(1, 1, 0)));
            var eval = Evaluation(w, squad);
            var mean = w.Objective("WeightedMean", "{\"common\":{\"title\":\"M\"}}");
            var men = new[] { w.Man(Inside, ForceId.Friend), w.Man(Outside, ForceId.Friend) };
            var group = w.Squad(ForceId.Friend, men);
            foreach (var parent in new[] { eval, mean })
            {
                w.Objective("ReachArea", $"{{\"common\":{{\"title\":\"c1\",\"subject\":{w.Net(group)},\"parent\":{w.Net(parent)}}},\"area\":{Circle0},\"minMen\":1}}");
                w.Objective("ReachArea", $"{{\"common\":{{\"title\":\"c2\",\"subject\":{w.Net(group)},\"parent\":{w.Net(parent)},\"weight\":3}},\"area\":{Circle0},\"minMen\":2}}");
            }
            w.Run(3);
            var (a, b) = (w.S(eval), w.S(mean));
            Assert.Equal((b.State, b.Score, b.ProgressCur, b.ProgressNeed), (a.State, a.Score, a.ProgressCur, a.ProgressNeed));
            Assert.InRange(a.Score, 0.24f, 0.26f);   // (1·1 + 3·0) / 4
        }

        /// <summary>⭐ E-1 / SR-72 — the SHIPPED sample library <c>macros/evaluation/infantry</c> through the real file library: a platoon's squad leader gets <c>SquadLeader</c>, the
        /// others <c>Default</c>; and the <c>SquadLeader</c> file's nested <c>MemberEvaluation</c> (its params read FROM the file, <c>^subject</c> / <c>^parent</c> bound as the expander would)
        /// serves the leader's own men — the recursion. Both files hold the standards root (WeightedMean) and a required NoHarm.</summary>
        [Fact]
        public void TheSampleLibrary_Infantry_ServesTheChain_AndTheSquadLeaderMacroRecurses()
        {
            var files = FileMacroLibrary.ForNode(null);
            Assert.True(files.TryRead("evaluation/infantry/SquadLeader", out var slJson, out var why1), why1);
            Assert.True(files.TryRead("evaluation/infantry/Default", out var defJson, out var why2), why2);
            JsonObject Behaviours(string json) => JsonNode.Parse(json)!["entities"]!.AsObject();
            foreach (var json in new[] { slJson, defJson })
            {
                var kinds = Behaviours(json).Select(kv => kv.Value!["Behavior"]!["Name"]!.GetValue<string>()).ToList();
                Assert.Contains("WeightedMean", kinds); Assert.Contains("NoHarm", kinds);
                Assert.Contains("HitRatio", kinds); Assert.Contains("ReportLatency", kinds);   // ⭐ S4 E-2: the measures
                // every measure's params parse as the kind's own DTO, carry a group, and hang under the root
                foreach (var kv in Behaviours(json))
                {
                    var b = kv.Value!["Behavior"]!; string kind = b["Name"]!.GetValue<string>();
                    if (kind is not ("HitRatio" or "Count" or "ReportLatency")) continue;
                    var common = JsonNode.Parse(b["Params"]!.GetValue<string>())!["common"]!;
                    Assert.False(string.IsNullOrEmpty(common["group"]?.GetValue<string>()), kind);
                    Assert.NotNull(common["parent"]);
                }
            }
            Assert.Contains("reports", Behaviours(slJson).Select(kv => kv.Value!["Behavior"]!["Params"]!.GetValue<string>()).Where(p => p.Contains("\"what\"")).Single(p => p.Contains("reports")));
            var nested = Behaviours(slJson).Select(kv => kv.Value!["Behavior"]!).Single(b => b["Name"]!.GetValue<string>() == "MemberEvaluation");

            var w = EvalWorld(files);
            var men = new[] { Soldier(w, new Vector3(1, 1, 0)), Soldier(w, new Vector3(2, 2, 0)) };
            var leader = Soldier(w, new Vector3(0, 0, 0), TacticalDesignation.SquadLeader);
            var leaderRoster = w.Squad(ForceId.Friend, men);   // the leader's own roster, on a unit body of his
            w.Repo.AddComponent(leader, w.Repo.GetComponentRO<UnitRoster>(leaderRoster));
            var platoon = w.Squad(ForceId.Friend, leader, Soldier(w, new Vector3(7, 7, 0)));
            Evaluation(w, platoon);
            w.Run(3);
            Assert.Equal(new[] { EvalFolder + "Default", EvalFolder + "SquadLeader" }, w.Created.Select(MacroOf).Where(m => m != MeanMacro).OrderBy(m => m, StringComparer.Ordinal).ToArray());   // ⭐ S4 ①: …and the means (the file library ships that macro)

            // the macro's nested evaluation as the expander would have bound it: subject = the leader
            string bound = nested["Params"]!.GetValue<string>().Replace("^subject", GuidOf(w, leader).ToString("D"));
            var pj = JsonNode.Parse(bound)!;
            Assert.Equal("macros/evaluation/infantry", pj["library"]!.GetValue<string>());
            var inner = w.Objective("MemberEvaluation", $"{{\"common\":{{\"title\":\"Squad's men\",\"subject\":{w.Net(leader)}}},\"library\":\"{pj["library"]!.GetValue<string>()}\"}}");
            w.Repo.AddComponent(inner, new EntityGuid(Guid.NewGuid()));
            int before = w.Created.Count;
            w.Run(3);
            var served = w.Created.Skip(before).Where(r => MacroOf(r) != MeanMacro).ToList();
            Assert.Equal(2, served.Count);
            Assert.All(served, r => Assert.Equal(EvalFolder + "Default", MacroOf(r)));
            Assert.Equal(men.Select(m => GuidOf(w, m).ToString("D")).OrderBy(g => g), served.Select(r => ArgsOf(r)["subject"]!.GetValue<string>()).OrderBy(g => g));
        }

        // ───────────── ⭐ rules S4 ① — SelectionMean: the role composites and the squad's mean of a standard, by SELECTION (§00.11) ─────────────

        private const string MeanMacro = Hrot.AI.Behaviors.Brains.Objectives.MemberEvaluationBehavior.MeanMacro;

        private static Entity MemberRoot(ObjWorld w, Entity subject, Entity parent)
            => w.Objective("WeightedMean", $"{{\"common\":{{\"title\":\"Member standards\",\"subject\":{w.Net(subject)},\"parent\":{w.Net(parent)}}}}}");

        /// <summary>A standard: a ReachArea with one man inside (score 1) or outside (score 0).</summary>
        private static Entity Standard(ObjWorld w, Entity parent, bool met, string title = "Reach", string group = "fire")
        {
            var squad = w.Squad(ForceId.Friend, w.Man(met ? Inside : Outside, ForceId.Friend));
            return w.Objective("ReachArea", $"{{\"common\":{{\"title\":\"{title}\",\"group\":\"{group}\",\"subject\":{w.Net(squad)},\"parent\":{w.Net(parent)}}},\"area\":{Circle0},\"minMen\":1}}");
        }

        /// <summary>The rail plays the creating node for the evaluation's MEAN requests: each new request for <see cref="MeanMacro"/> becomes the SelectionMean it would expand to
        /// (the shipped macro's params, ids bound). Returns every mean spawned so far, by <c>role:&lt;key&gt;</c> / <c>standard:&lt;key&gt;</c>.</summary>
        private static Dictionary<string, Entity> SpawnMeans(ObjWorld w, Entity eval, Dictionary<string, Entity> spawned)
        {
            foreach (var r in w.Created.Where(r => MacroOf(r) == MeanMacro))
            {
                var a = ArgsOf(r);
                string id = a["by"]!.GetValue<string>() + ":" + a["key"]!.GetValue<string>();
                if (a["scope"]!.GetValue<string>() != GuidOf(w, eval).ToString("D") || spawned.ContainsKey(id)) continue;
                spawned[id] = w.Objective("SelectionMean",
                    $"{{\"common\":{{\"title\":\"{a["title"]!.GetValue<string>()}\",\"parent\":{w.Net(eval)},\"group\":\"{a["group"]!.GetValue<string>()}\",\"scored\":false}},\"by\":\"{a["by"]!.GetValue<string>()}\",\"key\":\"{a["key"]!.GetValue<string>()}\",\"scope\":{w.Net(eval)}}}");
            }
            return spawned;
        }

        private sealed record Platoon(Entity Eval, Entity Sl1, Entity Sl2, Entity Mg, Entity Root1, Entity Root2, Entity RootMg, Entity Commander);

        /// <summary>A platoon (roster: two squad leaders + a machine gunner), the evaluation over it, and the roots its macros would have made (the rail plays the creating node):
        /// each root holds a "Reach" standard (SL1 met, SL2 not, MG met); SL1's root also holds a NESTED evaluation over his squad, whose two men's "Reach" are both unmet.</summary>
        private static Platoon BuildPlatoon(ObjWorld w)
        {
            var sl1 = Soldier(w, new Vector3(1, 1, 0), TacticalDesignation.SquadLeader); var sl2 = Soldier(w, new Vector3(2, 2, 0), TacticalDesignation.SquadLeader);
            var mg = Soldier(w, new Vector3(3, 3, 0), TacticalDesignation.MachineGunner);
            var cmd = w.Squad(ForceId.Friend, sl1, sl2, mg);
            var eval = Evaluation(w, cmd);
            var r1 = MemberRoot(w, sl1, eval); var r2 = MemberRoot(w, sl2, eval); var rm = MemberRoot(w, mg, eval);
            Standard(w, r1, true); Standard(w, r2, false); Standard(w, rm, true);
            var nested = w.Objective("MemberEvaluation", $"{{\"common\":{{\"title\":\"Squad's men\",\"subject\":{w.Net(sl1)},\"parent\":{w.Net(r1)},\"scored\":false}},\"library\":\"{EvalLibrary}\"}}");
            var squadMen = new[] { Soldier(w, new Vector3(5, 5, 0), TacticalDesignation.AutomaticRifleman), Soldier(w, new Vector3(6, 6, 0), TacticalDesignation.AutomaticRifleman) };
            foreach (var m in squadMen) Standard(w, MemberRoot(w, m, nested), false);
            return new Platoon(eval, sl1, sl2, mg, r1, r2, rm, cmd);
        }

        /// <summary>⭐ S4 ① — a platoon with two squads: ONE role mean per role of the evaluation's men (inserted through the ONE <c>MacroInsertion</c> request, args by / key / scope / parent / title /
        /// group, asked ONCE), each with the right member count and the right mean — "All SquadLeader" = (1 + 0) / 2 over the two leaders' roots, "All MachineGunner" = the gunner's. Group of a role mean:
        /// <c>roles</c>. 🔴 Red-proof: the role predicate counting a non-member (every root of the scope) ⇒ the SquadLeader count / mean reds.</summary>
        [Fact]
        public void SelectionMean_OneRoleMeanPerRole_WithTheRightMembersAndMean()
        {
            var w = EvalWorld(Lib("Default", "evaluation/selection-mean"));
            var p = BuildPlatoon(w);
            w.Run(3);
            var means = SpawnMeans(w, p.Eval, new());
            int asked = w.Created.Count(r => MacroOf(r) == MeanMacro);
            w.Run(4);
            Assert.Equal(asked, w.Created.Count(r => MacroOf(r) == MeanMacro));   // asked ONCE
            Assert.Contains("role:SquadLeader", means.Keys); Assert.Contains("role:MachineGunner", means.Keys);
            Assert.Equal(2, means.Keys.Count(k => k.StartsWith("role:")));        // two roles of the evaluation's own men — not the nested squad's
            var sl = w.S(means["role:SquadLeader"]); var mg = w.S(means["role:MachineGunner"]);
            Assert.Equal("All SquadLeader", sl.Title.ToString());
            Assert.Equal("roles", sl.Group.ToString());
            Assert.Equal(("SelectionMean", 2, 0.5f), (sl.Kind.ToString(), sl.ProgressNeed, sl.Score));
            Assert.Equal((1, 1f), (mg.ProgressNeed, mg.Score));
            Assert.Equal(0.6f, sl.Pass);   // the one pass mark: PassOf adds the kind
            var r = w.Created.Single(r => MacroOf(r) == MeanMacro && ArgsOf(r)["key"]!.GetValue<string>() == "SquadLeader");
            Assert.True(r.IsTransient);
            Assert.Equal(GuidOf(w, p.Eval).ToString("D"), ArgsOf(r)["parent"]!.GetValue<string>());
        }

        /// <summary>⭐ S4 ① — it follows the chain LIVE: a reinforcement of an existing role joins that role's mean (count + 1, NO new composite, no new request); a man of a NEW role gets a
        /// new composite. A casualty's root stays in his role's mean after he is gone. 🔴 Red-proof: the served-means set not kept (re-asking every tick) or the role key not looked up for a late man.</summary>
        [Fact]
        public void SelectionMean_AReinforcementJoinsItsRole_ANewRoleGetsAComposite_ACasualtyStays()
        {
            var w = EvalWorld(Lib("Default", "evaluation/selection-mean"));
            var p = BuildPlatoon(w);
            w.Run(3);
            var means = SpawnMeans(w, p.Eval, new());
            w.Run(2);
            int asked = w.Created.Count(r => MacroOf(r) == MeanMacro);
            Assert.Equal(2, w.S(means["role:SquadLeader"]).ProgressNeed);

            var late = Soldier(w, new Vector3(9, 9, 0), TacticalDesignation.SquadLeader);
            ref var roster = ref w.Repo.GetComponentRW<UnitRoster>(p.Commander);
            UnitRoster.Add(ref roster, late);
            Standard(w, MemberRoot(w, late, p.Eval), true);   // the instance his macro would make
            w.Run(3);
            Assert.Equal(asked, w.Created.Count(r => MacroOf(r) == MeanMacro));   // no new composite for an existing role
            Assert.Equal(3, w.S(means["role:SquadLeader"]).ProgressNeed);
            Assert.InRange(w.S(means["role:SquadLeader"]).Score, 0.66f, 0.67f);   // (1 + 0 + 1) / 3

            var medic = Soldier(w, new Vector3(8, 8, 0), TacticalDesignation.Medic);
            UnitRoster.Add(ref roster, medic);
            Standard(w, MemberRoot(w, medic, p.Eval), true);
            w.Run(3);
            SpawnMeans(w, p.Eval, means);
            Assert.Contains("role:Medic", means.Keys);
            Assert.Equal(asked + 1, w.Created.Count(r => MacroOf(r) == MeanMacro));
            w.Run(3);
            Assert.Equal(1, w.S(means["role:Medic"]).ProgressNeed);

            w.Repo.DestroyEntity(p.Sl2);                                          // a casualty: his subject is gone, his root stays
            w.Run(4);
            Assert.Equal(3, w.S(means["role:SquadLeader"]).ProgressNeed);
        }

        /// <summary>⭐ S4 ① — a STANDARD mean is the mean of that standard across the men under the evaluation (here "Reach": 1, 0, 1 ⇒ 2/3, three men) and EXCLUDES a nested squad's men (their two unmet
        /// "Reach" would make it 2/5); its group is the standard's own. 🔴 Red-proof: <c>IsStandard</c> not stopping at a nested <c>MemberEvaluation</c> ⇒ 5 members, 0.4.</summary>
        [Fact]
        public void SelectionMean_TheStandardMean_IsTheMeanAcrossTheMen_AndExcludesANestedSquad()
        {
            var w = EvalWorld(Lib("Default", "evaluation/selection-mean"));
            var p = BuildPlatoon(w);
            w.Run(3);
            var means = SpawnMeans(w, p.Eval, new());
            w.Run(4);
            var reach = w.S(means["standard:Reach"]);
            Assert.Equal("Reach (mean)", reach.Title.ToString());
            Assert.Equal("fire", reach.Group.ToString());
            Assert.Equal(3, reach.ProgressNeed);
            Assert.InRange(reach.Score, 0.66f, 0.67f);
            Assert.Single(means.Keys.Where(k => k.StartsWith("standard:")));      // one per distinct title (the composites' own titles are not standards)
        }

        /// <summary>⭐ S4 ⑤ — a STANDARD mean carries the standards' own Value and Target as means (the debrief's "squad's mean of the same measure" beside the man's Value / Target): three men's "Pool"
        /// counters 10 / 20 / 30 (target 40) ⇒ "Pool (mean)" Value 20, Target 40; a ROLE mean keeps its Value = its Score. 🔴 Red-proof: <c>RawMeans</c> not applied ⇒ Value = the score, not 20.</summary>
        [Fact]
        public void SelectionMean_AStandardMean_CarriesTheMeanValueAndTarget()
        {
            var w = EvalWorld(Lib("Default", "evaluation/selection-mean"));
            var sls = new[] { 10, 20, 30 }.Select(_ => Soldier(w, new Vector3(1, 1, 0), TacticalDesignation.SquadLeader)).ToArray();
            var eval = Evaluation(w, w.Squad(ForceId.Friend, sls));
            int start = 10;
            foreach (var m in sls)
            {
                var root = MemberRoot(w, m, eval);
                w.Objective("Counter", $"{{\"common\":{{\"title\":\"Pool\",\"parent\":{w.Net(root)}}},\"start\":{start}}}");
                start += 10;
            }
            w.Run(3);
            var means = SpawnMeans(w, eval, new());
            w.Run(4);
            var pool = w.S(means["standard:Pool"]);
            Assert.Equal(3, pool.ProgressNeed);
            Assert.Equal(20f, pool.Value, 3);
            Assert.NotEqual(pool.Score, pool.Value);
            Assert.Equal(w.S(means["role:SquadLeader"]).Score, w.S(means["role:SquadLeader"]).Value);
        }

        /// <summary>⭐ S4 ① — the means are <c>scored:false</c>: the evaluation's own Score, State and weight are what its member roots alone make them (here (1 + 0 + 1) / 3), with the means present or
        /// not. 🔴 Red-proof: the macro's <c>scored</c> dropped (a mean counts as a child with weight 1) ⇒ the score moves.</summary>
        [Fact]
        public void SelectionMean_DoesNotMoveTheEvaluationsOwnScore_ScoredFalse()
        {
            var w = EvalWorld(Lib("Default", "evaluation/selection-mean"));
            var p = BuildPlatoon(w);
            w.Run(4);
            float before = w.S(p.Eval).Score;
            Assert.InRange(before, 0.66f, 0.67f);
            var means = SpawnMeans(w, p.Eval, new());
            w.Run(4);
            Assert.Equal(3, means.Count);
            Assert.All(means.Values, m => Assert.Equal(0, w.S(m).Scored));
            Assert.Equal(before, w.S(p.Eval).Score);
        }

        /// <summary>⭐ S4 ① — a library WITHOUT the mean macro gets no means (and no error): the evaluation behaves as before.</summary>
        [Fact]
        public void SelectionMean_ALibraryWithoutTheMacro_InsertsNone()
        {
            var w = EvalWorld(Lib("Default"));
            BuildPlatoon(w);
            w.Run(4);
            Assert.DoesNotContain(w.Created, r => MacroOf(r) == MeanMacro);
        }

        /// <summary>⭐ S4 ① — the params are checked at assignment: <c>by</c> must be role|standard and <c>key</c> given.</summary>
        [Fact]
        public void SelectionMean_RefusesBadParams()
        {
            Assert.NotEmpty(Hrot.AI.Behaviors.Brains.Objectives.SelectionMeanBehavior.Validate("{\"by\":\"squad\",\"key\":\"x\"}"));
            Assert.NotEmpty(Hrot.AI.Behaviors.Brains.Objectives.SelectionMeanBehavior.Validate("{\"by\":\"role\"}"));
            Assert.Empty(Hrot.AI.Behaviors.Brains.Objectives.SelectionMeanBehavior.Validate("{\"by\":\"role\",\"key\":\"Medic\"}"));
        }

        /// <summary>⭐ S4 ① — the SHIPPED macro <c>evaluation/selection-mean</c> is real: it expands from the file library, takes by / key / scope / parent / title / group, and holds ONE
        /// <c>SelectionMean</c> with <c>scored:false</c> bound to the evaluation.</summary>
        [Fact]
        public void TheShippedSelectionMeanMacro_HoldsOneSelectionMean_ScoredFalse()
        {
            var files = FileMacroLibrary.ForNode(null);
            Assert.True(files.TryRead(MeanMacro, out var json, out var why), why);
            var ents = JsonNode.Parse(json)!["entities"]!.AsObject();
            var b = Assert.Single(ents).Value!["Behavior"]!;
            Assert.Equal("SelectionMean", b["Name"]!.GetValue<string>());
            var pj = JsonNode.Parse(b["Params"]!.GetValue<string>())!;
            Assert.Equal(("^by", "^key", "^scope", "^parent"), (pj["by"]!.GetValue<string>(), pj["key"]!.GetValue<string>(), pj["scope"]!.GetValue<string>(), pj["common"]!["parent"]!.GetValue<string>()));
            Assert.False(pj["common"]!["scored"]!.GetValue<bool>());
        }
    }
}