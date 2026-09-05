# BATCH-02: NoSave Blackboard Components + BlueprintStateTranslator (BSA-101 + BSA-202)

**Batch Number:** BATCH-02  
**Tasks:** BSA-101 (Mark blackboard components `NoSave`), BSA-202 (`BlueprintStateTranslator` + legacy black-hole + AssetId emit fix)  
**Phase:** Phase 1 (BSA-101) + Phase 2 (BSA-202)  
**Estimated Effort:** 6-8 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (BSA-102 — core attach/detach seam)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Mark the three `BlueprintBlackboard*` ECS components as `[DataPolicy(DataPolicy.NoSave)]` so volatile runtime bytes stop leaking into scenario JSON. Then create the `BlueprintStateTranslator` that extracts/loads declarative blueprint assignments instead, with legacy-key black-holing so old scenarios don't crash. Also fix the compiler emitter to populate `BlueprintDefinition.AssetId` — it exists but is never set.

### Required Reading (IN ORDER)
1. **Design Document:** `.dev/_DONE/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` — §2 (NoSave principle), §4 (static assignment / translator), §11 (AssetId fix)
2. **Task Details:** `.dev/_DONE/blueprint-scenario/TASK-DETAIL.md` — BSA-101 and BSA-202 sections
3. **Task Tracker:** `.dev/_DONE/blueprint-scenario/TASK-TRACKER.md`
4. **Previous Review:** `.dev/_DONE/blueprint-scenario/reviews/BATCH-01-REVIEW.md`

### Source Code Location
- **Blackboard components (edit):** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard1024.cs`, `4096.cs`, `16384.cs`
- **DTO + Intent (new):** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintAssignmentDto.cs` + `Hrot/Engine/Hrot.Common/Serializers/InitialBlueprintsIntent.cs`
- **Translator (new):** `Hrot/Subsystems/Hrot.SimHost/Serializers/BlueprintStateTranslator.cs`
- **Serializer factory (edit):** `Hrot/Subsystems/Hrot.SimHost/Serializers/HrotScenarioSerializerFactory.cs`
- **Compiler emitter (edit):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/CSharpEmitter.cs`
- **Pattern reference — black-hole translator:** `Hrot/Subsystems/Hrot.SimHost/Serializers/Blackboard1024Translator.cs`
- **Pattern reference — intent component:** `Hrot/Engine/Hrot.Common/Serializers/InitialPassengersIntent.cs`
- **Interface:** `FDP/Toolkits/Fdp.Toolkits/Scenario/IEntityScenarioTranslator.cs`
- **BlueprintRegistry:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs`
- **Partition API:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs`
- **Core attach seam (Task 4 test helper):** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintInstanceService.cs` (from BATCH-01)

### Report Submission
**When done, submit your report to:**  
`.dev/_DONE/blueprint-scenario/reports/BATCH-02-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

Today `BlueprintBlackboard{1024,4096,16384}` carry `[ComponentId]` but **no `[DataPolicy]`**, so they serialize into scenario JSON by default — this is the original bug. The fix is two-fold and must land together: (1) mark them `NoSave`, (2) provide a translator that black-holes the legacy keys so old scenarios load without error.

**BSA-101 must not ship without BSA-202's legacy black-hole** — old scenarios with `BlueprintBlackboard*` keys would hit `FdpAutoSerializer` (which doesn't know about the now-`NoSave` components) and throw `InvalidOperationException`.

BSA-202 also **must fix the AssetId emit** first — the `Extract` method maps `BlueprintId` → `AssetId` via the registry, but `def.AssetId` is always `Guid.Empty` because the compiler never sets it (Design §11).

---

## 🎯 Batch Objectives

1. Mark 3 blackboard components `[DataPolicy(DataPolicy.NoSave)]`
2. Create `BlueprintAssignmentDto` (simple DTO) + `InitialBlueprintsIntent` (transient managed component)
3. Fix compiler emitter to populate `BlueprintDefinition.AssetId`
4. Create `BlueprintStateTranslator : IEntityScenarioTranslator` (Extract assignments, Inject intent, black-hole legacy keys)
5. Register translator in `HrotScenarioSerializerFactory`

---

## ✅ Tasks

### Task 1: Mark blackboard components `NoSave` (BSA-101)

**Files:** 
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard1024.cs` (EDIT)
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard4096.cs` (EDIT)
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard16384.cs` (EDIT)

**Description:** Add `[DataPolicy(DataPolicy.NoSave)]` attribute to all three structs. Mirrors the pattern used on `Blackboard1024`, `BrainBlackboard`, etc. in `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs`.

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BlueprintBlackboard1024)]
[DataPolicy(DataPolicy.NoSave)]  // ← ADD THIS LINE
public unsafe struct BlueprintBlackboard1024
```

Add `using Fdp.Core;` if not already present (the attribute lives in `Fdp.Core`).

**Tests required:**
- **Test 1 — Reflection:** Verify each of the three structs has the `DataPolicyAttribute` with value `DataPolicy.NoSave`. Use `typeof(BlueprintBlackboard1024).GetCustomAttribute<DataPolicyAttribute>()` and assert `.Policy == DataPolicy.NoSave`.
- **Test 2 — Serialization exclusion:** Create an entity with `BlueprintBlackboard1024`, serialize via `ScenarioSerializer.SerializeEntity` (or the full serializer), assert the JSON does NOT contain the string `"BlueprintBlackboard1024"`.

Note: Test 2 may fail until Task 4's translator is in place (the serializer might throw without a handler). That's fine — it verifies the coupling between BSA-101 and BSA-202. Just document it.

---

### Task 2: Create `BlueprintAssignmentDto` + `InitialBlueprintsIntent` (BSA-202 prereq)

**Files (NEW):**
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintAssignmentDto.cs`
- `Hrot/Engine/Hrot.Common/Serializers/InitialBlueprintsIntent.cs`

**`BlueprintAssignmentDto`** — simple data class in `Fdp.Toolkit.Blueprints` namespace:
```csharp
namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Declarative blueprint assignment for scenario persistence.
/// Stored inside <see cref="InitialBlueprintsIntent"/>.
/// </summary>
public sealed class BlueprintAssignmentDto
{
    /// <summary>The stable Asset GUID of the Instance Blueprint.</summary>
    public required Guid AssetId { get; init; }
    
    /// <summary>Per-variable overrides. Null/empty in MVP (see Design §6).</summary>
    public Dictionary<string, object>? Overrides { get; init; }
}
```

**`InitialBlueprintsIntent`** — transient managed component. Study the pattern in `Hrot/Engine/Hrot.Common/Serializers/InitialPassengersIntent.cs` first, then mirror it:
```csharp
namespace Hrot.Common.Serializers;

[DataPolicy(DataPolicy.Transient)]
[ComponentId(GlobalComponentIds.InitialBlueprintsIntent)]
public sealed class InitialBlueprintsIntent
{
    public List<BlueprintAssignmentDto> Blueprints { get; set; } = new();
}
```

**CRITICAL for `ComponentId`:** You must add a new constant `InitialBlueprintsIntent` to the `GlobalComponentIds` class. Find it (likely `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` or similar) and add a new entry with a unique, non-colliding integer value. Follow the existing naming/numbering pattern.

**Tests required:**
- **Test 3 — DTO JSON round-trip:** Serialize a `BlueprintAssignmentDto` with `AssetId = Guid.NewGuid()` and `Overrides = null`; assert deserialized object equals original and serialized JSON has no `"Overrides"` key. Repeat with populated `Overrides`.
- **Test 4 — Intent round-trip:** In a world with `InitialBlueprintsIntent` registered as a managed component, `world.SetManagedComponent(entity, new InitialBlueprintsIntent { Blueprints = ... })` then `world.GetManagedComponent<InitialBlueprintsIntent>(entity)` returns the same data.

---

### Task 3: Fix compiler emit — populate `BlueprintDefinition.AssetId` (BSA-202 prereq)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/CSharpEmitter.cs` (EDIT)

**Description:** The `EmitInstanceRegistration` method (line ~248) emits the `BlueprintDefinition` constructor but never sets `AssetId`. Add it.

In `EmitInstanceRegistration`, after `StateSize = {className}.StateSize,` (line ~264), add:
```
AssetId = new Guid("{asset.AssetId}"),
```

The `asset.AssetId` is already available — it's used in the `EmitFileHeader` method (line 121): `WriteLine($"// Asset: {asset.Name} ({asset.AssetId})");`

Use `asset.AssetId.ToString("D")` to get the hyphenated GUID format that `new Guid(string)` expects.

The `asset` parameter is `IrAsset asset` — check its type. `asset.AssetId` should be a `Guid`.

**Tests required:**
- **Test 5 — AssetId populated after compile:** Compile an Instance blueprint, register it, then call `registry.TryGetById(id, out var def)` → assert `def.AssetId != Guid.Empty` and `def.AssetId == <the asset's original Guid>`.
  - Use the existing test infrastructure: `BlueprintAssetBuilder` to build an Instance asset, compile it, check the registry.
  - Study `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage7_EmitTests/InstanceEmitGoldenTests.cs` for the compilation test pattern.
- **Test 6 — Golden snapshots update:** If the existing golden snapshot tests (`InstanceEmitGoldenTests.Instance_EmitMatchesGoldenSource`) fail because the emitted source now includes `AssetId`, update the golden files. **This is the one exception to the no-snapshot-regen rule** — the golden MUST be updated because the emitted code intentionally changed.

**⚠️ This test will also verify that old scenarios hold valid AssetIds for the translator's id→AssetId reverse lookup.**

---

### Task 4: Create `BlueprintStateTranslator` + register it (BSA-202)

**Files (NEW):**
- `Hrot/Subsystems/Hrot.SimHost/Serializers/BlueprintStateTranslator.cs`

**Files (EDIT):**
- `Hrot/Subsystems/Hrot.SimHost/Serializers/HrotScenarioSerializerFactory.cs`

#### 4a. Create `BlueprintStateTranslator`

Implement `IEntityScenarioTranslator`. Study the existing translators first — especially `BrainBlackboardTranslator.cs` and `Blackboard1024Translator.cs` for the no-op Inject + GetOutputDomKeys pattern.

```csharp
namespace Hrot.SimHost.Serializers;

public sealed class BlueprintStateTranslator : IEntityScenarioTranslator
{
    private const string OutputKey = "BlueprintAssignments";
    
    // Legacy blackboard keys — claimed so FdpAutoSerializer doesn't try to
    // deserialize old scenarios that still carry these after NoSave migration.
    private static readonly string[] LegacyBlackboardKeys =
    {
        "BlueprintBlackboard1024",
        "BlueprintBlackboard4096",
        "BlueprintBlackboard16384",
    };
    
    private readonly BlueprintRegistry _registry;
    
    public BlueprintStateTranslator(BlueprintRegistry registry) { ... }
    
    // GetConsumedComponentsMask: return mask with bits set for all three
    // BlueprintBlackboard* component IDs (use ComponentTypeRegistry.GetId)
    
    // CanTranslate: true if entity has ANY BlueprintBlackboard* component
    
    // Extract:
    //   For each BlueprintBlackboard* tier on the entity:
    //     - Get component memory via GetComponentRO
    //     - Call BlueprintBlackboardPartitions.GetSlotCount(memory)
    //     - Loop 0..count-1, call GetSlot(memory, i), read slot.BlueprintId
    //     - Map id → AssetId via _registry.TryGetById(id, out def) → def.AssetId
    //     - Skip if not found (shouldn't happen, but be resilient)
    //   Return Dictionary with [OutputKey] = List<BlueprintAssignmentDto> (as serializable objects)
    //   IMPORTANT: Return the Dto objects as anonymous objects or a serializable form:
    //     new { AssetId = dto.AssetId.ToString(), Overrides = dto.Overrides }
    //   Or use a Dictionary<string, object> for each assignment.
    //   The serializer needs to be able to serialize these to JSON.
    
    // Inject:
    //   If scenarioData contains OutputKey:
    //     - Parse the JSON array into BlueprintAssignmentDto[]
    //     - Set an InitialBlueprintsIntent managed component on the entity
    //   For legacy keys: NO-OP (black-hole — don't inject anything)
    
    // GetOutputDomKeys:
    //   yield return OutputKey;
    //   foreach (var key in LegacyBlackboardKeys) yield return key;
}
```

**Extract implementation details:**
- Use `fixed` or `Unsafe.AsPointer` to get `byte*` from the fixed buffer — study how `BlueprintAttachService.GetTierMemoryAndMeta` does this (now in `BlueprintInstanceService.cs`)
- For each tier: `world.HasComponent<T>(entity)` → `world.GetComponentRO<T>(entity)` → get byte pointer → `GetSlotCount`/`GetSlot`
- Map slot entries to `BlueprintAssignmentDto` with `AssetId = def.AssetId`
- Return serializable data. The `Dictionary<string, object>` value should be something the JSON serializer can handle. The simplest: return a `List<Dictionary<string, object>>` where each dict has `"AssetId"` (string GUID) and optionally `"Overrides"`.

**Inject implementation details:**
- For `OutputKey`: parse the JSON array, create `InitialBlueprintsIntent`, set on entity via `repo.SetManagedComponent(entity, intent)`
- For legacy keys: no-op (just return)
- Parse scenario data using `System.Text.Json` — the `scenarioData[OutputKey]` is a `JsonElement` or similar. Cast it.

**Critical: `CanTranslate` must also return `true` during load when the entity has the legacy keys in its DOM, not just the blackboard components.** Actually, no — `CanTranslate` is called on the entity repo during EXTRACT. During load/INJECT, the serializer uses `GetOutputDomKeys()` to route keys. The `CanTranslate` gate doesn't apply to inject — it's only for extract. For inject to work, the translator just needs to be registered and its `GetOutputDomKeys()` to claim the right keys.

Verify this by reading `ScenarioSerializer.cs` deserialization path, specifically how it routes keys to translators. If `GetOutputDomKeys()` is enough for the routing during deserialization, `CanTranslate` only matters during extract.

**⚠️ IMPORTANT — verify the serializer routing:** Check `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` around line 389 (as referenced in the design doc) to understand how `GetOutputDomKeys()` is used during deserialization. Make sure the routing works as described in the design.

#### 4b. Register in factory

In `HrotScenarioSerializerFactory.Build()`, add:
```csharp
.RegisterTranslator(new BlueprintStateTranslator(blueprintRegistry))
```

**Problem:** `HrotScenarioSerializerFactory.Build()` currently only takes `BehaviorRegistry`. You need to also pass `BlueprintRegistry`. Add it as a parameter:
```csharp
public static ScenarioSerializer Build(BehaviorRegistry behaviorRegistry, BlueprintRegistry blueprintRegistry)
```

Then find all callers of `HrotScenarioSerializerFactory.Build(...)` and update them to pass the blueprint registry. Search for `HrotScenarioSerializerFactory.Build` across the codebase.

**Tests required:**

All tests should use the real production path — no mocks for the unit under test.

- **Test 7 — Extract round-trip:** Create an entity, attach 2 Instance blueprints via `BlueprintInstanceService.AttachToEntity` (from BATCH-01), create the translator, call `Extract()`, assert:
  - Result dictionary contains key `"BlueprintAssignments"`
  - The array has exactly 2 entries
  - Each entry has the correct `AssetId` (matching the blueprints' asset GUIDs)
  - Result does NOT contain any `BlueprintBlackboard*` keys

- **Test 8 — Inject → Intent:** Create a scenario data dictionary with `"BlueprintAssignments"` = array of `{ AssetId = "<guid>" }` objects, call `Inject()`, assert the entity has `InitialBlueprintsIntent` with the correct `BlueprintAssignmentDto`s.

- **Test 9 — Legacy black-hole:** Create scenario data with a `"BlueprintBlackboard1024"` key (simulating an old scenario), call `Inject()`. Assert no exception is thrown and the entity does NOT get a `BlueprintBlackboard1024` component added.

- **Test 10 — GetOutputDomKeys returns all 4 keys:** Call `GetOutputDomKeys()` and assert the returned collection (after `.ToList()`) contains exactly: `"BlueprintAssignments"`, `"BlueprintBlackboard1024"`, `"BlueprintBlackboard4096"`, `"BlueprintBlackboard16384"`.

- **Test 11 — CanTranslate returns true for entity with blackboard:** Create entity with `BlueprintBlackboard1024`, assert `CanTranslate(repo, entity)` is true.

- **Test 12 — AssetId emit fix (cross-check with Task 3):** After compiling + registering an Instance blueprint, `registry.TryGetById(id, out def)` → `def.AssetId == <asset GUID>` (not `Guid.Empty`).

---

## 🧪 Testing Requirements

**Test files:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/` — BSA-101 tests (DataPolicy reflection)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage7_EmitTests/` — AssetId emit tests (or alongside existing golden tests)
- `Hrot/Subsystems/Hrot.SimHost.Tests/` — Translator tests (create `BlueprintStateTranslatorTests.cs`)

**Test quality rules (from TASK-DETAIL.md header):**
- Every test must drive the real production path — no mocks for the unit under test
- Assert on concrete values (counts, GUIDs, exact keys), never on string presence or log output
- One test per bullet in the success conditions
- Do NOT weaken existing tests
- Do NOT regenerate snapshots except for the golden emit update (Test 6)

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] All three `BlueprintBlackboard*` structs have `[DataPolicy(DataPolicy.NoSave)]`
- [ ] `BlueprintAssignmentDto` created in `Fdp.Toolkit.Blueprints`
- [ ] `InitialBlueprintsIntent` created with unique `ComponentId` + `[Transient]`
- [ ] `CSharpEmitter.EmitInstanceRegistration` populates `AssetId`
- [ ] `BlueprintStateTranslator` created implementing `IEntityScenarioTranslator` with correct Extract/Inject/GetOutputDomKeys
- [ ] Translator registered in `HrotScenarioSerializerFactory.Build()`
- [ ] All callers of `HrotScenarioSerializerFactory.Build()` updated with new parameter
- [ ] All 12 specified tests pass
- [ ] All pre-existing tests in touched projects pass (0 net-new failures)
- [ ] Golden emit snapshots updated (expected change)

---

## ⚠️ Common Pitfalls to Avoid

1. **BSA-101 ALONE WILL BREAK SCENARIO LOAD.** Do not commit the `NoSave` attributes without the translator. They are a single commit.
2. **`ComponentId` collision.** When adding `InitialBlueprintsIntent` to `GlobalComponentIds`, pick a value that doesn't collide. Search the file for the highest used value.
3. **Serializer routing.** Verify that `GetOutputDomKeys()` is sufficient for the deserialization routing. If `CanTranslate()` is also checked during inject, the legacy keys won't route correctly. Read `ScenarioSerializer.cs` around line 389 to confirm.
4. **`HrotScenarioSerializerFactory.Build()` callers.** You must find and update all call sites. Use grep: `HrotScenarioSerializerFactory\.Build`.
5. **Golden snapshot update.** The `InstanceEmitGoldenTests` will fail after the AssetId emit change — this is expected. Update the `.golden` files. This is the ONLY snapshot regen allowed.
6. **Extract must not allocate per-call managed memory** — no `List<T>.Add` in a loop without pre-allocation. Use `stackalloc` or pre-sized collections.
7. **`GetComponentRO` vs `GetComponentRW`.** Extract reads only, so use `GetComponentRO`. Inject writes, so use `GetComponentRW` or `SetManagedComponent`.

---

## 📊 Report Requirements

- **Q1:** What issues did you encounter? How did you resolve them?
- **Q2:** What design decisions did you make beyond the spec?
- **Q3:** Does `GetOutputDomKeys()` alone route legacy keys during deserialization, or did you need `CanTranslate` changes too? What did you find in `ScenarioSerializer.cs`?
- **Q4:** List all callers of `HrotScenarioSerializerFactory.Build()` that you updated.
- **Q5:** What value did you assign as `GlobalComponentIds.InitialBlueprintsIntent`?
- **Q6:** Did the golden emit snapshots need updating? List the files changed.
- **Q7:** Suggested commit message.

---

## 📚 Reference Materials
- **Design:** `.dev/_DONE/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` — §2, §4, §10, §11
- **Task Details:** `.dev/_DONE/blueprint-scenario/TASK-DETAIL.md` — BSA-101 and BSA-202
- **Pattern — black-hole translator:** `Hrot/Subsystems/Hrot.SimHost/Serializers/Blackboard1024Translator.cs`
- **Pattern — intent component:** `Hrot/Engine/Hrot.Common/Serializers/InitialPassengersIntent.cs`
- **Pattern — NoSave components:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs` (line 58-60, `BrainBlackboard`)
- **Interface:** `FDP/Toolkits/Fdp.Toolkits/Scenario/IEntityScenarioTranslator.cs`
- **Serializer factory:** `Hrot/Subsystems/Hrot.SimHost/Serializers/HrotScenarioSerializerFactory.cs`
- **Compiler emitter:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/CSharpEmitter.cs`
- **Core attach seam:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintInstanceService.cs`
- **Partition API:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs`
