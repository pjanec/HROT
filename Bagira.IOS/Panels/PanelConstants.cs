namespace Bagira.IOS.Panels;

/// <summary>
/// Central repository for all named constants used by IOS UI panels.
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
    /// Prevents a stack overflow if circular <see cref="Bagira.BDC.SSTD.EntityInfo.CommanderId"/>
    /// relationships exist in malformed incoming data (e.g. unit A commands unit B
    /// which commands unit A).
    /// </summary>
    public const int MaxOrbatDepth = 32;

    // ── Text inputs ───────────────────────────────────────────────────────────

    /// <summary>
    /// ImGui input-text buffer size (in characters) for all filter / search
    /// text fields across the IOS panels.
    /// </summary>
    public const int FilterTextMaxLength = 256;
}
