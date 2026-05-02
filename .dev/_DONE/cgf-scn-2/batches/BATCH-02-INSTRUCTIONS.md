# BATCH-02: MissionPlan Serialization + FdpAutoSerializer Upgrade

**Batch Number:** BATCH-02
**Tasks:** TASK-S201, TASK-S202, TASK-S301, TASK-S302
**Phase:** Phase 2 — MissionPlan Scenario Serialization; Phase 3 — FdpAutoSerializer Upgrade
**Estimated Effort:** 8-12 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 complete (WeaponChannelTranslator removed from SimHostApp.cs)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements Phase 2 and Phase 3 of the cgf-scn-2 workstream.

- **Phase 2 (TASK-S201, S202):** Create `MissionPlanTranslator` and register it at 3 sites.
  This is a custom `IEntityScenarioTranslator` that handles `ActiveMissionPlan` (managed)
  and `MissionPlanQueue` (unmanaged InlineArray) atomically.
- **Phase 3 (TASK-S301, S302):** Upgrade `FdpAutoSerializer` to correctly serialize
  `fixed` buffers and `[InlineArray]` types using compiled expression trees.

### Required Reading (IN ORDER)

1. **Batch-01 Review:** `.dev/cgf-scn-2/reviews/BATCH-01-REVIEW.md` — No issues, clean handoff.
2. **Design Doc Phase 2:** `.dev/cgf-scn-2/DESIGN.md` — Section "Phase 2: MissionPlan Scenario Serialization" in full.
3. **Design Doc Phase 3:** `.dev/cgf-scn-2/DESIGN.md` — Section "Phase 3: FdpAutoSerializer Upgrade for Unmanaged Memory Layouts" in full.
4. **Task Definitions:** `.dev/cgf-scn-2/TASK-DETAIL.md` — Tasks TASK-S201 through TASK-S302.
5. **Design Talk (reference implementation):** `.dev/cgf-scn-2/design-talk.md` — Contains
   a near-complete reference implementation of `MissionPlanTranslator`. Use it, but align
   the `IEntityScenarioTranslator` interface to match `PassengerBufferTranslator`.

### Source Code to Study Before Starting

| File | Why |
|---|---|
| `Hrot/Subsystems/Hrot.SimHost/Serializers/PassengerBufferTranslator.cs` | Reference `IEntityScenarioTranslator` implementation pattern |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/TargetMemoryTranslator.cs` | Another reference translator |
| `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs` | File to be upgraded for Phase 3 |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/DomainMissionPlan.cs` | `ActiveMissionPlan`, `DomainMissionPlan` types |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/MissionComponents.cs` | `MissionPlanQueue`, `MissionPhaseBuffer`, `MissionPhase`, `MissionTrigger` |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Registration site 1 (already has BehaviorRegistry constructed via `CgfBehaviorSetup`) |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Registration site 2 (has `behaviorRegistry` local var) |
| `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs` | Registration site 3 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/ScenarioSerializerTests.cs` | Test patterns for scenario serializer |

### Source Code Locations for New Files

| File | Change |
|---|---|
| `Hrot/Subsystems/Hrot.SimHost/Serializers/MissionPlanTranslator.cs` | NEW — TASK-S201 |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | UPDATE — add `MissionPlanTranslator` registration (TASK-S202) |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | UPDATE — add `MissionPlanTranslator` registration (TASK-S202) |
| `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs` | UPDATE — add `MissionPlanTranslator` registration (TASK-S202) |
| `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs` | UPDATE — fixed buffer + InlineArray (TASK-S301, S302) |

### Test Projects

- `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj` — new FdpAutoSerializer tests (Phase 3)
- `Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj` — new MissionPlanTranslator tests (Phase 2)

Build and test with:
```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln --no-restore
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build
```

### Report Submission

When done, submit your report to:
`.dev/cgf-scn-2/reports/BATCH-02-REPORT.md`

---

## Context

**Phase 2:** When the editor saves a scenario, `ActiveMissionPlan` (managed class) is skipped
by `FdpAutoSerializer` entirely, and `MissionPlanQueue` (InlineArray) is truncated to its
first element. `MissionPlanTranslator` intercepts both and handles them atomically via
the custom translator path. See DESIGN.md Phase 2 for the root cause analysis.

**Phase 3:** The C# compiler lowers `fixed` buffers and `[InlineArray]` to a struct with a
single backing field, so reflection sees only one element. The expression-tree compiler in
`FdpAutoSerializer` must be extended to detect these and emit loops. DESIGN.md Phase 3
explains the detection approach and the critical `Entity`-in-buffer constraint.

---

## Batch Objectives

- Implement `MissionPlanTranslator` so mission plans survive scenario round-trips
- Register it at all 3 `ScenarioSerializerBuilder` sites
- Upgrade `FdpAutoSerializer` to correctly serialize `fixed` buffers (TASK-S301)
- Upgrade `FdpAutoSerializer` to correctly serialize `[InlineArray]` types (TASK-S302)
- All backed by unit tests verifying actual serialization correctness

---

## Tasks

### Task 1: Implement MissionPlanTranslator (TASK-S201)

**File:** `Hrot/Subsystems/Hrot.SimHost/Serializers/MissionPlanTranslator.cs` (NEW FILE)
**Task Definition:** See [TASK-DETAIL.md — TASK-S201](../TASK-DETAIL.md#task-s201-implement-missionplantranslator)

**Key interface to implement:** `IEntityScenarioTranslator` (same interface as `PassengerBufferTranslator`).

**Critical method signatures:**
```csharp
public sealed class MissionPlanTranslator : IEntityScenarioTranslator
{
    private const string Key = "MissionPlan";
    private readonly BehaviorRegistry _registry;

    public MissionPlanTranslator(BehaviorRegistry registry) { ... }

    public BitMask256 GetConsumedComponentsMask()  // set bits for MissionPlanQueue AND mask managed ActiveMissionPlan
    public string[] GetOutputDomKeys() => new[] { Key };
    public bool CanTranslate(EntityRepository repo, Entity entity) // true iff entity has ActiveMissionPlan
    public Dictionary<string, object> Extract(EntityRepository repo, Entity entity, IGuidResolver resolver)
    public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> data, IGuidResolver resolver)
}
```

**Extract logic:**
1. Get `ActiveMissionPlan` via `((ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(entity)`
2. Get `MissionPlanQueue` via `repo.GetComponent<MissionPlanQueue>(entity)`
3. Serialize `ActiveMissionPlan.Plan` to a JSON string using `HrotSerializerOptions.HrotJsonOptions`
   (`Hrot.Common.Scenario.HrotSerializerOptions` or similar — find it in the Hrot.Common project)
4. Build a `JsonObject` with keys `"PlanData"`, `"CurrentPhase"`, `"PhaseElapsedSeconds"`

**Inject logic:**
1. Read `"PlanData"` from DOM and deserialize to `DomainMissionPlan`
2. Call `repo.SetManagedComponent(entity, new ActiveMissionPlan { Plan = domainPlan })`
3. Rebuild `MissionPlanQueue`: for each task, call `_registry.TryGetId(task.BehaviorId, out int behaviorId)`;
   use `MissionTrigger.BehaviorFinished` as default trigger; populate `queue.Phases[i]`
4. Call `repo.SetComponent(entity, queue)`

**Reference:** `.dev/cgf-scn-2/design-talk.md` lines ~90-160 has a near-complete implementation.
Adapt it to match the `IEntityScenarioTranslator` interface exactly as `PassengerBufferTranslator` does.

**GetConsumedComponentsMask:** Must include `MissionPlanQueue` type ID (so auto-serializer skips it).
`ActiveMissionPlan` is managed — the auto-serializer already skips managed types, but you
must also include its ID in the mask to be explicit. Check how `IEntityScenarioTranslator`
is consumed in `FdpAutoSerializer` / `ScenarioSerializerBuilder` to confirm.

**Required Tests (add to `Hrot.SimHost.Tests` in a new file `MissionPlanTranslatorTests.cs`):**

1. **Extract test:** Create entity with `ActiveMissionPlan` (1 task, `BehaviorId = "FireAtTarget"`)
   + matching `MissionPlanQueue` (1 phase); call `Extract`; assert the returned dictionary
   has key `"MissionPlan"` with a `JsonObject` containing `"PlanData"`, `"CurrentPhase"`,
   and `"PhaseElapsedSeconds"`.
2. **Inject test:** Call `Inject` with the DOM from test 1; assert entity has
   `ActiveMissionPlan` with `Plan.Tasks[0].BehaviorId == "FireAtTarget"` and a matching
   `MissionPlanQueue` with `Phases[0].BehaviorId` equal to the registry-resolved ID.
3. **CanTranslate false:** Entity with no `ActiveMissionPlan`; `CanTranslate` returns `false`.
4. **Round-trip test:** Serialize a world with a mission entity; deserialize into a fresh
   repo; assert `ActiveMissionPlan.Plan.Tasks.Count` and all `BehaviorId` strings match.

---

### Task 2: Register MissionPlanTranslator at All Sites (TASK-S202)

**Files to UPDATE:** `SimHostApp.cs`, `CgfSubsystem.cs`, `EditorBootstrap.cs`
**Task Definition:** See [TASK-DETAIL.md — TASK-S202](../TASK-DETAIL.md#task-s202-register-missionplantranslator-at-all-serializer-sites)

Each site already has a `BehaviorRegistry` instance; pass it to the constructor.

**Site 1 — `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`:**
The `ScenarioSerializerBuilder` already exists (after WeaponChannelTranslator removal from BATCH-01).
Add:
```csharp
var scenarioSerializer = new ScenarioSerializerBuilder(HrotSubsystemTypes.Scenario)
    .RegisterTranslator(new Hrot.SimHost.Serializers.MissionPlanTranslator(behaviorRegistry))
    .RegisterTranslator(new Hrot.SimHost.Serializers.TargetMemoryTranslator())
    .RegisterTranslator(new Hrot.SimHost.Serializers.PassengerBufferTranslator())
    .Build();
```
Find where `behaviorRegistry` is available in `SimHostApp.cs` — check for `CgfBehaviorSetup`
or the `BehaviorRegistry` construction pattern.

**Site 2 — `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`:**
The `behaviorRegistry` local variable exists before `ScenarioSerializerBuilder` is called.
Add the translator before `.Build()`.

**Site 3 — `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs`:**
Find where `CreateFileService()` builds the serializer. Obtain or construct a `BehaviorRegistry`
via `CgfBehaviorSetup.RegisterAll(...)` (same call pattern as CGF and SimHost). Pass it to
`MissionPlanTranslator`.

**Constraint:** Registration must occur BEFORE `.Build()` is called.

**Success check:** `dotnet build` succeeds for all 3 affected projects with no CS errors.

---

### Task 3: FdpAutoSerializer — fixed Buffer Expression Trees (TASK-S301)

**File:** `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs` (UPDATE)
**Task Definition:** See [TASK-DETAIL.md — TASK-S301](../TASK-DETAIL.md#task-s301-fdpautoserializer-fixed-buffer-expression-trees)
**Design Reference:** DESIGN.md § Phase 3 — Approach

**What to change in `GetSerializableFields`:**
Detect fields where `field.GetCustomAttribute<System.Runtime.CompilerServices.FixedBufferAttribute>() != null`.
For such fields, extract the `FixedBufferAttribute.ElementType` and `FixedBufferAttribute.Length`.

**What to change in `BuildExtract`:**
When a field has `FixedBufferAttribute`, do NOT call `SerializeFieldToNode` for it.
Instead, emit a loop expression that:
1. Pins the struct in memory (or uses `Unsafe.AsPointer` / `Unsafe.Add`)
2. Reads `Length` elements of `ElementType`
3. Writes them as a `JsonArray` of integers/floats

**What to change in `BuildInject`:**
Symmetric loop: read a `JsonArray` from the DOM, write each element back via `Unsafe.Add`.

**Critical safety check:**
Before emitting any loop, if `ElementType == typeof(Entity)` or the element type is a struct
containing an `Entity` field, `Build()` must throw `InvalidOperationException` with a message
naming the component type and field. Do NOT silently skip or emit raw integers for entity handles.

Supported element types: `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`,
`float`, `double`. If the element type is anything else (and not Entity), skip with a logged
warning (consistent with existing skip behavior).

**Implementation approach** (use `System.Runtime.CompilerServices.Unsafe.Add` for zero-allocation):
```csharp
// Pseudo-expression tree for Extract of fixed byte Data[4]:
// var arr = new JsonArray();
// for (int i = 0; i < 4; i++)
//     arr.Add(Unsafe.Add(ref comp.Data, i));
// json["Data"] = arr;
```

Use `Expression.Loop` + `Expression.Block` + a counter variable. The result JSON value
is a `JsonArray` of `JsonValue<ElementType>` instances.

**Required Tests (add to `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/` — new file
`FdpAutoSerializerFixedBufferTests.cs` or extend `AutoSerializerTests.cs` if it exists):**

1. Component with `fixed byte Data[4]` values `{1,2,3,4}`; auto-serialize; assert
   JSON has `"Data": [1,2,3,4]`.
2. Inject from `"Data": [5,6,7,8]`; assert the component field has those values.
3. Component with `fixed long EntityIds[2]`; call `Build()`; assert `InvalidOperationException`
   is thrown and the message contains the field name.
4. `BrainBlackboard` round-trip: create with non-zero `Memory` bytes; auto-serialize;
   inject; assert byte-for-byte identity. (Find `BrainBlackboard` in
   `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BrainComponents.cs`)

---

### Task 4: FdpAutoSerializer — InlineArray Expression Trees (TASK-S302)

**File:** `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs` (UPDATE, same file as Task 3)
**Task Definition:** See [TASK-DETAIL.md — TASK-S302](../TASK-DETAIL.md#task-s302-fdpautoserializer-inlinearray-expression-trees)
**Design Reference:** DESIGN.md § Phase 3 — Approach

**Detection:** In `GetSerializableFields`, check if `field.FieldType.GetCustomAttribute<System.Runtime.CompilerServices.InlineArrayAttribute>() != null`.
If yes, record this field as an "InlineArray field" with `InlineArrayAttribute.Length` and
element type (the type of the first field of the `[InlineArray]` struct).

**Serialization:** Use `System.Runtime.InteropServices.MemoryMarshal.CreateSpan<T>` to
create a `Span<T>` over the inline array, then serialize each element.

For element types that are structs with multiple fields (e.g., `MissionPhase`), serialize
each element as a `JsonObject` using the existing field-traversal logic recursively
(or call `SerializeFieldToNode<MissionPhase>(element)` if that helper handles structs).

**Same entity safety check:** If the element type is `Entity` or contains an `Entity`
field, throw `InvalidOperationException`.

**Required Tests:**

1. Struct with a `[InlineArray(3)]` of `float`; serialize; assert JSON array length is 3
   with correct values.
2. Inject from JSON array; assert all 3 values are restored.
3. `MissionPlanQueue` auto-serialization round-trip (WITHOUT `DataPolicy.NoSave`): create
   a queue with 2 phases; serialize; inject into a fresh component; assert all
   `BehaviorId`, `Trigger`, `CurrentPhase`, `PhaseElapsedSeconds` match.

---

## Mandatory Workflow: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests:**

1. **Task 1 (S201):** Create `MissionPlanTranslator.cs` → Write tests → **ALL tests pass.**
2. **Task 2 (S202):** Register at 3 sites → Build succeeds for all 3 projects → **ALL tests pass.**
3. **Task 3 (S301):** Fixed buffer expression trees → Write tests → **ALL tests pass.**
4. **Task 4 (S302):** InlineArray expression trees → Write tests → **ALL tests pass.**

**DO NOT** move to the next task until all tests pass.
**DO NOT** ask for permission to run tests, fix failures, or iterate. Work autonomously
until ALL tasks are done and ALL tests pass. Then write the report.

---

## Testing Requirements

- Minimum **12 new unit tests** across Phase 2 and Phase 3.
- Tests for Phase 2 must verify actual JSON DOM content and round-trip correctness.
- Tests for Phase 3 must verify actual byte-level values (not just "serialization succeeded").
- `FdpAutoSerializer` tests must check that `InvalidOperationException` is thrown for
  Entity-in-buffer — NOT just that it "does something".

### Test Quality Standard

**REQUIRED:** Tests asserting actual values in JSON (e.g., `Assert.Equal(5, array[0].GetValue<int>())`).
**NOT ACCEPTABLE:** Tests that only check `Assert.NotNull(result)` or `Assert.True(jsonHasKey)`.

---

## Success Criteria

- [ ] `MissionPlanTranslator` created with correct `Extract`/`Inject` (TASK-S201)
- [ ] Registered at `SimHostApp.cs`, `CgfSubsystem.cs`, `EditorBootstrap.cs` (TASK-S202)
- [ ] `FdpAutoSerializer` correctly serializes `fixed` buffers as `JsonArray` (TASK-S301)
- [ ] `FdpAutoSerializer` correctly serializes `[InlineArray]` as `JsonArray` (TASK-S302)
- [ ] `Build()` throws `InvalidOperationException` for Entity-in-fixed-buffer and Entity-in-InlineArray
- [ ] `BrainBlackboard` round-trip passes
- [ ] All new tests pass
- [ ] All existing tests pass (specifically the 403 SimHost tests and 746 Toolkits tests)

---

## Common Pitfalls

- The `IEntityScenarioTranslator` interface uses `Dictionary<string, object>` for DOM data
  in some older implementations, but newer ones may differ — confirm by reading
  `PassengerBufferTranslator` and the `IEntityScenarioTranslator` interface file carefully.
- `GetConsumedComponentsMask` must include `MissionPlanQueue` so the auto-serializer
  doesn't also try to auto-serialize it. Find how this works in `FdpAutoSerializer.Build()`.
- When detecting `FixedBufferAttribute`, note the compiler generates a nested struct type
  (e.g., `LocomotionChannel.<Params>e__FixedBuffer`) — the `FixedBufferAttribute` is on
  the FIELD, not on the nested type.
- For InlineArray, `MemoryMarshal.CreateSpan` requires `unsafe` context or the `ref` to
  the first element via `Unsafe.As<T, E>(ref field)`.
- In `BuildInject`, when writing back to a `fixed` buffer field inside an expression tree,
  you may need to use pinned pointers or `ref` locals — check if the existing `BuildInject`
  uses a by-value copy pattern (it does: `GetComponentCopy`) and plan accordingly.

---

## Developer Insights

**Q1:** What issues did you run into implementing the expression trees for fixed buffers?
How did you handle the pinning / Unsafe.Add pattern inside expression trees?

**Q2:** Was the `IEntityScenarioTranslator` interface exactly as you expected from reading
`PassengerBufferTranslator`? Were there surprises?

**Q3:** Did the `CgfBehaviorSetup` call in `EditorBootstrap.cs` require any non-obvious
wiring? Was the `BehaviorRegistry` already present or did you need to construct one?

**Q4:** What design decisions did you make beyond the spec?

**Q5:** Suggest a git commit message for this batch.
