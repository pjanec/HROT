using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace Hrot.IG;

// -- Managed events (published via FdpEventBus.PublishManaged / ConsumeManagedEvents) --

/// <summary>
/// Sent from IG to ExCon when the operator selects a non-local context action.
/// Non-local means the action name does not start with "IG_".
/// </summary>
public sealed class ContextActionTriggered
{
    /// <summary>Network identity of the entity on which the action was triggered.</summary>
    public int EntityNetworkId { get; init; }

    /// <summary>Name of the triggered action (matches <see cref="Components.ContextAction.ActionName"/>).</summary>
    public string ActionName { get; init; } = string.Empty;
}
