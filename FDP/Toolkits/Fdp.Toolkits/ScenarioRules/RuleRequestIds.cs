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
        /// <summary>The pending ids, and the world epoch they belong to (⭐ Undo U3: entity handles mean nothing after a restore).</summary>
        private sealed class Pending { public readonly Dictionary<Entity, Guid> Map = new(); public int Epoch; }

        private static readonly ConditionalWeakTable<EntityRepository, Pending> PerWorld = new();

        /// <summary>⭐ Undo U3 (📄 docs/UX/DESIGN_Edit_Mode_Undo.md §1.1 B) — a restore replaced the world in place: the handles are another world's, forget them.</summary>
        private static void ForgetIfRestored(EntityRepository repo, Pending p)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(repo, ref p.Epoch)) p.Map.Clear();
        }

        /// <summary>Remembers <paramref name="requestId"/> for <paramref name="entity"/> (an empty id clears it).</summary>
        public static void Set(EntityRepository repo, Entity entity, Guid requestId)
        {
            var p = PerWorld.GetValue(repo, static r => { var n = new Pending(); Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(r, ref n.Epoch); return n; });   // synced to the world as it is now
            lock (p) { ForgetIfRestored(repo, p); if (requestId == Guid.Empty) p.Map.Remove(entity); else p.Map[entity] = requestId; }
        }

        /// <summary>The pending id of <paramref name="entity"/>, removed; <see cref="Guid.Empty"/> when none — and none after a restore.</summary>
        public static Guid Take(EntityRepository repo, Entity entity)
        {
            if (!PerWorld.TryGetValue(repo, out var p)) return Guid.Empty;
            lock (p) { ForgetIfRestored(repo, p); return p.Map.Remove(entity, out var id) ? id : Guid.Empty; }
        }
    }
}
