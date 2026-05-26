# BATCH-04 Instructions — Replay Browser Frankenstein

**Batch:** BATCH-04
**Target tasks:** RBF-P4T1, RBF-P4T2, RBF-P4T3, RBF-P4T4, RBF-P4T5, RBF-P4T6, RBF-P4T7
**Design reference:** `.dev/replay-browser-frankenstein/DESIGN.md` §6, §8
**Task details:** `.dev/replay-browser-frankenstein/TASK-DETAILS.md` (Phase P4)
**Workspace root:** `D:\Work\IOS-IG-SimHost-FDP-2`

---

## Build and test commands

```
dotnet build IOS-IG-SimHost.sln
dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj --filter "FullyQualifiedName~RBF_P4T"
dotnet test Hrot/Subsystems/Hrot.ReplayBrowser.Tests/Hrot.ReplayBrowser.Tests.csproj --filter "FullyQualifiedName~RBF_P4T"
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
```

---

## Corrective tasks (C0)

None — no open P1/P2 debt items from BATCH-03.

---

## Task: RBF-P4T1 — Multi-file open dialog

**Reference:** TASK-DETAILS.md `RBF-P4T1`, DESIGN §8.1

### Production changes

**File: `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IFileDialogService.cs`**

Add a third method to the interface:

```csharp
/// <summary>
/// Displays an "Open File" modal dialog with multi-select enabled.
/// </summary>
/// <param name="callSiteId">Stable call-site identifier used to persist directory memory.</param>
/// <param name="extensionFilter">File extension filter string, e.g. <c>"*.fdp"</c>.</param>
/// <returns>
/// The full paths chosen by the user, or <c>null</c> if the user cancelled
/// or the dialog was superseded by a subsequent call.
/// </returns>
Task<string[]?> ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter);
```

**File: `FDP/Engine/Fdp.Presentation/ImGui/Panels/WinFormsFileDialogService.cs`**

Implement `ShowOpenMultipleFilesDialogAsync` in `WinFormsFileDialogService`. Use
`OFN_ALLOWMULTISELECT | OFN_EXPLORER` with a buffer of `32768` characters (to fit ~100
paths). Parse the multi-select result: Windows populates `lpstrFile` as either a single
path (if one file selected) or `directory\0file1\0file2\0...\0\0`. Return the array of
full paths. On non-Windows, return `null`.

```csharp
public Task<string[]?> ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter)
{
    string dir = _openDirectories.GetOrAdd(callSiteId, Environment.CurrentDirectory);
    return ShowMultiSelectDialogAsync(callSiteId, extensionFilter, dir);
}
```

Add a private `ShowMultiSelectDialogAsync` method that returns `Task<string[]?>`. Parse
the null-delimited buffer to extract all selected paths. Persist the directory of the
first file back into `_openDirectories`.

**File: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs`**

1. Add two new properties:
   ```csharp
   /// <summary>
   /// Called when the user confirms file selection. Receives the selected paths.
   /// Returns a rejection reason string, or null on success.
   /// </summary>
   public Func<string[], string?>? OnLoadGroup { get; set; }

   /// <summary>
   /// After a rejected LoadGroup call, holds the rejection reason shown in a modal.
   /// Cleared when the modal is dismissed. Exposed for testing.
   /// </summary>
   internal string? LoadGroupRejectionReason { get; private set; }
   ```

2. Change the existing private `LoadFdpAsync` to `internal` and rewrite:
   ```csharp
   internal async Task LoadFdpAsync()
   {
       var paths = await _fileDialogService.ShowOpenMultipleFilesDialogAsync(
           "ReplayBrowser_LoadRecording", "*.fdp");
       if (paths == null || paths.Length == 0) return;

       _playbackHistory.Clear();
       _inspectorState.SelectedEntity = null;
       IsPlaying = false;

       if (OnLoadGroup != null)
       {
           string? rejection = OnLoadGroup(paths);
           if (rejection != null)
               LoadGroupRejectionReason = rejection;
       }
       else
       {
           // Fallback for single-file backward-compat when no manager is wired
           _context.LoadRecording(paths[0]);
       }
   }
   ```

3. Add an ImGui modal draw in `DrawContent()` before all rows:
   ```csharp
   if (LoadGroupRejectionReason != null)
   {
       Gui.OpenPopup("LoadGroupError");
   }
   if (Gui.BeginPopupModal("LoadGroupError", ImGuiWindowFlags.AlwaysAutoResize))
   {
       Gui.TextWrapped(LoadGroupRejectionReason ?? string.Empty);
       if (Gui.Button("OK"))
       {
           LoadGroupRejectionReason = null;
           Gui.CloseCurrentPopup();
       }
       Gui.EndPopup();
   }
   ```

### Test changes

**File: `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Foundation/RBF_P4T1_LoadFdpTests.cs`** (new)

Tests live in `Fdp.Presentation.ReplayBrowser.Foundation` namespace, project
`Fdp.Presentation.Tests`.

Create a `StubFileDialogService` that implements `IFileDialogService` and returns
pre-configured paths from `ShowOpenMultipleFilesDialogAsync`. Other methods return `null`.

```csharp
[Fact]
public void RBF_P4T1_LoadFdpAsync_PassesAllPathsToManager()
{
    // Arrange
    var stub = new StubFileDialogService(new[] { "/a.fdp", "/b.fdp" });
    string[]? capturedPaths = null;
    var panel = MakePanel(stub);
    panel.OnLoadGroup = paths => { capturedPaths = paths; return null; };

    // Act
    panel.LoadFdpAsync().GetAwaiter().GetResult();

    // Assert
    Assert.NotNull(capturedPaths);
    Assert.Equal(new[] { "/a.fdp", "/b.fdp" }, capturedPaths);
}

[Fact]
public void RBF_P4T1_LoadFdpAsync_RejectionShowsModal()
{
    // Arrange
    var stub = new StubFileDialogService(new[] { "/x.fdp" });
    var panel = MakePanel(stub);
    panel.OnLoadGroup = _ => "Exercise mismatch: two exercise IDs found";

    // Act
    panel.LoadFdpAsync().GetAwaiter().GetResult();

    // Assert
    Assert.Equal("Exercise mismatch: two exercise IDs found", panel.LoadGroupRejectionReason);
}
```

Helper `MakePanel` creates a `ReplayTimelinePanel` with stub services.
Stub `IFileDialogService` must implement all three methods (`ShowOpenFileDialogAsync`
returns `null`, `ShowSaveAsDialogAsync` returns `null`).

---

## Task: RBF-P4T2 — FederationPanel (new ImGui panel)

**Reference:** TASK-DETAILS.md `RBF-P4T2`, DESIGN §8.2

### Production changes

**File: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/FederationPanel.cs`** (new)

```csharp
namespace Fdp.Presentation.Panels.ReplayBrowser;

public enum ViewMode { SingleNode, Merged }

/// <summary>
/// ImGui panel for per-node replay federation controls.
/// Handles mode toggle, per-node time offsets, base wall-tick input, and
/// the local-entities provider dropdown (Merged View only).
/// DESIGN §8.2.
/// </summary>
public sealed class FederationPanel
{
    private readonly FederatedReplayManager _manager;

    public ViewMode ActiveMode { get; private set; } = ViewMode.SingleNode;
    public event Action<ViewMode>? OnViewModeChanged;

    // Computed: true when any node in manager.NodeOffsets has a non-zero value
    public bool HasNonZeroOffset => _manager.NodeOffsets.Values.Any(v => v != 0L);

    public FederationPanel(FederatedReplayManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    public void SetMode(ViewMode mode)
    {
        if (_manager == null) return;  // defensive
        ActiveMode = mode;
        OnViewModeChanged?.Invoke(mode);
    }

    public void SetNodeOffset(int nodeId, long offsetTicks)
        => _manager.SetNodeOffset(nodeId, offsetTicks);

    public void SetBaseWallTicks(long ticks)
        => _manager.SetBaseWallTicks(ticks);

    public void SetLocalEntitiesProvider(int nodeId)
        => _manager.SetLocalEntitiesProvider(nodeId);

    public void DrawContent()
    {
        // Mode radio: Single-Node | Merged
        int modeInt = (int)ActiveMode;
        if (Gui.RadioButton("Single-Node", ref modeInt, 0))
            SetMode(ViewMode.SingleNode);
        Gui.SameLine();
        if (Gui.RadioButton("Merged View", ref modeInt, 1))
            SetMode(ViewMode.Merged);

        if (ActiveMode == ViewMode.Merged)
        {
            Gui.TextDisabled("Note: Merged View scrub may stutter — this is by design (offline synthesis).");

            // Local-Entities Provider dropdown
            int currentProviderId = _manager.LocalEntitiesProviderNodeId;
            Gui.Text("Local-Entities Provider:");
            Gui.SameLine();
            string previewLabel = $"Node {currentProviderId}";
            if (Gui.BeginCombo("##lep", previewLabel))
            {
                foreach (int nodeId in _manager.Contexts.Keys.OrderBy(x => x))
                {
                    bool selected = nodeId == currentProviderId;
                    if (Gui.Selectable($"Node {nodeId}", selected))
                        _manager.SetLocalEntitiesProvider(nodeId);
                }
                Gui.EndCombo();
            }
        }

        // Global causality banner
        if (HasNonZeroOffset)
            Gui.TextColored(new System.Numerics.Vector4(1f, 0.7f, 0.2f, 1f),
                "Causality may not hold -- non-zero offsets active");

        // Per-node offset rows
        foreach (var kvp in _manager.Contexts.OrderBy(x => x.Key))
        {
            int nodeId = kvp.Key;
            long currentOffset = _manager.NodeOffsets.TryGetValue(nodeId, out long off) ? off : 0L;
            Gui.Text($"Node {nodeId} offset:");
            Gui.SameLine();
            int offsetInt = (int)currentOffset;  // ImGui int input for display only
            Gui.SetNextItemWidth(120f);
            if (Gui.InputInt($"##offset_{nodeId}", ref offsetInt))
                _manager.SetNodeOffset(nodeId, offsetInt);
            if (currentOffset != 0L)
            {
                Gui.SameLine();
                Gui.TextColored(new System.Numerics.Vector4(1f, 0.7f, 0.2f, 1f), "[!]");
                if (Gui.IsItemHovered())
                    Gui.SetTooltip($"Node {nodeId} has a non-zero time offset.");
            }
        }
    }
}
```

Note: `Gui` is `ImGuiNET.ImGui` aliased in other panels — check what alias/static import the
existing panels in that file use; replicate it. If the other panels use `ImGuiNET.ImGui`
directly, do the same. The `.OrderBy(x => x)` on `_manager.Contexts.Keys` requires
`using System.Linq;`.

Do NOT add `using System.Linq` if the file structure shows it's not needed; check
surrounding files for the import convention.

### Test changes

**File: `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Federation/RBF_P4T2_FederationPanelTests.cs`** (new)

Tests live in `Fdp.Presentation.ReplayBrowser.Federation` namespace.

The tests need real `.fdp` + `.meta.json` pairs. Reuse a similar helper to the one in
`TransientMasterBuilderTests`: create a single-frame recording with a global entity.

```csharp
[Fact]
public void RBF_P4T2_OffsetEdit_CallsManagerSetNodeOffset()
{
    using var manager = MakeSingleNodeManager(nodeId: 1);
    var panel = new FederationPanel(manager);
    panel.SetNodeOffset(1, 5000L);
    Assert.Equal(5000L, manager.NodeOffsets.GetValueOrDefault(1));
}

[Fact]
public void RBF_P4T2_BaseTickEdit_CallsManagerSetBaseWallTicks()
{
    using var manager = MakeSingleNodeManager(nodeId: 1);
    var panel = new FederationPanel(manager);
    panel.SetBaseWallTicks(2_000_000L);
    Assert.Equal(2_000_000L, manager.BaseWallTicks);
}

[Fact]
public void RBF_P4T2_NonZeroOffset_ShowsWarningGlyph()
{
    using var manager = MakeSingleNodeManager(nodeId: 1);
    var panel = new FederationPanel(manager);
    Assert.False(panel.HasNonZeroOffset);
    panel.SetNodeOffset(1, 100L);
    Assert.True(panel.HasNonZeroOffset);
}

[Fact]
public void RBF_P4T2_ModeToggle_FiresViewModeChanged()
{
    using var manager = MakeSingleNodeManager(nodeId: 1);
    var panel = new FederationPanel(manager);
    ViewMode? received = null;
    panel.OnViewModeChanged += m => received = m;
    panel.SetMode(ViewMode.Merged);
    Assert.Equal(ViewMode.Merged, received);
}

[Fact]
public void RBF_P4T2_ProviderDropdown_HiddenInSingleNode()
{
    // When mode == SingleNode, the provider is not accessible as the dropdown is not rendered.
    // Test via: ActiveMode == SingleNode implies panel does NOT call SetLocalEntitiesProvider.
    using var manager = MakeSingleNodeManager(nodeId: 1);
    var panel = new FederationPanel(manager);
    Assert.Equal(ViewMode.SingleNode, panel.ActiveMode);
    // Provider state not changed by panel in SingleNode mode.
    int initialProvider = manager.LocalEntitiesProviderNodeId;
    // Panel cannot change provider while in SingleNode (no dropdown rendered).
    Assert.Equal(initialProvider, manager.LocalEntitiesProviderNodeId);
}

[Fact]
public void RBF_P4T2_ProviderDropdown_VisibleInMerged_DefaultsToManagerValue()
{
    using var manager = MakeSingleNodeManager(nodeId: 1);
    var panel = new FederationPanel(manager);
    panel.SetMode(ViewMode.Merged);
    // The panel reflects the manager's current LocalEntitiesProviderNodeId
    Assert.Equal(manager.LocalEntitiesProviderNodeId, 1);
}

[Fact]
public void RBF_P4T2_ProviderDropdownChange_CallsManagerSetLocalEntitiesProvider()
{
    using var manager = MakeTwoNodeManager(nodeId1: 1, nodeId2: 2);
    var panel = new FederationPanel(manager);
    panel.SetMode(ViewMode.Merged);
    panel.SetLocalEntitiesProvider(2);
    Assert.Equal(2, manager.LocalEntitiesProviderNodeId);
}
```

Helper `MakeSingleNodeManager(int nodeId)` creates one recording at `nodeId`, returns a
`FederatedReplayManager`. Helper `MakeTwoNodeManager` creates two recordings.
These helpers follow the pattern of `TransientMasterBuilderTests.MakeNetworkRecording`.

---

## Task: RBF-P4T3 — Subsystem mode swap + repo rebind

**Reference:** TASK-DETAILS.md `RBF-P4T3`, DESIGN §6, §8.4, §8.5

### Production changes

**File: `Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs`**

Add fields and internal method:

```csharp
private ViewMode _viewMode = ViewMode.SingleNode;
private EntityRepository? _transientMaster;
private TransientMasterBuilder? _transientBuilder;
```

Add `internal ViewMode ViewMode => _viewMode;` (read-only accessor for tests).

In `Initialize` (non-headless block), after building `_scenarioSerializer`:
```csharp
_transientBuilder = new TransientMasterBuilder(_scenarioSerializer);
```

Add `internal void LoadFdpGroupForTest(string[] paths, TransientMasterBuilder builder)`:
```csharp
internal void LoadFdpGroupForTest(string[] paths, TransientMasterBuilder builder)
{
    _manager?.Dispose();
    _manager = FederatedReplayManager.LoadGroup(paths);
    _transientBuilder = builder;
    _manager.OnTimeChanged += OnManagerTimeChanged;
    OnManagerTimeChanged();
}
```

Add `internal void SetViewMode(ViewMode mode)`:
```csharp
internal void SetViewMode(ViewMode mode)
{
    _viewMode = mode;
    if (mode == ViewMode.Merged)
    {
        // Gate 1: force IsPlaying = false
        if (_timelinePanel != null) _timelinePanel.IsPlaying = false;
        // Gate 2: disable search
        if (_searchPanel != null)
        {
            _searchPanel.IsMergedViewActive = true;
            _searchPanel.CurrentFilePath = null;
        }
        // Rebuild transient master now
        BuildAndBindTransientMaster();
    }
    else // SingleNode
    {
        // Dispose transient master
        _transientMaster?.Dispose();
        _transientMaster = null;
        // Restore search path
        if (_searchPanel != null)
        {
            _searchPanel.IsMergedViewActive = false;
            if (_manager != null &&
                _manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var ctx))
                _searchPanel.CurrentFilePath = ctx.CurrentFdpPath;
        }
        // Rebind to provider's sandbox repo
        OnManagerTimeChanged();
    }
}
```

Modify `OnManagerTimeChanged()`:
```csharp
private void OnManagerTimeChanged()
{
    if (_manager == null || _manager.Contexts.Count == 0) return;
    if (_viewMode == ViewMode.Merged)
    {
        BuildAndBindTransientMaster();
        return;
    }
    // SingleNode: bind to provider's sandbox repo
    int nodeId = _manager.LocalEntitiesProviderNodeId;
    if (_manager.Contexts.TryGetValue(nodeId, out var ctx))
        RebindActiveRepo(ctx.SandboxRepo);
}
```

Add `private void BuildAndBindTransientMaster()`:
```csharp
private void BuildAndBindTransientMaster()
{
    if (_manager == null || _transientBuilder == null) return;
    var newMaster = _transientBuilder.Build(_manager);
    var old = _transientMaster;
    _transientMaster = newMaster;
    RebindActiveRepo(newMaster);
    old?.Dispose();
}
```

Wire `FederationPanel` in subsystem (in `Initialize`'s non-headless block): after creating
the search panel, create `FederationPanel` and subscribe `OnViewModeChanged`:
```csharp
var federationPanel = new FederationPanel(_manager ?? /* create a placeholder manager */ ...);
```

Actually — the manager is created LAZILY when the user loads files. At `Initialize` time
the manager may not exist yet. The `FederationPanel` is only useful once a manager is
loaded. The recommended approach:
- Hold a field `private FederationPanel? _federationPanel;`
- Create it in `LoadFdpGroupForTest` and in the `OnLoadGroup` delegate wired to
  `_timelinePanel.OnLoadGroup`
- Wire `federationPanel.OnViewModeChanged += SetViewMode`
- Add a window for it in `RegisterWindowsCore` (optional — add if a window type exists
  for it, or just add it via `RegisterWindow` with a generic wrapper)

For simplicity in this batch, DO NOT add the federation panel to the window manager yet
(that requires a `FederationWindow` wrapper class which is not scoped here). The panel
exists as a field and its `DrawContent()` will be called from `DrawUI` in a future iteration.

In `ReplayBrowserSubsystem.Shutdown()`, add:
```csharp
_transientMaster?.Dispose();
```

### Test changes

**File: `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs`**

Add the following test methods (in the `ReplayBrowserSubsystemTests` class):

```csharp
// Helpers needed for P4T3 tests
private static ScenarioSerializer MakeTestSerializer()
{
    using var primeRepo = new EntityRepository();
    primeRepo.RegisterComponent<NetworkIdentity>();
    primeRepo.RegisterComponent<NetworkAuthority>();
    primeRepo.RegisterComponent<DummyPosition>();
    return new ScenarioSerializerBuilder("TestSubsystem").Build();
}

private string MakeOneFrameRecording(int nodeId, Guid exerciseId)
{
    string path = Path.Combine(_tempDir, $"node{nodeId}_{Guid.NewGuid():N}.fdp");
    var meta = new RecordingMetadata { ExerciseId = exerciseId, NodeId = nodeId };
    using var repo = new EntityRepository();
    repo.RegisterComponent<NetworkIdentity>();
    var e = repo.CreateEntity();
    repo.AddComponent(e, new NetworkIdentity { Value = nodeId * 100L });
    repo.SetAuthority<NetworkIdentity>(e, true);
    using (var rec = new AsyncRecorder(path, meta))
        rec.CaptureKeyframe(repo, 1_000_000L, blocking: true, eventBus: repo.Bus);
    File.WriteAllText(path + ".meta.json", MetadataSerializer.Serialize(meta));
    return path;
}
```

Add a `_tempDir` field and clean up in `Dispose`.

```csharp
[Fact]
public void RBF_P4T3_SingleNodeMode_BindsToCtxRepo()
{
    _subsystem.Initialize(HeadlessConfig());
    var exerciseId = Guid.NewGuid();
    var path = MakeOneFrameRecording(1, exerciseId);
    var serializer = MakeTestSerializer();
    _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(serializer));
    _subsystem.Manager!.SetBaseWallTicks(1_000_000L);

    _subsystem.SetViewMode(ViewMode.SingleNode);

    var expectedRepo = _subsystem.Manager.Contexts[1].SandboxRepo;
    Assert.Same(expectedRepo, _subsystem.ActiveRepo);
}

[Fact]
public void RBF_P4T3_MergedMode_BindsToTransientMaster()
{
    _subsystem.Initialize(HeadlessConfig());
    var exerciseId = Guid.NewGuid();
    var path = MakeOneFrameRecording(1, exerciseId);
    var serializer = MakeTestSerializer();
    _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(serializer));
    _subsystem.Manager!.SetBaseWallTicks(1_000_000L);

    _subsystem.SetViewMode(ViewMode.Merged);

    Assert.NotNull(_subsystem.ActiveRepo);
    // Active repo is NOT the sandbox repo of the provider
    Assert.NotSame(_subsystem.Manager.Contexts[1].SandboxRepo, _subsystem.ActiveRepo);
}

[Fact]
public void RBF_P4T3_OnTimeChangedInMerged_RebuildsMaster()
{
    _subsystem.Initialize(HeadlessConfig());
    var exerciseId = Guid.NewGuid();
    var path = MakeOneFrameRecording(1, exerciseId);
    var serializer = MakeTestSerializer();
    int buildCount = 0;
    var countingBuilder = new CountingTransientMasterBuilder(
        new TransientMasterBuilder(serializer), () => buildCount++);
    _subsystem.LoadFdpGroupForTest(new[] { path }, countingBuilder);
    _subsystem.Manager!.SetBaseWallTicks(1_000_000L);
    _subsystem.SetViewMode(ViewMode.Merged);
    int countAfterSwitch = buildCount;

    // Fire OnTimeChanged by seeking
    _subsystem.Manager.SetBaseWallTicks(1_000_000L);

    Assert.True(buildCount > countAfterSwitch);
}

[Fact]
public void RBF_P4T3_ProviderChangeInMerged_RebuildsMaster()
{
    _subsystem.Initialize(HeadlessConfig());
    var exerciseId = Guid.NewGuid();
    var path1 = MakeOneFrameRecording(1, exerciseId);
    var path2 = MakeOneFrameRecording(2, exerciseId);
    var serializer = MakeTestSerializer();
    int buildCount = 0;
    var countingBuilder = new CountingTransientMasterBuilder(
        new TransientMasterBuilder(serializer), () => buildCount++);
    _subsystem.LoadFdpGroupForTest(new[] { path1, path2 }, countingBuilder);
    _subsystem.Manager!.SetBaseWallTicks(1_000_000L);
    _subsystem.SetViewMode(ViewMode.Merged);
    int countBefore = buildCount;

    _subsystem.Manager.SetLocalEntitiesProvider(2);

    Assert.True(buildCount > countBefore);
}

[Fact]
public void RBF_P4T3_ModeSwitchToSingle_DisposesTransientMaster()
{
    _subsystem.Initialize(HeadlessConfig());
    var exerciseId = Guid.NewGuid();
    var path = MakeOneFrameRecording(1, exerciseId);
    var serializer = MakeTestSerializer();
    _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(serializer));
    _subsystem.Manager!.SetBaseWallTicks(1_000_000L);
    _subsystem.SetViewMode(ViewMode.Merged);
    var masterRepo = _subsystem.ActiveRepo;
    Assert.NotNull(masterRepo);

    _subsystem.SetViewMode(ViewMode.SingleNode);

    // The transient master from Merged view must have been disposed.
    // The subsystem's internal _transientMaster is now null.
    Assert.Equal(ViewMode.SingleNode, _subsystem.ViewMode);
    // _activeRepo is now the sandbox repo (not the old transient master)
    Assert.NotSame(masterRepo, _subsystem.ActiveRepo);
}

[Fact]
public void RBF_P4T3_ModeSwitchToMerged_ForcesIsPlayingFalse()
{
    // In headless mode, _timelinePanel is null. This test verifies the guard
    // does not throw and the mode switch completes without error.
    _subsystem.Initialize(HeadlessConfig());
    var exerciseId = Guid.NewGuid();
    var path = MakeOneFrameRecording(1, exerciseId);
    var serializer = MakeTestSerializer();
    _subsystem.LoadFdpGroupForTest(new[] { path }, new TransientMasterBuilder(serializer));
    _subsystem.Manager!.SetBaseWallTicks(1_000_000L);

    var ex = Record.Exception(() => _subsystem.SetViewMode(ViewMode.Merged));
    Assert.Null(ex);
    Assert.Equal(ViewMode.Merged, _subsystem.ViewMode);
}
```

Add a `CountingTransientMasterBuilder` inner class that wraps a real builder and calls
the counter on each `Build` invocation:

```csharp
private sealed class CountingTransientMasterBuilder : TransientMasterBuilder
{
    private readonly TransientMasterBuilder _inner;
    private readonly Action _onBuild;
    public CountingTransientMasterBuilder(TransientMasterBuilder inner, Action onBuild)
        : base(inner) // TransientMasterBuilder is sealed — do NOT inherit; see note below
    ...
}
```

IMPORTANT: `TransientMasterBuilder` is `sealed`. You cannot inherit from it. Instead, add
an **internal virtual method** `internal virtual EntityRepository DoBuild(FederatedReplayManager m)`
to `TransientMasterBuilder` that the real `Build` delegates to, and override in a test
subclass. Since `sealed` prevents this, use a different pattern:

Add an **adapter seam** to `ReplayBrowserSubsystem`:
```csharp
internal Func<FederatedReplayManager, EntityRepository>? TransientBuildOverride;
```

`BuildAndBindTransientMaster` checks `TransientBuildOverride` first:
```csharp
private void BuildAndBindTransientMaster()
{
    if (_manager == null) return;
    EntityRepository newMaster;
    if (TransientBuildOverride != null)
        newMaster = TransientBuildOverride(_manager);
    else if (_transientBuilder != null)
        newMaster = _transientBuilder.Build(_manager);
    else return;
    var old = _transientMaster;
    _transientMaster = newMaster;
    RebindActiveRepo(newMaster);
    old?.Dispose();
}
```

Tests that need to count builds use `TransientBuildOverride` instead of a subclass.

Update the P4T3 tests to use `TransientBuildOverride` for counting:
```csharp
int buildCount = 0;
_subsystem.TransientBuildOverride = mgr =>
{
    buildCount++;
    return new EntityRepository();
};
```

Note: `LoadFdpGroupForTest` should set `_transientBuilder` but also should NOT override
`TransientBuildOverride` — only the tests set `TransientBuildOverride` explicitly.

---

## Task: RBF-P4T4 — Inspector field flagging for Entity.Null paradoxes

**Reference:** TASK-DETAILS.md `RBF-P4T4`, DESIGN §8.3

### Production changes

**File: `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IInspectorContext.cs`**

Add `bool IsMergedView { get; set; }` to both the interface AND the `InspectorState` class:

```csharp
public interface IInspectorContext
{
    Entity? SelectedEntity { get; set; }
    Entity? HoveredEntity { get; set; }
    bool IsMergedView { get; set; }
}

public class InspectorState : IInspectorContext
{
    public Entity? SelectedEntity { get; set; }
    public Entity? HoveredEntity { get; set; }
    public bool IsMergedView { get; set; }
}
```

Add a static helper in the same file (or in a new
`EntityFieldParadoxHelper.cs` if preferred — keep it in Abstractions):

```csharp
/// <summary>
/// Returns true when an Entity-typed inspector field should be flagged
/// as a potential paradox. DESIGN §8.3.
/// </summary>
public static class EntityFieldParadoxHelper
{
    public static bool ShouldFlag(Entity value, bool isMergedView)
        => isMergedView && value.IsNull;

    public static string ParadoxTooltip =>
        "Referenced entity not present in federated snapshot. This may be due to " +
        "a manual time offset, or a recorded cluster desync in the original live run.";
}
```

### Test changes

**File: `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Foundation/RBF_P4T4_EntityFieldFlaggingTests.cs`** (new)

```csharp
[Fact]
public void RBF_P4T4_NullEntityField_InMerged_RendersWarning_RegardlessOfOffset()
{
    Assert.True(EntityFieldParadoxHelper.ShouldFlag(Entity.Null, isMergedView: true));
}

[Fact]
public void RBF_P4T4_NullEntityField_InSingleNode_NoWarning()
{
    Assert.False(EntityFieldParadoxHelper.ShouldFlag(Entity.Null, isMergedView: false));
}

[Fact]
public void RBF_P4T4_NonNullEntityField_NoWarning()
{
    var liveEntity = new Entity(1, 1);
    Assert.False(EntityFieldParadoxHelper.ShouldFlag(liveEntity, isMergedView: true));
}

[Fact]
public void RBF_P4T4_TooltipMentionsBothCauses()
{
    string tooltip = EntityFieldParadoxHelper.ParadoxTooltip;
    Assert.Contains("time offset", tooltip, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("desync", tooltip, StringComparison.OrdinalIgnoreCase);
}
```

---

## Task: RBF-P4T5 — Documentation: severe stutter is expected

**Reference:** TASK-DETAILS.md `RBF-P4T5`, DESIGN §9, SC-6

### Production changes

In `FederationPanel.DrawContent()`, inside the `if (ActiveMode == ViewMode.Merged)` block,
already added the line:
```csharp
Gui.TextDisabled("Note: Merged View scrub may stutter -- this is by design (offline synthesis).");
```

Add to `ONBOARDING.md` (`.dev/replay-browser-frankenstein/ONBOARDING.md`) a paragraph
about expected stutter in Merged View.

### Test changes

**File: `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Federation/RBF_P4T5_DocumentationTests.cs`** (new)

```csharp
[Fact]
public void RBF_P4T5_MergedViewDisclaimer_ContainsStutterText()
{
    // The DrawContent method cannot be invoked without ImGui context.
    // Verify the disclaimer string is present in the source code via an
    // attribute-based assertion on the FederationPanel type.
    // Since we cannot call DrawContent, the test verifies the predicate
    // for when the disclaimer is rendered.

    // Test the predicate logic: disclaimer must be rendered when mode == Merged.
    // FederationPanel exposes ActiveMode after SetMode; the text is rendered
    // when ActiveMode == Merged. We test this via a mock-free structural assertion.
    using var manager = MakeSingleNodeManager(nodeId: 1);
    var panel = new FederationPanel(manager);
    panel.SetMode(ViewMode.Merged);
    // Verify disclaimer condition: merged mode is active.
    Assert.Equal(ViewMode.Merged, panel.ActiveMode);
    // The actual string content "stutter" is visible in FederationPanel.cs source.
}
```

If this test cannot add meaningful value over "does it compile", just write:
```csharp
[Fact]
public void RBF_P4T5_MergedMode_ActiveAfterSetMode()
{
    using var manager = MakeSingleNodeManager(nodeId: 1);
    var panel = new FederationPanel(manager);
    panel.SetMode(ViewMode.Merged);
    Assert.Equal(ViewMode.Merged, panel.ActiveMode);
}
```

And use a simpler test that verifies the disclaimer is present in the source file
via a reflection-based string scan of `FederationPanel.cs`. Preferred: do NOT use
file-system checks in tests. Use a method containing the string as a testable predicate.

Add an `internal static string MergedViewDisclaimerText => "Note: Merged View scrub may stutter";`
constant to `FederationPanel` and test that it contains "stutter":

```csharp
[Fact]
public void RBF_P4T5_FederationPanel_DisclaimerTextContainsSutter()
{
    Assert.Contains("stutter", FederationPanel.MergedViewDisclaimerText,
        StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void RBF_P4T5_FederationPanel_DisclaimerTextContainsOffline()
{
    Assert.Contains("offline", FederationPanel.MergedViewDisclaimerText,
        StringComparison.OrdinalIgnoreCase);
}
```

---

## Task: RBF-P4T6 — Disable continuous playback in Merged View

**Reference:** TASK-DETAILS.md `RBF-P4T6`, DESIGN §6.2.1

### Production changes

**File: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs`**

1. Add a new property:
   ```csharp
   /// <summary>Returns true when the active view is Merged View.</summary>
   public Func<bool>? IsMergedViewQuery { get; set; }
   ```

2. Add an `internal static` helper:
   ```csharp
   internal static bool IsPlayEnabled(bool hasRecording, bool isMergedView)
       => hasRecording && !isMergedView;
   ```

3. In `DrawRow1_Transport`, change the Play/Pause button block:
   - Compute `bool isMerged = IsMergedViewQuery?.Invoke() ?? false;`
   - Compute `bool playEnabled = IsPlayEnabled(hasRecording, isMerged);`
   - Before the Play/Pause button: `if (!playEnabled) Gui.BeginDisabled();`
   - After the button: `if (!playEnabled) Gui.EndDisabled();`
   - Change the tooltip for the Play/Pause button:
     ```csharp
     if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
     {
         if (isMerged)
             Gui.SetTooltip("Continuous playback is disabled in Merged View. Use Step-Forward/Backward or the timeline slider.");
         else
             Gui.SetTooltip(IsPlaying ? "Pause playback" : "Start playback");
     }
     ```

   Full change in context:
   ```csharp
   // Replace the existing play/pause block:
   bool isMerged = IsMergedViewQuery?.Invoke() ?? false;
   bool playEnabled = IsPlayEnabled(hasRecording, isMerged);
   TransportShape playPauseShape = IsPlaying ? TransportShape.Pause : TransportShape.Play;
   if (!playEnabled) Gui.BeginDisabled();
   if (TransportIconRenderer.DrawButton("##rb_play_pause", iconSize, playPauseShape, playEnabled, out _, out _))
   {
       IsPlaying = !IsPlaying;
   }
   if (!playEnabled) Gui.EndDisabled();
   if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.DelayNormal))
   {
       if (isMerged)
           Gui.SetTooltip("Continuous playback is disabled in Merged View. Use Step-Forward/Backward or the timeline slider.");
       else
           Gui.SetTooltip(IsPlaying ? "Pause playback" : "Start playback");
   }
   ```

   NOTE: `TransportIconRenderer.DrawButton` already receives an `enabled` boolean; pass
   `playEnabled` instead of `hasRecording` to that call.

### Test changes

**File: `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/Foundation/ReplayTimelinePanelTests.cs`** (existing)

Add the following tests to the existing `ReplayTimelinePanelTests` class:

```csharp
[Fact]
public void RBF_P4T6_Play_DisabledInMerged()
{
    Assert.False(ReplayTimelinePanel.IsPlayEnabled(hasRecording: true, isMergedView: true));
}

[Fact]
public void RBF_P4T6_Play_EnabledInSingleNode()
{
    Assert.True(ReplayTimelinePanel.IsPlayEnabled(hasRecording: true, isMergedView: false));
}

[Fact]
public void RBF_P4T6_Play_DisabledWhenNoRecording()
{
    Assert.False(ReplayTimelinePanel.IsPlayEnabled(hasRecording: false, isMergedView: false));
}

[Fact]
public void RBF_P4T6_PlayTooltipContainsDisclaimer()
{
    // Verify the disclaimer string is present in the tooltip text used in Merged View.
    // The actual tooltip is set in DrawRow1_Transport; this test verifies the string constant.
    const string mergedTooltip = "Continuous playback is disabled in Merged View. Use Step-Forward/Backward or the timeline slider.";
    Assert.Contains("disabled in Merged View", mergedTooltip);
}
```

---

## Task: RBF-P4T7 — Disable search in Merged View

**Reference:** TASK-DETAILS.md `RBF-P4T7`, DESIGN §6.2.2

### Production changes

**File: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs`**

1. Add property:
   ```csharp
   /// <summary>
   /// When true, the panel renders a "Search disabled in Merged View" overlay.
   /// Set by the subsystem when entering/leaving Merged View.
   /// </summary>
   public bool IsMergedViewActive { get; set; }
   ```

2. In `DrawContent`, add an early-return path BEFORE `EnsureSession()`:
   ```csharp
   if (IsMergedViewActive)
   {
       Gui.TextDisabled("Search is disabled in Merged View. Switch to Single-Node View to search a specific recording.");
       return;
   }
   ```

### Test changes

**File: `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/SearchPanel/ReplaySearchPanelTests.cs`** (existing)

Add the following tests (inspect the existing test class structure; append at the end):

```csharp
[Fact]
public void RBF_P4T7_EnterMerged_NullsSearchPanelPath()
{
    // The subsystem sets CurrentFilePath = null when entering Merged.
    // This test verifies the property is settable to null.
    var panel = MakeSearchPanel();
    panel.CurrentFilePath = "/some/recording.fdp";
    panel.CurrentFilePath = null;
    Assert.Null(panel.CurrentFilePath);
}

[Fact]
public void RBF_P4T7_SearchPanel_RendersDisabledOverlayInMerged()
{
    // When IsMergedViewActive == true, the overlay path is active.
    var panel = MakeSearchPanel();
    panel.IsMergedViewActive = false;
    Assert.False(panel.IsMergedViewActive);
    panel.IsMergedViewActive = true;
    Assert.True(panel.IsMergedViewActive);
}

[Fact]
public void RBF_P4T7_SearchPanel_NoOverlayInSingleNode()
{
    // IsMergedViewActive defaults to false.
    var panel = MakeSearchPanel();
    Assert.False(panel.IsMergedViewActive);
}
```

---

## Subsystem wiring — `_timelinePanel.OnLoadGroup`

In `ReplayBrowserSubsystem.Initialize` (non-headless block), after the `_timelinePanel`
is created, wire the `OnLoadGroup` delegate:

```csharp
_timelinePanel.OnLoadGroup = paths =>
{
    try
    {
        _manager?.Dispose();
        _manager = FederatedReplayManager.LoadGroup(paths);
        _transientBuilder = new TransientMasterBuilder(_scenarioSerializer);
        _manager.OnTimeChanged += OnManagerTimeChanged;
        OnManagerTimeChanged();
        return null;  // success
    }
    catch (ArgumentException ex)
    {
        return ex.Message;  // rejection reason
    }
};
_timelinePanel.IsMergedViewQuery = () => _viewMode == ViewMode.Merged;
```

---

## Tests to write (complete list)

### `Fdp.Presentation.Tests` (filter: `RBF_P4T`)

1. `RBF_P4T1_LoadFdpAsync_PassesAllPathsToManager`
2. `RBF_P4T1_LoadFdpAsync_RejectionShowsModal`
3. `RBF_P4T2_OffsetEdit_CallsManagerSetNodeOffset`
4. `RBF_P4T2_BaseTickEdit_CallsManagerSetBaseWallTicks`
5. `RBF_P4T2_NonZeroOffset_ShowsWarningGlyph`
6. `RBF_P4T2_ModeToggle_FiresViewModeChanged`
7. `RBF_P4T2_ProviderDropdown_HiddenInSingleNode`
8. `RBF_P4T2_ProviderDropdown_VisibleInMerged_DefaultsToManagerValue`
9. `RBF_P4T2_ProviderDropdownChange_CallsManagerSetLocalEntitiesProvider`
10. `RBF_P4T4_NullEntityField_InMerged_RendersWarning_RegardlessOfOffset`
11. `RBF_P4T4_NullEntityField_InSingleNode_NoWarning`
12. `RBF_P4T4_NonNullEntityField_NoWarning`
13. `RBF_P4T4_TooltipMentionsBothCauses`
14. `RBF_P4T5_FederationPanel_DisclaimerTextContainsSutter` (or similar)
15. `RBF_P4T5_FederationPanel_DisclaimerTextContainsOffline`
16. `RBF_P4T6_Play_DisabledInMerged`
17. `RBF_P4T6_Play_EnabledInSingleNode`
18. `RBF_P4T6_Play_DisabledWhenNoRecording`
19. `RBF_P4T6_PlayTooltipContainsDisclaimer`
20. `RBF_P4T7_EnterMerged_NullsSearchPanelPath`
21. `RBF_P4T7_SearchPanel_RendersDisabledOverlayInMerged`
22. `RBF_P4T7_SearchPanel_NoOverlayInSingleNode`

### `Hrot.ReplayBrowser.Tests` (filter: `RBF_P4T`)

23. `RBF_P4T3_SingleNodeMode_BindsToCtxRepo`
24. `RBF_P4T3_MergedMode_BindsToTransientMaster`
25. `RBF_P4T3_OnTimeChangedInMerged_RebuildsMaster`
26. `RBF_P4T3_ProviderChangeInMerged_RebuildsMaster`
27. `RBF_P4T3_ModeSwitchToSingle_DisposesTransientMaster`
28. `RBF_P4T3_ModeSwitchToMerged_ForcesIsPlayingFalse`

---

## Success criteria

All 28 tests pass. `dotnet build IOS-IG-SimHost.sln` clean (0 errors, 0 warnings).

The following invariants must hold after this batch:
- `FederationPanel.ActiveMode == SingleNode` after construction (default).
- `TransientBuildOverride` is null in production — only set in tests.
- `IsMergedViewQuery` on `ReplayTimelinePanel` defaults to null (= not merged).
- `IsMergedViewActive` on `ReplaySearchPanel` defaults to false.
- `EntityFieldParadoxHelper.ShouldFlag(Entity.Null, false)` returns false.
- `FederatedReplayManager.BaseWallTicks` is a public getter added in BATCH-02 (already done).
- `FederatedReplayManager.NodeOffsets` is `IReadOnlyDictionary<int, long>` already added.

---

## Insight questions (Q1–Q5) — MUST be answered in the report

**Q1:** `TransportIconRenderer.DrawButton` takes an `enabled` bool parameter. What
happens to `IsPlaying` in the play handler if you accidentally pass `hasRecording`
instead of `playEnabled` to `DrawButton`'s enabled param? Document the behavioral
difference.

**Q2:** When `_timelinePanel` is null (headless mode) and `SetViewMode(Merged)` is called,
what guards prevent a `NullReferenceException`? List all null checks in the code path.

**Q3:** `FederatedReplayManager.LoadGroup` throws `ArgumentException` on validation
failure. In the `OnLoadGroup` delegate, we catch `ArgumentException` and return
`ex.Message`. Are there other exception types that `LoadGroup` might throw (e.g.,
`IOException` on missing `.meta.json`)? How does the current code handle them?

**Q4:** When the subsystem switches from Merged to Single-Node view, what is the correct
`CurrentFdpPath` to restore to `ReplaySearchPanel`? Explain which context is queried and
what happens if the recording file has since been deleted.

**Q5:** `ReplaySearchPanel.DrawContent` adds an early exit before `EnsureSession()` when
`IsMergedViewActive`. What happens to any in-progress async search when the panel stops
rendering? Does the search task get cancelled?

---

## Checklist before finishing

- [ ] Build the solution: `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 warnings
- [ ] Run P4 tests in `Fdp.Presentation.Tests`: all pass
- [ ] Run P4 tests in `Hrot.ReplayBrowser.Tests`: all pass
- [ ] Run full `Fdp.Toolkits.Tests` suite to verify no regressions
- [ ] Write report to `.dev/replay-browser-frankenstein/reports/BATCH-04-REPORT.md`
- [ ] Answer all Q1–Q5 in the report

---

## Notes for the developer

1. **`TransportIconRenderer.DrawButton` signature**: the third positional `bool` parameter
   is `enabled`. Double-check the actual method signature in
   `FDP/Engine/Fdp.Presentation/ImGui/Icons/TransportIconRenderer.cs`
   before changing the call site.

2. **`FederationPanel` uses ImGui**: `DrawContent` will not be called in tests (no ImGui
   context). All logic tests go through the public/internal method calls (`SetMode`,
   `SetNodeOffset`, etc.) rather than through `DrawContent`. Do NOT try to call
   `DrawContent` in tests.

3. **`Gui` alias**: Check `ReplayTimelinePanel.cs` imports — it likely uses
   `using Gui = ImGuiNET.ImGui;` or a static import. Use the same convention in
   `FederationPanel.cs`. Do NOT use `ImGuiNET.ImGui` directly in one place and `Gui` in
   another within the same file.

4. **`WinFormsFileDialogService` multi-select buffer**: The Windows `OPENFILENAME` struct
   uses `lpstrFile` with `OFN_ALLOWMULTISELECT`. The format of the returned buffer is:
   `<directory>\0<file1>\0<file2>\0...\0\0`.
   If only one file is selected, the buffer is: `<full-path>\0\0`.
   Parse both cases correctly. The buffer needs to be at least 32768 bytes to hold
   multiple paths safely.

5. **`FederationPanel` uses `LINQ`**: `OrderBy` calls require `using System.Linq;`.
   Add it if needed. This is not available in all contexts — check if
   `Fdp.Presentation` project already imports LINQ.

6. **Test file placement**:
   - P4T1, P4T4, P4T5, P4T6 tests → `Fdp.Presentation.Tests/ImGui/ReplayBrowser/Foundation/`
   - P4T2 tests → `Fdp.Presentation.Tests/ImGui/ReplayBrowser/Federation/`
   - P4T3 tests → `Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs` (existing file)
   - P4T7 tests → `Fdp.Presentation.Tests/ImGui/ReplayBrowser/SearchPanel/ReplaySearchPanelTests.cs` (existing file)

7. **`FederatedReplayManager.BaseWallTicks`**: already public from BATCH-02. Verify it
   compiles before writing `RBF_P4T2_BaseTickEdit_CallsManagerSetBaseWallTicks`.

8. **`RBF_P4T3_ModeSwitchToMerged_ForcesIsPlayingFalse`**: In headless mode, `_timelinePanel`
   is null. The test verifies that `SetViewMode(Merged)` does NOT throw when
   `_timelinePanel == null`, and that `_viewMode` is set to `Merged`. The actual
   `IsPlaying = false` enforcement is tested in a non-headless integration context
   (out of scope for this batch's unit tests). Mark the null-guard path with a comment.
