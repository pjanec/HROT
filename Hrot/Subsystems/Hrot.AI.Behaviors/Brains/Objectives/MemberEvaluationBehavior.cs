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
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MemberEvaluationBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.MemberEvaluation)]
        public static void Resolve(in MemberEvaluationParamsJsonDto authored, ref MemberEvaluationBlackboard bb, EntityRepository world, Entity self)
        {
            var problems = ValidateParams(in authored);
            if (problems.Count > 0) throw new InvalidOperationException("MemberEvaluation: " + string.Join("; ", problems));
            bb.Block = new Block { Common = ObjectiveCommonBlock.From(in authored.Common), Library = new FixedString64(Folder(authored.Library)) };
            string overrides = authored.Overrides is { Count: > 0 } ? JsonSerializer.Serialize(authored.Overrides) : "{}";
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
            if (a.Overrides != null)
                foreach (var (k, v) in a.Overrides)
                    if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(Folder(v))) problems.Add($"overrides '{k}': names no macro.");
            return problems;
        }

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
            if (f.Reset) { b.Served = default; b.ServedCount = 0; b.Unmatched = 0; }   // SR-114: a reset clears the working state
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (ObjectiveChildren.Evaluates(in s))
            {
                string note = Serve(ref b, self, world);
                if (note.Length == 0 && b.Unmatched > 0) note = $"{b.Unmatched} member(s) without a macro";
                WeightedMeanBehavior.Aggregate(world, self, ref s, WeightedMeanBehavior.DefaultPassScore, f.Now, Kind, note);
            }
            ObjectiveHost.CloseComposite(world, self, ref s, f.Now);
            return NodeStatus.Running;
        }

        /// <summary>Inserts the macro of every roster member not served yet; returns why it could not (a refused request / no library), else empty.</summary>
        private static string Serve(ref Block b, Entity self, EntityRepository world)
        {
            var subject = b.Common.Subject.Resolve(world);
            if (subject.IsNull || !world.IsAlive(subject) || !world.HasComponent<UnitRoster>(subject)) return "";
            var roster = world.GetComponentRO<UnitRoster>(subject);
            string library = b.Library.ToString();
            IMacroLibrary? macros = world.HasSingletonManaged<IMacroLibrary>() ? world.GetSingletonManaged<IMacroLibrary>() : null;
            Dictionary<string, string>? overrides = null;
            string? refused = null;
            for (int i = 0; i < roster.Count; i++)
            {
                var member = roster.SubordinateEntities[i];
                if (member.IsNull || !world.IsAlive(member)) continue;
                int nid = EntityRef.Of(world, member).NetworkId;
                if (nid == 0 || IsServed(in b, nid)) continue;
                if (macros is null) { refused = "this node has no macro library"; break; }
                overrides ??= ReadOverrides(world, self);
                var role = world.HasComponent<UnitSubordinate>(member) && world.GetComponentRO<UnitSubordinate>(member).Designation != TacticalDesignation.Undefined
                    ? world.GetComponentRO<UnitSubordinate>(member).Designation : (TacticalDesignation)roster.TacticalDesignations[i];
                string? macro = Pick(world, macros, library, overrides, member, role);
                if (macro is null) { Mark(ref b, world, roster, nid); b.Unmatched++; continue; }   // asked once: a missing macro is a Reason, not an error
                if (!world.IsComponentTypeRegistered<EntityGuid>() || !world.HasComponent<EntityGuid>(member) || !world.HasComponent<EntityGuid>(self))
                { Mark(ref b, world, roster, nid); b.Unmatched++; continue; }   // SR-45: a macro takes its subject / parent by their stable ids
                var args = new JsonObject
                {
                    ["subject"] = world.GetComponentRO<EntityGuid>(member).Value.ToString("D"),
                    ["parent"] = world.GetComponentRO<EntityGuid>(self).Value.ToString("D"),
                };
                var at = world.HasComponent<SimTransform>(member) ? world.GetComponentRO<SimTransform>(member).Position : default;
                string? why = MacroInsertion.Request(world, self, macro, at, default, args.ToJsonString(), out _, salt: nid.ToString());
                if (why != null) { refused = why; break; }   // asked again next tick
                Mark(ref b, world, roster, nid);
            }
            return refused ?? "";
        }

        private static bool IsServed(in Block b, int nid)
        {
            for (int i = 0; i < b.ServedCount; i++) if (b.Served[i] == nid) return true;
            return false;
        }

        /// <summary>Remembers a member (≤ 16). When full, the ids that left the roster make room first — a roster holds 16, so the current men always fit.</summary>
        private static void Mark(ref Block b, EntityRepository world, in UnitRoster roster, int nid)
        {
            if (b.ServedCount >= Ids16Length)
            {
                int kept = 0;
                for (int i = 0; i < b.ServedCount; i++)
                {
                    long id = b.Served[i]; bool inRoster = false;
                    for (int r = 0; r < roster.Count && !inRoster; r++) inRoster = EntityRef.Of(world, roster.SubordinateEntities[r]).NetworkId == id;
                    if (inRoster) b.Served[kept++] = id;
                }
                b.ServedCount = kept;
                if (b.ServedCount >= Ids16Length) return;
            }
            b.Served[b.ServedCount++] = nid;
        }

        private const int Ids16Length = 16;

        private static Dictionary<string, string> ReadOverrides(EntityRepository world, Entity self)
        {
            if (!world.HasManagedComponent<ActionArgs>(self)) return new();
            var items = ((ISimulationView)world).GetManagedComponentRO<ActionArgs>(self).Items;
            if (items.Count <= OverridesItem || string.IsNullOrWhiteSpace(items[OverridesItem])) return new();
            try { return JsonSerializer.Deserialize<Dictionary<string, string>>(items[OverridesItem]) ?? new(); }
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
