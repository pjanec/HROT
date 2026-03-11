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
    /// Default MapId used when publishing <see cref="MapCommandRequest"/> messages
    /// to activate tools on the nearest IG instance.
    /// Matches <c>IgNetworkConstants.InstanceId</c> (300).
    /// Use 0 to broadcast to every IG in the group.
    /// </summary>
    internal const int DefaultTargetMapId = 300;

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

    /// <summary>
    /// The <c>activeTool</c> string sent in the config JSON when the operator
    /// activates polygonal area authoring.
    /// </summary>
    internal const string AreaAuthoringToolName = "AREA_AUTHORING";

    // ── Log topics ────────────────────────────────────────────────────────────

    /// <summary>DDS topic name used in interaction-log entries for config patches.</summary>
    internal const string LogTopicConfig = "MapInteractionConfig";

    /// <summary>DDS topic name used in interaction-log entries for tool-activation commands.</summary>
    internal const string LogTopicCommand = "MapCommandRequest";

    /// <summary>DDS topic name used in interaction-log entries for click events.</summary>
    internal const string LogTopicClick = "MapClickEvent";

    /// <summary>DDS topic name used in interaction-log entries for create requests.</summary>
    internal const string LogTopicCreate = "CreateEntityRequest";

    /// <summary>DDS topic name used in interaction-log entries for entity-creation acknowledgements.</summary>
    internal const string LogTopicCreateAck = "CreateEntityAck";

    /// <summary>DDS topic name used in interaction-log entries for selection events.</summary>
    internal const string LogTopicSelection = "SelectionChangedEvent";

    // ── DDS topic names ───────────────────────────────────────────────────────

    /// <summary>DDS topic name for <c>MissionControlRequest</c> messages.</summary>
    internal const string TopicMissionControl = "MissionControlRequest";

    /// <summary>DDS topic name for <c>ContextActionsUpdate</c> messages.</summary>
    internal const string TopicContextActions = "ContextActionsUpdate";

    /// <summary>DDS topic name for <c>MapCommandRequest</c> tool-activation messages.</summary>
    internal const string TopicMapCommand = "MapCommandRequest";
}
