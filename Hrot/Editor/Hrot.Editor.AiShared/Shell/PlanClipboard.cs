using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using MissionPlan = Hrot.Core.Mission.MissionPlan;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐ L-14 / CE-4314 (LC-6, 📄 docs/UX/DESIGN_Scenario_Lifecycle_Review.md) — <i>Copy plan</i> / <i>Paste plan</i>: a unit's mission plan
/// copied onto other units. Each target gets ITS OWN copy (<see cref="OrdersPlanEdits.CopyAsNew"/>: new task ids, all planned), committed
/// through the one mission facade (<see cref="IMissionEditorService.CommitMissionAsync"/>) at the target's own base version — the same
/// write the Orders tab's Apply makes. One clipboard per host (the Details bundle owns it), so both map hosts behave the same by construction.
/// </summary>
public sealed class PlanClipboard
{
    private readonly Func<IMissionEditorService?> _missions;
    private MissionPlan? _plan;

    public PlanClipboard(Func<IMissionEditorService?> missions) => _missions = missions ?? throw new ArgumentNullException(nameof(missions));

    /// <summary>Whether a plan has been copied.</summary>
    public bool HasPlan => _plan is not null;

    /// <summary>How many tasks the copied plan holds (0 when none).</summary>
    public int TaskCount => _plan?.Tasks.Count ?? 0;

    /// <summary>The commits the last <see cref="Paste"/> sent (one per target) — the caller or a rail may await them.</summary>
    public IReadOnlyList<Task<MissionCommitResult>> LastPaste { get; private set; } = Array.Empty<Task<MissionCommitResult>>();

    /// <summary>Copies <paramref name="unit"/>'s plan. False (and the clipboard unchanged) when it has no plan or no tasks.</summary>
    public bool Copy(EntityRepository world, Entity unit)
    {
        if (_missions() is not { } svc || NetOf(world, unit) is not int net) return false;
        var (plan, _) = svc.GetMissionSnapshot(net);
        if (plan is null || plan.Tasks.Count == 0) return false;
        _plan = OrdersPlanEdits.Copy(plan);
        return true;
    }

    /// <summary>Pastes the copied plan onto every target that can hold one — each its own copy. Returns how many were sent.</summary>
    public int Paste(EntityRepository world, IReadOnlyList<Entity> targets)
    {
        if (_plan is null || _missions() is not { } svc) { LastPaste = Array.Empty<Task<MissionCommitResult>>(); return 0; }
        var sent = new List<Task<MissionCommitResult>>(targets.Count);
        for (int i = 0; i < targets.Count; i++)
        {
            if (NetOf(world, targets[i]) is not int net) continue;
            var (_, version) = svc.GetMissionSnapshot(net);
            sent.Add(svc.CommitMissionAsync(net, OrdersPlanEdits.CopyAsNew(_plan), version));
        }
        LastPaste = sent;
        return sent.Count;
    }

    /// <summary>The targets of a paste on <paramref name="clicked"/>: the whole selection when it holds the clicked unit, else that unit alone.</summary>
    public static IReadOnlyList<Entity> TargetsOf(Entity clicked, IReadOnlyList<Entity> selected)
    {
        for (int i = 0; i < selected.Count; i++)
            if (selected[i] == clicked) return selected;
        return clicked.IsNull ? selected : new[] { clicked };
    }

    private static int? NetOf(EntityRepository world, Entity e)
        => !e.IsNull && world.IsAlive(e) && world.HasComponent<NetworkIdentity>(e) ? world.GetComponentRO<NetworkIdentity>(e).Value : null;
}
