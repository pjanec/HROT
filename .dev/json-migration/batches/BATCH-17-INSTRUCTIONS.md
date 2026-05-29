# BATCH-17: Phase 3 — First Migrator Pair

**Batch Number:** BATCH-17
**Tasks:** JM-P3-001, JM-P3-002, JM-P3-003, JM-P3-004, JM-P3-005
**Phase:** Phase 3 — First Migrator Pair
**Estimated Effort:** 8-12 hours
**Priority:** HIGH
**Dependencies:** BATCH-16 approved (Phase 2 complete, JM-P2-011 gate passed)

---

## Onboarding & Workflow

### Developer Instructions

Phase 2 is fully approved and all JSON files carry `$meta` envelopes. Phase 3 delivers the first
real schema change: a v1→v2 upgrade that adds an optional `Tags` field to `EntityInfo`. This is
a deliberately low-stakes first migrator designed to validate the full pipeline end-to-end.

You will author two migrators, create paired test corpus files, update the module registration,
and write a comprehensive test suite.

### Required Reading (IN ORDER)

1. **Onboarding:** `.dev/json-migration/ONBOARDING.md` — project context
2. **Task Definitions:** `.dev/json-migration/TASK-DETAILS.md` — Phase 3 section (JM-P3-001 through JM-P3-005)
3. **Design — Worked Example:** `.dev/json-migration/Migration-system.md` — *04 §4* (lines ~3284–3450): complete migrator code + lifecycle trace
4. **Design — Migrator Guidelines:** `.dev/json-migration/Migration-system.md` — *07 §10* (lines ~5380–5500): mandatory authoring rules
5. **Design — Helpers Spec:** `.dev/json-migration/Migration-system.md` — *03 §10* (lines ~2725–2880): `EntityPatch`, `CasingPolicy`, `NestedJsonPatch` specs
6. **Design — Module Structure:** `.dev/json-migration/Migration-system.md` — *03 §9* (lines ~1069–1130): `RegisterAll` pattern
7. **Previous Review:** `.dev/json-migration/reviews/BATCH-16-REVIEW.md` — Phase 2 gate outcome

### Source Code Locations

- **Helpers (new):** `Hrot/Engine/Hrot.Common/Scenario/Migrations/Helpers/`
- **Migrators (new):** `Hrot/Engine/Hrot.Common/Scenario/Migrations/Migrators/Scenario/`
- **Module (update):** `Hrot/Engine/Hrot.Common/Scenario/Migrations/ScenarioMigrationModule.cs`
- **Test corpus (new):** `test-data/scenario-corpus/multi-version/` (workspace root, alongside `IOS-IG-SimHost.sln`)
- **Tests (new):** `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/Phase3MigratorTests.cs`
- **Test project:** `Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj`
- **Bootstrap (read only):** `Hrot/Engine/Hrot.Common/Scenario/Migrations/HrotMigrationBootstrap.cs`

### Build & Test Commands

```powershell
# Full solution build
dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4

# Run migration module tests only
dotnet test Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj --logger "console;verbosity=normal"

# Run full test suite for Phase 3 regression check
dotnet test Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj
```

### Report Submission

**When done, create:**
`.dev/json-migration/reports/BATCH-17-REPORT.md`

**If you have questions, create:**
`.dev/json-migration/questions/BATCH-17-QUESTIONS.md`

---

## Context

Phase 2 successfully rolled out the `$meta` envelope to every JSON read/write path. All
committed scenario fixtures now carry `$meta.schemaVersion: 1`. The migration adapters are
wired into every host bootstrap via `ScenarioMigrationModule.RegisterAll(reg)`.

Phase 3 delivers the first real schema change: `EntityInfo.Tags` (a v2-exclusive `List<string>`
field with empty-list default). This is the exact worked example from design document 04 §4. The
design contains complete migrator code — your job is to implement it faithfully plus full test
coverage.

**Why this schema change:** Low blast radius (optional field, empty default), exercises the
journal non-trivially, matches the design's primary worked example exactly. The architect has
pre-approved this choice.

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete each task fully before moving to the next.**

1. **Task 1 (Helpers + Migrators):** Implement → Write unit tests → ALL tests pass
2. **Task 2 (Test corpus):** Create files → Write corpus round-trip tests → ALL tests pass
3. **Task 3 (Module update):** Update module → Write registry tests → ALL tests pass
4. **Task 4 (Bootstrap verification):** Add bootstrap integration test → ALL tests pass
5. **Task 5 (T4 sample):** Write T4-003 test → ALL tests pass

**Do NOT ask for permission to run tests, fix compilation errors, or iterate until tests pass.
Complete all of this independently and report only when everything is green.**

---

## Tasks

### Task 1: Helpers + Migrators (JM-P3-001)

**Task Definition:** See [TASK-DETAILS.md](../TASK-DETAILS.md#jm-p3-001--author-first-migrator-pair-recommended-entityinfotags)

**Design reference:** *04 §4.2/§4.3* (complete migrator code), *07 §10* (authoring guidelines), *03 §10.1/§10.2* (helper specs)

#### 1a. Create `CasingPolicy.cs`

**File:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/Helpers/CasingPolicy.cs` (NEW)

Implement the `CasingPolicy` enum exactly per design *03 §10.2*. Namespace: `Hrot.Common.Scenario.Migrations.Helpers`.

Three values: `MatchExisting`, `ForcePascal`, `ForceCamel`.

#### 1b. Create `EntityPatch.cs`

**File:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/Helpers/EntityPatch.cs` (NEW)

Implement the full `EntityPatch` static class per design *03 §10.1*.
Namespace: `Hrot.Common.Scenario.Migrations.Helpers`.

The entities payload structure (`root["entities"]`) is a `JsonObject` keyed by GUID string.
Each value is a `JsonObject` whose properties are component names (PascalCase or camelCase).

**Required methods (all must be implemented — they are used by future migrators):**

```csharp
// Core iteration
public static void OnEachEntity(JsonObject root, Action<string, JsonObject> action)
// Iterates root["entities"] as JsonObject, skips non-object entries.

public static void OnComponent(JsonObject root, string componentName,
    Action<string, JsonObject> action)
// Calls action for every entity that has the named component as a JsonObject property.

// Bulk operations
public static void RenameComponent(JsonObject root, string oldName, string newName)
// Renames a component across all entities. Throws MigrationException if any entity
// already has the new name alongside the old one.

public static void RenameField(JsonObject root, string componentName,
    string oldField, string newField,
    CasingPolicy casing = CasingPolicy.MatchExisting)
// Renames a field within a component, across all entities that have it.

public static void AddField(JsonObject root, string componentName,
    string fieldName, JsonNode defaultValue,
    CasingPolicy casing = CasingPolicy.MatchExisting)
// Adds a field with a static default value; skips if already present (idempotent).

public static void AddField(JsonObject root, string componentName,
    string fieldName, Func<JsonObject, JsonNode> computeFromComponent,
    CasingPolicy casing = CasingPolicy.MatchExisting)
// Adds a field computed from the component; skips if already present.

public static void RemoveField(JsonObject root, string componentName, string fieldName)
// Removes a field; no-op if field absent.

public static void TransformComponent(JsonObject root, string componentName,
    Action<JsonObject, JsonObject> transform)
// (entity, component) — general-purpose transform; may add/remove sibling components.
```

`CasingPolicy` handling: when `MatchExisting`, inspect existing field names in the component to
infer whether it uses PascalCase or camelCase (majority wins; PascalCase wins ties). Only
`AddField` and `RenameField` use `CasingPolicy`.

#### 1c. Create `NestedJsonPatch.cs`

**File:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/Helpers/NestedJsonPatch.cs` (NEW)

Implement per design *03 §10.3*. Namespace: `Hrot.Common.Scenario.Migrations.Helpers`.

```csharp
public static void EditEscapedJsonObject(JsonObject parent, string propertyName,
    Action<JsonObject> editAction)
// Parses the string value at propertyName as nested JSON, calls editAction, re-serializes.
// Throws MigrationException if missing, not a string, or not valid JSON.

public static void EditEscapedJsonArray(JsonObject parent, string propertyName,
    Action<JsonArray> editAction)
// Variant for arrays.
```

#### 1d. Create `V1ToV2_EntityInfo_AddTags.cs`

**File:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/Migrators/Scenario/V1ToV2_EntityInfo_AddTags.cs` (NEW)

Implement **exactly** as specified in design *04 §4.2* (up-migrator). The design contains complete
working code — implement it faithfully.

**Namespace:** `Hrot.Common.Scenario.Migrations.Migrators.Scenario`

Required usings: `Fdp.Core.Logging`, `Fdp.Core.Serialization.Migrations`,
`Hrot.Common.Scenario.Migrations.Helpers`, `System.Text.Json.Nodes`.

The class is `internal sealed` and implements `IJsonDocumentMigrator`.

Required XML doc comment (see design *07 §10.8*):
```csharp
/// <summary>
/// Migrates Hrot.Scenario from v1 to v2 by adding a Tags field to EntityInfo.
/// </summary>
/// <remarks>
/// Schema change:
/// - v1: EntityInfo { Name, ForceId }
/// - v2: EntityInfo { Name, ForceId, Tags: List&lt;string&gt; }
///
/// Up-migration default: Tags = [].
/// Down-migration: Tags field removed (information loss).
/// Round-trip: lossy (v_higher Tag content cannot be recovered from v_lower).
/// </remarks>
```

Properties: `DocType => HrotDocumentTypes.Scenario`, `FromVersion => 1`, `ToVersion => 2`.

`Apply`: Use `EntityPatch.OnEachEntity` with `ctx.WithItem("entities")` + `ctx.WithItem(entityId)`
scope push. Check `entity["EntityInfo"] is not JsonObject info` — skip if absent.
Check `info.ContainsKey("Tags")` — return early if present (idempotency).
Set `info["Tags"] = new JsonArray()`. Increment count. After loop: `ctx.Report.AddNote(...)` +
`FdpLog<V1ToV2_EntityInfo_AddTags>.Info(...)`.

#### 1e. Create `V2ToV1_EntityInfo_RemoveTags.cs`

**File:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/Migrators/Scenario/V2ToV1_EntityInfo_RemoveTags.cs` (NEW)

Implement **exactly** as specified in design *04 §4.2* (down-migrator). The design contains complete
working code.

**Namespace:** `Hrot.Common.Scenario.Migrations.Migrators.Scenario`

Properties: `DocType => HrotDocumentTypes.Scenario`, `FromVersion => 2`, `ToVersion => 1`.

`Apply`: Use `EntityPatch.OnEachEntity` with scope push. Check `entity["EntityInfo"] is not JsonObject info` — skip. Call `info.Remove("Tags")` and count if true. After loop: `ctx.Report.AddNote(...)`.

No `FdpLog` call needed in the down-migrator (it's typically only executed locally by the editor).

XML doc comment:
```csharp
/// <summary>
/// Migrates Hrot.Scenario from v2 to v1 by removing the Tags field from EntityInfo.
/// </summary>
/// <remarks>
/// Schema change:
/// - v2: EntityInfo { Name, ForceId, Tags: List&lt;string&gt; }
/// - v1: EntityInfo { Name, ForceId }
///
/// Down-migration removes Tags entirely. This is a lossy operation.
/// Round-trip: Tags content cannot be recovered.
/// </remarks>
```

---

### Task 2: Test Corpus (JM-P3-002)

**Task Definition:** See [TASK-DETAILS.md](../TASK-DETAILS.md#jm-p3-002--author-paired-test-corpus-v1--v2)

**Design reference:** *07 §6.1 step 2*, *06 §6.1*

Create two scenario JSON files at the workspace root (next to `IOS-IG-SimHost.sln`):

#### 2a. `test-data/scenario-corpus/multi-version/v1_complete/scenario.json`

A v1 scenario with two entities that both have `EntityInfo` components (and NO `Tags` field).
Also include at least one entity WITHOUT `EntityInfo` to test the "skip" path.

Minimal structure:
```json
{
  "$meta": {
    "docType": "Hrot.Scenario",
    "schemaVersion": 1
  },
  "entities": {
    "aaaaaaaa-0001-0000-0000-000000000001": {
      "SimTransform": {
        "Position": [100.0, 200.0, 0.0],
        "Rotation": [0, 0, 0, 1]
      },
      "EntityInfo": {
        "Name": "Alpha-1",
        "ForceId": "Friend"
      }
    },
    "aaaaaaaa-0001-0000-0000-000000000002": {
      "SimTransform": {
        "Position": [150.0, 250.0, 0.0],
        "Rotation": [0, 0, 0, 1]
      },
      "EntityInfo": {
        "Name": "Bravo-1",
        "ForceId": "Hostile"
      }
    },
    "aaaaaaaa-0001-0000-0000-000000000003": {
      "SimTransform": {
        "Position": [200.0, 300.0, 0.0],
        "Rotation": [0, 0, 0, 1]
      }
    }
  }
}
```

**Important:** Do NOT include a legacy `header.subsystemType` field — this is a pure migration
corpus file, not a legacy-compatible scenario.

#### 2b. `test-data/scenario-corpus/multi-version/v2_complete/scenario.json`

The exact v2 equivalent: same structure as v1_complete but with `"Tags": []` added to every
`EntityInfo`, and `$meta.schemaVersion: 2`.

```json
{
  "$meta": {
    "docType": "Hrot.Scenario",
    "schemaVersion": 2
  },
  "entities": {
    "aaaaaaaa-0001-0000-0000-000000000001": {
      "SimTransform": {
        "Position": [100.0, 200.0, 0.0],
        "Rotation": [0, 0, 0, 1]
      },
      "EntityInfo": {
        "Name": "Alpha-1",
        "ForceId": "Friend",
        "Tags": []
      }
    },
    "aaaaaaaa-0001-0000-0000-000000000002": {
      "SimTransform": {
        "Position": [150.0, 250.0, 0.0],
        "Rotation": [0, 0, 0, 1]
      },
      "EntityInfo": {
        "Name": "Bravo-1",
        "ForceId": "Hostile",
        "Tags": []
      }
    },
    "aaaaaaaa-0001-0000-0000-000000000003": {
      "SimTransform": {
        "Position": [200.0, 300.0, 0.0],
        "Rotation": [0, 0, 0, 1]
      }
    }
  }
}
```

---

### Task 3: Update ScenarioMigrationModule (JM-P3-003)

**Task Definition:** See [TASK-DETAILS.md](../TASK-DETAILS.md#jm-p3-003--register-migrator-pair-bump-currentversion)

**Design reference:** *03 §9.1*, *07 §6.1 step 3*

**File:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/ScenarioMigrationModule.cs` (UPDATE)

Replace the `RegisterPassthroughDocType` call with `RegisterDocType` using both migrators.
Bump `CurrentVersion` to 2.

```csharp
public const int CurrentVersion = 2;

public static void RegisterAll(MigrationRegistry registry)
{
    if (registry == null)
        throw new System.ArgumentNullException(nameof(registry));

    registry.RegisterDocType(
        HrotDocumentTypes.Scenario,
        currentVersion: CurrentVersion,
        migrators: new IJsonDocumentMigrator[]
        {
            new Migrators.Scenario.V1ToV2_EntityInfo_AddTags(),
            new Migrators.Scenario.V2ToV1_EntityInfo_RemoveTags(),
        });
}
```

Update the class XML doc comment to reflect the new version and migration chain.

Add required usings for the `Migrators.Scenario` namespace.

---

### Task 4: Verify Bootstrap Wiring (JM-P3-004)

**Task Definition:** See [TASK-DETAILS.md](../TASK-DETAILS.md#jm-p3-004--update-host-bootstraps-to-use-the-module)

**Design reference:** *07 §6.1 step 4*, C-2

**Verification:** Open `Hrot/Engine/Hrot.Common/Scenario/Migrations/HrotMigrationBootstrap.cs`
and confirm that `BuildSimHostCgf`, `BuildIg`, `BuildEditor`, and `BuildClusterRunnerMigrate`
all call `ScenarioMigrationModule.RegisterAll(reg)`. No code changes are needed to this file
since it was already wired in Phase 2 — the module update in Task 3 is sufficient.

The success condition (v1 scenario triggers up-migration) is verified by tests in Task 5.

---

### Task 5: Tests (JM-P3-001 + JM-P3-002 + JM-P3-003 + JM-P3-004 + JM-P3-005)

**File:** `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/Phase3MigratorTests.cs` (NEW)

**Target: 16 tests minimum.** All must pass. Use xUnit only (no FluentAssertions).
Test name convention: `ClassName_Scenario_ExpectedOutcome`.

#### Group 1: Migrator unit tests (§10.9 requirement)

The migrators are tested directly by constructing a `JsonObject` with the expected entity shape
and calling `migrator.Apply(root, ctx)`.

Helper setup:
```csharp
private static MigrationContext MakeContext() =>
    new MigrationContext(
        new DocumentMeta("Hrot.Scenario", 1, "0.0.0", "test", DateTimeOffset.UtcNow),
        new MigrationReport());

private static JsonObject MakeRoot(params (string id, JsonObject entity)[] entities)
{
    var entitiesObj = new JsonObject();
    foreach (var (id, entity) in entities)
        entitiesObj[id] = entity;
    return new JsonObject { ["entities"] = entitiesObj };
}

private static JsonObject MakeEntityWith(JsonObject entityInfo) =>
    new JsonObject { ["EntityInfo"] = entityInfo };

private static JsonObject MakeEntityInfoV1(string name, string forceId) =>
    new JsonObject { ["Name"] = name, ["ForceId"] = forceId };
```

**Required tests:**

1. `V1ToV2_AddTags_EntityWithEntityInfo_AddsEmptyTagsArray`
   - Entity has `EntityInfo { Name, ForceId }`, no Tags
   - After Apply: `entity["EntityInfo"]["Tags"]` is a `JsonArray` with Count 0

2. `V1ToV2_AddTags_EntityWithoutEntityInfo_IsNotModified`
   - Entity has no `EntityInfo` component
   - After Apply: entity is unchanged (no Tags, no new properties)

3. `V1ToV2_AddTags_EntityAlreadyHasTags_IsIdempotent`
   - Entity has `EntityInfo.Tags = ["existing"]`
   - After Apply: Tags is still `["existing"]` (not replaced with empty array)

4. `V1ToV2_AddTags_MultipleEntities_AllGetTags`
   - 3 entities with EntityInfo, 1 without
   - After Apply: 3 entities have Tags, 1 is unchanged

5. `V1ToV2_AddTags_ReportNoteIncludesCount`
   - Apply on 2 entities with EntityInfo
   - `ctx.Report.Notes` contains a note mentioning "2" (the count)

6. `V1ToV2_AddTags_DocTypeIsScenario`
   - `new V1ToV2_EntityInfo_AddTags().DocType` == `HrotDocumentTypes.Scenario`

7. `V1ToV2_AddTags_FromVersion1_ToVersion2`
   - `FromVersion == 1`, `ToVersion == 2`

8. `V2ToV1_RemoveTags_EntityWithTags_RemovesTags`
   - Entity has `EntityInfo { Name, ForceId, Tags: ["recon"] }`
   - After Apply: Tags property is absent from EntityInfo

9. `V2ToV1_RemoveTags_EntityWithoutEntityInfo_IsNotModified`
   - Entity has no `EntityInfo`
   - After Apply: entity is unchanged

10. `V2ToV1_RemoveTags_EntityWithoutTags_IsIdempotent`
    - Entity has `EntityInfo { Name, ForceId }` (no Tags)
    - After Apply: EntityInfo unchanged (remove on absent key is no-op)

11. `V2ToV1_RemoveTags_MultipleEntities_AllLoseTags`
    - 3 entities with EntityInfo+Tags, 1 without EntityInfo
    - After Apply: 3 have EntityInfo without Tags, 1 is unchanged

12. `V2ToV1_RemoveTags_DocTypeIsScenario_FromVersion2_ToVersion1`
    - `DocType == HrotDocumentTypes.Scenario`, `FromVersion == 2`, `ToVersion == 1`

#### Group 2: Registry validation tests (JM-P3-003)

13. `ScenarioMigrationModule_CurrentVersion_Is2`
    - `ScenarioMigrationModule.CurrentVersion == 2`

14. `ScenarioMigrationModule_RegisterAll_CanMigrateV1ToV2`
    - Build a `MigrationServices` via `BuildServices(ScenarioMigrationModule.RegisterAll)`
    - `services.Registry.CanMigrate(HrotDocumentTypes.Scenario, 1, 2)` is true

15. `ScenarioMigrationModule_RegisterAll_CanMigrateV2ToV1`
    - Same setup
    - `services.Registry.CanMigrate(HrotDocumentTypes.Scenario, 2, 1)` is true

#### Group 3: Bootstrap integration test (JM-P3-004)

16. `ReadOnlyAdapter_LoadV1ScenarioCorpus_ProducesV2Dom`
    - Use `HrotMigrationBootstrap.BuildSimHostCgf("test")` (production factory)
    - Load `test-data/scenario-corpus/multi-version/v1_complete/scenario.json` via
      `services.ReadOnly.LoadAndMigrateAsync(path)`
    - Assert `dom.AsJsonObject()["$meta"]["schemaVersion"].GetValue<int>() == 2`
    - Assert `dom.AsJsonObject()["entities"]["aaaaaaaa-0001-0000-0000-000000000001"]["EntityInfo"]["Tags"]` is a `JsonArray`

#### Group 4: Corpus round-trip tests (JM-P3-002 + JM-P3-005 T4-003)

These live in the same test class or a sibling class. They use the actual corpus files on disk
(resolved via `FindWorkspaceRoot()` — same approach as `Phase2ConventionTests`).

17. `V1CorpusFile_MigratedThroughPipeline_MatchesV2CorpusFile`
    - Parse `v1_complete/scenario.json` as `JsonObject`
    - Use `MigrationServices.Pipeline.MigrateTo(dom, targetVersion: 2)` directly to up-migrate
    - Parse `v2_complete/scenario.json` as `JsonObject`
    - Assert the two DOMs are JSON-equivalent (use `ToJsonString()` comparison after stripping
      `$meta.engineVersion` from both, since engineVersion is set at runtime)

18. `V2CorpusFile_DownMigratedThroughPipeline_LosesTagsField`
    - Parse `v2_complete/scenario.json` as `JsonObject`
    - Use `MigrationServices.Pipeline.MigrateTo(dom, targetVersion: 1)`
    - Assert `schemaVersion == 1`
    - Assert no entity has a `Tags` field in its `EntityInfo`

That's 18 tests total. All must pass.

**Locate the workspace root in corpus tests:**

```csharp
private static string FindWorkspaceRoot()
{
    DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IOS-IG-SimHost.sln")))
        dir = dir.Parent;
    if (dir == null)
        throw new InvalidOperationException("Cannot locate workspace root.");
    return dir.FullName;
}
```

**Build services helper for registry tests:**
```csharp
private static MigrationServices BuildServices(Action<MigrationRegistry> registerFormats) =>
    MigrationBootstrap.Build(
        registerFormats,
        new InMemoryMigrationStorage(),
        () => "test-1.0",
        "Hrot.Common.Tests");
```

---

## Testing Requirements

- **Minimum:** 18 passing tests
- **Zero tolerance:** No test that only asserts "no exception" — every test must assert a
  specific value or structural condition
- **Idempotency must be explicitly tested** (tests 3 and 10 above)
- **Scope absence must be tested** (tests 2 and 9 above — entities without `EntityInfo` must be unchanged)
- **Count-based reporting must be tested** (test 5)

---

## Success Criteria

This batch is DONE when:

- [ ] `CasingPolicy.cs` created with 3 values
- [ ] `EntityPatch.cs` created with all 8 methods implemented
- [ ] `NestedJsonPatch.cs` created with 2 methods
- [ ] `V1ToV2_EntityInfo_AddTags.cs` created with XML doc comment and correct logic
- [ ] `V2ToV1_EntityInfo_RemoveTags.cs` created with XML doc comment and correct logic
- [ ] `test-data/scenario-corpus/multi-version/v1_complete/scenario.json` created
- [ ] `test-data/scenario-corpus/multi-version/v2_complete/scenario.json` created
- [ ] `ScenarioMigrationModule.cs` updated: `CurrentVersion = 2`, uses `RegisterDocType`
- [ ] `HrotMigrationBootstrap.cs` verified (read-only — no changes needed)
- [ ] `Phase3MigratorTests.cs` created with 18 tests, all passing
- [ ] `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore` succeeds (zero errors)
- [ ] `dotnet test Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj` — all tests pass
- [ ] `dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj` — no regressions
- [ ] Report submitted

---

## Common Pitfalls to Avoid

1. **Forgetting the `MigrationContext` scope push** — the migrators MUST call `ctx.WithItem("entities")`
   and `ctx.WithItem(entityId)` to maintain path tracking. This is §10.3 compliance.

2. **`Direction` property** — `IJsonDocumentMigrator` in this codebase does NOT have a `Direction`
   property (design D-002 resolved this — it was removed as redundant). Do not add it.

3. **`JsonNode` cloning** — `JsonNode` is not safely cloneable out of tree. When creating
   `new JsonArray()` as a default value, create it fresh per entity, not shared.

4. **`info["Tags"] = new JsonArray()` vs `info.Add("Tags", new JsonArray())`** — both work in
   `System.Text.Json.Nodes` but use the indexer assignment to match the design pattern.

5. **Phase2ConventionTests** — the v2 corpus file must NOT have a `header.subsystemType` field,
   otherwise `T_Conv_02` would check it for `schemaVersion == 1` and fail. The corpus files in
   `test-data/` are pure migration test inputs, not legacy-compatible scenarios.

6. **`RegisterDocType` signature** — the method takes `IEnumerable<IJsonDocumentMigrator>`, not
   `params`. Pass both migrators as a collection.

7. **`MigrationContext` constructor** — check the actual constructor signature in
   `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationContext.cs` before using it in tests.

---

## Notes on JM-P3-006

JM-P3-006 (Architect dry-run gate) is a **manual gate** that requires the architect to run the
editor against a v1 scenario interactively. This cannot be implemented or automated by the
developer. Do not attempt to implement it — it will be performed separately after this batch is
reviewed and approved.

---

## Reference Materials

- **Task Definitions:** `.dev/json-migration/TASK-DETAILS.md` — JM-P3-001 through JM-P3-005
- **Migrator code (complete):** `.dev/json-migration/Migration-system.md` lines ~3305–3385 (04 §4.2)
- **Authoring guidelines:** `.dev/json-migration/Migration-system.md` lines ~5380–5500 (07 §10)
- **Helper specs:** `.dev/json-migration/Migration-system.md` lines ~2725–2880 (03 §10)
- **Module pattern:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/ScenarioMigrationModule.cs`
- **Bootstrap pattern:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/HrotMigrationBootstrap.cs`
- **Existing tests:** `Hrot/Engine/Hrot.Common.Tests/Migrations/ModuleRegistrationTests.cs`
- **Convention tests:** `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/Phase2ConventionTests.cs`
- **IJsonDocumentMigrator:** `FDP/Engine/Fdp.Core/Serialization/Migrations/IJsonDocumentMigrator.cs`
- **MigrationRegistry:** `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationRegistry.cs`

---

## Developer Report Requirements

When submitting `.dev/json-migration/reports/BATCH-17-REPORT.md`:

**Q1:** What issues did you encounter implementing `EntityPatch`? How did you handle edge cases
(e.g., mixed-casing detection, null entity values in the entities map)?

**Q2:** Did you discover any design discrepancies between what the design specifies and what the
actual codebase supports? (e.g., `IJsonDocumentMigrator` interface differences, `MigrationContext`
constructor signature)

**Q3:** What design decisions did you make beyond the spec? For `CasingPolicy.MatchExisting`,
what heuristic did you use?

**Q4:** Are there edge cases in the corpus round-trip test (test 17) that required special handling
(e.g., floating point JSON comparison, property ordering)?

**Q5:** What is the recommended commit message for this batch?
