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
    /// <see cref="Close"/> are calls on it (a run ⇒ <i>Stop &amp; Load</i> · unsaved edits ⇒ <i>Save / Discard / Cancel</i>); the RAW verbs are EXPLICIT <see cref="IRunScenarioControl"/> members, handed only to the
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
    public const string ScenarioFileName = ContentLayout.ScenarioFileName;

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
                Opened?.Invoke();   // ⭐ B-14 — a load (to edit, or Live) starts a new history
            });
        Door = new ScenarioDoor(this, () => ClusterReplayControls.ModeOf(CurrentClusterState), RawLeave)
        {
            AutosaveOf = AutosaveOfScenario,   // ⭐ CE-4099 A7 — the door's recovery offer
        };
    }

    /// <summary>
    /// ⭐ CE-4099 A7 — a scenario's autosave state, read by the door ONCE per open (a user action, never per frame): <c>AutosaveFiles.Read</c> — the ONE reader HTTP's <c>GET /scenario/autosave</c> and the master's prefetch use — under the NAS
    /// root of THIS session's scenarios root (<c>{nas}/scenarios</c>, so a host and its rails agree on one tree); a root of another shape falls back to the shared NAS root.
    /// </summary>
    private Fdp.Toolkit.Orchestration.AutosaveFiles.Status AutosaveOfScenario(string scenarioName)
    {
        string scenarios = _scenariosRoot().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string nas = string.Equals(Path.GetFileName(scenarios), Fdp.Toolkit.Orchestration.ContentLayout.ScenariosDirectoryName, StringComparison.Ordinal) && Path.GetDirectoryName(scenarios) is { Length: > 0 } parent
            ? parent
            : Fdp.Toolkit.Orchestration.OrchestrationConstants.GetSharedRoot();
        return Fdp.Toolkit.Orchestration.AutosaveFiles.Read(nas, scenarioName, Fdp.Toolkit.Scenario.ScenarioDescription.HashOf);
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
        Opened?.Invoke();
    }

    /// <summary>⭐ B-14 / Edit-mode undo U5 (📄 docs/UX/DESIGN_Edit_Mode_Undo.md §2.2 lifetime) — a scenario was opened, created or cleared: the undo history
    /// starts over (an undo across it would hit the wrong entities).</summary>
    public event Action? Opened;

    /// <summary>⭐ B-14 / U5 — the scenario was written (Save, Save As; ⛔ not a checkpoint, not a selection saved as a macro): the clean point.</summary>
    public event Action? Saved;

    /// <inheritdoc/>
    /// <remarks>⭐ <c>CE-4101</c> / <c>CE-1443</c> — the OUT door: editing with unsaved edits asks <i>Save / Discard / Cancel</i> first; a run is simply stopped. Replaced <i>New Exercise</i>.</remarks>
    public void Close() => Door.Leave(LeaveReason.Close);

    /// <inheritdoc/>
    /// <remarks>⭐ <c>CE-1443</c> (R-1043) — the operator's load: the door resolves the cluster's armed mode (<see cref="RunModeChoice.LoadTarget"/>) and opens / loads live.</remarks>
    public void LoadScenario(string scenarioName) => Door.Load(scenarioName);

    /// <summary>
    /// ⭐ The raw leave to Idle the door calls once it has decided: leaves the host's dry run, sends the ONE Idle intent (<see cref="ScenarioLoadSequence.Unload"/>), abandons a queued load (the operator just asked
    /// for a fresh exercise — honouring it afterwards would undo the request) and, reaches the other nodes (⛔ not <see cref="ClearWorld"/>, which is local; the unload to Idle clears the world on every node, CE-2101).
    /// </summary>
    private void RawLeave(LeaveReason reason)
    {
        if (reason is LeaveReason.Exit or LeaveReason.Close && CurrentClusterState == ClusterState.Idle) return;   // ⭐ CE-4101 follow-up / CE-1443 — exiting or closing a cluster at rest: nothing to stop, no Idle intent to send
        _leaveRun?.Invoke();
        _loads.Unload();
        _loads.Cancel();   // the operator just asked to leave — honouring a queued load afterwards would undo the request
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

    void IRunScenarioControl.OpenForEdit(string scenarioName) => ((IRunScenarioControl)this).OpenForEdit(scenarioName, null);

    /// <summary>⭐ CE-3393 — the args the resident load applied (which preset the world holds).</summary>
    string? IRunScenarioControl.LoadedArgsJson => _world is null ? null : Fdp.Toolkit.Scenario.ScenarioDescription.ArgsOf(_world);

    /// <summary>⭐ CE-3375 — the edit open with a preset's args (null ⇒ the plain open, exactly as before).</summary>
    void IRunScenarioControl.OpenForEdit(string scenarioName, string? argsJson) => ((IRunScenarioControl)this).OpenForEdit(scenarioName, argsJson, recoverAutosave: false);

    /// <summary>⭐ CE-4099 A7 (backend's session half, one pass-through) — the edit open; <paramref name="recoverAutosave"/> rides the load's <c>TransitionStateIntent.RecoverAutosave</c> (the wire carries it, the nodes stage the autosave).</summary>
    void IRunScenarioControl.OpenForEdit(string scenarioName, string? argsJson, bool recoverAutosave)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        _leaveRun?.Invoke();
        _loads.Request(new TransitionStateIntent
        {
            TransactionId    = Guid.NewGuid(),
            TargetState      = ClusterState.OperatingEdit,
            ScenarioId       = scenarioName,
            ExerciseId       = Fdp.Core.ExerciseId.NewId(),
            ScenarioArgsJson = string.IsNullOrWhiteSpace(argsJson) ? null : argsJson,
            RecoverAutosave  = recoverAutosave,
        });
    }

    void IRunScenarioControl.LoadForLive(string scenarioName) => ((IRunScenarioControl)this).LoadForLive(scenarioName, null);

    /// <summary>
    /// ⭐ A fresh ExerciseId per load, mirroring the orchestrator panel's "Load into Live" button and DebugApiService.LoadScenarioLive: a live load IS a new exercise run, and the id is what recording/replay keys off.
    /// ⭐ CE-295 — through the shared sequence: from Live or Edit it unloads first (a single request planned an empty path and only copied the files).
    /// ⭐ CE-4141 / AQ108 D24 — the intent is the ONE <see cref="ScenarioLoadSequence.LiveLoadIntent"/> (this used to be a hand-written copy of it): paused at 0 (Q86 §4-G, 🔒 "Scenario load should reset the clock to zero and paused";
    /// running after a load is a property of the load, and the editor's load does not ask for it) and, when the picker's run setup asked for any, the args every node's load reads.
    /// </summary>
    void IRunScenarioControl.LoadForLive(string scenarioName, string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        _leaveRun?.Invoke();
        _loads.Request(ScenarioLoadSequence.LiveLoadIntent(scenarioName, startPaused: true, scenarioArgsJson: argsJson));
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
        RecordingOpened?.Invoke(exerciseId);   // ⭐ R-5 / V6 — the ONE branch where a recording opens: the host decides what fills its world (this node's part ⇒ today's AAR; a group with no part here ⇒ its ReviewSource)
    }

    /// <summary>
    /// ⭐ R-5 / V6 (docs/UX/DESIGN_Review_Perspective.md §3 D6) — raised AFTER the replay intent went out, for EVERY path that opens a recording (the door's, the run choice's raw verbs, HTTP's):
    /// they all end in <see cref="IRunScenarioControl.OpenRecording(Guid, double?)"/>. A host with a review source classifies the exercise here. ⛔ Not the door and not the run choice
    /// (<c>ScenarioDoor</c> / <c>RunModeChoice</c> are untouched): the raw verb is where every route has already converged.
    /// </summary>
    public event Action<Guid>? RecordingOpened;

    /// <inheritdoc/>
    /// <remarks>⭐ <c>CE-4097</c> part two (AQ108 D8, D22) — in a Live / DAR / AAR run a run choice (<see cref="SaveIntercept"/>) turns a Save into a checkpoint and the
    /// scenario is NEVER written. Every user save path ends here (File › Save, Ctrl+S, the toolbar); HTTP <c>POST /scenario/save</c> is explicit and 409s in those states.</remarks>
    public void SaveCurrent()
    {
        if (SaveIntercept?.Invoke() == true) return;
        if (string.IsNullOrEmpty(_loadedScenarioName)) return;
        if (VariantSaveRefusal is not null) return;   // ⭐ CE-3394 — a variant world is never written (the menu says why); the cluster half gates the SURFACES (CE-3407)
        PublishScenarioSave(_loadedScenarioName!);
        Saved?.Invoke();
    }

    /// <inheritdoc/>
    /// <remarks>⭐ CE-3407 (AQ108 §0 D27, R-3083) — ONE answer: the cluster first (<see cref="SaveRefusal.Of"/> — an error, or any state but Edit), then the variant gate.
    /// The variant gate is cached per world version (it walks the entities): a menu reads it every frame it is open, the world changes only on a load.</remarks>
    public string? SaveDisabledReason
    {
        get
        {
            if (SaveRefusal.Of(_clusterView()) is { } cluster) return cluster;
            return VariantSaveRefusal;
        }
    }

    /// <summary>⭐ CE-3394 — the variant half alone: the backstop <see cref="SaveCurrent"/> / <see cref="SaveAs"/> keep.</summary>
    /// <remarks>⛔ Not the cluster half: <c>New from a recipe</c> calls <see cref="SaveAs"/> as soon as its open WENT OUT (<c>ScenarioDoor.Open</c>'s <c>then</c>), while the
    /// cluster may still be Idle — the master orders the save after the load. The cluster half gates the user's surfaces, which all read <see cref="SaveDisabledReason"/>.</remarks>
    private string? VariantSaveRefusal
    {
        get
        {
            if (_world is null) return null;
            uint v = _world.GlobalVersion;
            if (v != _gateVersion || !_gateRead) { _gateVersion = v; _gateRead = true; _gateReason = Fdp.Toolkit.Scenario.ScenarioSaveGate.CanSave(_world); }
            return _gateReason;
        }
    }
    private uint _gateVersion;
    private bool _gateRead;
    private string? _gateReason;

    /// <inheritdoc/>
    public void SaveAs(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        if (VariantSaveRefusal is not null) return;   // ⭐ CE-3394 — under a new name it would lose the absent entities just the same
        PublishScenarioSave(scenarioName);
        CopyTakes(_loadedScenarioName, scenarioName);
        _loadedScenarioName = scenarioName;
        Saved?.Invoke();
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
            var src = Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.ResolveUnderScenariosRoot(_scenariosRoot(), from), "takes");   // ⭐ L6 — "new from a recipe": `from` is the recipe's own name
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
    private void PublishScenarioSave(string scenarioName, Guid[]? onlyGuids = null, string? headerJson = null)
    {
        _orchestrationBus.PublishManaged(new ExecuteStorageOpIntent
        {
            RequestId    = Guid.NewGuid(),
            Operation    = StorageOpType.SaveScenario,
            ScenarioName = scenarioName,
            OnlyGuids    = onlyGuids,    // ⭐ CE-4210 MA-9 — null for every ordinary save
            HeaderJson   = headerJson,
        });
    }

    /// <inheritdoc/>
    /// <remarks>⭐ New scenario N2 — the same storage-op door as a save (<see cref="ExecuteStorageOpIntent"/>), so it is identical on every host.</remarks>
    public void CreateBlank(string scenarioName, string terrainName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName) || string.IsNullOrWhiteSpace(terrainName)) return;
        _orchestrationBus.PublishManaged(BlankScenarioIntent(scenarioName, terrainName));
    }

    /// <summary>⭐ N2 — the ONE construction of the blank-create intent (the session and <c>POST /scenario/new</c>).</summary>
    public static ExecuteStorageOpIntent BlankScenarioIntent(string scenarioName, string terrainName) => new()
    {
        RequestId    = Guid.NewGuid(),
        Operation    = StorageOpType.CreateBlankScenario,
        ScenarioName = scenarioName,
        HeaderJson   = new System.Text.Json.Nodes.JsonObject { ["terrainName"] = terrainName }.ToJsonString(),
    };

    /// <inheritdoc/>
    /// <remarks>⭐ CE-4210 MA-9 — the ONE save path with a filter and a header. ⛔ Unlike <see cref="SaveAs"/> it changes NOTHING of the open scenario: not its loaded name, not its takes.</remarks>
    public void SaveSelectionAs(string scenarioName, Guid[] onlyGuids, string? headerJson)
    {
        if (string.IsNullOrWhiteSpace(scenarioName) || onlyGuids is not { Length: > 0 }) return;
        if (SaveIntercept?.Invoke() == true) return;
        PublishScenarioSave(scenarioName, onlyGuids, headerJson);
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
    public void RestoreCheckpoint(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        _orchestrationBus.PublishManaged(new RestoreCheckpointIntent { RequestId = Guid.NewGuid(), Label = label });   // ⭐ CE-1443 — the existing restore path (the master refuses outside a recorded live run)
    }

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
