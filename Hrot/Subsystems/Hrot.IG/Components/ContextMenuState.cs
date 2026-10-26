using Fdp.Core;

namespace Hrot.IG.Components;

/// <summary>
/// Managed ECS component attached to an entity that currently has an active or
/// queued context menu.
///
/// Created / updated by <see cref="Hrot.IG.Systems.ContextMenuSystem"/> in
/// response to right-click triggers and <see cref="Hrot.Common.Events.ContextActionsUpdate"/> events.
///
/// This component intentionally uses a managed class (<c>class</c>) so that the
/// <see cref="MenuJson"/> string can be a reference type — registered via
/// <c>repo.RegisterManagedComponent&lt;ContextMenuState&gt;()</c>.
/// </summary>
[ComponentId(GlobalComponentIds.ContextMenuState)]
[DataPolicy(DataPolicy.Transient)]   // ⭐ CE-4049 — the IG's right-click MENU is UI state: never recorded or saved (its sibling CanvasContextMenuState is NoReplay); was NoPreview (CE-3299 D7)
// ⭐ CE-4046 — an immutable record: a change is a new value (`with`) + Set, so the recorder sees it (docs/DESIGN_Replay_Census_H0.md §2)
public sealed record ContextMenuState
{
    /// <summary>
    /// Pre-serialised JSON menu definition for this entity.
    /// Populated from the most recent <see cref="Hrot.Common.Events.ContextActionsUpdate"/> event.
    /// Empty string means no definition has arrived yet.
    /// </summary>
    public string MenuJson { get; init; } = string.Empty;

    /// <summary>
    /// <c>true</c> when the menu should be rendered this frame.
    /// Set by right-click input; cleared after an action is selected or the
    /// operator clicks away from the menu.
    /// </summary>
    public bool IsOpen { get; init; }

    /// <summary>Screen-space X coordinate at which the menu popup should appear.</summary>
    public float ScreenX { get; init; }

    /// <summary>Screen-space Y coordinate at which the menu popup should appear.</summary>
    public float ScreenY { get; init; }
}
