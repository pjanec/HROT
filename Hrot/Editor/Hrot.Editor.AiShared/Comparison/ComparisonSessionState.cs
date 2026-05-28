namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Holds the parsed LLM comparison response and severity-filter state for a single asset.
/// See design sections 6.2 and 6.3.
/// </summary>
public sealed class ComparisonSessionState
{
    private readonly HashSet<string> _enabledSeverities;

    /// <summary>The asset this comparison session belongs to.</summary>
    public Guid AssetId { get; }

    /// <summary>The parsed LLM response for this comparison.</summary>
    public ComparisonResponse Response { get; }

    /// <summary>
    /// Migration notice to display when version A was migrated to a newer schema before comparison.
    /// Null for same-schema comparisons.
    /// </summary>
    public string? MigrationNotice { get; }

    /// <summary>
    /// True when the asset was saved after the comparison was loaded, meaning the prose
    /// summary may no longer reflect the current asset state.
    /// </summary>
    public bool IsStale { get; private set; }

    /// <summary>
    /// Currently-enabled severity levels for canvas annotation filtering.
    /// Defaults: behavior, feature, removal, tuning enabled; cosmetic disabled.
    /// </summary>
    public IReadOnlySet<string> EnabledSeverities => _enabledSeverities;

    /// <summary>
    /// Creates a new session state with default severity filter
    /// (behavior, feature, removal, tuning enabled; cosmetic disabled).
    /// </summary>
    public ComparisonSessionState(Guid assetId, ComparisonResponse response, string? migrationNotice = null)
    {
        AssetId = assetId;
        Response = response;
        MigrationNotice = migrationNotice;
        IsStale = false;
        _enabledSeverities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "behavior",
            "feature",
            "removal",
            "tuning",
        };
    }

    /// <summary>
    /// Toggles the given severity on or off. If currently enabled, disables it; otherwise enables it.
    /// </summary>
    public void ToggleSeverity(string severity)
    {
        if (!_enabledSeverities.Remove(severity))
            _enabledSeverities.Add(severity);
    }

    /// <summary>Marks the comparison as stale (the underlying asset was modified after load).</summary>
    public void MarkStale()
    {
        IsStale = true;
    }
}

/// <summary>
/// Singleton registry of active comparison sessions, keyed by AssetId.
/// See design section 6.9.
/// </summary>
public sealed class ComparisonSessionRegistry
{
    private readonly Dictionary<Guid, ComparisonSessionState> _sessions = new();

    /// <summary>Returns the comparison session for the given asset, or null if none is active.</summary>
    public ComparisonSessionState? GetSession(Guid assetId)
        => _sessions.TryGetValue(assetId, out var state) ? state : null;

    /// <summary>Stores or replaces the comparison session for <see cref="ComparisonSessionState.AssetId"/>.</summary>
    public void SetSession(ComparisonSessionState session)
        => _sessions[session.AssetId] = session;

    /// <summary>Removes any active comparison session for the given asset.</summary>
    public void ClearSession(Guid assetId)
        => _sessions.Remove(assetId);
}
