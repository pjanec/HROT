namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Blueprint-editor-local copy of the shared reactive guard vocabulary constants.
/// Kept separate from <c>Hrot.Editor.AiShared.ReactiveGuardVocabulary</c> to avoid
/// adding a project reference in <c>Hrot.Blueprints.Editor</c>.
/// See <c>Hrot/Docs/ReactiveGuards.md</c> for cross-subsystem usage.
/// </summary>
public static class ReactiveGuardVocabulary
{
    public const string CategoryName = "Reactive Guards";

    public const string BlueprintWhenNodeTooltip =
        "A When node re-evaluates its condition every tick. When the condition transitions " +
        "from false to true (rising edge), the OnFired exec output triggers. " +
        "This is the Instance Blueprint's reactive guard mechanism. " +
        "(WhenNode is for Instance Blueprints only; use Observer Selectors in BTrees, " +
        "transition guards in HSMs.)";

    public const string CrossSubsystemHintBlueprint =
        "If you're familiar with BTree Observer Selectors or HSM transition guards, " +
        "When nodes play the same role in an Instance Blueprint.";

    /// <summary>Kept for source-compat; same text as <see cref="CrossSubsystemHintBlueprint"/>.</summary>
    public const string CrossSubsystemHintWhen = CrossSubsystemHintBlueprint;
}
