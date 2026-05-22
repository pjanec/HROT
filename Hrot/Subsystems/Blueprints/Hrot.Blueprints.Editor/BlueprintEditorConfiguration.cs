namespace Hrot.Blueprints.Editor;

/// <summary>Compile-time configuration for the Blueprint editor integration.</summary>
public sealed record BlueprintEditorConfiguration(
    string DebugMapsOutputDirectory,
    string BehaviorsDllDirectory,
    string BehaviorsBuildTarget = "");
