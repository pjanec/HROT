# BATCH-01: Stage 1 Foundation — Audit, Harness, DTOs, and Contract

**Batch Number:** BATCH-01
**Tasks:** RB-1.0, RB-1.1, RB-1.2, RB-1.3
**Phase:** Stage 1 — Headless JSON Export Pipeline (Foundation)
**Estimated Effort:** 10–14 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Developer Instructions

This is the first batch for the **FDP Replay Browser** workstream. You are building the headless JSON export pipeline for `.fdp` flight recordings. This batch covers the foundational work: codebase audit and gap fixes, the reusable test substrate, the domain DTOs, and the service interface contract. No ImGui or Raylib code is touched in this batch.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — how batches work
2. **Onboarding:** `.dev/replay-browser-2/ONBOARDING.md` — project overview
3. **Design Document:** `.dev/replay-browser-2/DESIGN.md` — architecture and JSON schema
4. **Task Definitions:** `.dev/replay-browser-2/TASK-DETAILS.md` — see RB-1.0, RB-1.1, RB-1.2, RB-1.3
5. **Design Talk (code samples):** `.dev/replay-browser-2/design-talk.md` — lines referenced in each task below

### Source Code Locations
- **Audit targets:** `FDP/Engine/Fdp.Core/ComponentType.cs`, `FDP/Engine/Fdp.Core/EventType.cs`, `FDP/Engine/Fdp.Core/`
- **New headless code:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/` (create folder)
- **New helper extensions (if needed):** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RepositoryExtensions.cs`
- **Test project:** `FDP/Toolkits/Fdp.Toolkits.Tests/` (existing project, add `ReplayBrowser/` subfolder)
- **Existing tests for context:** `FDP/Engine/Fdp.Core.Tests/`

### Report Submission
**When done, submit your report to:**
`.dev/replay-browser-2/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/replay-browser-2/questions/BATCH-01-QUESTIONS.md`

---

## Context

Stage 1 is the backend-first export pipeline. Before writing any production code, RB-1.0 verifies codebase anchors are present and adds any missing accessor APIs. RB-1.1 builds the `FdpRecordingHarness` test substrate — all Stage 1/3/4 tests use it to produce real `.fdp` files. RB-1.2 lands the domain DTOs. RB-1.3 declares the service interface with a stub implementation. The actual export logic and CLI ship in BATCH-02.

**Related Tasks:**
- [RB-1.0](../TASK-DETAILS.md#rb-10--codebase-audit-and-gap-fix) — Audit and patch accessor APIs
- [RB-1.1](../TASK-DETAILS.md#rb-11--fdprecordingharness-test-substrate) — Test substrate
- [RB-1.2](../TASK-DETAILS.md#rb-12--domain-dtos-jsonexportoptions-changelogentrydto-enums) — Domain DTOs
- [RB-1.3](../TASK-DETAILS.md#rb-13--irecordingexportservice-contract) — Service interface

---

## Batch Objectives

1. Verify and patch any missing enumeration APIs on `ComponentTypeRegistry` and `EventType`.
2. Build `FdpRecordingHarness` — the reusable in-test recording builder that produces real `.fdp` binary files.
3. Land `JsonExportOptions`, `ExportWindowMode`, `ExportFormatMode`, and `ChangelogEntryDto`.
4. Declare `IRecordingExportService` with a `NotImplementedException` stub, ensuring zero dependency on `Fdp.Presentation` or `Raylib`.

---

## Tasks

### Task 1: RB-1.0 — Codebase Audit and Gap Fix

**Files:** `FDP/Engine/Fdp.Core/ComponentType.cs`, `FDP/Engine/Fdp.Core/EventType.cs`, optionally `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RepositoryExtensions.cs`

**Task Definition:** See [TASK-DETAILS.md §RB-1.0](../TASK-DETAILS.md#rb-10--codebase-audit-and-gap-fix) for the exact concrete steps.

**What to do:**
1. Open `FDP/Engine/Fdp.Core/ComponentType.cs` and check whether `ComponentTypeRegistry` (or the same class that maintains the map of registered types) exposes a method that returns a `IReadOnlyList<Type>` or `IEnumerable<Type>` of all registered component types. If it does not, add `public static IReadOnlyList<Type> GetAllRegistered()` that returns a snapshot of the internal map's type list.
2. Do the same for `EventType` in `FDP/Engine/Fdp.Core/EventType.cs`.
3. Check `EntityRepository` for `HasComponentByTypeId(Entity, int typeId)`. If absent, add a small static helper extension method `RepositoryExtensions.HasComponentByTypeId(this EntityRepository repo, Entity entity, int typeId)` at `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RepositoryExtensions.cs` using the documented fallback `repo.GetHeader(e.Index).ComponentMask.IsSet(typeId)`.

**Tests required (in `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Audit/`):**
- A test that registers two distinct component types and asserts both appear in `ComponentTypeRegistry.GetAllRegistered()`.
- A test that registers two event types and asserts both appear in `EventType.GetAllRegistered()`.
- If the extension helper was added: a positive test (`HasComponentByTypeId` returns true when the component is present) and a negative test (returns false when absent).

---

### Task 2: RB-1.1 — `FdpRecordingHarness` Test Substrate

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarness.cs` (NEW FILE in the test project)

**Task Definition:** See [TASK-DETAILS.md §RB-1.1](../TASK-DETAILS.md#rb-11--fdprecordingharness-test-substrate) for all fluent method signatures and behavior.

**What to do:**

Build the harness exactly per TASK-DETAILS.md §RB-1.1. The harness must:
- Allocate an `EntityRepository`, a `RecorderSystem`, and a temp-file `Stream` (use `Path.GetTempFileName()` + `FileStream`, or `MemoryStream` for small recordings).
- Expose fluent methods: `SpawnEntity()`, `WithComponent<T>(...)`, `MutateComponent<T>(entity, mutator)`, `FireUnmanagedEvent<T>(...)`, `FireManagedEvent<T>(...)`, `AddComponent<T>(entity, ...)`, `RemoveComponent<T>(entity)`, `DestroyEntity(entity)`, `Tick()`, `RecordKeyframe()`, `RecordDelta()`, `BuildToTempFile(out string path)`, `BuildToStream(out Stream s)`.
- Call `EntityRepository.Tick()` (or equivalent method that advances `GlobalVersion`/`GlobalTime`) once per `Tick()` call.
- Return a path or stream usable directly by `new PlaybackController(path)`.
- Implement `IDisposable` — `Dispose` deletes any temp files created.

**Self-test (`HarnessSelfTest`) — add to `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarnessTests.cs`:**
- Instantiate a 3-entity, 5-frame recording: 1 keyframe + 4 deltas. Destroy entity on tick 3. Fire a managed + unmanaged event on tick 4.
- Call `BuildToTempFile(out string path)`.
- Open with `new PlaybackController(path)` (or `RecordingReader`) and step through: assert the tick/FrameType/WallClockTicks sequence matches what the test orchestrated.
- Call `Dispose()` and assert the temp file no longer exists.

---

### Task 3: RB-1.2 — Domain DTOs

**Files (NEW, all in `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/`):**
- `JsonExportOptions.cs`
- `ChangelogEntryDto.cs`

**Task Definition:** See [TASK-DETAILS.md §RB-1.2](../TASK-DETAILS.md#rb-12--domain-dtos-jsonexportoptions-changelogentrydto-enums) and [DESIGN.md §3.2](../DESIGN.md#32-domain-models-fdptoolkitsreplaybrowser) for exact field names and defaults. The verbatim DTO source is in DESIGN.md §3.2. Put `ExportWindowMode` and `ExportFormatMode` enums in `JsonExportOptions.cs`.

**What to do:**
- `JsonExportOptions` fields and defaults must match DESIGN.md §3.2 exactly (including `EndFrame = int.MaxValue`, `EndTimeSec = float.PositiveInfinity`, etc.).
- `ChangelogEntryDto` is a `record` with the fields listed in DESIGN.md §3.2. For now, `IReadOnlyList<DiffNode> Mutations` can reference a placeholder abstract class `DiffNode` in `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/DiffNode.cs` (just the base with `Name` and `IsModified`; the full hierarchy lands in Stage 3 / BATCH-03).
- `FdpJsonOptionsRegistry` is an existing class — do not recreate it; import it.

**Tests required (in `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/`):**
- Instantiate `JsonExportOptions` with no constructor args and assert every default value matches DESIGN.md §3.2.
- Round-trip JSON test: `JsonSerializer.Serialize` → `JsonSerializer.Deserialize` through `FdpJsonOptionsRegistry.Indented` options and assert every field is preserved (including `List<Entity> TargetEntities`).

---

### Task 4: RB-1.3 — `IRecordingExportService` Contract

**Files (NEW, in `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/`):**
- `IRecordingExportService.cs`
- `RecordingExportService.cs` (stub throwing `NotImplementedException`)

**Task Definition:** See [TASK-DETAILS.md §RB-1.3](../TASK-DETAILS.md#rb-13--irecordingexportservice-contract) and [DESIGN.md §3.3](../DESIGN.md#33-service-contract) for the exact interface signature.

**What to do:**
- The interface must have exactly the signature from DESIGN.md §3.3: `void ExportToJson(string inputFdpPath, string outputJsonPath, JsonExportOptions options)`.
- The stub `RecordingExportService` must compile and have zero transitive references to `Fdp.Presentation` or any `Raylib*` assembly.

**Tests required (in `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/`):**
- An **assembly-reference test**: load `typeof(RecordingExportService).Assembly` and assert that `GetReferencedAssemblies()` contains no entry whose name starts with `Fdp.Presentation` or `Raylib`.

---

## Testing Requirements

- All new tests go under `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/` in appropriate sub-folders.
- All tests must pass with `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`.
- No test may reference `Fdp.Presentation`, `Raylib`, or `ImGui`.
- The harness self-test (`HarnessSelfTest`) is mandatory and must verify byte-level correctness by re-reading the produced `.fdp` file.

## Mandatory Workflow

**Complete all four tasks fully before writing the report. Do NOT stop and ask if it's OK to run tests or fix compile errors — run them, fix the root cause, and iterate until all pass. No permission needed for obvious next steps.**

Order of work:
1. RB-1.0 (audit + patch) → run any impacted existing tests to make sure nothing broke.
2. RB-1.1 (harness) → run `HarnessSelfTest` until it passes.
3. RB-1.2 (DTOs) → run DTO tests.
4. RB-1.3 (interface + stub) → run assembly-reference test.
5. Final: `dotnet build` of the FDP solution + `dotnet test` of `Fdp.Toolkits.Tests`. All green before submitting the report.

---

## Success Criteria

This batch is DONE when:
- [ ] `ComponentTypeRegistry.GetAllRegistered()` exists and its unit test passes.
- [ ] `EventType.GetAllRegistered()` exists and its unit test passes.
- [ ] `RepositoryExtensions.HasComponentByTypeId` exists (if needed) with passing tests.
- [ ] `FdpRecordingHarness` compiles and `HarnessSelfTest` passes (5-frame recording, read back correctly, no temp file leak).
- [ ] `JsonExportOptions` defaults test passes.
- [ ] `JsonExportOptions` JSON round-trip test passes.
- [ ] `IRecordingExportService` interface and stub compile.
- [ ] Assembly-reference test asserts no `Fdp.Presentation`/`Raylib` in the toolkits assembly.
- [ ] `dotnet build` of the FDP solution passes with zero errors.
- [ ] `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj` is all green.

---

## Common Pitfalls to Avoid

- **Do not create a new `FdpConfig` or `ComponentTypeRegistry`** — these already exist in `Fdp.Core`. Find them and extend if necessary.
- **The harness must produce real `.fdp` binary files**, not mocked/fake structures. Use `RecorderSystem` and `PlaybackController` exactly as the production code would.
- **`ChangelogEntryDto.Mutations` type**: use a placeholder stub `DiffNode` base class now; the full hierarchy is Stage 3. Ensure it compiles.
- **Temp file cleanup**: the harness `Dispose` must delete temp files. Use `try/finally` or `IDisposable` pattern.
- **Do not add `using Fdp.Presentation` anywhere in the new `ReplayBrowser/` folder** — this is a headless zone.

---

## Reference Materials
- **Task Definitions:** [TASK-DETAILS.md](../TASK-DETAILS.md) — RB-1.0, RB-1.1, RB-1.2, RB-1.3
- **Design:** [DESIGN.md](../DESIGN.md) — §3.2 (DTOs), §3.3 (interface), §1 (anchor table)
- **Code samples:** [design-talk.md](../design-talk.md) — lines 879–906 (DTOs), 2142–2160, 2167–2172
- **FDP solution:** `FDP/FDP.sln`
- **Test project:** `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
- **Existing usage pattern for RecorderSystem:** search in `FDP/Engine/Fdp.Core.Tests/` for `RecorderSystem`
