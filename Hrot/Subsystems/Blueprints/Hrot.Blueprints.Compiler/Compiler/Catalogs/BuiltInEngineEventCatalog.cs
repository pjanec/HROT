namespace Hrot.Blueprints.Core.Compiler.Catalogs;

// Animation event type FQN prefix (DD-3 §3, namespace Hrot.MuscleCharacter.Animation.Events).
// Used when building catalog entries to avoid repeating the prefix everywhere.
file static class AnimFqn
{
    private const string Ns = "Hrot.MuscleCharacter.Animation.Events";
    public static string Of(string typeName) => $"{Ns}.{typeName}";
}

// Navigation event type FQN prefix (Fdp.Toolkit.Navigation namespace).
file static class NavFqn
{
    private const string Ns = "Fdp.Toolkit.Navigation";
    public static string Of(string typeName) => $"{Ns}.{typeName}";
}

// Behavior lifecycle event type FQN prefix (Fdp.Toolkit.Behavior.Events namespace).
// P4 (GAP-3) -- events used by PublishEventNode / world.Bus.Publish.
file static class BehaviorFqn
{
    private const string Ns = "Fdp.Toolkit.Behavior.Events";
    public static string Of(string typeName) => $"{Ns}.{typeName}";
}

public sealed class BuiltInEngineEventCatalog : IEngineEventCatalog
{
    public static readonly BuiltInEngineEventCatalog Instance = new();

    public IReadOnlyList<EngineEventCatalogEntry> GetEntries() =>
        new List<EngineEventCatalogEntry>
        {
            // ---- Existing non-animation entries ---------------------------------
            new("HitEvent",              "Fdp.Toolkit.Combat.Contracts.HitEvent"),
            new("BehaviorFinishedEvent", "Fdp.Toolkit.Behavior.Events.BehaviorFinishedEvent"),
            new("TargetVisibleEvent",    "Fdp.Toolkit.Perception.Events.TargetVisibleEvent"),

            // ---- Animation lifecycle events (DD-3 §3.1, §4.1) ------------------
            // IDs 8201-8204; all Reliable + Volatile + PropagatesAcrossNodes=true.

            new(Name:                "MontageStartedEvent",
                EventTypeFqn:        AnimFqn.Of("MontageStartedEvent"),
                DisplayName:         "Montage Started",
                Category:            "Animation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "MontageId", "QueueIndex" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "MontageEndedEvent",
                EventTypeFqn:        AnimFqn.Of("MontageEndedEvent"),
                DisplayName:         "Montage Ended",
                Category:            "Animation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "MontageId", "QueueIndex", "EndReason" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "MontageSectionAdvancedEvent",
                EventTypeFqn:        AnimFqn.Of("MontageSectionAdvancedEvent"),
                DisplayName:         "Montage Section Advanced",
                Category:            "Animation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "MontageId", "ToSectionIndex" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "StanceChangedEvent",
                EventTypeFqn:        AnimFqn.Of("StanceChangedEvent"),
                DisplayName:         "Stance Changed",
                Category:            "Animation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "PreviousStance", "NewStance" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            // ---- Animation notify events (DD-3 §3.2, §4.1) ---------------------
            // IDs 8210-8213.

            // FootstepEvent: Muscle-local only — NOT propagated, NOT brain-visible (DD-3 §5.2).
            // Registered here so BP2017 can fire if a Brain Blueprint author explicitly
            // references it by type name. The Brain-side WhenNode dropdown must filter it
            // out by checking PropagatesAcrossNodes == false.
            new(Name:                "FootstepEvent",
                EventTypeFqn:        AnimFqn.Of("FootstepEvent"),
                DisplayName:         "Footstep",
                Category:            "Animation/Notify",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "FootIndex", "SurfaceTypeHint" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: false),

            new(Name:                "HitWindowOpenedEvent",
                EventTypeFqn:        AnimFqn.Of("HitWindowOpenedEvent"),
                DisplayName:         "Hit Window Opened",
                Category:            "Animation/Notify",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "MontageId", "WindowId" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "HitWindowClosedEvent",
                EventTypeFqn:        AnimFqn.Of("HitWindowClosedEvent"),
                DisplayName:         "Hit Window Closed",
                Category:            "Animation/Notify",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "MontageId", "WindowId" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "AnimNotifyEvent",
                EventTypeFqn:        AnimFqn.Of("AnimNotifyEvent"),
                DisplayName:         "Anim Notify (Generic)",
                Category:            "Animation/Notify",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "MontageId", "MarkerHash" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            // ---- Navigation lifecycle events (NAV-P4 §4.5, §5) ------------------
            // All propagate across nodes (Brain-visible) unless noted.

            new(Name:                "MoveStartedEvent",
                EventTypeFqn:        NavFqn.Of("MoveStartedEvent"),
                DisplayName:         "Move Started",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     "",
                FilterableFields:    new[] { "RouteHandle" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "MoveCompletedEvent",
                EventTypeFqn:        NavFqn.Of("MoveCompletedEvent"),
                DisplayName:         "Move Completed",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "Reason", "RouteHandle" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "PathReplannedEvent",
                EventTypeFqn:        NavFqn.Of("PathReplannedEvent"),
                DisplayName:         "Path Replanned",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "RouteHandle", "ReplanCount" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "OffMeshTraversalStartedEvent",
                EventTypeFqn:        NavFqn.Of("OffMeshTraversalStartedEvent"),
                DisplayName:         "Off-Mesh Traversal Started",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "TraversalKind" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "OffMeshTraversalEndedEvent",
                EventTypeFqn:        NavFqn.Of("OffMeshTraversalEndedEvent"),
                DisplayName:         "Off-Mesh Traversal Ended",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "Kind" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "MoveBlockedEvent",
                EventTypeFqn:        NavFqn.Of("MoveBlockedEvent"),
                DisplayName:         "Move Blocked",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "ReasonCode" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            // WaypointReachedEvent: Muscle-local only (progress tracking). Brain Blueprints
            // should not subscribe to individual waypoint events due to high frequency.
            new(Name:                "WaypointReachedEvent",
                EventTypeFqn:        NavFqn.Of("WaypointReachedEvent"),
                DisplayName:         "Waypoint Reached",
                Category:            "Navigation/Progress",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "SegmentIndex" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: false),

            new(Name:                "NavigationPathDetailsArrivedEvent",
                EventTypeFqn:        NavFqn.Of("NavigationPathDetailsArrivedEvent"),
                DisplayName:         "Navigation Path Details Arrived",
                Category:            "Navigation/PathDetails",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "RouteHandle", "IsAutoRefresh" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            // ---- Behavior lifecycle events (P4 -- GAP-3 -- world.Bus.Publish via PublishEventNode) --
            // Pre-existing in FDP/Toolkits/Fdp.Toolkits/Behavior/Events/ but not previously catalogued.

            new(Name:                "ClearBehaviorEvent",
                EventTypeFqn:        BehaviorFqn.Of("ClearBehaviorEvent"),
                DisplayName:         "Clear Behavior",
                Category:            "Behavior/Lifecycle",
                TargetFieldName:     "Entity",
                FilterableFields:    Array.Empty<string>(),
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "AssignBehaviorHashEvent",
                EventTypeFqn:        BehaviorFqn.Of("AssignBehaviorHashEvent"),
                DisplayName:         "Assign Behavior Hash",
                Category:            "Behavior/Lifecycle",
                TargetFieldName:     "Entity",
                FilterableFields:    new[] { "BehaviorHash" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            // AssignTacticalIntentEvent is `public sealed class` (managed -- carries string
            // fields IntentId/JsonParams) -- see AssignTacticalIntentEvent.cs. Must publish via
            // IEventBus.PublishManaged<T>, hence Managed: true (the only Managed entry today).
            new(Name:                "AssignTacticalIntentEvent",
                EventTypeFqn:        BehaviorFqn.Of("AssignTacticalIntentEvent"),
                DisplayName:         "Assign Tactical Intent",
                Category:            "Behavior/Lifecycle",
                TargetFieldName:     "Entity",
                FilterableFields:    new[] { "IntentId", "JsonParams" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true,
                Managed:             true,
                // Payload data-in pins (besides the "Target" entity) — string IntentId + JsonParams.
                // Baked so a pin-less PublishEvent node rehydrates these pins (Blocker-1 part 2).
                PayloadFields:       new EventPayloadField[]
                {
                    new("IntentId",   "System.String"),
                    new("JsonParams", "System.String"),
                }),
        };
}
