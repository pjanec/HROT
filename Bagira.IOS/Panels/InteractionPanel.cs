namespace Bagira.IOS.Panels;

/// <summary>
/// A single entry in the <see cref="InteractionPanel"/> event log.
/// </summary>
public sealed record LogEntry(
    DateTime Time,
    string   Direction,
    string   Topic,
    string   Details);

/// <summary>
/// IOS UI panel that acts as a live diagnostic / event log, showing every
/// incoming (RX) and outgoing (TX) DDS interaction.
///
/// <para><b>Memory discipline:</b> the internal log list is pre-allocated to
/// <see cref="PanelConstants.MaxLogEntries"/> and the oldest entry is evicted
/// before inserting a new one once the cap is reached, so heap usage is
/// bounded and constant after warm-up.  The <see cref="Draw"/> method iterates
/// the list with a plain <c>for</c>-loop — no LINQ, no allocations
/// (CODE-STANDARDS §4).</para>
///
/// <para><b>Testing:</b> <see cref="AddLog"/> and <see cref="Entries"/> are
/// fully accessible without ImGui; tests simply call <c>AddLog</c> and assert
/// against <see cref="Entries"/> / <see cref="EntryCount"/>.</para>
/// </summary>
public sealed class InteractionPanel
{
    // ── Internal log (pre-allocated to avoid later heap churn) ────────────────

    private readonly List<LogEntry> _log;

    // Cached read-only wrapper — avoids allocating a new wrapper on every
    // Entries access while still preventing external mutation through a cast.
    private readonly System.Collections.ObjectModel.ReadOnlyCollection<LogEntry> _readOnlyLog;

    // ── Constructor ───────────────────────────────────────────────────────────

    public InteractionPanel()
    {
        // Pre-allocate exactly MaxLogEntries capacity so no further
        // array resize occurs during normal operation (CODE-STANDARDS §4).
        _log         = new List<LogEntry>(PanelConstants.MaxLogEntries);
        _readOnlyLog = _log.AsReadOnly();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Read-only view of the current log entries, ordered oldest-to-newest.
    /// The returned collection is a <see cref="System.Collections.ObjectModel.ReadOnlyCollection{T}"/>
    /// wrapper — it cannot be cast back to <see cref="List{T}"/> and mutated.
    /// </summary>
    public IReadOnlyList<LogEntry> Entries => _readOnlyLog;

    /// <summary>Number of entries currently stored.</summary>
    public int EntryCount => _log.Count;

    /// <summary>
    /// Appends a new log entry.  When the cap
    /// (<see cref="PanelConstants.MaxLogEntries"/>) would be exceeded the
    /// oldest entry is removed first, keeping memory use constant.
    /// </summary>
    /// <param name="direction">Typically "RX" or "TX".</param>
    /// <param name="topic">DDS topic name (e.g. "MapClickEvent").</param>
    /// <param name="details">Human-readable payload summary.</param>
    public void AddLog(string direction, string topic, string details)
    {
        // Evict the oldest entry when at capacity — O(n) for a List but
        // acceptable at MaxLogEntries = 100 (one small memmove per frame max).
        if (_log.Count >= PanelConstants.MaxLogEntries)
            _log.RemoveAt(0);

        _log.Add(new LogEntry(DateTime.UtcNow, direction, topic, details));
    }

    // ── Draw stub (Phase P9) ──────────────────────────────────────────────────

    /// <summary>
    /// Renders the event-log table via ImGui.
    /// Called once per frame from the application shell (Phase P9).
    ///
    /// <para>No allocations inside this method — iteration uses a plain
    /// <c>for</c>-loop over the pre-built <see cref="_log"/> list.</para>
    /// </summary>
    public void Draw(IIosLogic logic)
    {
        // Phase P9 implementation:
        // ImGui.Begin("Data Monitor");
        //
        // if (ImGui.BeginTable("log", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY))
        // {
        //     ImGui.TableSetupColumn("Time");
        //     ImGui.TableSetupColumn("Topic");
        //     ImGui.TableSetupColumn("Details");
        //     ImGui.TableHeadersRow();
        //
        //     // Plain for-loop — zero allocations in the hot draw path.
        //     for (int i = 0; i < _log.Count; i++)
        //     {
        //         var entry = _log[i];
        //         ImGui.TableNextRow();
        //         ImGui.TableNextColumn(); ImGui.Text(entry.Time.ToString("HH:mm:ss"));
        //         ImGui.TableNextColumn(); ImGui.Text($"{entry.Direction} {entry.Topic}");
        //         ImGui.TableNextColumn(); ImGui.Text(entry.Details);
        //     }
        //
        //     ImGui.EndTable();
        // }
        //
        // ImGui.End();
    }
}
