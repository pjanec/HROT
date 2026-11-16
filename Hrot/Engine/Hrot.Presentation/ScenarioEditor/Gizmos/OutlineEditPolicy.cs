using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.UI.Common.AddEntity;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ CE-4624 (minefields MS-U3, DESIGN_Minefields MR-8) — the ONE question the vertex editor asks before it changes an entity's outline: <i>"may this outline change?"</i>. A feature whose shape must stay put
/// (a minefield with a mine gone off: a struck (row, col) would silently move) registers a refusal here; <see cref="VertexEditGizmo"/> asks <see cref="ReasonFor"/> and, when it gets one, puts it on the hint line and
/// changes nothing. ⛔ The gizmo never names a feature. Refusals are plain functions over the world (no state here).
/// </summary>
public static class OutlineEditPolicy
{
    private static readonly List<Func<EntityRepository, Entity, string?>> Refusals = new();

    static OutlineEditPolicy()
    {
        Register(MinefieldOps.OutlineRefusal);   // MR-8: the minefield's shape lock
    }

    /// <summary>Adds a refusal: a function returning the reason an entity's outline may not change, or null. Idempotent for the same delegate.</summary>
    public static void Register(Func<EntityRepository, Entity, string?> refusal)
    {
        lock (Refusals) { if (!Refusals.Contains(refusal)) Refusals.Add(refusal); }
    }

    /// <summary>The first reason <paramref name="entity"/>'s outline may not change, or null when it may. ⭐ No allocation when nothing refuses.</summary>
    public static string? ReasonFor(EntityRepository world, Entity entity)
    {
        for (int i = 0; i < Refusals.Count; i++)
            if (Refusals[i](world, entity) is { } reason) return reason;
        return null;
    }
}
