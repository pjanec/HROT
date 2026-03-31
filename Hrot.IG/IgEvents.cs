using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace Hrot.IG;

// ── Unmanaged events (published via cmd.PublishEvent / Bus.Consume) ──────────────────

/// <summary>
/// Published by <see cref="Translators.WeaponFireIngressTranslator"/> when a
/// <c>WeaponFire</c> DDS message is received.  The IG visual layer consumes this
/// event to trigger a muzzle-flash particle effect on the shooter entity.
/// </summary>
[EventId(6001)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IgWeaponFireEvent
{
    /// <summary>Network entity ID of the firing entity.</summary>
    public long ShooterEntityId;

    /// <summary>Network entity ID of the intended target.</summary>
    public long TargetEntityId;

    /// <summary>Zero-based weapon slot index.</summary>
    public int WeaponIndex;
}

// ── Managed events (published via FdpEventBus.PublishManaged / ConsumeManagedEvents) ─

/// <summary>
/// Sent from ExCon → IG to update the list of context-menu actions available
/// for a specific network entity.
/// </summary>
public sealed class ContextActionsUpdate
{
    /// <summary>Network identity of the entity whose actions are being updated.</summary>
    public int EntityNetworkId { get; init; }

    /// <summary>Replacement action list (replaces any previously stored list).</summary>
    public System.Collections.Generic.List<Hrot.IG.Components.ContextAction> Actions { get; init; } = new();
}

/// <summary>
/// Sent from IG → ExCon when the operator selects a non-local context action.
/// Non-local means the action name does <em>not</em> start with <c>"IG_"</c>.
/// </summary>
public sealed class ContextActionTriggered
{
    /// <summary>Network identity of the entity on which the action was triggered.</summary>
    public int EntityNetworkId { get; init; }

    /// <summary>Name of the triggered action (matches <see cref="Components.ContextAction.ActionName"/>).</summary>
    public string ActionName { get; init; } = string.Empty;
}
