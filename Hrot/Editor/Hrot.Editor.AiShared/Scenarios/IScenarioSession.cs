using System;
using System.Collections.Generic;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.RunMode;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐⭐⭐ <b>The host-agnostic scenario session — the scenario half of the editor, shared.</b>
/// 📄 <b><c>docs/DESIGN_Cgf_Scenario_Session_Slice.md</c></b> §3 ① / §4 *(the authoritative class
/// diagram)*; <c>AQ60</c> §3b/§4/§4b rulings <b>R1</b> *(whole editor → shared)*, <b>R2</b> *(distinct
/// menu items, no chameleons)*, <b>R3</b> *(the toolbar is its own design)*. Axis-C increment <b>E1</b>.
///
/// <para>🎯 <b>The user's principle</b> *(<c>2026-08-26</c>)*: CGF ≡ the editor bar
/// distributed-vs-no-network ⇒ <b>most stuff shared, minimal duplication</b>. This interface is what
/// makes the scenario verbs available on <i>any</i> host that owns a world and an orchestration bus,
/// rather than only on the one assembly that happened to grow them.</para>
///
/// <para>⭐⭐ <b>Why an interface and not just the class.</b> <c>IEditorLogic</c> is the editor's panel
/// facade and cannot be shared *(assembly wall <c>Hrot.Editor → Hrot.CGF</c>)*, and
/// <see cref="ScenarioMenuCommands"/>-style registrars must bind to something headless-testable. ⛔ The
/// registrar binding to the editor-only <c>IEditorLogic</c> WAS the wall — design §2, measured.</para>
///
/// <para>⚠⚠ <b>TWO clear verbs, not one, and the difference is load-bearing.</b>
/// <see cref="ClearWorld"/> is the LOCAL wipe; <see cref="Close"/> leaves the open scenario for Idle CLUSTER-WIDE.
/// 📐 Measured <c>2026-08-26</c>: the load state machine calls the local wipe as <i>step 1 of its own
/// sequence</i> *(<c>EditorApplication.Update</c>)*, so collapsing the two would make every load publish
/// a second <c>Idle</c> intent from inside the handler for the first one. ⇒ ⛔ the design's single
/// <c>NewExercise()</c> member is <b>as-built as two</b>; recorded in the design §4.</para>
/// </summary>
public interface IScenarioSession
{
    // ── ⭐⭐⭐ CE-4101 (AQ108 §2.11a) — THE GUARDED DOOR. ⛔ This interface does NOT extend IRunScenarioControl any more: that one holds the RAW verbs (open / load / open a recording WITHOUT
    //    asking), handed only to the run choice. Every member below is the guarded one — a run ⇒ Stop & Load · unsaved edits ⇒ Save / Discard / Cancel (the session's Door); a caller that holds a session
    //    can only reach the door.

    /// <summary>The currently loaded scenario name, or <c>null</c> after a clear.</summary>
    string? LoadedScenarioName { get; }

    /// <summary>⭐ GUARDED — opens <paramref name="scenarioName"/> for AUTHORING, cluster-wide (the door asks <i>Stop &amp; Load</i> over a run, <i>Save / Discard / Cancel</i> over unsaved edits).</summary>
    void OpenForEdit(string scenarioName);

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1443</c> (U2a, R-1043) — <b>THE operator's scenario load</b>, GUARDED like every verb here: it goes into the cluster's CURRENT armed mode (Live armed ⇒ <see cref="LoadForLive"/>, Preview ⇒ <see cref="OpenForEdit"/>, AAR ⇒ the last load's mode),
    /// resolved ONCE in the run choice. The File menu, Start's Recent rows and every picker call THIS. ⛔ Sends no armed mode. A default member (a fake, or a session with no door, opens for editing).
    /// </summary>
    void LoadScenario(string scenarioName) => OpenForEdit(scenarioName);

    /// <summary>⭐ GUARDED — loads <paramref name="scenarioName"/> for RUNNING, cluster-wide (a fresh exercise; the same decision as <see cref="OpenForEdit"/>).</summary>
    void LoadForLive(string scenarioName);

    /// <summary>⭐ GUARDED — opens a RECORDING into AAR (the same decision as <see cref="OpenForEdit"/>).</summary>
    void OpenRecording(Guid exerciseId);

    /// <summary>⭐ GUARDED — <see cref="OpenRecording(Guid)"/> at a moment of the recording's own sim clock; <see langword="null"/> = its start. A default member so a fake need not state it.</summary>
    void OpenRecording(Guid exerciseId, double? atSimSeconds) => OpenRecording(exerciseId);

    /// <summary>Saves the world into the scenario most recently loaded. No-op when none is loaded.</summary>
    void SaveCurrent();

    /// <summary>Saves the live running state — publishes the existing <c>TakeCheckpointIntent</c> to the master.</summary>
    void TakeCheckpoint();

    /// <summary>
    /// ⭐⭐ Pumps the session once per frame: drains <see cref="ClusterStateUpdateEvent"/> and advances the
    /// deferred-load state machine *(a load first asks the cluster for <see cref="ClusterState.Idle"/>,
    /// then wipes and requests the target state once idle is observed)*.
    /// </summary>
    /// <remarks>⚠ <b>Main thread only</b> — it both consumes the orchestration events and publishes.</remarks>
    void Update();

    /// <summary>The cluster lifecycle state as last observed by <see cref="Update"/>.</summary>
    ClusterState CurrentClusterState { get; }

    /// <summary>
    /// ⭐ <b>The LOCAL wipe</b> — clears the world and resets time to zero. ⛔ Publishes nothing: this is
    /// the step the load state machine performs before requesting its target state, and it is what
    /// <c>IEditorLogic.NewScenario()</c> has always meant.
    /// </summary>
    void ClearWorld();

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1443</c> (U2a, AQ108 §2.15) — <b>Close</b>: back to Idle (the Start perspective), through the ONE door (<c>Door.Leave(LeaveReason.Close)</c>): a run is stopped (■ never asks), editing with unsaved edits asks <i>Save / Discard / Cancel</i>,
    /// a cluster in its ERROR state asks only <i>discard and close?</i>. ⛔ Exempt from the readiness gate. REPLACES the withdrawn <i>New Exercise</i> (its local world wipe is gone — the unload to Idle clears the world on every node).
    /// </summary>
    void Close();

    /// <summary>
    /// ⭐ <c>CE-1443</c> — restores a checkpoint of the running exercise by its LABEL: publishes the existing <c>RestoreCheckpointIntent</c> (the master pauses, every node stages the keyframe, then all restore and the cluster records on as a NEW exercise, D18). The master accepts it
    /// only in <c>OperatingLive</c> of a recorded run. A default member (a fake does nothing).
    /// </summary>
    void RestoreCheckpoint(string label) { }

    /// <summary>Saves the world as a new scenario and remembers the name for <see cref="SaveCurrent"/>.</summary>
    void SaveAs(string scenarioName);

    /// <summary>Serializes the world to an explicit file path *(the raw round-trip seam)*.</summary>
    void SaveTo(string filePath);

    /// <summary>True when the loaded scenario came up in degraded mode *(snapshot fallback)*.</summary>
    bool IsDegraded { get; }

    /// <summary>The migration sidecars stored alongside the loaded scenario; empty when none.</summary>
    IReadOnlyList<SidecarFileInfo> GetMigrationSidecars();
}
