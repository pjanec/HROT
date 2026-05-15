# BATCH-02: Stage 1 Completion — Context, Export Service, CLI, and Acceptance Gate

**Batch Number:** BATCH-02
**Tasks:** Corrective Task 0 (P2 harness fix), RB-1.4, RB-1.5, RB-1.6, RB-1.7
**Phase:** Stage 1 — Headless JSON Export Pipeline (Implementation)
**Estimated Effort:** 14–18 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (all items complete)

---

## Onboarding & Workflow

### Developer Instructions

You are continuing Stage 1 of the FDP Replay Browser. BATCH-01 laid the foundation (registry APIs, harness, DTOs, service interface). This batch implements the actual production code: the `ReplayBrowserContext`, the `RecordingExportService` (absolute-state path only — changelog mode depends on Stage 3), the `Fdp.Tools.RecordingDumper` CLI, and the full EX-T test suite (EX-T01..EX-T26 mandatory now, EX-T27..T29 deferred to BATCH-03). You also fix the P2 issue from BATCH-01's review (harness self-test must verify frame content, not just frame metadata).

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Onboarding:** `.dev/replay-browser-2/ONBOARDING.md`
3. **BATCH-01 Review:** `.dev/replay-browser-2/reviews/BATCH-01-REVIEW.md` — understand the P2 issue
4. **Design Document:** `.dev/replay-browser-2/DESIGN.md` — §3.4 (pipeline algorithm), §3.5 (filtering rules), §3.7 (CLI), §3.8 (test IDs EX-T01..T32), §3.9 (definition of done)
5. **Task Definitions:** `.dev/replay-browser-2/TASK-DETAILS.md` — RB-1.4, RB-1.5, RB-1.6, RB-1.7
6. **Design Talk (code samples):** `.dev/replay-browser-2/design-talk.md` — lines 519–561 (export skeleton), 631–707 (CLI + windowing), 855–862 (CLI), 964–999 (context structure)

### Source Code Locations
- **Context (new):** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs`
- **Export service (update stub):** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs`
- **CLI console app (new project):** `FDP/Tools/Fdp.Tools.RecordingDumper/`
- **CLI tests (new project):** `FDP/Tools/Fdp.Tools.RecordingDumper.Tests/`
- **Stage 1 tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/` (extend existing)
- **Harness self-test fix:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarnessTests.cs`
- **FDP solution:** `FDP/FDP.sln` (must add the two new projects to it)

### Report Submission
**When done, submit your report to:**
`.dev/replay-browser-2/reports/BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev/replay-browser-2/questions/BATCH-02-QUESTIONS.md`

---

## Context

Stage 1 closes with this batch. The export service is the backbone: it builds its own isolated `ReplayBrowserContext` per call so the GUI's live sandbox is never disturbed. The CLI is a thin wrapper mapping command-line flags to `JsonExportOptions` and invoking the service. All 32 EX-T tests from DESIGN.md §3.8 must be green (EX-T27..29 are changelog-mode tests deferred to BATCH-03 after Stage 3 backend is done, but EX-T01..26 and EX-T30..32 are mandatory for this batch).

**Related Tasks:**
- [RB-1.4](../TASK-DETAILS.md#rb-14--headless-replaybrowsercontext) — Sandbox context
- [RB-1.5](../TASK-DETAILS.md#rb-15--recordingexportservice-implementation) — Export service (absolute-state path)
- [RB-1.6](../TASK-DETAILS.md#rb-16--fdptoolsrecordingdumper-console-app) — CLI tool
- [RB-1.7](../TASK-DETAILS.md#rb-17--stage-1-acceptance-gate) — Gate

---

## Batch Objectives

1. Fix P2 issue: extend `HarnessSelfTest` to step through frames and verify destruction log + events on the correct frames.
2. Implement `ReplayBrowserContext` (sandbox with `EntityRepository`, `FdpEventBus`, `PlaybackController`, `DiagnosticEventHistoryService`).
3. Implement `RecordingExportService.ExportToJson` for the absolute-state path (streaming, `Utf8JsonWriter`, all EX-T01..T26 green).
4. Create `Fdp.Tools.RecordingDumper` console app (EX-T30..T32).
5. All tests passing; FDP solution builds clean.

---

## Corrective Task 0 — Fix `HarnessSelfTest` Frame-Content Assertions (P2 from BATCH-01)

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarnessTests.cs`

**What is wrong:** The self-test only checks frame count, frame types, and wall clock ticks. It does NOT step through frames with `PlaybackController.StepForward(repo)` and verify:
- Frame 3 (tick 3): the destroyed entity appears in `repo.GetDestructionLog()` after `ApplyFrame`.
- Frame 4 (tick 4): the unmanaged event (`HarnessTestEventA`) is readable via `_repo.Bus.Read<HarnessTestEventA>()`, and the managed event (`HarnessTestManagedEvent`) is readable via `_repo.Bus.ReadManaged<HarnessTestManagedEvent>()`.

**What to do:** Extend the single `HarnessSelfTest_ProducesReadableRecording` test (or split into a second test if preferred) to:
1. Create a fresh `EntityRepository` (the replay sandbox) and step through all 5 frames using `PlaybackController.StepForward(sandboxRepo)`.
2. After `StepForward` on frame 3: assert `sandboxRepo.GetDestructionLog()` contains the entity that was destroyed.
3. After `StepForward` on frame 4: assert `sandboxRepo.Bus.Read<HarnessTestEventA>()` returns the event with `Payload == 99`, and `sandboxRepo.Bus.ReadManaged<HarnessTestManagedEvent>()` returns an event with `Tag == "test"` (or the appropriate managed-event reading API).

Do NOT stop if you hit API naming issues — check the existing `Fdp.Core.Tests` or `Fdp.Toolkit.Replay.Tests` to see how `StepForward`, `GetDestructionLog`, and bus reading work in tests.

---

## Task 1: RB-1.4 — Headless `ReplayBrowserContext`

**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs` (NEW)

**Task Definition:** See [TASK-DETAILS.md §RB-1.4](../TASK-DETAILS.md#rb-14--headless-replaybrowsercontext) and [DESIGN.md §4.2](../DESIGN.md#42-sandbox-context). The verbatim class skeleton is in DESIGN.md §4.2. Code samples in design-talk.md lines 964–999.

**What to do:**
- Implement the class exactly per DESIGN.md §4.2. The properties are: `SandboxRepo`, `SandboxBus`, `HistoryService`, `Playback`, `Session` (a `RepositoryAdapter(SandboxRepo)`), `InspectorState`, `DiffService`, `CurrentFdpPath`, `CurrentFrame`.
- `SeekToFrame(frameIndex)` must, in this exact order:
  1. `SandboxBus.ClearCurrentBuffers()`
  2. `Playback.SeekToFrame(SandboxRepo, frameIndex)`
  3. `HistoryService.Capture("Replay", SandboxBus, (uint)CurrentFrame)`
- `StepForward` and `StepBackward` follow the same pattern (clear, step, capture).
- `Dispose`: dispose `Playback`, `SandboxRepo`, mark disposed. Double-dispose must be a no-op.
- The context may reference `Fdp.Presentation` types for `IInspectorContext`, `InspectorState`, and `RepositoryAdapter`. Check where those live (likely `Fdp.Presentation/ImGui/Abstractions/`). If the context would create a `Fdp.Presentation` dependency in the `Fdp.Toolkits` assembly (which must stay headless), move it to a dedicated `Hrot.ReplayBrowser` assembly. For now, if `RepositoryAdapter`/`InspectorState` are in `Fdp.Presentation`, note this in the report — the context may need to live in a different assembly.

**Tests (FND-T06, FND-T07 from DESIGN.md §4.8):**
These are Stage 2 test IDs but the context is implemented here. Place them in `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Context/` or `Hrot/Subsystems/Hrot.ReplayBrowser.Tests/ReplayBrowser/Context/` depending on where the class lives.
- FND-T06: `SeekToFrame` calls `ClearCurrentBuffers`, then `SeekToFrame` on `PlaybackController`, then `HistoryService.Capture` — verified by spying on fake bus/history. Test all 3 calls happen in the correct order.
- FND-T07: `Dispose` disposes `PlaybackController` and `EntityRepository`; double-dispose is safe (no exception).

---

## Task 2: RB-1.5 — `RecordingExportService` Implementation (Absolute-State Path)

**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs` (UPDATE stub)

**Task Definition:** See [TASK-DETAILS.md §RB-1.5](../TASK-DETAILS.md#rb-15--recordingexportservice-implementation) and [DESIGN.md §3.4](../DESIGN.md#34-pipeline-algorithm) for the full algorithm. Code samples in design-talk.md lines 519–561, 671–706.

**What to do:**

Implement `ExportToJson` for `ExportFormatMode.AbsoluteState` only (Changelog mode is BATCH-03). Follow DESIGN.md §3.4 step by step:

1. Open `PlaybackController(fdpPath)`, read `RecordingGlobalHeader` (magic, `FormatVersion`, `Timestamp`). Assert `FormatVersion == FdpConfig.FORMAT_VERSION`.
2. Allocate sandbox `EntityRepository` and `FdpEventBus`. Create `Utf8JsonWriter` over a `FileStream` (with `new JsonWriterOptions { Indented = !options.Minified }`).
3. Emit JSON header block.
4. Seek-to-start window per DESIGN.md §3.5 rules.
5. Frame loop: `while (playback.StepForward(sandboxRepo))`:
   - End-window check → break.
   - After `ApplyFrame` (which `StepForward` does internally): capture history if `IncludeEvents`.
   - Read `GlobalTime` via `HasSingletonUnmanaged<GlobalTime>` / `GetSingletonUnmanaged<GlobalTime>()`.
   - Emit `FrameHeader` (all 9 fields per DESIGN.md §3.1 JSON schema).
   - Emit `DestroyedEntities` on delta frames.
   - If `IncludeEntities` (AbsoluteState): enumerate active entities (filtered by `TargetEntities`/`TargetEntityIndex` if set), iterate `header.ComponentMask` bits, resolve type via `ComponentTypeRegistry`, call `HasAuthority`, serialize payload via `ScenarioSerializer` built by `HrotScenarioSerializerFactory.Build(behaviorRegistry)`, run through `JsonAestheticFormatter.FlattenNumericArrays`.
   - If `IncludeEvents`: read unmanaged events via `IEventStreamInspector.InspectReadBuffer()` on active stream set, read managed events via `bus.ReadManaged<T>()` on registered types. Tag each with `IsManaged`.
6. Close JSON, dispose resources.

Key callout: accept `BehaviorRegistry` as a constructor parameter so tests can inject a stub. When no registry is provided (or when no `HrotScenarioSerializerFactory` is available in the test context), use `FdpAutoSerializer` as fallback for unknown types.

**Tests (EX-T01..EX-T26) — ALL MANDATORY for this batch:**

See DESIGN.md §3.8 for the exact assertion table. Tests go in `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs`. Use `FdpRecordingHarness` to build fixture recordings. The 26 tests cover:
- EX-T01: service constructs in isolation; no Presentation/Raylib in assembly graph
- EX-T02: round-trip — 1 keyframe + 3 deltas, Header fields correct, Frames count matches
- EX-T03..T11: JSON schema correctness (frame types, entities, HasAuthority, timing fields)
- EX-T12..T14: frame and time windowing
- EX-T15..T16: entity filtering
- EX-T17..T19: IncludeEvents/IncludeEntities/Minified flags
- EX-T20: `FlattenNumericArrays` on Vector3/Quaternion
- EX-T21: entity cross-reference strings `"[Index, vGen]"`
- EX-T22: custom `IEntityScenarioTranslator` honored
- EX-T23..T24: managed and unmanaged events
- EX-T25: peak heap < 32 MB during 10k-frame export
- EX-T26: parallel context isolation (export doesn't disturb another context's `CurrentFrame`)

---

## Task 3: RB-1.6 — `Fdp.Tools.RecordingDumper` Console App

**New project:** `FDP/Tools/Fdp.Tools.RecordingDumper/Fdp.Tools.RecordingDumper.csproj` (NEW — .NET 8 console exe)
**New test project:** `FDP/Tools/Fdp.Tools.RecordingDumper.Tests/Fdp.Tools.RecordingDumper.Tests.csproj` (NEW)

**Task Definition:** See [TASK-DETAILS.md §RB-1.6](../TASK-DETAILS.md#rb-16--fdptoolsrecordingdumper-console-app) and [DESIGN.md §3.7](../DESIGN.md#37-cli-fdptoolsrecordingdumper). Code samples in design-talk.md lines 631–707, 855–862.

**What to do:**
1. Create the `.csproj` targeting `net8.0` with reference to `Fdp.Toolkits`. Use the same `CommandLine` package (check `FDP/Examples/Fdp.Examples.Runner/Fdp.Examples.Runner.csproj` for the exact package reference).
2. Create `DumperOptions` class with `[Option]` attributes for every switch in DESIGN.md §3.7's switch table.
3. Validate mutual exclusion of `--start-frame`/`--end-frame` vs `--start-time`/`--end-time` at parse time.
4. Map to `JsonExportOptions`, invoke `RecordingExportService.ExportToJson`, return exit codes per DESIGN.md §3.7 (0=success, 1=arg error, 2=file-not-found, 3=runtime error).
5. Add both new projects to `FDP/FDP.sln`.

**Tests (EX-T30..T32 from DESIGN.md §3.8):**
- EX-T30: every switch round-trips to the correct `JsonExportOptions` field.
- EX-T31: `--start-frame` + `--start-time` together returns exit code 1 with an error message.
- EX-T32: CLI invocation `dotnet run -i fixture.fdp -o out.json --minified --no-events` produces the same JSON as calling the service directly.

Add an assembly-reference test: `Fdp.Tools.RecordingDumper` has no transitive reference to `Fdp.Presentation` or `Hrot.ClusterRunner`.

---

## Task 4: RB-1.7 — Stage 1 Acceptance Gate

After all the above is done:
1. Run `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj` — all EX-T01..T26 green.
2. Run `dotnet test FDP/Tools/Fdp.Tools.RecordingDumper.Tests/Fdp.Tools.RecordingDumper.Tests.csproj` — EX-T30..T32 green.
3. Verify FND-T06 and FND-T07 (context tests) are green.
4. Run `dotnet build FDP/FDP.sln` — 0 errors.
5. Note in the report: EX-T27..T29 are deferred to BATCH-03 (Changelog mode requires Stage 3 diff engine).

---

## Testing Requirements

- ALL EX-T01..T26 must pass (26 tests). EX-T27..T29 deferred.
- EX-T30..T32 must pass (3 tests).
- FND-T06, FND-T07 must pass (2 tests).
- Corrective: harness self-test must now assert destruction log and events on the correct frames.
- EX-T25 (heap < 32 MB on 10k-frame recording) is a non-negotiable performance gate.
- No `using Fdp.Presentation` anywhere in `Fdp.Toolkits/ReplayBrowser/` (the headless zone).

## Mandatory Workflow

**Complete ALL tasks before writing the report. Do NOT stop and ask for permission to run tests, fix compile errors, or refactor broken code — that is your job. Finish everything, ensure all tests are green, then write the report.**

Recommended order:
1. Corrective Task 0 (harness self-test fix) — do this first, verify it passes.
2. RB-1.4 (context) → run FND-T06 and FND-T07.
3. RB-1.5 (export service) → run EX-T01..T26 one by one, fix failures.
4. RB-1.6 (CLI) → run EX-T30..T32.
5. RB-1.7 (acceptance gate) → full suite run, record results.

---

## Success Criteria

This batch is DONE when:
- [ ] `HarnessSelfTest` now asserts destruction log content on frame 3 and events on frame 4 (reads them back from the replayed recording)
- [ ] `ReplayBrowserContext` compiles with FND-T06 and FND-T07 passing
- [ ] EX-T01..T26 all pass (absolute-state export path)
- [ ] EX-T30..T32 all pass (CLI)
- [ ] Assembly-reference test: `Fdp.Tools.RecordingDumper` has no `Fdp.Presentation` or `Hrot.ClusterRunner` transitive reference
- [ ] `dotnet build FDP/FDP.sln` succeeds with 0 errors
- [ ] Report notes EX-T27..T29 as explicitly deferred to BATCH-03

---

## Common Pitfalls to Avoid

- **`SeekToFrame` ordering**: MUST be Clear → Seek → Capture, in exactly that order. Swapping Capture before Seek causes the history service to capture stale events.
- **`HrotScenarioSerializerFactory`** lives in `Hrot.SimHost` — adding a reference from `Fdp.Toolkits` to `Hrot.SimHost` creates a circular dependency if `Hrot.SimHost` references `Fdp.Toolkits`. Inject the `BehaviorRegistry` or a `ScenarioSerializer` instance through the constructor to avoid this. Check the actual project references before deciding.
- **`FlattenNumericArrays`**: call this on the JSON node *before* writing to the `Utf8JsonWriter`, not after. The aesthetics formatter operates on `JsonNode` trees, not on raw UTF-8 bytes.
- **CLI mutual exclusion**: validate BEFORE constructing `JsonExportOptions`, not inside the service.
- **Exit codes**: use `Environment.Exit(code)` or `return` from `Main` (if `Main` returns `int`). Do NOT throw on validation errors — emit a message to stderr and exit with code 1.
- **10k-frame EX-T25**: the heap budget (< 32 MB delta) requires streaming — do NOT buffer all frames in a `List<>`. The `Utf8JsonWriter` writes directly to `FileStream`. Keep no per-frame buffers alive across iterations.

---

## Reference Materials
- **Task Defs:** [TASK-DETAILS.md](../TASK-DETAILS.md) — RB-1.4, RB-1.5, RB-1.6, RB-1.7
- **Design §3:** [DESIGN.md §3](../DESIGN.md#3-stage-1--headless-json-export-pipeline)
- **Design §4.2:** [DESIGN.md §4.2](../DESIGN.md#42-sandbox-context)
- **Code samples:** [design-talk.md](../design-talk.md) lines 519–561, 631–707, 855–862, 964–999
- **CommandLine package example:** `FDP/Examples/Fdp.Examples.Runner/Program.cs` and `.csproj`
- **FDP solution:** `FDP/FDP.sln`
- **Existing test structure reference:** `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
- **BATCH-01 Review:** `.dev/replay-browser-2/reviews/BATCH-01-REVIEW.md`
