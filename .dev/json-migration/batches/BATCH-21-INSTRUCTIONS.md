# BATCH-21 Instructions -- EntityPatch Hardening + Doc Update + Corpus Expansion

**Date:** 2026-06-03
**Branch:** `json-migration`
**Prereq commit:** `24c26e52` (review: BATCH-20 APPROVED)

---

## Context

Onboarding:
- Read [Migration-system.md](../Migration-system.md) for the overall design.
- Read [BATCH-20-REPORT.md](../reports/BATCH-20-REPORT.md) -- BATCH-20 results and developer insights.
- Read [TASK-DETAILS.md](../TASK-DETAILS.md) -- task definitions.
- Read [DEBT-TRACKER.md](../DEBT-TRACKER.md) -- open debt items.

Current state:
- Phases 1-4 are complete. All test failures from BATCH-20 have been resolved.
- Two GATE tasks remain (JM-P3-006, JM-P4-006); these require human approval and are NOT in scope.
- This batch clears P2/P3 backlog debt and expands the scenario corpus.

**Pre-existing build errors:** `Hrot.Blueprints.Tests` CS0234/CS0246 -- do NOT fix; unrelated.

**Pre-existing test failures (NOT caused by BATCH-20):** 38 tests in `Fdp.Toolkits.Tests` unrelated to this batch (CombatComponentTests, NavTests, GizmoTests etc). Do not fix these.

---

## Test-Driven Task Progression (MANDATORY)

For every change:
1. **Understand the failure first.** Read the source file before changing it.
2. **Implement the minimal fix** to make the test pass without breaking other tests.
3. **Run the affected test project** after every task and verify no regressions.
4. **Do not add tests unless the task specifically says to.**

---

## Task List

### Task 1 -- D-019: Document sync-wrapper decision in 05-integration-patches.md

**File:** `.dev/json-migration/05-integration-patches.md`

**Problem:** Key Finding 5 says RoadNetworkLoader "must either make it async (preferred) or use a sync wrapper." The implementation has used a sync wrapper (`.GetAwaiter().GetResult()`), but this choice is not documented.

**What to do:**

In the **Key Findings** section at the bottom of the file, update Finding 5 to record the actual decision. Replace:

> 5. **RoadNetworkLoader is synchronous** -- Phase 2 must either make it async (preferred) or use a sync wrapper around the async adapter.

With:

> 5. **RoadNetworkLoader and NodeConfiguration.LoadFrom are synchronous** -- Both use option (b): `.GetAwaiter().GetResult()` sync wrapper around `ReadOnlyMigrationAdapter.LoadAndMigrateAsync`. Option (a) (making them async) was considered but rejected because it would require cascading async changes through `ZoneManagerService`, `EditorZoneAuthoringSystem`, and `SimHostApp` entry points. The sync-wrapper approach is safe for these specific paths because they run during startup/editor setup on a thread that is not a UI thread and has no running `SynchronizationContext` that would deadlock. Future work: if these call sites move to async entry points, remove the `.GetAwaiter().GetResult()` calls.

**Also** add a note to the `### NodeConfiguration.LoadFrom -- JM-P2-008` section replacing the "After:" pseudo-code (which shows `await`) with a note that the actual implementation uses `.GetAwaiter().GetResult()`:

In the NodeConfiguration call-site patch pseudo-code block, after the "After (read):" line, add:
```
// Note: LoadFrom is synchronous; .GetAwaiter().GetResult() used instead of await.
// A surrounding try-catch preserves the "never throws" contract.
```

**Success criteria:** No build or test changes needed. The document records the decision accurately.

---

### Task 2 -- D-026: Add null guard to `EntityPatch.AddField`

**File:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/Helpers/EntityPatch.cs`

**Problem:** The `AddField` overload that takes a `JsonNode defaultValue` parameter does `defaultValue?.DeepClone()`, which silently inserts JSON null when `defaultValue` is `null`. This is unexpected: callers intend to set a real default.

**What to do:**

In the `AddField(JsonObject root, string componentName, string fieldName, JsonNode defaultValue, CasingPolicy casing)` overload, add an `ArgumentNullException` guard as the first line of the method:

```csharp
if (defaultValue is null)
    throw new ArgumentNullException(nameof(defaultValue),
        "Use JsonValue.Create((object?)null) explicitly if a JSON null default is intended.");
```

**Also** add test T_EP_13 to `EntityPatchTests.cs`:

```csharp
// T_EP_13
[Fact]
public void AddField_NullDefaultValue_ThrowsArgumentNullException()
{
    var root = MakeScenarioRoot("e1", MakeEntityInfo());
    Assert.Throws<ArgumentNullException>(() =>
        EntityPatch.AddField(root, "EntityInfo", "NewField", (JsonNode)null!, CasingPolicy.ForcePascal));
}
```

**Run:** `dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" --no-build`

**Success criteria:** T_EP_13 passes. All other EntityPatch tests still pass.

---

### Task 3 -- D-027: Add `TransformComponent` tests to `EntityPatchTests.cs`

**File:** `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/EntityPatchTests.cs`

**Problem:** `EntityPatch.TransformComponent` has no unit tests (D-027). Future migrators will depend on it; correctness must be verified.

**What to do:**

Add the following three tests after the existing `OnComponent_EntityHasComponent_CallbackCalled` test:

```csharp
// -- Group 6: TransformComponent --

// T_EP_14
[Fact]
public void TransformComponent_EntityHasComponent_TransformApplied()
{
    var info = MakeEntityInfo("Alpha", "Friend");
    var root = MakeScenarioRoot("e1", info);

    EntityPatch.TransformComponent(root, "EntityInfo", (entity, component) =>
    {
        component["Rank"] = "Colonel";
    });

    var rank = root["entities"]!["e1"]!["EntityInfo"]!["Rank"]!.GetValue<string>();
    Assert.Equal("Colonel", rank);
}

// T_EP_15
[Fact]
public void TransformComponent_EntityLacksComponent_NothingHappens()
{
    // Entity has SimTransform, not EntityInfo.
    var root = MakeScenarioRoot("e1"); // no entityInfo param -> SimTransform only

    int callCount = 0;
    EntityPatch.TransformComponent(root, "EntityInfo", (entity, component) =>
    {
        callCount++;
    });

    Assert.Equal(0, callCount);
}

// T_EP_16
[Fact]
public void TransformComponent_TransformAddsComponentToEntity_SiblingVisible()
{
    var info = MakeEntityInfo();
    var root = MakeScenarioRoot("e1", info);

    EntityPatch.TransformComponent(root, "EntityInfo", (entity, component) =>
    {
        // The transform may add sibling components.
        entity["NewComp"] = new JsonObject { ["Value"] = 42 };
    });

    var newComp = root["entities"]!["e1"]!["NewComp"]!["Value"]!.GetValue<int>();
    Assert.Equal(42, newComp);
}
```

**Run:** `dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" --no-build`

**Success criteria:** T_EP_14, T_EP_15, T_EP_16 all pass. All existing EntityPatch tests still pass.

---

### Task 4 -- D-028: Add `InferCasing` tests via `AddField` with `MatchExisting`

**File:** `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/EntityPatchTests.cs`

**Problem:** `EntityPatch.InferCasing` is private and has no test coverage. Its tie-goes-to-Pascal rule, all-lowercase boundary, and mixed boundary are untested (D-028). Test it indirectly through `AddField` with `CasingPolicy.MatchExisting`.

**What to do:**

Add the following tests after the TransformComponent group:

```csharp
// -- Group 7: InferCasing (tested via AddField with CasingPolicy.MatchExisting) --

// T_EP_17
[Fact]
public void AddField_MatchExisting_AllPascalFields_NewFieldIsPascal()
{
    // EntityInfo has Name (Pascal) and ForceId (Pascal) -> majority Pascal.
    var root = MakeScenarioRoot("e1", MakeEntityInfo());

    EntityPatch.AddField(root, "EntityInfo", "tags", new JsonArray(), CasingPolicy.MatchExisting);

    var component = root["entities"]!["e1"]!["EntityInfo"]!.AsObject();
    Assert.True(component.ContainsKey("Tags"), "Expected 'Tags' (Pascal), got camel or absent.");
    Assert.False(component.ContainsKey("tags"), "Should not have lowercase 'tags'.");
}

// T_EP_18
[Fact]
public void AddField_MatchExisting_AllCamelFields_NewFieldIsCamel()
{
    // Build a component that has only camelCase fields.
    var camelComp = new JsonObject { ["name"] = "Alpha", ["forceId"] = "Friend" };
    var root = new JsonObject
    {
        ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
        ["entities"] = new JsonObject
        {
            ["e1"] = new JsonObject { ["CamelComp"] = camelComp }
        }
    };

    EntityPatch.AddField(root, "CamelComp", "Tags", new JsonArray(), CasingPolicy.MatchExisting);

    var component = root["entities"]!["e1"]!["CamelComp"]!.AsObject();
    Assert.True(component.ContainsKey("tags"), "Expected 'tags' (camel), got pascal or absent.");
    Assert.False(component.ContainsKey("Tags"), "Should not have Pascal 'Tags'.");
}

// T_EP_19
[Fact]
public void AddField_MatchExisting_EmptyComponent_DefaultsToPascal()
{
    // Empty component -> tie (0 Pascal vs 0 Camel) -> PascalCase wins.
    var emptyComp = new JsonObject();
    var root = new JsonObject
    {
        ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
        ["entities"] = new JsonObject
        {
            ["e1"] = new JsonObject { ["EmptyComp"] = emptyComp }
        }
    };

    EntityPatch.AddField(root, "EmptyComp", "field", JsonValue.Create(1)!, CasingPolicy.MatchExisting);

    var component = root["entities"]!["e1"]!["EmptyComp"]!.AsObject();
    Assert.True(component.ContainsKey("Field"), "Expected 'Field' (Pascal default for tie), got camel or absent.");
}

// T_EP_20
[Fact]
public void AddField_MatchExisting_EqualPascalAndCamel_PascalWinsTie()
{
    // 2 Pascal, 2 Camel -> tie -> Pascal wins.
    var mixedComp = new JsonObject
    {
        ["Name"] = "A",
        ["ForceId"] = "B",
        ["color"] = "red",
        ["weight"] = 10
    };
    var root = new JsonObject
    {
        ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
        ["entities"] = new JsonObject
        {
            ["e1"] = new JsonObject { ["MixedComp"] = mixedComp }
        }
    };

    EntityPatch.AddField(root, "MixedComp", "newField", JsonValue.Create("v")!, CasingPolicy.MatchExisting);

    var component = root["entities"]!["e1"]!["MixedComp"]!.AsObject();
    Assert.True(component.ContainsKey("NewField"), "Expected 'NewField' (Pascal wins tie), got camel or absent.");
}
```

**Run:** `dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" --no-build`

**Success criteria:** T_EP_17 through T_EP_20 all pass.

---

### Task 5 -- JM-P5-001: Add two minimal corpus entries

**Goal:** Expand the scenario corpus with two additional edge-case fixtures.

#### 5a. Minimal-entity scenario (single entity, minimal fields)

**Create:** `test-data/scenario-corpus/multi-version/v1_minimal-entity/scenario.json`

Content -- a v1 scenario with a single entity that has only `EntityInfo` (no `Tags` yet) and no `SimTransform`:

```json
{
  "$meta": { "docType": "Hrot.Scenario", "schemaVersion": 1 },
  "entities": {
    "aaaaaaaa-0001-0000-0000-000000000001": {
      "EntityInfo": {
        "Name": "Solo",
        "ForceId": "Neutral"
      }
    }
  }
}
```

**Create:** `test-data/scenario-corpus/multi-version/v2_minimal-entity/scenario.json`

Content -- the v2 migration result (Tags added):

```json
{
  "$meta": { "docType": "Hrot.Scenario", "schemaVersion": 2 },
  "entities": {
    "aaaaaaaa-0001-0000-0000-000000000001": {
      "EntityInfo": {
        "Name": "Solo",
        "ForceId": "Neutral",
        "Tags": []
      }
    }
  }
}
```

#### 5b. Empty-entities scenario (no entities at all)

**Create:** `test-data/scenario-corpus/multi-version/v1_empty-entities/scenario.json`

Content:

```json
{
  "$meta": { "docType": "Hrot.Scenario", "schemaVersion": 1 },
  "entities": {}
}
```

**Create:** `test-data/scenario-corpus/multi-version/v2_empty-entities/scenario.json`

Content (migration is a no-op since there are no entities):

```json
{
  "$meta": { "docType": "Hrot.Scenario", "schemaVersion": 2 },
  "entities": {}
}
```

**Add to BASELINES.md** (`test-data/scenario-corpus/BASELINES.md`): add a note in the "Corpus Inventory" section listing the two new pairs.

**Success criteria:** The new files exist. No test changes needed (the existing corpus tests only load `multi-version/v1_complete`; the new fixtures are standalone and will be used in future tests).

---

### Task 6 -- JM-P5-004: Stale-sidecar audit

**Goal:** Confirm no stale migration sidecars have accumulated in the workspace.

**What to look for:**
- Directories named `.migration-snapshots` or `.migration-journals` anywhere under `test-data/`, `scenarios/`, or project source trees.
- Any `.fdp.migration-*.json` or `*.migration-snapshot.json` files.

**How to check:**

Run in the terminal (from workspace root):
```
Get-ChildItem -Path . -Filter ".migration-snapshots" -Recurse -Directory
Get-ChildItem -Path . -Filter ".migration-journals" -Recurse -Directory
Get-ChildItem -Path . -Include "*.migration-*.json" -Recurse
```

**Expected result:** No matches. If any are found, document them in the report and file a new debt item.

**Deliverable:** A short note in the batch report stating whether the audit found anything.

---

## Final Verification

After all tasks:

1. Build the affected projects:
   ```
   dotnet build "Hrot/Engine/Hrot.Common/Hrot.Common.csproj" -c Debug --no-restore
   dotnet build "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" -c Debug --no-restore
   ```

2. Run the tests:
   ```
   dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" --no-build
   dotnet test "Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj" --no-build
   dotnet test "FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj" --no-build
        --filter "EX_T|Build_ComponentWithEntityInInlineArray|Defaults_MatchDesign"
   ```

3. Build the full solution:
   ```
   dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
   ```
   (Only `Hrot.Blueprints.Tests` CS0234/CS0246 errors are expected.)

---

## Report Format

Write your report to `.dev/json-migration/reports/BATCH-21-REPORT.md`.

Include:
- Summary table of tasks with PASS/FAIL/SKIP
- Test results before and after for each task
- Files created and modified
- Deviations from instructions (if any)

### Developer Insights Section (mandatory)

Answer these questions:
1. What issues were encountered during implementation?
2. What weak points were spotted in the codebase?
3. What design decisions were made beyond the spec?
