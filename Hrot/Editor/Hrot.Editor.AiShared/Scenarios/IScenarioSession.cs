using System.Collections.Generic;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;

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
/// <see cref="ClearWorld"/> is the LOCAL wipe; <see cref="NewExercise"/> is the CLUSTER-WIDE reset.
/// 📐 Measured <c>2026-08-26</c>: the load state machine calls the local wipe as <i>step 1 of its own
/// sequence</i> *(<c>EditorApplication.Update</c>)*, so collapsing the two would make every load publish
/// a second <c>Idle</c> intent from inside the handler for the first one. ⇒ ⛔ the design's single
/// <c>NewExercise()</c> member is <b>as-built as two</b>; recorded in the design §4.</para>
/// </summary>
public interface IScenarioSession
{
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
    /// ⭐⭐⭐ <b>The CLUSTER-WIDE reset</b> — finishes any running exercise and starts fresh
    /// *(<c>File/Live/New Exercise</c>)*. Requests <see cref="ClusterState.Idle"/> of the master and wipes
    /// locally. ⚠ <b>Destructive</b>: the caller owns the confirmation *(ruling 53 — the confirm belongs
    /// where the operator sits, and a headless node logs instead of prompting)*.
    /// </summary>
    void NewExercise();

    /// <summary>
    /// ⭐⭐⭐ <b>Load for RUNNING, cluster-wide</b> — the <c>/scenario/load/live</c> path.
    /// Publishes <c>TransitionStateIntent{OperatingLive}</c> with a <b>fresh</b> <c>ExerciseId</c>: a live
    /// load IS a new exercise run, and that id is what recording/replay keys off.
    /// </summary>
    void LoadForLive(string scenarioName);

    /// <summary>
    /// ⭐⭐⭐ <b>Open for AUTHORING, cluster-wide</b> — the <c>/scenario/load/edit</c> path.
    /// Asks for <see cref="ClusterState.Idle"/>, wipes locally once idle, then requests
    /// <c>OperatingEdit</c>. ⭐ <c>ExerciseId</c> is deliberately fresh-per-transaction here as it always
    /// was; authoring is not an exercise run, which is why the direct cluster-intent arm in
    /// <c>DebugApiService.LoadScenarioEdit</c> uses <c>Guid.Empty</c>.
    /// </summary>
    void OpenForEdit(string scenarioName);

    /// <summary>Saves the world into the scenario most recently loaded. No-op when none is loaded.</summary>
    void SaveCurrent();

    /// <summary>Saves the world as a new scenario and remembers the name for <see cref="SaveCurrent"/>.</summary>
    void SaveAs(string scenarioName);

    /// <summary>Serializes the world to an explicit file path *(the raw round-trip seam)*.</summary>
    void SaveTo(string filePath);

    /// <summary>
    /// ⭐⭐ <b>Saves the live running state</b> — publishes the existing <c>TakeCheckpointIntent</c> to the
    /// master *(<c>File/Checkpoint/Take Checkpoint</c>)*.
    ///
    /// <para>⚠ <b>DEVIATION from design §4</b>, argued: the class diagram draws
    /// <c>ScenarioMenuCommands ..&gt; TakeCheckpointIntent</c> — the menu publishing directly. ⛔ That would
    /// put the same one-line publish in BOTH hosts' composition roots *(two implementations of one
    /// concept — ruling 9)*. ⭐ The session already holds the orchestration bus, so it is the single home.
    /// Folded into the design's as-built section.</para>
    /// </summary>
    void TakeCheckpoint();

    /// <summary>The currently loaded scenario name, or <c>null</c> after a clear.</summary>
    string? LoadedScenarioName { get; }

    /// <summary>True when the loaded scenario came up in degraded mode *(snapshot fallback)*.</summary>
    bool IsDegraded { get; }

    /// <summary>The migration sidecars stored alongside the loaded scenario; empty when none.</summary>
    IReadOnlyList<SidecarFileInfo> GetMigrationSidecars();
}
