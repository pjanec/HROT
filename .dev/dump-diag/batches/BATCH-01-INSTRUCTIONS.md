# BATCH-01: JSON Serialisation Foundation

**Batch Number:** BATCH-01
**Tasks:** DD-P1-T01, DD-P1-T02, DD-P1-T03, DD-P1-T04
**Phase:** Phase 1 — JSON Serialisation Foundation
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements the entire JSON Serialisation Foundation (Phase 1 of the dump-diag
design). You will consolidate scattered `JsonSerializerOptions`, fix the `FixedString64`
serialisation bug, and centralise custom JSON converters. All four tasks must be completed
in sequence.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — How to work with batches
2. **Task Detail:** `.dev/dump-diag/TASK-DETAIL.md` — See DD-P1-T01 through DD-P1-T04
3. **Design Document:** `.dev/dump-diag/DESIGN.md` — Sections 1.1, 1.2, 1.3, 1.4

### Source Code Locations

- **Converter source (move FROM):** `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioJsonConverters.cs`
- **Converter source (move FROM):** `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationJsonOptions.cs`
- **Converter target (move TO):** `FDP/Engine/Fdp.Core/Serialization/Converters/` (new directory)
- **Registry target (new file):** `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs`
- **Formatter target (new file):** `FDP/Toolkits/Fdp.Toolkits/Serialization/JsonAestheticFormatter.cs`
- **Callers to refactor:**
  - `FDP/Engine/Fdp.Core/FlightRecorder/FdpAutoSerializer.cs`
  - `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs`
  - `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationJsonOptions.cs`
  - `Hrot/Engine/Hrot.Core/` — `MetadataSerializer.cs` (locate it)
  - Presentation layer — `HrotSerializerOptions.cs` (locate it), `EventBrowserPanel.cs`,
    `EntityJsonDumper.cs` (locate all files)
- **Test projects:**
  - `FDP/Engine/Fdp.Core.Tests/` — add new tests here
  - `FDP/Toolkits/Fdp.Toolkits/` tests (locate the tests project, likely `Fdp.Toolkits.Tests`)
  - Any project with `ScenarioJsonConvertersTests.cs`

### Build Command

```
cd FDP
dotnet build FDP.sln
```

### Test Command

```
cd FDP
dotnet test FDP.sln
```

Or test individual projects:

```
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/dump-diag/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/dump-diag/questions/BATCH-01-QUESTIONS.md`

---

## Context

Phase 1 is the foundation that all later phases depend on. Before any diagnostic dump code
can be written, the JSON serialisation must be consolidated:
- Converters must live in `Fdp.Core` so they are accessible from any layer
- A single `FdpJsonOptionsRegistry` must replace the scattered options instances
- The `FixedString64` bug (fields serialised as structs rather than strings) must be fixed
- The aesthetic formatter must be extracted for reuse in dump output

**Related Tasks:**
- [DD-P1-T01](./../TASK-DETAIL.md#dd-p1-t01--move-fixedstring-converters-to-fdpcore) — Move converters to Fdp.Core
- [DD-P1-T02](./../TASK-DETAIL.md#dd-p1-t02--fdpjsonoptionsregistry) — Create FdpJsonOptionsRegistry
- [DD-P1-T03](./../TASK-DETAIL.md#dd-p1-t03--jsonaestheticformatter) — Extract JsonAestheticFormatter
- [DD-P1-T04](./../TASK-DETAIL.md#dd-p1-t04--refactor-existing-json-callers) — Refactor callers

---

## Batch Objectives

Complete all four tasks so that:
1. Custom converters (FixedString32/64, Vector array types, StrictStringEnumConverter) live in
   `Fdp.Core.Serialization.Converters`
2. `FdpJsonOptionsRegistry.DefaultRelaxed` and `FdpJsonOptionsRegistry.Indented` are the
   canonical options instances used everywhere
3. `JsonAestheticFormatter.FlattenNumericArrays` is extracted and used by `ScenarioFileService`
4. All existing callers use the registry singletons instead of local options instances

---

## Tasks

### Task 1: Move FixedString/Vector Converters to Fdp.Core (DD-P1-T01)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#dd-p1-t01--move-fixedstring-converters-to-fdpcore)

Create the directory `FDP/Engine/Fdp.Core/Serialization/Converters/` and move the converters.
New namespace: `Fdp.Core.Serialization.Converters`.

Files to create:
- `Fdp.Core/Serialization/Converters/FixedStringConverters.cs` — `FixedString32Converter`, `FixedString64Converter`
- `Fdp.Core/Serialization/Converters/VectorArrayConverters.cs` — the four Vector/Quaternion converters
- `Fdp.Core/Serialization/Converters/StrictStringEnumConverter.cs` — moved from `OrchestrationJsonOptions.cs`

Leave `[Obsolete]` forwarders in `ScenarioJsonConverters.cs` for the old types.
`OrchestrationJsonOptions` retains a thin wrapper for `StrictStringEnumConverter`.

**Tests Required (in `Fdp.Core.Tests`):**
- `new FixedString64Converter()` compiles and round-trips `"hello"` correctly
- `new StrictStringEnumConverter()` serialises enum value as its string name, not integer
- No new NuGet packages added to `Fdp.Core.csproj`

---

### Task 2: FdpJsonOptionsRegistry (DD-P1-T02)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#dd-p1-t02--fdpjsonoptionsregistry)

Create `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs`.

Key properties for `DefaultRelaxed`:
- `IncludeFields = true`
- `PropertyNameCaseInsensitive = true`
- `AllowTrailingCommas = true`
- `ReadCommentHandling = JsonCommentHandling.Skip`
- `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`
- All converters from DD-P1-T01 registered (FixedString32/64, Vector2/3/4, Quaternion arrays,
  **StrictStringEnumConverter** — NOT `JsonStringEnumConverter`)
- `.MakeReadOnly()` called

`Indented` = `new JsonSerializerOptions(DefaultRelaxed)` with `WriteIndented = true`, also
`.MakeReadOnly()`.

Both are `public static readonly` properties, NOT mutable fields.

**Tests Required (in `Fdp.Core.Tests`):**
- Both singletons are non-null
- Attempting to mutate `DefaultRelaxed.WriteIndented` throws `InvalidOperationException`
- `FixedString64` round-trip via `DefaultRelaxed`
- Struct with public field (not property) serialises non-empty JSON with the field name

---

### Task 3: JsonAestheticFormatter (DD-P1-T03)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#dd-p1-t03--jsonaestheticformatter-fdptoolkits)

Create `FDP/Toolkits/Fdp.Toolkits/Serialization/JsonAestheticFormatter.cs`.

Namespace: `Fdp.Toolkits.Serialization`.

Extract `WriteFormattedToken` / `IsPureNumericArray` logic from:
`Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs`
into `public static string FlattenNumericArrays(string rawJson)`.

Then update `ScenarioFileService.SaveScenario` to delegate to this method. The private methods
may be removed from `ScenarioFileService`.

**Tests Required:**
- Already-flat numeric array unchanged
- Indented numeric array collapsed inline
- Mixed arrays (string + number) NOT collapsed
- Existing `ScenarioFileService` save/load round-trip tests still pass

---

### Task 4: Refactor Existing JSON Callers (DD-P1-T04)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#dd-p1-t04--refactor-existing-callers)

Replace all local `JsonSerializerOptions` instances with `FdpJsonOptionsRegistry` singletons.

Callers to update (find all, do not miss any):
- `FdpAutoSerializer._fieldAwareOptions` → `FdpJsonOptionsRegistry.DefaultRelaxed`
  (there are TWO `FdpAutoSerializer.cs` files — both in `Fdp.Core/FlightRecorder/` and
  `Fdp.Toolkits/Scenario/`)
- `OrchestrationJsonOptions.Default` → delegate to `FdpJsonOptionsRegistry.DefaultRelaxed`
  (or replace calls at use sites)
- `MetadataSerializer._options` → `FdpJsonOptionsRegistry.DefaultRelaxed`
- `HrotSerializerOptions.HrotJsonOptions` → `FdpJsonOptionsRegistry.Indented`
- `EventBrowserPanel` single-item copy path → `FdpJsonOptionsRegistry.Indented` +
  `JsonAestheticFormatter.FlattenNumericArrays`
- `EntityJsonDumper.Dump` → `FdpJsonOptionsRegistry.Indented` +
  `JsonAestheticFormatter.FlattenNumericArrays`

**Tests Required:**
- `FixedString64` round-trip via the updated `EventBrowserPanel` path produces string value,
  NOT a struct object JSON
- All existing `FdpAutoSerializer` round-trip tests pass
- `OrchestrationPayloadDtos` deserialization tests pass (field names unchanged)
- `ScenarioJsonConvertersTests` all pass

---

## Mandatory Workflow

**CRITICAL: Complete tasks in order. Do NOT move to next task until all tests pass.**

1. DD-P1-T01 → verify tests pass
2. DD-P1-T02 → verify tests pass
3. DD-P1-T03 → verify tests pass
4. DD-P1-T04 → verify ALL tests pass (full solution)

Do NOT stop to ask whether to run tests, fix failing tests, or continue. Do it all. If tests
fail, find and fix the root cause. Only write the report when everything is done and passing.

---

## Testing Requirements

- Minimum 12 unit tests total across all tasks
- Each test validates actual behavior (not just compilation)
- All existing tests must continue to pass
- Run full solution build before submitting report

---

## Quality Standards

**TEST QUALITY:**
- NOT ACCEPTABLE: Tests that only verify object construction compiles
- REQUIRED: Tests that verify actual serialisation values and round-trip correctness
- REQUIRED: Tests verify immutability of registry singletons

**REPORT QUALITY:**
- REQUIRED: Document issues encountered and how you resolved them
- REQUIRED: Document which callers were found and updated in DD-P1-T04
- REQUIRED: Share any observations about the existing code quality

---

## Success Criteria

- [ ] DD-P1-T01: Converter files in `Fdp.Core/Serialization/Converters/`, `[Obsolete]`
      forwarders in `ScenarioJsonConverters.cs`, no new NuGet packages
- [ ] DD-P1-T02: `FdpJsonOptionsRegistry` in `Fdp.Core.Serialization` namespace, both
      singletons frozen, all converters registered including `StrictStringEnumConverter`
- [ ] DD-P1-T03: `JsonAestheticFormatter.FlattenNumericArrays` in `Fdp.Toolkits.Serialization`,
      `ScenarioFileService.SaveScenario` delegates to it
- [ ] DD-P1-T04: All identified callers updated, full build passes, all tests pass
- [ ] Report submitted at `.dev/dump-diag/reports/BATCH-01-REPORT.md`

---

## Developer Insights (Report Questions)

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** How many callers did you find for DD-P1-T04? Were there any you did not expect?

**Q3:** Did you spot any weak points in the existing codebase while refactoring? What would you
improve?

**Q4:** What design decisions did you make beyond the instructions? What alternatives did you
consider?

**Q5:** Are there any serialisation edge cases or concerns you noticed during implementation?

---

## Reference Materials

- **Task Detail:** `.dev/dump-diag/TASK-DETAIL.md` — DD-P1-T01 through DD-P1-T04
- **Design:** `.dev/dump-diag/DESIGN.md` — Sections 1.1, 1.2, 1.3, 1.4
- **Debt Tracker:** `.dev/dump-diag/DEBT-TRACKER.md` — P3 items for context only
