using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Inspector.ActionBinding;

/// <summary>
/// ⭐⭐ <c>CE-1167</c> (docs/blueprints/AI_Editor_Shared_Infrastructure.md §7.1c G-5) — the ONE rule for which methods a binding slot
/// offers, for BTree and HSM alike: the exporter's entries hosted for that slot (and, for a BTree, of its kind — a condition or an action)
/// ∪ the names the asset already binds. ⚠ The asset's own names stay in the union even with an exporter: an asset may name a method the
/// current assembly no longer exports, and dropping it would hide a dangling binding instead of showing it.
/// </summary>
public static class ActionBindingMethods
{
    /// <param name="schema">The exporter (null in a lightweight host or a test ⇒ the asset's own names only).</param>
    /// <param name="hosting">The flag an entry must carry (<see cref="ActionHosting.BTree"/>, <see cref="ActionHosting.HsmGuard"/> …).</param>
    /// <param name="condition">For a BTree: true ⇒ conditions only, false ⇒ actions only; null ⇒ either (the HSM's hosting bits already
    /// tell a guard from an activity).</param>
    /// <param name="own">The bindings the asset already holds for this kind of slot.</param>
    /// <returns>The method FQNs (unsorted; the drawer sorts).</returns>
    public static IEnumerable<string> For(IActionSchemaExporter? schema, ActionHosting hosting, bool? condition,
                                          IEnumerable<BehaviorActionBinding?> own)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (schema != null)
            foreach (var kv in schema.All)
                if (kv.Value.Hosting.HasFlag(hosting) && (condition is not { } c || kv.Value.IsCondition == c))
                    names.Add(kv.Key);
        foreach (var b in own)
            if (!string.IsNullOrEmpty(b?.MethodFqn)) names.Add(b!.MethodFqn!);
        return names;
    }
}
