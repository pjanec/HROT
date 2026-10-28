using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.Behavior;
using Hrot.CGF.Configuration;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.Replication.Services;
using Hrot.Presentation.DebugApi;
using Hrot.UI.Common.Facades;

namespace Hrot.ReplayBrowser;

/// <summary>
/// ⭐⭐⭐ RB-1 (<c>docs/DESIGN_Replay_Browser_Debug_Http.md</c> §0, §3) — <b>the Replay Browser answers the SHARED debug routes</b>: it
/// fills the provider members every host's routes read (<c>Master</c>, <c>Drive</c>, <c>ClusterState</c>, <c>Exercises</c>, the replay's
/// length and recording, a serializer-backed <c>Extraction</c>) over its own <see cref="FederatedReplayManager"/>, plus the one seam only it
/// has (<see cref="IReplayBrowserControl"/>). ⛔ No second route family and no second replay engine (R-4081).
/// </summary>
/// <remarks>
/// <para>⭐ The Replay Browser is a standalone offline host (R-4080), so its provider is the only one in its process and every
/// "any node" read of the debug service lands on it by construction.</para>
/// <para>⭐ Its replay "zero" is the cluster's: the earliest first frame across the loaded nodes — so <c>atSeconds</c> means the same
/// thing here as on a cluster replay (<c>DebugApiService.EnterReplay</c>, C9).</para>
/// <para>⚠ Everything here runs on the main thread: the debug service drains its jobs in the host's frame, as the panels run.</para>
/// </remarks>
public sealed partial class ReplayBrowserSubsystem : IProvidesDebugSurface
{
    // ── IProvidesDebugSurface ─────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-259am</c> — THIS SUBSYSTEM CONTRIBUTES A DEBUG PROVIDER, like every other host that owns a perspective</b>, and since
    /// RB-1 (<c>docs/DESIGN_Replay_Browser_Debug_Http.md</c>) it fills the members the SHARED routes read — master, drive, cluster state,
    /// exercises, the replay's length and recording, a serializer-backed extraction — over its own federation. The provider is built in
    /// <c>ReplayBrowserSubsystem.DebugSurface.cs</c>.
    ///
    /// <para>🔴 <b>What its absence cost, measured `2026-09-11` driving <c>--mode replaybrowser</c> over HTTP:</b> with no provider
    /// <c>GET /capabilities</c> reported <c>providers=[]</c> and every world/gizmo route answered as if the host held nothing.</para>
    ///
    /// <para>⛔ <b>SUPERSEDED `2026-10-26` (RB-1):</b> this comment listed <c>drive</c> and <c>extraction</c> as measured absences ("replay
    /// time is not cluster time", "the inspector's ExtractionService is never set") and named a second, isolated replay world owned by the
    /// debug service (CE-259an). That world is retired (R-3054) and the routes now drive THIS host's federation: one replay world.</para>
    ///
    /// <para>⛔ <b>Still absent, each a measured absence:</b> <c>architecture</c> (no <c>ModuleHostKernel</c> here —
    /// <c>DESIGN_Subsystem_Composition_Unification.md:1153</c>), <c>missionEditor</c>, the roster and the dump status (no catalog,
    /// no mission service, no cluster).</para>
    ///
    /// <para>⚠ <c>world.read</c> covers the mutating entity routes too; a write lands in the replay's sandbox, which the next seek replaces —
    /// discarded, not destructive (R-141, R-137).</para>
    /// </summary>
    public Hrot.Presentation.DebugApi.ISubsystemDebugProvider? CreateDebugProvider() => BuildDebugProvider();

    // ── Play state — ONE per host ─────────────────────────────────────────────────────────────────────────────────

    // ⚠ The timeline panel owns the play state when there is one (a windowed host); a headless host has no panel, so these hold
    //   it. ⛔ Never both: whichever exists is the one the frame loop and the transport read.
    private bool _headlessPlaying;
    private float _headlessRate = 1f;

    /// <summary>Whether the replay plays — the timeline panel's state, or the headless host's.</summary>
    internal bool IsPlaying
    {
        get => _timelinePanel?.IsPlaying ?? _headlessPlaying;
        set { if (_timelinePanel != null) _timelinePanel.IsPlaying = value; else _headlessPlaying = value; }
    }

    /// <summary>The playback rate — the timeline panel's, or the headless host's.</summary>
    internal float PlaybackRate
    {
        get => _timelinePanel?.PlaybackRate ?? _headlessRate;
        set { if (_timelinePanel != null) _timelinePanel.PlaybackRate = value; else _headlessRate = value; }
    }

    /// <summary>Where the exercises on disk are scanned (test seam): the staging root every node records under.</summary>
    internal string RecordingsRoot { get; set; } = OrchestrationConstants.ResolveStagingRoot();

    // ── Open / close / seek — the ONE path the panels and HTTP share ────────────────────────────────────────────────

    /// <summary>
    /// Opens a node group (the federation's validation decides: one exercise, one file per node). Returns why it was refused, or
    /// <see langword="null"/>. ⭐ The timeline panel's <c>OnLoadGroup</c> calls this too — one load path.
    /// </summary>
    internal string? OpenGroup(string[] paths)
    {
        try
        {
            var manager = FederatedReplayManager.LoadGroup(paths);
            _manager?.Dispose();
            _transientMaster?.Dispose();
            _transientMaster = null;

            _manager = manager;
            _eventsCapturedAt = null;
            _manager.OnTimeChanged += OnManagerTimeChanged;
            ApplyRecordingGeoOrigin();   // CE-3126
            ApplyRecordingTerrain();     // CE-3118

            CreateOrReplaceFederationPanel();

            OnManagerTimeChanged();
            _timelinePanel?.SetManager(_manager);
            _playbackHistory.Clear();
            IsPlaying = false;
            LocalLoop = null;   // ⭐ CE-3329 §12n — a new recording starts unlooped
            // ⭐ RB-7 — a headless host builds the map pack on its first open (it has no world before), through the one construction
            if (_headless && _selectionRequests == null && _activeRepo != null)
            {
                _inspectorState ??= new InspectorState();
                BuildMapInteraction(_activeRepo, BehaviorRegistryLoaded());
            }
            return null;
        }
        catch (LoadGroupException ex) { return ex.Message; }
        catch (System.IO.IOException ex) { return $"Failed to read recording file: {ex.Message}"; }
        catch (UnauthorizedAccessException ex) { return $"Access denied reading recording file: {ex.Message}"; }
        catch (System.Text.Json.JsonException ex) { return $"Recording metadata is corrupt: {ex.Message}"; }
    }

    /// <summary>Closes the open group — the replay ends (<c>POST /replay/stop</c>).</summary>
    internal void CloseGroup()
    {
        IsPlaying = false;
        LocalLoop = null;   // ⭐ CE-3329 §12n
        _manager?.Dispose();
        _manager = null;
        _transientMaster?.Dispose();
        _transientMaster = null;
        _timelinePanel?.SetManager(null);
        _playbackHistory.Clear();
        _extraction = null;
        _eventsCapturedAt = null;
        if (_headless)
            _activeRepo = null;
        else
        {
            var empty = new EntityRepository();
            RepositoryPriming.RegisterDiscoveredComponents(empty);
            RebindActiveRepo(empty);
        }
    }

    /// <summary>The earliest first frame across the loaded nodes — the replay's zero; <see langword="null"/> with nothing open.</summary>
    internal long? ReplayZeroTicks => FrameTicksAcrossNodes(first: true);

    /// <summary>The latest last frame across the loaded nodes.</summary>
    internal long? ReplayEndTicks => FrameTicksAcrossNodes(first: false);

    /// <summary>
    /// ⭐ CE-3329 P1 (row 13) — the loaded node group's wall ↔ sim map: each node's map from its frame index, intersected as the
    /// master does (R3, the earliest-starting node's sim times). Built on demand; Empty with nothing open or for v6 recordings.
    /// </summary>
    internal Fdp.Core.FlightRecorder.ReplayTimeMap TimeMap
        => _manager == null ? Fdp.Core.FlightRecorder.ReplayTimeMap.Empty
           : Fdp.Core.FlightRecorder.ReplayTimeMap.Intersect(_manager.Contexts.Values
                 .Where(c => c.Playback is { TotalFrames: > 0 })
                 .OrderBy(c => c.Playback!.GetFrameMetadata(0).WallClockTicks)
                 .Select(c => Fdp.Core.FlightRecorder.ReplayTimeMap.FromRecording(c.Playback!)));

    private FederatedReplayManager? _timeMapFor;
    private Fdp.Core.FlightRecorder.ReplayTimeMap _timeMapCache = Fdp.Core.FlightRecorder.ReplayTimeMap.Empty;

    /// <summary>⭐ CE-3330 T13 — <see cref="TimeMap"/> built once per loaded group (the timeline reads it every frame).</summary>
    internal Fdp.Core.FlightRecorder.ReplayTimeMap CachedTimeMap
    {
        get
        {
            if (!ReferenceEquals(_manager, _timeMapFor)) { _timeMapCache = TimeMap; _timeMapFor = _manager; }
            return _timeMapCache;
        }
    }

    /// <summary>⭐ CE-3329 R-3058 — this viewer's time axis (the Replay Browser keeps its own, R2). Sim by default.</summary>
    internal Fdp.Toolkit.Orchestration.ReplayAxis Axis { get; set; } = Fdp.Toolkit.Orchestration.ReplayAxis.Sim;

    /// <summary>⭐ CE-3329 §12n — this viewer's own LOOP range, wall seconds after the replay's zero (the Replay Browser's clock is its own,
    /// so the loop is local: <see cref="TickLocalLoop"/> wraps its playback). Cleared when a group opens or closes.</summary>
    internal (double From, double To)? LocalLoop { get; set; }

    /// <summary>⭐ CE-3329 §12n — while playing, a playhead at or past the loop's end goes back to its start (called once per frame
    /// after the playback steps). ⛔ Not <see cref="SeekToSeconds"/>: a wrap is not a navigation, it must not fill the history.</summary>
    private void TickLocalLoop()
    {
        if (LocalLoop is not { } loop || !IsPlaying || _manager == null) return;
        if (ReplaySeconds is not { } now || now < loop.To || ReplayZeroTicks is not { } zero) return;
        _manager.SetBaseWallTicks(zero + (long)Math.Round(loop.From * TimeSpan.TicksPerSecond));
    }

    /// <summary>Where the replay stands, in seconds after its zero.</summary>
    internal double? ReplaySeconds
        => _manager is { } m && ReplayZeroTicks is { } zero ? (m.BaseWallTicks - zero) / (double)TimeSpan.TicksPerSecond : null;

    private long? FrameTicksAcrossNodes(bool first)
    {
        if (_manager == null) return null;
        long? best = null;
        foreach (var ctx in _manager.Contexts.Values)
        {
            var p = ctx.Playback;
            if (p == null || p.TotalFrames == 0) continue;
            long t = p.GetFrameMetadata(first ? 0 : p.TotalFrames - 1).WallClockTicks;
            if (best is null || (first ? t < best : t > best)) best = t;
        }
        return best;
    }

    /// <summary>
    /// Seeks every node to <paramref name="seconds"/> after the replay's zero, clamped to the recording, and records the move in the
    /// playback history (where it was, where it went) — as a search hit's seek does.
    /// </summary>
    internal void SeekToSeconds(double seconds)
    {
        if (_manager == null || ReplayZeroTicks is not { } zero || ReplayEndTicks is not { } end) return;
        long target = Math.Clamp(zero + (long)Math.Round(seconds * TimeSpan.TicksPerSecond), zero, end);
        Entity selected = _inspectorState?.SelectedEntity ?? Entity.Null;
        _playbackHistory.PushWaypoint(PrimaryNodeCurrentFrame(), selected);
        _manager.SetBaseWallTicks(target);
        _playbackHistory.PushWaypoint(PrimaryNodeCurrentFrame(), selected);
    }

    // ── The selection history follows the selection (RB-3 / RB-7) ─────────────────────────────────────────────────────

    private Entity _historySelected = Entity.Null;

    /// <summary>
    /// ⭐ RB-7 — every selection joins the selection history, whatever made it: a click, a panel's entity link, <c>POST /selection</c>
    /// (<c>SelectEntityCommand</c> through <c>SelectionRequestSystem</c>). ⭐ The history's own back / forward does not push (it is
    /// navigating, and the entry it lands on is already there).
    /// </summary>
    private void FollowTheSelectionIntoItsHistory()
    {
        var selected = _selectionView?.PrimarySelected ?? Entity.Null;
        if (selected == _historySelected) return;
        _historySelected = selected;
        if (!selected.IsNull) _entityHistory.PushSelection(selected);
    }

    // ── The event history at the shown frame (RB-5) ───────────────────────────────────────────────────────────────────

    private (int Node, int Frame)? _eventsCapturedAt;

    /// <summary>
    /// ⭐ RB-5 — the primary node's event history follows the shown frame, as <see cref="ReplayBrowserContext"/>'s own seek and step
    /// keep it: a step forward adds the frame's events, a step back rewinds, a jump starts over. 🔴 Before: the federation seeks and steps
    /// with the history suppressed, so after the load the Event Browser panel showed frame 0's events whatever frame was shown — and
    /// <c>GET /events</c> had nothing to read here.
    /// </summary>
    private void CaptureEventsAtTheNewFrame()
    {
        if (_manager == null || !_manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var ctx) || ctx.Playback == null)
            return;
        int node = _manager.LocalEntitiesProviderNodeId, frame = ctx.CurrentFrame;
        if (frame < 0 || _eventsCapturedAt == (node, frame)) return;
        var history = ctx.HistoryService;
        if (_eventsCapturedAt is not { } last || last.Node != node || frame > last.Frame + 1) history.ClearHistory();
        else if (frame < last.Frame) history.RewindHistory((uint)frame);
        history.Capture("Replay", ctx.SandboxBus, (uint)frame);
        _eventsCapturedAt = (node, frame);
    }

    /// <summary>The primary node's event history (<c>GET /events</c>).</summary>
    private Fdp.Core.Diagnostics.IDiagnosticEventHistoryService? PrimaryEventHistory
        => _manager != null && _manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var ctx) ? ctx.HistoryService : null;

    // ── The debug surface ─────────────────────────────────────────────────────────────────────────────────────────────

    private Fdp.Toolkit.Diagnostics.IEntityStateExtractionService? _extraction;
    private EntityRepository? _extractionFor;

    /// <summary>
    /// ⭐ CE-171 — the entity dumps through THIS host's <see cref="Fdp.Toolkit.Scenario.ScenarioSerializer"/> (the one its inspector
    /// uses), so a typed component reads as its DTO, not <c>FixedElementField</c>. ⚠ Rebuilt when the world is swapped (every seek
    /// rebinds it), never answered for a stale one.
    /// </summary>
    private Fdp.Toolkit.Diagnostics.IEntityStateExtractionService? ExtractionForActiveWorld()
    {
        var world = _activeRepo;
        if (world == null) return null;
        if (!ReferenceEquals(world, _extractionFor) || _extraction == null)
        {
            var map = world.HasSingletonManaged<NetworkEntityMap>() ? world.GetSingletonManaged<NetworkEntityMap>() : null;
            _extraction = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(world, map, Serializer);
            _extractionFor = world;
        }
        return _extraction;
    }

    /// <summary>The scenario serializer — built on first use on a headless host, which builds no panels (the inspector's own otherwise).</summary>
    private Fdp.Toolkit.Scenario.ScenarioSerializer Serializer
        => _scenarioSerializer ??= Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(BehaviorRegistryLoaded());

    private BehaviorRegistry BehaviorRegistryLoaded()
    {
        if (!_behaviorsLoaded)
        {
            CgfBehaviorSetup.LoadFromAiAssembly(_behaviorRegistry, _blueprintRegistry);
            _behaviorsLoaded = true;
        }
        return _behaviorRegistry;
    }

    private bool _behaviorsLoaded;

    /// <summary>The serializer the host's exports and dumps use (test seam).</summary>
    internal Fdp.Toolkit.Scenario.ScenarioSerializer SerializerForTest => Serializer;

    /// <summary>The exercises on disk this host can open, newest first (<c>GET /exercises</c>).</summary>
    private IReadOnlyList<ExerciseInventoryItem> ExercisesOnDisk()
        => RecordingGroupInventory.Scan(RecordingsRoot)
                                  .Select(g => new ExerciseInventoryItem(g.ExerciseId, g.StartTimeUtc, g.Duration, null))
                                  .ToList();

    private ClusterState ReplayState => _manager is { Contexts.Count: > 0 } ? ClusterState.OperatingReplay : ClusterState.Idle;

    private string? PrimaryRecordingPath
        => _manager != null && _manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var ctx) ? ctx.CurrentFdpPath : null;

    private TimelineAccess? _timelineAccess;
    private ReplayBrowserMasterControl? _masterControl;
    private ReplayBrowserTransport? _transport;
    private ReplayBrowserControl? _browserControl;

    internal IClusterMasterControl MasterControl => _masterControl ??= new ReplayBrowserMasterControl(this);
    internal ITimeTransportFacade Transport => _transport ??= new ReplayBrowserTransport(this);
    internal IReplayBrowserControl BrowserControl => _browserControl ??= new ReplayBrowserControl(this);

    /// <summary>⭐ CE-1391 — every recording the ONE opener (<c>ReplayBrowserControl.Open</c>) was asked to open, in order (rails: each open path ends there).</summary>
    internal List<string> RecordingOpens { get; } = new();

    /// <summary>
    /// ⭐ RB-1 — the cluster master's part, played by the federation: <c>OperatingReplay</c> opens an exercise found on disk (none named:
    /// the newest, as a cluster master picks), <c>Idle</c> closes it, a seek seeks. ⛔ The rest refuse — an offline viewer saves,
    /// checkpoints and dumps nothing.
    /// </summary>
    private sealed class ReplayBrowserMasterControl : IClusterMasterControl
    {
        private readonly ReplayBrowserSubsystem _host;
        public ReplayBrowserMasterControl(ReplayBrowserSubsystem host) => _host = host;

        public void Transition(TransitionStateIntent intent)
        {
            switch (intent.TargetState)
            {
                case ClusterState.OperatingReplay:
                {
                    // ⭐ CE-1391 — through the ONE opener (the exercise id resolved by the inventory there), as POST /replay {paths} and the pack's reel items
                    var refused = _host.BrowserControl.Open(RecordingName.Exercise(intent.ExerciseId), intent.TargetWallTicks / (double)TimeSpan.TicksPerSecond);
                    if (refused != null) throw new InvalidOperationException(refused);
                    return;
                }
                case ClusterState.Idle:
                    _host.CloseGroup();
                    return;
                default:
                    throw Offline($"{intent.TargetState}");
            }
        }

        public void SeekReplay(SeekReplayIntent intent)
        {
            if (_host.ReplayState != ClusterState.OperatingReplay) throw new InvalidOperationException("No recording is open: POST /replay first.");
            // ⭐ CE-3329 P1 (R11, row 13) — a target in RECORDED sim time resolves through the group's map, as the master's does
            if (intent.TargetSimSeconds is { } sim)
            {
                var map = _host.TimeMap;
                if (!map.HasSimTime || _host.ReplayZeroTicks is not { } zero)
                    throw new InvalidOperationException("These recordings have no sim axis (format v6): seek with atSeconds.");
                _host.SeekToSeconds((map.SeekWallAt(sim) - zero) / (double)TimeSpan.TicksPerSecond);
                return;
            }
            _host.SeekToSeconds(intent.TargetWallTicks / (double)TimeSpan.TicksPerSecond);
        }

        /// <summary>⭐ CE-3329 R-3058 — in the Replay Browser the axis is the VIEWER's (R2); held by the browser, read back in the status.</summary>
        public void SetReplayAxis(SetReplayAxisIntent intent) => _host.Axis = intent.Axis;

        /// <summary>
        /// ⭐ CE-3329 §12n — the Replay Browser's clock is its own, so its loop is the VIEWER's (like the axis): held by the browser and
        /// wrapped in its playback (<c>TickLocalLoop</c>). Same validation and sim resolution as the master's, without a status topic.
        /// </summary>
        public void SetReplayLoop(SetReplayLoopIntent intent)
        {
            if (_host.ReplayState != ClusterState.OperatingReplay) throw new InvalidOperationException("No recording is open: POST /replay first.");
            if (intent.IsClear) { _host.LocalLoop = null; return; }
            var map = _host.TimeMap;
            long? zero = _host.ReplayZeroTicks;
            double length = zero is { } z0 && _host.ReplayEndTicks is { } e0 ? (e0 - z0) / (double)TimeSpan.TicksPerSecond : 0;
            var loop = intent.Resolve(sim => map.HasSimTime && zero is { } z ? Math.Max(0, (map.WallAt(sim) - z) / (double)TimeSpan.TicksPerSecond) : null,
                                      length)
                ?? throw new InvalidOperationException($"The loop needs both bounds on one axis (sim needs a v7 recording) and 0 <= from < to <= the recording's length ({length:F3}s); the previous loop stays.");
            _host.LocalLoop = loop;
        }

        public void Storage(ExecuteStorageOpIntent intent) => throw Offline("storage");
        public void TakeCheckpoint(TakeCheckpointIntent intent) => throw Offline("checkpoint");
        public void RestoreCheckpoint(RestoreCheckpointIntent intent) => throw Offline("checkpoint restore");
        public void SetRunSettings(SetRunSettingsIntent intent) => throw Offline("run settings");
        public void CheckUnsaved(CheckUnsavedIntent intent) => throw Offline("unsaved-changes check");
        public void Cancel(CancelOperationIntent intent) => throw Offline("cancel");
        public void Dump(ExecuteDiagnosticDumpIntent intent) => throw Offline("diagnostic dump");

        private static InvalidOperationException Offline(string what)
            => new($"The Replay Browser is a standalone offline viewer (R-4080): it has no '{what}'. It opens, seeks and closes recordings.");
    }

    /// <summary>
    /// ⭐ RB-1 — <c>/sim/play</c>, <c>/pause</c>, <c>/step</c>, <c>/timescale</c> drive the replay's playback: the same play state and
    /// frame loop the timeline panel drives. ⭐ <see cref="ITimeTransportFacade.TotalTime"/> is the seconds after the replay's zero.
    /// </summary>
    private sealed class ReplayBrowserTransport : ITimeTransportFacade
    {
        private readonly ReplayBrowserSubsystem _host;
        public ReplayBrowserTransport(ReplayBrowserSubsystem host) => _host = host;

        private bool Open => _host.ReplayState == ClusterState.OperatingReplay;

        public bool IsPlayPauseEnabled => Open;
        public bool IsStepEnabled => Open;
        public bool IsStopEnabled => Open;
        public bool IsPaused => !_host.IsPlaying;
        public double TotalTime => _host.ReplaySeconds ?? 0d;
        public float TimeScale => _host.PlaybackRate;

        public void TogglePlayPause() { if (Open) _host.IsPlaying = !_host.IsPlaying; }

        /// <summary>One recorded frame forward; playback pauses, as the panel's step button does.</summary>
        public void Step()
        {
            if (!Open) return;
            _host.IsPlaying = false;
            _host.TryStepForwardViaManager();
        }

        /// <summary><paramref name="seconds"/> of recording forward (backward when negative), paused.</summary>
        public void StepBy(float seconds)
        {
            if (!Open || _host.ReplaySeconds is not { } now) return;
            _host.IsPlaying = false;
            _host.SeekToSeconds(now + seconds);
        }

        /// <summary>Pauses and goes back to the start — the replay's "stop" (it stays open; <c>POST /replay/stop</c> closes it).</summary>
        public void Stop()
        {
            if (!Open) return;
            _host.IsPlaying = false;
            _host.SeekToSeconds(0d);
        }

        public void SetTimeScale(float scale)
        {
            if (!(scale > 0f) || !float.IsFinite(scale))
                throw new ArgumentOutOfRangeException(nameof(scale), scale, "The playback rate must be a positive number.");
            _host.PlaybackRate = scale;
        }

        // ⭐ CE-3329 row 21 — the session members: an offline viewer only replays (no live run to review); a seek and the axis go through
        //   the SAME intents and the SAME master control every other host's facade publishes to (ReplaySessionIntents).
        public TransportMode Mode => Open ? TransportMode.Replay : TransportMode.Idle;
        public Fdp.Toolkit.Orchestration.ReplayAxis Axis => _host.Axis;
        public void SetAxis(Fdp.Toolkit.Orchestration.ReplayAxis axis) => _host.MasterControl.SetReplayAxis(ReplaySessionIntents.SetAxis(axis));
        public void EnterReview(double? at = null) { }   // offline: no live run
        public void ExitReview() { }
        public void EnterReviewAtSim(double simSeconds) { }   // offline: no live run
        public Fdp.Toolkit.Orchestration.ReplaySession? Session => null;
        public double? LivePoint => null;

        // ⭐ CE-3329 §12n — the loop is the viewer's own (its clock is its own): set through the same intents and the same master control.
        public void SetLoop(double from, double to) { if (Open) _host.MasterControl.SetReplayLoop(ReplaySessionIntents.SetLoop(Axis, from, to)); }
        public void ClearLoop() { if (Open) _host.MasterControl.SetReplayLoop(ReplaySessionIntents.ClearLoop()); }
        public (double From, double To)? Loop
        {
            get
            {
                if (!Open || _host.LocalLoop is not { } l) return null;
                var map = _host.CachedTimeMap;
                if (Axis != Fdp.Toolkit.Orchestration.ReplayAxis.Sim || !map.HasSimTime || _host.ReplayZeroTicks is not { } zero) return l;
                double Sim(double wall) => map.SimAt(zero + (long)(wall * TimeSpan.TicksPerSecond));
                return (Sim(l.From), Sim(l.To));
            }
        }

        public void Seek(double t)
        {
            if (!Open) return;
            var (wall, sim) = ReplaySessionIntents.OnAxis(Axis, t);
            _host.MasterControl.SeekReplay(ReplaySessionIntents.Seek(wall, sim));
        }
    }

    /// <summary>⭐ RB-3 — what only this host has.</summary>
    private sealed class ReplayBrowserControl : IReplayBrowserControl
    {
        private readonly ReplayBrowserSubsystem _host;
        public ReplayBrowserControl(ReplayBrowserSubsystem host) => _host = host;

        /// <summary>
        /// ⭐⭐ <c>CE-1391</c> — THE recording opener of this host (<see cref="IRecordingOpener"/>): <c>POST /replay {path | paths}</c>, the master transition of
        /// <c>POST /replay {exerciseId}</c>, the director routes' <c>recording=</c> and the pack's render jobs and reel items all end HERE, and from here in
        /// <see cref="OpenGroup"/> (the Timeline panel's load path too). An exercise id is found in the inventory under <see cref="RecordingsRoot"/>
        /// (none named: the newest, as a cluster master picks).
        /// </summary>
        public string? Open(RecordingName recording, double atSeconds)
        {
            if (recording == null) return "No recording named.";
            string[] paths;
            if (recording.IsExercise)
            {
                var groups = RecordingGroupInventory.Scan(_host.RecordingsRoot);
                var group = recording.ExerciseId == Guid.Empty
                    ? groups.FirstOrDefault()
                    : groups.FirstOrDefault(g => g.ExerciseId == recording.ExerciseId);
                if (group == null)
                    return recording.ExerciseId == Guid.Empty
                        ? $"No recorded exercise under '{_host.RecordingsRoot}'. Name the files: POST /replay {{\"paths\": [...]}}."
                        : $"Exercise {recording.ExerciseId} is not recorded under '{_host.RecordingsRoot}'. GET /exercises lists what is.";
                paths = group.Paths.ToArray();
            }
            else
            {
                if (recording.Paths.Count == 0) return "No recording files named.";
                var missing = recording.Paths.FirstOrDefault(p => !System.IO.File.Exists(p));
                if (missing != null) return $"No such recording file: '{missing}'.";
                paths = recording.Paths.ToArray();
            }
            _host.RecordingOpens.Add(recording.Text);
            var refused = _host.OpenGroup(paths);
            if (refused != null) return recording.IsExercise ? $"The recording group was refused: {refused}" : refused;
            _host.SeekToSeconds(atSeconds);
            return null;
        }

        /// <summary>⭐ CE-1391 — the group open now (its node files, the exercise its primary node's meta names); null: nothing open.</summary>
        public RecordingName? Shown
        {
            get
            {
                var m = _host._manager;
                if (m == null || m.Contexts.Count == 0) return null;
                var paths = m.Contexts.OrderBy(kv => kv.Key).Select(kv => kv.Value.CurrentFdpPath)
                             .Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToArray();
                return new RecordingName(_host.PrimaryMetadata?.ExerciseId ?? Guid.Empty, paths);
            }
        }

        public JsonObject Status()
        {
            var m = _host._manager;
            var nodes = new JsonArray();
            if (m != null)
                foreach (var (nodeId, ctx) in m.Contexts.OrderBy(kv => kv.Key))
                    nodes.Add(new JsonObject
                    {
                        ["nodeId"] = nodeId,
                        ["frame"]  = ctx.CurrentFrame,
                        ["frames"] = ctx.Playback?.TotalFrames ?? 0,
                        ["path"]   = ctx.CurrentFdpPath,
                    });
            var zero = _host.ReplayZeroTicks;
            var end  = _host.ReplayEndTicks;
            return new JsonObject
            {
                ["exerciseId"]   = m?.ExerciseId.ToString(),
                ["frame"]        = m == null ? null : _host.PrimaryNodeCurrentFrame(),
                ["frames"]       = m != null && m.Contexts.TryGetValue(m.LocalEntitiesProviderNodeId, out var p) ? p.Playback?.TotalFrames : null,
                ["atSeconds"]    = _host.ReplaySeconds is { } s ? Math.Round(s, 6) : null,
                ["spanSeconds"]  = zero is { } z && end is { } e ? Math.Round((e - z) / (double)TimeSpan.TicksPerSecond, 6) : null,
                ["primaryNode"]  = m?.LocalEntitiesProviderNodeId,
                ["view"]         = _host.ViewMode == ViewMode.Merged ? "merged" : "node",
                ["playing"]      = _host.IsPlaying,
                ["rate"]         = _host.PlaybackRate,
                ["nodes"]        = nodes,
                ["history"]      = new JsonObject
                {
                    ["canGoBack"] = _host._playbackHistory.CanGoBack, ["canGoForward"] = _host._playbackHistory.CanGoForward,
                },
                ["selectionHistory"] = new JsonObject
                {
                    ["canGoBack"] = _host._entityHistory.CanGoBack, ["canGoForward"] = _host._entityHistory.CanGoForward,
                },
            };
        }

        public string? View(string view)
        {
            var m = _host._manager;
            if (m == null) return "No recording is open: POST /replay first.";
            if (string.Equals(view, "merged", StringComparison.OrdinalIgnoreCase))
            {
                _host.SetViewMode(ViewMode.Merged);
                return null;
            }
            if (!int.TryParse(view, out int node) || !m.Contexts.ContainsKey(node))
                return $"view is \"merged\" or a loaded node id ({string.Join(", ", m.Contexts.Keys.OrderBy(k => k))}).";
            _host.SetViewMode(ViewMode.SingleNode);
            m.SetLocalEntitiesProvider(node);
            return null;
        }

        public string? History(bool back)
        {
            var h = _host._playbackHistory;
            if (back ? !h.CanGoBack : !h.CanGoForward) return $"Nothing to go {(back ? "back" : "forward")} to in the playback history.";
            if (back) h.GoBack(); else h.GoForward();
            return null;
        }

        public string? SelectionHistory(bool back)
        {
            var h = _host._entityHistory;
            if (back ? !h.CanGoBack : !h.CanGoForward) return $"Nothing to go {(back ? "back" : "forward")} to in the selection history.";
            if (back) h.GoBack(); else h.GoForward();
            return null;
        }

        public (string? Job, string? Error) Export(ReplayExportRequest request) => _host.StartExport(request);

        public JsonObject? ExportStatus(string job) => _host._exports.TryGetValue(job, out var j) ? j.Describe() : null;
    }

    // ── RB-4 — JSON export jobs ─────────────────────────────────────────────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, ExportJob> _exports = new();

    /// <summary>One export: running, then done (the file) or failed (why).</summary>
    private sealed class ExportJob
    {
        public required string Id { get; init; }
        public required string Input { get; init; }
        public required string Output { get; init; }
        public volatile string State = "running";
        public string? Error;

        public JsonObject Describe() => new()
        {
            ["job"] = Id, ["state"] = State, ["input"] = Input, ["path"] = Output,
            ["bytes"] = State == "done" && System.IO.File.Exists(Output) ? new System.IO.FileInfo(Output).Length : null,
            ["error"] = Error,
        };
    }

    /// <summary>
    /// ⭐ RB-4 — the Timeline panel's export (<see cref="IRecordingExportService.ExportToJson"/> of the shown node's recording, on a
    /// long-running task of its own — the panel's own call), with every option the panel has. ⭐ The units are network ids, resolved in the
    /// shown node's world, whose handles are the recording's.
    /// </summary>
    private (string? Job, string? Error) StartExport(ReplayExportRequest r)
    {
        if (_manager == null || !_manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var ctx) || ctx.CurrentFdpPath is not { } input)
            return (null, "No recording is open: POST /replay first.");

        var o = new JsonExportOptions
        {
            IncludeEntities = r.IncludeEntities, IncludeEvents = r.IncludeEvents, Minified = r.Minified, EpsilonTolerance = r.Epsilon,
        };
        switch (r.Window.ToLowerInvariant())
        {
            case "full": o.WindowMode = ExportWindowMode.FullFile; break;
            case "frames":
                o.WindowMode = ExportWindowMode.ByFrame;
                o.StartFrame = (int)(r.From ?? 0);
                o.EndFrame = r.To is { } toFrame ? (int)toFrame : int.MaxValue;
                break;
            case "time":
                o.WindowMode = ExportWindowMode.ByTime;
                o.StartTimeSec = (float)(r.From ?? 0);
                o.EndTimeSec = r.To is { } toSec ? (float)toSec : float.PositiveInfinity;
                break;
            default: return (null, $"window '{r.Window}' is full, frames or time.");
        }
        switch (r.Format.ToLowerInvariant())
        {
            case "absolute": o.FormatMode = ExportFormatMode.AbsoluteState; break;
            case "changelog": o.FormatMode = ExportFormatMode.Changelog; break;
            case "incremental": o.FormatMode = ExportFormatMode.Incremental; break;
            default: return (null, $"format '{r.Format}' is absolute, changelog or incremental.");
        }
        if (r.Entities is { Count: > 0 } ids)
        {
            o.FilterBySelection = true;
            foreach (long id in ids)
            {
                var e = NetworkIdResolver.ResolveNetworkId(ctx.SandboxRepo, id);
                if (e.IsNull) return (null, $"Unit {id} is not in the shown world at this frame.");
                o.TargetEntities.Add(e);
            }
        }
        if (r.EntityIndex is { } index)
        {
            o.FilterByEntityIndex = true;
            o.TargetEntityIndex = index;
        }

        string job = Guid.NewGuid().ToString("N")[..12];
        string output = r.Output ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-replay-exports", $"{job}.json");
        var dir = System.IO.Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        var entry = new ExportJob { Id = job, Input = input, Output = output };
        _exports[job] = entry;
        var export = _exportService ??= new RecordingExportService(Serializer, _diffService);
        System.Threading.Tasks.Task.Factory.StartNew(() =>
        {
            try { export.ExportToJson(input, output, o); entry.State = "done"; }
            catch (Exception ex) { entry.Error = $"{ex.GetType().Name}: {ex.Message}"; entry.State = "failed"; }
        }, System.Threading.Tasks.TaskCreationOptions.LongRunning);
        return (job, null);
    }

    /// <summary>
    /// ⭐ RB-3 — the histories move the federation on a headless host too. ⚠ A windowed host wires them with its panels
    /// (<see cref="WireDelegatesForTest"/>); a headless one builds no panel, so a back / forward would otherwise move nothing.
    /// </summary>
    private void WireHeadlessHistories()
    {
        _playbackHistory.OnWaypointRequested += wp => SeekFrameViaManager(wp.FrameIndex);
        _entityHistory.OnSelectionChanged += e => RequestSelectionFromHistory(e);   // ⭐ RB-7 — the windowed host's handler, minus its panel
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> (R-4087 addendum 5; DESIGN_Director_Timeline_Editor.md §2c rule 6) — a timeline event clicked selects its unit through
    /// the ONE selection (the request RB-7's history and <c>POST /selection</c> use): the subject's network id → the shown world's entity.
    /// ⛔ An event with no networked subject (a round) selects nothing. Never a seek (R-3059).
    /// </summary>
    internal bool SelectTimelineEvent(Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent e)
    {
        if (e.Subject <= 0 || _activeRepo is not { } world || !world.HasSingletonManaged<NetworkEntityMap>()) return false;
        if (world.GetSingletonManaged<NetworkEntityMap>() is not { } map || !map.TryGetEntity(e.Subject, out var entity) || !world.IsAlive(entity))
            return false;
        return RequestSelectionFromHistory(entity);
    }

    /// <summary>
    /// ⭐⭐⭐ UXI-11 S-3b — a step through the selection history REQUESTS the selection (the map ring and the inspector follow it through
    /// <c>SelectionRequestSystem</c>), on a windowed and a headless host alike. <see langword="false"/> when no world is shown.
    /// </summary>
    private bool RequestSelectionFromHistory(Entity e)
    {
        if (_activeRepo == null) return false;
        _activeRepo.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest.ReplaceWith(e, "Replay.History"));
        return true;
    }

    /// <summary>
    /// ⭐⭐ The provider — every accessor a <see cref="Func{T}"/>, because this host's world is swapped on every seek, step and view
    /// switch (<see cref="RebindActiveRepo"/>): a captured value would answer for the pre-load world forever.
    /// </summary>
    private ISubsystemDebugProvider BuildDebugProvider()
        => new SubsystemDebugProvider(
            subsystemName: Name,
            // ⭐ "ReplayBrowser" — the perspective the window manager reports (DESIGN_Perspective_Unification.md:84); the dispatcher matches on it.
            perspective:   "ReplayBrowser",
            world:         () => _activeRepo,
            tkbDb:         () => _tkb,   // ⭐ CE-4058 ① — the catalogue every repo here carries
            // ⭐ the map off the CURRENT repo (EnsureNetworkEntityMap rebuilds it on every rebind)
            entityMap:     SubsystemDebugProvider.EntityMapFrom(() => _activeRepo),
            // ⭐ CE-171 — serializer-backed, the inspector's own serializer
            extraction:    ExtractionForActiveWorld,
            drive:         () => Transport,
            // ⭐ the very buffer this host's map draws and its gizmo systems fill from Update
            gizmoBuffer:   () => _gizmoBuffer,
            master:        () => MasterControl,
            clusterState:  () => ReplayState,
            exercises:     ExercisesOnDisk,
            replayAxis:     () => Axis,   // ⭐ CE-3329 — the viewer's
            replayDuration: () => ReplayZeroTicks is { } z && ReplayEndTicks is { } e ? (float)((e - z) / (double)TimeSpan.TicksPerSecond) : null,
            replayRecording: () => PrimaryRecordingPath,
            // ⭐ RB-2 — where the replay stands on the wall clock and its zero: what frame-based seeks resolve against
            replayPosition: () => ReplayZeroTicks is { } z && _manager is { } m
                                  ? new ReplayPosition(z, m.BaseWallTicks, TimeMap is { HasSimTime: true } tm ? tm.SimAt(m.BaseWallTicks) : null) : null,
            // ⭐ RB-5 — the shown frame's recorded events
            eventHistory:  () => PrimaryEventHistory,
            // ⭐ RB-6 — the shown node's timeline index and this host's lane catalogue
            timeline:      () => _timelineAccess ??= new TimelineAccess(() => _timelineSource, _timelineLanes, TimelineContext),
            replayBrowser: () => BrowserControl);
}
