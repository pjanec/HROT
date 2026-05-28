# BATCH-02: Debt fixes + MigrationPipeline + DomDiffer Extraction

**Batch Number:** BATCH-02
**Tasks:** D-001 (P2 debt fix), D-002/D-003 (P3 debt fixes), JM-P1-006, JM-P1-007
**Phase:** Phase 1 — Core infrastructure
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 completed and committed

---

## Developer Role

Your role is described in `.github\skills\developer\SKILL.md`. Read it before starting.

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Onboarding:** `.dev/json-migration/ONBOARDING.md`
2. **Previous review:** `.dev/json-migration/reviews/BATCH-01-REVIEW.md` — read all issues found; Corrective Task 0 below fixes D-001.
3. **Task Details:** `.dev/json-migration/TASK-DETAILS.md` — sections JM-P1-006 and JM-P1-007.
4. **Design — Interfaces:** `Migration-system.md` doc 03 §3.4 (pipeline invariants), §4.2 (MigrationPipeline contract), §3.4 invariants 1-4.
5. **Design — Test plan:** `Migration-system.md` doc 06 §3.5 (T1-120..T1-139 pipeline tests), §3.7 (T1-220..T1-229 DomDiffer tests).
6. **Existing diff source:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/DiffNode.cs` and `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/ComponentDiffService.cs` — study before extracting.

### Source Code Locations

- **Implementation:** `FDP/Engine/Fdp.Core/Serialization/Migrations/` (add `MigrationPipeline.cs`)
- **Internal diff types:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/Diff/` (new subdirectory)
- **Existing diff (to extract from):** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/`
- **Test project:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/`
- **Toolkits project file:** `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`
- **Core project file:** `FDP/Engine/Fdp.Core/Fdp.Core.csproj`

### Build & Test Commands

```powershell
# Build both affected projects
dotnet build FDP/Engine/Fdp.Core/Fdp.Core.csproj
dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj

# Run migration tests only
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --filter "FullyQualifiedName~Migrations"

# Full regression: run ALL core tests + ALL toolkits tests
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj
dotnet test FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj  # if toolkits has tests
```

> Check whether `Fdp.Toolkits` has its own test project; if so, run it. The instruction in TASK-DETAILS JM-P1-007 says "all existing Fdp.Toolkits tests touching diff (ReplayBrowser tests) still pass."

### Report Submission

**When done, submit your report to:**
`.dev/json-migration/reports/BATCH-02-REPORT.md`

---

## Context

BATCH-01 delivered the foundation types, envelope, JSONPath, context, and registry. BATCH-02 first fixes the debt identified in BATCH-01's review, then builds on top:

- **MigrationPipeline** (JM-P1-006): the orchestrator that routes a JSON document through the registry's migrator chain, enforces post-migrator invariants, and returns a `MigrationReport`.
- **DomDiffer extraction** (JM-P1-007): moves the pure DOM-diff logic currently living in `Fdp.Toolkits.ReplayBrowser.Diff` down into `Fdp.Core.Serialization.Migrations.Internal.Diff`, then rewires `ComponentDiffService` to use the extracted types. This clears the way for JM-P1-008's `DiffToJournalConverter`.

JM-P1-006 and JM-P1-007 are **independent** per the design (doc 07 §4.1 Step 6 explicitly notes they can run in parallel). Implement whichever order is convenient; all three must pass before the report.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

0. **Corrective Task 0 (debt D-001, D-002, D-003):** Fix issues → Update tests → **ALL tests pass** ✅
1. **JM-P1-006 (MigrationPipeline):** Implement → Write T1-120..T1-139 → **ALL tests pass** ✅
2. **JM-P1-007 (DomDiffer extraction):** Extract + rewire → Write T1-220..T1-229 → **ALL existing diff tests still pass** ✅

**DO NOT** move to the next task until current task tests are all green.

**DO NOT stop to ask permission. Work autonomously. Fix failures immediately.**

---

## ✅ Tasks

### Corrective Task 0: Fix BATCH-01 debt items

#### D-001 (P2): Wrap non-integer `schemaVersion` as `MigrationException`

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/JsonEnvelope.cs`
**Problem:** In `ReadMetaObject`, the line `schemaVersion = reader.GetInt32();` throws `InvalidOperationException` (not `MigrationException`) when the token is not a number. Callers catching only `MigrationException` miss this.
**Fix:** Wrap the `reader.GetInt32()` call in a try-catch:
```csharp
case FieldSchemaVersion:
    try { schemaVersion = reader.GetInt32(); }
    catch (Exception ex) when (ex is InvalidOperationException or FormatException)
    {
        throw new MigrationException(
            $"'{MetaFieldName}.{FieldSchemaVersion}' must be an integer; got token type {reader.TokenType}.",
            innerException: ex);
    }
    break;
```

**Test update:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/JsonEnvelopeTests.cs` — test `Peek_NonIntegerSchemaVersion_ThrowsMigrationException` (T1-010): change `Assert.ThrowsAny<Exception>` to `Assert.Throws<MigrationException>`.

#### D-002 (P3): Remove `Direction` from `IJsonDocumentMigrator`

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/IJsonDocumentMigrator.cs`
**Fix:** Remove the `Direction` property from the interface. The registry already infers direction from the version delta (`diff == 1` → Up, `diff == -1` → Down) and validates it independently.
Update `MigrationRegistry.cs` to remove the `m.Direction != expectedDirection` check (it becomes redundant). Remove `Direction` from `StubMigrator.cs` in tests.

**Important:** Check if any other code references `IJsonDocumentMigrator.Direction` and update all callers. Run a workspace search for `.Direction` on types that implement `IJsonDocumentMigrator` to ensure all are cleaned up.

#### D-003 (P3): Make `MigrationReport.AddWarning(string)` internal

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationReport.cs`
**Fix:** Change the `public void AddWarning(string message)` overload to `internal void AddWarning(string message)`. Migrators must use `ctx.AddWarning(message)` which properly captures the path; direct calls to `Report.AddWarning` with a hardcoded `"$"` path are a footgun.

---

### Task 1: MigrationPipeline (JM-P1-006)

**Full task spec:** [TASK-DETAILS.md §JM-P1-006](../TASK-DETAILS.md#jm-p1-006--migrationpipeline-gate)
**Design refs:** `Migration-system.md` doc 03 §3.4 invariants 1-4, §4.2 (pipeline algorithm); doc 07 §4.1 Step 6.

**File to create:** `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationPipeline.cs`

**Key contracts (verify against doc 03 §4.2):**
- Constructor takes `MigrationRegistry`.
- `MigrateToCurrent(JsonObject root, string? sourcePath = null)` — migrates to the current version registered for the document's docType.
- `MigrateTo(JsonObject root, int targetVersion, string? sourcePath = null)` — migrates to an explicit version.
- After **each** migrator in the chain returns, the pipeline checks **all four invariants** from doc 03 §3.4:
  1. `root["$meta"]` object identity is unchanged (same object reference — the migrator must not replace `$meta`).
  2. `$meta.docType` is unchanged.
  3. `$meta.schemaVersion` is unchanged by the migrator (the pipeline sets it, the migrator must not).
  4. Diagnostic fields `engineVersion`, `createdBy`, `createdUtc` are unchanged.
  Any violation throws `MigrationException` with a message identifying which invariant and which migrator.
- The pipeline **sets** `$meta.schemaVersion` after a successful migrator step (to `ToVersion`).
- If the migrator throws, the pipeline catches the exception, augments it with `MigrationContext.CurrentPath` if it's not already a `MigrationException`, and re-throws.
- Returns `MigrationReport` with `DocType`, `FromVersion`, `ToVersion`, `Direction`, `Duration` (measured in the pipeline), and any warnings/notes added by migrators.
- Passthrough docTypes: return empty report immediately (no migrators run).

**Synthetic test migrators** — add to a new file `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/TestMigrators.cs`:

The schema evolution for `"Test.Doc"` across three versions:
- v1: `{ "$meta": { "docType": "Test.Doc", "schemaVersion": 1 }, "items": [ { "name": "..." } ] }`
- v2: adds `"kind": "default"` to each item. Up: for each item in `items`, add `"kind": "default"`. Down: remove `"kind"` from each item.
- v3: adds `"metadata": {}` to each item. Up: for each item, add `"metadata": {}`. Down: remove `"metadata"` from each item.

Implement these four migrators. Also implement a misbehaving migrator for invariant tests:
```csharp
internal sealed class TestDocV1ToV2_ViolatesMeta : IJsonDocumentMigrator
{
    // Changes docType inside $meta — pipeline must detect and throw.
    public void Apply(JsonObject root, MigrationContext ctx)
    {
        root["$meta"]!.AsObject()["docType"] = "Test.OtherDoc";
    }
}
```

**Tests to implement:** `T1-120` through `T1-139` in `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/MigrationPipelineTests.cs`.

Key tests requiring special attention:
- **T1-120** (`MigrateToCurrent_AlreadyCurrent_ReturnsEmptyReport`): verify `Report.Warnings` empty, `Report.Notes` empty, `Report.Direction` is `Up` or some neutral value.
- **T1-130** (`MigrateToCurrent_MigratorTouchesMeta_PipelineThrows`): use `TestDocV1ToV2_ViolatesMeta`; must throw `MigrationException` with a message identifying the invariant.
- **T1-131** (`MigrateToCurrent_MigratorReplacesMetaObject_PipelineThrows`): a migrator that calls `root["$meta"] = new JsonObject()` must be detected.
- **T1-132** (`MigrateToCurrent_MigratorChangesSchemaVersion_PipelineThrows`): a migrator that does `root["$meta"]!.AsObject()["schemaVersion"] = 99` must be detected.
- **T1-138** (`MigrationContext_DurationRecorded`): assert `report.Duration > TimeSpan.Zero`.
- **T1-139** (`MigratorScope_AddedWarnings_CaptureItemPath`): the migrator uses `ctx.WithItem("items")` then `ctx.AddWarning("test")` — assert `report.Warnings[0].Path == "$.items"`.

**Existing `StubMigrator` re-use:** the pipeline tests can use `StubMigrator` for non-invariant tests (T1-120 through T1-129, T1-133 through T1-137) but the invariant tests (T1-130..T1-132) need the specific violating migrators.

---

### Task 2: DomDiffer extraction (JM-P1-007)

**Full task spec:** [TASK-DETAILS.md §JM-P1-007](../TASK-DETAILS.md#jm-p1-007--domdiffer-extraction-from-fdptoolkits-gate)
**Design refs:** `Migration-system.md` doc 03 §2.3, §6.4 (M-1 resolution); doc 07 §4.1 Step 7.

#### What to move

The existing diff types live in `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/DiffNode.cs` (namespace `Fdp.Toolkit.ReplayBrowser.Diff`). The file contains `DiffNode`, `DiffObject`, and `DiffValue`.

The diff algorithm lives in `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/ComponentDiffService.cs`.

**Target location in Fdp.Core:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/Diff/`

**New namespace:** `Fdp.Core.Serialization.Migrations.Internal`

#### Files to create in Fdp.Core

- **`DiffNode.cs`** — abstract base `DiffNode` with `Name`, `IsModified`.
- **`DiffObject.cs`** — `DiffObject : DiffNode` with `Children` list and `EvaluateModificationState()`.
- **`DiffValue.cs`** — `DiffValue : DiffNode` with `OldValue`, `NewValue`, `ValueType`.
- **`DomDiffer.cs`** — the diff algorithm (currently the body of `ComponentDiffService.ComputeDiff`). The `DomDiffer` class must expose:

```csharp
internal static class DomDiffer
{
    // Returns a DiffNode tree describing differences between a and b.
    // Returns null if the two inputs are structurally identical.
    public static DiffNode? Diff(JsonNode? a, JsonNode? b, string rootName = "$");
}
```

The algorithm is the recursive logic from `ComponentDiffService.ComputeDiff` — move it here, keeping the epsilon-tolerance for numeric leaves. The `double epsilonTolerance` parameter is kept (default 0.0 for the migration use-case which compares exact bytes).

#### Rewrite ComponentDiffService

After extracting, `ComponentDiffService.ComputeDiff` must delegate to `DomDiffer.Diff`. The types it returns (`DiffNode`, `DiffObject`, `DiffValue`) now come from `Fdp.Core.Serialization.Migrations.Internal`. The `IComponentDiffService` interface and its callers must continue to compile unchanged — **no public API change**.

Since `ComponentDiffService` is in `Fdp.Toolkits` and it will now reference `Fdp.Core`, verify that `Fdp.Toolkits.csproj` already references `Fdp.Core.csproj`. If not, add the project reference.

The namespace `Fdp.Toolkit.ReplayBrowser.Diff` **stays** on the old types in the Toolkits project as type aliases or re-exports — but since the types in `Fdp.Core` are in a different namespace, you need to decide the approach:
- Option A: Keep `DiffNode/DiffObject/DiffValue` in `Fdp.Toolkit.ReplayBrowser.Diff` as type aliases pointing to the Core versions. This preserves all existing callers without change.
- Option B: Change the types in `Fdp.Toolkit.ReplayBrowser.Diff` to be the ones from Core (using `using` aliases). No public API change if the original namespace types can be found.

**Preferred approach:** Use `using` aliases or re-export the types in the Toolkits namespace so callers compile unchanged. Verify with a build of `Fdp.Toolkits.csproj`.

> **Note on namespace discrepancy (C-9):** The existing namespace is `Fdp.Toolkit.ReplayBrowser.Diff` (singular "Toolkit"), not `Fdp.Toolkits` (plural). The folder is `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/` (plural folder, singular namespace). Keep the existing namespace pattern for anything you leave in Toolkits.

#### Tests to implement

Tests for `DomDiffer` live in `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/Internal/DomDifferTests.cs`.

Implement tests `T1-220` through `T1-229` per `Migration-system.md` doc 06 §3.7:

| ID | What to verify |
|----|----------------|
| T1-220 | Identical DOMs produce null (empty diff) |
| T1-221 | New field in B not in A → `DiffValue` marked modified |
| T1-222 | Field in A missing in B → `DiffValue` marked modified |
| T1-223 | Same field, different value → `DiffValue.IsModified == true`, `OldValue`/`NewValue` match |
| T1-224 | Nested difference → `DiffObject.IsModified == true`, correct child path |
| T1-225 | Array element added → detected as modified |
| T1-226 | Array element removed → detected as modified |
| T1-227 | Array element changed → detected as modified |
| T1-228 | Type changed at same path → detected (e.g., `"foo"` → `42`) |
| T1-229 | 50+ nested levels → no stack overflow |

Tests must verify **actual values** (not just `IsModified == true`): check `OldValue`, `NewValue`, `Name` where relevant.

---

## 🧪 Testing Requirements

### Test files

- `MigrationPipelineTests.cs` — T1-120..T1-139 (20 tests)
- `TestMigrators.cs` — synthetic migrators (not test methods; no `[Fact]`)
- `Internal/DomDifferTests.cs` — T1-220..T1-229 (10 tests)

### Quality bar

- **Pipeline invariant tests (T1-130..T1-132) must name the violated invariant** in the exception message they assert. Use `Assert.Contains("docType", ex.Message)` (or similar substring check) to verify the message is specific.
- **DomDiffer tests must check actual OldValue/NewValue strings**, not just `IsModified` booleans.
- **T1-229 (stack overflow)** must actually recurse 50+ levels. Build the test DOM programmatically.
- **T1-138 (duration)** must be `> TimeSpan.Zero`. Add a small artificial step if needed (avoid `Thread.Sleep`; just exercise a real migrator step — it's inherently > 0).

### Regression requirement

After extracting `DomDiffer`, all existing `Fdp.Toolkits` tests (if any) that exercise `ComponentDiffService` must still pass. Run the Toolkits test suite.

---

## ⚠️ Quality Standards

**Before submitting report, verify:**

- [ ] `dotnet build FDP/Engine/Fdp.Core/Fdp.Core.csproj` — 0 warnings.
- [ ] `dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` — 0 errors (must compile after the extraction).
- [ ] `dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj` — all pass (new + pre-existing).
- [ ] T1-010 now asserts `Assert.Throws<MigrationException>` (not `ThrowsAny`).
- [ ] `IJsonDocumentMigrator` no longer has `Direction` property.
- [ ] `MigrationReport.AddWarning(string)` is `internal`.
- [ ] All four pipeline invariants are enforced (verified by T1-130..T1-132 + one additional invariant test).
- [ ] DomDiffer lives in `Fdp.Core.Serialization.Migrations.Internal`; Toolkits still compiles.

---

## 🎯 Success Criteria

- [ ] Debt D-001: T1-010 asserts `MigrationException`; `JsonEnvelope` wraps the reader exception.
- [ ] Debt D-002: `Direction` removed from `IJsonDocumentMigrator`.
- [ ] Debt D-003: `MigrationReport.AddWarning(string)` is internal.
- [ ] JM-P1-006: `MigrationPipeline.cs` created; tests T1-120..T1-139 pass.
- [ ] JM-P1-007: `Internal/Diff/*.cs` created in Fdp.Core; `ComponentDiffService` rewired; tests T1-220..T1-229 pass; Toolkits still compiles and any existing diff tests pass.
- [ ] Full test suite green; report submitted.

---

## 📊 Report Requirements

Submit to `.dev/json-migration/reports/BATCH-02-REPORT.md`. Include:

**Q1:** What issues did you encounter implementing the pipeline invariant checks? Were there any subtleties in the object-identity check for `$meta`?

**Q2:** What approach did you choose for preserving Toolkits callers' compatibility after the DomDiffer extraction? Did you use type aliases, re-exports, or a different technique?

**Q3:** What design decisions did you make beyond the spec? What alternatives did you consider?

**Q4:** Were there any pre-existing edge cases in `ComponentDiffService` that surprised you during extraction?

**Q5:** Test counts per class (MigrationPipelineTests: N, DomDifferTests: N).

**Q6:** Suggested git commit message for this batch.

---

## 📚 Reference Materials

- **Previous review:** `.dev/json-migration/reviews/BATCH-01-REVIEW.md`
- **Task Details:** `.dev/json-migration/TASK-DETAILS.md` — §JM-P1-006, §JM-P1-007
- **Design:**
  - doc 03 §3.4: Pipeline invariants 1-4
  - doc 03 §4.2: MigrationPipeline algorithm
  - doc 03 §2.3, §6.4: M-1 (DomDiffer extraction resolution)
  - doc 06 §3.5: T1-120..T1-139
  - doc 06 §3.7: T1-220..T1-229
- **Existing diff source:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/`
