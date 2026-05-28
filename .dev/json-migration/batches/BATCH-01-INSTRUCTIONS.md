# BATCH-01: Phase 1 Foundation — Types, Envelope, JSONPath, Context, Registry

**Batch Number:** BATCH-01
**Tasks:** JM-P1-001, JM-P1-002, JM-P1-003, JM-P1-004, JM-P1-005
**Phase:** Phase 1 — Core infrastructure
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## Developer Role

Your role is described in `.github\skills\developer\SKILL.md`. Read it before starting.

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Onboarding:** `.dev/json-migration/ONBOARDING.md` — read completely; it explains the project goal, file layout, logging conventions, and build commands.
2. **Task Details:** `.dev/json-migration/TASK-DETAILS.md` — read the preamble (codebase-fit corrections C-1 through C-9) and the sections for JM-P1-001 through JM-P1-005.
3. **Design — Overview:** `Migration-system.md` doc 01 §1–§3 (Purpose, Problem, Architectural decisions D-01 through D-05, D-11).
4. **Design — Wire formats:** `Migration-system.md` doc 02 §2 (envelope format), §6 (JSONPath dialect).
5. **Design — Interfaces:** `Migration-system.md` doc 03 §3.1–§3.5, §4.1 (all public types in this batch).
6. **Design — Test plan:** `Migration-system.md` doc 06 §2 (test conventions), §3.1–§3.6 (T1 tests for the types you will implement).

**All design content is in one file:** `.dev/json-migration/Migration-system.md` (7 sub-documents concatenated). Sub-document boundaries are marked as `*End of document NN-name.md*`.

### Source Code Locations

- **Primary implementation:** `FDP/Engine/Fdp.Core/Serialization/Migrations/` (new files — create this directory)
- **Internal helpers:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/` (new subdirectory)
- **Test project:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/` (new subdirectory; test fixtures under `TestFixtures/`)
- **Logging pattern reference:** `FDP/Engine/Fdp.Core/Logging/FdpLog.cs`
- **Existing serialization code (for context):** `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs`
- **Existing test conventions reference:** any existing file in `FDP/Engine/Fdp.Core.Tests/` (e.g., `EntityRepositoryTests.cs`)
- **Project file to extend:** `FDP/Engine/Fdp.Core/Fdp.Core.csproj`
- **Test project file:** `FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj`

### Build & Test Commands

```powershell
# Build the engine
dotnet build FDP/Engine/Fdp.Core/Fdp.Core.csproj

# Run migration-specific tests only
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --filter "FullyQualifiedName~Migrations"

# Run all Fdp.Core tests (to catch regressions)
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/json-migration/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/json-migration/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch lays the foundation for the entire migration system. You are building the core types and low-level machinery that every other Phase 1 component depends on. Nothing outside `Fdp.Core` is touched.

The five tasks decompose naturally into a bottom-up build order: foundation types first (JM-P1-001), then the envelope reader/writer (JM-P1-002), then the JSONPath dialect (JM-P1-003), then the context/scope stack that uses JSONPath (JM-P1-004), and finally the registry that wires doc types to migrators (JM-P1-005).

The test plan (doc 06 §3) lists exact test IDs for each class. You must implement all of them — they are part of the deliverable, not optional.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **JM-P1-001 (Foundation types):** Implement all files → Write T1-030..T1-035 → **ALL tests pass** ✅
2. **JM-P1-002 (JsonEnvelope):** Implement → Write T1-001..T1-020 → **ALL tests pass** ✅
3. **JM-P1-003 (JSONPath):** Implement → Write T1-160..T1-194 → **ALL tests pass** ✅
4. **JM-P1-004 (MigrationContext):** Implement → Write T1-090..T1-101 → **ALL tests pass** ✅
5. **JM-P1-005 (Registry + IJsonDocumentMigrator):** Implement → Write T1-050..T1-077 → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous tasks)

**Why:** Each component is built on the previous. Passing tests are the only reliable signal that the foundation is solid before building on it.

**DO NOT stop to ask permission for obvious next steps. Work autonomously through all 5 tasks, run the tests, fix failures, and submit the report when everything is green.**

---

## 🎯 Tasks

### Task 1: Foundation Types (JM-P1-001)

**Full task spec:** [TASK-DETAILS.md §JM-P1-001](../TASK-DETAILS.md#jm-p1-001--foundation-types)
**Design refs:** `Migration-system.md` doc 03 §3.1, §3.2, §3.6, §3.7; doc 07 §4.1 Step 1.

**Files to create (all new):**
- `FDP/Engine/Fdp.Core/Serialization/Migrations/DocumentMeta.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationDirection.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationReport.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationWarning.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationException.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/SnapshotEntry.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/SidecarFileInfo.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/SidecarKind.cs`
- `FDP/Engine/Fdp.Core/Serialization/FdpDocumentTypes.cs`

**Key contracts (verify against doc 03):**
- `DocumentMeta` is a record with: `DocType` (non-empty, non-null — throws `ArgumentException`), `SchemaVersion` (≥ 1 — throws `ArgumentOutOfRangeException`), `EngineVersion`/`CreatedBy`/`CreatedUtc` (optional diagnostics). Non-UTC `CreatedUtc` is coerced to UTC with a logged warning (`FdpLog<DocumentMeta>.Warn`).
- `MigrationException` extends `InvalidOperationException`. Carries: `DocType`, `FromVersion`, `ToVersion`, `SourcePath`, `Path`. Has constructors for both format-migration failures and general migration errors.
- `FdpDocumentTypes` is a static class with string constants: `FlightRecorderMetadata`, `RoadNetwork`, `MigrationJournal`.

**Tests to implement:** `T1-030` through `T1-035` in `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/DocumentMetaTests.cs`.

---

### Task 2: JsonEnvelope (JM-P1-002)

**Full task spec:** [TASK-DETAILS.md §JM-P1-002](../TASK-DETAILS.md#jm-p1-002--jsonenvelope-streaming-peek)
**Design refs:** `Migration-system.md` doc 02 §2, doc 03 §3.3; doc 07 §4.1 Step 2.

**File to create:** `FDP/Engine/Fdp.Core/Serialization/Migrations/JsonEnvelope.cs`

**Key contracts (verify against doc 02 §2 and doc 03 §3.3):**
- Public constant `MetaFieldName = "$meta"`.
- Three `Peek` overloads: `Peek(ReadOnlySpan<byte>)`, `Peek(Stream)`, `Peek(string path)`. All return `DocumentMeta`.
- The **stream overload** uses `Utf8JsonReader` in forward-only mode and **stops reading after the `$meta` closing `}`** — it must not read further than necessary (T1-004 validates stream position).
- `Read(JsonObject root)` — reads from an already-parsed DOM.
- `Write(JsonObject root, DocumentMeta meta)` — stamps `$meta` as first property.
- `HasEnvelope(JsonObject root)` — returns bool without throwing.
- `WithSchemaVersion(DocumentMeta meta, int version)` — returns updated meta record.
- `WithEngineVersion(DocumentMeta meta, string engineVersion)` — returns updated meta record.
- `$meta` must appear before any other properties in the output.
- The five allowed `$meta` fields are: `docType`, `schemaVersion`, `engineVersion`, `createdBy`, `createdUtc`. Any additional field throws `MigrationException` (doc 02 §2.2 wire contract).
- `$meta` at non-first position: log warning via `FdpLog<JsonEnvelope>.Warn`, continue parsing.
- Missing `$meta`: throw `MigrationException`.
- Do not use `JsonSerializer.Deserialize<DocumentMeta>()` — read fields explicitly to avoid depending on naming conventions.

**Tests to implement:** `T1-001` through `T1-020` in `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/JsonEnvelopeTests.cs`.

Test fixtures (JSON files) go in `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/TestFixtures/Envelopes/`. Create fixture files needed by tests (valid_basic.json, missing_meta.json, etc.).

---

### Task 3: JSONPath Parser/Applicator (JM-P1-003)

**Full task spec:** [TASK-DETAILS.md §JM-P1-003](../TASK-DETAILS.md#jm-p1-003--jsonpath-parserapplicator)
**Design refs:** `Migration-system.md` doc 02 §6, doc 03 §6.3; doc 07 §4.1 Step 3.

**Files to create:**
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/JsonPath.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/JsonPathParser.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/JsonPathApplicator.cs`

**Restricted dialect (doc 02 §6):**
- Supported: root `$`, dotted segment `.identifier`, quoted bracket `['key']` (with `\'` and `\\` escaping), array index `[N]`.
- Rejected by parser (throw descriptive exception): wildcards `*`, recursive descent `..`, filters `[?(...)]`, negative indexes `[-N]`, slices `[M:N]`.
- **Canonical builder rule (doc 02 §6.8):** use dotted form for keys matching `[A-Za-z_][A-Za-z0-9_]*`; use bracketed form for all other keys. `WithIndex(int)` always uses `[N]`.

**Key contracts:**
- `TryWrite(JsonObject root, JsonPath path, JsonNode? value)` — returns `false` (silently skips) when an intermediate parent is missing. **Does NOT create missing parents.** This implements the "user-deletion-wins" rule (doc design D-16).
- `TryRemove(JsonObject root, JsonPath path)` — returns `true` if removed or already absent; returns `false` if an intermediate parent is missing.
- `Read(JsonObject root, JsonPath path)` — returns `JsonNode?` (null if missing path; `JsonValue.Create((object?)null)` for JSON null); never throws on missing path.

**Tests to implement:** `T1-160` through `T1-194` in `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/Internal/JsonPathTests.cs`.

---

### Task 4: MigrationContext + ScopePathStack (JM-P1-004)

**Full task spec:** [TASK-DETAILS.md §JM-P1-004](../TASK-DETAILS.md#jm-p1-004--migrationcontext--scope-stack)
**Design refs:** `Migration-system.md` doc 03 §3.5; doc 07 §4.1 Step 4.

**Files to create:**
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/ScopePathStack.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationContext.cs`

**Key contracts:**
- `MigrationContext` constructor is `internal` — only the pipeline creates instances. Migrators receive it as a parameter.
- `WithItem(string key)` — push a path segment. Returns an `IDisposable` that pops on dispose (LIFO). Uses canonical-form rules from doc 02 §6.8: dotted for identifier keys, bracketed otherwise.
- `WithIndex(int index)` — push `[N]` segment. Returns `IDisposable`.
- `WithPathSuffix(string preCanonicalizedSuffix)` — appends a multi-segment suffix verbatim (no re-encoding).
- `CurrentPath` — returns the assembled JSONPath string (e.g. `"$.entities['abc-def'].tags[2]"`). When no scope is active, returns `"$"`.
- `AddWarning(string message)` — adds a `MigrationWarning` to the internal report; the warning's `Path` is automatically captured from `CurrentPath`.
- `Report` — returns the accumulated `MigrationReport` after migration.

**Tests to implement:** `T1-090` through `T1-101` in `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/MigrationContextTests.cs`.

---

### Task 5: MigrationRegistry + IJsonDocumentMigrator (JM-P1-005)

**Full task spec:** [TASK-DETAILS.md §JM-P1-005](../TASK-DETAILS.md#jm-p1-005--registry--ijsondocumentmigrator)
**Design refs:** `Migration-system.md` doc 03 §3.4, §4.1; doc 07 §4.1 Step 5.

**Files to create:**
- `FDP/Engine/Fdp.Core/Serialization/Migrations/IJsonDocumentMigrator.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationRegistry.cs`

**Key contracts:**
- `IJsonDocumentMigrator` has: `string DocType`, `int FromVersion`, `int ToVersion`, `void Apply(JsonObject root, MigrationContext ctx)`.
- `MigrationRegistry.RegisterDocType(string docType, int currentVersion, IReadOnlyList<IJsonDocumentMigrator> migrators)` enforces all rules from doc 03 §4.1:
  - `docType` non-empty, non-null.
  - `currentVersion` ≥ 1.
  - Every version step from 1 to currentVersion has exactly one up-migrator (`FromVersion = N`, `ToVersion = N+1`) and exactly one down-migrator (`FromVersion = N+1`, `ToVersion = N`).
  - No duplicate `(FromVersion, ToVersion)` pairs.
  - No non-adjacent migrators (`|ToVersion - FromVersion| != 1`).
  - No gaps in the chain.
  - `docType` not already registered.
- `RegisterPassthroughDocType(string docType, int currentVersion)` — registers a format with no migrators. Any version accepted.
- `IsRegistered(string docType)` — true if registered (migration or passthrough).
- `IsPassthrough(string docType)` — true if passthrough only.
- `GetCurrentVersion(string docType)` — throws `MigrationException` if not registered.
- `GetPath(string docType, int fromVersion, int toVersion)` — returns ordered list of migrators. Empty list if `from == to`. Throws if passthrough or unregistered. Handles multi-step up and multi-step down paths.
- `CanMigrate(string docType, int from, int to)` — returns bool, never throws.
- `RegisteredDocTypes` — enumerates all doc types (both migration-enabled and passthrough).
- **Registry seals** after `MigrationBootstrap.Build` returns (implemented in JM-P1-013, but the sealing mechanism must be present in the registry now). Once sealed, further `RegisterDocType`/`RegisterPassthroughDocType` calls throw.

**Tests to implement:** `T1-050` through `T1-077` in `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/MigrationRegistryTests.cs`.

---

## 🧪 Testing Requirements

### Test organization

All test files live under `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/`.

- `DocumentMetaTests.cs` — T1-030..T1-035
- `JsonEnvelopeTests.cs` — T1-001..T1-020
- `Internal/JsonPathTests.cs` — T1-160..T1-194
- `MigrationContextTests.cs` — T1-090..T1-101
- `MigrationRegistryTests.cs` — T1-050..T1-077

Test fixtures (JSON files) live under `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/TestFixtures/Envelopes/`.

### Test conventions (from doc 06 §2.1)

- xUnit only. No FluentAssertions. Use `Assert.*`.
- Test name pattern: `MethodOrFeature_Scenario_ExpectedBehavior`.
- One test class per production class under test.
- Async tests are `async Task`; never `async void`.
- Test data: deterministic values (GUIDs like `00000000-0000-0000-0000-000000000001`, schema versions 1/2/3, doc types `"Test.Doc"`). No real HROT doc types in core tests.

### Quality bar

**Tests must verify behavior, not just compilation or object existence.**

- ✅ `Constructor_EmptyDocType_ThrowsArgumentException` must actually verify the exception type AND a useful message substring.
- ✅ `Peek_StreamInput_StopsAfterMetaClose` (T1-004) must verify actual stream position is within the `$meta` region.
- ✅ `TryWrite_MissingParent_ReturnsFalse` (T1-190) must verify the return value AND that the DOM is unchanged.
- ✅ `GetPath_MultiStepUp_ReturnsMigratorsInOrder` (T1-071) must verify the order is `[v1→v2, v2→v3]` (check migrator FromVersion/ToVersion fields).
- ❌ Tests that only check `Assert.NotNull(result)` are insufficient.
- ❌ Tests that only verify a string is contained in a message without checking type or behavior are insufficient.

### Mandatory: include fixture files

For `JsonEnvelopeTests`, create the JSON fixture files that tests load. Minimum required:

- `TestFixtures/Envelopes/valid_basic.json` — minimal valid envelope + one body field.
- `TestFixtures/Envelopes/valid_full.json` — all five `$meta` fields populated.
- `TestFixtures/Envelopes/missing_meta.json` — JSON object with no `$meta`.
- `TestFixtures/Envelopes/extra_field_in_meta.json` — `$meta` with a sixth unknown field.
- `TestFixtures/Envelopes/meta_not_first.json` — `$meta` is not the first property.

For `SyntheticDocs` (used in later batches but needed as a reference for the test doc schema used in T1-050 registry tests):

- `TestFixtures/SyntheticDocs/test_doc_v1.json` — schema for the `"Test.Doc"` docType at v1.
- `TestFixtures/SyntheticDocs/test_doc_v2.json` — schema for `"Test.Doc"` at v2.

The shape of synthetic docs (used for T1 pipeline tests, but define them now so they are consistent):
```json
// v1: { "$meta": {"docType":"Test.Doc","schemaVersion":1,...}, "items": [ { "name": "..." } ] }
// v2: { "$meta": {"docType":"Test.Doc","schemaVersion":2,...}, "items": [ { "name": "...", "kind": "default" } ] }
// v3: { "$meta": {"docType":"Test.Doc","schemaVersion":3,...}, "items": [ { "name": "...", "kind": "...", "metadata": {} } ] }
```

---

## ⚠️ Quality Standards

**Before submitting the report, verify:**

- [ ] `dotnet build FDP/Engine/Fdp.Core/Fdp.Core.csproj` succeeds with zero warnings (batch adds files to a project that has `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).
- [ ] `dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj` passes all tests (both new and pre-existing).
- [ ] All tests in T1-001..T1-020, T1-030..T1-035, T1-050..T1-077, T1-090..T1-101, T1-160..T1-194 are implemented. No skipped or ignored tests without explicit rationale.
- [ ] Logging uses `FdpLog<T>` with index-based templates. No `string.Format` or string interpolation in log calls.
- [ ] `MigrationException` is thrown (not `Exception`, not `InvalidOperationException` directly) wherever the design specifies it.
- [ ] `internal` constructor on `MigrationContext` is actually `internal` (not `public`).
- [ ] No typed DTO deserialization via `JsonSerializer` for the envelope fields — read `$meta` manually from the DOM.

**Common mistake to avoid:** `Utf8JsonReader` in the stream peek overload — remember to handle the case where `$meta` is not the very first property (log warning, continue scanning). The reader must advance past the `$meta` object and stop without reading the rest of the document body.

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] JM-P1-001: All 9 files created; tests T1-030..T1-035 pass.
- [ ] JM-P1-002: `JsonEnvelope.cs` created; tests T1-001..T1-020 pass; fixture files committed.
- [ ] JM-P1-003: 3 `JsonPath*.cs` files created; tests T1-160..T1-194 pass.
- [ ] JM-P1-004: `ScopePathStack.cs` + `MigrationContext.cs` created; tests T1-090..T1-101 pass.
- [ ] JM-P1-005: `IJsonDocumentMigrator.cs` + `MigrationRegistry.cs` created; tests T1-050..T1-077 pass.
- [ ] `dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj` — all tests pass (new + pre-existing).
- [ ] Zero build warnings.
- [ ] Report submitted to `.dev/json-migration/reports/BATCH-01-REPORT.md`.

---

## 📊 Report Requirements

Submit to `.dev/json-migration/reports/BATCH-01-REPORT.md`. Include:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points or inconsistencies in the existing codebase or design? What specifically?

**Q3:** What design decisions did you make beyond the specification (where the spec was silent or ambiguous)? What alternatives did you consider and why did you choose this approach?

**Q4:** What edge cases did you discover during implementation that were not explicitly mentioned in the design or task description?

**Q5:** What is your test count per class? (DocumentMetaTests: N, JsonEnvelopeTests: N, JsonPathTests: N, MigrationContextTests: N, MigrationRegistryTests: N)

**Q6:** Suggested git commit message for this batch.

---

## 📚 Reference Materials

- **Task Details:** `.dev/json-migration/TASK-DETAILS.md` — §JM-P1-001 through §JM-P1-005
- **Design (all docs):** `.dev/json-migration/Migration-system.md`
  - doc 01 §3: Architectural decisions D-01..D-16 (D-05, D-11, D-16 especially relevant)
  - doc 02 §2: `$meta` wire format (exact JSON shape, allowed fields, ordering)
  - doc 02 §6: JSONPath restricted dialect
  - doc 03 §3: Public types (DocumentMeta through MigrationException)
  - doc 03 §4.1: Registry rules
  - doc 03 §6.3: JSONPath types contract
  - doc 06 §2: Test conventions
  - doc 06 §3.1–§3.6: T1 test cases
- **Logging:** `FDP/Engine/Fdp.Core/Logging/FdpLog.cs`
- **Existing serialization namespace:** `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs`
- **Build setup:** see ONBOARDING.md "How to build" section
