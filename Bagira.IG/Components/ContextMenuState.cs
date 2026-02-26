using System.Collections.Generic;

namespace Bagira.IG.Components;

/// <summary>
/// A single action entry displayed in a context menu.
/// </summary>
public sealed class ContextAction
{
    /// <summary>Human-readable label shown in the menu row.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Internal action identifier.
    /// Names prefixed with <c>"IG_"</c> are handled locally by the IG application
    /// (e.g. <c>"IG_Lock_Camera"</c>, <c>"IG_Center"</c>); all other names are
    /// forwarded to IOS as a <see cref="ContextActionTriggered"/> managed event.
    /// </summary>
    public string ActionName { get; init; } = string.Empty;
}

/// <summary>
/// Managed ECS component attached to an entity that currently has an active or
/// queued context menu.
///
/// Created / updated by <see cref="Bagira.IG.Systems.ContextMenuSystem"/> in
/// response to right-click triggers and <see cref="ContextActionsUpdate"/> events.
///
/// This component intentionally uses a managed class (<c>class</c>) so that the
/// <see cref="Actions"/> list can be a reference type — registered via
/// <c>repo.RegisterManagedComponent&lt;ContextMenuState&gt;()</c>.
/// </summary>
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
