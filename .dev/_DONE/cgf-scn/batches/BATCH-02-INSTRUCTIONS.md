# BATCH-02: EntityCreationRequest Extension, Behavior Remapping Infrastructure, Debt Fix

**Batch Number:** BATCH-02
**Tasks:** TASK-C013, TASK-C005 (a–d), DEBT-D002
**Phase:** Phase 2 — Staging Entity Extractor (infrastructure sub-tasks)
**Estimated Effort:** 8-10 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (must be complete — `ScenarioEntityCreationRequestSource` and `CompositeEntityCreationRequestSource` exist)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch delivers all the "infrastructure" components needed before the main
`StagingEntityExtractor` (TASK-C004, next batch) can be built.  There are three
independent work streams:

1. **DEBT-D002** — Fix 3 stale system-count assertions in `Hrot.SimHost.Tests`.
2. **TASK-C013** — Extend `EntityCreationRequest` and `CreateEntityRequestSystem`
   to support pre-allocated network IDs and child component overrides.
3. **TASK-C005** — Add behavior param remapping infrastructure:
   a. `RemapNetworkIdAttribute`
   b. `FireAtTargetParamsJsonDto`, `FollowRouteParamsJsonDto`, `MoveToLocationParamsJsonDto`
   c. `BehaviorParamRemapperCompiler`
   d. `ScenarioBehaviorRemapper`

### Required Reading (IN ORDER)

1. **Design:** `.dev/cgf-scn/DESIGN.md` — Decisions 5, 6, 7 (root-entity extraction,
   two-pass ID remapping, DTO-based JSON remapping).
2. **Task Details:** `.dev/cgf-scn/TASK-DETAIL.md` — sections TASK-C013 and TASK-C005
   (Phase 2).
3. **Previous Review:** `.dev/cgf-scn/reviews/BATCH-01-REVIEW.md` — understand
   what was completed in the last batch.

### Existing Files You Must Read Before Coding

| File | What to understand |
|------|-------------------|
| `Hrot/Engine/Hrot.Core/Network/EntityLifecycleInterfaces.cs` | Current `EntityCreationRequest` DTO structure — you are adding two new `init`-only properties |
| `Hrot/Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs` | `ProcessIncomingRequest` (uses `AllocateId()`) and `ProcessPendingRequest` (spawns children) — you modify both |
| `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs` | The 3 stale system-count tests (also `CgfLogicPack_EmptyWorld`, `SimHostCoreLogicPack_EmptyWorld`, `SimulationLogicModule_EmptyWorld`) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/` | Location for the new attribute and compiler classes |
| `FDP/Toolkits/Fdp.Toolkits.Tests/` | Where behavior remapping tests go |

### Source Code Location

- **Modified files:**
  - `Hrot/Engine/Hrot.Core/Network/EntityLifecycleInterfaces.cs` (add 2 properties to `EntityCreationRequest`)
  - `Hrot/Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs` (use pre-alloc ID + child overrides)
  - `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs` (fix 3 stale assertions — DEBT-D002)
- **New files:**
  - `FDP/Toolkits/Fdp.Toolkits/Behavior/Attributes/RemapNetworkIdAttribute.cs`
  - `FDP/Toolkits/Fdp.Toolkits/Behavior/Params/FireAtTargetParamsJsonDto.cs`
  - `FDP/Toolkits/Fdp.Toolkits/Behavior/Params/FollowRouteParamsJsonDto.cs`
  - `FDP/Toolkits/Fdp.Toolkits/Behavior/Params/MoveToLocationParamsJsonDto.cs`
  - `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorParamRemapperCompiler.cs`
  - `FDP/Toolkits/Fdp.Toolkits/Behavior/ScenarioBehaviorRemapper.cs`
- **Test files:**
  - `Hrot/Subsystems/Hrot.CGF.Tests/` or `Hrot/Subsystems/Hrot.SimHost.Tests/`: tests for C013
  - `FDP/Toolkits/Fdp.Toolkits.Tests/`: tests for C005c and C005d

### Build Commands

```powershell
# From repo root d:\Work\IOS-IG-SimHost-FDP-2

# Build full solution
dotnet build IOS-IG-SimHost.sln

# Run CGF/SimHost tests (includes C013 tests + fixed D002 tests)
dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj

# Run FDP toolkit tests (includes C005 tests)
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj

# Run Hrot.Core tests (should still be green)
dotnet test Hrot\Engine\Hrot.Core.Tests\Hrot.Core.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/cgf-scn/reports/BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev/cgf-scn/questions/BATCH-02-QUESTIONS.md`

---

## Context

Phase 2 builds the machinery that `StagingEntityExtractor` (next batch) will consume:
- `EntityCreationRequest` needs `PreAllocatedNetworkId` so ID remapping can bypass the allocator
- `CreateEntityRequestSystem` needs to use `ChildComponentOverrides` to merge child authored state
- Behavior param DTOs with `[RemapNetworkId]` attributes provide the schema for JSON remapping
- The remapper compiler and registry wire those together

**Related Tasks:**
- [TASK-C013](../TASK-DETAIL.md#task-c013--entitycreationrequest-extension-and-createentityrequestsystem-genesis-gateway) — DTO extension + genesis gateway
- [TASK-C005](../TASK-DETAIL.md#task-c005--behavior-param-remapping-infrastructure) — Behavior remapping

---

## 🎯 Batch Objectives

- Fix 3 stale system-count test assertions (DEBT-D002)
- Add `PreAllocatedNetworkId` and `ChildComponentOverrides` to `EntityCreationRequest`
- Modify `CreateEntityRequestSystem` to use pre-allocated IDs and child overrides
- Implement `RemapNetworkIdAttribute`
- Implement 3 behavior param DTOs
- Implement `BehaviorParamRemapperCompiler` with expression-tree compiled delegates
- Implement `ScenarioBehaviorRemapper`
- All tests passing

---

## ✅ Tasks

### Task 0: Fix Stale System-Count Tests (DEBT-D002)

**File:** `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs` (MODIFY)

Update the hardcoded expected system counts in the following 3 tests to match
the current actual counts (which changed after BATCH-01 added `CreateEntityRequestSystem`
usage of the new composite source):

- `CgfLogicPack_EmptyWorld`
- `SimHostCoreLogicPack_EmptyWorld`
- `SimulationLogicModule_EmptyWorld`

**How:** Run the test, read the actual count from the failure message, update
the assertion.  Do NOT simply delete the assertion; update it to the correct value.

### Task 1: EntityCreationRequest DTO Extension (TASK-C013, part 1)

**File:** `Hrot/Engine/Hrot.Core/Network/EntityLifecycleInterfaces.cs` (MODIFY)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c013--entitycreationrequest-extension-and-createentityrequestsystem-genesis-gateway)

Add two `init`-only properties to `EntityCreationRequest`:
```csharp
public long PreAllocatedNetworkId { get; init; } = 0;
public IReadOnlyDictionary<int, (long PreAllocatedId, IReadOnlyList<object> Components)>? ChildComponentOverrides { get; init; } = null;
```

All existing construction sites use object initializers; new properties default to `0`/`null`
so no existing code breaks.

### Task 2: CreateEntityRequestSystem Gateway (TASK-C013, part 2)

**File:** `Hrot/Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs` (MODIFY)
**Task Definition:** Same as above.

Modify `ProcessIncomingRequest`:
- If `request.PreAllocatedNetworkId != 0` → use that value directly as network ID
  (skip `_idAllocator.AllocateId()`)
- If `request.PreAllocatedNetworkId == 0` → call `_idAllocator.AllocateId()` as before

Modify `ProcessPendingRequest` (in the child loop):
- After determining the child's `InstanceId`, check `request.ChildComponentOverrides`
- If the dictionary is non-null and contains an entry for that `InstanceId`:
  - Use `entry.PreAllocatedId` for the child's `SpawnEntityCommand.NetworkId` if non-zero
    (fall through to `AllocateId()` if `entry.PreAllocatedId == 0`)
  - Use `AddRange` to merge `entry.Components` into the child's `InitialComponents` list
- If the dictionary is null, or no entry for this `InstanceId` → use `AllocateId()` as before

**Tests Required** (in `Hrot.SimHost.Tests` or `Hrot.CGF.Tests`):
See TASK-DETAIL.md success conditions 1–6 for TASK-C013.  All 6 must be tested:
1. Normal request — `AllocateId()` still called
2. Pre-allocated ID bypasses `AllocateId()`
3. Child uses pre-allocated ID and overrides merged
4. `PreAllocatedId = 0` in override entry falls through to `AllocateId()`
5. Null `ChildComponentOverrides` — `AllocateId()` called for each child
6. Key not present for a child in `ChildComponentOverrides` — `AllocateId()` called

### Task 3: RemapNetworkIdAttribute (TASK-C005a)

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Attributes/RemapNetworkIdAttribute.cs` (NEW FILE)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c005--behavior-param-remapping-infrastructure) — section C005a.

Attribute class:
- `[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]`
- No properties; marker attribute only
- Namespace: consistent with existing toolkit attribute namespace (check the directory)
- No test required for the attribute itself (exercised by C005c tests)

### Task 4: Behavior Param DTOs (TASK-C005b)

**Files:** (all NEW in `FDP/Toolkits/Fdp.Toolkits/Behavior/Params/`)
- `FireAtTargetParamsJsonDto.cs`
- `FollowRouteParamsJsonDto.cs`
- `MoveToLocationParamsJsonDto.cs`

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c005--behavior-param-remapping-infrastructure) — section C005b.

Key properties per DTO (JSON key ↔ C# property via `[JsonPropertyName(...)]` or camelCase serializer):
- `FireAtTargetParamsJsonDto`: `TargetNetworkId` (long, `[RemapNetworkId]`), `MaxRounds` (int), `CooldownSeconds` (float)
- `FollowRouteParamsJsonDto`: `RouteEntityId` (long, `[RemapNetworkId]`)
- `MoveToLocationParamsJsonDto`: `TargetLat` (double), `TargetLon` (double), `Speed` (double), `ArrivalRadius` (double)

Use `[JsonPropertyName("camelCaseName")]` attributes on each property to match the
JSON keys already produced by `MissionPanel.BuildXxxParams` helpers:
`targetNetworkId`, `maxRounds`, `cooldownSeconds`, `routeEntityId`, `targetLat`, `targetLon`, `speed`, `arrivalRadius`.

`System.Text.Json` with `PropertyNameCaseInsensitive = true` must round-trip correctly.

No tests required for DTOs alone (exercised by C005c tests).

### Task 5: BehaviorParamRemapperCompiler (TASK-C005c)

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorParamRemapperCompiler.cs` (NEW FILE)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c005--behavior-param-remapping-infrastructure) — section C005c.

Key design requirements:
- Generic static method `Compile<TDto>()` returns a cached delegate
  `Func<string?, Dictionary<long, long>, string?>`
- Reflection (`GetProperties`, `GetCustomAttributes`) done once at compile time, NOT
  per invocation
- Uses `System.Linq.Expressions` to build a lambda that:
  - Deserializes JSON to `TDto`
  - Remaps `long` properties tagged `[RemapNetworkId]` via `TryGetValue` on the map
  - For `int` properties tagged `[RemapNetworkId]`: narrow-cast the new `long` to `int`
  - Re-serializes to JSON
- If no `[RemapNetworkId]` properties → return an identity delegate `(json, _) => json`
- Null/empty JSON → return input unchanged
- Caching: delegate is built once per `TDto` type and reused

**Tests Required** (in `FDP/Toolkits/Fdp.Toolkits.Tests/`):
See TASK-DETAIL.md success conditions 1–6 for C005c.

### Task 6: ScenarioBehaviorRemapper (TASK-C005d)

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/ScenarioBehaviorRemapper.cs` (NEW FILE)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c005--behavior-param-remapping-infrastructure) — section C005d.

Key design:
- `Register<TDto>(string behaviorId)` — calls `BehaviorParamRemapperCompiler.Compile<TDto>()`,
  stores in a `Dictionary<string, Func<string?, Dictionary<long,long>, string?>>`
  Throws `InvalidOperationException` on duplicate registration
- `RemapJson(string behaviorId, string? json, Dictionary<long,long> idMap)` —
  looks up and invokes the delegate; if `behaviorId` not registered, returns `json` unchanged

**Tests Required** (in `FDP/Toolkits/Fdp.Toolkits.Tests/`):
See TASK-DETAIL.md success conditions 1–3 for C005d.

---

## 🧪 Testing Requirements

- Minimum 6 tests for TASK-C013 (covering all 6 success conditions)
- Minimum 6 tests for TASK-C005c (covering all 6 success conditions)
- Minimum 3 tests for TASK-C005d (covering all 3 success conditions)
- All tests assert values/behavior — not just "no exception"
- No `Thread.Sleep` in tests; use proper deterministic patterns

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 0 (DEBT-D002):** Fix stale assertions → `dotnet test Hrot.SimHost.Tests` → **ALL pass** ✅
2. **Task 1+2 (C013):** Extend DTO + modify system → Write tests → **ALL pass** ✅
3. **Task 3+4 (C005a+b):** Add attribute + DTOs (no tests needed yet) ✅
4. **Task 5 (C005c):** Implement compiler → Write tests → **ALL pass** ✅
5. **Task 6 (C005d):** Implement remapper → Write tests → **ALL pass** ✅
6. **Final:** `dotnet build IOS-IG-SimHost.sln` → **0 errors** ✅

**Do NOT stop to ask for permission to run tests, fix errors, or proceed to the
next task.  Fix all failures at the root cause.  Write the report only after
everything passes.**

---

## 📊 Report Requirements

Submit `.dev/cgf-scn/reports/BATCH-02-REPORT.md` with:

### 1. Completion Summary
Files created/modified with one-line descriptions.

### 2. Test Results
Final `dotnet test` output for all three test projects.

### 3. Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Were there any surprises in the `CreateEntityRequestSystem` code when
wiring in child component overrides?

**Q3:** What design decisions did you make beyond the instructions?

**Q4:** What edge cases did you discover that weren't in the spec?

**Q5:** Any concerns about the expression-tree compilation approach for the
behavior param remapper?

**Q6:** Suggested git commit message.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] DEBT-D002: 3 stale system-count assertions fixed; `Hrot.SimHost.Tests` all pass
- [ ] TASK-C013: `EntityCreationRequest` has `PreAllocatedNetworkId` and `ChildComponentOverrides`
- [ ] TASK-C013: `CreateEntityRequestSystem` uses pre-allocated IDs and child overrides
- [ ] TASK-C013: 6 unit tests pass
- [ ] TASK-C005a: `RemapNetworkIdAttribute` created
- [ ] TASK-C005b: 3 DTO classes created with correct JSON property names
- [ ] TASK-C005c: `BehaviorParamRemapperCompiler` with cached expression-tree delegates; 6 tests pass
- [ ] TASK-C005d: `ScenarioBehaviorRemapper` with registration + remap; 3 tests pass
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors
- [ ] Report submitted to `.dev/cgf-scn/reports/BATCH-02-REPORT.md`

---

## ⚠️ Common Pitfalls to Avoid

- Do NOT use `PropertyInfo.GetValue`/`SetValue` inside the compiled delegate —
  reflection must happen only inside `Compile<TDto>()`, not in the returned lambda.
- Do NOT forget `[JsonPropertyName("...")]` on DTO properties — the JSON must match
  what `MissionPanel.BuildXxxParams` produces (camelCase keys).
- Do NOT call `AllocateId()` when `PreAllocatedNetworkId != 0` — the pre-allocated
  ID is the definitive network ID for that entity.
- Do NOT modify `NedEntityCreationRequestSource` or `NetworkSpawningSystem`.
- Do NOT modify `ReferenceEpisodeLoadHandler` in FDP toolkit.
- The `ChildComponentOverrides` dictionary merge uses `AddRange` on the existing
  `childComponents` list — do NOT replace the list.

---

## 📚 Reference Materials

- **Task Details:** `.dev/cgf-scn/TASK-DETAIL.md` — TASK-C013, TASK-C005
- **Design:** `.dev/cgf-scn/DESIGN.md` — Decisions 5, 6, 7
- **EntityCreationRequest:** `Hrot/Engine/Hrot.Core/Network/EntityLifecycleInterfaces.cs`
- **CreateEntityRequestSystem:** `Hrot/Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs`
- **FDP toolkit behavior dir:** `FDP/Toolkits/Fdp.Toolkits/Behavior/`
- **FDP toolkit tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/`
- **Previous review:** `.dev/cgf-scn/reviews/BATCH-01-REVIEW.md`
