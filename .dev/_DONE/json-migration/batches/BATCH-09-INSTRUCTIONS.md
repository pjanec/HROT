# BATCH-09 — Phase 2 Foundation: Integration-Patches Doc + HROT Module Skeletons

**Batch Number:** BATCH-09
**Tasks:** JM-P2-001, JM-P2-002
**Phase:** Phase 2 — Envelope rollout (foundation)
**Estimated Effort:** 8-12 hours
**Workspace root:** `d:\Work\IOS-IG-SimHost-FDP`

---

## 1. Onboarding

### 1.1 Codebase background

Phase 1 is complete and approved (BATCH-08 review, all 350 tests pass). The migration library
lives in `FDP/Engine/Fdp.Core/Serialization/Migrations/` and is fully tested. See:
- `.dev/json-migration/reviews/BATCH-08-REVIEW.md` — what Phase 1 delivered
- `.dev/json-migration/ONBOARDING.md` — project structure overview
- `.dev/json-migration/TASK-DETAILS.md` — per-task deliverables and success conditions
- `.dev/json-migration/Migration-system.md` — the full 7-part design (5500+ lines)

**Codebase-fit corrections that apply throughout:**

| ID | Rule |
|----|------|
| C-1 | `BehaviorTree` is NOT a standalone versioned format; no `.bt.json` load path exists. `HrotDocumentTypes.BehaviorTree` constant may exist but `BehaviorTreeMigrationModule` is NOT created. |
| C-2 | Unified `NodeBootstrapper` (role-driven). `MigrationServices` wires into the existing `NodeBootstrapper`. |
| C-4 | `OrchestratorContext` registers at `currentVersion = 2` (not 1). Disk files already have `schemaVersion: 2`. |
| C-7 | xUnit only, no FluentAssertions. Use `Assert.*` in tests. |
| C-8 | New test project `Hrot.Common.Tests` is added in this batch to host HROT-side module tests. |
| C-9 | Toolkit folder is `Fdp.Toolkits` (singular project, plural folder). |

Full correction list: `.dev/json-migration/TASK-DETAILS.md`, section "Codebase-fit corrections".

### 1.2 Previous batch notes

No debt items from BATCH-08 carry into this batch. All Phase 1 debt is RESOLVED.

---

## 2. Task Assignments

### TASK A — JM-P2-001: Write the integration-patches document

**Full spec:** `.dev/json-migration/TASK-DETAILS.md#jm-p2-001--write-integration-patches-document-doc-05`
**Design ref:** *07 §11* (Migration-system.md, section "Phase 2: Envelope rollout", §11 of doc 07)

**Deliverable:** `.dev/json-migration/05-integration-patches.md`

This is a **research and documentation task**. You must survey the codebase and produce a
precise technical document cataloguing every JSON read/write touchpoint in the engine. The
document is the architect's blueprint that gates all Phase 2 code patches (JM-P2-003+).

**Required format for each touchpoint:**

```markdown
### <Touchpoint Name> — <JM-P2-XXX>

**File(s):**
- `path/to/file.cs` (class name, method name)

**Current JSON shape (summary):**
<Describe how the file currently reads/writes JSON, what version fields it uses>

**Target shape:**
<Describe what changes: $meta added, Header.SchemaVersion removed, which adapter type>

**Adapter type:** ReadOnlyMigrationAdapter | PersistentMigrationAdapter | JsonEnvelope.Write (passthrough)

**DocType constant:** `HrotDocumentTypes.X` or `FdpDocumentTypes.X` (version N)

**Call-site patch (pseudo-code):**
<Brief before/after pseudocode for the key change>
```

**Touchpoints to catalogue (from TASK-DETAILS.md#jm-p2-001):**

1. **Scenario read/write paths** (JM-P2-003):
   - `ScenarioFileService.LoadScenario` / `SaveScenario` — `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs`
   - `ScenarioSerializer` in `Fdp.Toolkits` — find via grep for `ScenarioSerializer`
   - `HrotScenarioLoadHandler.PrepareAsync` — `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs`

2. **Blueprint read/write paths** (JM-P2-004):
   - `BlueprintJsonServices` — `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/BlueprintJsonServices.cs`

3. **TKB read/write paths** (JM-P2-005):
   - `TkbLoadClusterStateHandler` — find in `Hrot/Subsystems/Hrot.SimHost/`
   - Any editor-side TKB writers

4. **Road network read/write paths** (JM-P2-006):
   - `RoadNetworkLoader.LoadFromJson` — `FDP/Toolkits/Fdp.Toolkits/CarKinem/Road/RoadNetworkLoader.cs`
   - Any editor-side road network writers

5. **Replay metadata paths** (JM-P2-007, per C-5):
   - `RecordingDumper` / `Program.cs` — `FDP/Tools/Fdp.Tools.RecordingDumper/Program.cs`
   - `ReplayBrowserContext` — `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs`
   - `TransientMasterBuilder` — `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/TransientMasterBuilder.cs`
   - `RecordingExportService` — `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs`

6. **Passthrough writers** (JM-P2-008, per C-4, C-5):
   - `GlobalContextClusterOpHandler` — `Hrot/Subsystems/Hrot.Orchestrator/GlobalContextClusterOpHandler.cs` (v2, C-4)
   - `NedExConEgressWriters` (MapInteractionConfig) — grep for `MapInteractionConfig`
   - `NodeConfiguration` — grep for `NodeConfiguration`
   - `EditDocumentJsonSerializer` (StructEdit) — `FDP/ExtDeps/StructEdit/src/StructEdit.Json/`

7. **Editor UI hooks** (deferred to Phase 4, but enumerate here for completeness):
   - Warning modal: `ScenarioFileService` after `LoadScenario` (Phase 4 JM-P4-001)
   - Degraded-mode banner (Phase 4 JM-P4-002)
   - Migration history menu (Phase 4 JM-P4-003)

8. **CLI entrypoint** (deferred to Phase 4, enumerate here):
   - `Hrot.ClusterRunner --mode migrate` (Phase 4 JM-P4-004)

**How to research each touchpoint:**

For each file listed above:
1. Read the file and identify the JSON serialization pattern
2. Note: what fields carry the version? (`Header.SchemaVersion`, `SchemaVersion`, none?)
3. Note: is it a read path, write path, or both?
4. Note: current using namespace/imports for JSON

**Correction notes for specific touchpoints:**
- `ScenarioHeader.SchemaVersion: int` (Fdp.Toolkits) and `ScenarioHeaderDto.SchemaVersion: string "1.0"` (Hrot.Core) are BOTH removed in Phase 2 (C-3). `$meta.schemaVersion` becomes canonical.
- `GlobalContextClusterOpHandler` already writes `schemaVersion: 2`; Phase 2 wraps it in `$meta` at version 2 and strips the naked field (C-4).
- `StructEdit`'s strict `"1.0"` equality check is retired; envelope-based passthrough takes over (C-3).

**Success condition:** The document is complete enough that a developer can implement JM-P2-003 through JM-P2-009 by reading it alongside the design. Each touchpoint must show current shape, target shape, adapter type, and docType constant.

---

### TASK B — JM-P2-002: HrotDocumentTypes + PassthroughFormatsModule + skeleton modules

**Full spec:** `.dev/json-migration/TASK-DETAILS.md#jm-p2-002--hrotdocumenttypes--passthroughformatsmodule-hrot-side`
**Design refs:** *02 §9.2*, *03 §9.1*, *03 §9.2*

#### B.1 HrotDocumentTypes.cs

**File:** `Hrot/Engine/Hrot.Common/Scenario/HrotDocumentTypes.cs`

Create this class expanding from the existing `HrotSubsystemTypes.cs`. The existing
`HrotSubsystemTypes.cs` must NOT be deleted (it's used by `ScenarioSerializer` and others);
create `HrotDocumentTypes.cs` as a new file alongside it.

The design spec for this class is in Migration-system.md §9.2 (search for "HrotDocumentTypes").
Key rules from C-1 and C-4:
- Include `BehaviorTree = "Hrot.BehaviorTree"` as a constant, but it is NOT registered in any module (C-1).
- `OrchestratorContext` passthrough registers at version **2** (C-4).

The subsystem identifiers (`SimHostSubsystem`, `CgfSubsystem`, `IgSubsystem`) that were
in `HrotSubsystemTypes` should also appear in `HrotDocumentTypes` for routing consistency.
Do NOT modify `HrotSubsystemTypes.cs` — it remains as-is for backward compatibility.

#### B.2 Skeleton migration modules

Create in `Hrot/Engine/Hrot.Common/Scenario/Migrations/`:

1. **PassthroughFormatsModule.cs** — registers all engine-internal formats as passthrough.
   Design spec: Migration-system.md §9.2 ("PassthroughFormatsModule"). Apply C-4: register
   `OrchestratorContext` at version 2 (not 1 as shown in the example).
   Formats: `StructEdit (1)`, `MapInteractionConfig (1)`, `OrchestratorContext (2)`, `TestScript (1)`, `NodeConfiguration (1)`.

2. **ScenarioMigrationModule.cs** — skeleton for the Scenario format.
   Design spec: Migration-system.md §9.1. `CurrentVersion = 1`. `RegisterAll` calls
   `registry.RegisterDocType(HrotDocumentTypes.Scenario, currentVersion: 1, migrators: Array.Empty<IJsonDocumentMigrator>())`.
   **Note:** `RegisterDocType` with empty migrators means the format is at version 1 with no
   chain. Check MigrationRegistry API - if RegisterDocType with empty array is not allowed,
   use `RegisterPassthroughDocType` for the skeleton but keep CurrentVersion = 1.

3. **BlueprintMigrationModule.cs** — skeleton for Blueprint format. Same pattern as Scenario.
   `CurrentVersion = 1`. DocType = `HrotDocumentTypes.Blueprint`.

4. **TkbMigrationModule.cs** — skeleton for TKB format. Same pattern.
   `CurrentVersion = 1`. DocType = `HrotDocumentTypes.TkbDefinition`.

5. **RoadNetworkMigrationModule.cs** — skeleton for RoadNetwork format. Same pattern.
   DocType = `FdpDocumentTypes.RoadNetwork` (this type lives in Fdp.Core, not Hrot.Common).
   `CurrentVersion = 1`.

**Important note on RegisterDocType vs RegisterPassthroughDocType:**
Read `MigrationRegistry.cs` in `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationRegistry.cs`
before implementing. If `RegisterDocType` requires at least one migrator pair, use
`RegisterPassthroughDocType` for all skeleton modules (since they have no migrators yet).
The `CurrentVersion = 1` constant is set at the skeleton stage; it will be bumped in JM-P3-003.

#### B.3 New test project: Hrot.Common.Tests

Create `Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj`.

Model it after `FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj` for project structure.
The project needs:
- Framework: `net8.0`
- References: `Hrot.Common`, `Fdp.Core` (for `MigrationRegistry` and related types)
- xUnit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk NuGet packages

**Tests to add** in `Hrot/Engine/Hrot.Common.Tests/Migrations/ModuleRegistrationTests.cs`:

Each test creates a fresh `MigrationRegistry`, calls `module.RegisterAll(registry)`, and
asserts that no exception is thrown and the expected docType is registered.

- **JM-P2-002-T01** `PassthroughFormatsModule_RegisterAll_RegistersFiveDocTypes`
  Verify that after `PassthroughFormatsModule.RegisterAll(reg)`, calling
  `reg.Seal()` does not throw, and the registry accepts (without throwing) a lookup
  for each of the 5 registered types. Use `MigrationBootstrap.Build` with the module
  to confirm build-and-seal works cleanly.

- **JM-P2-002-T02** `ScenarioMigrationModule_RegisterAll_RegistersScenarioDocType`
  Verify current version is 1 and registration does not throw.

- **JM-P2-002-T03** `BlueprintMigrationModule_RegisterAll_RegistersBlueprintDocType`
  Same check for Blueprint.

- **JM-P2-002-T04** `TkbMigrationModule_RegisterAll_RegistersTkbDocType`
  Same check for TKB.

- **JM-P2-002-T05** `RoadNetworkMigrationModule_RegisterAll_RegistersRoadNetworkDocType`
  Same check for RoadNetwork.

- **JM-P2-002-T06** `HrotDocumentTypes_AllConstantsAreNonEmpty`
  Use reflection to enumerate all `public const string` fields on `HrotDocumentTypes`
  and assert none is null or empty.

- **JM-P2-002-T07** `OrchestratorContext_RegistersAtVersionTwo`
  Create a registry, call `PassthroughFormatsModule.RegisterAll`, then use
  `MigrationBootstrap.Build` to get `MigrationServices`. Call
  `services.ReadOnly.LoadAsync(stream, "Hrot.OrchestratorContext")` on a JSON string
  with `$meta.schemaVersion: 2` and verify it loads without error (no migration needed
  since current == 2). This confirms C-4 is correctly applied.

**Test for MigrationRegistry internals access:**
Add `InternalsVisibleTo("Hrot.Common.Tests")` to `Hrot.Common.csproj`.

#### B.4 Add Hrot.Common.Tests to the solution

Add the new test project to `IOS-IG-SimHost.sln`:
```
dotnet sln IOS-IG-SimHost.sln add Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj
```

---

## 3. Test-Driven Task Progression

**Mandatory workflow — follow exactly:**

```
For each deliverable:
  1. Read the existing code that will be touched or extended.
  2. Write the test(s) first (or alongside) the implementation.
  3. Confirm the tests compile and fail (red) before implementing.
  4. Implement until tests pass (green).
  5. Run the full test suite to confirm no regressions:
     dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4
     dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" -c Debug
     dotnet test "FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj" -c Debug --no-build
  6. Only mark a task done when all its tests pass and zero build errors remain.
```

**Do not proceed to the next task until the current one is green.**

---

## 4. Build Verification

Before writing the report, run:

```powershell
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4 2>&1 | Select-String "error CS|Build succeeded|Build FAILED" | Select-Object -Last 5
dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" -c Debug 2>&1 | Select-Object -Last 10
dotnet test "FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj" -c Debug --no-build 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 3
```

All three must show clean results.

---

## 5. Report Format

Write your completion report to `.dev/json-migration/reports/BATCH-09-REPORT.md`.

Structure:

```markdown
# BATCH-09 Report

**Status:** Complete | Partial | Blocked
**Tests:** X/X passing

## Tasks Completed
- [ ] JM-P2-001: 05-integration-patches.md written
- [ ] JM-P2-002: HrotDocumentTypes + modules + Hrot.Common.Tests

## Developer Insights

### Issues Encountered
<What went wrong, what was unclear, what took longer than expected>

### Weak Points Spotted
<Any fragile code, missing coverage, design ambiguity observed in the codebase>

### Design Decisions Beyond the Spec
<Anything you decided that the spec didn't fully prescribe, and why>

## JM-P2-001 Summary
<Key findings from the codebase survey — which touchpoints were straightforward,
which ones had surprises (e.g., unexpected version fields, missing files)>

## JM-P2-002 Summary
<Module and test project implementation notes; any deviations from the spec>

## Build / Test Results
<Paste relevant build output>

## Files Created / Modified
<List of all files touched>
```

---

## 6. Notes for Autonomous Work

- Do not stop for questions unless a critical design conflict is found.
- If `RegisterDocType` with empty migrators throws, use `RegisterPassthroughDocType` for
  the skeleton phase — document this decision in your report.
- `MigrationRegistry` is in `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationRegistry.cs`.
  Read it before writing the module skeleton code.
- The `Hrot.Common.csproj` already references `Fdp.Core`, so `MigrationRegistry` and
  `IJsonDocumentMigrator` are available in `Hrot.Common` without extra project references.
- `FdpDocumentTypes.RoadNetwork` is in `FDP/Engine/Fdp.Core/Serialization/FdpDocumentTypes.cs`.
  `RoadNetworkMigrationModule` in `Hrot.Common` can reference it via the existing `Fdp.Core`
  project reference.
- Your role is described in `.github/skills/developer/SKILL.md`.
