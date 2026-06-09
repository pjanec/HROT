namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Compile-time constants shared across the Blueprints subsystem.
/// </summary>
public static class BlueprintConstants
{
    /// <summary>
    /// Requests attaching an Instance blueprint to an entity at runtime.
    /// </summary>
    public const int EventId_AttachInstanceBlueprint  = 9100;

    /// <summary>
    /// Requests detaching an Instance blueprint from an entity at runtime.
    /// </summary>
    public const int EventId_RemoveInstanceBlueprint  = 9101;

    /// <summary>
    /// Requests replacing one Instance blueprint with another on an entity.
    /// Applied as: detach old → attach new (remove-before-add ordering).
    /// </summary>
    public const int EventId_ReplaceInstanceBlueprint = 9102;
}
