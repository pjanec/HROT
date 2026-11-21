using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using StructEdit.Reflection;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐⭐ <c>CE-4298</c> — THE compiled-condition cache: a condition's JSON → its LEVEL predicate, compiled once PROCESS-WIDE (the delegate holds no
    /// world: it takes the repository and the entity). A trigger rule's <c>when</c> (<c>TriggerWhen</c>, rules S2-c) and a mission task's
    /// <i>then</i> / <i>give up</i> (<c>PlanConditionCache</c>, mission plan D3) both read it — ⛔ never a second compiler or a second cache for one
    /// condition language (R-174). Moved down from <c>Hrot.AI.Behaviors.TriggerWhen</c> so the mission director (this assembly) shares it.
    /// <para>The hot path is a reference lookup of the very string the caller holds; a miss goes to the content-keyed table, so two conditions
    /// authored the same share one delegate.</para>
    /// </summary>
    public static class ConditionCache
    {
        /// <summary>The ONE level-predicate compiler of authored conditions.</summary>
        public static readonly IPredicateCompiler Compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());

        private static readonly ConcurrentDictionary<string, Func<EntityRepository, Entity, bool>> ByContent = new();
        private static readonly ConditionalWeakTable<string, Func<EntityRepository, Entity, bool>> ByReference = new();

        /// <summary>The LEVEL predicate of <paramref name="json"/> (compiled once). ⛔ Throws when the JSON is not a predicate or does not compile.</summary>
        public static Func<EntityRepository, Entity, bool> Get(string json)
        {
            if (ByReference.TryGetValue(json, out var hit)) return hit;
            var f = ByContent.GetOrAdd(json, Compile);
            ByReference.AddOrUpdate(json, f);
            return f;
        }

        /// <summary>The condition <paramref name="json"/> holds (the authored-condition JSON form). ⛔ Throws when it is not a predicate.</summary>
        public static SearchPredicateDto Parse(string json)
            => JsonSerializer.Deserialize<SearchPredicateDto>(json, BehaviorParams.JsonOptions)
               ?? throw new InvalidOperationException("the condition is empty");

        /// <summary>The canonical JSON of an authored condition.</summary>
        public static string ToJson(SearchPredicateDto condition) => JsonSerializer.Serialize(condition, BehaviorParams.JsonOptions);

        private static Func<EntityRepository, Entity, bool> Compile(string json) => Compiler.CompileLevelPredicate(Parse(json));
    }
}
