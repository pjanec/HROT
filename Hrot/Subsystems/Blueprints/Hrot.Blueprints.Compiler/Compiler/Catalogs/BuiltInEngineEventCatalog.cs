namespace Hrot.Blueprints.Core.Compiler.Catalogs;

// Animation event type FQN prefix (DD-3 §3, namespace Hrot.MuscleCharacter.Animation.Events).
// Used when building catalog entries to avoid repeating the prefix everywhere.
file static class AnimFqn
{
    private const string Ns = "Hrot.MuscleCharacter.Animation.Events";
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
        };
}
