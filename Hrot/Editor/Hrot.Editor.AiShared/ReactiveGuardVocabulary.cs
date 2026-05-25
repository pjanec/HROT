namespace Hrot.Editor.AiShared;

/// <summary>
/// Shared string constants for the "Reactive Guards" palette category and tooltips.
/// Used by the BTree, HSM, and Blueprint editors to surface a consistent concept.
/// </summary>
public static class ReactiveGuardVocabulary
{
    public const string CategoryName = "Reactive Guards";

    public const string GenericTooltip =
        "Reactive guards re-evaluate their condition every tick. " +
        "When the condition transitions from false to true (rising edge), " +
        "the guard fires. Each subsystem has its own reactive guard implementation: " +
        "Observer Selectors in BTree, transition guards in HSM, and When nodes in " +
        "Instance Blueprints.";

    public const string BTreeObserverSelectorTooltip =
        "An Observer Selector re-evaluates its guard children every tick from the root, " +
        "preempting lower-priority running children if a higher-priority guard becomes true. " +
        "This is the BTree's reactive guard mechanism.";

    public const string HsmTransitionGuardTooltip =
        "A transition guard is re-evaluated every tick while its source state is active. " +
        "When the guard becomes true, the transition fires (subject to event matching). " +
        "This is the HSM's reactive guard mechanism.";

    /// <summary>Short display label for the Guard inspector field in the HSM editor.</summary>
    public const string HsmTransitionGuardDisplayName = "Guard (Reactive Guard)";

    public const string BlueprintWhenNodeTooltip =
        "A When node re-evaluates its condition every tick. When the condition transitions " +
        "from false to true (rising edge), the OnFired exec output triggers. " +
        "This is the Instance Blueprint's reactive guard mechanism. " +
        "(WhenNode is for Instance Blueprints only; use Observer Selectors in BTrees, " +
        "transition guards in HSMs.)";

    public const string CrossSubsystemHintBTree =
        "If you're familiar with HSM transition guards or Instance Blueprint When nodes, " +
        "Observer Selector children play the same role in a BTree.";

    public const string CrossSubsystemHintHsm =
        "If you're familiar with BTree Observer Selectors or Instance Blueprint When nodes, " +
        "transition guards play the same role in an HSM.";

    public const string CrossSubsystemHintBlueprint =
        "If you're familiar with BTree Observer Selectors or HSM transition guards, " +
        "When nodes play the same role in an Instance Blueprint.";
}
