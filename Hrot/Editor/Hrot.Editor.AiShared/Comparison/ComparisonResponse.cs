namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Parsed result of an LLM comparison response. See design section 5.
/// </summary>
public sealed record ComparisonResponse(
    /// <summary>Prose summary extracted from the HUMAN SUMMARY section. Null when unrecoverable.</summary>
    string? HumanSummary,
    /// <summary>One-sentence top-level description from the JSON "summary" field.</summary>
    string TopLevelSummary,
    /// <summary>Parsed list of semantic changes from the JSON "changes" array.</summary>
    IReadOnlyList<ComparisonChange> Changes,
    /// <summary>Non-fatal warnings: unknown kinds/severities, missing fields, truncation.</summary>
    IReadOnlyList<string> Warnings);

/// <summary>
/// A single semantic change entry from the LLM's structured JSON response.
/// Kind and Severity are stored as strings to survive unknown values without throwing.
/// See design section 5.2 for enum value descriptions.
/// </summary>
public sealed record ComparisonChange(
    /// <summary>
    /// Change kind (e.g., "node_added", "variable_renamed"). Unknown values are normalized
    /// to "node_modified" by the parser with a warning.
    /// </summary>
    string Kind,
    /// <summary>VisualId/stableId/Id of the affected element, or null for asset-wide changes.</summary>
    string? ElementId,
    /// <summary>Human-readable description of which element was affected.</summary>
    string ElementDescription,
    /// <summary>For node_modified or variable_retyped, the specific field that changed; null otherwise.</summary>
    string? Field,
    /// <summary>The prior value for changes with before/after; null otherwise.</summary>
    string? OldValue,
    /// <summary>The new value for changes with before/after; null otherwise.</summary>
    string? NewValue,
    /// <summary>
    /// Severity (e.g., "cosmetic", "tuning", "behavior"). Unknown values are normalized
    /// to "tuning" by the parser with a warning.
    /// </summary>
    string Severity,
    /// <summary>1-3 sentence explanation of the change and its likely impact.</summary>
    string Description);
