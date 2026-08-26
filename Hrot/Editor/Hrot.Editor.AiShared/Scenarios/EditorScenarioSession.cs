using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Core;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;
using Hrot.ScenarioEditor.Services;

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
public sealed class EditorScenarioSession : IScenarioSession
{
    /// <summary>The file name every scenario directory stores its world under.</summary>
    public const string ScenarioFileName = "scenario.json";

    private readonly ScenarioFileService     _fileService;
    private readonly FdpEventBus             _orchestrationBus;
    private readonly EntityRepository        _world;
    private readonly MigrationAlertManager   _alertManager;
    private readonly Func<string>            _scenariosRoot;

    private string?      _loadedScenarioName;
    private string?      _pendingScenarioLoad;
    private ClusterState _pendingTargetState = ClusterState.OperatingEdit;
    private bool         _waitingForIdle;
    private ClusterState _currentClusterState = ClusterState.Idle;

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
        EntityRepository       world,
        Func<string>           scenariosRoot,
        MigrationAlertManager? alertManager = null)
    {
        _fileService      = fileService      ?? throw new ArgumentNullException(nameof(fileService));
        _orchestrationBus = orchestrationBus ?? throw new ArgumentNullException(nameof(orchestrationBus));
        _world            = world            ?? throw new ArgumentNullException(nameof(world));
        _scenariosRoot    = scenariosRoot    ?? throw new ArgumentNullException(nameof(scenariosRoot));
        _alertManager     = alertManager     ?? new MigrationAlertManager();
    }

    /// <summary>The alert manager this session reports migration state through.</summary>
    public MigrationAlertManager AlertManager => _alertManager;

    /// <inheritdoc/>
    public ClusterState CurrentClusterState => _currentClusterState;

    /// <inheritdoc/>
    public string? LoadedScenarioName => _loadedScenarioName;

    /// <inheritdoc/>
    public bool IsDegraded => _alertManager.IsDegradedMode;

    /// <inheritdoc/>
    public void Update()
    {
        foreach (var ev in _orchestrationBus.ReadManaged<ClusterStateUpdateEvent>())
            _currentClusterState = ev.CurrentState;

        if (!_waitingForIdle || string.IsNullOrEmpty(_pendingScenarioLoad)) return;
        if (_currentClusterState != ClusterState.Idle) return;

        _waitingForIdle = false;
        var scenarioName = _pendingScenarioLoad;
        var target       = _pendingTargetState;
        _pendingScenarioLoad = null;

        // 1. Safely wipe the existing state (fires WorldResetEvent and SoftClears the repo)
        //    so the new scenario starts on a blank slate.
        ClearWorld();

        // 2. Dispatch a cluster transition intent to route the load through the orchestrator.
        //    This triggers HrotEditLoadHandler -> StagingEntityExtractor -> NetworkSpawningSystem
        _orchestrationBus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = target,
            ScenarioId    = scenarioName,
            ExerciseId    = Guid.NewGuid(),
        });

        _loadedScenarioName = scenarioName;
    }

    /// <inheritdoc/>
    public void ClearWorld()
    {
        _fileService.NewScenario(_world);
        _loadedScenarioName = null;
        _alertManager.OnScenarioCleared();
    }

    /// <inheritdoc/>
    public void NewExercise()
    {
        // ⭐⭐ The cluster-wide half: ask the master to finish whatever is running and return to Idle.
        //    ⛔ This is what distinguishes New Exercise from ClearWorld — on a single-process editor the
        //    two look almost identical, but on a cluster only this one reaches the other nodes.
        _orchestrationBus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = ClusterState.Idle,
        });

        // ⚠ Any load that was waiting for idle is abandoned — the operator just asked for a fresh
        //   exercise, so honouring a queued load afterwards would silently undo their request.
        _waitingForIdle      = false;
        _pendingScenarioLoad = null;

        ClearWorld();
    }

    /// <inheritdoc/>
    public void OpenForEdit(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        _pendingScenarioLoad = scenarioName;
        _pendingTargetState  = ClusterState.OperatingEdit;
        _waitingForIdle      = true;

        _orchestrationBus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = ClusterState.Idle,
        });
    }

    /// <inheritdoc/>
    public void LoadForLive(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;

        // ⭐ A fresh ExerciseId per load, mirroring the orchestrator panel's "Load into Live" button and
        //   DebugApiService.LoadScenarioLive: a live load IS a new exercise run, and the id is what
        //   recording/replay keys off.
        _orchestrationBus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = ClusterState.OperatingLive,
            ScenarioId    = scenarioName,
            ExerciseId    = Guid.NewGuid(),
        });

        _loadedScenarioName = scenarioName;
    }

    /// <inheritdoc/>
    public void SaveCurrent()
    {
        if (string.IsNullOrEmpty(_loadedScenarioName)) return;
        WriteScenarioDirectory(_loadedScenarioName!);
    }

    /// <inheritdoc/>
    public void SaveAs(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return;
        WriteScenarioDirectory(scenarioName);
        _loadedScenarioName = scenarioName;
    }

    /// <inheritdoc/>
    public void SaveTo(string filePath) => _fileService.SaveScenario(_world, filePath);

    /// <inheritdoc/>
    public void TakeCheckpoint()
        => _orchestrationBus.PublishManaged(new TakeCheckpointIntent { RequestId = Guid.NewGuid() });

    /// <inheritdoc/>
    public IReadOnlyList<SidecarFileInfo> GetMigrationSidecars()
        => _fileService.GetSidecarsForLastLoadAsync().GetAwaiter().GetResult();

    private void WriteScenarioDirectory(string scenarioName)
    {
        var dir = Path.Combine(_scenariosRoot(), scenarioName);
        Directory.CreateDirectory(dir);
        _fileService.SaveScenario(_world, Path.Combine(dir, ScenarioFileName));
    }
}
