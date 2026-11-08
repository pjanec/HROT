using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.AI.Behaviors.Brains.Objectives.Verbs;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐⭐ rules S4 E-1 (📄 docs/DESIGN_Scenario_Rules.md §00.11; SR-72 / SR-72a / SR-81) — <c>MemberEvaluation</c>: a composite over the men of its subject's
    /// <see cref="UnitRoster"/> (ONE level — a role's macro may itself hold a <c>MemberEvaluation</c> over its own subject's roster). Every tick, for a member it
    /// has not served, it picks ONE macro by SR-81 — <c>overrides[@RefKey]</c> → <c>&lt;library&gt;/&lt;RefKey&gt;</c> → <c>&lt;library&gt;/&lt;TacticalDesignation&gt;</c> →
    /// <c>&lt;library&gt;/&lt;entity class&gt;</c> → <c>&lt;library&gt;/Default</c>, the first the node's macro library HAS — and inserts it THROUGH <see cref="MacroInsertion"/>
    /// (the one request <c>InsertMacro</c> publishes) at the member, args <c>{subject: member, parent: this}</c>. The inserted roots are its children, so it
    /// follows the chain LIVE: a reinforcement gets an instance, a casualty's instance STAYS and keeps its result. Its verdict is the ONE weighted-mean code
    /// (<see cref="WeightedMeanBehavior.Aggregate"/>).
    /// </summary>
    public static class MemberEvaluationBehavior
    {
        public const string Kind = BehaviorNames.MemberEvaluation;

        /// <summary>The overrides JSON lives in <see cref="ActionArgs"/> item 0 (the way the Trigger keeps its lists).</summary>
        public const int OverridesItem = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            /// <summary>The library folder under <c>macros/</c> (no leading <c>macros/</c>).</summary>
            public FixedString64 Library;
            /// <summary>Working state (recorded with the blackboard): the members already served (net ids, 16 slots — a roster holds 16), and how many.</summary>
            public Ids16 Served; public int ServedCount;
            /// <summary>How many members no macro covered (each is asked about once): the status says so.</summary>
            public int Unmatched;
            /// <summary>⭐ S4 ① — the means already inserted: role keys and standard titles (FNV-1a hashes, ≤ <see cref="MeansCap"/> of each; a full set inserts no more and says so).</summary>
            public Ids16 RoleMeans, StandardMeans; public int RoleMeanCount, StandardMeanCount;
            /// <summary>⭐ S7 P-5 — <c>members</c> (0 roster · 1 side) and <c>aggregate</c> (0 mean · 1 points).</summary>
            public byte Members, Aggregate;
        }

        /// <summary>The cap of role means, and of standard means, one evaluation keeps.</summary>
        public const int MeansCap = 16;
        /// <summary>The shipped macro (<c>macros/evaluation/selection-mean</c>) that is one <see cref="SelectionMeanBehavior"/>.</summary>
        public const string MeanMacro = "evaluation/selection-mean";

        [StructLayout(LayoutKind.Sequential)]
        public struct MemberEvaluationBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.MemberEvaluation)]
        public static void Resolve(in MemberEvaluationParamsJsonDto authored, ref MemberEvaluationBlackboard bb, EntityRepository world, Entity self)
        {
            var problems = ValidateParams(in authored);
            if (problems.Count > 0) throw new InvalidOperationException("MemberEvaluation: " + string.Join("; ", problems));
            bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in authored.Common), Library = new FixedString64(Folder(authored.Library)),
                Members = (byte)(Is(authored.Members, "side") ? 1 : 0), Aggregate = (byte)(Is(authored.Aggregate, "points") ? 1 : 0),
            };
            string overrides = authored.Overrides is { Count: > 0 } ? JsonSerializer.Serialize(authored.Overrides, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed) : "{}";
            if (world != null && world.IsAlive(self)) world.SetManagedComponent(self, new ActionArgs(new[] { overrides }));
        }

        [BehaviorValidator(BehaviorNames.MemberEvaluation)]
        public static IEnumerable<string> Validate(string json)
        {
            MemberEvaluationParamsJsonDto dto;
            try { dto = JsonSerializer.Deserialize<MemberEvaluationParamsJsonDto>(json, BehaviorParams.JsonOptions); }
            catch (JsonException ex) { return new[] { $"params are not valid JSON — {ex.Message}" }; }
            return ValidateParams(in dto);
        }

        private static List<string> ValidateParams(in MemberEvaluationParamsJsonDto a)
        {
            var problems = new List<string>();
            string lib = Folder(a.Library);
            if (lib.Length == 0) problems.Add("'library' is required (a folder under macros/, e.g. macros/evaluation/infantry).");
            else if (lib.Length > 63) problems.Add($"'library' is {lib.Length} characters; at most 63.");
            if (!string.IsNullOrWhiteSpace(a.Members) && !Is(a.Members, "roster") && !Is(a.Members, "side")) problems.Add($"members '{a.Members}': must be 'roster' or 'side'.");
            if (!string.IsNullOrWhiteSpace(a.Aggregate) && !Is(a.Aggregate, "mean") && !Is(a.Aggregate, "points")) problems.Add($"aggregate '{a.Aggregate}': must be 'mean' or 'points'.");
            if (a.Overrides != null)
                foreach (var (k, v) in a.Overrides)
                    if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(Folder(v))) problems.Add($"overrides '{k}': names no macro.");
            return problems;
        }

        private static bool Is(string? value, string name) => string.Equals(value?.Trim(), name, StringComparison.OrdinalIgnoreCase);

        /// <summary>A macro path as the instance names it: no leading <c>macros/</c>, no edge slashes.</summary>
        internal static string Folder(string? path)
        {
            string p = (path ?? "").Trim().Replace('\\', '/').Trim('/');
            return p.StartsWith("macros/", StringComparison.Ordinal) ? p.Substring(7).Trim('/') : p;
        }

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run, ObjectiveHost.CloseMode.Composite);
            if (f.Reset) { b.Served = default; b.ServedCount = 0; b.Unmatched = 0; b.RoleMeans = default; b.StandardMeans = default; b.RoleMeanCount = 0; b.StandardMeanCount = 0; }   // SR-114: a reset clears the working state
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (ObjectiveChildren.Evaluates(in s))
            {
                string note = Serve(ref b, self, world);
                string meansNote = b.Aggregate == 0 ? ServeMeans(ref b, self, world) : "";   // ⭐ S7 P-5: a game score (aggregate points) has no role / standard MEANS — those are the training debrief's (S4 ①)
                if (note.Length == 0) note = meansNote;
                if (note.Length == 0 && b.Unmatched > 0) note = $"{b.Unmatched} member(s) without a macro";
                if (b.Aggregate == 0) WeightedMeanBehavior.Aggregate(world, self, ref s, s.Pass, f.Now, Kind, note);
                else AggregatePoints(world, self, ref s, f.Now, note);
            }
            ObjectiveHost.CloseComposite(world, self, ref s, f.Now);
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐ S7 P-5 — <c>aggregate: points</c>: the team total. <c>Value</c> = Σ the instances' <c>Value</c> (the ONE sum, <see cref="ObjectiveChildren.PointsOf"/>); it stays Active — a score is no pass/fail —
        /// and is Achieved only when the exercise is closing and every instance has closed (so the composite close does not Fail it "not achieved by the end").
        /// </summary>
        private static void AggregatePoints(EntityRepository world, Entity self, ref ObjectiveStatus s, double now, string note)
        {
            s.Value = ObjectiveChildren.PointsOf(world, self, out int n);
            s.Target = 0f; s.Score = 0f;
            var t = ObjectiveChildren.Of(world, self);
            s.ProgressCur = t.Closed; s.ProgressNeed = t.Count;
            bool done = ExerciseResult.PhaseOf(world) != ExercisePhase.Running && t.Closed >= t.Count;
            string reason = $"{s.Value:0.##} points from {n} member(s)";
            ObjectiveHost.Settle(ref s, Kind, done ? ObjectiveState.Achieved : ObjectiveState.Active, note.Length == 0 ? reason : reason + "; " + note, now);
        }

        private static readonly List<Entity> s_side = new();
        private static readonly HashSet<long> s_current = new();

        /// <summary>
        /// Inserts the macro of every member not served yet; returns why it could not (a refused request / no library), else empty. <c>roster</c>: the subject's <see cref="UnitRoster"/>; ⭐ S7 P-5 <c>side</c>:
        /// every man of the subject's side as he appears (<see cref="ObjectiveCensus.MenOfSide"/>) — a respawned bot is in no roster. ONE path: the member list differs, what is done for each does not.
        /// </summary>
        private static string Serve(ref Block b, Entity self, EntityRepository world)
        {
            var subject = b.Common.Subject.Resolve(world);
            if (subject.IsNull || !world.IsAlive(subject)) return "";
            bool bySide = b.Members != 0;
            if (!bySide && !world.HasComponent<UnitRoster>(subject)) return "";
            UnitRoster roster = default;
            s_side.Clear(); s_current.Clear();
            int count;
            if (bySide)
            {
                var side = ObjectiveCensus.SideOf(world, subject);
                if (side == ForceId.Neutral) return "";
                ObjectiveCensus.MenOfSide(world, side, s_side);
                count = s_side.Count;
                foreach (var m in s_side) s_current.Add(EntityRef.Of(world, m).NetworkId);
            }
            else
            {
                roster = world.GetComponentRO<UnitRoster>(subject);
                count = roster.Count;
                for (int i = 0; i < roster.Count; i++) s_current.Add(EntityRef.Of(world, roster.SubordinateEntities[i]).NetworkId);
            }
            string library = b.Library.ToString();
            IMacroLibrary? macros = world.HasSingletonManaged<IMacroLibrary>() ? world.GetSingletonManaged<IMacroLibrary>() : null;
            Dictionary<string, string>? overrides = null;
            string? refused = null;
            for (int i = 0; i < count; i++)
            {
                var member = bySide ? s_side[i] : roster.SubordinateEntities[i];
                if (member.IsNull || !world.IsAlive(member)) continue;
                int nid = EntityRef.Of(world, member).NetworkId;
                if (nid == 0 || IsServed(in b, nid)) continue;
                if (!MakeRoom(ref b)) { refused = "more than 16 members to remember at once"; break; }   // never serve a man we could not remember (he would be served every tick)
                if (macros is null) { refused = "this node has no macro library"; break; }
                overrides ??= ReadOverrides(world, self);
                var role = world.HasComponent<UnitSubordinate>(member) && world.GetComponentRO<UnitSubordinate>(member).Designation != TacticalDesignation.Undefined
                    ? world.GetComponentRO<UnitSubordinate>(member).Designation : bySide ? TacticalDesignation.Undefined : (TacticalDesignation)roster.TacticalDesignations[i];
                string? macro = Pick(world, macros, library, overrides, member, role);
                if (macro is null) { Mark(ref b, nid); b.Unmatched++; continue; }   // asked once: a missing macro is a Reason, not an error
                if (!world.IsComponentTypeRegistered<EntityGuid>() || !world.HasComponent<EntityGuid>(member) || !world.HasComponent<EntityGuid>(self))
                { Mark(ref b, nid); b.Unmatched++; continue; }   // SR-45: a macro takes its subject / parent by their stable ids
                var args = new JsonObject
                {
                    ["subject"] = world.GetComponentRO<EntityGuid>(member).Value.ToString("D"),
                    ["parent"] = world.GetComponentRO<EntityGuid>(self).Value.ToString("D"),
                };
                var at = world.HasComponent<SimTransform>(member) ? world.GetComponentRO<SimTransform>(member).Position : default;
                string? why = MacroInsertion.Request(world, self, macro, at, default, args.ToJsonString(), out _, salt: nid.ToString());
                if (why != null) { refused = why; break; }   // asked again next tick
                Mark(ref b, nid);
            }
            s_side.Clear(); s_current.Clear();
            return refused ?? "";
        }

        /// <summary>
        /// ⭐ rules S4 ① — keeps the SELECTION means: ONE <c>SelectionMean{by: role}</c> titled <c>All &lt;role&gt;</c> per role key of its men, and ONE <c>SelectionMean{by: standard}</c> titled
        /// <c>&lt;title&gt; (mean)</c> per distinct standard title under their roots, each inserted through <see cref="MacroInsertion"/> (the shipped <see cref="MeanMacro"/>) with Parent =
        /// this evaluation and <c>scored: false</c> (the macro's) so the evaluation's own score is unchanged. A library without the macro gets none (nothing to say). Returns a note when capped.
        /// </summary>
        private static string ServeMeans(ref Block b, Entity self, EntityRepository world)
        {
            if (!world.HasSingletonManaged<IMacroLibrary>() || !world.IsComponentTypeRegistered<EntityGuid>() || !world.HasComponent<EntityGuid>(self)) return "";
            var subject = b.Common.Subject.Resolve(world);
            if (subject.IsNull || !world.IsAlive(subject) || !world.HasComponent<UnitRoster>(subject)) return "";
            var macros = world.GetSingletonManaged<IMacroLibrary>();
            if (macros is null || !macros.TryRead(MeanMacro, out _, out _)) return "";
            string guid = world.GetComponentRO<EntityGuid>(self).Value.ToString("D");
            bool capped = false;

            var roster = world.GetComponentRO<UnitRoster>(subject);
            for (int i = 0; i < roster.Count; i++)
            {
                var member = roster.SubordinateEntities[i];
                if (member.IsNull || !world.IsAlive(member) || EntityRef.Of(world, member).NetworkId == 0) continue;
                string role = RoleKey(world, member);
                if (role.Length == 0) continue;
                if (!InsertMean(world, self, guid, "role", role, "All " + role, "roles", ref b.RoleMeans, ref b.RoleMeanCount, ref capped)) return "";
            }
            foreach (var e in world.Query().With<ObjectiveLinks>().With<ObjectiveStatus>().Build())
            {
                if (!SelectionMeanBehavior.IsStandard(world, e, self)) continue;
                ref readonly var st = ref world.GetComponentRO<ObjectiveStatus>(e);
                string title = st.Title.ToString();
                if (title.Length == 0 || title.Length > 55) continue;
                if (!InsertMean(world, self, guid, "standard", title, title + " (mean)", st.Group.ToString(), ref b.StandardMeans, ref b.StandardMeanCount, ref capped)) return "";
            }
            return capped ? $"means capped at {MeansCap} roles / {MeansCap} standards" : "";
        }

        /// <summary>One mean, once: false = the request was refused (asked again next tick).</summary>
        private static bool InsertMean(EntityRepository world, Entity self, string evaluationGuid, string by, string key, string title, string group,
            ref Ids16 set, ref int count, ref bool capped)
        {
            long h = Hash(by + ":" + key);
            for (int i = 0; i < count; i++) if (set[i] == h) return true;
            if (count >= MeansCap) { capped = true; return true; }
            var args = new JsonObject
            {
                ["by"] = by, ["key"] = key, ["scope"] = evaluationGuid, ["parent"] = evaluationGuid,
                ["title"] = title.Length > 63 ? title.Substring(0, 63) : title, ["group"] = group,
            };
            string? why = MacroInsertion.Request(world, self, MeanMacro, default, default, args.ToJsonString(), out _, salt: "mean:" + by + ":" + key);
            if (why != null) return false;
            set[count++] = h;
            return true;
        }

        /// <summary>FNV-1a 64 over the UTF-16 chars — stable across runs, so a recorded set means the same after a replay.</summary>
        private static long Hash(string text)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in text) { h ^= c; h *= 1099511628211UL; }
            return (long)h;
        }

        /// <summary>
        /// ⭐ rules S4 ① — a man's ROLE KEY, the ONE function: the <c>UnitSubordinate.Designation</c> when he has one, else the name of his TKB template (his class); "" = neither.
        /// <c>SelectionMean{by: role}</c> selects by it and this evaluation names its role means by it.
        /// </summary>
        internal static string RoleKey(EntityRepository world, Entity member)
        {
            if (world.IsComponentTypeRegistered<UnitSubordinate>() && world.HasComponent<UnitSubordinate>(member)
                && world.GetComponentRO<UnitSubordinate>(member).Designation is var d && d != TacticalDesignation.Undefined) return d.ToString();
            return ClassOf(world, member) ?? "";
        }

        private static bool IsServed(in Block b, int nid)
        {
            for (int i = 0; i < b.ServedCount; i++) if (b.Served[i] == nid) return true;
            return false;
        }

        /// <summary>Room for one more served id (≤ 16): when full, the ids that are no longer current (<c>s_current</c>: left the roster / no longer a living man of the side) make room first.</summary>
        private static bool MakeRoom(ref Block b)
        {
            if (b.ServedCount < Ids16Length) return true;
            int kept = 0;
            for (int i = 0; i < b.ServedCount; i++)
                if (s_current.Contains(b.Served[i])) b.Served[kept++] = b.Served[i];
            b.ServedCount = kept;
            return b.ServedCount < Ids16Length;
        }

        private static void Mark(ref Block b, int nid) => b.Served[b.ServedCount++] = nid;   // MakeRoom ran before this member was handled

        private const int Ids16Length = 16;

        private static Dictionary<string, string> ReadOverrides(EntityRepository world, Entity self)
        {
            if (!world.HasManagedComponent<ActionArgs>(self)) return new();
            var items = ((ISimulationView)world).GetManagedComponentRO<ActionArgs>(self).Items;
            if (items.Count <= OverridesItem || string.IsNullOrWhiteSpace(items[OverridesItem])) return new();
            try { return JsonSerializer.Deserialize<Dictionary<string, string>>(items[OverridesItem], Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed) ?? new(); }
            catch (JsonException) { return new(); }
        }

        /// <summary>⭐ SR-81 / SR-72a — the first macro that EXISTS: the member's <c>@RefKey</c>, then his role, then his entity class, then <c>Default</c>; at each step
        /// the author's override of that name first, then the library's file of it. Null = none (not even a Default).</summary>
        internal static string? Pick(EntityRepository world, IMacroLibrary macros, string library, IReadOnlyDictionary<string, string> overrides, Entity member, TacticalDesignation role)
        {
            foreach (var (overrideKey, file) in Names(world, member, role))
            {
                if (overrides.TryGetValue(overrideKey, out var o) && Folder(o) is { Length: > 0 } ov && macros.TryRead(ov, out _, out _)) return ov;
                string path = library + "/" + file;
                if (macros.TryRead(path, out _, out _)) return path;
            }
            return null;
        }

        private static IEnumerable<(string OverrideKey, string File)> Names(EntityRepository world, Entity member, TacticalDesignation role)
        {
            if (world.IsComponentTypeRegistered<RefKey>() && world.HasComponent<RefKey>(member))
            {
                string key = world.GetComponentRO<RefKey>(member).Value.ToString();
                if (key.Length > 0) yield return ("@" + key, key);
            }
            if (role != TacticalDesignation.Undefined) yield return (role.ToString(), role.ToString());
            string? cls = ClassOf(world, member);
            if (!string.IsNullOrEmpty(cls)) yield return (cls, cls);
            yield return ("Default", "Default");
        }

        /// <summary>The member's entity class: the name of his TKB template.</summary>
        private static string? ClassOf(EntityRepository world, Entity member)
        {
            if (!world.HasComponent<TkbIdentity>(member) || !world.HasSingletonManaged<ITkbDatabase>()) return null;
            var db = world.GetSingletonManaged<ITkbDatabase>();
            return db != null && db.TryGetByType((long)world.GetComponentRO<TkbIdentity>(member).TkbType, out var t) ? t?.Name : null;
        }

        [Behavior(BehaviorNames.MemberEvaluation, Params = typeof(MemberEvaluationParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.MemberEvaluation, Curated = true, Group = "Library/Objectives", ParamsType = typeof(MemberEvaluationBlackboard))]
        public static BTreeBuilder<MemberEvaluationBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<MemberEvaluationBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
