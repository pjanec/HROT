# CGF-1-BATCH-11 Report

**Batch:** CGF-1-BATCH-11  
**Author:** Developer  
**Date:** 2026-04-05  
**Status:** Complete — awaiting review

---

## Summary

Part A (two tech-debt items from BATCH-10 review) and Part B (CGF1-S0306 Scenario/Story
Serialization Toolkit) are fully delivered.  Solution builds clean; 22 / 22 orchestrator
tests pass (+2 new from Part A.1) and 10 / 10 new scenario-serializer tests pass.

---

## Part A — Tech debt closures

### A.1 — `StorageGatewayModule.PushToNodesAsync` unit tests (Issue 1, P3)

**File:** `Hrot.Orchestrator.Tests/StorageGatewayTests.cs`

Added two tests alongside the existing `Pull`-path parity tests:

| Test | What it covers |
|------|---------------|
| `PushToNodes_CopiesFileToAllTargets` | Creates a real NAS file, pushes to three local temp directories, asserts every destination copy exists and `GatewayResult.SuccessCount == 3`. |
| `PushToNodes_BadTarget_ReturnsPartialFailure` | Two good targets + one unreachable UNC path (`\\255.255.255.255\...`), asserts `SuccessCount == 2` and `FailureCount == 1`. |

Tests are in the same `StorageGatewayTests` class and use the existing `CreateNasFile` /
temp-directory helpers for symmetry with the pull tests.

**DEBT-TRACKER:** Row closed `✅`.

---

### A.2 — `ClusterMaster` XML hygiene (Issue 2, P3)

**File:** `Hrot.Orchestrator/ClusterMaster.cs`

Replaced invalid `<see cref="_remainingAcks"/>` (which referenced a non-existent private
member) with plain prose `SerializeLocalTask.RemainingAcks` in the XML summary of
`_pendingSerializeTasks`.  No production code changed.

```xml
<!-- Before -->
/// When <see cref="_remainingAcks"/> reaches zero, a round-trip pull from the NAS is triggered.

<!-- After -->
/// When <c>SerializeLocalTask.RemainingAcks</c> reaches zero, a round-trip pull from the NAS is triggered.
```

**DEBT-TRACKER:** Row closed `✅`.

---

### A.3 — DEBT-TRACKER

Rows for A.1 and A.2 closed `✅`.

---

## Part B — CGF1-S0306: Scenario/Story Serialization Toolkit

### New projects added to solution

| Project | Path |
|---------|------|
| `FDP.Toolkit.Scenario` | `FDP/Toolkits/FDP.Toolkit.Scenario/` |
| `FDP.Toolkit.Scenario.Tests` | `FDP/Toolkits/FDP.Toolkit.Scenario.Tests/` |

Both added to `IOS-IG-SimHost.sln`.

---

### B.1 — Component ID assignments

| Constant | ID | Notes |
|----------|----|-------|
| `ScenarioComponentIds.ScenarioIgnoreTag` | 200 | `[DataPolicy(NoSave)]` |
| `ScenarioComponentIds.StoryTag` | 201 | Managed class component, `[DataPolicy(NoSave)]` |
| `DummyPosition` (test-only) | 210 | |
| `TestBallisticProjectile` (test-only) | 211 | |
| `TestPhysicsCollider` (test-only) | 212 | |
| `GuidedTarget` (test-only) | 213 | contains `Entity TargetId` |
| `NoSaveVelocity` (test-only) | 214 | `[DataPolicy(NoSave)]` |
| `CachedSpeedComponent` (test-only) | 215 | has `[ScenarioIgnore] float CachedWheelAngle` |

---

### B.2 — Core API surface (`FDP.Toolkit.Scenario`)

#### `ScenarioIgnoreTag`
Unmanaged marker tag (ID 200, NoSave) — entities bearing this tag are skipped during
`Serialize`.

#### `StoryTag` (class, ID 201, NoSave)
Managed class component (required because `string?` fields are not `unmanaged`).  Stamped
on every entity when `Deserialize(asStory: true, storyId: ...)` is called.  Never written
to the scenario file.

#### `IEntityScenarioTranslator`
Interface for custom N:M translation:

```csharp
BitMask256 GetConsumedComponentsMask();
bool CanTranslate(EntityRepository repo, Entity entity);
Dictionary<string, object> Extract(EntityRepository, Entity, IGuidResolver);
void Inject(EntityRepository, Entity, Dictionary<string, object>, IGuidResolver);
```

#### `IGuidResolver`
Bidirectional `Entity ↔ string` resolver passed to translators:

```csharp
string Resolve(Entity entity);   // save path: entity → GUID string
Entity Resolve(string guidStr);  // load path: GUID string → entity
```

#### `FdpAutoSerializer`
Expression-tree compiled 1:1 fallback serializer.  Compiles one
`Func<EntityRepository, Entity, IGuidResolver, JsonObject?>` extract delegate and one
`Action<EntityRepository, Entity, JsonNode?, IGuidResolver>` inject delegate per saveable
value-type component at `Build()` time.  No `PropertyInfo.GetValue` on the hot path
(`UsesRuntimeReflection == false` after build).

Key implementation details:
- Uses `GetComponentCopy<T>` static wrapper to avoid expression-tree `ref return`
  incompatibility with `EntityRepository.GetComponent<T>`.
- Uses `CreateJsonValue<T>` static wrapper to avoid the optional `JsonNodeOptions?`
  parameter that `JsonValue.Create<T>` acquired in .NET 8.
- `ScenarioIgnoreAttribute` on a field excludes it from both extract and inject paths.
- `Entity`-typed fields are transparently replaced with GUID strings via `IGuidResolver`.

#### `ScenarioSerializerBuilder` + `ScenarioSerializer`
Fluent builder; call `RegisterTranslator` for each custom translator, then `Build()`.

**Serialize pipeline (per entity):**
1. Per-entity saveable mask = `globalSaveableMask & entityComponentMask`.
2. For each `IEntityScenarioTranslator` where `CanTranslate` is true: `Extract` → add
   named DOM entries → `ClearConsumed` from remaining mask.
3. `FdpAutoSerializer` processes remaining set bits (1:1 auto-serialized components).

**Deserialize pipeline:**
1. Guard: peek `Header.SubsystemType`; bail if mismatch (no entities created).
2. Pass 1: create entities, build `guidToEntity` map.
3. Pass 2: build full `scenarioData` dict from all entity node keys; call all translator
   `Inject` methods unconditionally (each translator self-filters via its own keys);
   auto-serializer handles remaining keys by `FindTypeIdByName` lookup.
4. Stamp `StoryTag` on all entities if `asStory == true`.

---

### B.3 — Design decisions and deviations

**`StoryTag` as class (not struct)**  
The original spec described `StoryTag` with `string? StoryId` as a managed struct.
`EntityRepository.RegisterManagedComponentInternal<T>()` carries a `where T : class`
constraint; a struct containing a reference field violates it.  Changed to a class to
satisfy the constraint while keeping the same public API.  Since `StoryTag` is `NoSave`
and in the test it is observed only via `HasComponent` / `GetComponent`, the change is
transparent to callers.

**N:M translator inject path**  
The initial `Deserialize` implementation checked whether any of the translator's *consumed
component type names* (e.g. "TestBallisticProjectile", "TestPhysicsCollider") appeared as
keys in the entity's DOM node to decide whether to call `Inject`.  This works for 1:1
translators but breaks for N:M translators that use custom DOM keys (e.g. "OrdnanceDef").
Fixed by building a full `scenarioData` dict from all entity node keys unconditionally
and calling every translator's `Inject` (each translator self-filters via `TryGetValue`).

**`GetComponentCopy<T>` wrapper**  
`EntityRepository.GetComponent<T>` returns `ref readonly T`; the expression-tree API
(`Expression.Assign`) cannot consume a by-ref return.  A private static wrapper
`GetComponentCopy<T>(EntityRepository, Entity)` was added to `FdpAutoSerializer` — it
delegates to `TryGetComponent<T>` which returns `T` by value through the `out` parameter.

**`CreateJsonValue<T>` wrapper**  
In .NET 8 `JsonValue.Create<T>(T, JsonNodeOptions?)` has two parameters (the second is
optional in C# but not in `MethodInfo`).  `Expression.Call(method, oneArg)` fails unless
the arity matches exactly.  A private static wrapper `CreateJsonValue<T>(T value)` was
added to `FdpAutoSerializer` to provide a strict 1-parameter signature for expression-tree
invocation.

---

### B.4 — Test success conditions (all 10 verified)

| Test | Result |
|------|--------|
| `RoundTrip_1to1_PreservesAllFields` | ✅ Pass |
| `NtoM_CustomTranslator_CompressesComponents` | ✅ Pass |
| `ConsumptionMask_PreventsDuplication` | ✅ Pass |
| `EntityCrossReference_ResolvedViaIGuidResolver` | ✅ Pass |
| `DataPolicyNoSave_ComponentExcluded` | ✅ Pass |
| `ScenarioIgnore_FieldExcluded` | ✅ Pass |
| `ScenarioIgnoreTag_EntitySkipped` | ✅ Pass |
| `StoryLoad_StampsStoryTag` | ✅ Pass |
| `SubsystemType_MismatchSkipsDeserialize` | ✅ Pass |
| `FdpAutoSerializer_NoReflectionOnHotPath` | ✅ Pass |

---

## File Map

### Modified files

| File | Change |
|------|--------|
| `Hrot.Orchestrator.Tests/StorageGatewayTests.cs` | +2 `PushToNodes` tests (A.1) |
| `Hrot.Orchestrator/ClusterMaster.cs` | XML hygiene fix (A.2) |
| `IOS-IG-SimHost.sln` | Added 2 new projects |

### New files (`FDP.Toolkit.Scenario`)

| File | Purpose |
|------|---------|
| `FDP.Toolkit.Scenario.csproj` | Project file |
| `ScenarioComponentIds.cs` | ID constants 200, 201 |
| `IEntityScenarioTranslator.cs` | N:M translator interface |
| `IGuidResolver.cs` | Entity ↔ GUID resolver interface |
| `ScenarioHeader.cs` | Serialized header record |
| `ScenarioIgnoreAttribute.cs` | Per-field exclusion attribute |
| `ScenarioIgnoreTag.cs` | Entity-level skip marker (ID 200) |
| `StoryTag.cs` | Story membership tag (ID 201, class) |
| `FdpAutoSerializer.cs` | Expression-tree compiled 1:1 fallback |
| `ScenarioSerializerBuilder.cs` | Fluent builder |
| `ScenarioSerializer.cs` | Save/load orchestrator |

### New files (`FDP.Toolkit.Scenario.Tests`)

| File | Purpose |
|------|---------|
| `FDP.Toolkit.Scenario.Tests.csproj` | Test project file |
| `TestComponents.cs` | Test-only component types (IDs 210–215) + `MissileOrdnanceTranslator` |
| `ScenarioSerializerTests.cs` | 10 unit tests |

---

## Test Results

```
Hrot.Orchestrator.Tests:  Passed: 22 / 22  (+2 new: PushToNodes tests)
FDP.Toolkit.Scenario.Tests: Passed: 10 / 10  (all new)
```

---

## Outstanding Issues / Next Steps

- `CGF1-S0307` Application-Layer Scenario Save/Load Wiring — planned **CGF-1-BATCH-12**
  (builds on this toolkit; wires `ScenarioSerializer` into `CgfApplication` /
  `StorageGatewayModule` for the actual save and load operations).
- `CGF1-S0302` Portable Scenario Loading — planned after **S0306 + S0307**.
