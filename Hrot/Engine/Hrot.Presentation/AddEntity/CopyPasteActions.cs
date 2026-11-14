using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using Hrot.Common.Macros;
using Hrot.UI.Common.Adapters;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ A5 / LC-3 (📄 docs/UX/DESIGN_Entity_Copy_Paste.md §2.3) — <i>Copy</i>, <i>Paste</i>, <i>Duplicate</i> on the map's actions, built beside Add Entity
/// by the map pack for EVERY host that authors entities (one clipboard per map). Copy takes the selection when the clicked entity is in it, else the
/// clicked entity (the anchor is the clicked one); Paste arms the paste tool (<see cref="ScenarioSpawnAdapter.ArmPaste"/>); Duplicate = Copy + Paste.
/// The modes are the catalogue's (Edit and Live; refused in Review by the edit gate anyway).
/// </summary>
public sealed class CopyPasteActions
{
    private readonly Func<ScenarioSpawnAdapter?> _spawn;
    private readonly Fdp.Toolkit.Vis2D.Abstractions.ISelectionState? _selection;

    public CopyPasteActions(Func<ScenarioSpawnAdapter?> spawn, Fdp.Toolkit.Vis2D.Abstractions.ISelectionState? selection)
    {
        _spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
        _selection = selection;
        Clipboard = new EntityClipboard(() => _spawn()?.ExpanderOrNull, () => _spawn()?.IdAllocator);
    }

    /// <summary>This map's clipboard.</summary>
    public EntityClipboard Clipboard { get; }

    /// <summary>Registers Copy / Paste / Duplicate on <paramref name="actions"/>.</summary>
    public void RegisterOn(GlobalActionRegistry actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        actions.RegisterLocal(GlobalActionIds.CopyEntities, (view, target) => { if (view is EntityRepository w) Copy(w, target); });
        actions.RegisterLocal(GlobalActionIds.PasteEntities, (_, _) => Paste());
        actions.RegisterLocal(GlobalActionIds.DuplicateEntities, (view, target) => { if (view is EntityRepository w && Copy(w, target)) Paste(); });
    }

    /// <summary>The entities a Copy on <paramref name="clicked"/> takes: the selection when it holds the clicked one, else the clicked one alone.</summary>
    public IReadOnlyList<Entity> TargetsOf(Entity clicked)
    {
        var selected = _selection?.SelectedEntities;
        if (selected is { Count: > 0 } && (clicked.IsNull || _selection!.IsSelected(clicked))) return new List<Entity>(selected);
        return clicked.IsNull ? Array.Empty<Entity>() : new[] { clicked };
    }

    /// <summary>Copies <see cref="TargetsOf"/>(<paramref name="clicked"/>), anchored on the clicked entity (the primary selected one with no click).</summary>
    public bool Copy(EntityRepository world, Entity clicked)
    {
        var targets = TargetsOf(clicked);
        if (targets.Count == 0) return false;
        var anchor = !clicked.IsNull ? clicked : _selection?.PrimarySelected ?? targets[0];
        return Clipboard.Copy(world, targets, anchor);
    }

    /// <summary>Arms the paste tool; false when nothing is copied or this host has no map tools.</summary>
    public bool Paste() => _spawn() is { } spawn && spawn.ArmPaste(Clipboard);
}
