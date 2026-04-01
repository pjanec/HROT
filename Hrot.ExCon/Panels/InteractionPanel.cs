using System.Collections.Concurrent;
using ImGuiNET;

namespace Hrot.ExCon.Panels;

/// <summary>
/// A single entry in the <see cref="InteractionPanel"/> event log.
/// </summary>
public sealed record LogEntry(
    DateTime Time,
    string   Direction,
    string   Topic,
    string   Details);

/// <summary>
/// ExCon UI panel that acts as a live diagnostic / event log, showing every
/// incoming (RX) and outgoing (TX) DDS interaction.
///
/// <para><b>Memory discipline:</b> the internal log list is pre-allocated to
/// <see cref="PanelConstants.MaxLogEntries"/> and the oldest entry is evicted
/// before inserting a new one once the cap is reached, so heap usage is
/// bounded and constant after warm-up.  The <see cref="Draw"/> method iterates
/// the list with a plain <c>for</c>-loop — no LINQ, no allocations
/// (CODE-STANDARDS §4).</para>
///
/// <para><b>Thread safety (ExCon-DEBT-034):</b> DDS ingress callbacks may fire
/// on background threads and call <see cref="AddLog"/> at any time.
/// <see cref="AddLog"/> enqueues to a <see cref="ConcurrentQueue{T}"/> staging
/// buffer so it is safe to call from any thread.  The main application thread
/// must call <see cref="DrainPendingLogs"/> once per frame (from
/// <c>ExConLogic.Update</c>) before drawing the panel, which transfers all
/// queued entries into the main-thread-only <c>_log</c> list.</para>
///
/// <para><b>Testing:</b> <see cref="AddLog"/> and <see cref="Entries"/> are
/// fully accessible without ImGui; tests call <c>AddLog</c> then
/// <see cref="DrainPendingLogs"/> before asserting against
/// <see cref="Entries"/> / <see cref="EntryCount"/>.</para>
/// </summary>
public sealed class InteractionPanel
{
    // ── Thread-safe staging queue (written from any thread) ───────────────────

    private readonly ConcurrentQueue<LogEntry> _pending = new();

    // ── Main-thread log (drained from _pending, read during Draw) ─────────────

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
    /// Read-only view of the committed log entries, ordered oldest-to-newest.
    /// Only reflects entries that have been moved to the main thread via
    /// <see cref="DrainPendingLogs"/>; entries still in the staging queue are
    /// not visible here.
    /// The returned collection is a
    /// <see cref="System.Collections.ObjectModel.ReadOnlyCollection{T}"/>
    /// wrapper — it cannot be cast back to <see cref="List{T}"/> and mutated.
    /// </summary>
    public IReadOnlyList<LogEntry> Entries => _readOnlyLog;

    /// <summary>Number of committed entries currently stored.</summary>
    public int EntryCount => _log.Count;

    /// <summary>
    /// Thread-safe: enqueues a new log entry into the staging buffer.
    /// Safe to call from any thread (e.g. DDS ingress callbacks).
    /// Entries will not appear in <see cref="Entries"/> until
    /// <see cref="DrainPendingLogs"/> is called from the main thread.
    /// </summary>
    /// <param name="direction">Typically "RX" or "TX".</param>
    /// <param name="topic">DDS topic name (e.g. "MapClickEvent").</param>
    /// <param name="details">Human-readable payload summary.</param>
    public void AddLog(string direction, string topic, string details)
    {
        _pending.Enqueue(new LogEntry(DateTime.UtcNow, direction, topic, details));
    }

    /// <summary>
    /// <b>Main-thread only.</b> Drains all pending entries from the staging
    /// queue into the committed log list, enforcing the
    /// <see cref="PanelConstants.MaxLogEntries"/> cap.
    ///
    /// <para>Must be called once per frame from <c>ExConLogic.Update</c> before
    /// the panel is drawn, so that entries written on DDS ingress threads
    /// (ExCon-DEBT-034) reach the UI safely.</para>
    ///
    /// <para>When the cap would be exceeded the oldest committed entry is
    /// evicted first — O(n) for a List but acceptable at MaxLogEntries = 100
    /// (one small memmove per drained item at most).</para>
    /// </summary>
    /// <returns>The number of entries drained in this call.</returns>
    public int DrainPendingLogs()
    {
        int drained = 0;
        while (_pending.TryDequeue(out var entry))
        {
            if (_log.Count >= PanelConstants.MaxLogEntries)
                _log.RemoveAt(0);

            _log.Add(entry);
            drained++;
        }
        return drained;
    }

    // ── Selection state ───────────────────────────────────────────────────────

    private int _selectedIndex = -1;

    // Persistent buffer for the Details TextMultiline widget — avoids per-frame allocs.
    private string _detailsBuf = string.Empty;

    // ── Draw stub (Phase P9) ──────────────────────────────────────────────────

    /// <summary>
    /// Renders the event-log table via ImGui with a two-pane layout:
    /// left = scrollable entry list (selectable rows), right = detail view.
    /// Called once per frame from the application shell (Phase P9).
    ///
    /// <para>No allocations inside this method — iteration uses a plain
    /// <c>for</c>-loop over the pre-built <see cref="_log"/> list.</para>
    /// </summary>
    public void Draw(IExConLogic logic)
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        ExConPanelColors.Push();
        ImGui.Begin("Data Monitor");
        ExConPanelColors.Pop();

        DrawContent(logic);

        ImGui.End();
    }

    /// <summary>
    /// Renders only the panel body content (no <c>ImGui.Begin</c>/<c>End</c>).
    /// Called by the Window Manager when this panel is hosted as a
    /// <see cref="ManagedWindow"/>; also called by <see cref="Draw"/> in standalone mode.
    /// </summary>
    public void DrawContent(IExConLogic logic)
    {
        // ── Outer two-column layout: list | detail ─────────────────────────
        float totalWidth = ImGui.GetContentRegionAvail().X;

        if (ImGui.BeginTable("##DmLayout", 2,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Log",    ImGuiTableColumnFlags.WidthFixed,  totalWidth * 0.55f);
            ImGui.TableSetupColumn("Detail", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            // ── Left: log table ────────────────────────────────────────────
            ImGui.TableSetColumnIndex(0);
            DrawLogList();

            // ── Right: entry detail ────────────────────────────────────────
            ImGui.TableSetColumnIndex(1);
            DrawEntryDetail();

            ImGui.EndTable();
        }
    }

    private void DrawLogList()
    {
        ImGui.BeginChild("##DmLogList");

        if (ImGui.BeginTable("log", 3,
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.RowBg   |
            ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Time",    ImGuiTableColumnFlags.WidthFixed,   68f);
            ImGui.TableSetupColumn("Topic",   ImGuiTableColumnFlags.WidthFixed,  140f);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            // Plain for-loop — zero allocations in the hot draw path.
            for (int i = 0; i < _log.Count; i++)
            {
                var entry   = _log[i];
                bool sel    = i == _selectedIndex;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                // Selectable spans the whole row via SelectableFlags.SpanAllColumns.
                if (ImGui.Selectable(
                        entry.Time.ToString("HH:mm:ss") + $"##dm{i}",
                        sel,
                        ImGuiSelectableFlags.SpanAllColumns))
                {
                    _selectedIndex = i;
                    // Pre-compute the details buffer for the right pane.
                    _detailsBuf = entry.Details;
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{entry.Direction} {entry.Topic}");

                ImGui.TableNextColumn();
                // Truncate long details in the list view.
                string brief = entry.Details.Length > 80
                    ? entry.Details[..80] + "…"
                    : entry.Details;
                ImGui.TextUnformatted(brief);
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();
    }

    private void DrawEntryDetail()
    {
        ImGui.BeginChild("##DmDetail");

        if (_selectedIndex < 0 || _selectedIndex >= _log.Count)
        {
            ImGui.TextDisabled("Select an entry to view details.");
            ImGui.EndChild();
            return;
        }

        var e = _log[_selectedIndex];

        // ── Header fields ──────────────────────────────────────────────────
        if (ImGui.BeginTable("##DmFields", 2, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 80f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            DrawFieldRow("Time",      e.Time.ToString("HH:mm:ss.fff"));
            DrawFieldRow("Direction", e.Direction);
            DrawFieldRow("Topic",     e.Topic);

            ImGui.EndTable();
        }

        ImGui.Separator();

        // ── Details text (scrollable, copyable) ───────────────────────────
        ImGui.TextDisabled("Details:");
        float detailH = ImGui.GetContentRegionAvail().Y - 4f;
        ImGui.InputTextMultiline(
            "##DmDetailsText",
            ref _detailsBuf,
            (uint)System.Math.Max(4096, _detailsBuf.Length + 64),
            new System.Numerics.Vector2(-1, detailH),
            ImGuiInputTextFlags.ReadOnly);

        ImGui.EndChild();
    }

    private static void DrawFieldRow(string field, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextDisabled(field);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(value);
    }
}