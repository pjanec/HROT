# FDP Replay Browser — Task Details

**Reference**: [DESIGN.md](./DESIGN.md). Design-talk line ranges below point into [design-talk.md](./design-talk.md) for verbatim code samples a developer can lift.

Task ID convention: `RB-<stage>.<index>` — `RB-1.x` Stage 1, `RB-2.x` Stage 2, `RB-3.x` Stage 3, `RB-4.x` Stage 4, `RB-5.x` Stage 5. Test-ID prefixes are `EX-T*` Stage 1, `FND-T*` Stage 2, `DIF-T*` Stage 3, `SR-T*` Stage 4 — all listed in the corresponding DESIGN.md tables.

Every task lists **binary success conditions**. A task is "done" only when *every* bullet is true. When a bullet says "test passes" the test is one of the IDs listed in DESIGN.md.

---

## Stage 1 — Headless JSON Export Pipeline

> See [DESIGN.md §3](./DESIGN.md#3-stage-1--headless-json-export-pipeline).

### RB-1.0 — Codebase Audit and Gap Fix

**Scope**: Resolve the two open codebase verification items in DESIGN.md §1 risk register D1/D2 before any production code is written.

**Concrete steps**:
1. Confirm `ComponentTypeRegistry` has an enumeration of all registered types. If absent, add a `public static IReadOnlyList<Type> GetAllRegistered()` accessor in [FDP/Engine/Fdp.Core/ComponentType.cs](../../FDP/Engine/Fdp.Core/ComponentType.cs) that returns a snapshot of the internal map.
2. Confirm `EventType` (in `FDP/Engine/Fdp.Core/EventType.cs`) has the equivalent accessor. Add if missing.
3. Confirm `EntityRepository.HasComponentByTypeId(Entity, int)` exists. If absent, document the fallback `repo.GetHeader(e.Index).ComponentMask.IsSet(typeId)` in a small `RepositoryExtensions.HasComponentByTypeId` helper in `Fdp.Toolkits/ReplayBrowser/`.

**Success conditions**:
- [ ] `ComponentTypeRegistry.GetAllRegistered()` (or equivalent) returns a non-empty enumeration when called from a unit test that has at least one registered component.
- [ ] `EventType.GetAllRegistered()` (or equivalent) does the same for events.
- [ ] A unit test in `Fdp.Core.Tests` covers each accessor: register two types, assert both appear.
- [ ] If a helper extension was added, it has its own xUnit test that exercises both the positive and negative cases of presence.

---

### RB-1.1 — `FdpRecordingHarness` Test Substrate

**Scope**: Build a reusable in-test recording harness. This is the substrate for every backend test in Stages 1, 3, and 4.

**Concrete steps**:
1. Place at `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarness.cs`.
2. The harness:
   - Allocates an `EntityRepository`, a `RecorderSystem`, and a `MemoryStream` (or temp `FileStream`).
   - Exposes fluent methods: `SpawnEntity()`, `WithComponent<T>(...)`, `MutateComponent<T>(entity, mutator)`, `FireUnmanagedEvent<T>(...)`, `FireManagedEvent<T>(...)`, `AddComponent<T>(entity, ...)`, `RemoveComponent<T>(entity)`, `DestroyEntity(entity)`, `Tick()`, `RecordKeyframe()`, `RecordDelta()`, `BuildToTempFile(out string path)`, `BuildToStream(out Stream s)`.
   - Increments `EntityRepository.Tick()` once per `Tick()` call so `GlobalVersion`/`GlobalTime` advance correctly.
   - Returns the path/stream usable directly by `new PlaybackController(path)` or `new RecordingReader(...)`.

**Success conditions**:
- [ ] Harness produces an `.fdp` file whose first frame is a Keyframe with `FormatVersion == FdpConfig.FORMAT_VERSION`.
- [ ] `RecordingReader.ReadNextFrame` over the produced file yields the same `Tick`/`FrameType`/`WallClockTicks` sequence the test orchestrated.
- [ ] A self-test (`HarnessSelfTest`) instantiates a 3-entity, 5-frame recording (1 keyframe + 4 deltas) with a destruction on tick 3 and a managed + unmanaged event on tick 4, then re-reads it and asserts byte-level equality of those features.
- [ ] Harness creates no leaked temp files when `Dispose` is called.

---

### RB-1.2 — Domain DTOs (`JsonExportOptions`, `ChangelogEntryDto`, enums)

**Scope**: Land the headless domain models defined in [DESIGN.md §3.2](./DESIGN.md#32-domain-models-fdptoolkitsreplaybrowser).

**Concrete steps**:
1. Create `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/JsonExportOptions.cs` with the exact class definition from DESIGN.md §3.2.
2. Create `ExportWindowMode`, `ExportFormatMode` enums same path.
3. Create `ChangelogEntryDto` record same folder.

Reference DTO field set and defaults: design-talk.md lines 879–906, 2142–2160, 2167–2172.

**Success conditions**:
- [ ] All field names and default values match DESIGN.md §3.2 verbatim.
- [ ] xUnit test instantiates `JsonExportOptions` and asserts every default.
- [ ] A round-trip JSON test (`JsonSerializer.Serialize` + `Deserialize`) preserves all fields including the `List<Entity>` (use `FdpJsonOptionsRegistry.Indented`).

---

### RB-1.3 — `IRecordingExportService` Contract

**Scope**: Land the public interface from [DESIGN.md §3.3](./DESIGN.md#33-service-contract).

**Concrete steps**: Create `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/IRecordingExportService.cs` and a stub `RecordingExportService` implementing it (throwing `NotImplementedException` for now).

**Success conditions**:
- [ ] Interface signature exactly matches DESIGN.md §3.3.
- [ ] Stub assembly compiles and has zero references to `Fdp.Presentation` or `Raylib*` (asserted by a referenced-assembly test).

---

### RB-1.4 — Headless `ReplayBrowserContext`

**Scope**: Implement the sandbox context per [DESIGN.md §4.2](./DESIGN.md#42-sandbox-context). Placed in the shared `Fdp.Toolkits/ReplayBrowser/` folder because both the export service and the GUI need it.

**Concrete steps**: Build per DESIGN.md §4.2. Reference design-talk.md lines 964–999 for the structural code sample (kept; expand `SeekToFrame` to also re-Capture history).

**Success conditions**:
- [ ] `Initialize` / `Dispose` follow IDisposable contract; double-dispose is a no-op.
- [ ] `SeekToFrame` invokes, in this exact order: `SandboxBus.ClearCurrentBuffers()`, `Playback.SeekToFrame(SandboxRepo, frameIndex)`, `HistoryService.Capture("Replay", SandboxBus, (uint)CurrentFrame)`. Verified via spy on each.
- [ ] `StepForward` returns `false` past EOF without throwing.
- [ ] FND-T06 and FND-T07 pass.

---

### RB-1.5 — `RecordingExportService` Implementation

**Scope**: Implement the export pipeline per [DESIGN.md §3.4 / §3.5 / §3.6](./DESIGN.md#34-pipeline-algorithm).

**Reference code in design-talk.md**: 519–561 (skeleton), 671–706 (time windowing), 2178–2247 (changelog mode).

**Concrete steps**:
1. Build the streaming exporter using `Utf8JsonWriter` against a `FileStream`.
2. Resolve `ScenarioSerializer` via `HrotScenarioSerializerFactory.Build(behaviorRegistry)` — accept the `BehaviorRegistry` as a constructor parameter on `RecordingExportService` so tests can inject a stub.
3. For `IncludeEntities`, iterate `header.ComponentMask` bits and call `IInspectableSession.HasAuthority` via a `RepositoryAdapter`.
4. For `Changelog` mode, depend on `IComponentDiffService` (RB-3.x) — Stage 1 ships the absolute-state path first, Changelog lands after Stage 3 backend is done.

**Success conditions (Absolute-state path, mandatory for Stage 1 closeout)**:
- [ ] EX-T01 through EX-T26 all pass.
- [ ] Streaming verified: peak managed heap during a 10k-frame export is below 32 MB (EX-T25 fixture).
- [ ] Service does not depend on `Fdp.Presentation` (assembly-reference test).
- [ ] All numeric `Vector3`/`Quaternion` payload arrays are flattened to one-line arrays (EX-T20).

**Success conditions (Changelog path, can land alongside Stage 3)**:
- [ ] EX-T27, EX-T28, EX-T29 pass.

---

### RB-1.6 — `Fdp.Tools.RecordingDumper` Console App

**Scope**: Stand up the CLI per [DESIGN.md §3.7](./DESIGN.md#37-cli-fdptoolsrecordingdumper).

**Reference**: design-talk.md lines 631–707, 855–862.

**Concrete steps**:
1. Create `FDP/Tools/Fdp.Tools.RecordingDumper/Fdp.Tools.RecordingDumper.csproj` (.NET 8 console exe).
2. Use the same `CommandLine` package pattern as `FDP/Examples/Fdp.Examples.Runner/Program.cs`.
3. Define a `DumperOptions` class with `[Option]` attributes mapped to DESIGN.md §3.7's switch table.
4. Validate mutual exclusion of `--start-frame`/`--end-frame` vs `--start-time`/`--end-time` at parse time.
5. Map to `JsonExportOptions`, invoke `RecordingExportService.ExportToJson` on the main thread, return exit codes per DESIGN.md §3.7.

**Success conditions**:
- [ ] EX-T30, EX-T31, EX-T32 all pass.
- [ ] An assembly-reference test asserts `Fdp.Tools.RecordingDumper` has no transitive reference to `Fdp.Presentation` or `Hrot.ClusterRunner`.
- [ ] `dotnet run -- -i fixture.fdp -o out.json --minified` produces a non-empty `out.json` and exits with code `0`.
- [ ] Invocation without required `--input` returns exit code `1` and prints usage to stderr.

---

### RB-1.7 — Stage 1 Acceptance Gate

**Scope**: Verify all EX-T tests in CI.

**Success conditions**:
- [ ] All 32 EX-T tests pass in the test runner CI configuration.
- [ ] No additions to `Fdp.Presentation` were required for Stage 1.
- [ ] A handcrafted 100 MB recording fixture exports without OOM and within linear time (manual measurement noted in test log).

---

## Stage 2 — Replay Browser Subsystem Foundation

> See [DESIGN.md §4](./DESIGN.md#4-stage-2--replay-browser-subsystem-foundation).

### RB-2.1 — History Trackers

**Scope**: `EntitySelectionHistory` and `PlaybackHistoryTracker` per [DESIGN.md §4.6](./DESIGN.md#46-histories-stage-2-deliverable). These are *headless* pieces — they ship and get tested before the windows are touched.

**Reference code**: design-talk.md lines 1797–1855 (entity history), 2003–2050 (playback history). The samples are suitable; lift them verbatim.

**Concrete steps**:
1. Place at `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/EntitySelectionHistory.cs` and `.../PlaybackHistoryTracker.cs`.
2. Implement the navigation-suspension flag.
3. Truncate the forward stack on a new push after a back.
4. Suppress duplicate consecutive pushes.

**Success conditions**:
- [ ] FND-T01, FND-T02, FND-T03, FND-T04 pass for `EntitySelectionHistory`.
- [ ] FND-T05 passes for `PlaybackHistoryTracker`.
- [ ] xUnit smoke test: 100 randomized push/back/forward sequences stay internally consistent (`CanGoBack`/`CanGoForward` never out of sync with the index pointer).

---

### RB-2.2 — `ImGuiEntityLink` Utility

**Scope**: Per [DESIGN.md §4.7](./DESIGN.md#47-entity-deep-link-primitive).

**Reference**: design-talk.md lines 1862–1907. The sample is suitable; lift `TryParse` verbatim.

**Concrete steps**: Place at `FDP/Engine/Fdp.Presentation/ImGui/Utils/ReplayBrowser/ImGuiEntityLink.cs`. The `Draw` method uses `SmallButton` with the ExConViolet style color.

**Success conditions**:
- [ ] FND-T13, FND-T14 pass.
- [ ] Additional parser tests for: leading/trailing spaces, lowercase/uppercase `v` prefix, negative index, missing comma, empty string. All malformed inputs return false without throwing.

---

### RB-2.3 — `ReplayBrowserSubsystem` Skeleton

**Scope**: Subsystem class per [DESIGN.md §4.1](./DESIGN.md#41-subsystem).

**Concrete steps**:
1. Create `Hrot/Subsystems/Hrot.ReplayBrowser/Hrot.ReplayBrowser.csproj`.
2. Implement `ReplayBrowserSubsystem : ISubsystem, IWindowRegistrar` with empty `RegisterWindows`. Explicitly **do not** implement `IMapCameraProvider`.
3. Wire `Initialize(SubsystemConfig)` to allocate the `ReplayBrowserContext`, `EntitySelectionHistory`, `PlaybackHistoryTracker`, and (when `!Headless`) a `MapCanvas` + `DebugGizmoLayer` + `GridMapLayer`.
4. `Update` ticks the canvas; `DrawWorld` calls `_canvas.Draw()`.
5. Add the subsystem assembly to the ClusterRunner solution so `ScanForSubsystems` picks it up.

**Success conditions**:
- [ ] FND-T09 (headless mode), FND-T10 (no `IMapCameraProvider`), FND-T11 (CLI discovery) pass.
- [ ] `Hrot.ClusterRunner.Tests` integration test launches the runner headless with `-m replaybrowser` and asserts the subsystem's `Initialize` ran.

---

### RB-2.4 — Reused Panel Wiring (Inspector + Events) and 4 Windows

**Scope**: Per [DESIGN.md §4.4](./DESIGN.md#44-windows). Five windows total (Timeline, Inspector, Diff, Events, Search) — Timeline panel itself is RB-2.5, Diff panel is RB-3.4, Search window is RB-4.10.

**Reference**: design-talk.md lines 1313–1373 (window classes), 1437–1499 (window composition).

**Concrete steps**:
1. Create `ReplayTimelineWindow`, `FdpEntityInspectorWindow`, `ComponentDiffWindow`, `FdpEventBrowserWindow`, `ReplaySearchWindow` shells, all in `FDP/Engine/Fdp.Presentation/ImGui/Windows/ReplayBrowser/`.
2. Each window inherits `ManagedWindow`, takes `(id, title, perspective, panel, color)` constructor, `Scope = PerspectiveBound`, `IsOpen = true`, delegates `DrawClientArea` to its panel.
3. `FdpEntityInspectorWindow` injects `() => new RepositoryAdapter(_context.SandboxRepo)` and a closure returning the local `InspectorState`.
4. The subsystem's `RegisterWindows` registers all five via `WindowManager.RegisterWindow`.

**Success conditions**:
- [ ] FND-T12 passes — exactly five windows registered with `PerspectiveBound` scope and `"ReplayBrowser"` perspective.
- [ ] A presentation-layer smoke test (no ImGui calls) builds an in-memory `WindowManager`, runs `RegisterWindows`, and verifies the five windows are present with correct `OwningPerspective` and `Scope`.
- [ ] When the inspector window draws (mocked ImGui surface), `InspectorState.SelectedEntity` changes propagate to `EntitySelectionHistory.PushSelection` via `OnEntitySelected`.

---

### RB-2.5 — `ReplayTimelinePanel`

**Scope**: Timeline + transport + history navigation + JSON Export Options expander, per [DESIGN.md §4.5](./DESIGN.md#45-timeline-panel--full-layout-per-design-talk).

**Reference**: design-talk.md lines 753–845 (export options + execution), 2079–2125 (transport with history). The wireframe in DESIGN.md §4.5 is normative — preserve every row and label.

**Concrete steps**:
1. Place at `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplayTimelinePanel.cs`.
2. Render the full layout in the wireframe — back/forward history buttons, Rewind/Step Back/Step Forward, Pause/Play, the timeline slider, the meta line, the file picker, and the **JSON Export Options expander** with every field in DESIGN.md §4.5.
3. The expander uses `ImGui.TreeNode("JSON Export Options")` and disables irrelevant inputs via `BeginDisabled`/`EndDisabled` based on `WindowMode`/`FormatMode`. See design-talk.md lines 754–812 for the disabling pattern.
4. `Save to JSON…` button:
   - Clones the local `JsonExportOptions` to an `optionsSnapshot` (per design-talk.md lines 820–845).
   - Calls `_fileDialogService.ShowSaveAsDialogAsync("dump.json", "*.json")`.
   - On success, runs `RecordingExportService.ExportToJson` on `Task.Factory.StartNew(..., LongRunning)`.
   - Sets a UI flag while the export is running and shows the flag in the toolbar.
5. Provide an `OnEntitySelected` callback hook for selection deep-link from anywhere using the timeline's history.

**Success conditions**:
- [ ] Every UI row in DESIGN.md §4.5's wireframe is present (visual inspection in dev; ASCII-snapshot test of the imgui mock surface for label presence).
- [ ] Disabled-state logic verified: when `WindowMode==FullFile`, both frame and time inputs are disabled; when `ByFrame`, time inputs are disabled; when `ByTime`, frame inputs are disabled. A presentation-test asserts the `BeginDisabled` calls happen in the right order.
- [ ] FND-T17 passes (immutable snapshot during async).
- [ ] Clicking `Save to JSON…` with a stub `_fileDialogService` returning a path triggers exactly one `ExportToJson` call (verified via spy).
- [ ] Pressing the History Back button after stepping forward twice calls `_context.SeekToFrame` with the previous frame index.

---

### RB-2.6 — Subsystem Composition Root and Delegate Wiring

**Scope**: Per [DESIGN.md §4.6](./DESIGN.md#46-histories-stage-2-deliverable), wire the delegates between panels, histories, and the context.

**Reference**: design-talk.md lines 1966–1989, 2110–2125, 2058–2073 (causality jump).

**Concrete steps**:
1. In `ReplayBrowserSubsystem.Initialize` after panels are built (per DESIGN.md §6.7.3, composition root is the **only** place history trackers meet panels):
   ```csharp
   _entityHistory.OnSelectionChanged += e => _context.InspectorState.SelectedEntity = e;
   _playbackHistory.OnSeekRequested  += f => _context.SeekToFrame(f);

   Action<int>    seekIntent   = f => { _playbackHistory.PushFrame(f); _context.SeekToFrame(f); };
   Action<Entity> selectIntent = e => _entityHistory.PushSelection(e);

   _inspectorPanel.OnEntitySelected = selectIntent;
   _diffPanel.OnEntityLinkClicked   = selectIntent;
   _eventPanel.OnEntityLinkClicked  = selectIntent;
   _searchPanel = new ReplaySearchPanel(editService, searchService, seekIntent, selectIntent);
   ```
2. Implement causality right-click menu in `EventBrowserPanel`'s row context: "Step Forward and Diff Target" — closure captures the same `seekIntent`/`selectIntent` delegates, pushes pre-frame, calls `_context.StepForward()`, pushes post-frame, `selectIntent(target)`.
3. The `EntityInspectorPanel` toolbar gains Back/Forward arrow buttons gated by `history.CanGoBack/Forward`.

**Success conditions**:
- [ ] FND-T15 passes (`EntityInspectorPanel.OnEntitySelected` propagates).
- [ ] FND-T16 passes (Step-Forward-and-Diff macro full sequence).
- [ ] FND-T18 passes (`seekIntent(7)` triggers exactly one `PlaybackHistoryTracker.PushFrame(7)` and one `_context.SeekToFrame(7)`, in that order; `selectIntent(e)` triggers exactly one `EntitySelectionHistory.PushSelection(e)` and the `OnSelectionChanged → InspectorState.SelectedEntity` chain fires once).
- [ ] A presentation-layer test verifies clicking the inspector's Back arrow with `CanGoBack==false` is a no-op.
- [ ] Selecting an entity via the inspector list pushes onto the entity history; selecting the same entity twice in a row produces only one history entry (no duplicates).

---

### RB-2.7 — Stage 2 Acceptance Gate

**Success conditions**:
- [ ] All FND-T tests pass.
- [ ] Launching the cluster runner headless with `-m replaybrowser` succeeds and the subsystem reports five registered windows.
- [ ] Manual smoke test: launch GUI in `replaybrowser` perspective, load an `.fdp` file, scrub the timeline, watch the inspector and event browser update; entity selection deep-links work; Save-to-JSON exports a file without freezing the UI.

---

## Stage 3 — Diff Engine

> See [DESIGN.md §5](./DESIGN.md#5-stage-3--diff-engine). Stage 3 backend ships BEFORE the diff panel UI; the changelog mode in Stage 1 depends on it.

### RB-3.1 — `DiffNode` Hierarchy

**Scope**: Per [DESIGN.md §5.1](./DESIGN.md#51-data-model).

**Reference**: design-talk.md lines 1604–1638. Sample suitable; lift verbatim.

**Concrete steps**: Place under `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/`. Add `DiffObject.EvaluateModificationState()`.

**Success conditions**:
- [ ] `DiffObject` propagates `IsModified` from any modified descendant (unit test).
- [ ] `DiffValue` records `OldValue`, `NewValue`, `ValueType`, and `IsModified` set at construction time.
- [ ] Public types compile in an assembly with no Presentation reference.

---

### RB-3.2 — `IComponentDiffService` + `ComponentDiffService`

**Scope**: Per [DESIGN.md §5.2](./DESIGN.md#52-service).

**Reference**: design-talk.md lines 1646–1684 (recursive diff with epsilon). Sample suitable; lift verbatim and add `ComputeTreeDiff` / `ComputeEntityDiff` overloads.

**Concrete steps**:
1. Place at `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/ComponentDiffService.cs`.
2. Implement the recursive object-tree diff with `EpsilonTolerance` for numeric leaves.
3. Array-with-any-difference rule: full-array text emitted as one modified leaf — match the talk's note ("if they differ, treat the entire array as changed to keep visual output clean"), design-talk.md lines 382–384.
4. `ComputeEntityDiff(entity, repo, serializer, applyStepFunc)`: serialize pre, run `applyStepFunc()`, serialize post, return rooted list of `DiffNode`s (one per touched component on the entity).
5. `ComputeTreeDiff(before, after, epsilon)` is the unrooted helper used by changelog mode.

**Success conditions**:
- [ ] DIF-T01..DIF-T13 all pass.
- [ ] An allocation profile test (`GC.GetTotalAllocatedBytes(true)` before/after 1000 invocations with a 200-leaf tree) reports < 1 MB total allocations (DIF-T09).
- [ ] Bringing in the service requires no presentation or Raylib reference.

---

### RB-3.3 — Wire Changelog Mode into `RecordingExportService`

**Scope**: Plug `IComponentDiffService` into `RecordingExportService` per [DESIGN.md §3.6](./DESIGN.md#36-changelog-mode).

**Reference**: design-talk.md lines 2178–2247.

**Success conditions**:
- [ ] EX-T27, EX-T28, EX-T29 pass.
- [ ] When `FormatMode == Changelog` and no target entities are specified, the service returns an empty JSON array (no error).

---

### RB-3.4 — `ComponentDiffPanel`

**Scope**: ImGui panel rendering of the diff tree per [DESIGN.md §5.3](./DESIGN.md#53-diff-panel-layout-preserve-exactly).

**Reference**: design-talk.md lines 1690–1782. The layout in DESIGN.md §5.3 is normative.

**Concrete steps**:
1. Place at `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ComponentDiffPanel.cs`.
2. Render the wireframe with the two top toggles "Ignore Epsilon (< 0.001)" + "Hide Unchanged Components & Fields". **`_hideUnchanged` defaults to true** (design talk requirement, lines 1592–1593).
3. Use `BeginTable("DiffViewerTable", 2, Borders | RowBg | Resizable | SizingFixedFit)`.
4. Internal nodes: `TreeNodeEx(DefaultOpen | SpanAvailWidth)`. Leaves: `TreeNodeEx(Leaf | NoTreePushOnOpen | SpanAvailWidth)`.
5. Apply the early-return cull `if (_hideUnchanged && !node.IsModified) return;` at the very start of `DrawDiffNode`.
6. Render leaf values with the cyan/green/amber/light-gray palette by `JsonValueKind` (design-talk.md lines 1561–1572).
7. Detect entity-handle strings via `ImGuiEntityLink.TryParse` and render both old/new sides as `ImGuiEntityLink.Draw` buttons; fire `_onEntityLinkClicked` on click.

**Success conditions**:
- [ ] Snapshot test (against a mock ImGui surface) renders a diff tree with: top toolbar showing both checkboxes with `_hideUnchanged` checked by default; pruning of unchanged siblings of a modified leaf at depth 4.
- [ ] Clicking an entity-handle leaf invokes `_onEntityLinkClicked` once with the parsed entity.
- [ ] Toggling `_hideUnchanged` to false expands and shows the full structural hierarchy of any component that mutated (verified by counting visible rows before/after toggle).
- [ ] Manual smoke: in the running GUI, step a recording where only `Position.X` changes — only `SimTransform → Position → X` rows are visible.

---

### RB-3.5 — Stage 3 Acceptance Gate

**Success conditions**:
- [ ] DIF-T01..DIF-T13 pass.
- [ ] Changelog export passes EX-T27..T29.
- [ ] `ComponentDiffPanel` integrated into `ComponentDiffWindow` and visible in the ReplayBrowser perspective.

---

## Stage 4 — Advanced Recording Search Engine

> See [DESIGN.md §6](./DESIGN.md#6-stage-4--advanced-recording-search-engine). All backend tasks (RB-4.1..RB-4.7) must pass tests before any UI task (RB-4.8..RB-4.11) is started.

### RB-4.1 — Search Domain DTOs

**Scope**: Per [DESIGN.md §6.1](./DESIGN.md#61-domain-polymorphic-serializable-via-structedit).

**Reference**: design-talk.md lines 4256–4279, 2500–2525, 2697–2711, 3266–3275 (combined). The sample DTO definitions are suitable; lift them. **Important extension**: `StructuralPredicateDto` carries a new `AuthorityRequirement` enum (`Any`, `RequireAuthority`, `RequireGhost`) — DESIGN.md §6.1 — used by the structural search loop to filter ECS authority status.

**Concrete steps**: Place under `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/`. Includes `SearchPredicateDto` base + all concrete predicates (compound, property-match, transient-event, lifecycle, spatial-bounding, structural) + the value-predicate DTOs (numeric, string, enum) + `SearchResultDto` + `LifecycleSearchResultDto` + `BoundingBox2D` + the two custom attributes + the `AuthorityRequirement` enum.

**Success conditions**:
- [ ] All DTOs are public, polymorphic from `SearchPredicateDto`.
- [ ] `StructuralPredicateDto.AuthorityRequirement` defaults to `Any`.
- [ ] A round-trip JSON test through `JsonSerializer` (with type discriminator) preserves every field across every concrete type, including `AuthorityRequirement`.
- [ ] No reference to `Fdp.Presentation` from this folder.

---

### RB-4.2 — `IPropertyEvaluator` (+ `StructEdit` binding)

**Scope**: Per [DESIGN.md §6.2](./DESIGN.md#62-compilation-layer).

**Reference**: design-talk.md lines 2313–2342 (PropertyEvaluator code sample). Suitable; lift.

**Concrete steps**:
1. Open a transient `IEditSession` via `IComponentEditService.Open(dummy, componentType, EditScope.ForField($"$.{propertyPath}"))`.
2. Extract the precompiled `IValueBinding` and `IEditBuffer`.
3. `GetValueAsString` calls `_buffer.ReplaceInstance(componentInstance); return _binding.GetBoxed()?.ToString() ?? "null";`.

**Success conditions**:
- [ ] Reading a known field (e.g., `SimTransform.Position.X`) returns the expected string for various sample instances.
- [ ] Invalid `PropertyPath` throws a clear `ArgumentException` at construction (not at evaluation).
- [ ] 10k `GetValueAsString` invocations allocate < 1 KB (allocation profile).

---

### RB-4.3 — `IPredicateCompiler` (+ `PredicateCompiler`)

**Scope**: Per [DESIGN.md §6.2](./DESIGN.md#62-compilation-layer).

**Reference**: design-talk.md lines 4296–4376 (compilation tree). Lift the recursive compiler structure verbatim.

**Concrete steps**:
1. Compile `CompoundPredicateDto` to a closure that short-circuits AND/OR over child closures.
2. Compile `PropertyMatchDto` to a closure that: 
   - Guards `repo.HasComponent` (or fallback `header.ComponentMask.IsSet`).
   - Calls `PropertyEvaluator.GetValueAsString` on the component instance via `repo.GetRawObject(entity, componentType)` (or fallback `repo.GetComponentRO<T>` via reflection — confirm during RB-1.0).
   - Dispatches the operator (Equals/Contains/StartsWith/GreaterThan/LessThan/Changed) against the per-leaf value predicate DTO.
3. Mandatory-component extraction (`ExtractMandatoryComponents` from design-talk.md lines 4364–4376) — feeds into `QueryBuilder.With(t)` for AND-only roots.

**Success conditions**:
- [ ] SR-T05, SR-T06, SR-T07, SR-T08, SR-T35 all pass.
- [ ] Mandatory-component extraction test: given root `AND(PropertyMatch(BehaviorState,...), PropertyMatch(NavigationStatus,...))`, the resulting query has `With(BehaviorState)` and `With(NavigationStatus)` (verified via spy on QueryBuilder).
- [ ] OR roots do NOT add mandatory components (verified by inverting the previous test).

---

### RB-4.4 — `IEventScannerCompiler` (+ `FastEventScanner<T>`, `ManagedEventScanner<T>`, occurrence scanner)

**Scope**: Per [DESIGN.md §6.2](./DESIGN.md#62-compilation-layer).

**Reference**: design-talk.md lines 2937–3022 (unmanaged), 3125–3161 (managed), 3284–3325 (occurrence branch).

**Concrete steps**: Implement the three closures and branch in the factory by `IsValueType` and `AnyOccurrence || PropertyPath==null/empty`.

**Success conditions**:
- [ ] SR-T23..SR-T27 all pass.
- [ ] Pure-occurrence variant performs zero payload reads (verified by counting `bus.Read<T>` invocations across 1000 frames where the event is absent).
- [ ] Allocations during a 10k-frame pure-occurrence scan are < 1 KB.

---

### RB-4.5 — `IRecordingSearchService` (+ `RecordingSearchService`)

**Scope**: Per [DESIGN.md §6.3](./DESIGN.md#63-service-contract) and [§6.4](./DESIGN.md#64-execution-algorithms-preserve-exactly-from-the-talk).

**Reference**: design-talk.md lines 2351–2401 (component property loop), 3622–3666 (spatial loop), 3825–3897 (structural loop), 2722–2810 (lifecycle loop), 3025–3052 (event loop).

**Concrete steps**:
1. Place at `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/RecordingSearchService.cs`.
2. Dispatch on root predicate type:
   - Pure event predicate root → event scanner loop.
   - Lifecycle root → lifecycle loop.
   - Any other (component, structural, spatial, compound) → unified frame-step loop with the compiled predicate.
3. Use a fresh `ReplayBrowserContext` per invocation; dispose on exit.
4. Every loop calls `repo.GetDestructionLog()` + `ClearDestructionLog()` per frame (for state-machine cleanup).
5. **Strict zero-allocation extraction loop** (DESIGN.md §6.8 SR-T34):
   - The `List<SearchResultDto> results` is allocated once before the loop with a sensible default capacity (e.g., 64) and grown only when matches arrive.
   - The `HashSet<Entity>` state machines (`insideZone`, `hasComponent`, `activeRanges` keys) are allocated once before the loop.
   - **No** `string.Format`, interpolated strings, `ToString()` on value types, `LINQ` materialization, lambda closure capture (other than fields/locals captured at compile time before the loop), or `Tuple`/`KeyValuePair` boxing occurs inside the loop body.
   - Per-match `SearchResultDto` allocations are accepted only on actual matches; on no-match frames, total managed allocations per loop iteration must be exactly 0 bytes.
6. **Strict event-scanner timing** (DESIGN.md §6.4):
   - Per frame execute exactly: `playback.StepForward(sandboxRepo)` → `scanner.Invoke(sandboxBus, ...)` → next iteration.
   - **Never** call `SandboxBus.ClearCurrentBuffers()` between StepForward and Invoke. Do **not** route through `ReplayBrowserContext.SeekToFrame` for the per-frame step (that helper clears the bus). The headless search service steps the `PlaybackController` directly.
7. **Structural authority-aware presence** (DESIGN.md §6.4):
   - Compute `present` per `(entity, header)` according to `AuthorityRequirement`: bit-test `ComponentMask`, AND with `AuthorityMask.IsSet(typeId)` (RequireAuthority) or `!AuthorityMask.IsSet(typeId)` (RequireGhost).
   - The `hasComponent` membership set tracks effective presence; authority flips alone (with `ComponentMask` unchanged) are edges under the active requirement.

**Success conditions**:
- [ ] SR-T02..SR-T36, SR-T37, SR-T38 all pass (every test in DESIGN.md §6.8).
- [ ] An assembly-reference test asserts no `Fdp.Presentation` or Raylib dependency.
- [ ] SR-T36 specifically asserts the service does not disturb a parallel context's `CurrentFrame`.
- [ ] SR-T34 zero-allocation assertion — `GC.GetAllocatedBytesForCurrentThread()` snapshot before/after a 10k-frame compiled-predicate scan reports `(after - before) == 0` for no-match frames.
- [ ] SR-T37 authority-aware structural search emits results per `AuthorityRequirement`.
- [ ] SR-T38 confirms scanner sees `StepForward`-injected events without any intervening buffer clear.

---

### RB-4.6 — `BoundingBoxPickerGizmo`

**Scope**: Per [DESIGN.md §6.6](./DESIGN.md#66-spatial-gizmo).

**Reference**: design-talk.md lines 3494–3565 (gizmo class). Suitable; lift.

**Concrete steps**:
1. Implement `IEntityStatefulGizmo` with `RequiresExclusiveFocus = true`, `WantsRawInput = true`.
2. Draw a translucent box via `IDebugDrawBuilder.EmitRaw(DebugPrimitive.MakeBox2D(...))` during drag.
3. Cancel on Right-click or `MapKeyboardKey.Escape`.

**Success conditions**:
- [ ] SR-T30 and SR-T31 pass.
- [ ] A headless test fires the press/drag/release sequence and asserts `onComplete` is called exactly once with the right `(Min, Max)`.
- [ ] No `onComplete` after Escape; `onRemove` is called.

---

### RB-4.7 — Stage 4 Backend Acceptance Gate

**Success conditions**:
- [ ] SR-T01..SR-T38 all pass.
- [ ] SR-T28 specifically (preset round-trip) passes — this *unblocks* the UI tasks.
- [ ] Allocation gates SR-T08 (compiler) and SR-T34 (extraction-loop strict-zero) both pass.
- [ ] Chunk-skip gate SR-T09 passes.
- [ ] Authority filtering gate SR-T37 passes.
- [ ] Event-scanner timing invariant SR-T38 passes.

---

### RB-4.8 — StructEdit Plumbing for the Search Panel

**Scope**: Per [DESIGN.md §6.5](./DESIGN.md#65-structedit-ui-plumbing).

**Reference**: design-talk.md lines 2528–2632 (session lifecycle, drawing, validate+commit), 4386–4435 (preset save/load).

**Concrete steps**:
1. The panel owns a `_predicateSession` opened over the root `CompoundPredicateDto` via `IComponentEditService.Open(root, typeof(CompoundPredicateDto), EditScope.WholeComponent)`.
2. On every draw: if `_predicateSession.RebuildState == RebuildRequired`, call `RebuildDocument()`.
3. Render the document via `ComponentEditDrawer.DrawEditNode(_predicateSession.Document.Root)`.
4. Save Preset → `_predicateSession.ToJson()` written to file via `IFileDialogService.ShowSaveAsDialogAsync`.
5. Load Preset → read file, `_predicateSession.LoadJson(json); MarkStructuralChange(); RebuildDocument();`.

**Success conditions**:
- [ ] SR-T29 passes.
- [ ] Round-trip test through real `EditDocumentJsonSerializer` for a 3-level nested compound query: serialize → reload → re-execute against an `.fdp` fixture → bit-identical `SearchResultDto` sequence vs. the original.

---

### RB-4.9 — Custom `IImGuiFieldDrawer`s

**Scope**: Three custom drawers per [DESIGN.md §6.5](./DESIGN.md#65-structedit-ui-plumbing).

**Reference**:
- `BoundingBoxFieldDrawer` — design-talk.md lines 3574–3613.
- `BehaviorHashFieldDrawer` — design-talk.md lines 4071–4122.
- Filtered-type-combo (`FilteredTypeComboFieldDrawer`) — design-talk.md lines 3392–3445 (event variant) + 3978–4028 (component variant). Lift either; generalize on `IReadOnlyList<Type>` source.

**Concrete steps**:
1. `BoundingBoxFieldDrawer` (TargetType `BoundingBox2D`) renders `DragFloat2 Min/Max` + `Pick Area` button calling `ISpatialPickerContext.RequestBoundingBoxPick(jsonPath)`. On `TryConsumeBoundingBoxPick`, writes back the value.
2. `BehaviorHashFieldDrawer` (TargetType `int`) only activates when the field carries `[BehaviorHashPicker]`. Caches `BehaviorRegistry.GetRegisteredNames()` once. Combo with embedded `InputTextWithHint` filter.
3. `FilteredTypeComboFieldDrawer` for `Type` fields, with mode-switched cache (`ComponentTypeRegistry.GetAllRegistered()` or `EventType.GetAllRegistered()`). Embedded `InputTextWithHint` first item; `OrdinalIgnoreCase` filtering.

**Success conditions**:
- [ ] SR-T32 (bounding box write-back) passes.
- [ ] SR-T33 (behavior hash round-trip) passes.
- [ ] A presentation test for the filtered-type combo: with cached list `[A_Comp, AB_Comp, B_Comp]` and filter `"ab"`, only `AB_Comp` is rendered; with empty filter, all three are rendered; `SetItemDefaultFocus` is called on the currently selected entry.

---

### RB-4.10 — `ReplaySearchPanel` (all five modes)

**Scope**: Per [DESIGN.md §6.7](./DESIGN.md#67-search-window--five-mode-layout-preserve-every-wireframe).

**Reference**: Every wireframe in design-talk.md — lines 2643–2671 (component), 2868–2887 (lifecycle), 3217–3243 (event), 3331–3350 (event occurrence), 3768–3791 (spatial), 3905–3929 (structural), 4452–4489 (nested compound). All wireframes are normative — **do not simplify**.

**Concrete steps**:
1. Place at `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs`.
2. **Constructor signature (decoupling contract, DESIGN.md §6.7.3)**: `ReplaySearchPanel(IComponentEditService editService, IRecordingSearchService searchService, Action<int> onSeekRequested, Action<Entity> onEntitySelected)`. The panel **must not** reference `PlaybackHistoryTracker` or `EntitySelectionHistory` directly. It invokes the raw delegates on every click. History wiring is the composition root's job (RB-2.6).
3. Top: radio bar `Search Mode: ( ) Component  ( ) Event  ( ) Lifecycle  ( ) Spatial  ( ) Structural  ( ) Compound`. Switching the mode swaps the `_currentPredicate` to the matching DTO and rebuilds the session.
4. Below the mode radio: `Save Preset...` / `Load Preset...` toolbar.
5. Below: the StructEdit-rendered criteria region. **For the Compound mode the panel calls `_componentEditDrawer.DrawEditNode(_predicateSession.Document.Root)` exactly once and does NOT write any custom `ImGui.TreeNodeEx`/`ImGui.Indent`/`[+ Add]`/`[X]` logic** — all that comes from `DynamicArrayBinding` (DESIGN.md §6.7.1 binding contract).
6. Below: `Execute Search` button + status line.
7. Below: the appropriate results grid for the mode (3-column for component/event/spatial/structural/compound, 4-column for lifecycle).
8. Result cells `Frame N` and `[i, vN]` are deep-link buttons — invoke `_onSeekRequested(N)` and `_onEntitySelected(entity)` directly.
9. Background search uses `Task.Run`; while running, show `"Scanning frames..."` and disable the Execute button.

**Success conditions**:
- [ ] Snapshot tests for each of the seven embedded wireframes in DESIGN.md §6.7.1 (Component Mutation, Lifecycle, Event property-match, Event pure-occurrence, Spatial, Structural with `Authority Req.` row, Compound nested) verify presence of every labeled row, column header, and button shown in the wireframe — including the `Authority Req.` row in Structural and the `[+ Add]`/`[X]` controls in the Compound tree.
- [ ] Reflection assertion: `typeof(ReplaySearchPanel).GetFields(NonPublic|Instance)` contains no field whose declared type is `PlaybackHistoryTracker` or `EntitySelectionHistory`. Compound layout test verifies no `ImGui.TreeNodeEx`/`ImGui.Indent` is called from `ReplaySearchPanel` itself (counted via a spy on the mock ImGui surface) — every tree node originates from `ComponentEditDrawer`.
- [ ] Switching modes preserves `Save Preset...`/`Load Preset...` state but rebuilds the criteria session (verified by spying on `_predicateSession`).
- [ ] Clicking `Frame N` invokes `_onSeekRequested(N)` exactly once; clicking `[i, vN]` invokes `_onEntitySelected(Entity(i,N))` exactly once (covered by SR-T39).
- [ ] EOF row in lifecycle mode renders plain `[EOF]` text (not a button) when `EndFrame == playback.TotalFrames`.
- [ ] SR-T39 passes (panel constructed with stub spy delegates, no history-tracker instance involved).

---

### RB-4.11 — Stage 4 Final Gate

**Success conditions**:
- [ ] All SR-T* tests pass.
- [ ] All six search wireframes render verbatim per DESIGN.md §6.7 / design-talk.md.
- [ ] Manual smoke: open a 10k-frame `.fdp` fixture; run each of the six search modes; click results to verify deep-linking; save/load a 3-level compound preset.

---

## Stage 5 — Global Registration

> See [DESIGN.md §7](./DESIGN.md#7-stage-5--global-registration).

### RB-5.1 — Add `Hrot.ReplayBrowser.csproj` to the ClusterRunner Solution

**Concrete steps**: Reference the new project from the runner's main solution; do not modify `LocalWindowController.OpenLocalWindow` (the subsystem auto-registers its own perspective windows via `IWindowRegistrar`).

**Success conditions**:
- [ ] `dotnet build` of the runner solution succeeds.
- [ ] `dotnet run --project Hrot.ClusterRunner -- -m replaybrowser --headless` (or equivalent) initializes the subsystem and exits cleanly.
- [ ] `Hrot.ClusterRunner.Tests` adds a test that asserts `ScanForSubsystems` discovers the new subsystem.

---

### RB-5.2 — End-to-End Manual Smoke

**Success conditions**:
- [ ] `dotnet run --project Hrot.ClusterRunner -- -m replaybrowser` launches the GUI in the ReplayBrowser perspective.
- [ ] All five windows are docked per DESIGN.md §4.3.
- [ ] Loading an `.fdp` file scrubs through frames; the inspector, event browser, diff viewer, and search window all function.
- [ ] Switching to any other perspective and back preserves the dock layout via ImGui `.ini`.
- [ ] The `Save to JSON…` button exports without UI freeze.

---

## Cross-Stage / Continuous

### RB-X.1 — Documentation Hygiene

**Success conditions**:
- [ ] Each merged task adds or updates an entry in `TASK-TRACKER.md` and (if applicable) the technical-debt tracker (`DEBT-TRACKER.md`).
- [ ] If the developer discovers a deviation from DESIGN.md during implementation, the deviation is recorded as a `DEBT-*` row (priority assigned) before merge.

### RB-X.2 — Style and Allocation Audits

**Success conditions**:
- [ ] No file outside `FDP/Engine/Fdp.Presentation/` references `ImGuiApi` or Raylib types (verified via dependency analysis on the `Fdp.Toolkits/ReplayBrowser` assembly graph).
- [ ] Backend test allocation budgets specified in DESIGN.md tables (DIF-T09, SR-T08, EX-T25, SR-T34) are checked in CI.
