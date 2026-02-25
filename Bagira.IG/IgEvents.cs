using Fdp.Kernel;

namespace Bagira.IG;

// ── Event ID constants ────────────────────────────────────────────────────────

/// <summary>
/// Stable event-type identifiers for IG-specific events
/// (§CODE-STANDARDS §1 — no magic numbers).
/// </summary>
public static class IgEventIds
{
    /// <summary><see cref="FireInteractionEvent"/> event type ID.</summary>
    public const int FireInteractionEventId = 3001;
}

// ── Unmanaged events (published via FdpEventBus.Publish / ConsumeEvents) ──────

/// <summary>
/// Fired when a network combat interaction (weapon detonation, fire notification)
/// is received from the SimHost.  Consumed by
/// <see cref="Bagira.IG.Systems.EventToEffectSystem"/> to spawn temporary visual
/// effects (explosion circle + tracer line).
///
/// Positions are in FDP world-space metres (X = east, Y = north).
/// </summary>
[EventId(IgEventIds.FireInteractionEventId)]
public struct FireInteractionEvent
{
    /// <summary>World-space X position of the firing entity (shooter).</summary>
    public float ShooterX;

    /// <summary>World-space Y position of the firing entity (shooter).</summary>
    public float ShooterY;

    /// <summary>World-space X position of the impact / target.</summary>
    public float TargetX;

    /// <summary>World-space Y position of the impact / target.</summary>
    public float TargetY;
}

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
