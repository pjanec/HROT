# Onboarding — FDP Replay Browser

Welcome. You are joining the effort to build the **FDP Replay Browser**: a diagnostic toolchain for `.fdp` flight recordings. Two artifacts ship at the end:

1. **`Fdp.Tools.RecordingDumper`** — a headless console application that converts a `.fdp` recording to a human-readable JSON dump or to a compact per-entity changelog. Used from CI/scripts and from inside the GUI.
2. **`Hrot.ReplayBrowser`** — a cluster-runner subsystem with its own perspective: an isolated ECS sandbox, a `MapCanvas`, a timeline, reused live-mode panels (entity inspector, event browser), a frame-diff viewer, and a powerful search window.

The work is staged **backend-first** so each layer has thorough xUnit coverage before any ImGui glue is written.

---

## 1. Where the design lives

| Document | Purpose |
|---|---|
| [design-talk.md](./design-talk.md) | The full collaborative design conversation. Normative for UI wireframes and for any code samples worth lifting verbatim. **Read it.** It is long but it is the source of truth when this folder is silent. |
| [DESIGN.md](./DESIGN.md) | The structured design: architecture, JSON schema, APIs, repository layout, test plan, dependency/risk register, final-idea coverage matrix. |
| [TASK-DETAILS.md](./TASK-DETAILS.md) | Per-task scope, code-sample references back into the design talk, and **binary success conditions** that gate task completion. |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Lightweight checklist of progress across all stages with links into the details. |
| [DEBT-TRACKER.md](./DEBT-TRACKER.md) | Technical-debt log to record any deviations from the design as they happen. |

Read in this order: DEV-GUIDE → DESIGN → TASK-DETAILS → grab a task from TASK-TRACKER.

> Read the developer behavior contract: **[../.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md)**. It defines how you work — testing, batching, debt logging, and review conventions. Follow it.

---

## 2. What we're refactoring / building

We are **adding** new code, not refactoring large existing systems. The new code:

- Lives in a new subfolder under `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/` (headless, no ImGui).
- A new CLI project at `FDP/Tools/Fdp.Tools.RecordingDumper/`.
- New ImGui panels under `FDP/Engine/Fdp.Presentation/ImGui/{Panels,Windows,Utils,Editing}/ReplayBrowser/`.
- A new subsystem assembly `Hrot/Subsystems/Hrot.ReplayBrowser/`.

It **consumes**, but does not modify, these existing components (see [DESIGN.md §1](./DESIGN.md#1-architectural-overview) for the verified anchor table):

- Flight Recorder (`Fdp.Core/FlightRecorder/`): `RecordingReader`, `PlaybackController`, `PlaybackSystem.ApplyFrame`, `RecorderSystem`, `RecordingGlobalHeader`, `FrameOuterHeader`, `FdpConfig.FORMAT_VERSION`.
- ECS core (`Fdp.Core/`): `EntityRepository`, `EntityHeader`, `BitMask256`, `EntityIndex`, `FdpEventBus`, `ComponentType`/`ComponentTypeRegistry`, `EventType`, `GlobalTime`.
- JSON pipeline: `ScenarioSerializer` (`Fdp.Toolkits/Scenario/`), `HrotScenarioSerializerFactory` (`Hrot.SimHost/Serializers/`), `FdpJsonOptionsRegistry`, `JsonAestheticFormatter`, `DiagnosticGuidResolver`, `InspectorJsonUtils`, `IEntityScenarioTranslator`, `FdpAutoSerializer`.
- Diagnostics: `DiagnosticEventHistoryService`, `IEventStreamInspector`.
- Inspector/UI: `EntityInspectorPanel`, `EventBrowserPanel`, `ComponentReflector`, `ImGuiPropertyTree`, `IInspectableSession`, `IInspectorContext`, `InspectorState`, `RepositoryAdapter`, `IFileDialogService`.
- Window framework: `ManagedWindow`, `WindowScope`, `WindowManager`, `IWindowRegistrar`, `LocalWindowController` (no change required, but read it).
- Subsystem framework: `ISubsystem`, `SubsystemConfig`, `SubsystemOrchestrator`, `IMapCameraProvider` (we explicitly **do not** implement this — see DESIGN.md §4.1), `ScanForSubsystems` in `Hrot.ClusterRunner.Program`.
- Map / gizmo: `MapCanvas`, `DebugGizmoLayer`, `GridMapLayer`, `IEntityStatefulGizmo`, `GlobalGizmoManager`, `IDebugDrawBuilder`, `DebugPrimitive`, `MapMouseButton`, `MapKeyboardKey`.
- StructEdit (`FDP/ExtDeps/StructEdit/`): `IComponentEditService`, `IEditSession`, `ComponentEditDrawer`, `EditDocumentJsonSerializer`, `IImGuiFieldDrawer`, plus the existing `IComponentPickerContext` and `MapPickableWorldLocationAttribute`.
- Behavior: `BehaviorRegistry`.

---

## 3. Repo layout cheat-sheet

```
.dev/replay-browser-2/                       ← this folder (specs)
FDP/
  Engine/
    Fdp.Core/                                ← Flight Recorder & ECS — read-only
    Fdp.Core.Tests/
    Fdp.Presentation/
      ImGui/
        Panels/                              ← EntityInspectorPanel, EventBrowserPanel, etc. (reused)
        Panels/ReplayBrowser/                ← NEW: ReplayTimelinePanel, ComponentDiffPanel, ReplaySearchPanel
        Windows/                             ← MessageLogWindow, etc.
        Windows/ReplayBrowser/               ← NEW: 5 PerspectiveBound windows
        Utils/                               ← InspectorJsonUtils, ImGuiPropertyTree, etc.
        Utils/ReplayBrowser/                 ← NEW: ImGuiEntityLink
        Editing/                             ← ComponentEditDrawer, IImGuiFieldDrawer, etc.
        Editing/ReplayBrowser/               ← NEW: custom field drawers
    Fdp.Presentation.Tests/
      ReplayBrowser/                         ← NEW: presentation-layer tests
  Toolkits/
    Fdp.Toolkits/
      Scenario/                              ← ScenarioSerializer (reused)
      Diagnostics/                           ← DiagnosticGuidResolver, gizmo infrastructure
      Diagnostics/Gizmos/ReplayBrowser/      ← NEW: BoundingBoxPickerGizmo
      Replay/                                ← (existing) ReplayModule, PlaybackTickSystem — leave alone
      ReplayBrowser/                         ← NEW: ALL HEADLESS REPLAY BROWSER CODE GOES HERE
        JsonExportOptions.cs
        IRecordingExportService.cs
        RecordingExportService.cs
        ReplayBrowserContext.cs
        EntitySelectionHistory.cs
        PlaybackHistoryTracker.cs
        Diff/                                ← DiffNode, ComponentDiffService
        Search/                              ← all search DTOs, compilers, RecordingSearchService
    Fdp.Toolkits.Tests/
      ReplayBrowser/                         ← NEW: ALL HEADLESS TESTS GO HERE
        Support/FdpRecordingHarness.cs
        Export/                              ← EX-T01..T32
        Diff/                                ← DIF-T01..T13
        Search/                              ← SR-T01..T36
  Tools/
    Fdp.Tools.RecordingDumper/               ← NEW: CLI console exe
    Fdp.Tools.RecordingDumper.Tests/         ← NEW
Hrot/
  Runner/
    Hrot.ClusterRunner/
      Program.cs                             ← ScanForSubsystems lives here — NO CHANGE needed
      Presentation/LocalWindowController.cs  ← read-only reference
    Hrot.ClusterRunner.Tests/                ← we add one test asserting -m replaybrowser discovery
  Subsystems/
    Hrot.ReplayBrowser/                      ← NEW: subsystem assembly
    Hrot.ReplayBrowser.Tests/                ← NEW
```

---

## 4. How to build and run

The repo uses the standard .NET 8 toolchain. From the repo root:

```powershell
# Build everything
dotnet build .\IOS-IG-SimHost-FDP-2.sln

# Run the headless dumper (Stage 1 deliverable)
dotnet run --project FDP\Tools\Fdp.Tools.RecordingDumper -- -i path\to\capture.fdp -o dump.json

# Time-windowed minified dump without events
dotnet run --project FDP\Tools\Fdp.Tools.RecordingDumper -- `
    -i capture.fdp -o slice.json --start-time 5.0 --end-time 10.0 --no-events --minified

# Launch the ReplayBrowser perspective (after Stage 2 lands)
dotnet run --project Hrot\Runner\Hrot.ClusterRunner -- -m replaybrowser
```

Tests are run per-project:

```powershell
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
dotnet test FDP\Tools\Fdp.Tools.RecordingDumper.Tests\Fdp.Tools.RecordingDumper.Tests.csproj
dotnet test FDP\Engine\Fdp.Presentation.Tests\Fdp.Presentation.Tests.csproj
dotnet test Hrot\Subsystems\Hrot.ReplayBrowser.Tests\Hrot.ReplayBrowser.Tests.csproj
```

---

## 5. Workflow expectations

1. Pick a task from [TASK-TRACKER.md](./TASK-TRACKER.md) following the stage order.
2. Read its entry in [TASK-DETAILS.md](./TASK-DETAILS.md) and the design talk lines it cites for any verbatim-liftable code samples.
3. Implement, write tests that satisfy **every** binary success condition.
4. Do **not** mark a task done until all its success conditions and referenced test IDs are green.
5. Backend tests must pass before the UI for the same stage is started. This is non-negotiable: Stage 1 EX-T*, Stage 3 DIF-T*, Stage 4 SR-T*.
6. Any deviation from DESIGN.md gets a row in [DEBT-TRACKER.md](./DEBT-TRACKER.md) with priority and target batch.
7. Read and abide by **[../.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md)** for the team's overall working contract (review conventions, debt handling, test discipline, batching, etc.).

---

## 6. Pointers for first-day reading

If you have only an hour, read in this order:
1. [DESIGN.md §1 Architectural Overview](./DESIGN.md#1-architectural-overview) and §2 Repository Layout.
2. [DESIGN.md §3 Stage 1](./DESIGN.md#3-stage-1--headless-json-export-pipeline) — that is the next thing shipping.
3. [TASK-DETAILS.md RB-1.0 → RB-1.7](./TASK-DETAILS.md#stage-1--headless-json-export-pipeline) — the first concrete tasks.
4. Skim [design-talk.md](./design-talk.md) for context.
5. Skim `FDP/Engine/Fdp.Core/FlightRecorder/PlaybackController.cs` and `PlaybackSystem.cs` to see what we're consuming.

Welcome aboard.
