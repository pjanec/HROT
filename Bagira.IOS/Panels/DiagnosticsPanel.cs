using FDP.Toolkit.DER;
using Bagira.IOS.Services;

namespace Bagira.IOS.Panels;

/// <summary>
/// IOS UI panel that exposes runtime diagnostics:
/// <list type="bullet">
///   <item>Total entity count from the DER repository.</item>
///   <item>Live queue of pending (unresolved) DDS requests from the
///   <see cref="IRequestTransactionManager"/>.</item>
///   <item>Rolling DDS events-per-second metric.</item>
/// </list>
///
/// <para><b>Event-rate metric:</b> call <see cref="RecordEvent"/> once for
/// each DDS event processed.  Call <see cref="Update"/> once per frame with
/// the frame delta.  After each
/// <see cref="PanelConstants.DiagnosticsEventRateSampleWindowS"/>-second
/// window the committed rate is refreshed and the counter resets.</para>
///
/// <para><b>Testing:</b> <see cref="GetEntityCount"/>,
/// <see cref="GetPendingRequestSnapshot"/>, <see cref="RecordEvent"/>, and
/// <see cref="Update"/> are all exercisable without an ImGui context.</para>
/// </summary>
public sealed class DiagnosticsPanel
{
    // ── Rolling event-rate state ──────────────────────────────────────────────

    private int   _windowEventCount;
    private float _windowElapsedSeconds;
    private float _committedRate;

    // ── Public read-back ──────────────────────────────────────────────────────

    /// <summary>
    /// The most recently committed events-per-second reading.
    /// Updated once per
    /// <see cref="PanelConstants.DiagnosticsEventRateSampleWindowS"/>-second
    /// window.  Zero until the first full window elapses.
    /// </summary>
    public float EventsPerSecond => _committedRate;

    // ── Per-event and per-frame API ───────────────────────────────────────────

    /// <summary>
    /// Increments the in-progress event counter by one.  Call once for each
    /// DDS event processed (e.g. inside ingress handler callbacks or
    /// <c>IosLogic.Update</c>).
    /// </summary>
    public void RecordEvent() => _windowEventCount++;

    /// <summary>
    /// Advances the rolling sample window by <paramref name="dt"/> seconds.
    /// When the accumulated time reaches
    /// <see cref="PanelConstants.DiagnosticsEventRateSampleWindowS"/> the
    /// committed rate is refreshed and the counter resets.
    /// </summary>
    /// <param name="dt">Frame delta-time in seconds (from Raylib.GetFrameTime).</param>
    public void Update(float dt)
    {
        if (dt <= 0f) return;

        _windowElapsedSeconds += dt;

        if (_windowElapsedSeconds >= PanelConstants.DiagnosticsEventRateSampleWindowS)
        {
            _committedRate        = _windowEventCount / _windowElapsedSeconds;
            _windowEventCount     = 0;
            _windowElapsedSeconds = 0f;
        }
    }

    // ── Static query helpers (public for testability) ─────────────────────────

    /// <summary>Returns the total number of entities currently in <paramref name="repo"/>.</summary>
    public static int GetEntityCount(IDerRepo repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        return repo.GetAllEntities().Count();
    }

    /// <summary>
    /// Returns a snapshot list of all currently pending (unresolved) DDS
    /// requests held by <paramref name="txMgr"/>.
    /// </summary>
    public static IReadOnlyList<PendingRequest> GetPendingRequestSnapshot(
        IRequestTransactionManager txMgr)
    {
        ArgumentNullException.ThrowIfNull(txMgr);
        return txMgr.GetPendingRequests().ToList().AsReadOnly();
    }

    // ── Draw stub ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the diagnostics panel via ImGui.
    /// Called once per frame from the application shell (Phase P10).
    ///
    /// <para>All rendering code is commented out pending Raylib/rlImGui
    /// linkage.  The diagnostic methods are fully testable without an ImGui
    /// context.</para>
    /// </summary>
    public void Draw(IIosLogic logic)
    {
        // Phase P10 implementation:
        //
        // ImGui.Begin("Diagnostics");
        //
        // int entityCount = GetEntityCount(logic.Repo);
        // ImGui.Text($"Entities in Repo: {entityCount}");
        //
        // var pending = GetPendingRequestSnapshot(logic.TransactionManager);
        // ImGui.Text($"Pending DDS Requests: {pending.Count}");
        //
        // if (pending.Count > 0)
        // {
        //     ImGui.Indent();
        //     foreach (var req in pending)
        //     {
        //         double ageMs = (DateTime.UtcNow - req.SentTime).TotalMilliseconds;
        //         ImGui.Text($"[{req.RequestId:N}] {req.Description} ({ageMs:F0} ms)");
        //     }
        //     ImGui.Unindent();
        // }
        //
        // ImGui.Separator();
        // ImGui.Text($"DDS Events/s: {_committedRate:F1}");
        //
        // ImGui.End();
    }
}
