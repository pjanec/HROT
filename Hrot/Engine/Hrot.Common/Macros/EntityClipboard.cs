using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Scenario;
using Hrot.Core.Network;

namespace Hrot.Common.Macros;

/// <summary>
/// ⭐⭐ A5 / LC-3 (📄 docs/UX/DESIGN_Entity_Copy_Paste.md §2) — <i>Copy</i> / <i>Paste</i> / <i>Duplicate</i>, ONE path in every mode and shared with macro
/// expansion: <see cref="Copy"/> writes the selection as a scenario slice (<see cref="ScenarioSerializer.Serialize(EntityRepository, ScenarioHeader, IReadOnlySet{Guid})"/>,
/// the save's own selection filter); <see cref="Paste"/> expands it through this host's ONE <see cref="SliceExpander"/> (the macro expander's core: ids
/// pre-allocated, references between copies remapped to them, every position relocated and turned, fresh derived GUIDs) and creates each entity through
/// <see cref="Edits.Submit"/> — the creation every placement makes (R-145). ⇒ Edit and Live alike; one undo step in Edit; refused in Review by the gate.
/// <para>A pasted entity is SAVED (not transient like a macro child); a reference to an entity it did not copy resolves in the live world
/// (<see cref="LiveRefScope"/>); its mission plan gets new task ids.</para>
/// </summary>
public sealed class EntityClipboard
{
    /// <summary>How a paste names its step (the journal, the undo history).</summary>
    public const string PasteLabel = "Paste";

    private readonly Func<MacroExpander?> _expander;
    private readonly Func<INetworkIdAllocator?> _ids;
    private string? _slice;
    private WorldPoint _anchor;
    private Bearing _anchorHeading;
    private readonly List<Vector3> _offsets = new();

    /// <param name="expander">This host's ONE expander (<c>EntityCreation.MacroExpansionSystem.Expander</c>) — its <see cref="MacroExpander.Slices"/> is the shared core.</param>
    /// <param name="ids">The ids its expansions take (<see cref="MacroExpansionSystem.IdAllocator"/>).</param>
    public EntityClipboard(Func<MacroExpander?> expander, Func<INetworkIdAllocator?> ids)
    {
        _expander = expander ?? throw new ArgumentNullException(nameof(expander));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    /// <summary>Whether something was copied.</summary>
    public bool HasCopy => _slice is not null;

    /// <summary>How many entities were copied.</summary>
    public int Count => _offsets.Count;

    /// <summary>Each copied entity's position relative to the anchor, as copied (the paste tool's ghosts).</summary>
    public IReadOnlyList<Vector3> Offsets => _offsets;

    /// <summary>The copied group's heading (a plain click keeps it).</summary>
    public Bearing Heading => _anchorHeading;

    /// <summary>The copied slice (a rail reads it).</summary>
    public string? Slice => _slice;

    /// <summary>Copies <paramref name="selection"/> (every entity with a stable key; others are skipped); the anchor is <paramref name="primary"/>'s place and
    /// heading (the first copied entity when it is not one of them). False — and the clipboard unchanged — when nothing could be copied.</summary>
    public bool Copy(EntityRepository world, IReadOnlyList<Entity> selection, Entity primary)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(selection);
        if (_expander() is not { } expander) return false;
        if (!world.IsComponentTypeRegistered<EntityGuid>() || !world.IsComponentTypeRegistered<SimTransform>()) return false;

        var guids = new HashSet<Guid>();
        var placed = new List<WorldPoint>();
        Entity anchorEntity = Entity.Null;
        for (int i = 0; i < selection.Count; i++)
        {
            var e = selection[i];
            if (!world.IsAlive(e) || !world.HasComponent<EntityGuid>(e) || !world.HasComponent<SimTransform>(e)) continue;
            var g = world.GetComponentRO<EntityGuid>(e).Value;
            if (g == Guid.Empty || !guids.Add(g)) continue;
            placed.Add(world.GetComponentRO<SimTransform>(e).Position);
            if (anchorEntity.IsNull || e == primary) anchorEntity = e;
        }
        if (guids.Count == 0) return false;

        var serializer = expander.Slices.Serializer;
        _slice = serializer.Serialize(world, new ScenarioHeader(serializer.SubsystemType), guids).ToJsonString();
        var at = world.GetComponentRO<SimTransform>(anchorEntity);
        _anchor = at.Position;
        _anchorHeading = new Bearing(at.Rotation.HeadingDeg);
        _offsets.Clear();
        for (int i = 0; i < placed.Count; i++) _offsets.Add((Vector3)placed[i] - (Vector3)_anchor);
        return true;
    }

    /// <summary>The requests a paste at <paramref name="at"/> facing <paramref name="heading"/> sends (pure: nothing is created). Null when nothing is copied
    /// or this host cannot expand; throws <see cref="SliceExpander.NoKey"/> for a slice entity with no key.</summary>
    public IReadOnlyList<EntityCreationRequest>? Requests(EntityRepository world, WorldPoint at, Bearing heading, Guid pasteId)
    {
        if (_slice is null || _expander() is not { } expander || _ids() is not { } ids) return null;
        var into = new List<EntityCreationRequest>();
        var options = new SliceExpander.Options(pasteId, "copy:", Owner: 0, Transient: false,   // owner 0 ⇒ the default processor; SAVED, unlike a macro child
            Outside: LiveRefScope.Of(world),                                                        // what was not copied resolves in the live world
            PerChild: (_, comps, _, _) =>
            {
                for (int i = 0; i < comps.Count; i++)
                    if (comps[i] is ActiveMissionPlan plan) comps[i] = plan.WithNewTaskIds();   // each copy its own task ids
            });
        expander.Slices.Expand(_slice, new Relocation(_anchor, _anchorHeading, at, heading), ids, options, into);
        return into;
    }

    /// <summary>Pastes at <paramref name="at"/> facing <paramref name="heading"/>: ONE step (Edit mode's undo), each entity one journalled creation. Returns how
    /// many were sent (0 when nothing is copied, the host cannot expand, or the slice is refused).</summary>
    public int Paste(EntityRepository world, WorldPoint at, Bearing heading)
    {
        ArgumentNullException.ThrowIfNull(world);
        IReadOnlyList<EntityCreationRequest>? requests;
        try { requests = Requests(world, at, heading, Guid.NewGuid()); }
        catch (SliceExpander.NoKey) { return 0; }
        if (requests is null || requests.Count == 0) return 0;

        EditSteps.Begin(world, PasteLabel);
        try
        {
            for (int i = 0; i < requests.Count; i++)
            {
                var r = requests[i];
                Edits.Submit(world, EditOp.Create(id => world.Bus.PublishManaged(SliceExpander.WithRequestId(r, id)), PasteLabel));
            }
        }
        finally { EditSteps.End(world); }
        return requests.Count;
    }
}
