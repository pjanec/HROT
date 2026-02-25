namespace Bagira.IOS;

/// <summary>
/// Named constants for <see cref="IosLogic"/>.
///
/// <para>Centralised here so that any threshold change is a single-line edit
/// (CODE-STANDARDS §1 — no magic numbers in production code).</para>
/// </summary>
internal static class IosLogicConstants
{
    // ── MapInteractionConfig ──────────────────────────────────────────────────

    /// <summary>
    /// Map group targeted by the IOS when publishing
    /// <c>MapInteractionConfig</c>.  0 = broadcast to all IGs.
    /// </summary>
    internal const int DefaultMapGroupId = 0;

    /// <summary>
    /// JSON schema version embedded in every <c>MapInteractionConfig</c>
    /// published by the IOS.
    /// </summary>
    internal const int JsonSchemaVersion = 1;

    // ── Placement tool ────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>activeTool</c> string sent in the config JSON when the operator
    /// activates the entity-placement tool.
    /// </summary>
    internal const string PlacementToolName = "PLACEMENT";

    // ── Log topics ────────────────────────────────────────────────────────────

    /// <summary>DDS topic name used in interaction-log entries for config patches.</summary>
    internal const string LogTopicConfig = "MapInteractionConfig";

    /// <summary>DDS topic name used in interaction-log entries for click events.</summary>
    internal const string LogTopicClick = "MapClickEvent";

    /// <summary>DDS topic name used in interaction-log entries for create requests.</summary>
    internal const string LogTopicCreate = "CreateEntityRequest";

    /// <summary>DDS topic name used in interaction-log entries for selection events.</summary>
    internal const string LogTopicSelection = "SelectionChangedEvent";
}
