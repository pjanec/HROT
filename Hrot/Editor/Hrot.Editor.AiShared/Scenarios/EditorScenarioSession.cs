using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;
using Hrot.ScenarioEditor.Services;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐⭐⭐ <b>The one implementation of <see cref="IScenarioSession"/> — instantiated by EVERY host.</b>
/// 📄 <b><c>docs/DESIGN_Cgf_Scenario_Session_Slice.md</c></b> §3 ①/③, §4 *(class diagram)*, §5
/// *(sequence)*. Axis-C increment <b>E1</b>; ruling <b>R1</b> — the whole editor moves to shared, and
/// nothing here is capability-level editor-only.
///
/// <para>⭐⭐ <b>The world is a CONSTRUCTOR PARAMETER, and that is the whole reason this slice is
/// cheap.</b> 📐 Measured <c>2026-08-26</c> *(design §2)*: every dependency the scenario half of
/// <c>EditorApplication</c> touched was already engine- or shared-level, and the world was already
/// injected. ⇒ ⛔ nothing had to be re-architected for CGF; the same class simply runs over
/// <c>_context.World</c> and <c>_context.EventBus</c> there.</para>
///
/// <para>⭐⭐⭐ <b>The DEFERRED-LOAD state machine is preserved EXACTLY.</b> An edit-open first asks the
/// cluster for <see cref="ClusterState.Idle"/>; only when <see cref="Update"/> observes idle does it wipe
/// locally and request the target. ⚠ <b>Do not "simplify" this into a single publish</b> — the two-phase
/// shape is what keeps a reload from materialising into a world that is still standing, and it is what
/// <c>DebugApiService.LoadScenarioEdit</c> calls the *"editor driver"* and deliberately prefers.</para>
///
/// <para>⭐ <b>A live load has NO idle hop</b>, deliberately — <c>DebugApiService.LoadScenarioLive</c>:
/// *"Uniform in every host — there is no editor-only live-load driver to prefer."* ⇒ the asymmetry is
/// the design, not an oversight; each node's own live-load handler does its own clearing.</para>
/// </summary>
public sealed class EditorScenarioSession : IScenarioSession, IRunScenarioControl, IHasScenarioDoor, Hrot.UI.Common.RunMode.ISaveInterception
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-4101</c> (AQ108 §2.11a) — <b>the ONE guarded door in and out of the open scenario.</b> This session's PUBLIC <see cref="OpenForEdit"/> / <see cref="LoadForLive"/> / <see cref="OpenRecording(Guid)"/> /
    /// <see cref="NewExercise"/> are calls on it (a run ⇒ <i>Stop &amp; Load</i> · unsaved edits ⇒ <i>Save / Discard / Cancel</i>); the RAW verbs are EXPLICIT <see cref="IRunScenarioControl"/> members, handed only to the
    /// <see cref="RunModeChoice"/> (<see cref="RunControl"/>). HTTP asks through <see cref="Door"/> with its refusing asker.
    /// </summary>
    public ScenarioDoor Door { get; }

    /// <summary>
    /// ⭐ <c>CE-4101</c> — the RAW verbs, for the run choice and for nobody else (<c>AttachRunChoice</c>): its mode moves (■ in Live, → Preview, go-live from Edit) ask their own R3 confirmation and then must open WITHOUT the door asking again.
    /// ⛔ Any other caller holds the session (<see cref="IScenarioSession"/>) and reaches only the door.
    /// </summary>
    public IRunScenarioControl RunControl => this;

    /// <inheritdoc/>
    public Func<bool>? SaveIntercept { get; set; }

    /// <summary>The file name every scenario directory stores its world under.</summary>
    public const string ScenarioFileName = "scenario.json";

    // ⭐ CE-1439 — BOTH null together on a host with no world of its own (the Orchestrator, the cluster master's window): the only three members that touch them
    //   (ClearWorld, SaveTo, GetMigrationSidecars) then have nothing local to act on. ⛔ Never one without the other.
    private readonly ScenarioFileService?    _fileService;
    private readonly FdpEventBus             _orchestrationBus;
    private readonly EntityRepository?       _world;
    private readonly MigrationAlertManager   _alertManager;
    private readonly Func<string>            _scenariosRoot;

    private string?      _loadedScenarioName;
    private readonly Func<Fdp.Toolkit.Orchestration.ClusterView?> _clusterView;   // ⭐ CE-1436 — the node's ONE cluster read model; this session folds no state of its own

    // ⭐ CE-295 — every load goes through the ONE shared sequence (Idle first, then the load); this session's own
    //   wait-for-Idle was where the sequence came from, and LoadForLive used to skip it.
    private readonly ScenarioLoadSequence _loads;

    /// <summary>
    /// ⭐ <b>The scenarios root is a DELEGATE, not a captured string</b>, and that is deliberate: the
    /// editor's <c>EditorBootstrap.ScenariosRoot</c> is a computed property over
    /// <c>ClusterConfiguration.Default.NasBasePath</c>. ⛔ Snapshotting it at construction would silently
    /// change when the value is read, so the late binding is preserved.
    /// </summary>
    /// <param name="alertManager">
    /// ⭐ Optional only because a headless rail has no alerts to show; a production host that HAS one must
    /// pass it *(CLAUDE.md's silent-default rule)*. When null a private instance is used, so
    /// <see cref="IsDegraded"/> still answers truthfully.
    /// </param>
    public EditorScenarioSession(
        ScenarioFileService    fileService,
        FdpEventBus            orchestrationBus,
        Func<Fdp.Toolkit.Orchestration.ClusterView?> clusterView,
        EntityRepository       world,
        Func<string>           scenariosRoot,
        MigrationAlertManager? alertManager = null,
        Action?                leaveRun     = null)
        : this(
            fileService ?? throw new ArgumentNullException(nameof(fileService)),
            orchestrationBus, clusterView,
            world       ?? throw new ArgumentNullException(nameof(world)),
            scenariosRoot, alertManager, leaveRun, worldless: false)
    {
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1439</c> (AQ108 §2.12) — <b>the SAME session on a host with NO world of its own</b>: the Orchestrator, whose window operates the cluster (open a scenario, open a
    /// recording, checkpoint, save through the master) while the entities live on the nodes. ⭐ Not a second implementation: every cluster verb is the shared body; what a world-less host
    /// legitimately has nothing to act on is exactly the three LOCAL members — <see cref="ClearWorld"/> (resets the loaded name and the migration alert, wipes nothing),
    /// <see cref="SaveTo"/> (the raw-path writer; throws — it has no production caller) and <see cref="GetMigrationSidecars"/> (empty). 📐 Measured: those are the only
    /// <c>_world</c> / <c>_fileService</c> readers in this class.
    /// </summary>
    public EditorScenarioSession(
        FdpEventBus            orchestrationBus,
        Func<Fdp.Toolkit.Orchestration.ClusterView?> clusterView,
        Func<string>           scenariosRoot,
        MigrationAlertManager? alertManager = null,
        Action?                leaveRun     = null)
        : this(null, orchestrationBus, clusterView, null, scenariosRoot, alertManager, leaveRun, worldless: true)
    {
    }

    private EditorScenarioSession(
        ScenarioFileService?   fileService,
        FdpEventBus            orchestrationBus,
        Func<Fdp.Toolkit.Orchestration.ClusterView?> clusterView,
        EntityRepository?      world,
        Func<string>           scenariosRoot,
        MigrationAlertManager? alertManager,
        Action?                leaveRun,
        bool                   worldless)
    {
        if (worldless != (fileService is null) || worldless != (world is null))
            throw new ArgumentException("A session has its file service AND its world, or neither (CE-1439).");
        _leaveRun         = leaveRun;
        _clusterView      = clusterView      ?? throw new ArgumentNullException(nameof(clusterView));
        _fileService      = fileService;
        _orchestrationBus = orchestrationBus ?? throw new ArgumentNullException(nameof(orchestrationBus));
        _world            = world;
        _scenariosRoot    = scenariosRoot    ?? throw new ArgumentNullException(nameof(scenariosRoot));
        _alertManager     = alertManager     ?? new MigrationAlertManager();
        _loads = new ScenarioLoadSequence(
            send:         intent => _orchestrationBus.PublishManaged(intent),
            currentState: () => CurrentClusterState,
            beforeLoad:   load =>
            {
                // An authoring load starts the editor's world on a blank slate (WorldResetEvent + SoftClear); a live
                // load is cleared on every node by the world boundary (CE-2101), as before.
                if (load.TargetState == ClusterState.OperatingEdit) ClearWorld();
                _loadedScenarioName = load.ScenarioId;
            });
        Door = new ScenarioDoor(this, () => ClusterReplayControls.ModeOf(CurrentClusterState), RawLeave);
    }

    // ⭐ CE-1025 (R-1031, load design §9.1) — leaves the host's own dry run WITHOUT a rewind, FIRST, at every load request (and New
    //   Exercise): the editor's dry run never moves the cluster, so the Idle the load asks for would not end it, and Stop rewound the new
    //   world to the old one. Null on a host with no dry run of its own (CGF).
    private readonly Action? _leaveRun;

    /// <summary>The alert manager this session reports migration state through.</summary>
    public MigrationAlertManager AlertManager => _alertManager;

    /// <inheritdoc/>
    public ClusterState CurrentClusterState => _clusterView()?.State ?? ClusterState.Idle;

    /// <inheritdoc/>
    public string? LoadedScenarioName => _loadedScenarioName;

    /// <inheritdoc/>
    public bool IsDegraded => _alertManager.IsDegradedMode;

    /// <inheritdoc/>
    public void Update()
    {
        // ⭐ CE-295 — a load waiting for the unload goes out once the cluster reports Idle.
        _loads.Pump();
    }

    /// <inheritdoc/>
    public void ClearWorld()
    {
        _fileService?.NewScenario(_world!);   // a world-less host (CE-1439) has no local world to wipe
        _loadedScenarioName = null;
        _alertManager.OnScenarioCleared();
    }

    /// <inheritdoc/>
    /// <remarks>⭐ <c>CE-4101</c> — the OUT door: editing with unsaved edits asks <i>Save / Discard / Cancel</i> first (it used to discard silently); a run is simply stopped.</remarks>
    public void NewExercise() => Door.Leave(LeaveReason.NewExercise);

    /// <summary>
    /// ⭐ The raw leave to Idle the door calls once it has decided: leaves the host's dry run, sends the ONE Idle intent (<see cref="ScenarioLoadSequence.Unload"/>), abandons a queued load (the operator just asked
    /// for a fresh exercise — honouring it afterwards would undo the request) and, for New Exercise, wipes the local world. ⛔ This is what distinguishes New Exercise from <see cref="ClearWorld"/>: on a cluster only this reaches the other nodes.
    /// </summary>
    private void RawLeave(LeaveReason reason)
    {
        _leaveRun?.Invoke();
        _loads.Unload();
        _loads.Cancel();
        if (reason == LeaveReason.NewExercise) ClearWorld();
    }

    /// <inheritdoc/>
    public void OpenForEdit(string scenarioName) => Door.Open(scenarioName);

    /// <inheritdoc/>
    public void LoadForLive(string scenarioName) => Door.LoadLive(scenarioName);

    /// <inheritdoc/>
    public void OpenRecording(Guid exerciseId) => Door.OpenRecording(exerciseId);

    /// <inheritdoc/>
    public void OpenRecording(Guid exerciseId, double? atSimSeconds) => Door.OpenRecording(exerciseId, atSimSeconds);

    // ── ⭐ CE-4101 — the RAW verbs: explicit IRunScenarioControl members, reached only through the door (after it decided) or the run choice (after its own confirmation) ──────────

    void IRunScenarioControl.OpenForEdit(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        _leaveRun?.Invoke();
        _loads.Request(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = ClusterState.OperatingEdit,
            ScenarioId    = scenarioName,
            ExerciseId    = Guid.NewGuid(),
        });
    }

    void IRunScenarioControl.LoadForLive(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        _leaveRun?.Invoke();

        // ⭐ A fresh ExerciseId per load, mirroring the orchestrator panel's "Load into Live" button and
        //   DebugApiService.LoadScenarioLive: a live load IS a new exercise run, and the id is what
        //   recording/replay keys off. ⭐ CE-295 — through the shared sequence: from Live or Edit it unloads first
        //   (a single request planned an empty path and only copied the files).
        _loads.Request(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = ClusterState.OperatingLive,
            ScenarioId    = scenarioName,
            ExerciseId    = Guid.NewGuid(),
            // ⭐ Q86 §4-G — the editor loads to a clock at 0, PAUSED (🔒 "Scenario load should reset the clock to zero
            //   and paused"); running after a load is a property of the load, and the editor's load does not ask for it.
            TimeMode      = "Deterministic",
        });
    }

    void IRunScenarioControl.OpenRecording(Guid exerciseId) => ((IRunScenarioControl)this).OpenRecording(exerciseId, null);

    void IRunScenarioControl.OpenRecording(Guid exerciseId, double? atSimSeconds)
    {
        if (exerciseId == Guid.Empty) return;   // ⭐ Empty means "the master picks" on the wire; an Open-Recording pick always names one
        _leaveRun?.Invoke();
        // A queued scenario load would otherwise fire after the replay request and undo it.
        _loads.Cancel();
        // ⭐ CE-4098 — the ONE construction of the replay intent (POST /replay {exerciseId} and the orchestrator's Load Replay end in it);
        //   the master plans the unload + the replay from ANY state in one request, so there is no Idle hop here. ⭐ CE-4097 part two (D21): at a moment
        //   of the recording's own sim clock when the timeline's "AAR from here" names one (the sim target wins on either axis); null = its start.
        _orchestrationBus.PublishManaged(Hrot.UI.Common.Facades.ReplaySessionIntents.EnterReplay(exerciseId, simSeconds: atSimSeconds));
    }

    /// <inheritdoc/>
    /// <remarks>⭐ <c>CE-4097</c> part two (AQ108 D8, D22) — in a Live / DAR / AAR run a run choice (<see cref="SaveIntercept"/>) turns a Save into a checkpoint and the
    /// scenario is NEVER written. Every user save path ends here (File › Save, Ctrl+S, the toolbar); HTTP <c>POST /scenario/save</c> is explicit and 409s in those states.</remarks>
    public void SaveCurrent()
    {
        if (SaveIntercept?.Invoke() == true) return;
        if (string.IsNullOrEmpty(_loadedScenarioName)) return;
        PublishScenarioSave(_loadedScenarioName!);
    }

    /// <inheritdoc/>
    public void SaveAs(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        PublishScenarioSave(scenarioName);
        CopyTakes(_loadedScenarioName, scenarioName);
        _loadedScenarioName = scenarioName;
    }

    /// <summary>
    /// ⭐ CE-4038 B1 (docs/DESIGN_Film_Director.md §4a, D-2) — a scenario's film takes live BESIDE it (<c>takes/*.take.json</c>), so "Save As"
    /// and "new from" (which loads the seed, then saves it as) carry them to the copy. ⛔ Never over a scenario that has takes of its own.
    /// (The same rule as <c>TakeStore.CopyTakes</c>, which this assembly cannot reach.)
    /// </summary>
    private void CopyTakes(string? from, string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.Equals(from, to, StringComparison.Ordinal)) return;
        try
        {
            var src = Path.Combine(_scenariosRoot(), from, "takes");
            var dst = Path.Combine(_scenariosRoot(), to, "takes");
            if (!Directory.Exists(src) || (Directory.Exists(dst) && Directory.EnumerateFiles(dst).Any())) return;
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.EnumerateFiles(src, "*.take.json"))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// ⭐⭐⭐ CE-275 — routes the scenario save through the cluster orchestrator, IDENTICALLY on every host
    /// (this one class is instantiated by the editor AND CGF). ⛔ There is no local/direct file write and no
    /// editor-only save path: the master fans <c>SerializeLocal</c> out to every node, and each node's
    /// <c>HrotScenarioSaveHandler</c> writes the slice it owns through the shared <c>ScenarioSaveCore</c>. The
    /// operator picks a NAME / subfolder under the NAS scenarios root, never a filesystem path.
    /// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4.
    /// </summary>
    private void PublishScenarioSave(string scenarioName)
    {
        _orchestrationBus.PublishManaged(new ExecuteStorageOpIntent
        {
            RequestId    = Guid.NewGuid(),
            Operation    = StorageOpType.SaveScenario,
            ScenarioName = scenarioName,
        });
    }

    /// <inheritdoc/>
    /// <remarks>⚠ CE-275 — the ONLY remaining raw-path writer, and it has no production caller (the facade
    /// method that reaches it is unreferenced). Scheduled for removal; all real saves go through
    /// <see cref="SaveAs"/>/<see cref="SaveCurrent"/> and the cluster handler.</remarks>
    public void SaveTo(string filePath)
    {
        if (_fileService is null || _world is null) throw new NotSupportedException("This host has no world of its own to write (the cluster saves through the master — SaveAs / SaveCurrent).");
        _fileService.SaveScenario(_world, filePath);
    }

    /// <inheritdoc/>
    public void TakeCheckpoint()
        => _orchestrationBus.PublishManaged(new TakeCheckpointIntent { RequestId = Guid.NewGuid() });

    /// <inheritdoc/>
    public IReadOnlyList<SidecarFileInfo> GetMigrationSidecars()
        => _fileService is null ? Array.Empty<SidecarFileInfo>() : _fileService.GetSidecarsForLastLoadAsync().GetAwaiter().GetResult();

    private void WriteScenarioDirectory(string scenarioName)
    {
        var dir = Path.Combine(_scenariosRoot(), scenarioName);
        Directory.CreateDirectory(dir);
        SaveTo(Path.Combine(dir, ScenarioFileName));
    }
}
