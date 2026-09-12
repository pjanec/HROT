using System.Numerics;
using Fdp.Core.Serialization.Migrations.Adapters;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// Manages the per-session migration alert state for the editor UI.
/// Tracks the most recent load result, queues one-time modal display
/// for migrations, and exposes the degraded-mode flag for the browser panel.
/// </summary>
/// <remarks>
/// <para>Call <see cref="OnScenarioLoaded"/> immediately after each scenario load completes.
/// Call <see cref="Draw"/> once per frame from within an active ImGui window.</para>
///
/// <para>⭐⭐ <b><c>CE-046</c> — moved here from <c>Hrot.Editor/Migration/</c> with
/// <see cref="EditorScenarioSession"/>, which owns it.</b> It is the scenario session's alert state, so
/// it had to travel with the session for CGF to get the same behaviour. ⭐ <b>PUBLIC now</b>, not
/// <c>internal</c>: <c>Hrot.Editor</c> is not in this assembly's <c>InternalsVisibleTo</c> list, and
/// <see cref="IScenarioSession.IsDegraded"/> is the member <c>IEditorLogic.IsScenarioDegraded</c>
/// forwards to.</para>
///
/// <para>⚠⚠ <b>MEASURED, <c>2026-08-26</c>: <see cref="Draw"/> has NO production caller.</b> The only
/// reference to the owning property was <c>EditorApplication.AlertManager</c> *(internal)*, and nothing
/// read it — so the degraded banner and the migration modal have never been drawn. ⛔ <b>NOT deleted</b>
/// *(CLAUDE.md: unreferenced is not unintentional — the class documents its own contract and
/// <see cref="IsDegradedMode"/> IS consumed via <c>IEditorLogic.IsScenarioDegraded</c>)</b>; the gap is
/// REPORTED as a finding instead. ⭐ Wiring <see cref="Draw"/> to a frame is a separate item.</para>
/// </remarks>
public sealed class MigrationAlertManager
{
    private MigrationLoadResult? _pendingAlert;     // non-null = modal not yet shown
    private MigrationLoadResult? _currentResult;    // tracks currently-loaded file
    private bool _suppressedForSession;             // checkbox state

    // ── State queries (used by tests and IEditorLogic implementations) ────────

    /// <summary>
    /// True when a migration-warning modal has been queued but not yet dismissed.
    /// </summary>
    public bool HasPendingAlert => _pendingAlert != null;

    /// <summary>
    /// True when the currently loaded scenario was loaded via degraded-mode
    /// snapshot fallback.
    /// </summary>
    public bool IsDegradedMode => _currentResult?.IsDegraded == true;

    // ── Mutators ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called after each scenario load. Queues a migration alert if
    /// <paramref name="result"/> reports that migration occurred and the user
    /// has not suppressed alerts for this session.
    /// </summary>
    public void OnScenarioLoaded(MigrationLoadResult? result)
    {
        _currentResult = result;
        _pendingAlert  = null;   // reset for the new file

        if (result == null) return;
        if (result.WasMigrated && !_suppressedForSession)
            _pendingAlert = result;
    }

    /// <summary>Resets migration alert state when the world is cleared (new scenario).</summary>
    public void OnScenarioCleared()
    {
        _currentResult = null;
        _pendingAlert  = null;
    }

    /// <summary>
    /// Marks alerts as suppressed for this session.
    /// Called by the ImGui checkbox in <see cref="Draw"/>; exposed for testing.
    /// </summary>
    internal void SuppressAlertsForSession() => _suppressedForSession = true;

    // ── ImGui rendering ───────────────────────────────────────────────────────

    /// <summary>
    /// Renders pending migration modal and/or degraded-mode banner.
    /// Must be called from within an active ImGui window context (i.e., inside
    /// a ManagedWindow's DrawClientArea) once per frame.
    /// </summary>
    public void Draw()
    {
        DrawDegradedBanner();
        DrawMigrationModal();
    }

    // ── Private rendering helpers ─────────────────────────────────────────────

    private void DrawDegradedBanner()
    {
        if (_currentResult?.IsDegraded != true) return;

        var originalVersion = _currentResult.OriginalMeta.SchemaVersion;
        var currentVersion  = _currentResult.CurrentMeta.SchemaVersion;

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.6f, 0f, 1f));
        ImGui.TextWrapped(
            $"[!] DEGRADED MODE: file is v{originalVersion} (binary supports v{currentVersion}). " +
            "A snapshot fallback is in use. Saving will LOSE newer-version data.");
        ImGui.PopStyleColor();
        ImGui.Separator();
    }

    private void DrawMigrationModal()
    {
        if (_pendingAlert == null) return;

        var alertResult = _pendingAlert;
        ImGui.OpenPopup("Scenario Migrated##migration_alert");
        _pendingAlert = null;

        bool open = true;
        if (!ImGui.BeginPopupModal("Scenario Migrated##migration_alert", ref open,
                ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextWrapped(
            $"This scenario has been migrated from v{alertResult.OriginalMeta.SchemaVersion} " +
            $"to v{alertResult.CurrentMeta.SchemaVersion}.\n" +
            "A backup of the original file was saved to the .migration-snapshots/ directory.");
        ImGui.Spacing();

        ImGui.Checkbox("Don't show this again for this session", ref _suppressedForSession);
        ImGui.Spacing();

        if (ImGui.Button("OK", new Vector2(120f, 0f)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }
}
