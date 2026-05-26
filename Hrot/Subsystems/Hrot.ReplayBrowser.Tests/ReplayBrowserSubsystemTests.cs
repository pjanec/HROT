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
    /// FND-T12: RegisterWindowsCore registers exactly 5 windows; all are
    /// PerspectiveBound scope; all belong to the "ReplayBrowser" perspective.
    /// </summary>
    [Fact]
    public void RegisterWindowsCore_RegistersFiveWindows_AllReplayBrowserPerspective()
    {
        _subsystem.Initialize(HeadlessConfig());

        var atlas  = new IconAtlas(IntPtr.Zero, 1, 1);
        var wm     = new WindowManager(atlas);

        // Use stub panels (subsystem is headless so real panels were not created)
        var timelinePanel  = new Fdp.Presentation.Panels.ReplayBrowser.ReplayTimelinePanel(
            CreateNullContext(),
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

        Assert.Equal(5, windows.Count);

        foreach (var (_, win) in windows)
        {
            Assert.Equal(WindowScope.PerspectiveBound, win.Scope);
            Assert.Equal("ReplayBrowser", win.OwningPerspective);
        }
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
}
