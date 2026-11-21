using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.AiEditor.Persistence.Hsm;

/// <summary>
/// ⭐ Q84 C1 + D1 (docs/blueprints/Architect_Question_84 §3/§6) — history is a property of the COMPOSITE being
/// re-entered ("start at initial / resume last child / resume last leaf"), which is exactly how the FastHSM kernel
/// stores it (<c>HsmKernelCore</c> saves on exit, restores on entry of the state carrying the flag).
/// <para>The editor used to create a separate childless "History" pseudo-state; emitted as <c>.History()</c> on a
/// LEAF it meant nothing at runtime. This migration folds such a node into its parent: the parent takes the flag,
/// transitions into the node now target the parent, and the node (with any transitions out of it) is removed.
/// Idempotent; applied on editor load and before emit, so an old file behaves the same either way.</para>
/// </summary>
public static class HsmHistoryMigration
{
    /// <summary>Applies the migration in place. Returns the number of pseudo-states folded.</summary>
    public static int Apply(HsmAssetDto dto)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));
        var byId = dto.States.ToDictionary(s => s.StableId);

        var pseudos = dto.States
            .Where(s => (s.IsHistory || s.IsDeepHistory)
                        && s.ChildStableIds.Count == 0
                        && s.ParentStableId is { } p && byId.ContainsKey(p))
            .ToList();

        foreach (var pseudo in pseudos)
        {
            var parent = byId[pseudo.ParentStableId!.Value];
            if (pseudo.IsDeepHistory) parent.IsDeepHistory = true;
            if (pseudo.IsHistory)     parent.IsHistory = true;

            foreach (var t in dto.Transitions.Where(t => t.TargetStableId == pseudo.StableId))
                t.TargetStableId = parent.StableId;
            foreach (var g in dto.GlobalTransitions.Where(g => g.TargetStableId == pseudo.StableId))
                g.TargetStableId = parent.StableId;
            dto.Transitions.RemoveAll(t => t.SourceStableId == pseudo.StableId);
            foreach (var r in dto.Regions.Where(r => r.InitialChildStableId == pseudo.StableId))
                r.InitialChildStableId = null;

            parent.ChildStableIds.Remove(pseudo.StableId);
            dto.States.Remove(pseudo);
        }
        return pseudos.Count;
    }
}
