using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.IG.Components;

/// <summary>
/// Managed ECS component attached to an entity that currently has an active or
/// queued context menu.
///
/// Created / updated by <see cref="Hrot.IG.Systems.ContextMenuSystem"/> in
/// response to right-click triggers and <see cref="ContextActionsUpdate"/> events.
///
/// This component intentionally uses a managed class (<c>class</c>) so that the
/// <see cref="Actions"/> list can be a reference type — registered via
/// <c>repo.RegisterManagedComponent&lt;ContextMenuState&gt;()</c>.
/// </summary>
[ComponentId(GlobalComponentIds.ContextMenuState)]
public sealed class ContextMenuState
{
    /// <summary>
    /// Available actions for this entity's context menu.
    /// Populated from the most recent <see cref="ContextActionsUpdate"/> event.
    /// </summary>
    public List<ContextAction> Actions { get; set; } = new();

    /// <summary>
    /// <c>true</c> when the menu should be rendered this frame.
    /// Set by right-click input; cleared after an action is selected or the
    /// operator clicks away from the menu.
    /// </summary>
    public bool IsOpen { get; set; }

    /// <summary>Screen-space X coordinate at which the menu popup should appear.</summary>
    public float ScreenX { get; set; }

    /// <summary>Screen-space Y coordinate at which the menu popup should appear.</summary>
    public float ScreenY { get; set; }
}
