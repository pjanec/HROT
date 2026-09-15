# FDP Replay Browser — Design

## 0. Purpose

Build tooling that lets a developer diagnose `.fdp` flight recordings interactively. Two artifacts:

1. **Headless JSON dumper / search engine** — usable from CLI and from background tasks in the GUI. Streams arbitrarily large recordings.
2. **`ReplayBrowser` cluster-runner subsystem** — a perspective-bound ImGui environment with its own isolated ECS sandbox, its own `MapCanvas`, and reused live-mode diagnostic panels (entity inspector, event browser).

The implementation order is strictly **backend-first**. Stage 1 (headless export) and Stage 3.A (diff engine) ship with extensive xUnit coverage **before** any ImGui code is written. Stage 5 (search engine) likewise lands as a headless service with full coverage before its UI panel.

> The complete design talk is in [design-talk.md](./design-talk.md). When this document is silent on a detail, that file is the source of truth. **UI layouts in the design talk are normative** — do not simplify.

---

## 1. Architectural Overview

```
+------------------------------------------------------------------+
|                      Hrot.ClusterRunner                          |
|   ScanForSubsystems  ─► picks up "ReplayBrowserSubsystem"        |
|                       │  via "-m replaybrowser" CLI arg          |
+-----------------------│------------------------------------------+
                        ▼
+------------------------------------------------------------------+
|        Hrot.ReplayBrowser   (subsystem assembly)                 |
|  ┌────────────────────────────────────────────────────────────┐  |
|  │ ReplayBrowserSubsystem                                      │ |
|  │   owns ReplayBrowserContext + MapCanvas                     │ |
|  │   registers 5 PerspectiveBound windows via IWindowRegistrar │ |
|  │     - ReplayTimelineWindow                                  │ |
|  │     - FdpEntityInspectorWindow  (reuses EntityInspectorPanel)│|
|  │     - ComponentDiffWindow                                   │ |
|  │     - FdpEventBrowserWindow     (reuses EventBrowserPanel)  │ |
|  │     - ReplaySearchWindow                                    │ |
|  └────────────────────────────────────────────────────────────┘  |
+-----------------------│------------------------------------------+
                        ▼ depends on
+------------------------------------------------------------------+
|   Fdp.Toolkits/ReplayBrowser/  (headless, no ImGui, no Raylib)   |
|     - JsonExportOptions, ChangelogEntryDto, ExportWindowMode     |
|     - IRecordingExportService / RecordingExportService           |
|     - ReplayBrowserContext (EntityRepository+FdpEventBus+        |
|       PlaybackController+DiagnosticEventHistoryService sandbox)  |
|     - DiffNode tree + IComponentDiffService                      |
|     - Search domain (SearchPredicateDto and concrete predicates) |
|     - IPropertyEvaluator, IPredicateCompiler, IEventScannerCompiler|
|     - IRecordingSearchService / RecordingSearchService           |
+-----------------------│------------------------------------------+
                        ▼ consumed by
+------------------------------------------------------------------+
|   FDP/Tools/Fdp.Tools.RecordingDumper  (.NET 8 console exe)      |
|     CommandLine parsing → JsonExportOptions → RecordingExportService|
+------------------------------------------------------------------+
```

### Isolation invariants

* The `ReplayBrowserContext` owns dedicated `EntityRepository`, `FdpEventBus`, `DiagnosticEventHistoryService` and `PlaybackController`. No reference to any live cluster repository ever crosses the boundary.
* The subsystem **does not** implement `IMapCameraProvider`. Switching perspectives must not synchronize cameras with `SimHost`/`IG`/`ExCon`.
* `RecordingExportService` and `RecordingSearchService` build their own `ReplayBrowserContext` instances internally so the GUI's live sandbox timeline state is never disturbed by an export or search.

### Verified codebase anchors

Names below were confirmed in the repo before this design was finalized. Paths are anchors, not duplications.

| Used by design | Verified location |
|---|---|
| `RecordingGlobalHeader`, `FrameOuterHeader`, `RecordingReader` | `FDP/Engine/Fdp.Core/FlightRecorder/` |
| `PlaybackController`, `PlaybackSystem.ApplyFrame`, `RecorderSystem` | `FDP/Engine/Fdp.Core/FlightRecorder/` |
| `FdpConfig.FORMAT_VERSION` = `4` | `FDP/Engine/Fdp.Core/FdpConfig.cs` |
| `EntityRepository`, `EntityHeader` (96 B, `ComponentMask`/`AuthorityMask`/`LastChangeTick`), `BitMask256.IsSet` | `FDP/Engine/Fdp.Core/` |
| `EntityIndex.MaxIssuedIndex` | `FDP/Engine/Fdp.Core/EntityIndex.cs` |
| `GlobalTime.TotalTime`, `GlobalTime.FrameNumber` | `FDP/Engine/Fdp.Core/GlobalTime.cs` |
| `FdpEventBus` (`Read<T>`, `ReadManaged<T>`, `HasEvent(Type)`, `ClearCurrentBuffers`, `InjectIntoCurrentBySize`, `InjectManagedIntoCurrent`) | `FDP/Engine/Fdp.Core/FdpEventBus.cs` |
| `ComponentType<T>.ID` / `ComponentTypeRegistry` | `FDP/Engine/Fdp.Core/ComponentType.cs` |
| `EventType` registry | `FDP/Engine/Fdp.Core/EventType.cs` |
| `ScenarioSerializer` | `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` |
| `HrotScenarioSerializerFactory.Build(BehaviorRegistry)` | `Hrot/Subsystems/Hrot.SimHost/Serializers/HrotScenarioSerializerFactory.cs` |
| `FdpJsonOptionsRegistry` (`Indented`, `DefaultRelaxed`) | `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs` |
| `JsonAestheticFormatter.FlattenNumericArrays` | `FDP/Toolkits/Fdp.Toolkits/Serialization/JsonAestheticFormatter.cs` |
| `DiagnosticGuidResolver` | `FDP/Toolkits/Fdp.Toolkits/Diagnostics/DiagnosticGuidResolver.cs` |
| `InspectorJsonUtils.BuildComponentJson` | `FDP/Engine/Fdp.Presentation/ImGui/Utils/InspectorJsonUtils.cs` |
| `IEntityStateExtractionService`, `EntityJsonDumper`, `FdpAutoSerializer`, `IEntityScenarioTranslator` (`BrainBlackboardTranslator`) | `FDP/Toolkits/Fdp.Toolkits/...`, `Hrot/Subsystems/Hrot.SimHost/Serializers/` |
| `EntityInspectorPanel`, `EventBrowserPanel`, `ComponentReflector`, `ImGuiPropertyTree`, `MessageLogWindow` | `FDP/Engine/Fdp.Presentation/ImGui/...` |
| `IInspectableSession.HasAuthority(Entity, Type)`, `IInspectorContext`, `InspectorState.SelectedEntity`, `RepositoryAdapter` | `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/` |
| `IDiagnosticEventHistoryService.Capture`, `DiagnosticEventHistoryService` | `FDP/Engine/Fdp.Core/Diagnostics/` |
| `IEventStreamInspector.InspectReadBuffer` | `FDP/Engine/Fdp.Core/IEventStreamInspector.cs` |
| `ManagedWindow` (id is **string**), `WindowScope.{Global,PerspectiveBound}`, `WindowManager`, `IWindowRegistrar` | `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/` |
| `LocalWindowController.OpenLocalWindow` | `Hrot/Runner/Hrot.ClusterRunner/Presentation/LocalWindowController.cs` |
| `ISubsystem` (`Initialize(SubsystemConfig)`, `Update`, `DrawWorld`, `DrawUI`, `Shutdown`, `Name`, `TitleBarColor`), `SubsystemOrchestrator`, `ScanForSubsystems` (strips `Subsystem` suffix) | `FDP/Toolkits/Fdp.Toolkits/Runner/`, `Hrot/Runner/Hrot.ClusterRunner/Program.cs` |
| `MapCanvas`, `DebugGizmoLayer`, `GridMapLayer`, `MapCameraView`, `IMapCameraProvider` | `FDP/Engine/Fdp.Presentation/Vis2D/`, `FDP/Toolkits/Fdp.Toolkits/Runner/`, `FDP/Toolkits/Fdp.Toolkits/Vis2D/` |
| `IFileDialogService.ShowSaveAsDialogAsync` | `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IFileDialogService.cs` |
| `IComponentEditService`, `IEditSession`, `ComponentEditDrawer`, `EditNodeKind`, `IValueBinding`, `IEditBuffer`, `IContainerBinding`, `IImGuiFieldDrawer`, `ICustomFieldEditor`, `EditDocumentJsonSerializer` | `FDP/ExtDeps/StructEdit/src/StructEdit.{Core,Json}/`, `FDP/Engine/Fdp.Presentation/ImGui/Editing/` |
| `IEntityStatefulGizmo` (`RequiresExclusiveFocus`, `WantsRawInput`, `OnMouseEvent`, `OnDragUpdate`, `OnKeyEvent`), `GizmoInteractionManager`, `GlobalGizmoManager`, `IDebugDrawBuilder`, `DebugPrimitive`, `PipelineTarget`, `Rgba32`, `MapMouseButton`, `MapKeyboardKey`, `MapPickableWorldLocationAttribute`, `IComponentPickerContext` | `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`, `FDP/Diagnostics/Fdp.Diagnostics.Contracts/`, `FDP/ExtDeps/GizmoMap/`, `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/`, `FDP/Engine/Fdp.Presentation/ImGui/Editing/`, `FDP/Toolkits/Fdp.Toolkits/Behavior/Attributes/` |
| `BehaviorRegistry` (`GetRegisteredNames`, `TryGetName`, `TryGetId`) | `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs` |
| `CommandLine` arg parser usage pattern | `FDP/Examples/Fdp.Examples.Runner/Program.cs` |

> **Open verification items** carried into Stage 1 implementation work, *not* into design ambiguity:
> * Exact `ComponentTypeRegistry` enumeration API (`GetOrRegister<T>` confirmed; the design assumes a `GetAllRegistered()`/`GetAllTypes()` enumerator exists or will be added — this is a small addition and recorded as task `RB-1.0`).
> * `EntityRepository.HasComponentByTypeId(entity, int typeId)` — design relies on this; if missing, the equivalent `header.ComponentMask.IsSet(typeId)` is the fallback (also `RB-1.0`).
> * The exact field names of `BehaviorState.ActiveBehaviorHash`, `NavigationStatus.Result`, `EntityInfo.Name`, `NetworkIdentity.Value` — searched paths are nominal, written as user-supplied `PropertyPath` strings into search predicates, so no design coupling.

---

## 2. Repository Layout

| Concern | Path |
|---|---|
| Shared headless replay/export/diff/search code | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/` |
| Tests for shared headless code | `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/` |
| Console dump tool (Stage 1) | `FDP/Tools/Fdp.Tools.RecordingDumper/` (new `.csproj`) |
| Console dump tool tests | `FDP/Tools/Fdp.Tools.RecordingDumper.Tests/` (new `.csproj`) |
| ImGui panels (diff, search, history) | `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/` |
| ImGui utilities (entity-link, filtered combo) | `FDP/Engine/Fdp.Presentation/ImGui/Utils/ReplayBrowser/` |
| ImGui windows | `FDP/Engine/Fdp.Presentation/ImGui/Windows/ReplayBrowser/` |
| Bounding-box picker gizmo + drawer | `FDP/Engine/Fdp.Presentation/ImGui/Editing/ReplayBrowser/` + `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/ReplayBrowser/` |
| Presentation-layer tests | `FDP/Engine/Fdp.Presentation.Tests/ReplayBrowser/` |
| ReplayBrowser subsystem | `Hrot/Subsystems/Hrot.ReplayBrowser/` (new `.csproj`) |
| Subsystem tests | `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/` (new `.csproj`) |

---

## 3. Stage 1 — Headless JSON Export Pipeline

### 3.1 Output JSON schema

All numeric arrays for `Vector3`/`Quaternion` shapes are flattened via `JsonAestheticFormatter.FlattenNumericArrays`. Entity cross-references inside payloads are formatted by `DiagnosticGuidResolver`, yielding `"[Index, vGeneration]"` strings.

```json
{
  "Header": {
    "Magic": "FDPREC",
    "FormatVersion": 4,
    "Timestamp": 1684070000
  },
  "Frames": [
    {
      "FrameHeader": {
        "FileFrameOrdinal": 0,
        "SimFrameNumber": 5678,
        "Tick": 1234,
        "FrameType": "Keyframe",
        "WallClockTicks": 638123456789,
        "RelativeWallTimeSec": 2.345,
        "SimTimeSec": 20.550,
        "CompressedSize": 1024,
        "UncompressedSize": 4096
      },
      "DestroyedEntities": [],
      "Entities": [
        {
          "EntityId": [1, 10],
          "Components": [
            {
              "ComponentType": "SimTransform",
              "HasAuthority": true,
              "Payload": {
                "Position": [100.0, 50.0, 0.0],
                "Rotation": [0.0, 0.0, 0.0, 1.0]
              }
            },
            {
              "ComponentType": "NetworkIdentity",
              "HasAuthority": false,
              "Payload": { "Value": 1000 }
            }
          ]
        }
      ],
      "Events": [
        {
          "EventType": "WeaponFireNotification",
          "IsManaged": false,
          "Payload": {
            "Shooter": "[42, v1]",
            "Target": "[43, v1]",
            "WeaponIndex": 0,
            "IsRemote": false
          }
        }
      ]
    }
  ]
}
```

Rules:

* `FrameType` ∈ `"Keyframe" | "Delta"`. Keyframes omit `DestroyedEntities` entirely (or emit `[]`).
* `Components` is a **list of objects** (not a dict) so each entry carries `ComponentType`, `HasAuthority`, and `Payload`.
* `FileFrameOrdinal` is the 0-based ordinal **in the file**; `SimFrameNumber` is `GlobalTime.FrameNumber`; `Tick` is the raw `FrameOuterHeader.Tick` (= `EntityRepository.GlobalVersion`).
* `RelativeWallTimeSec = (frame.WallClockTicks - firstFrame.WallClockTicks) / TimeSpan.TicksPerSecond`.
* `SimTimeSec = GlobalTime.TotalTime` queried via `repo.GetSingletonUnmanaged<GlobalTime>()` after `ApplyFrame`. Guarded by `HasSingletonUnmanaged<GlobalTime>()`.

### 3.2 Domain models (`Fdp.Toolkits/ReplayBrowser/`)

```csharp
public enum ExportWindowMode { FullFile, ByFrame, ByTime }
public enum ExportFormatMode { AbsoluteState, Changelog }

public sealed class JsonExportOptions
{
    public ExportWindowMode WindowMode = ExportWindowMode.FullFile;
    public ExportFormatMode FormatMode = ExportFormatMode.AbsoluteState;

    public int StartFrame = 0;
    public int EndFrame = int.MaxValue;
    public float StartTimeSec = 0f;
    public float EndTimeSec = float.PositiveInfinity;

    public bool FilterBySelection = false;
    public List<Entity> TargetEntities = new();
    public bool FilterByEntityIndex = false;   // CLI --entity-id
    public int TargetEntityIndex = -1;

    public bool IncludeEntities = true;
    public bool IncludeEvents = true;
    public bool Minified = false;
    public double EpsilonTolerance = 0.001;    // used by Changelog mode
}

public sealed record ChangelogEntryDto(
    int FrameIndex,
    long WallClockTicks,
    double RelativeWallTimeSec,
    double SimTimeSec,
    string EntityHandle,
    IReadOnlyList<DiffNode> Mutations);
```

### 3.3 Service contract

```csharp
public interface IRecordingExportService
{
    /// <summary>
    /// Streams an .fdp recording to <paramref name="outputJsonPath"/> using
    /// <paramref name="options"/>. Allocation-isolated; uses its own ReplayBrowserContext.
    /// </summary>
    void ExportToJson(string inputFdpPath, string outputJsonPath, JsonExportOptions options);
}
```

### 3.4 Pipeline (algorithm)

1. Open the recording with a fresh `PlaybackController(fdpPath)`. Read its `FormatVersion` (must equal `FdpConfig.FORMAT_VERSION = 4`), `RecordingTimestamp`, and `TotalFrames`.
2. Allocate sandbox `EntityRepository`, `FdpEventBus`. Create `Utf8JsonWriter(FileStream, new JsonWriterOptions { Indented = !options.Minified })`.
3. **Header** — emit `{Magic: "FDPREC", FormatVersion, Timestamp}` from `RecordingGlobalHeader`.
4. **Seek-to-start window**:
   * `FullFile` → no-op; first `StepForward` produces frame 0.
   * `ByFrame` → `playback.SeekToFrame(repo, options.StartFrame)`.
   * `ByTime` → capture `startWallTicks = playback.GetFrameMetadata(0).WallClockTicks` first; compute `targetStartTicks = startWallTicks + (long)(options.StartTimeSec * TimeSpan.TicksPerSecond)`; call `playback.SeekToWallClockTicks(repo, targetStartTicks)`.
5. **Frame loop** — `while (playback.StepForward(repo))`:
   1. Compute `currentFrame = playback.CurrentFrame`, `meta = playback.GetFrameMetadata(currentFrame)`.
   2. End-window check (frame-based or time-based) → `break`.
   3. After ApplyFrame: capture history via `historyService.Capture("Export", bus, (uint)currentFrame)` (only when events will be emitted).
   4. Read `GlobalTime` via `HasSingletonUnmanaged<GlobalTime>` then `GetSingletonUnmanaged<GlobalTime>()` for `SimFrameNumber` and `SimTimeSec`.
   5. Emit `FrameHeader` (all 9 fields).
   6. Emit `DestroyedEntities` on delta frames using `repo.GetDestructionLog()` then `repo.ClearDestructionLog()`.
   7. If `IncludeEntities`:
      * `AbsoluteState` mode: enumerate active entities (filtered by `TargetEntities`/`TargetEntityIndex` if set). For each, iterate `header.ComponentMask` bits, resolve type via `ComponentTypeRegistry`, call `IInspectableSession.HasAuthority(entity, type)` (via `RepositoryAdapter`), and serialize the payload through `ScenarioSerializer` built by `HrotScenarioSerializerFactory.Build(behaviorRegistry)`. Custom translators (`IEntityScenarioTranslator`) and `FdpAutoSerializer` fallback are used automatically. Output is run through `JsonAestheticFormatter.FlattenNumericArrays` before emission.
      * `Changelog` mode: see §3.6 below.
   8. If `IncludeEvents`: read unmanaged streams via `IEventStreamInspector.InspectReadBuffer()` over the active stream set, and read managed streams via `bus.ReadManaged<T>()` enumeration on registered types. Each emission tags `IsManaged` correctly.
6. Close JSON array, dispose context.

`FdpJsonOptionsRegistry.Indented` is the default; `FdpJsonOptionsRegistry.DefaultRelaxed` when `options.Minified`.

### 3.5 Filtering / windowing rules (single source of truth)

| Option | Effect |
|---|---|
| `WindowMode == FullFile` | iterate entire file |
| `WindowMode == ByFrame` | `SeekToFrame(StartFrame)` then break when `currentFrame > EndFrame` |
| `WindowMode == ByTime` | seek by wall-clock; break when `meta.WallClockTicks > targetEndTicks` |
| `FilterBySelection == true` | restrict per-frame `Entities` to `TargetEntities` (still emits `DestroyedEntities` filtered by this set) |
| `FilterByEntityIndex == true` | restrict per-frame `Entities` to a single ECS index |
| `IncludeEntities == false` | omit `Entities`, `DestroyedEntities` blocks |
| `IncludeEvents == false` | omit `Events` block; skip `historyService.Capture` call |

`ByFrame` and `ByTime` are mutually exclusive at the CLI layer (validation error) but the in-process API trusts the caller's `WindowMode`.

### 3.6 Changelog mode

Per-frame, for each target entity in `options.TargetEntities`:

1. If `!repo.IsAlive(entity)` → set baseline to `null` and skip.
2. Serialize the entity post-step to a `JsonNode` tree (`ScenarioSerializer` output, projected via `InspectorJsonUtils.BuildComponentJson`).
3. Compute `IComponentDiffService.ComputeTreeDiff(baseline, current, options.EpsilonTolerance)`.
4. If the result tree contains modified leaves, emit a `ChangelogEntryDto` to the root JSON array using `JsonSerializer.Serialize(writer, entry, FdpJsonOptionsRegistry.Indented/DefaultRelaxed)`.
5. Update `baselines[entity] = current`.

Memory is `O(N_targets)` regardless of total frames.

### 3.7 CLI (`Fdp.Tools.RecordingDumper`)

Argument parsing uses the same `CommandLine` package pattern as `FDP/Examples/Fdp.Examples.Runner/Program.cs`.

| Switch | Aliases | Maps to |
|---|---|---|
| `--input <path>` | `-i` | input `.fdp` path (required) |
| `--output <path>` | `-o` | output `.json` path (required) |
| `--start-frame <int>` | `-s` | `WindowMode = ByFrame`, `StartFrame` |
| `--end-frame <int>` | `-e` | `WindowMode = ByFrame`, `EndFrame` |
| `--start-time <sec>` | `-t` | `WindowMode = ByTime`, `StartTimeSec` |
| `--end-time <sec>` | `-u` | `WindowMode = ByTime`, `EndTimeSec` |
| `--entity-id <index>` | | `FilterByEntityIndex = true`, `TargetEntityIndex` |
| `--no-events` | | `IncludeEvents = false` |
| `--no-entities` | | `IncludeEntities = false` |
| `--minified` | | `Minified = true` |
| `--changelog` | | `FormatMode = Changelog` (requires `--entity-id` or a comma-separated `--entities`) |
| `--epsilon <double>` | | `EpsilonTolerance` |

Mutual exclusion of frame-based vs time-based windowing is enforced at CLI parse time.

Exit codes: `0` success, `1` argument validation failure, `2` file-not-found, `3` runtime extraction error (with stack trace to stderr).

### 3.8 Backend tests for Stage 1 — *non-negotiable, comprehensive*

Test project: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/` plus `FDP/Tools/Fdp.Tools.RecordingDumper.Tests/`.

A reusable in-test recording-builder (`FdpRecordingHarness`) is required: it instantiates an `EntityRepository`, a `RecorderSystem`, and a temp file `Stream`, lets the test spawn entities, mutate components, fire events, tick, and produce a real `.fdp` file. This guarantees tests use the *actual* on-disk format.

| ID | What it asserts |
|---|---|
| EX-T01 | `RecordingExportService` can be constructed in isolation; no presentation or Raylib dependency in its assembly graph. |
| EX-T02 | Round-trip: harness records 1 keyframe + 3 deltas with 5 entities + 2 unmanaged event types + 1 managed event type. Export produces JSON whose `Header.Magic == "FDPREC"`, `FormatVersion == FdpConfig.FORMAT_VERSION`, and `Frames.Length == 4`. |
| EX-T03 | First frame is `"Keyframe"` and has no `DestroyedEntities` key (or empty array). |
| EX-T04 | Delta frames carry `DestroyedEntities` populated with the exact `[Index, vGen]` strings of entities destroyed that tick. |
| EX-T05 | Each `Components` entry is an **object** with `ComponentType`/`HasAuthority`/`Payload` keys (not a flat dict). |
| EX-T06 | `HasAuthority` reflects `IInspectableSession.HasAuthority`: assert both `true` and `false` cases in the same fixture. |
| EX-T07 | `RelativeWallTimeSec` is zero on frame 0 and strictly monotone non-decreasing thereafter; matches `(wall - firstWall)/TimeSpan.TicksPerSecond` to within 1e-9. |
| EX-T08 | `SimTimeSec` matches `GlobalTime.TotalTime` extracted directly from the same recording (sandbox parity). |
| EX-T09 | `SimFrameNumber` matches `GlobalTime.FrameNumber`. |
| EX-T10 | `FileFrameOrdinal` is dense 0..N-1 across emitted frames after windowing. |
| EX-T11 | `Tick` equals `FrameOuterHeader.Tick` for every frame (== `EntityRepository.GlobalVersion`). |
| EX-T12 | `ByFrame` windowing: setting `StartFrame=2, EndFrame=3` on a 5-frame recording emits exactly those two frames. |
| EX-T13 | `ByTime` windowing: setting `StartTimeSec=0.5, EndTimeSec=1.0` emits only frames whose relative wall time falls in `[0.5, 1.0]`. |
| EX-T14 | `ByTime` with start past EOF emits empty `Frames` array and writes a valid empty JSON document. |
| EX-T15 | `FilterByEntityIndex` restricts `Entities` per frame to the target index. `DestroyedEntities` mirrors the same filter. |
| EX-T16 | `FilterBySelection` with `TargetEntities = [a, b]` emits only those entities. |
| EX-T17 | `IncludeEvents=false` omits `Events` keys; `DiagnosticEventHistoryService.Capture` is not invoked (verified via test double). |
| EX-T18 | `IncludeEntities=false` omits `Entities` and `DestroyedEntities` keys but preserves `FrameHeader` and `Events`. |
| EX-T19 | `Minified=true` uses `FdpJsonOptionsRegistry.DefaultRelaxed`; produced JSON does not contain `\n` between top-level object members. |
| EX-T20 | Numeric `Vector3`/`Quaternion` payloads are flattened into single-line arrays of length 3 / 4. |
| EX-T21 | Entity cross-references inside event payloads render as `"[Index, vGen]"` strings, not as raw guids/longs. |
| EX-T22 | Custom `IEntityScenarioTranslator` (a stub `FooBlackboardTranslator`) is honored; its projected DTO appears under `Payload`. |
| EX-T23 | Managed events: a registered managed event type is emitted with `IsManaged: true` and its payload is correctly serialized. |
| EX-T24 | Unmanaged event with no payload still emits an entry with `IsManaged: false`. |
| EX-T25 | A test invokes the export against a 10k-frame harness recording and asserts peak managed heap delta `< 32 MB` (`GC.GetTotalMemory(true)` before/after, with `Utf8JsonWriter` streaming). |
| EX-T26 | The export does **not** mutate the GUI's live sandbox: a parallel `ReplayBrowserContext` is loaded and seeked to frame 7; running an export of the same file leaves that context's `CurrentFrame` and `GlobalVersion` unchanged (proves the service builds its own sandbox). |
| EX-T27 | Changelog mode: harness mutates `SimTransform.Position` on entity A on ticks 2, 4, 5. Export with `FormatMode=Changelog`, target `[A]`, emits 3 entries with correct `FrameIndex` and only the modified leaves. |
| EX-T28 | Changelog `EpsilonTolerance`: subpixel mutation < epsilon is suppressed; mutation ≥ epsilon is emitted. |
| EX-T29 | Changelog when target entity is destroyed mid-recording: baseline resets to `null`, no spurious entries after destruction. |
| EX-T30 | CLI parse: every documented switch round-trips into the right `JsonExportOptions` field. |
| EX-T31 | CLI parse: simultaneous `--start-frame` + `--start-time` returns exit code 1 with a clear error message. |
| EX-T32 | CLI integration: invoking `Fdp.Tools.RecordingDumper -i fixture.fdp -o out.json --minified --no-events` produces the same JSON as calling the service directly with the equivalent options. |

### 3.9 Stage 1 Definition of Done

* All EX-T01..EX-T32 pass in CI.
* `Fdp.Tools.RecordingDumper` builds as a standalone console app with no transitive reference to `Fdp.Presentation` or any subsystem package (validated by an assembly-reference test).
* A handcrafted 100 MB recording fixture exports without OOM and in linear time.

---

## 4. Stage 2 — Replay Browser Subsystem Foundation

### 4.1 Subsystem

```csharp
public sealed class ReplayBrowserSubsystem : ISubsystem, IWindowRegistrar
{
    public string Name => "ReplayBrowser";          // CLI: -m replaybrowser
    public Vector4 TitleBarColor => new(0.2f, 0.6f, 0.8f, 1f); // distinct blue

    private ReplayBrowserContext _context = null!;
    private MapCanvas _canvas = null!;
    private EntitySelectionHistory _entityHistory = null!;
    private PlaybackHistoryTracker _playbackHistory = null!;
    private bool _headless;

    // Reused live-mode panels (one instance each)
    private ReplayTimelinePanel _timelinePanel = null!;
    private ComponentDiffPanel  _diffPanel = null!;
    private EntityInspectorPanel _inspectorPanel = null!;
    private EventBrowserPanel _eventPanel = null!;
    private ReplaySearchPanel _searchPanel = null!;

    public void Initialize(SubsystemConfig config) { ... }
    public void Update(float dt)                   { ... }
    public void DrawWorld()                        { if (!_headless) _canvas.Draw(); }
    public void DrawUI()                           { /* WindowManager renders the windows */ }
    public void Shutdown()                         { _context?.Dispose(); }

    public void RegisterWindows(WindowManager wm)  { /* see §4.4 */ }
}
```

Crucially, it **does not** implement `IMapCameraProvider`. `SubsystemOrchestrator` therefore will not synchronize cameras with other perspectives, satisfying the strict-isolation requirement.

CLI integration is automatic: `Hrot.ClusterRunner.Program.ScanForSubsystems` reflects all non-abstract `ISubsystem` types and strips the `Subsystem` suffix; `-m replaybrowser` activates this subsystem in the runner.

### 4.2 Sandbox context

```csharp
public sealed class ReplayBrowserContext : IDisposable
{
    public EntityRepository SandboxRepo { get; }
    public FdpEventBus SandboxBus { get; }
    public IDiagnosticEventHistoryService HistoryService { get; }
    public PlaybackController? Playback { get; private set; }
    public IInspectableSession Session { get; }   // RepositoryAdapter(SandboxRepo)
    public InspectorState InspectorState { get; }
    public IComponentDiffService DiffService { get; }
    public string? CurrentFdpPath { get; private set; }
    public int CurrentFrame => Playback?.CurrentFrame ?? -1;

    public ReplayBrowserContext(IComponentDiffService diffService) { ... }
    public void LoadRecording(string fdpPath) { ... }
    public void SeekToFrame(int frameIndex) { ... }    // ClearCurrentBuffers + SeekToFrame + Capture
    public void StepForward() { ... }
    public void StepBackward() { ... }
    public void Dispose() { ... }
}
```

`SeekToFrame` and `StepForward` always:
1. `SandboxBus.ClearCurrentBuffers()`
2. `Playback.SeekToFrame(SandboxRepo, frameIndex)` or `StepForward(SandboxRepo)`
3. `HistoryService.Capture("Replay", SandboxBus, (uint)CurrentFrame)` so the `EventBrowserPanel` sees the frame's transient events.

### 4.3 Window layout (binding to `WindowScope.PerspectiveBound`)

Identical to the design talk wireframe — preserve it:

```
+-----------------------------------------------------------------------+
| Main Menu Bar (Perspective Switcher, Global Tools)                    |
+-------------------+-----------------------------------+---------------+
| LEFT DOCK         | CENTRAL DOCK (Passthru)           | RIGHT DOCK    |
|                   |                                   |               |
| [Replay Entity    |                                   | [Frame Diff   |
|  Inspector]       |                                   |  Viewer]      |
|                   |        Raylib MapCanvas           |               |
| - Entity Search   |        (Isolated Sandbox)         | - Hierarchical|
| - Entity List     |                                   |   Change Tree |
| - Component       |                                   |               |
|   Reflector Tree  |                                   |---------------+
|                   |                                   | [Replay Event |
|                   |                                   |  Browser]     |
|                   |                                   |               |
|                   |                                   | - Event List  |
|                   |                                   | - Payload     |
+-------------------+-----------------------------------+---------------+
| BOTTOM DOCK                                                           |
| [Replay Timeline]                                                     |
| [|<] [<] [||] [>] [>|]  Timeline: [========O-----------------------]  |
| Meta: Tick 4567 | SimFrame 1200 | SimTime 20.55s | Delta | 1024 Bytes |
| > JSON Export Options (Expander)                                      |
+-----------------------------------------------------------------------+
| Global Status Bar                                                     |
+-----------------------------------------------------------------------+
```

The Replay Search window is the fifth window. It is opened from the timeline's "Search…" button or the perspective menu and docks anywhere the user chooses; ImGui's `.ini` saves it per-perspective.

### 4.4 Windows

All four windows wrap a single panel each, all use `WindowScope.PerspectiveBound`, all tagged with `owningPerspective = "ReplayBrowser"`, all start with `IsOpen = true`, all share `TitleBarColor` for visual cohesion.

```csharp
internal sealed class ReplayTimelineWindow : ManagedWindow {
    public ReplayTimelineWindow(string id, string title, string perspective,
        ReplayTimelinePanel panel, Vector4 color)
        : base(id, title, perspective, WindowScope.PerspectiveBound)
    { _panel = panel; TitleBarColor = color; IsOpen = true; }
    protected override void DrawClientArea() => _panel.DrawContent();
}
// FdpEntityInspectorWindow, ComponentDiffWindow, FdpEventBrowserWindow, ReplaySearchWindow follow the same pattern.
```

The `FdpEntityInspectorWindow` injects `() => new RepositoryAdapter(_context.SandboxRepo)` and a getter for the local `InspectorState`, ensuring the live editor's `InspectorState` is never touched.

### 4.5 Timeline panel — full layout per design talk

```
[Replay Timeline]
+-----------------------------------------------------------------------+
| [<- Back] [Fwd ->]   |   [|< Rewind]  [< Step Back]  [Step Forward >] |
| [|| Pause / Play >]                                                   |
| Timeline:  [========O-----------------------]   Frame 1234 / 5000     |
| Meta: Tick 4567 | SimFrame 1200 | SimTime 20.55s | Delta | 1024 B     |
| Load .fdp...   File: capture_01.fdp                                   |
| > JSON Export Options (Expander, persistent state JsonExportOptions)  |
|   - Export Range:  (•) Full File   ( ) By Frame   ( ) By Time         |
|     Frame inputs (disabled unless ByFrame): Start [..] End [..]       |
|     Time  inputs (disabled unless ByTime):  Start [..] End [..]       |
|   - Format:        (•) Absolute   ( ) Changelog                       |
|   - Filters: [ ] Filter by Entity ID  Index [..]                      |
|              [ ] Use Inspector Selection                              |
|   - Payload: [x] Include Entities  [x] Include Events                 |
|   - Output:  [ ] Minified                                             |
|   - Epsilon (changelog only): [ 0.001 ]                               |
|   - [ Save to JSON... ]                                               |
+-----------------------------------------------------------------------+
```

* `<- Back` / `Fwd ->` invoke `PlaybackHistoryTracker.GoBack/GoForward` (cyan-tinted to distinguish from frame-step buttons).
* `[Save to JSON...]` snapshots a clone of the options DTO, opens an async file dialog, and runs `IRecordingExportService.ExportToJson` on `Task.Factory.StartNew(..., LongRunning)`. The active context's `CurrentFrame` is **not** disturbed.

### 4.6 Histories (Stage 2 deliverable)

```csharp
public sealed class EntitySelectionHistory {
    public bool CanGoBack { get; }
    public bool CanGoForward { get; }
    public event Action<Entity>? OnSelectionChanged;
    public void PushSelection(Entity e);
    public void GoBack();
    public void GoForward();
}

public sealed class PlaybackHistoryTracker {
    public bool CanGoBack { get; }
    public bool CanGoForward { get; }
    public event Action<int>? OnSeekRequested;
    public void PushFrame(int frameIndex);
    public void GoBack();
    public void GoForward();
}
```

Both implement the "navigating ≠ recording" invariant: while `GoBack`/`GoForward` is running, `PushSelection`/`PushFrame` no-ops. Forward stacks are truncated on a new push after a back.

The subsystem wires:
* `_entityHistory.OnSelectionChanged += e => _context.InspectorState.SelectedEntity = e;`
* `_playbackHistory.OnSeekRequested += f => _context.SeekToFrame(f);`
* `_inspectorPanel.OnEntitySelected = _entityHistory.PushSelection;`
* `_diffPanel.OnEntityLinkClicked = _entityHistory.PushSelection;`
* `_eventPanel.OnEntityLinkClicked = _entityHistory.PushSelection;`
* Causality menu in event browser → "Step Forward and Diff Target": pushes pre-jump frame, calls `_context.StepForward()`, pushes post-jump frame, calls `_entityHistory.PushSelection(target)`.

The `EntityInspectorPanel` toolbar gains Back/Forward arrow buttons (`history.CanGoBack` gates them).

### 4.7 Entity deep-link primitive

```csharp
public static class ImGuiEntityLink {
    public static bool Draw(string label);                                // ExConViolet SmallButton
    public static bool TryParse(string text, out Entity entity);          // "[i, vN]" → Entity
}
```

Used inside `ComponentDiffPanel` and `EventBrowserPanel` (the latter via a per-row callback hook we add).

### 4.8 Backend tests for Stage 2 — *backend pieces, before UI builds*

Stage 2 has both pure-logic pieces (history trackers, context) that get headless tests **before** the windows are wired, and ImGui pieces that get verified in `Fdp.Presentation.Tests`.

Test project: `FDP/Engine/Fdp.Presentation.Tests/ReplayBrowser/` + `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/`.

| ID | What it asserts |
|---|---|
| FND-T01 | `EntitySelectionHistory.PushSelection` advances `CanGoBack`; back/forward emit `OnSelectionChanged` exactly once each. |
| FND-T02 | Pushing a duplicate consecutive entity is a no-op. |
| FND-T03 | After `GoBack` then `PushSelection(x)` the forward stack is truncated. |
| FND-T04 | While `GoBack` is in flight, a re-entrant `PushSelection` from inside `OnSelectionChanged` is suppressed (no infinite recursion). |
| FND-T05 | `PlaybackHistoryTracker` mirrors all four invariants above for frame indices. |
| FND-T06 | `ReplayBrowserContext.SeekToFrame` calls `ClearCurrentBuffers`, then `SeekToFrame` on `PlaybackController`, then `HistoryService.Capture` — verified by spying on a fake bus/history. |
| FND-T07 | `ReplayBrowserContext.Dispose` disposes its `PlaybackController` and `EntityRepository`; double-dispose is safe. |
| FND-T08 | Loading a recording into a context does not mutate the global `ComponentTypeRegistry` or any process-global state shared with a live cluster (tested via a parallel control context). |
| FND-T09 | `ReplayBrowserSubsystem.Initialize(SubsystemConfig{Headless=true})` builds the context but skips canvas/panel construction; `DrawWorld` and `DrawUI` are no-ops. |
| FND-T10 | `ReplayBrowserSubsystem` does NOT implement `IMapCameraProvider` (reflection assertion: `!subsystem is IMapCameraProvider`). |
| FND-T11 | `ScanForSubsystems` (or its equivalent in `Hrot.ClusterRunner.Program`) discovers `ReplayBrowserSubsystem` under the CLI name `"replaybrowser"`. |
| FND-T12 | `RegisterWindows` registers exactly five windows, all with `Scope == WindowScope.PerspectiveBound` and `OwningPerspective == "ReplayBrowser"`. |
| FND-T13 | `ImGuiEntityLink.TryParse("[42, v3]")` → `Entity(42, 3)`; parses with/without `v` prefix and tolerant of internal whitespace. |
| FND-T14 | `ImGuiEntityLink.TryParse` returns false on malformed strings (no crash). |
| FND-T15 | `EntityInspectorPanel.OnEntitySelected` propagates a click to the injected delegate (presentation test with mock ImGui surface). |
| FND-T16 | Causality "Step Forward and Diff Target" macro: stub recording with two frames; clicking the menu pushes pre-frame, advances, pushes post-frame, selects the parsed entity — full sequence verified by spying on both trackers. |
| FND-T17 | "Save to JSON…" flow: snapshot of `JsonExportOptions` is immutable from the caller after the `Task.Run` begins (mutating the live options must not affect the in-flight task). |
| FND-T18 | **Composition-root history routing.** Wiring the subsystem's `seekIntent`/`selectIntent` closures: invoking `seekIntent(7)` once results in **exactly one** `PlaybackHistoryTracker.PushFrame(7)` call **and** **exactly one** `ReplayBrowserContext.SeekToFrame(7)` call, in that order (verified via spies). `selectIntent` similarly pushes onto `EntitySelectionHistory` exactly once and triggers the `InspectorState.SelectedEntity` assignment via the `OnSelectionChanged` chain. No panel directly invokes the trackers. |

---

## 5. Stage 3 — Diff Engine

### 5.1 Data model

```csharp
public abstract class DiffNode {
    public string Name { get; }
    public bool IsModified { get; protected set; }
    protected DiffNode(string name) => Name = name;
}

public sealed class DiffObject : DiffNode {
    public List<DiffNode> Children { get; } = new();
    public DiffObject(string name) : base(name) { }
    public void EvaluateModificationState() => IsModified = Children.Exists(c => c.IsModified);
}

public sealed class DiffValue : DiffNode {
    public string OldValue { get; }
    public string NewValue { get; }
    public JsonValueKind ValueType { get; }
    public DiffValue(string name, string oldVal, string newVal, JsonValueKind kind, bool isModified)
        : base(name) { OldValue = oldVal; NewValue = newVal; ValueType = kind; IsModified = isModified; }
}
```

### 5.2 Service

```csharp
public interface IComponentDiffService
{
    DiffNode? ComputeDiff(string name, JsonNode? oldNode, JsonNode? newNode, double epsilonTolerance);

    IReadOnlyList<DiffNode> ComputeEntityDiff(
        Entity entity,
        EntityRepository sandboxRepo,
        ScenarioSerializer serializer,
        Action applyStepFunc);

    IReadOnlyList<DiffNode> ComputeTreeDiff(JsonNode? before, JsonNode? after, double epsilonTolerance);
}
```

Algorithm preserves the design talk:

* If both nodes are `JsonObject`, recurse over the union of keys.
* Leaf `JsonValueKind.Number` → parse as double; `|old-new| < epsilon` ⇒ `IsModified=false` but still emitted.
* Other leaves use `ToJsonString()` equality.
* Arrays whose contents differ at any index are emitted as a single modified leaf (entire-array transition) to keep visual output clean — matches the talk.
* Even when nothing in a subtree mutated, the subtree is still emitted under one root per component (so "Hide Unchanged" is a UI decision). Only the **top-level** "no components changed" case yields an empty list.

### 5.3 Diff panel layout (preserve exactly)

```
[Frame Diff Viewer]
+-----------------------------------------------------------------------+
| Options: [ ] Ignore Epsilon (< 0.001)   [x] Hide Unchanged Components |
+-----------------------------------------------------------------------+
| Property                        | Value Transition                    |
|---------------------------------|-------------------------------------|
| ▼ SimTransform                  |                                     |
|   ▼ Position                    |                                     |
|       X                         |  12.345  ->  12.350                 |
|       Z                         |   0.000  ->   0.050                 |
|   ▼ Rotation                    |                                     |
|       W                         |   1.000  ->   0.999                 |
| ▼ IgHealthState                 |                                     |
|       Damage                    |   0.0    ->  15.5                   |
| ▼ TargetMemory                  |                                     |
|   ▼ PositionsX                  |                                     |
|       [0]                       | 100.250  -> 101.000                 |
+-----------------------------------------------------------------------+
```

Implementation rules:

* `Hide Unchanged` defaults to **checked** (talk requirement). With it on, the renderer prunes any node whose `IsModified` is false. Pruning applies to both root components and nested fields.
* `BeginTable("DiffViewerTable", 2, Borders|RowBg|Resizable|SizingFixedFit)`.
* Internal nodes: `TreeNodeEx(DefaultOpen | SpanAvailWidth)`.
* Leaves: `TreeNodeEx(Leaf | NoTreePushOnOpen | SpanAvailWidth)`, jump to column 1, render `OldValue` disabled, ` -> `, then syntax-colored `NewValue`.
* Syntax palette: cyan numbers, green strings, amber booleans, light-gray null/other.
* Entity-handle leaves (detected by `ImGuiEntityLink.TryParse`) render both sides as `ImGuiEntityLink` buttons.

### 5.4 Backend tests for Stage 3 — *before any panel UI is written*

Test project: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Diff/`.

| ID | What it asserts |
|---|---|
| DIF-T01 | Two identical `JsonObject`s ⇒ root `DiffObject.IsModified == false`, no `DiffValue` has `IsModified==true`. |
| DIF-T02 | Single leaf change: only that leaf's `IsModified` is true; its parent's `EvaluateModificationState` propagates `true` up to the root. |
| DIF-T03 | Disjoint key sets (key present only in old, or only in new) are emitted as `DiffValue` with `"null"`/value sides. |
| DIF-T04 | Numeric diff under epsilon: `IsModified==false`; over epsilon: `IsModified==true`. |
| DIF-T05 | Mixed type leaf (was number, now string) is emitted as modified with correct `ValueType`. |
| DIF-T06 | Arrays differing at any index produce a single modified leaf for the full array string. |
| DIF-T07 | `ComputeEntityDiff` calls the supplied `applyStepFunc` exactly once, *between* the two serializer captures. |
| DIF-T08 | `ComputeEntityDiff` returns an empty list when the entity vanished mid-step. |
| DIF-T09 | Allocation budget: running `ComputeDiff` 1000× over a 200-leaf tree allocates <1 MB total (`GC.GetTotalAllocatedBytes(true)`). |
| DIF-T10 | The same tree, run through the diff twice in a row (no changes), produces no modifications. |
| DIF-T11 | `ComputeTreeDiff(null, postState, ε)` (entity birth) returns all-modified leaves. |
| DIF-T12 | `ComputeTreeDiff(preState, null, ε)` (entity death) returns all-modified leaves with `"null"` new values. |
| DIF-T13 | Hide-unchanged pruning rule: when fed a tree with a single modified leaf at depth 4, the renderer (verified via headless tree walker stub) visits exactly the chain of 4 nodes — siblings are pruned. |

---

## 6. Stage 4 — Advanced Recording Search Engine

### 6.1 Domain (polymorphic, serializable via StructEdit)

```csharp
public abstract class SearchPredicateDto { }

public enum LogicalOperator { And, Or }
public sealed class CompoundPredicateDto : SearchPredicateDto {
    public LogicalOperator Operator { get; set; } = LogicalOperator.And;
    public List<SearchPredicateDto> Conditions { get; set; } = new();
}

public enum SearchOperator { Equals, Contains, GreaterThan, LessThan, Changed, StartsWith }
public sealed class PropertyMatchDto : SearchPredicateDto {
    public Type ComponentType { get; set; } = null!;
    public string PropertyPath { get; set; } = string.Empty;   // "Position.X"
    public SearchOperator Operator { get; set; } = SearchOperator.Equals;
    public SearchPredicateDto Predicate { get; set; } = null!;  // Numeric/String/Enum DTO
}

public abstract class SearchPredicateValueDto : SearchPredicateDto { }
public sealed class NumericPredicateDto : SearchPredicateValueDto {
    public double MinValue = double.MinValue; public double MaxValue = double.MaxValue;
}
public sealed class StringPredicateDto : SearchPredicateValueDto {
    public string Substring = ""; public bool StartsWith; public bool ExactMatch;
}
public sealed class EnumPredicateDto<TEnum> : SearchPredicateValueDto where TEnum : struct, Enum {
    public List<TEnum> AllowedValues { get; set; } = new();
}

public sealed class TransientEventPredicateDto : SearchPredicateDto {
    public Type EventType { get; set; } = null!;
    public bool AnyOccurrence { get; set; } = true;
    public string PropertyPath { get; set; } = string.Empty;
    public SearchOperator Operator { get; set; } = SearchOperator.Equals;
    public string TargetValue { get; set; } = string.Empty;
}

public enum EntityIdentifierType { EcsHandle, NetworkId, NameSubstring }
public sealed class LifecyclePredicateDto : SearchPredicateDto {
    public EntityIdentifierType IdentifierType { get; set; } = EntityIdentifierType.NameSubstring;
    public string TargetValue { get; set; } = string.Empty;
}

public enum BoundaryEvent { Entry, Exit, EntryOrExit }
public sealed class SpatialBoundingPredicateDto : SearchPredicateDto {
    [MapPickableBoundingBox] public BoundingBox2D Bounds { get; set; }
    public BoundaryEvent TriggerEvent { get; set; } = BoundaryEvent.EntryOrExit;
}

public enum StructuralModification { Added, Removed, AnyChange }

/// <summary>
/// Distinguishes locally-owned components from ghost replicas in a distributed ECS.
/// In a multi-host deployment an entity can carry the same component bit in its
/// ComponentMask on every host but only one host holds AuthorityMask for it; the
/// others are read-only ghosts. Diagnostic searches must be able to scope to one
/// or the other to avoid investigating phantom state changes on replicas.
/// </summary>
public enum AuthorityRequirement { Any, RequireAuthority, RequireGhost }

public sealed class StructuralPredicateDto : SearchPredicateDto {
    public Type ComponentType { get; set; } = null!;
    public StructuralModification ModificationType { get; set; } = StructuralModification.Added;
    public AuthorityRequirement AuthorityRequirement { get; set; } = AuthorityRequirement.Any;
}

public sealed record SearchResultDto(int FrameIndex, long WallClockTicks, Entity Entity, string ContextMessage);

public sealed record LifecycleSearchResultDto(Entity Entity, int StartFrame, int EndFrame, string MatchContext);

public struct BoundingBox2D { public Vector2 Min, Max; }

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MapPickableBoundingBoxAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class BehaviorHashPickerAttribute : Attribute { }
```

### 6.2 Compilation layer

```csharp
public interface IPropertyEvaluator                { string GetValueAsString(object component); }
public interface IPredicateCompiler                { Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root); }
internal delegate void EventScannerDelegate(FdpEventBus bus, int frame, long ticks, List<SearchResultDto> results);
public interface IEventScannerCompiler             { EventScannerDelegate CompileScanner(TransientEventPredicateDto predicate); }
```

`PropertyEvaluator` uses `IComponentEditService.Open(dummy, type, EditScope.ForField($"$.{propertyPath}"))` to obtain a precompiled `IValueBinding`. The hot loop calls `_buffer.ReplaceInstance(componentInstance); _binding.GetBoxed()` — no reflection.

`PredicateCompiler` recursively builds `Func<EntityRepository, Entity, bool>`:
* `CompoundPredicateDto` → chained AND/OR with short-circuit.
* `PropertyMatchDto` → `HasComponent` guard + `PropertyEvaluator.GetValueAsString` + per-leaf operator evaluator dispatched by value-predicate sub-type.
* `StructuralPredicateDto` and `SpatialBoundingPredicateDto` and `LifecyclePredicateDto` produce specialized closures that the **execution engine** consumes alongside the bool predicate (they're tracked-state predicates and need pre/post bookkeeping; see §6.4).

`EventScannerCompiler` branches on `IsValueType`:
* Pure occurrence (`AnyOccurrence == true || PropertyPath == ""`) → `bus.HasEvent(criteria.EventType)` direct closure.
* Value type → `FastEventScanner<T>` reading `bus.Read<T>()`.
* Reference type → `ManagedEventScanner<T>` reading `bus.ReadManaged<T>()`.

### 6.3 Service contract

```csharp
public interface IRecordingSearchService
{
    IReadOnlyList<SearchResultDto>           ExecuteSearch(string fdpPath, SearchPredicateDto root);
    IReadOnlyList<LifecycleSearchResultDto>  ExecuteLifecycleSearch(string fdpPath, LifecyclePredicateDto criteria);
}
```

`ExecuteSearch` dispatches by root predicate shape (or composite content) to the correct extraction loop:
* Component / Compound / Structural / Spatial → frame-step loop with `QueryDelta`.
* Transient event (unmanaged + managed) → frame-step loop using compiled scanner.
* Lifecycle → its own loop (uses `GetDestructionLog`).

Compound predicates may contain heterogeneous leaves; the service compiles each leaf separately and runs them within a single frame-step pass.

### 6.4 Execution algorithms (preserve exactly from the talk)

* **Component property** — `QueryDelta(query, lastScannedVersion)`; per entity, evaluate compiled predicate; emit `SearchResultDto` on match.
* **Spatial bounding** — maintain `HashSet<Entity> insideZone`; on `SimTransform` chunk mutation, transition entries/exits; honor `TriggerEvent`; purge destroyed entities each frame.
* **Structural** — iterate `EntityIndex` 0..`MaxIssuedIndex`; skip rows where `header.LastChangeTick <= lastScannedVersion`; for each candidate `(entity, header)` compute the **effective presence** under the configured `AuthorityRequirement`:
  * `Any` → `present = header.ComponentMask.IsSet(typeId)`
  * `RequireAuthority` → `present = header.ComponentMask.IsSet(typeId) && header.AuthorityMask.IsSet(typeId)`
  * `RequireGhost` → `present = header.ComponentMask.IsSet(typeId) && !header.AuthorityMask.IsSet(typeId)`

  The membership set used for edge detection (`HashSet<Entity> hasComponent`) tracks the *effective* presence under the active requirement, so flipping authority alone (without changing `ComponentMask`) under `RequireAuthority`/`RequireGhost` is itself a structural edge. Emit Added/Removed edges accordingly; on destruction emit `"Lost {T} (Destroyed)"`.
* **Lifecycle** — for each `NameSubstring`/`NetworkId`/`EcsHandle` mode, track `activeRanges[entity]→startFrame`; on destruction close the range; on EOF flush remaining alive ranges with `EndFrame = EOF`.
* **Transient events — strict invocation contract.** Per simulation tick the loop body executes exactly this sequence:
  1. `playback.StepForward(sandboxRepo)` — this internally calls `PlaybackSystem.ApplyFrame`, which uses `InjectIntoCurrentBySize` / `InjectManagedIntoCurrent` to push the frame's events into `FdpEventBus`'s **read** buffer.
  2. `scanner.Invoke(sandboxBus, currentFrame, wallTicks, results)` — reads via `bus.Read<T>()` / `bus.ReadManaged<T>()` / `bus.HasEvent(t)`, all of which observe the read buffer populated in step 1.
  3. Proceed to the next frame.

  The loop **must not** call `SandboxBus.ClearCurrentBuffers()` or any other read-buffer-mutating operation between steps 1 and 2. The headless `RecordingSearchService` therefore never invokes `ReplayBrowserContext.SeekToFrame` (which does clear buffers) — it steps the playback directly to preserve the read-buffer invariant. This contract is asserted by SR-T38.

### 6.5 StructEdit UI plumbing

* `ReplaySearchPanel` keeps the root `CompoundPredicateDto` and opens an `IEditSession` over it via `IComponentEditService.Open(root, typeof(CompoundPredicateDto), EditScope.WholeComponent)`.
* On every draw, if `session.RebuildState == RebuildRequired` it calls `session.RebuildDocument()`.
* The dynamic `Conditions` list uses `StructEdit`'s `DynamicArrayBinding` natively → `[+ Add]` button and `[X]` row controls "for free", and polymorphic type selection appears on `[+ Add]`.
* Custom field drawers (registered via `IImGuiFieldDrawer`):
  * `BoundingBoxFieldDrawer` for `BoundingBox2D` — manual `DragFloat2 Min/Max` plus a `Pick Area` button that calls `ISpatialPickerContext.RequestBoundingBoxPick(jsonPath)`.
  * `BehaviorHashFieldDrawer` for `int` fields tagged `[BehaviorHashPicker]` — filtered combo driven by `BehaviorRegistry`.
  * `FilteredTypeComboFieldDrawer` for `Type` (`ComponentType`, `EventType`) — embedded filter field as the first item in the combo popup (`InputTextWithHint("##filter", "Type to filter...", ...)`); cached `_availableComponentTypes` / `_availableEventTypes` lists populated on mode switch.

Persistence: `Save Preset…` calls `session.ToJson()` → `EditDocumentJsonSerializer` and writes to disk. `Load Preset…` reads the file, `session.LoadJson(json); session.MarkStructuralChange(); session.RebuildDocument();`. Polymorphic `Conditions` round-trip natively.

### 6.6 Spatial gizmo

`BoundingBoxPickerGizmo` implements `IEntityStatefulGizmo`:

* `RequiresExclusiveFocus = true`, `WantsRawInput = true`.
* `OnMouseEvent(Left, pressed=true)` → set `_startPos`.
* `OnDragUpdate(worldPos)` → update `_currentPos` while drawing a translucent box via `IDebugDrawBuilder.EmitRaw(DebugPrimitive.MakeBox2D(...))` on `PipelineTarget.Map2D`.
* `OnMouseEvent(Left, pressed=false)` while dragging → fire `onComplete(new BoundingBox2D(min, max))` then `onRemove()`.
* `OnKeyEvent(Escape)` or right-click → cancel.

The subsystem owns a singleton `ISpatialPickerContext` implementation that the `BoundingBoxFieldDrawer` calls; on pick request, the subsystem instantiates the gizmo via `GlobalGizmoManager` against its own `MapCanvas`.

### 6.7 Search window — five-mode layout (preserve every wireframe)

The window's top region exposes the radio bar of modes from the design talk:

```
Search Mode:  ( ) Component Mutation  ( ) Transient Event  ( ) Lifecycle
              ( ) Spatial Bounding    ( ) Structural Modification
              (x) Compound (Composite Tree)
```

#### 6.7.1 Mode-by-mode wireframes (normative — preserved verbatim)

**Component Mutation** (matches design-talk.md lines 2643–2671):

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Target Definition                                                     |
|-----------------------------------------------------------------------|
| Component:    [ SimTransform ˅ ]                                      |
| Property:     [ Position.X     ]                                      |
| Entity Mask:  [ (Any)          ]   // Or specific ID e.g., [42, v3]   |
|-----------------------------------------------------------------------|
| Search Criteria                                                       |
|---------------------------------|-------------------------------------|
| Parameter                       | Value                               |
|---------------------------------|-------------------------------------|
| MinValue                        | [ 10.000                        ]   |
| MaxValue                        | [ 50.000                        ]   |
|-----------------------------------------------------------------------|
| [ Execute Search ]              | Status: 45 results found.           |
|-----------------------------------------------------------------------|
| Search Results                                                        |
|------------------|-------------------|--------------------------------|
| Frame            | Entity            | Value Transition               |
|------------------|-------------------|--------------------------------|
| [Frame 1420]     | [42, v3]          | 9.995  -> 10.050               |
| [Frame 1421]     | [42, v3]          | 10.050 -> 10.200               |
| [Frame 3005]     | [85, v1]          | 8.500  -> 12.000               |
+-----------------------------------------------------------------------+
```

**Entity Lifecycle** (matches design-talk.md lines 2868–2887):

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode:  ( ) Component Mutation    (x) Entity Lifecycle          |
+-----------------------------------------------------------------------+
| Target Definition                                                     |
|-----------------------------------------------------------------------|
| Identifier Type: [ Name Substring ˅ ]                                 |
| Target Value:    [ Tank                             ]                 |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: 3 ranges found.             |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-----------------------|-------------|--------------|
| Entity           | Match Context         | Start Frame | End Frame    |
|------------------|-----------------------|-------------|--------------|
| [42, v1]         | Name: Tank-1          | [Frame 0]   | [Frame 450]  |
| [85, v3]         | Name: Tank Platoon-2  | [Frame 120] | [Frame 3005] |
| [102, v1]        | Name: Tank-1          | [Frame 460] | [EOF]        |
+-----------------------------------------------------------------------+
```

**Transient Event — property match** (matches design-talk.md lines 3217–3243):

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode: ( ) Component Mutation  (x) Transient Event  ( ) Lifecycle |
+-----------------------------------------------------------------------+
| Target Definition                                                     |
|-----------------------------------------------------------------------|
| Event Type:   [ WeaponFireIntent ˅ ]                                  |
| Property:     [ WeaponIndex        ]                                  |
+-----------------------------------------------------------------------+
| Search Criteria (StructEdit)                                          |
|---------------------------------|-------------------------------------|
| Parameter                       | Value                               |
|---------------------------------|-------------------------------------|
| Operator                        | [ Equals ˅                      ]   |
| Target Value                    | [ 2                             ]   |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: 18 events found.            |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-----------------------------------|----------------|
| Frame            | Event Payload Value               | Related Entity |
|------------------|-----------------------------------|----------------|
| [Frame 1420]     | WeaponIndex: 2                    | [42, v3]       |
| [Frame 1421]     | WeaponIndex: 2                    | [42, v3]       |
| [Frame 3005]     | WeaponIndex: 2                    | [85, v1]       |
+-----------------------------------------------------------------------+
```

**Transient Event — pure occurrence** (matches design-talk.md lines 3331–3350):

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode: ( ) Component Mutation  (x) Transient Event  ( ) Lifecycle |
+-----------------------------------------------------------------------+
| Target Definition                                                     |
|-----------------------------------------------------------------------|
| Event Type:      [ WeaponFireIntent ˅ ]                               |
| Match Condition: (x) Any Occurrence   ( ) Specific Property Value     |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: 4 frames found.             |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-----------------------------------|----------------|
| Frame            | Event Payload Value               | Related Entity |
|------------------|-----------------------------------|----------------|
| [Frame 1420]     | WeaponFireIntent Occurred         | -              |
| [Frame 3005]     | WeaponFireIntent Occurred         | -              |
+-----------------------------------------------------------------------+
```

**Spatial Bounding** (matches design-talk.md lines 3768–3791):

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode:  ( ) Component  ( ) Event  ( ) Lifecycle  (x) Spatial    |
+-----------------------------------------------------------------------+
| Search Criteria (StructEdit)                                          |
|---------------------------------|-------------------------------------|
| Parameter                       | Value                               |
|---------------------------------|-------------------------------------|
| Bounds                          | [ Pick Area ]                       |
|   Min                           | [ 100.000      ] [ 100.000      ]   |
|   Max                           | [ 500.000      ] [ 500.000      ]   |
| Trigger Event                   | [ Entry ˅                       ]   |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: 2 perimeter breaches.       |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-------------------|--------------------------------|
| Frame            | Entity            | Event Type                     |
|------------------|-------------------|--------------------------------|
| [Frame 1420]     | [42, v3]          | Entered Area                   |
| [Frame 3005]     | [85, v1]          | Entered Area                   |
+-----------------------------------------------------------------------+
```

**Structural Modification** (matches design-talk.md lines 3905–3929; the `Authority Requirement` row is the new control added per §6.1 update):

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode:  ( ) Value  ( ) Event  ( ) Lifecycle  (x) Structural     |
+-----------------------------------------------------------------------+
| Target Definition                                                     |
|-----------------------------------------------------------------------|
| Component:    [ IsEmbarkedTag ˅ ]                                     |
| Entity Mask:  [ (Any)           ]                                     |
+-----------------------------------------------------------------------+
| Search Criteria (StructEdit)                                          |
|---------------------------------|-------------------------------------|
| Parameter                       | Value                               |
|---------------------------------|-------------------------------------|
| Modification Type               | [ Added ˅                       ]   |
| Authority Requirement           | [ Any ˅                         ]   |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: 12 additions found.         |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-------------------|--------------------------------|
| Frame            | Entity            | Event Type                     |
|------------------|-------------------|--------------------------------|
| [Frame 805]      | [42, v3]          | Gained IsEmbarkedTag           |
| [Frame 1420]     | [85, v1]          | Gained IsEmbarkedTag           |
+-----------------------------------------------------------------------+
```

**Compound — nested logic builder** (matches design-talk.md lines 4452–4489, embedded verbatim here as the normative wireframe; frontend developers must **not** write custom ImGui tree logic — `ComponentEditDrawer.DrawEditNode` over the `IEditSession` rendered from `CompoundPredicateDto` is what produces this layout):

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode:  ( ) Value  ( ) Event  ( ) Lifecycle  (x) Compound       |
+-----------------------------------------------------------------------+
| [ Save Preset... ] [ Load Preset... ]                                 |
|-----------------------------------------------------------------------|
| Search Criteria (StructEdit)                                          |
|---------------------------------|-------------------------------------|
| Property                        | Value                               |
|---------------------------------|-------------------------------------|
| ▼ RootPredicate                 |                                     |
|   Operator                      | [ AND ˅                           ] |
|   ▼ Conditions              [2] | [+ Add]                             |
|     ▼ [0] (PropertyMatch)       |                                 [X] |
|         ComponentType           | [ BehaviorState ˅                 ] |
|         PropertyPath            | [ ActiveBehaviorHash              ] |
|         Target Behavior         | [ Combat ˅                        ] |
|     ▼ [1] (CompoundPredicate)   |                                 [X] |
|         Operator                | [ OR ˅                            ] |
|         ▼ Conditions        [2] | [+ Add]                             |
|           ▼ [0] (PropertyMatch) |                                 [X] |
|               ComponentType     | [ NavigationStatus ˅              ] |
|               PropertyPath      | [ Result                          ] |
|               Operator          | [ Equals ˅                        ] |
|               Value             | [ FailedBlocked ˅                 ] |
|           ▼ [1] (Structural)    |                                 [X] |
|               ComponentType     | [ IsEmbarkedTag ˅                 ] |
|               Modification Type | [ Removed ˅                       ] |
|               Authority Req.    | [ RequireAuthority ˅              ] |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: Ready.                      |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-------------------|--------------------------------|
| Frame            | Entity            | Event Type                     |
|------------------|-------------------|--------------------------------|
| [Frame 1420]     | [42, v3]          | Compound Condition Met         |
+-----------------------------------------------------------------------+
```

**Compound layout rules (binding contract for the frontend developer):**

1. The Compound layout is **not** hand-rolled ImGui. The panel opens an `IEditSession` over the root `CompoundPredicateDto`, then calls `_componentEditDrawer.DrawEditNode(_predicateSession.Document.Root)` exactly once. All visible indentation, `▼` chevrons, `[+ Add]` array headers, `[X]` row controls, and polymorphic `<Type Selector>` dropdowns shown in the wireframe are produced by `StructEdit`'s native `DynamicArrayBinding` rendering inside `ComponentEditDrawer` — see design-talk.md lines 4491–4498 for the architectural justification.
2. The recursive indent is automatic: every nested `EditNodeKind.Class` or `EditNodeKind.DynamicArray` triggers an `ImGuiApi.TreeNodeEx`, and ImGui pushes the hierarchical indent before the drawer recurses into children. Frontend developers must **not** call `ImGui.Indent()`/`Unindent()` themselves.
3. Polymorphic items in the `Conditions` list are instantiated by clicking `[+ Add]`, which presents a type-selector populated from every concrete subclass of `SearchPredicateDto`. The new instance is appended; `_predicateSession.MarkStructuralChange()` is called automatically by `DynamicArrayBinding`; the next frame the panel calls `_predicateSession.RebuildDocument()` because `RebuildState == RebuildRequired`.
4. The `Target Behavior` row in the wireframe (showing `Combat ˅`) is produced by the `BehaviorHashFieldDrawer` (§6.5) reacting to the `[BehaviorHashPicker]` attribute on the underlying int field; **no special-case logic** in `ReplaySearchPanel` is permitted.
5. The `Authority Req.` row is produced by `StructEdit`'s native enum reflection over `AuthorityRequirement`; no custom drawer is needed.

#### 6.7.2 Result grid (all modes)

The result grid is always a 3- or 4-column `ImGuiTable` with `Borders | RowBg | ScrollY`. Frame cells are `ImGuiApi.SmallButton`s; entity cells use `ImGuiEntityLink.Draw`.

#### 6.7.3 Deep-link decoupling contract (presentation/composition split)

`ReplaySearchPanel` is **pure presentation**. It knows nothing about playback history or selection history. Its constructor accepts two plain delegates:

```csharp
public ReplaySearchPanel(
    IComponentEditService editService,
    IRecordingSearchService searchService,
    Action<int>    onSeekRequested,     // pure intent: "user wants to seek to frame N"
    Action<Entity> onEntitySelected);   // pure intent: "user wants entity E focused"
```

The panel invokes these delegates raw on every Frame/Entity cell click. **The panel does not call `PlaybackHistoryTracker.PushFrame` or `EntitySelectionHistory.PushSelection` itself.** The composition root in `ReplayBrowserSubsystem` intercepts those delegates and wires them through the history machinery:

```csharp
// Inside ReplayBrowserSubsystem.Initialize (composition root only):
Action<int> seekIntent = frameIndex =>
{
    _playbackHistory.PushFrame(frameIndex);     // record for Back/Forward
    _context.SeekToFrame(frameIndex);           // perform the seek
};

Action<Entity> selectIntent = entity =>
{
    _entityHistory.PushSelection(entity);       // history will fire OnSelectionChanged,
                                                // which assigns InspectorState.SelectedEntity
};

_searchPanel = new ReplaySearchPanel(editService, searchService, seekIntent, selectIntent);
_diffPanel.OnEntityLinkClicked  = selectIntent;
_eventPanel.OnEntityLinkClicked = selectIntent;
_inspectorPanel.OnEntitySelected = selectIntent;
```

The same rule applies to `ComponentDiffPanel` and `EventBrowserPanel` deep-link callbacks: they accept plain `Action<Entity>` delegates, never a reference to a history tracker.

This decoupling is testable: a presentation test of `ReplaySearchPanel` can wire stub `Action<int>` / `Action<Entity>` spies and assert exactly one invocation per click, without instantiating any history tracker at all (FND-T18, SR-T39 below).

### 6.8 Backend tests for Stage 4 — *all backend services must pass before the panel is wired*

Test project: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/`.

The harness from Stage 1 (`FdpRecordingHarness`) is extended to also let tests fire managed/unmanaged events and add/remove components.

| ID | What it asserts |
|---|---|
| SR-T01 | `RecordingSearchService` is constructable without ImGui/Raylib deps; assembly-reference test. |
| SR-T02 | Component property — Equals: harness records 5 frames where `Health=100,90,80,70,60` on entity A; search for `Equals` 80 yields one result at the correct frame. |
| SR-T03 | Component property — GreaterThan: same harness; search for `> 75` yields exactly 3 results. |
| SR-T04 | Component property — Contains (string): `EntityInfo.Name == "Tank-1"`; substring `"ank"` yields the entity. |
| SR-T05 | Compound AND: `Health < 50 AND BehaviorState.ActiveBehaviorHash == HASH(Combat)`; only frames satisfying both emit. |
| SR-T06 | Compound OR: same as above with OR; emission count equals union; no duplicates per frame/entity. |
| SR-T07 | Nested `(A AND (B OR C))`: hand-built DTO tree; closures compile recursively; semantics verified against a brute-force evaluator. |
| SR-T08 | Predicate compiler produces a single `Func<EntityRepository, Entity, bool>` — verified by counting allocations in 10k invocations (< 1 KB total). |
| SR-T09 | `QueryDelta` chunk skipping: harness fills 64 KB-worth of stationary entities + one mutating entity per frame; with profiling spy, the inner loop visits exactly the mutating entity each frame. |
| SR-T10 | Spatial bounding — Entry: entity walks `(0,0) → (100,100) → (200,200)` with zone `[150,150,250,250]`; emit `Entry` exactly once at the frame the threshold is crossed. |
| SR-T11 | Spatial bounding — Exit: reverse path; emit `Exit` exactly once. |
| SR-T12 | Spatial bounding — EntryOrExit: both edges emitted in the correct order. |
| SR-T13 | Spatial bounding — destruction inside zone: no spurious `Exit` from destruction is emitted; `insideZone` set is cleaned (verified by spy). |
| SR-T14 | Structural — Added: harness adds `IsEmbarkedTag` on tick 3; one Added result at frame 3 (and not at later frames). |
| SR-T15 | Structural — Removed: removed later; one Removed result at the correct frame. |
| SR-T16 | Structural — AnyChange: both edges emitted. |
| SR-T17 | Structural — destruction with the component → emits `"Lost {T} (Destroyed)"`. |
| SR-T18 | Structural — `LastChangeTick` bypass: stable rows skipped, verified by visit counter on `EntityHeader` access. |
| SR-T19 | Lifecycle — EcsHandle: search yields single range with correct `[Start,End]`. |
| SR-T20 | Lifecycle — NetworkId: harness gives `NetworkIdentity.Value = 1005` to entity B; search `NetworkId == 1005` returns its range; not matched by entity with `1006`. |
| SR-T21 | Lifecycle — NameSubstring: case-insensitive contains; `"tank"` matches `EntityInfo.Name="Tank-1"` and `"tank platoon-2"`. |
| SR-T22 | Lifecycle — alive at EOF: end frame is the playback EOF index, not a guess. |
| SR-T23 | Transient event — pure occurrence (unmanaged): a `WeaponFireIntent` fires on ticks 3 and 7; service returns exactly two results with `Entity.Null` and `"... Occurred"` context. |
| SR-T24 | Transient event — pure occurrence (managed): same logic for a managed event class. |
| SR-T25 | Transient event — property match (unmanaged): `WeaponFireIntent.WeaponIndex == 2` filters correctly. |
| SR-T26 | Transient event — property match (managed): `AssignBehaviorEvent.BehaviorName == "Combat"` filters correctly. |
| SR-T27 | Transient event — entity deep-link: when payload field is an `Entity`-formattable handle, the `SearchResultDto.Entity` is populated by `ImGuiEntityLink.TryParse`. |
| SR-T28 | Persistence: a 3-level nested compound predicate is `ToJson`'d, the file is round-tripped, `LoadJson` reconstructs equivalent semantics — verified by running the same recording through both DTOs and comparing result sequences. |
| SR-T29 | StructEdit predicate session: changing `Conditions` size sets `RebuildState == RebuildRequired`; after `RebuildDocument` the new child appears in `Document.Root`. |
| SR-T30 | Bounding-box gizmo: feeding `OnMouseEvent(Left, true, (10,10))`, `OnDragUpdate((20,30))`, `OnMouseEvent(Left, false, (20,30))` fires `onComplete` exactly once with `Min=(10,10), Max=(20,30)` (independent of click order). |
| SR-T31 | Bounding-box gizmo: `OnKeyEvent(Escape)` or right-click cancels and calls `onRemove` without firing `onComplete`. |
| SR-T32 | `BoundingBoxFieldDrawer` write-back: simulating `_pickerCtx.TryConsumeBoundingBoxPick(path, out box)` returning a value mutates the bound DTO field. |
| SR-T33 | `BehaviorHashFieldDrawer` round-trip: a `BehaviorRegistry.TryGetId("Combat", out hash)` followed by a Selectable click sets the underlying `int` to that hash. |
| SR-T34 | **Strict zero-allocation extraction loop.** The frame-step body of `RecordingSearchService.ExecuteSearch` (the code between `playback.StepForward(repo)` and the next iteration) must allocate **exactly 0 bytes** on the managed heap per frame. Test method: pre-warm with one warmup frame, then snapshot `GC.GetAllocatedBytesForCurrentThread()` before the loop body, run a representative scan over 10k frames (compiled predicate, populated `QueryDelta` source), snapshot after, and assert `(after - before) == 0`. Implication for implementation: results accumulate into a pre-allocated `List<SearchResultDto>` whose capacity is set once before the loop (`results.EnsureCapacity(expectedCount)`); no `ToString()`, boxing, closure capture, or interpolated-string allocation occurs inside the loop; per-match `SearchResultDto` instances do not count as loop allocations only if the predicate produced a match — they are *result* allocations, but no other allocations are permitted regardless of match. Performance budget secondary: complete a 10k-frame scan in `< 2 s` on the CI baseline. |
| SR-T35 | Compound short-circuit AND: a deliberately-expensive evaluator placed second in an AND chain is not invoked when the first leaf returns false (verified via call-count spy). |
| SR-T36 | Search engine isolation: invoking `ExecuteSearch` in parallel with a GUI-context's `SeekToFrame` (mocked) does not affect the GUI context's `CurrentFrame`. |
| SR-T37 | **Structural authority filtering.** Harness creates entity A with `IsEmbarkedTag` in its `ComponentMask` *and* `AuthorityMask`, and entity B (ghost replica) with the same bit set in `ComponentMask` only. `StructuralPredicateDto { AuthorityRequirement = RequireAuthority }` returns only A; `RequireGhost` returns only B; `Any` returns both. |
| SR-T38 | **Event scanner double-buffer invariant.** A harness recording fires `WeaponFireIntent` on tick 3. The scanner test verifies the exact call sequence per frame: `playback.StepForward(repo)` → `scanner.Invoke(bus, ...)` (sees the event), with **no** `SandboxBus.ClearCurrentBuffers()` call between them (verified via instrumented bus spy). Same assertion for managed events via `bus.ReadManaged<T>()` and for pure-occurrence via `bus.HasEvent(...)`. |
| SR-T39 | **Search panel decoupling.** `ReplaySearchPanel` is constructed with stub `Action<int>` and `Action<Entity>` spies (no history-tracker instances of any kind). Simulating a click on the `Frame` cell invokes the int spy exactly once with the expected value; the `Entity` cell invokes the Entity spy exactly once. Reflection assertion: `ReplaySearchPanel` has no field whose declared type is `PlaybackHistoryTracker` or `EntitySelectionHistory`. |

### 6.9 Stage 4 Definition of Done

* SR-T01..SR-T39 pass.
* `EditDocumentJsonSerializer` preset save/load round-trips through every polymorphic predicate type.
* Search panel (UI) renders all five mode wireframes verbatim, including the compound-tree indented variant; smoke-tested via `Fdp.Presentation.Tests`.

---

## 7. Stage 5 — Global Registration

* `Hrot.ReplayBrowser.csproj` is added to the ClusterRunner solution. Its `ReplayBrowserSubsystem` is automatically discovered by `ScanForSubsystems`.
* No change to `LocalWindowController.OpenLocalWindow` is needed for the subsystem itself; the perspective windows are auto-registered via `IWindowRegistrar`.
* `Hrot.ClusterRunner.Tests` gets a new test that launches the runner with `-m replaybrowser` headless and asserts the subsystem initialized and registered its five windows.

---

## 8. Dependency / Risk Register

| # | Topic | Risk | Mitigation |
|---|---|---|---|
| D1 | `ComponentTypeRegistry` enumeration API may not exist as `GetAllTypes()` | Stage 4 search depends on it for the filtered combo | Add a `RB-1.0` audit task to confirm and (if missing) add a thin enumerator into `ComponentType.cs` — this is a one-method change |
| D2 | `EntityRepository.HasComponentByTypeId(entity, int)` may not exist | Predicate compiler uses it | Fallback to `repo.GetHeader(entity.Index).ComponentMask.IsSet(typeId)` (verified to exist) |
| D3 | `FdpRecordingHarness` doesn't exist yet | Stage 1+4 tests need it | Build it as task `RB-1.1`; it is the test substrate for the whole project |
| D4 | `StructEdit.Json` round-trip of polymorphic `SearchPredicateDto` may need a discriminator | Preset save/load could fail | Validate by writing the round-trip test (SR-T28) FIRST, before building the UI |
| D5 | `BehaviorState.ActiveBehaviorHash`, `NavigationStatus.Result`, `EntityInfo.Name`, `NetworkIdentity.Value` exact names | Hard-coded test fixtures might miss | Tests reference real types resolved via reflection; if a name changes, only test fixtures (not production code) need an update |
| D6 | Search engine reading transient events expects events to still be in the read buffer after `PlaybackController.StepForward` | If the playback clears the bus before we read, we miss them | Verified by `PlaybackSystem.ApplyFrame` calling `InjectIntoCurrentBySize`/`InjectManagedIntoCurrent` into the read buffer; SR-T23..T26 will catch any regression |
| D7 | UI background export must not stall ImGui | Bad scheduling could freeze 60Hz | Strict `Task.Factory.StartNew(..., LongRunning)`; explicit "is exporting" UI flag; verified manually + by FND-T17 |

---

## 9. Phase Dependency Graph

```
Stage 1 (Headless Export + CLI + Tests EX-T01..T32)
   │
   ├─► Stage 3 (Diff Engine + Tests DIF-T01..T13)
   │       │
   │       ▼
   │   Stage 2 (Subsystem + Foundation UI + Tests FND-T01..T17)
   │       │
   │       ▼
   └─► Stage 4 (Search Backend Tests SR-T01..T36) ─► Stage 4 UI panel
                                                        │
                                                        ▼
                                                  Stage 5 (Global registration smoke)
```

Stage 1 ships first. Stage 3 diff engine ships before Stage 2 UI, because Stage 2's diff panel and the Stage 1 changelog mode both depend on it. Stage 2 UI builds on Stage 3 backend. Stage 4 backend ships before Stage 4 UI. Stage 5 closes out.

---

## 10. Final Idea Coverage Map

Each "final idea" from the design talk and where it lives in this design:

| Talk topic | Design section |
|---|---|
| Console dump utility for `.fdp` | §3 |
| FDPREC header / FrameOuterHeader / FrameType / WallClockTicks | §3.1, 3.4 |
| `PlaybackController` + `PlaybackSystem.ApplyFrame` reuse | §3.4, 4.2 |
| Isolated `EntityRepository`/`FdpEventBus`/history | §3 + §4.2 |
| JSON output uses `ScenarioSerializer` + `InspectorJsonUtils.BuildComponentJson` + `JsonAestheticFormatter.FlattenNumericArrays` + `DiagnosticGuidResolver` + `FdpJsonOptionsRegistry` | §3.4 |
| JSON schema with header + frames | §3.1 |
| Components as list of `{ComponentType, HasAuthority, Payload}` | §3.1 |
| `AuthorityMask` round-trip preservation | §3.4 (item 7), EX-T06 |
| `RelativeWallTimeSec` from first frame anchoring | §3.1, EX-T07 |
| `SimTimeSec` from `GlobalTime.TotalTime` | §3.1, EX-T08 |
| `Tick` = `GlobalVersion` (not ordinal) | §3.1, EX-T11 |
| `FileFrameOrdinal` and `SimFrameNumber` explicit naming | §3.1, EX-T09, EX-T10 |
| Keyframe vs delta semantics | §3.1, EX-T03 |
| `DestroyedEntities` array on delta | §3.1, EX-T04 |
| Components are *complete* in delta frames, no field merging | §3.4 (relies on `PlaybackSystem.ApplyFrame`); explicit in §3 |
| Shared `RecordingExportService` | §3.3 |
| Console = thin CLI wrapper | §3.7 |
| GUI background export via `IFileDialogService` + `Task.Factory.StartNew(LongRunning)` | §4.5, FND-T17 |
| Graphical browser = perspective-bound subsystem (not Debug menu window) | §4.1 |
| Subsystem CLI name `replaybrowser` | §4.1 |
| **No** `IMapCameraProvider` (strict isolation) | §4.1, FND-T10 |
| 5 PerspectiveBound windows (Timeline, Inspector, Diff, Events, Search) | §4.4, FND-T12 |
| Layout with central passthru `MapCanvas` | §4.3 |
| Reuse `EntityInspectorPanel` and `EventBrowserPanel` `DrawContent` | §4.4 |
| Yellow-header highlighting reuse via `ComponentReflector` | Implicit via `EntityInspectorPanel` reuse (§4.4 — no change needed) |
| `ComponentDiffService` + diff tree + epsilon tolerance | §5.1, 5.2, DIF-T04 |
| `DiffNode.IsModified` propagation, Hide Unchanged default ON | §5.1, 5.3 |
| Hide-unchanged prunes nested fields | §5.3, DIF-T13 |
| Syntax-colored diff (`OldValue -> NewValue`) | §5.3 |
| Frame Diff Viewer 2-column table layout | §5.3 |
| `ImGuiEntityLink` deep-link primitive | §4.7 |
| Entity selection history (Back/Forward) | §4.6, FND-T01..T04 |
| Causality "Step Forward and Diff Target" + playback history | §4.6 (causality), FND-T16 |
| `Save as JSON` + collapsible export options expander | §4.5, talk §"i will certainly want to project these options to the graphical tool" |
| CLI flags (frame window, time window, entity, scope, minified) | §3.7 |
| Mutual exclusion of frame vs time windowing | §3.7, EX-T31 |
| Changelog export mode (multi-entity) | §3.6, EX-T27..T29 |
| Search — component property | §6.1, 6.4, SR-T02..T04 |
| Search — value-range / enum-list / substring via StructEdit DTOs | §6.1, 6.5 |
| Search — visual layout for component mutation | §6.7 |
| Search — entity lifecycle (Ecs/NetId/NameSubstring) | §6.1, 6.4, SR-T19..T22 |
| Search — lifecycle layout with Start/End frame deep-link | §6.7 |
| Search — transient event (unmanaged + managed) | §6.1, 6.4, SR-T23..T27 |
| Search — pure event occurrence (no payload) | §6.2, SR-T23..T24 |
| Search — embedded substring filter in component/event type combos | §6.5 (`FilteredTypeComboFieldDrawer`) |
| Search — spatial bounding box with manual + map pick | §6.5, 6.6, SR-T10..T13, SR-T30..T32 |
| Search — Entry / Exit / EntryOrExit trigger | §6.4, SR-T10..T12 |
| Search — structural modification (Added/Removed/AnyChange) | §6.4, SR-T14..T18 |
| Search — behavioral / navigational queries via PropertyMatch + `BehaviorHashFieldDrawer` | §6.5, SR-T33 |
| Compound AND/OR / nested logic builder | §6.1, SR-T05..T07 |
| Nested visual layout (indented `Conditions`) | §6.7 |
| Save / Load preset via `StructEdit.Json` | §6.5, SR-T28 |
| Headless `RecordingSearchService` testable without UI | §6.3, all SR-* tests |
| `QueryDelta` chunk-skipping optimization | §6.4, SR-T09 |
| Strict zero-allocation hot path | §6.2 (compilation step), SR-T08 |
| Deep-linking (temporal + spatial) from result grid | §6.7 |
| Source layout (shared, CLI, presentation) | §2 |
| Stage ordering: CLI dump → UI foundation → search | §9 |
| Backend-first, thorough test coverage (success conditions) | §3.8, §4.8, §5.4, §6.8 |
| Strict zero-allocation in search extraction loop | §6.8 SR-T34 (rewritten) |
| Structural search with authority filtering (RequireAuthority/RequireGhost/Any) | §6.1, §6.4, §6.7 wireframe, SR-T37 |
| Event-scanner double-buffer timing invariant (StepForward → Scan, no ClearCurrentBuffers in between) | §6.4, SR-T38 |
| Search panel decoupled from history (raw `Action<int>`/`Action<Entity>` delegates) | §6.7.3, FND-T18, SR-T39 |
| Compound nested layout embedded as normative wireframe | §6.7.1 (Compound block) |
