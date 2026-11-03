using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;

namespace Fdp.Toolkit.ScenarioRules
{
    /// <summary>
    /// ⭐ CE-2380 part 2 (📄 docs/DESIGN_Remote_Edit_Outcome.md §4 C) — the remote-edit request id of a rule's pending <see cref="RuleControl"/> request, per world and rule entity.
    /// 📐 <see cref="RuleControl"/> is an unmanaged struct that is RECORDED (replay; <c>NoScenario</c> only) and brain-local — a Guid field would change its layout for every recording —
    /// so the id lives beside it, in a <see cref="ConditionalWeakTable{TKey,TValue}"/> like <c>EditOutcomeLog</c>: written by the <c>Rule.Seq</c> attribute handler, taken (once) by whoever judges the request.
    /// Only the latest unjudged request per entity is kept (a newer Seq supersedes an older one, exactly as the owner honours only the newest Seq).
    /// </summary>
    public static class RuleRequestIds
    {
        private static readonly ConditionalWeakTable<EntityRepository, Dictionary<Entity, Guid>> PerWorld = new();

        /// <summary>Remembers <paramref name="requestId"/> for <paramref name="entity"/> (an empty id clears it).</summary>
        public static void Set(EntityRepository repo, Entity entity, Guid requestId)
        {
            var map = PerWorld.GetValue(repo, static _ => new Dictionary<Entity, Guid>());
            lock (map) { if (requestId == Guid.Empty) map.Remove(entity); else map[entity] = requestId; }
        }

        /// <summary>The pending id of <paramref name="entity"/>, removed; <see cref="Guid.Empty"/> when none.</summary>
        public static Guid Take(EntityRepository repo, Entity entity)
        {
            if (!PerWorld.TryGetValue(repo, out var map)) return Guid.Empty;
            lock (map) { return map.Remove(entity, out var id) ? id : Guid.Empty; }
        }
    }
}
