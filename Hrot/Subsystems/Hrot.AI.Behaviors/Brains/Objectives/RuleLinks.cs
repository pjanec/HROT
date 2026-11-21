using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules V-1 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — the entities a trigger's rule NAMES, as network ids for the rules view:
    /// <b>DependsOn</b> = every <c>OnEntity</c> ref in its <c>when</c> (the tree walked: Compound children, a CountMatching's / OnEntity's inner predicate) ·
    /// <b>ActsOn</b> = every <c>target</c> in the args of its <c>do</c> steps (read from the step's JSON args — the one key every targeting verb names
    /// <c>Target</c>; ⭐ the JSON walk, not the action's params type, so it never has to build a slot). A reference not resolved to a net id yet
    /// (<c>"@Key"</c> not in the world) is looked up in the live world; one still unresolved leaves the set INCOMPLETE (<see cref="Result.Complete"/> false)
    /// and the caller asks again. Pure over the two JSON strings and the world.
    /// </summary>
    public static class RuleLinks
    {
        public struct Result
        {
            public byte DependsOnCount, ActsOnCount;
            public NetIds8 DependsOn, ActsOn;
            /// <summary>false = some reference is still unresolved (its id is 0 / left out) — ask again later.</summary>
            public bool Complete;
            /// <summary>1 = a step of the <c>do</c> list is <c>EndExercise</c> (CE-4238 follow-up).</summary>
            public byte EndsExercise;
        }

#pragma warning disable CS0649 // filled by OverlayFlat (reflection)
        private struct TargetProbe { public EntityLink Target; }
#pragma warning restore CS0649

        /// <summary>Computes both sets. ⛔ Never throws on a bad JSON (a refused trigger is the validator's job): the part that does not read is empty.</summary>
        public static Result Compute(EntityRepository? world, string? whenJson, string? doJson)
        {
            var dep = new List<int>(); var act = new List<int>();
            bool complete = true;
            byte endsExercise = 0;
            LiveRefScope? scope = null;
            int Id(EntityLink l)
            {
                if (!l.Ref.IsNone) return l.Ref.NetworkId;
                if (l.Spec.IsNone) return 0;
                if (world != null)
                {
                    scope ??= LiveRefScope.Of(world);
                    if (scope.TryResolve(l.Spec, out var e) && !e.IsNone) return e.NetworkId;
                }
                complete = false;
                return 0;
            }

            if (!string.IsNullOrEmpty(whenJson))
            {
                try { Walk(JsonSerializer.Deserialize<SearchPredicateDto>(whenJson, BehaviorParams.JsonOptions), dep, Id); }
                catch (Exception ex) when (ex is JsonException or NotSupportedException) { }
            }
            if (!string.IsNullOrEmpty(doJson))
            {
                try
                {
                    foreach (var step in ActionList.Get(doJson))
                    {
                        if (string.Equals(step.Act, "EndExercise", StringComparison.OrdinalIgnoreCase)) endsExercise = 1;
                        if (JsonNode.Parse(step.ArgsJson) is not JsonObject args) continue;
                        foreach (var (key, value) in args)
                        {
                            if (!string.Equals(key, "target", StringComparison.OrdinalIgnoreCase) || value is null) continue;
                            var probe = default(TargetProbe);
                            BehaviorParams.OverlayFlat(ref probe, new JsonObject { ["target"] = value.DeepClone() }.ToJsonString());
                            Add(act, Id(probe.Target));
                        }
                    }
                }
                catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException) { }
            }
            var r = new Result { Complete = complete, EndsExercise = endsExercise };
            Fill(dep, ref r.DependsOn, out r.DependsOnCount);
            Fill(act, ref r.ActsOn, out r.ActsOnCount);
            return r;
        }

        private static void Walk(SearchPredicateDto? p, List<int> into, Func<EntityLink, int> id)
        {
            switch (p)
            {
                case OnEntityPredicateDto oe: Add(into, id(oe.Ref)); Walk(oe.Predicate, into, id); break;
                case CountMatchingPredicateDto cm: Walk(cm.Predicate, into, id); break;
                case CompoundPredicateDto c: foreach (var child in c.Conditions) Walk(child, into, id); break;
            }
        }

        private static void Add(List<int> into, int netId) { if (netId != 0 && !into.Contains(netId)) into.Add(netId); }

        private static void Fill(List<int> from, ref NetIds8 to, out byte count)
        {
            int n = Math.Min(from.Count, 8);
            for (int i = 0; i < n; i++) to[i] = from[i];
            count = (byte)n;
        }

        /// <summary>Copies the sets into the status (the owner writes them every tick, like the links).</summary>
        public static void CopyTo(ref ObjectiveStatus s, in NetIds8 dep, byte depN, in NetIds8 act, byte actN)
        {
            s.DependsOnCount = depN; s.DependsOn = dep; s.ActsOnCount = actN; s.ActsOn = act;
        }
    }
}
