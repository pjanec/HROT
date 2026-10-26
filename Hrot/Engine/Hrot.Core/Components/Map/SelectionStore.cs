using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.IG.Components;

/// <summary>
/// ⭐⭐ <c>CE-4049</c> (coordinator handoff <c>Replay_Ui_State</c> ①; docs/blueprints/DESIGN_Editor_Entity_Selection_Source.md) — THE
/// selection, keyed by NETWORK ID: which units the operator selected and which of them is the primary. 🔒 User, <c>2026-10-25</c>:
/// <i>"selection is not the simulation state of an entity, it is a UI state having nothing to do with simulation and recording"</i>.
/// <para>⭐ A world SINGLETON, never recorded (<see cref="DataPolicy.Transient"/>), so a replay keyframe (which clears the component tables)
/// or a seek leaves it untouched — the pattern of <c>UnitDebugSettings</c> (CE-1329, Brain View §12r). The per-entity
/// <see cref="SelectionState"/> stays for its readers, as a COPY of this store rewritten every frame by
/// <c>EcsSelectionState.Sync</c>; only <c>EcsSelectionState</c> writes either.</para>
/// <para>A unit with no network id (a test world, a local-only entity) cannot be held here; its component is written directly, as before.</para>
/// </summary>
[ComponentId(GlobalComponentIds.SelectionStore)]
[DataPolicy(DataPolicy.Transient)]
public sealed class SelectionStore
{
    private readonly List<long> _selected = new();

    /// <summary>The primary's network id, 0 when none.</summary>
    public long Primary { get; private set; }

    /// <summary>The selected network ids, in the order they were selected.</summary>
    public IReadOnlyList<long> Selected => _selected;

    /// <summary>The store of <paramref name="world"/> (created on first use).</summary>
    public static SelectionStore Of(EntityRepository world)
    {
        if (world.HasSingletonManaged<SelectionStore>() && world.GetSingletonManaged<SelectionStore>() is { } s) return s;
        var created = new SelectionStore();
        world.SetSingletonManaged(created);
        return created;
    }

    /// <summary>The store of <paramref name="world"/>, or null when nothing was ever selected there (a read never creates it).</summary>
    public static SelectionStore? Peek(EntityRepository world)
        => world.HasSingletonManaged<SelectionStore>() ? world.GetSingletonManaged<SelectionStore>() : null;

    public bool Contains(long netId) => _selected.Contains(netId);

    /// <summary>Marks <paramref name="netId"/> selected or not, primary or not (the component's two flags).</summary>
    public void Set(long netId, bool selected, bool primary)
    {
        if (netId <= 0) return;
        if (!selected && !primary)
        {
            _selected.Remove(netId);
            if (Primary == netId) Primary = 0;
            return;
        }
        if (!_selected.Contains(netId)) _selected.Add(netId);
        if (primary) Primary = netId;
        else if (Primary == netId) Primary = 0;
    }

    public void Clear()
    {
        _selected.Clear();
        Primary = 0;
    }
}
