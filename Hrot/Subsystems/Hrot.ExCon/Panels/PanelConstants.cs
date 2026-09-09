namespace Hrot.ExCon.Panels;

/// <summary>
/// Central repository for all named constants used by ExCon UI panels.
///
/// <para>Centralising here ensures that a capacity or threshold change is a
/// one-line edit (CODE-STANDARDS §1 — no magic numbers in production code).
/// </para>
/// </summary>
public static class PanelConstants
{
    // ── ConfigPanel ───────────────────────────────────────────────────────────

    /// <summary>Minimum value for the icon-scale slider.</summary>
    public const float IconScaleMin = 0.5f;

    /// <summary>Maximum value for the icon-scale slider.</summary>
    public const float IconScaleMax = 2.0f;

    /// <summary>Default icon scale shown when the Config panel is first opened.</summary>
    public const float IconScaleDefault = 1.0f;

    // ── InteractionPanel ──────────────────────────────────────────────────────

    /// <summary>
    /// Maximum number of log entries retained by <see cref="InteractionPanel"/>.
    /// When this cap is reached the oldest entry is evicted before inserting the
    /// new one, keeping memory consumption constant.
    /// </summary>
    public const int MaxLogEntries = 100;

    // ── OrbatPanel ────────────────────────────────────────────────────────────

    /// <summary>
    /// Defensive recursion depth cap for the ORBAT tree renderer.
    /// Prevents a stack overflow if circular <see cref="Hrot.NED.Descriptors.EntityInfo.CommanderId"/>
    /// relationships exist in malformed incoming data (e.g. unit A commands unit B
    /// which commands unit A).
    /// </summary>
    public const int MaxOrbatDepth = 32;

    // ── Text inputs ───────────────────────────────────────────────────────────

    /// <summary>
    /// ImGui input-text buffer size (in characters) for all filter / search
    /// text fields across the ExCon panels.
    /// </summary>
    public const int FilterTextMaxLength = 256;

    // ── Entity selection sentinel ────────────────────────────────────────────

    /// <summary>
    /// Sentinel entity-ID value used across ExCon panels/logic (e.g.
    /// <see cref="ExConLogic"/>) to indicate that no entity is currently selected.
    /// ⚠ <c>InspectorMaxTotalLines</c> was removed alongside <c>InspectorPanel</c>
    /// (U-obs-5 follow-up, deleted as a measured-dead <c>[Obsolete]</c> panel —
    /// <c>docs/UX/UX_Feature_DeadUI_Removal.md:102</c>); this sentinel survives
    /// because <see cref="ExConLogic"/> still reads it.
    /// </summary>
    public const int InspectorNoSelection = 0;

    // ── DiagnosticsPanel ──────────────────────────────────────────────────────

    /// <summary>
    /// Duration (in seconds) of the rolling event-rate sample window used by
    /// <see cref="DiagnosticsPanel"/>. After this window elapses a new
    /// events-per-second reading is committed and the counter resets.
    /// </summary>
    public const float DiagnosticsEventRateSampleWindowS = 5.0f;

    // ── MissionPanel – mission editing ──────────────────────────────────────

    /// <summary>
    /// Maximum number of characters allowed in the behavior-params JSON editor.
    /// </summary>
    public const int MissionBehaviorParamsMaxLength = 2048;

    /// <summary>
    /// Default travel speed (m/s) injected into a <c>MoveToLocation</c> params
    /// JSON when the operator uses the "Pick Location" map-pick workflow and
    /// does not supply an explicit speed value.
    /// </summary>
    public const float MoveToLocationDefaultSpeed = 15f;

    /// <summary>
    /// Default arrival radius (metres) injected into a <c>MoveToLocation</c>
    /// params JSON when the operator uses the "Pick Location" map-pick workflow
    /// and does not supply an explicit arrival radius.
    /// </summary>
    public const float MoveToLocationDefaultArrivalRadius = 50f;

    /// <summary>
    /// Number of text lines shown in the behavior-params editor.
    /// </summary>
    public const int MissionBehaviorParamsEditorLines = 4;

    // ── MissionPanel – conflict detection ─────────────────────────────────────

    /// <summary>
    /// The <c>ErrorMessage</c> string that identifies an optimistic-lock
    /// version-conflict result returned from the SimHost.
    /// Matches <c>MissionControlAck.ErrorMessage</c> when
    /// <c>ErrorCode == <see cref="VersionConflictErrorCode"/></c>.
    /// </summary>
    public const string VersionConflictErrorMessage = "ERR_VERSION_CONFLICT";

    /// <summary>
    /// The numeric error code that signals an optimistic-lock version conflict
    /// (<c>MissionControlAck.ErrorCode == 7</c>).
    /// </summary>
    public const int VersionConflictErrorCode = 7;

    // ── Entity filter presets ─────────────────────────────────────────────────

    /// <summary>
    /// Filter preset name for the <c>road_graphs</c> map layer.
    /// Used by the <see cref="MissionPanel"/> when launching a "Pick Route" entity pick.
    /// </summary>
    public const string FilterPresetRoadGraphs = "road_graphs";
}
