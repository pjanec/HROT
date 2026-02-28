using Fdp.Kernel;

namespace Bagira.IG;

// ── Managed events (published via FdpEventBus.PublishManaged / ConsumeManagedEvents) ─

/// <summary>
/// Sent from IOS → IG to update the list of context-menu actions available
/// for a specific network entity.
/// </summary>
public sealed class ContextActionsUpdate
{
    /// <summary>Network identity of the entity whose actions are being updated.</summary>
    public int EntityNetworkId { get; init; }

    /// <summary>Replacement action list (replaces any previously stored list).</summary>
    public System.Collections.Generic.List<Bagira.IG.Components.ContextAction> Actions { get; init; } = new();
}

/// <summary>
/// Sent from IG → IOS when the operator selects a non-local context action.
/// Non-local means the action name does <em>not</em> start with <c>"IG_"</c>.
/// </summary>
public sealed class ContextActionTriggered
{
    /// <summary>Network identity of the entity on which the action was triggered.</summary>
    public int EntityNetworkId { get; init; }

    /// <summary>Name of the triggered action (matches <see cref="Components.ContextAction.ActionName"/>).</summary>
    public string ActionName { get; init; } = string.Empty;
}
