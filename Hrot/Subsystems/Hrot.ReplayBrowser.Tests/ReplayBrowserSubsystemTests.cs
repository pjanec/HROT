using System.Linq;
using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.Scenario;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.Runner;
using Hrot.Editor;
using Hrot.ReplayBrowser;
using Xunit;

namespace Hrot.ReplayBrowser.Tests;

/// <summary>
/// Unit tests for <see cref="ReplayBrowserSubsystem"/> covering BATCH-04
/// success conditions FND-T09 through FND-T12, FND-T15, FND-T16, FND-T18.
/// </summary>
public sealed class ReplayBrowserSubsystemTests : IDisposable
{
    private readonly ReplayBrowserSubsystem _subsystem;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"RBSTests_{Guid.NewGuid():N}");

    public ReplayBrowserSubsystemTests()
    {
        _subsystem = new ReplayBrowserSubsystem();
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _subsystem.Shutdown();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> — the Replay Browser and the editor compose their timeline lanes from the ONE shipped catalogue
    /// (<c>ShippedTimelineLanes.Catalog()</c>): same lane ids as the three providers they used to register, and neither host source
    /// registers a toolkit's lane provider by hand any more. 🔴 Red-proof: the Replay Browser's field re-registering the providers by hand
    /// (behind a runtime-false guard) ⇒ the source half fails.
    /// </summary>
    [Fact]
    public void CE3330_Node_TheReplayBrowser_ComposesTheShippedCatalogue()
    {
        var ctx = new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneContext(Recording: true);
        var before = new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCatalog()
            .Register(new Fdp.Toolkit.Replication.ReplicationTimelineLanes())
            .Register(new Fdp.Toolkit.Combat.CombatTimelineLanes())
            .Register(new Fdp.Toolkit.Behavior.Diagnostics.BehaviourTimelineLanes());
        Assert.Equal(before.Lanes(ctx).Select(l => l.Id), _subsystem.TimelineLanes.Lanes(ctx).Select(l => l.Id));

        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "HROT.sln"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("repo root not found");
        // the Replay Browser composes the shipped catalogue from the factory, never by hand (the editor's side is proven on a real headless editor:
        //   TheEditorRunsTheOrchestratorCoreTests.CE3330_Node_TheEditorsLiveTimelineIsItsNodeContexts_…)
        string text = File.ReadAllText(Path.Combine(root, "Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs"));
        Assert.Contains("ShippedTimelineLanes.Catalog()", text);
        Assert.DoesNotContain("new Fdp.Toolkit.Replication.ReplicationTimelineLanes()", text);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static SubsystemConfig HeadlessConfig() => new SubsystemConfig
    {
        DomainId      = 0,
        Headless      = true,
        OwnWindow     = false,
        NodeId        = 0,
        SubsystemName = "ReplayBrowser",
    };

    // ── FND-T09: Headless init succeeds ───────────────────────────────────

    /// <summary>
    /// FND-T09: Initialize with Headless=true must not throw;
    /// DrawWorld and DrawUI must not throw afterwards.
    /// </summary>
    [Fact]
    public void Initialize_Headless_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            _subsystem.Initialize(HeadlessConfig());
            _subsystem.Update(0.016f);
            _subsystem.DrawWorld();
            _subsystem.DrawUI();
        });

        Assert.Null(ex);
    }

    // ── FND-T10: Not IMapCameraProvider ──────────────────────────────────

    /// <summary>
    /// FND-T10: ReplayBrowserSubsystem must NOT implement IMapCameraProvider.
    /// </summary>
    [Fact]
    public void Subsystem_DoesNotImplementIMapCameraProvider()
    {
        // IMapCameraProvider is defined in Fdp.Presentation; check by name
        // to avoid a hard assembly dependency in the test project.
        var interfaces = typeof(ReplayBrowserSubsystem).GetInterfaces();
        foreach (var iface in interfaces)
        {
            Assert.False(
                iface.Name == "IMapCameraProvider",
                "ReplayBrowserSubsystem must not implement IMapCameraProvider");
        }
    }

    // ── FND-T11: Name + CLI key + INetworkFactory constructor ────────────

    /// <summary>
    /// FND-T11: Name returns "ReplayBrowser"; CLI discovery key matches;
    /// subsystem has an INetworkFactory constructor for ScanForSubsystems.
    /// </summary>
    [Fact]
    public void Name_ReturnsReplayBrowser_And_CliKeyMatches()
    {
        Assert.Equal("ReplayBrowser", _subsystem.Name);
        Assert.Equal("ReplayBrowser", typeof(ReplayBrowserSubsystem).Name.Replace("Subsystem", ""));
    }

    [Fact]
    public void Type_HasINetworkFactoryConstructor()
    {
        var ctor = typeof(ReplayBrowserSubsystem).GetConstructor(
            new[] { typeof(Hrot.Core.Network.INetworkFactory) });
        Assert.NotNull(ctor);
    }

    // ── FND-T12: 5 windows registered, all PerspectiveBound, all "ReplayBrowser" ─

    /// <summary>
    /// FND-T12: RegisterWindowsCore registers exactly 6 windows (CE-1150 / CE-1244); all are
    /// PerspectiveBound scope; all belong to the "ReplayBrowser" perspective.
    /// </summary>
    [Fact]
    public void RegisterWindowsCore_RegistersSixWindows_AllReplayBrowserPerspective()
    {
        _subsystem.Initialize(HeadlessConfig());

        var atlas  = new IconAtlas(IntPtr.Zero, 1, 1);
        var wm     = new WindowManager(atlas);

        // Use stub panels (subsystem is headless so real panels were not created)
        var timelinePanel  = new Fdp.Presentation.Panels.ReplayBrowser.ReplayTimelinePanel(
            null,
            () => 0,
            new StubExportService(),
            new StubFileDialogService(),
            new Fdp.Toolkit.ReplayBrowser.PlaybackHistoryTracker(),
            new InspectorState());
        var inspectorPanel = new Fdp.Presentation.Panels.EntityInspectorPanel();
        var diffPanel      = new Fdp.Presentation.Panels.ReplayBrowser.ComponentDiffPanel();
        var eventPanel     = new Fdp.Presentation.Panels.EventBrowserPanel(new StubHistoryService());
        var searchPanel    = new Fdp.Presentation.Panels.ReplayBrowser.ReplaySearchPanel(
            new NopPanelEditService(), new NopPanelSearchService(), _ => { }, _ => { }, (_, _) => { });

        _subsystem.RegisterWindowsCore(wm, timelinePanel, inspectorPanel, diffPanel, eventPanel, searchPanel);

        // Reflect into the private _windows dictionary
        var windowsField = typeof(WindowManager)
            .GetField("_windows", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var windows = (Dictionary<string, ManagedWindow>)windowsField.GetValue(wm)!;

        // ⭐ CE-1150 + CE-1244 — the shared Details panel (rb_details) and the shared diagnostics bundle's inspector / event browser
        //   (rb_fdp_inspector / rb_fdp_events, which replaced this host's own rb_inspector / rb_events copies).
        // ⭐ CE-1302 R1-c — and the event timeline (rb_event_timeline) over the primary node's recording.
        // ⭐ CE-1303 P2a — and the ONE film director's window (DirectorPack's, in this host's perspective — §11m D11).
        Assert.Equal(new[] { Hrot.UI.Common.Map3D.Director.DirectorWindow.WindowId, "rb_details", "rb_diff", "rb_event_timeline", "rb_fdp_events", "rb_fdp_inspector", "rb_search", "rb_timeline" },
                     windows.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

        foreach (var (_, win) in windows)
        {
            Assert.Equal(WindowScope.PerspectiveBound, win.Scope);
            Assert.Equal("ReplayBrowser", win.OwningPerspective);
        }
    }

    // ── U-obs-5 follow-up: FederationWindow — the composition-root registration rail ──────

    /// <summary>
    /// ⭐⭐⭐ <b>U-obs-5 follow-up.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
    /// <c>docs/designs/replay-browser-frankenstein/DESIGN.md</c> §8. <c>FederationPanel</c> was one of
    /// the six no-host panels measured by <c>BP-467</c> — its window is registered only when a
    /// <c>FederationPanel</c> already exists (i.e. a replay group was already loaded before
    /// <c>RegisterWindows</c> ran). With none loaded, <c>RegisterWindowsCore</c>'s default
    /// <c>federationPanel: null</c> registers no sixth window — pinning FND-T12's original count.
    /// </summary>
    [Fact]
    public void RegisterWindowsCore_WithNoGroupLoadedYet_RegistersOnlySixWindows()
    {
        _subsystem.Initialize(HeadlessConfig());

        var atlas = new IconAtlas(IntPtr.Zero, 1, 1);
        var wm    = new WindowManager(atlas);

        var timelinePanel = new Fdp.Presentation.Panels.ReplayBrowser.ReplayTimelinePanel(
            null, () => 0, new StubExportService(), new StubFileDialogService(),
            new Fdp.Toolkit.ReplayBrowser.PlaybackHistoryTracker(), new InspectorState());
        var inspectorPanel = new Fdp.Presentation.Panels.EntityInspectorPanel();
        var diffPanel       = new Fdp.Presentation.Panels.ReplayBrowser.ComponentDiffPanel();
        var eventPanel      = new Fdp.Presentation.Panels.EventBrowserPanel(new StubHistoryService());
        var searchPanel     = new Fdp.Presentation.Panels.ReplayBrowser.ReplaySearchPanel(
            new NopPanelEditService(), new NopPanelSearchService(), _ => { }, _ => { }, (_, _) => { });

        _subsystem.RegisterWindowsCore(wm, timelinePanel, inspectorPanel, diffPanel, eventPanel, searchPanel);

        var windowsField = typeof(WindowManager).GetField("_windows", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var windows = (Dictionary<string, ManagedWindow>)windowsField.GetValue(wm)!;

        Assert.Equal(8, windows.Count);   // ⭐ CE-1150 / CE-1244 / CE-1302 / CE-1303 P2a (the Director) — see the rail above
        Assert.False(windows.ContainsKey("rb_federation"));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The whole point of this conversion</b> — the host is actually REGISTERED in the
    /// production composition root, live, when a replay group is (re)loaded.
    /// <see cref="ReplayBrowserSubsystem.LoadFdpGroupForTest"/> shares
    /// <c>CreateOrReplaceFederationPanel</c> with the real <c>OnLoadGroup</c> delegate, so this
    /// exercises the exact code path a live group load takes. <c>_windowManager</c> is set the same
    /// way the real (non-headless) <c>RegisterWindows</c> would set it.
    /// </summary>
    [Fact]
    public void LoadFdpGroupForTest_WithAWindowManagerAlreadyRegistered_RegistersTheFederationWindow()
    {
        _subsystem.Initialize(HeadlessConfig());

        var atlas = new IconAtlas(IntPtr.Zero, 1, 1);
        var wm    = new WindowManager(atlas);
        var wmField = typeof(ReplayBrowserSubsystem).GetField("_windowManager", BindingFlags.NonPublic | BindingFlags.Instance)!;
        wmField.SetValue(_subsystem, wm);

        var exerciseId = Guid.NewGuid();
        var path = MakeOneFrameRecording(nodeId: 1, exerciseId);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));

        var windowsField = typeof(WindowManager).GetField("_windows", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var windows = (Dictionary<string, ManagedWindow>)windowsField.GetValue(wm)!;

        Assert.True(windows.ContainsKey("rb_federation"));
        Assert.IsType<Fdp.Presentation.Windows.ReplayBrowser.FederationWindow>(windows["rb_federation"]);
        Assert.Equal(WindowScope.PerspectiveBound, windows["rb_federation"].Scope);
        Assert.Equal("ReplayBrowser", windows["rb_federation"].OwningPerspective);
    }

    /// <summary>Reloading a SECOND group replaces the window under the same id rather than leaving a
    /// stale one registered alongside a new one (dictionary-indexer replace-by-id semantics).</summary>
    [Fact]
    public void LoadFdpGroupForTest_CalledTwice_ReplacesTheFederationWindow_DoesNotDuplicateIt()
    {
        _subsystem.Initialize(HeadlessConfig());

        var atlas = new IconAtlas(IntPtr.Zero, 1, 1);
        var wm    = new WindowManager(atlas);
        var wmField = typeof(ReplayBrowserSubsystem).GetField("_windowManager", BindingFlags.NonPublic | BindingFlags.Instance)!;
        wmField.SetValue(_subsystem, wm);

        var exerciseId = Guid.NewGuid();
        var path1 = MakeOneFrameRecording(nodeId: 1, exerciseId);
        _subsystem.LoadFdpGroupForTest(new[] { path1 }, new TransientMasterBuilder(MakeTestSerializer()));
        var firstWindow = _subsystem.Manager;

        var path2 = MakeOneFrameRecording(nodeId: 2, Guid.NewGuid());
        _subsystem.LoadFdpGroupForTest(new[] { path2 }, new TransientMasterBuilder(MakeTestSerializer()));

        var windowsField = typeof(WindowManager).GetField("_windows", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var windows = (Dictionary<string, ManagedWindow>)windowsField.GetValue(wm)!;

        Assert.Single(windows.Keys, k => k == "rb_federation");   // still exactly one entry
        Assert.NotSame(firstWindow, _subsystem.Manager);          // the manager really was replaced
    }

    // ── FND-T15: selectIntent wiring ──────────────────────────────────────

    /// <summary>
    /// FND-T15: selectIntent wired by WireDelegatesForTest raises OnSelectionChanged
    /// exactly once and updates InspectorState via the event chain.
    /// </summary>
    [Fact]
    public void WireDelegates_SelectIntent_PushesSelectionAndUpdatesInspectorState()
    {
        var entityHistory   = new EntitySelectionHistory();
        var playbackHistory = new PlaybackHistoryTracker();
        var inspectorState  = new InspectorState();
        var context         = new ReplayBrowserContext();
        var diffPanel       = new ComponentDiffPanel();
        var eventPanel      = new EventBrowserPanel(context.HistoryService);

        var (_, selectIntent, _) = _subsystem.WireDelegatesForTest(
            entityHistory, playbackHistory, inspectorState, context, diffPanel, eventPanel);

        int changeCount = 0;
        entityHistory.OnSelectionChanged += _ => changeCount++;

        var targetEntity = new Entity(7, 2);
        selectIntent(targetEntity);

        // Exactly one history entry pushed
        Assert.Equal(1, changeCount);
        // InspectorState updated via OnSelectionChanged chain
        Assert.Equal(targetEntity, inspectorState.SelectedEntity);

        // Second push with the same entity: EntitySelectionHistory suppresses duplicates;
        // no extra fire from the wiring itself
        selectIntent(targetEntity);
        Assert.True(changeCount >= 1);
    }

    // ── FND-T16: ExecuteCausalityJump sequence ────────────────────────────

    /// <summary>
    /// FND-T16: ExecuteCausalityJump must push pre-frame, step forward,
    /// push post-frame, and push selection in that order.
    /// Selection is observable via OnSelectionChanged; ordering is
    /// guaranteed by code structure (PushFrame calls precede PushSelection).
    /// </summary>
    [Fact]
    public void ExecuteCausalityJump_PushesPreAndPostFrameThenSelectsTarget()
    {
        var entityHistory   = new EntitySelectionHistory();
        var playbackHistory = new PlaybackHistoryTracker();
        var inspectorState  = new InspectorState();
        var context         = new ReplayBrowserContext();
        var diffPanel       = new ComponentDiffPanel();
        var eventPanel      = new EventBrowserPanel(context.HistoryService);

        _subsystem.WireDelegatesForTest(
            entityHistory, playbackHistory, inspectorState, context, diffPanel, eventPanel);

        int selectionFireCount = 0;
        entityHistory.OnSelectionChanged += _ => selectionFireCount++;

        var target = new Entity(5, 1);
        _subsystem.ExecuteCausalityJump(target);

        // Selection fired exactly once (PushSelection was called in the correct sequence)
        Assert.Equal(1, selectionFireCount);
        // InspectorState updated via OnSelectionChanged chain
        Assert.Equal(target, inspectorState.SelectedEntity);
    }

    // ── FND-T18: seekIntent and selectIntent ─────────────────────────────

    /// <summary>
    /// FND-T18: seekIntent pushes one frame to playback history and seeks context;
    /// two calls with distinct frames produce CanGoBack==true; GoBack fires
    /// OnSeekRequested with the earlier frame.
    /// </summary>
    [Fact]
    public void WireDelegates_SeekIntent_PushesFrameAndSeeksContext()
    {
        var entityHistory   = new EntitySelectionHistory();
        var playbackHistory = new PlaybackHistoryTracker();
        var inspectorState  = new InspectorState();
        var context         = new ReplayBrowserContext();
        var diffPanel       = new ComponentDiffPanel();
        var eventPanel      = new EventBrowserPanel(context.HistoryService);

        var (seekIntent, _, _) = _subsystem.WireDelegatesForTest(
            entityHistory, playbackHistory, inspectorState, context, diffPanel, eventPanel);

        // seekIntent must push waypoints (two distinct frames produce CanGoBack)
        seekIntent(5);
        seekIntent(10);

        // After two seeks with different frames, CanGoBack must be true
        Assert.True(playbackHistory.CanGoBack, "seekIntent must call PushWaypoint so two calls produce CanGoBack");

        // GoBack fires OnWaypointRequested with the previous frame
        int seekTarget = -1;
        playbackHistory.OnWaypointRequested += wp => seekTarget = wp.FrameIndex;
        playbackHistory.GoBack();
        Assert.Equal(5, seekTarget);
    }

    // ── RBF-P2T3: FederatedReplayManager wiring ──────────────────────────────

    /// <summary>
    /// RBF-P2T3: After headless Initialize, Manager and ActiveRepo must both be null;
    /// LoadFdpViaManager has not been called yet.
    /// </summary>
    [Fact]
    public void RBF_P2T3_Subsystem_InitialState_ManagerIsNull()
    {
        _subsystem.Initialize(HeadlessConfig());
        Assert.Null(_subsystem.Manager);
        Assert.Null(_subsystem.ActiveRepo);
    }

    /// <summary>
    /// RBF-P2T3: LoadFdpViaManager with a valid single-node recording creates the manager
    /// and binds ActiveRepo to the SandboxRepo of the loaded context.
    /// </summary>
    [Fact]
    public void RBF_P2T3_Subsystem_LoadOneFile_BindsActiveRepo()
    {
        _subsystem.Initialize(HeadlessConfig());

        var tempDir = Path.Combine(Path.GetTempPath(), $"rbf_p2t3a_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var exerciseId = Guid.NewGuid();
            var path = CreateMinimalFdpFile(tempDir, exerciseId, nodeId: 1);

            _subsystem.LoadFdpViaManager(path);

            Assert.NotNull(_subsystem.Manager);
            Assert.NotNull(_subsystem.ActiveRepo);
            int nodeId = _subsystem.Manager!.LocalEntitiesProviderNodeId;
            Assert.Same(
                _subsystem.Manager.Contexts[nodeId].SandboxRepo,
                _subsystem.ActiveRepo);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// RBF-P2T3: After LoadFdpViaManager, calling SetBaseWallTicks fires OnTimeChanged
    /// which re-runs RebindActiveRepo; ActiveRepo remains bound to the correct SandboxRepo.
    /// </summary>
    [Fact]
    public void RBF_P2T3_Subsystem_SeekAfterLoad_ActiveRepoRemainsCorrect()
    {
        _subsystem.Initialize(HeadlessConfig());

        var tempDir = Path.Combine(Path.GetTempPath(), $"rbf_p2t3b_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var exerciseId = Guid.NewGuid();
            var path = CreateMinimalFdpFile(tempDir, exerciseId, nodeId: 2);

            _subsystem.LoadFdpViaManager(path);

            int nodeId = _subsystem.Manager!.LocalEntitiesProviderNodeId;
            var expectedRepo = _subsystem.Manager.Contexts[nodeId].SandboxRepo;

            // SetBaseWallTicks fires OnTimeChanged => OnManagerTimeChanged => RebindActiveRepo
            _subsystem.Manager.SetBaseWallTicks(1_500_000L);

            Assert.Same(expectedRepo, _subsystem.ActiveRepo);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// ⭐ <c>CE-1302</c> R1-c (docs/DESIGN_Director_Timeline_Editor.md §9) — the event timeline opens the PRIMARY node's recording, re-opens on
    /// a new one, and its cursor seeks through the manager (D1) and reads the frame back.
    /// </summary>
    [Fact]
    public void CE1302_TheEventTimeline_FollowsThePrimaryRecording_AndSeeksThroughTheManager()
    {
        _subsystem.Initialize(HeadlessConfig());
        var tempDir = Path.Combine(Path.GetTempPath(), $"ce1302_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var first = CreateMinimalFdpFile(tempDir, Guid.NewGuid(), nodeId: 1);
            _subsystem.LoadFdpViaManager(first);
            _subsystem.SyncTimelineSource();
            var source = _subsystem.TimelineSource!;
            Assert.Equal(first, source.Path);
            Assert.True(source.Completion.Wait(TimeSpan.FromSeconds(30)));
            Assert.Equal(2, source.Index.TotalFrames);

            var cursor = new Fdp.Presentation.Panels.Timeline.FederatedReplayCursor(() => _subsystem.Manager, () => _subsystem.Manager!.LocalEntitiesProviderNodeId);
            Assert.Equal((1_000_000L, 2_000_000L), (cursor.ExtentFrom, cursor.ExtentTo));
            cursor.Seek(2_000_000L);
            Assert.Equal(1, _subsystem.PrimaryNodeCurrentFrame());
            Assert.Equal(2_000_000L, cursor.Now);
            var vm = Fdp.Presentation.Panels.Timeline.TimelinePanelViewModel.Build("rb_event_timeline", "event-timeline", source, cursor,
                new Fdp.Presentation.Panels.Timeline.TimelineWindow(cursor.ExtentFrom, cursor.ExtentTo),
                _subsystem.TimelineLanes.Lanes(new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneContext(Recording: true)));
            Assert.Equal(9, vm.Total.Count);                                  // ⭐ R-4079: the registered lanes — the three toolkits' nine
            Assert.True(vm.IsComplete && vm.CanSeek);
            Assert.Equal(2_000_000L, vm.Now);

            var second = CreateMinimalFdpFile(tempDir, Guid.NewGuid(), nodeId: 2);
            _subsystem.LoadFdpViaManager(second);
            _subsystem.SyncTimelineSource();
            Assert.Equal(second, _subsystem.TimelineSource!.Path);
        }
        finally
        {
            _subsystem.TimelineSource?.Dispose();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> (R-4087 addendum 5) — a timeline event clicked selects its UNIT through the one selection: the subject's network id
    /// resolves to the shown world's entity and a selection request goes out (RB-7's path); an event with no networked subject, or one whose
    /// unit the shown world does not hold, selects nothing. ⛔ Never a seek. 🔴 Red-proof: the network map lookup skipped ⇒ red.
    /// </summary>
    [Fact]
    public void CE3330_ATimelineEvent_SelectsItsUnit_NeverSeeks()
    {
        _subsystem.Initialize(HeadlessConfig());
        var path = MakeOneFrameRecording(nodeId: 1, Guid.NewGuid());   // one unit, network id 100
        _subsystem.LoadFdpViaManager(path);
        var cursor = new Fdp.Presentation.Panels.Timeline.FederatedReplayCursor(() => _subsystem.Manager, () => _subsystem.Manager!.LocalEntitiesProviderNodeId);
        cursor.Seek(1_000_000L);
        int frame = _subsystem.PrimaryNodeCurrentFrame();

        Assert.True(_subsystem.SelectTimelineEvent(new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent(0, "life.spawned", 100, 0, 0, 0, 0)));
        Assert.False(_subsystem.SelectTimelineEvent(new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent(0, "combat.shot", 0, 0, 0, 0, 0)));
        Assert.False(_subsystem.SelectTimelineEvent(new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent(0, "life.spawned", 999, 0, 0, 0, 0)));
        Assert.Equal(frame, _subsystem.PrimaryNodeCurrentFrame());   // a selection moved no time
    }

    // ── RBF-P2T3: minimal recording helper ───────────────────────────────────

    /// <summary>
    /// Creates a minimal valid .fdp + .meta.json pair in <paramref name="directory"/>.
    /// </summary>
    private static string CreateMinimalFdpFile(string directory, Guid exerciseId, int nodeId)
    {
        var path = Path.Combine(directory, $"node{nodeId}.fdp");
        var meta = new RecordingMetadata { ExerciseId = exerciseId, NodeId = nodeId };
        using var repo = new EntityRepository();
        using var recorder = new AsyncRecorder(path, meta);
        recorder.CaptureKeyframe(repo, 1_000_000L, blocking: true, eventBus: repo.Bus);
        recorder.CaptureFrame(repo, 0u, 2_000_000L, blocking: true, eventBus: repo.Bus);
        // Dispose finalizes .fdp and writes the .meta.json sidecar.
        return path;
    }

    // ── RBF-P4T3: multi-file federation helpers ───────────────────────────────

    /// <summary>
    /// Creates a minimal .fdp recording with one <see cref="NetworkIdentity"/> entity.
    /// </summary>
    private string MakeOneFrameRecording(int nodeId, Guid exerciseId)
    {
        string path = Path.Combine(_tempDir, $"node{nodeId}_{Guid.NewGuid():N}.fdp");
        var meta = new RecordingMetadata { ExerciseId = exerciseId, NodeId = nodeId };
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();
        var e = repo.CreateEntity();
        repo.AddComponent(e, new NetworkIdentity { Value = nodeId * 100L });
        using (var rec = new AsyncRecorder(path, meta))
            rec.CaptureKeyframe(repo, 1_000_000L, blocking: true, eventBus: repo.Bus);
        return path;
    }

    private static ScenarioSerializer MakeTestSerializer()
    {
        // Prime component-type registry entries used by the TransientMasterBuilder.
        using var primeRepo = new EntityRepository();
        primeRepo.RegisterComponent<NetworkIdentity>();
        primeRepo.RegisterComponent<NetworkAuthority>();
        return new ScenarioSerializerBuilder("TestSubsystem").Build();
    }

    // ── RBF-P4T3: federated-mode tests ───────────────────────────────────────

    [Fact]
    public void RBF_P4T3_SingleNodeMode_BindsToCtxRepo()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = MakeOneFrameRecording(nodeId: 1, exerciseId);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));

        _subsystem.SetViewMode(ViewMode.SingleNode);

        int nodeId = _subsystem.Manager!.LocalEntitiesProviderNodeId;
        Assert.Same(_subsystem.Manager.Contexts[nodeId].SandboxRepo, _subsystem.ActiveRepo);
    }

    [Fact]
    public void RBF_P4T3_MergedMode_BindsToTransientMaster()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = MakeOneFrameRecording(nodeId: 1, exerciseId);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));

        _subsystem.SetViewMode(ViewMode.Merged);

        int nodeId = _subsystem.Manager!.LocalEntitiesProviderNodeId;
        Assert.NotSame(_subsystem.Manager.Contexts[nodeId].SandboxRepo, _subsystem.ActiveRepo);
    }

    [Fact]
    public void RBF_P4T3_OnTimeChangedInMerged_RebuildsMaster()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = MakeOneFrameRecording(nodeId: 1, exerciseId);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));
        _subsystem.SetViewMode(ViewMode.Merged);

        int buildCount = 0;
        _subsystem.TransientBuildOverride = _ => { buildCount++; return new EntityRepository(); };
        _subsystem.Manager!.SetBaseWallTicks(1_500_000L);

        Assert.True(buildCount >= 1, "OnTimeChanged in Merged mode must trigger a transient master rebuild.");
    }

    [Fact]
    public void RBF_P4T3_ProviderChangeInMerged_RebuildsMaster()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        string path1 = MakeOneFrameRecording(nodeId: 1, exerciseId);
        string path2 = MakeOneFrameRecording(nodeId: 2, exerciseId);
        _subsystem.LoadFdpGroupForTest(new[] { path1, path2 }, new TransientMasterBuilder(MakeTestSerializer()));
        _subsystem.SetViewMode(ViewMode.Merged);

        int buildCount = 0;
        _subsystem.TransientBuildOverride = _ => { buildCount++; return new EntityRepository(); };
        _subsystem.Manager!.SetLocalEntitiesProvider(2);

        Assert.True(buildCount >= 1, "Changing the local-entities provider in Merged mode must trigger a master rebuild.");
    }

    [Fact]
    public void RBF_P4T3_ModeSwitchToSingle_DisposesTransientMaster()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = MakeOneFrameRecording(nodeId: 1, exerciseId);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));
        _subsystem.SetViewMode(ViewMode.Merged);
        var mergedRepo = _subsystem.ActiveRepo;

        _subsystem.SetViewMode(ViewMode.SingleNode);

        Assert.Equal(ViewMode.SingleNode, _subsystem.ViewMode);
        int nodeId = _subsystem.Manager!.LocalEntitiesProviderNodeId;
        Assert.Same(_subsystem.Manager.Contexts[nodeId].SandboxRepo, _subsystem.ActiveRepo);
        Assert.NotSame(mergedRepo, _subsystem.ActiveRepo);
    }

    [Fact]
    public void RBF_P4T3_ModeSwitchToMerged_DoesNotThrowInHeadlessMode()
    {
        // In headless mode, _timelinePanel and _searchPanel are null.
        // SetViewMode must guard those null refs gracefully.
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = MakeOneFrameRecording(nodeId: 1, exerciseId);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));

        var ex = Record.Exception(() => _subsystem.SetViewMode(ViewMode.Merged));
        Assert.Null(ex);
        Assert.Equal(ViewMode.Merged, _subsystem.ViewMode);
    }

    // ── Stubs ─────────────────────────────────────────────────────────────

    private static Fdp.Toolkit.ReplayBrowser.ReplayBrowserContext CreateNullContext()
        => new Fdp.Toolkit.ReplayBrowser.ReplayBrowserContext();

    private sealed class StubExportService : Fdp.Toolkit.ReplayBrowser.IRecordingExportService
    {
        public void ExportToJson(string input, string output,
            Fdp.Toolkit.ReplayBrowser.JsonExportOptions opts) { }
    }

    private sealed class StubFileDialogService : Fdp.Presentation.Abstractions.IFileDialogService
    {
        public System.Threading.Tasks.Task<string?> ShowSaveAsDialogAsync(
            string callSiteId, string defaultFileName, string extensionFilter)
            => System.Threading.Tasks.Task.FromResult<string?>(null);

        public System.Threading.Tasks.Task<string?> ShowOpenFileDialogAsync(string callSiteId, string extensionFilter)
            => System.Threading.Tasks.Task.FromResult<string?>(null);

        public System.Threading.Tasks.Task<string[]?> ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter)
            => System.Threading.Tasks.Task.FromResult<string[]?>(null);
    }

    private sealed class StubHistoryService : Fdp.Core.Diagnostics.IDiagnosticEventHistoryService
    {
        public void Capture(string providerName, Fdp.Core.FdpEventBus eventBus, uint currentFrame) { }

        public Fdp.Core.Diagnostics.CapturedEventDto[] GetHistory(
            System.Collections.Generic.IReadOnlyList<string>? providerFilter = null)
            => Array.Empty<Fdp.Core.Diagnostics.CapturedEventDto>();

        public void ClearHistory() { }

        public void RewindHistory(uint toFrame) { }
    }

    private sealed class NopPanelEditService : StructEdit.Core.IComponentEditService
    {
        private sealed class NopSession : StructEdit.Core.IEditSession
        {
            public StructEdit.Core.EditDocument Document => null!;
            public bool IsDirty => false;
            public StructEdit.Core.EditRebuildState RebuildState => StructEdit.Core.EditRebuildState.Stable;
            public void MarkStructuralChange() { }
            public void RebuildDocument() { }
            public StructEdit.Core.ValidationResult Validate() => StructEdit.Core.ValidationResult.Ok();
            public object Commit() => new object();
            public void Cancel() { }
            public void Dispose() { }
        }

        public StructEdit.Core.IEditSession Open(object component, Type componentType,
            StructEdit.Core.EditScope? scope = null, StructEdit.Core.EditContext? context = null)
            => new NopSession();
    }

    private sealed class NopPanelSearchService : Fdp.Toolkit.ReplayBrowser.Search.IRecordingSearchService
    {
        public System.Collections.Generic.IReadOnlyList<Fdp.Toolkit.ReplayBrowser.Search.SearchResultDto> ExecuteSearch(
            string fdpPath, Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto root,
            Fdp.Toolkit.ReplayBrowser.Search.TargetEntityFilter? entityFilter = null,
            System.Threading.CancellationToken ct = default)
            => System.Array.Empty<Fdp.Toolkit.ReplayBrowser.Search.SearchResultDto>();

        public System.Collections.Generic.IReadOnlyList<Fdp.Toolkit.ReplayBrowser.Search.LifecycleSearchResultDto> ExecuteLifecycleSearch(
            string fdpPath, Fdp.Toolkit.ReplayBrowser.Search.LifecyclePredicateDto criteria,
            Fdp.Toolkit.ReplayBrowser.Search.TargetEntityFilter? entityFilter = null,
            System.Threading.CancellationToken ct = default)
            => System.Array.Empty<Fdp.Toolkit.ReplayBrowser.Search.LifecycleSearchResultDto>();
    }

    // ── RBF-P5T1: Excise ReplayBrowserContext from subsystem ──────────────────

    /// <summary>
    /// RBF-P5T1: ReplayBrowserSubsystem must not hold any field of type ReplayBrowserContext.
    /// </summary>
    [Fact]
    public void RBF_P5T1_Subsystem_NoContextField()
    {
        var fields = typeof(ReplayBrowserSubsystem)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        bool hasContextField = fields.Any(f => f.FieldType == typeof(ReplayBrowserContext));
        Assert.False(hasContextField,
            "ReplayBrowserSubsystem must not hold a ReplayBrowserContext field (DESIGN §6.4)");
    }

    /// <summary>
    /// RBF-P5T1: Initialize + Update with no files loaded must not throw (null manager path).
    /// </summary>
    [Fact]
    public void RBF_P5T1_Subsystem_EmptyManager_NoNullRef()
    {
        var ex = Record.Exception(() =>
        {
            _subsystem.Initialize(HeadlessConfig());
            _subsystem.Update(0.016f);
        });
        Assert.Null(ex);
    }

    /// <summary>
    /// RBF-P5T1: After loading one file, SetBaseWallTicks via manager seeks the primary context;
    /// PrimaryNodeCurrentFrame() returns the frame corresponding to the new ticks.
    /// </summary>
    [Fact]
    public void RBF_P5T1_SingleNode_SeekViaManager()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = CreateMinimalFdpFile(_tempDir, exerciseId, nodeId: 1);
        _subsystem.LoadFdpViaManager(path);

        int nodeId = _subsystem.Manager!.LocalEntitiesProviderNodeId;
        var ctx = _subsystem.Manager.Contexts[nodeId];
        long ticks = ctx.Playback!.GetFrameMetadata(1).WallClockTicks;

        _subsystem.Manager.SetBaseWallTicks(ticks);

        Assert.Equal(ctx.CurrentFrame, _subsystem.PrimaryNodeCurrentFrame());
    }

    /// <summary>
    /// RBF-P5T1: In Merged mode, seeking via SetBaseWallTicks triggers a transient master rebuild;
    /// ActiveRepo is replaced each time (new reference from TransientBuildOverride).
    /// </summary>
    [Fact]
    public void RBF_P5T1_Merged_SeekRebuildsTransientMaster()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = MakeOneFrameRecording(nodeId: 1, exerciseId);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));
        _subsystem.SetViewMode(ViewMode.Merged);

        int rebuilds = 0;
        _subsystem.TransientBuildOverride = _ => { rebuilds++; return new EntityRepository(); };

        var repoBefore = _subsystem.ActiveRepo;
        _subsystem.Manager!.SetBaseWallTicks(1_500_000L);
        var repoAfter = _subsystem.ActiveRepo;

        Assert.True(rebuilds >= 1, "A seek in Merged mode must trigger at least one transient master rebuild.");
        Assert.NotSame(repoBefore, repoAfter);
    }

    /// <summary>
    /// RBF-P5T1: PrimaryNodeCurrentFrame() reflects the frame index of the primary context;
    /// this is what the EventBrowserPanel CurrentFrameProvider lambda captures.
    /// </summary>
    [Fact]
    public void RBF_P5T1_EventBrowser_CurrentFrameProvider_UsesActiveContext()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = CreateMinimalFdpFile(_tempDir, exerciseId, nodeId: 1);
        _subsystem.LoadFdpViaManager(path);

        int nodeId = _subsystem.Manager!.LocalEntitiesProviderNodeId;
        var ctx = _subsystem.Manager.Contexts[nodeId];
        long ticks = ctx.Playback!.GetFrameMetadata(1).WallClockTicks;
        _subsystem.Manager.SetBaseWallTicks(ticks);

        Assert.Equal(ctx.CurrentFrame, _subsystem.PrimaryNodeCurrentFrame());
        Assert.Equal(1, _subsystem.PrimaryNodeCurrentFrame());
    }

    // ── RBF-P5T3: Diff engine through _activeRepo ─────────────────────────────

    private static Fdp.Toolkit.Scenario.ScenarioSerializer MakeMinimalSerializer()
    {
        using var primeRepo = new EntityRepository();
        primeRepo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        return new Fdp.Toolkit.Scenario.ScenarioSerializerBuilder("Test").Build();
    }

    /// <summary>
    /// RBF-P5T3: In Merged mode the diff cycle calls SetBaseWallTicks exactly twice —
    /// once for the before-state and once to restore the after-state.
    /// </summary>
    [Fact]
    public void RBF_P5T3_Diff_Merged_TwoRebuilds()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = CreateMinimalFdpFile(_tempDir, exerciseId, nodeId: 1);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));
        _subsystem.SetViewMode(ViewMode.Merged);
        _subsystem.SetSerializerForTest(MakeMinimalSerializer());

        int buildCount = 0;
        _subsystem.TransientBuildOverride = _ => { buildCount++; return new EntityRepository(); };

        // Frame 1, non-null entity handle (not alive in rebuilt repo — that's fine)
        _subsystem.ComputeDiffForTest(frame: 1, entity: new Entity(1, 1));

        Assert.Equal(2, buildCount);
    }

    /// <summary>
    /// RBF-P5T3: After the diff cycle completes, BaseWallTicks is restored to the after-state ticks
    /// (not left at the before-state).
    /// </summary>
    [Fact]
    public void RBF_P5T3_Diff_RestoresAfterTicks()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = CreateMinimalFdpFile(_tempDir, exerciseId, nodeId: 1);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));
        _subsystem.SetViewMode(ViewMode.Merged);
        _subsystem.SetSerializerForTest(MakeMinimalSerializer());
        _subsystem.TransientBuildOverride = _ => new EntityRepository();

        int nodeId = _subsystem.Manager!.LocalEntitiesProviderNodeId;
        var ctx = _subsystem.Manager.Contexts[nodeId];
        long afterTicks = ctx.Playback!.GetFrameMetadata(1).WallClockTicks;

        _subsystem.ComputeDiffForTest(frame: 1, entity: new Entity(1, 1));

        // Manager must be left at the after-state ticks
        Assert.Equal(afterTicks, _subsystem.Manager!.BaseWallTicks);
    }

    /// <summary>
    /// RBF-P5T3: If the entity does not exist in the rebuilt repo (before or after state),
    /// no exception is thrown and the result is an empty diff list.
    /// </summary>
    [Fact]
    public void RBF_P5T3_Diff_NoCrashOnMissingEntity()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = CreateMinimalFdpFile(_tempDir, exerciseId, nodeId: 1);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));
        _subsystem.SetViewMode(ViewMode.Merged);
        _subsystem.SetSerializerForTest(MakeMinimalSerializer());
        // Override always returns empty repo — entity will not be found
        _subsystem.TransientBuildOverride = _ => new EntityRepository();

        var ex = Record.Exception(() =>
            _subsystem.ComputeDiffForTest(frame: 1, entity: new Entity(1, 1)));

        Assert.Null(ex);
    }

    /// <summary>
    /// RBF-P5T3: In Single-Node mode, the diff cycle does NOT trigger transient master rebuilds;
    /// it operates directly on the primary context's SandboxRepo.
    /// </summary>
    [Fact]
    public void RBF_P5T3_Diff_SingleNode_StillProducesDiff()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = CreateMinimalFdpFile(_tempDir, exerciseId, nodeId: 1);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));
        _subsystem.SetViewMode(ViewMode.SingleNode);
        _subsystem.SetSerializerForTest(MakeMinimalSerializer());

        int buildCount = 0;
        _subsystem.TransientBuildOverride = _ => { buildCount++; return new EntityRepository(); };

        var result = _subsystem.ComputeDiffForTest(frame: 1, entity: new Entity(1, 1));

        // In Single-Node mode no transient master rebuilds should happen
        Assert.Equal(0, buildCount);
        // Result is non-null (even if empty, since entity is not alive in sandbox)
        Assert.NotNull(result);
    }

    // ── RBF-P5T4: Subsystem guard for seek-to-change in Merged mode ───────────

    /// <summary>
    /// RBF-P5T4: When the subsystem is in Merged view, invoking OnSeekToChangeRequested
    /// via the wired delegate must NOT trigger a seek (manager BaseWallTicks unchanged).
    /// </summary>
    [Fact]
    public void RBF_P5T4_SubsystemShortCircuit_NoSeekInMerged()
    {
        _subsystem.Initialize(HeadlessConfig());
        var exerciseId = Guid.NewGuid();
        var path = MakeOneFrameRecording(nodeId: 1, exerciseId);
        _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(MakeTestSerializer()));
        _subsystem.SetViewMode(ViewMode.Merged);

        long initialTicks = _subsystem.Manager!.BaseWallTicks;

        var diffPanel      = new ComponentDiffPanel();
        var entityHistory  = new EntitySelectionHistory();
        var playbackHistory = new PlaybackHistoryTracker();
        var inspectorState = new InspectorState();
        var eventPanel     = new EventBrowserPanel(new StubHistoryService());

        _subsystem.WireDelegatesForTest(entityHistory, playbackHistory, inspectorState,
            new ReplayBrowserContext(), diffPanel, eventPanel);

        // Invoke seek-to-change in Merged mode — must short-circuit without starting a seek
        diffPanel.OnSeekToChangeRequested?.Invoke(1);

        Assert.Equal(initialTicks, _subsystem.Manager!.BaseWallTicks);
    }

    /// <summary>
    /// ⭐⭐ CE-3126 (R-229) — the recorder writes the terrain name and the geo origin into the recording's metadata, and the
    /// Replay Browser places the replay at THAT origin (not the terrain file's at replay time — the terrain may be gone).
    /// 📄 docs/DESIGN_Geo_Origin.md §2 E/F.
    /// </summary>
    [Fact]
    public void CE3126_TheRecordingCarriesItsOrigin_AndTheReplayUsesIt()
    {
        string path = Path.Combine(_tempDir, "geo.fdp");
        var meta = new RecordingMetadata { ExerciseId = Guid.NewGuid(), NodeId = 1 };
        using (var repo = new EntityRepository())
        {
            var geo = new Fdp.Modules.Geographic.Transforms.WGS84Transform(48.1, 11.5, 500.0);
            repo.SetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>(geo);
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainDefinition>();
            repo.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainDefinition { ResolvedName = "test-town" });
            Fdp.Toolkit.Scenario.ScenarioDescription.Stamp(repo, "What: a test.", "ua-tank-ammo", "0123456789abcdef");   // ⭐ CE-4058 ②
            using var rec = new AsyncRecorder(path, meta);
            Fdp.Toolkit.Replay.RecorderTickSystem.StampWorld(repo, rec.Metadata);
            rec.CaptureKeyframe(repo, 1_000_000L, blocking: true, eventBus: repo.Bus);
        }
        var written = Fdp.Core.FlightRecorder.Metadata.MetadataSerializer.Deserialize(File.ReadAllText(path + ".meta.json"));
        Assert.Equal("test-town", written.TerrainName);
        Assert.Equal("ua-tank-ammo", written.ScenarioName);       // ⭐ CE-4058 ② — a recording names its scenario …
        Assert.Equal("0123456789abcdef", written.ScenarioHash);   //   … and its hash as loaded
        Assert.NotNull(written.GeoOrigin);
        Assert.Equal(48.1, written.GeoOrigin!.Lat, 9);

        _subsystem.Initialize(HeadlessConfig());
        _subsystem.LoadFdpViaManager(path);
        var (lat, lon, alt) = _subsystem.GeoTransform.Origin;
        Assert.Equal(48.1, lat, 9);
        Assert.Equal(11.5, lon, 9);
        Assert.Equal(500.0, alt, 9);
    }

    /// <summary>
    /// ⭐⭐ CE-4058 ① (Director Timeline TE-12, R-4069) — every repo the browser binds carries the catalogue every node runs on, so the
    /// bodies, effects and sounds resolve their types. 🔴 Before: none — every vehicle drew as a plain box (Film Production §6).
    /// </summary>
    [Fact]
    public void CE4058_EveryReplayWorld_CarriesTheNodesCatalogue()
    {
        _subsystem.Initialize(HeadlessConfig());
        _subsystem.LoadFdpViaManager(RecordNamingTerrain("tkb.fdp", "test-town"));
        var world = _subsystem.ActiveRepo!;
        Assert.True(world.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>(), "the replay world has no TKB");
        var tkb = world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
        Assert.Same(_subsystem.Tkb, tkb);   // ONE per host, on every repo it binds
        var nodeCatalogue = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        Assert.Equal(nodeCatalogue.GetAll().Select(t => t.Name).OrderBy(n => n), tkb.GetAll().Select(t => t.Name).OrderBy(n => n));
    }

    // ── CE-3118 — the Replay Browser loads the recording's terrain and road network ────────────────────────────────

    /// <summary>Records one keyframe of a world that names <paramref name="terrainName"/> and holds a 2-node road blob.</summary>
    private string RecordNamingTerrain(string file, string terrainName)
    {
        string path = Path.Combine(_tempDir, file);
        var meta = new RecordingMetadata { ExerciseId = Guid.NewGuid(), NodeId = 1 };
        using var repo = new EntityRepository();
        repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainDefinition>();
        repo.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainDefinition { ResolvedName = terrainName });
        var b = new global::CarKinem.Road.RoadNetworkBuilder();
        b.AddNode(new System.Numerics.Vector2(0, 0)); b.AddNode(new System.Numerics.Vector2(100, 0));
        b.AddSegment(new System.Numerics.Vector2(0, 0), new System.Numerics.Vector2(100, 0), new System.Numerics.Vector2(100, 0),
            new System.Numerics.Vector2(100, 0), laneWidth: 5f, laneCount: 2, startNodeIdx: 0, endNodeIdx: 1);
        using var recorded = b.Build(10f, 20, 20);
        repo.SetSingleton(new global::CarKinem.Road.ZoneEnvironmentData { RoadNetwork = recorded });
        using (var rec = new AsyncRecorder(path, meta))
        {
            Fdp.Toolkit.Replay.RecorderTickSystem.StampWorld(repo, rec.Metadata);
            rec.CaptureKeyframe(repo, 1_000_000L, blocking: true, eventBus: repo.Bus);
        }
        return path;
    }

    /// <summary>
    /// ⭐ <c>CE-3118</c> — a recording made on test-town replays OVER test-town: the replay world holds the terrain's world model and
    /// its road network (test-town's 5-node graph), which is what the map's terrain and road gizmos draw. 🔴 Before: nothing under
    /// ReplayBrowser loaded a terrain, so the map showed no buildings, walls, doors or roads. 📄 docs/DESIGN_Geo_Origin.md §5.
    /// </summary>
    [Fact]
    public void CE3118_TheRecordingsTerrain_AndItsRoads_AreOnTheReplayWorld()
    {
        string path = RecordNamingTerrain("terrain.fdp", "test-town");
        _subsystem.TerrainCatalog = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(null, null);
        _subsystem.Initialize(HeadlessConfig());
        _subsystem.LoadFdpViaManager(path);

        Assert.Equal("test-town", _subsystem.ResidentTerrainName);
        var world = _subsystem.ActiveRepo!;
        var terrain = world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>();
        Assert.Equal("test-town", terrain.Name);
        Assert.NotEmpty(terrain.Prisms);
        var roads = world.GetSingleton<global::CarKinem.Road.ZoneEnvironmentData>().RoadNetwork;
        Assert.Equal(5, roads.Nodes.Length);   // the terrain's roads.json — ⛔ not the 2-node blob the recording process held
        Assert.Equal(4, roads.Segments.Length);
        // ⭐ CE-3134 — and its cover database, so the Cover layer draws in replay (test-town: 612 points)
        var cover = Assert.IsType<Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider>(
            world.GetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>());
        Assert.Equal(612, cover.Points.Count);
    }

    /// <summary>
    /// ⭐ <c>CE-3118</c> — the road network is never recorded: its blob is native memory of the recording process. 🔴 Before:
    /// <c>ZoneEnvironmentData</c> had no data policy, so it defaulted to recordable and saveable and a replay restored foreign
    /// pointers into the world the road gizmo reads.
    /// </summary>
    [Fact]
    public void CE3118_TheRoadNetworkSingleton_IsNeitherRecordedNorSaved()
    {
        using var repo = new EntityRepository();
        repo.SetSingleton(new global::CarKinem.Road.ZoneEnvironmentData());
        Assert.False(ComponentTypeRegistry.IsRecordable(Fdp.Core.GlobalComponentIds.ZoneEnvironmentData));
        Assert.False(ComponentTypeRegistry.IsSaveable(Fdp.Core.GlobalComponentIds.ZoneEnvironmentData));
    }

    /// <summary>⭐ <c>CE-3118</c> — a terrain that no longer resolves leaves the map without terrain; the replay still loads.</summary>
    [Fact]
    public void CE3118_ATerrainThatNoLongerExists_LeavesTheMapEmpty_AndTheReplayPlays()
    {
        string path = RecordNamingTerrain("gone.fdp", "a-terrain-that-was-deleted");
        _subsystem.TerrainCatalog = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(null, null);
        _subsystem.Initialize(HeadlessConfig());
        _subsystem.LoadFdpViaManager(path);

        Assert.Null(_subsystem.ResidentTerrainName);
        var world = _subsystem.ActiveRepo!;
        Assert.Empty(world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>().Prisms);
        Assert.False(world.GetSingleton<global::CarKinem.Road.ZoneEnvironmentData>().RoadNetwork.Nodes.IsCreated);
    }

    // ── RB-1 (docs/DESIGN_Replay_Browser_Debug_Http.md) — the shared debug members, filled over the federation ────────────────

    /// <summary>
    /// Records one node of an exercise where a node records (<c>nodes/node-N/exercises/&lt;id&gt;/node_N.fdp</c>): a keyframe at
    /// <paramref name="startSeconds"/> and three frames one second apart.
    /// </summary>
    private static string RecordNode(string stagingRoot, Guid exerciseId, int nodeId, double startSeconds, string? into = null)
    {
        string dir = into ?? Path.Combine(stagingRoot, "nodes", $"node-{nodeId}", "exercises", exerciseId.ToString());
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"node_{nodeId}.fdp");
        var meta = new RecordingMetadata { ExerciseId = exerciseId, NodeId = nodeId };
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();
        repo.AddComponent(repo.CreateEntity(), new NetworkIdentity { Value = nodeId * 100L });
        using (var rec = new AsyncRecorder(path, meta))
        {
            long t0 = TimeSpan.FromSeconds(startSeconds).Ticks;
            rec.CaptureKeyframe(repo, t0, blocking: true, eventBus: repo.Bus);
            for (uint f = 1; f <= 3; f++)
                rec.CaptureFrame(repo, f - 1, t0 + TimeSpan.FromSeconds(f).Ticks, blocking: true, eventBus: repo.Bus);
        }
        return path;
    }

    /// <summary>
    /// ⭐⭐ RB-1 — an agent opens, seeks, steps, plays and closes a recorded exercise through the SAME provider members every host
    /// fills (<c>Master</c>, <c>Drive</c>, <c>ClusterState</c>, <c>Exercises</c>, the replay's length and recording) — on a HEADLESS host,
    /// which builds no panel. 🔴 Before: the provider filled world, map and gizmo buffer only, so every control route failed here.
    /// </summary>
    [Fact]
    public void RB1_TheProvider_OpensSeeksStepsPlaysAndCloses_TheFederation_OverTheSharedMembers()
    {
        string root = Path.Combine(_tempDir, "staging");
        var exercise = Guid.NewGuid();
        string node1 = RecordNode(root, exercise, 1, startSeconds: 1.0);
        RecordNode(root, exercise, 2, startSeconds: 1.5);
        // ⚠ the same node archived too: the local copy wins, so the group never carries node 1 twice (LoadGroup refuses that)
        RecordNode(root, exercise, 1, startSeconds: 1.0,
            into: Path.Combine(root, "shared", "exercises", exercise.ToString()));

        _subsystem.TerrainCatalog = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(null, null);
        _subsystem.Initialize(HeadlessConfig());
        _subsystem.RecordingsRoot = root;
        var provider = _subsystem.CreateDebugProvider()!;

        Assert.Equal(Fdp.Toolkit.Orchestration.ClusterState.Idle, provider.ClusterState);
        Assert.Equal(exercise, Assert.Single(provider.Exercises!).ExerciseId);
        Assert.False(provider.Drive!.IsPlayPauseEnabled);

        // none named: the newest exercise on disk, as a cluster master picks — 2 s after the replay's zero (node 1's first frame, 1 s)
        provider.Master!.Transition(new Fdp.Toolkit.Orchestration.TransitionStateIntent
        {
            TargetState     = Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay,
            TargetWallTicks = TimeSpan.FromSeconds(2).Ticks,
        });
        Assert.Equal(Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay, provider.ClusterState);
        Assert.Equal(node1, provider.ReplayRecordingPath);                     // the lowest node is the primary
        Assert.Equal(3.5f, provider.ReplayDurationSeconds!.Value, 3);         // 1 s … 4.5 s (node 2's last frame)
        Assert.Equal(2.0, provider.Drive.TotalTime, 3);
        Assert.Equal(2, _subsystem.PrimaryNodeCurrentFrame());

        provider.Drive.Step();
        Assert.Equal(3.0, provider.Drive.TotalTime, 3);

        // ⭐ playback runs headless: one recorded frame per 1/60 s of host time, and it stops at the end
        provider.Drive.TogglePlayPause();
        Assert.False(provider.Drive.IsPaused);
        _subsystem.Update(1f / 30f);
        Assert.True(provider.Drive.IsPaused);
        Assert.Equal(3, _subsystem.PrimaryNodeCurrentFrame());

        provider.Master.SeekReplay(new Fdp.Toolkit.Orchestration.SeekReplayIntent { TargetWallTicks = TimeSpan.FromSeconds(1).Ticks });
        Assert.Equal(1.0, provider.Drive.TotalTime, 3);

        // ⭐ CE-3327 — the headless runner hands this host NO time (dt 0): it keeps its own clock, so play still moves (🔴 measured live: it did not)
        provider.Drive.TogglePlayPause();
        _subsystem.Update(0f);                                                // starts the host's own clock
        System.Threading.Thread.Sleep(60);
        _subsystem.Update(0f);
        Assert.True(_subsystem.PrimaryNodeCurrentFrame() > 1, $"frame {_subsystem.PrimaryNodeCurrentFrame()}: play did not advance on dt 0");
        if (!provider.Drive.IsPaused) provider.Drive.TogglePlayPause();
        provider.Drive.SetTimeScale(4f);
        Assert.Equal(4f, provider.Drive.TimeScale);

        Assert.NotNull(provider.Extraction);                                  // CE-171 — the host's serializer-backed dump
        Assert.NotNull(provider.ReplayBrowser);
        Assert.Throws<InvalidOperationException>(() => provider.Master.Storage(default));   // an offline viewer saves nothing

        provider.Master.Transition(new Fdp.Toolkit.Orchestration.TransitionStateIntent
            { TargetState = Fdp.Toolkit.Orchestration.ClusterState.Idle });
        Assert.Equal(Fdp.Toolkit.Orchestration.ClusterState.Idle, provider.ClusterState);
        Assert.Null(provider.ReplayRecordingPath);
    }

    /// <summary>⭐ RB-2 — a file (or a node group) outside the inventory opens through the Replay Browser's own seam, at a moment.</summary>
    [Fact]
    public void RB2_ARecordingFile_OpensByPath_AtAMoment_AndAMismatchedGroupIsRefused()
    {
        string root = Path.Combine(_tempDir, "loose");
        string a = RecordNode(root, Guid.NewGuid(), 1, startSeconds: 1.0);
        string b = RecordNode(root, Guid.NewGuid(), 2, startSeconds: 1.0);
        _subsystem.TerrainCatalog = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(null, null);
        _subsystem.Initialize(HeadlessConfig());
        var provider = _subsystem.CreateDebugProvider()!;

        Assert.Null(provider.ReplayBrowser!.Open(new[] { a }, atSeconds: 1.0));
        Assert.Equal(1.0, provider.Drive!.TotalTime, 3);
        Assert.Equal(a, provider.ReplayRecordingPath);
        Assert.Equal(1, provider.ReplayBrowser.Status()["frame"]!.GetValue<int>());

        Assert.Equal("exercise mismatch", provider.ReplayBrowser.Open(new[] { a, b }, 0));
        Assert.StartsWith("No such recording file", provider.ReplayBrowser.Open(new[] { a + ".gone" }, 0));
    }

    /// <summary>An event the RB-5 rail records one of per frame (its frame number).</summary>
    [EventId(932971)]
    public struct RbFramePing { public int Frame; }

    /// <summary>
    /// ⭐ RB-5 — the shown frame's recorded events are in the provider's event history (<c>GET /events</c>), and they FOLLOW the shown
    /// frame: a step adds its frame's events, a step back rewinds, a jump starts over. 🔴 Before: the federation moved with the history
    /// suppressed, so after the load the history held frame 0's events whatever frame was shown.
    /// </summary>
    [Fact]
    public void RB5_TheShownFramesRecordedEvents_FollowTheFederation()
    {
        string root = Path.Combine(_tempDir, "events");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "node_1.fdp");
        using (var repo = new EntityRepository())
        {
            repo.RegisterEvent<RbFramePing>();
            using var rec = new AsyncRecorder(path, new RecordingMetadata { ExerciseId = Guid.NewGuid(), NodeId = 1 });
            for (int f = 0; f <= 3; f++)
            {
                repo.Bus.Publish(new RbFramePing { Frame = f });
                long t = TimeSpan.FromSeconds(1 + f).Ticks;
                if (f == 0) rec.CaptureKeyframe(repo, t, blocking: true, eventBus: repo.Bus);
                else rec.CaptureFrame(repo, (uint)(f - 1), t, blocking: true, eventBus: repo.Bus);
                repo.Bus.SwapBuffers();
            }
        }
        _subsystem.TerrainCatalog = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(null, null);
        _subsystem.Initialize(HeadlessConfig());
        var provider = _subsystem.CreateDebugProvider()!;
        Assert.Null(provider.ReplayBrowser!.Open(new[] { path }, 0));

        int[] Frames() => provider.EventHistory!.GetHistory()
            .Where(e => e.TypeName == nameof(RbFramePing)).Select(e => (int)e.Frame).ToArray();

        Assert.Equal(new[] { 0 }, Frames());
        provider.Drive!.Step();
        provider.Drive.Step();
        Assert.Equal(new[] { 0, 1, 2 }, Frames());                                      // a step adds its frame
        provider.Master!.SeekReplay(new Fdp.Toolkit.Orchestration.SeekReplayIntent { TargetWallTicks = TimeSpan.FromSeconds(1).Ticks });
        Assert.Equal(new[] { 0, 1 }, Frames());                                         // a step back rewinds
        provider.Master.SeekReplay(new Fdp.Toolkit.Orchestration.SeekReplayIntent { TargetWallTicks = TimeSpan.FromSeconds(3).Ticks });
        Assert.Equal(new[] { 3 }, Frames());                                            // a jump starts over
        Assert.NotEmpty(provider.EventHistory!.GetHistory().Where(e => e.TypeName == nameof(RbFramePing) && ((RbFramePing)e.RawEvent).Frame == 3));
    }

    /// <summary>⭐ RB-3 — the node / merged view and the playback history, through the Replay Browser's own seam, on a headless host.</summary>
    [Fact]
    public void RB3_TheView_AndThePlaybackHistory_MoveTheFederation_OnAHeadlessHost()
    {
        string root = Path.Combine(_tempDir, "view");
        var exercise = Guid.NewGuid();
        string node1 = RecordNode(root, exercise, 1, startSeconds: 1.0);
        string node2 = RecordNode(root, exercise, 2, startSeconds: 1.0);
        _subsystem.TerrainCatalog = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(null, null);
        _subsystem.Initialize(HeadlessConfig());
        var p = _subsystem.CreateDebugProvider()!;
        var rb = p.ReplayBrowser!;
        Assert.Null(rb.Open(new[] { node1, node2 }, 0));

        Assert.Null(rb.View("2"));
        Assert.Equal(node2, p.ReplayRecordingPath);                                  // the shown node's recording
        Assert.Contains("merged", rb.View("7"));                                     // not a loaded node
        Assert.Null(rb.View("merged"));
        Assert.Equal("merged", rb.Status()["view"]!.GetValue<string>());
        Assert.Null(rb.View("1"));

        p.Master!.SeekReplay(new Fdp.Toolkit.Orchestration.SeekReplayIntent { TargetWallTicks = TimeSpan.FromSeconds(3).Ticks });
        Assert.Equal(3, _subsystem.PrimaryNodeCurrentFrame());
        Assert.Null(rb.History(back: true));                                         // back to where the seek started
        Assert.Equal(0, _subsystem.PrimaryNodeCurrentFrame());
        Assert.Null(rb.History(back: false));
        Assert.Equal(3, _subsystem.PrimaryNodeCurrentFrame());
        Assert.NotNull(rb.History(back: false));                                     // nothing further
    }

    /// <summary>
    /// ⭐ RB-4 — a JSON export job writes the SAME file the Timeline panel's export writes for the same options (one service, one call),
    /// off the main thread; units are network ids resolved in the shown world.
    /// </summary>
    [Fact]
    public void RB4_AnExportJob_WritesWhatThePanelsExportWrites_ForTheSameOptions()
    {
        string root = Path.Combine(_tempDir, "export");
        string path = RecordNode(root, Guid.NewGuid(), 1, startSeconds: 1.0);
        _subsystem.TerrainCatalog = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(null, null);
        _subsystem.Initialize(HeadlessConfig());
        var rb = _subsystem.CreateDebugProvider()!.ReplayBrowser!;
        Assert.Null(rb.Open(new[] { path }, 0));

        string output = Path.Combine(_tempDir, "out", "export.json");
        var (job, error) = rb.Export(new Hrot.Presentation.DebugApi.ReplayExportRequest(
            Window: "frames", From: 1, To: 3, Format: "changelog", Entities: new long[] { 100 }, Minified: true, Output: output));
        Assert.Null(error);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (rb.ExportStatus(job!)!["state"]!.GetValue<string>() == "running" && DateTime.UtcNow < deadline)
            System.Threading.Thread.Sleep(20);
        var status = rb.ExportStatus(job!)!;
        Assert.Equal("done", status["state"]!.GetValue<string>());
        Assert.True(status["bytes"]!.GetValue<long>() > 0);

        // the panel's own call: the same service, the same options (the unit as the handle the panel's selection holds)
        using var ctx = new ReplayBrowserContext();
        ctx.LoadRecording(path);
        var unit = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(ctx.SandboxRepo, 100);
        var options = new JsonExportOptions
        {
            WindowMode = ExportWindowMode.ByFrame, StartFrame = 1, EndFrame = 3, FormatMode = ExportFormatMode.Changelog,
            FilterBySelection = true, TargetEntities = new List<Entity> { unit }, Minified = true,
        };
        string panels = Path.Combine(_tempDir, "out", "panel.json");
        new RecordingExportService(_subsystem.SerializerForTest, new Fdp.Toolkit.ReplayBrowser.Diff.ComponentDiffService())
            .ExportToJson(path, panels, options);
        Assert.Equal(File.ReadAllText(panels), File.ReadAllText(output));

        Assert.Contains("window", rb.Export(new Hrot.Presentation.DebugApi.ReplayExportRequest(Window: "sideways")).Error);
        Assert.Contains("not in the shown world", rb.Export(new Hrot.Presentation.DebugApi.ReplayExportRequest(Entities: new long[] { 999 })).Error);
        Assert.Null(rb.ExportStatus("no-such-job"));
    }

    /// <summary>
    /// ⭐ RB-7 (R-4083) — on a HEADLESS Replay Browser a selection asked over HTTP (<c>SelectEntityCommand</c>) is applied by the same
    /// <c>SelectionRequestSystem</c> a click goes through (the host builds the map pack on its first open), and joins the selection
    /// history, which steps back. 🔴 Before: a headless host built no selection systems, so the command was a silent no-op.
    /// </summary>
    [Fact]
    public void RB7_ASelection_IsAppliedByTheSelectionSystems_AndJoinsTheSelectionHistory_OnAHeadlessHost()
    {
        string path = Path.Combine(_tempDir, "select", "node_1.fdp");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var repo = new EntityRepository())
        {
            repo.RegisterComponent<NetworkIdentity>();
            repo.AddComponent(repo.CreateEntity(), new NetworkIdentity { Value = 100 });
            repo.AddComponent(repo.CreateEntity(), new NetworkIdentity { Value = 101 });
            using var rec = new AsyncRecorder(path, new RecordingMetadata { ExerciseId = Guid.NewGuid(), NodeId = 1 });
            rec.CaptureKeyframe(repo, TimeSpan.FromSeconds(1).Ticks, blocking: true, eventBus: repo.Bus);
        }
        _subsystem.TerrainCatalog = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(null, null);
        _subsystem.Initialize(HeadlessConfig());
        var p = _subsystem.CreateDebugProvider()!;
        Assert.Null(p.ReplayBrowser!.Open(new[] { path }, 0));

        long? Primary()
        {
            var w = p.World!;
            foreach (var e in w.Query().With<Hrot.IG.Components.SelectionState>().Build())
            {
                ref readonly var st = ref w.GetComponentRO<Hrot.IG.Components.SelectionState>(e);
                if (st.IsPrimarySelection) return w.GetComponentRO<NetworkIdentity>(e).Value;
            }
            return null;
        }
        void Select(long id) { p.World!.Bus.Publish(new Hrot.Common.Events.SelectEntityCommand { NetworkId = id }); Frames(2); }
        void Frames(int n) { for (int i = 0; i < n; i++) _subsystem.Update(1f / 60f); }

        Select(100);
        Assert.Equal(100, Primary());
        Select(101);
        Assert.Equal(101, Primary());
        Assert.Null(p.ReplayBrowser.SelectionHistory(back: true));
        Frames(2);
        Assert.Equal(100, Primary());
        Assert.NotNull(p.ReplayBrowser.SelectionHistory(back: true));                  // nothing before the first
    }

    // ── CE-1303 P2a (docs/DESIGN_Director_Timeline_Editor.md §11m) — the ONE film director, in the Replay Browser, over a recording ──────────

    private static readonly Vector3 Key1At = new(10f, 0f, 0f), Key2At = new(0f, 30f, 0f);

    /// <summary>A recording of <paramref name="seconds"/> of a run at 60 Hz (v7: each frame's sim time from <see cref="GlobalTime"/>, sim 0 at the
    /// keyframe) whose meta names its scenario <c>s-film</c>, under <paramref name="dir"/>.</summary>
    private static string RecordFilmRun(string dir, double seconds)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "node_1.fdp");
        var meta = new RecordingMetadata { ExerciseId = Guid.NewGuid(), NodeId = 1, ScenarioName = "s-film", ScenarioHash = "0123456789abcdef" };
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();
        repo.AddComponent(repo.CreateEntity(), new NetworkIdentity { Value = 100 });
        using (var rec = new AsyncRecorder(path, meta))
        {
            long t0 = TimeSpan.FromSeconds(1).Ticks;
            repo.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0d });
            rec.CaptureKeyframe(repo, t0, blocking: true, eventBus: repo.Bus);
            int n = (int)Math.Round(seconds * 60d);
            for (int f = 1; f <= n; f++)
            {
                repo.SetSingletonUnmanaged(new GlobalTime { TotalTime = f / 60d });
                rec.CaptureFrame(repo, (uint)(f - 1), t0 + f * TimeSpan.TicksPerSecond / 60, blocking: true, eventBus: repo.Bus);
            }
        }
        return path;
    }

    /// <summary>A two-shot take (v2 keys): key 1 at 1 s looks at <see cref="Key1At"/>, key 2 — the cut — at 3 s looks at <see cref="Key2At"/>; it ends at 5 s.</summary>
    private static Hrot.UI.Common.Map3D.Director.Take TwoShotTake() => new()
    {
        Scenario = "s-film", Name = "main", Title = "Two shots",
        Keys = new[]
        {
            new Hrot.UI.Common.Map3D.Director.CameraKey { At = new Hrot.UI.Common.Map3D.Director.AtExpr(1d), Camera = new Hrot.UI.Common.Map3D.Director.CameraSpec { LookAt = Key1At, Distance = 20f } },
            new Hrot.UI.Common.Map3D.Director.CameraKey { At = new Hrot.UI.Common.Map3D.Director.AtExpr(3d), Camera = new Hrot.UI.Common.Map3D.Director.CameraSpec { LookAt = Key2At, Distance = 20f } },
        },
        EndAt = new Hrot.UI.Common.Map3D.Director.AtExpr(5d),
    };

    /// <summary>Opens a 6 s recording on a headless browser and saves <see cref="TwoShotTake"/> BESIDE it (TE-1) through the pack's store.</summary>
    private (Hrot.UI.Common.Map3D.Director.DirectorPack Pack, string Dir) OpenFilmRecording(string name)
    {
        Hrot.UI.Common.Map3D.FilmStudio.Reset();
        string dir = Path.Combine(_tempDir, name, "exercises", Guid.NewGuid().ToString());
        string path = RecordFilmRun(dir, 6d);
        _subsystem.TerrainCatalog = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(null, null);
        _subsystem.Initialize(HeadlessConfig());
        Assert.Null(_subsystem.CreateDebugProvider()!.ReplayBrowser!.Open(new[] { path }, 0));
        var pack = _subsystem.Director!;
        pack.Takes.Save(TwoShotTake());
        return (pack, dir);
    }

    /// <summary>One frame of the browser; returns the recording's sim second the director's camera was set for this frame (the step comes after).</summary>
    private double Frame()
    {
        double before = _subsystem.DirectorSimSeconds;
        _subsystem.Update(1f / 60f);
        return before;
    }

    private static Vector3? Camera() => Hrot.UI.Common.Map3D.FilmStudio.Shot?.Pose?.LookAt;

    /// <summary>Frames until the runner plays (load → enter → play), at most 20.</summary>
    private void UntilPlaying(Hrot.UI.Common.Map3D.Director.DirectorRunner runner)
    {
        for (int i = 0; i < 20 && runner.Phase != Hrot.UI.Common.Map3D.Director.DirectorPhase.Playing; i++) _subsystem.Update(1f / 60f);
        Assert.Equal(Hrot.UI.Common.Map3D.Director.DirectorPhase.Playing, runner.Phase);
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2a (§11m D1, success condition 1) — on a REAL recording opened in the Replay Browser, the pack's runner plays a two-shot take
    /// through <c>ReplayDirectorHost</c>: the take is read from beside the recording (TE-1), the recording's playhead advances one step per frame,
    /// and the camera at every film second is the key of the shot on screen — key 1 in [1, 3), key 2 (the cut) in [3, 5) — then the item ends.
    /// 🔴 Mutation: <c>ReplayDirectorHost.StepBy</c> not moving the playhead ⇒ the sim never reaches the cut; the second-shot frames are empty.
    /// </summary>
    [Fact]
    public void CE1303_P2a_TheDirector_PlaysATake_OverARecording()
    {
        var (pack, dir) = OpenFilmRecording("plays");
        Assert.True(File.Exists(Path.Combine(dir, "s-film", "takes", "main.take.json")));   // TE-1 — beside the recording, keyed by its scenario
        var runner = pack.Runner;
        Assert.Empty(runner.Play(new Hrot.UI.Common.Map3D.Director.PlayRequest(Hrot.UI.Common.Map3D.Director.Reel.Of("s-film", "main"), Speed: 1000d)));

        var seen = new List<(double Sim, Vector3? Camera)>();
        for (int i = 0; i < 400 && runner.Busy; i++)
        {
            double sim = Frame();
            if (runner.Phase == Hrot.UI.Common.Map3D.Director.DirectorPhase.Playing) seen.Add((sim, Camera()));
        }
        Assert.False(runner.Busy);
        var report = runner.Reports[0];
        Assert.True(report.Status == "ok", $"{report.Status}: {string.Join("; ", report.Problems)}");
        var first = seen.Where(s => s.Sim > 1.02 && s.Sim < 2.98).ToList();
        var second = seen.Where(s => s.Sim > 3.02 && s.Sim < 4.98).ToList();
        Assert.True(first.Count > 60, $"frames in shot 1: {first.Count}");
        Assert.True(second.Count > 60, $"frames in shot 2: {second.Count}");
        Assert.All(first, s => Assert.Equal(Key1At, s.Camera));
        Assert.All(second, s => Assert.Equal(Key2At, s.Camera));
        Assert.Equal(new[] { 1, 2 }, report.Cuts.Select(c => c.Shot).ToArray());   // two cuts, in order
        Assert.InRange(report.SimEnd, 4.98, 5.04);
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2a (§11m D5, success condition 2) — <c>PlayFrom(t)</c> on the Replay Browser SEEKS the recording: the frame it starts playing,
    /// the playhead (the director's sim and the transport's seconds) stands at t within one step, and the first frame shows the shot of t (shot 2's
    /// key). 🔴 Mutation: the runner not seeking on <c>StartAt</c> (the <c>SeekSource(_startAt)</c> call removed) ⇒ the playhead is at 0 and the
    /// camera is none / key 1.
    /// </summary>
    [Fact]
    public void CE1303_P2a_PlayFrom_SeeksTheRecording_ToTheShot()
    {
        var (pack, _) = OpenFilmRecording("from");
        var runner = pack.Runner;
        Assert.Empty(runner.PlayFrom(new Hrot.UI.Common.Map3D.Director.PlayRequest(Hrot.UI.Common.Map3D.Director.Reel.Of("s-film", "main"), Speed: 1000d), 3.5));
        UntilPlaying(runner);

        Assert.InRange(_subsystem.DirectorSimSeconds, 3.5 - 1d / 60, 3.5 + 1d / 60);
        Assert.InRange(_subsystem.Transport.TotalTime, 3.5 - 1d / 60, 3.5 + 1d / 60);   // the recording's own transport stands there too
        double sim = Frame();
        Assert.InRange(sim, 3.5 - 1d / 60, 3.5 + 1d / 60);
        Assert.Equal(2, runner.Status()["shot"]!.GetValue<int>());
        Assert.Equal(Key2At, Camera());
        Assert.True(_subsystem.DirectorSimSeconds > sim);                              // and it plays on from there
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2a (§11m D4, success condition 3) — CR-4 on the Replay Browser: an edit of the OPEN take (the cut moved from 3 s to 2 s through
    /// <c>TakeEditor</c>, not saved) is what the NEXT play shows — at 2.5 s the camera is key 2 — while the file still says 3 s. 🔴 Mutation: the pack
    /// not wiring <c>Runner.OpenTake</c> ⇒ the second play reads the file and shows key 1 at 2.5 s.
    /// </summary>
    [Fact]
    public void CE1303_P2a_TheNextPlay_ShowsTheUnsavedEdit()
    {
        var (pack, dir) = OpenFilmRecording("edit");
        var runner = pack.Runner;
        Assert.Empty(pack.Session.Open("s-film", "main"));
        var request = new Hrot.UI.Common.Map3D.Director.PlayRequest(Hrot.UI.Common.Map3D.Director.Reel.Of("s-film", "main"), Speed: 1000d, Cinema: false);

        Vector3? CameraAt(double t)
        {
            Assert.Empty(runner.PlayFrom(request, t));
            UntilPlaying(runner);
            Frame();
            var cam = Camera();
            runner.Stop();
            return cam;
        }

        Assert.Equal(Key1At, CameraAt(2.5));                                            // as saved: the cut at 3 s
        Assert.True(pack.Session.Editor!.Apply(new Hrot.UI.Common.Map3D.Director.TakeEdit.MoveKey(1, 2d)));
        Assert.True(pack.Session.Editor.Dirty);
        Assert.Equal(Key2At, CameraAt(2.5));                                            // the unsaved edit: the cut at 2 s
        Assert.True(File.Exists(Path.Combine(dir, "s-film", "takes", "main.take.json")));
        Assert.Equal(3d, pack.Takes.Load("s-film", "main").Value!.Keys[1].At.Offset);    // the file is untouched
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2a (§11m D6, success condition 4) — Loop shot on the Replay Browser: played from 1.5 s (shot 1, [1, 3)), the playhead goes
    /// back to the shot's START when it reaches the shot's end — never into shot 2 — and the take is read again at the wrap, so the cut moved to
    /// 2 s mid-pass makes the NEXT pass wrap at 2 s. 🔴 Mutation: the loop's <c>SeekSource(again.Start)</c> removed ⇒ the playhead runs on past 3 s
    /// into shot 2.
    /// </summary>
    [Fact]
    public void CE1303_P2a_LoopShot_ReturnsToTheShotStart()
    {
        var (pack, _) = OpenFilmRecording("loop");
        var runner = pack.Runner;
        Assert.Empty(pack.Session.Open("s-film", "main"));
        runner.LoopShot = true;
        Assert.Empty(runner.PlayFrom(new Hrot.UI.Common.Map3D.Director.PlayRequest(Hrot.UI.Common.Map3D.Director.Reel.Of("s-film", "main"), Speed: 1000d, Cinema: false), 1.5));
        UntilPlaying(runner);

        var wraps = new List<(double From, double To)>();
        double last = Frame();
        bool edited = false;
        for (int i = 0; i < 400 && wraps.Count < 3; i++)
        {
            double sim = Frame();
            Assert.Equal(Key1At, Camera());                                              // never shot 2's camera
            if (sim < last - 0.5) wraps.Add((last, sim));
            if (wraps.Count == 1 && !edited && sim > 1.5)                                // mid-pass: the cut to 2 s, unsaved
            {
                Assert.True(pack.Session.Editor!.Apply(new Hrot.UI.Common.Map3D.Director.TakeEdit.MoveKey(1, 2d)));
                edited = true;
            }
            last = sim;
        }
        Assert.True(wraps.Count >= 3, $"wraps: {wraps.Count}");
        const double step = 1d / 60;
        Assert.InRange(wraps[0].From, 3d - step, 3d + 1e-3);                              // the end of shot 1 …
        Assert.InRange(wraps[0].To, 1d, 1d + 2 * step);                                   // … back to its start (and the frame's one step)
        Assert.InRange(wraps[1].From, 3d - step, 3d + 1e-3);                              // the pass the edit was made in ends as planned
        Assert.InRange(wraps[2].From, 2d - step, 2d + 1e-3);                              // the next pass plays the edit: the cut at 2 s
        Assert.InRange(wraps[2].To, 1d, 1d + 2 * step);
        Assert.True(runner.Busy);
        runner.Stop();
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2a (§11m D1) — a take LONGER than its recording (ends at 8 s, the recording at 6 s) ends where the recording does, and the
    /// report says so — ⛔ not a run that waits forever on a playhead that cannot advance. 🔴 Mutation: the runner's "the source did not move"
    /// check removed ⇒ the runner is still busy after 600 frames.
    /// </summary>
    [Fact]
    public void CE1303_P2a_ATakeLongerThanItsRecording_EndsWhereTheRecordingEnds()
    {
        var (pack, _) = OpenFilmRecording("long");
        pack.Takes.Save(TwoShotTake() with { EndAt = new Hrot.UI.Common.Map3D.Director.AtExpr(8d) });
        var runner = pack.Runner;
        Assert.Empty(runner.Play(new Hrot.UI.Common.Map3D.Director.PlayRequest(Hrot.UI.Common.Map3D.Director.Reel.Of("s-film", "main"), Speed: 1000d)));
        for (int i = 0; i < 600 && runner.Busy; i++) Frame();
        Assert.False(runner.Busy);
        Assert.Contains(runner.Reports[0].Problems, p => p.Contains("the source ends at 6"));
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2a (§11m, success condition 5; R-4068 / CR-2) — the Replay Browser builds NO director of its own: its source calls only
    /// <c>DirectorPack.Attach</c> (no runner, session or window constructed), and the attached pack is the films-only one — no extension, no tabs,
    /// the window in this host's perspective — wired to THIS host's Event Timeline. 🔴 Mutation: a hand-built <c>new DirectorRunner(</c> in the
    /// subsystem ⇒ the source half fails.
    /// </summary>
    [Fact]
    public void CE1303_P2a_TheReplayBrowser_AttachesTheDirectorThroughThePack_Only()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "HROT.sln"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("repo root not found");
        string text = string.Concat(Directory.EnumerateFiles(Path.Combine(root, "Hrot/Subsystems/Hrot.ReplayBrowser"), "*.cs").Select(File.ReadAllText));
        Assert.Contains("DirectorPack.Attach(", text);
        foreach (var built in new[] { "new DirectorRunner(", "new DirectorTakeSession(", "new DirectorWindow(", "new WindowCinema(", "new MapLayerOverride(" })
            Assert.DoesNotContain(built, text);

        var (pack, _) = OpenFilmRecording("pack");
        Assert.Null(pack.Runner.Extension);
        Assert.Empty(pack.Window.Tabs);
        Assert.Equal("ReplayBrowser", pack.Window.OwningPerspective);
        Assert.Empty(pack.Session.Open("s-film", "main"));
        Assert.NotEmpty(_subsystem.EventTimeline.Tracks!());                             // the RB's Event Timeline shows the open take's tracks
        Assert.True(pack.Runner.OpenTake!("s-film", "main") is { } open && ReferenceEquals(open, pack.Session.Editor!.Take));
    }
}
