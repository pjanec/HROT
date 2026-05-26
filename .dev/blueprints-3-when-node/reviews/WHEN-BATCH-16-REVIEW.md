# WHEN-BATCH-16 — Review

**Batch:** WHEN-BATCH-16  
**Tasks:** WHEN-M10-T1 through WHEN-M10-T6  
**Review Result:** APPROVED WITH P1 CORRECTIONS REQUIRED IN BATCH-17

---

## Review Summary

All 20 named success-criteria tests pass. No regressions (100 pre-existing failures
unchanged, 626 passing). The core defect fixes (T1 InstanceId determinism, T2 HasComponent
guard, T3 Vector epsilon, T4 BP2014 type check, T5 pin coverage) are correctly implemented
and well-tested. However, one **critical test quality issue** (T6) and one **pre-existing
omission** (BP2032 coverage attribute) must be fixed in BATCH-17 as corrective tasks.

---

## Findings

### P1 — `PreservesSensor` test always takes the conditional-skip path

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Integration/CoverAwarePatrolEndToEndTest.cs`

The `CoverAwarePatrol_HotReload_SoftReload_PreservesSensor` test includes a conditional
early-return path:
```csharp
if (childrenBefore.Count == 0)
{
    // ...skip assertions...
    return;
}
```
This path is **always taken** because the CoverAwarePatrol recipe json has an empty `Links`
array — no nodes are exec-connected. The `SpawnEqsSensorNode` exists as a node declaration
but is never reachable from `EventEntry`. After 5 ticks, zero child entities have been
spawned, so the sensor-preservation assertion (`IsAlive`, `HasComponent<EqsSensor>`, etc.)
is never executed.

The test therefore passes trivially without actually testing sensor preservation.

**Fix required in BATCH-17:**
Either (a) fix the CoverAwarePatrol recipe to wire its nodes properly (connect
`EventEntry → SpawnEqsSensorNode → WhenNode → ReadEqsResultNode → ChannelCommand`) so
the blueprint actually executes, OR (b) replace the recipe-based test with a minimal
purpose-built blueprint that unconditionally spawns a sensor on every tick, then asserts
sensor survival across a soft reload.

---

### P1 — `BP2032` diagnostic code lacks `[CoversDiagnosticCode]` coverage attribute

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage6_LoweringTests/SpawnEqsSensorLoweringTests.cs`

The `Validate_SpawnEqsSensor_InstanceIdCollision_BP2032` test does not carry
`[CoversDiagnosticCode("BP2032")]`, causing `V_AllValidatorsCoverageTests` to fail.
Additionally, the collision-path test (`..._CollisionPath`) is still skipped with the
comment "non-deterministic across .NET versions" — a reason that applied to `GetHashCode()`
but not to the now-deterministic `BlueprintIdHash.Compute()`.

**Fix required in BATCH-17:**
1. Add `[CoversDiagnosticCode("BP2032")]` attribute to the happy-path test (tests that
   BP2032 does NOT fire for distinct GUIDs — still exercises the code path and satisfies
   the coverage checker).
2. Either un-skip `..._CollisionPath` (now that `BlueprintIdHash.Compute` is deterministic,
   a test fixture can craft two Guid values that are known to produce the same FNV-1a hash
   by brute force in a small loop), or add a new `Validate_SpawnEqsSensor_BP2032_FiresOnCollision`
   test that uses two `SpawnEqsSensorNode` instances with the same `Id` Guid (identical
   nodes) to guarantee a collision.

---

## Implementation Quality Assessment

### M10-T1 (InstanceId determinism): CORRECT ✅

Both sites (Stage2_Validate.cs line 993 and Stage5_Schedule.cs line 693) now use
`(int)BlueprintIdHash.Compute(...)`. The validator and emitter use identical formulas.
The two new tests properly assert that the emitted literal matches the computed value.
New test `StableAcrossProcessRestart` uses two compile calls in the same process to
verify determinism — acceptable rationale documented.

### M10-T2 (HasComponent guard): CORRECT ✅

`InstanceEmitter.EmitReadEqsResultHelpers` emits the `HasComponent<EqsCognitiveBuffer>`
guard at line 427, immediately after `IsAlive` and before `GetComponentRO`. The `result`
local is already initialized to `default` before the guard chain. Tests verify via
source-text search that both guards appear in the generated code.

### M10-T3 (Vector epsilon): CORRECT ✅

`StatementEmitter.cs` now branches on `FieldCSharpType.Contains("Vector2")` /
`Contains("Vector3")` and emits `LengthSquared() > (eps * eps)` for vectors. The existing
`Vector2_EmitsLengthSquaredComparison` test was updated to use `epsilon > 0`. The scalar
regression test verifies `MathF.Abs` is still emitted for float fields.

One observation: `TryResolveFieldCSharpType` in Stage5 performs `AppDomain` assembly scan
on every call. This is correct-by-behavior (works in test + production when types are
loaded) but slow. Tracked as P2 debt.

### M10-T4 (BP2014 type check): CORRECT ✅

BP2014 now fires only when the resolved type is NOT float/double/Vector2/Vector3. If
type resolution fails (component type not loaded), BP2014 is suppressed. Tests correctly
assert no-fire for float/double/Vector2 and confirm fire for int/bool fields.

### M10-T5 (SpawnEqsSensor pin-binding): CORRECT ✅

Investigation confirmed both branches (wired pin → upstream expression; unconnected pin →
literal default) exist in Stage5. Tests `Lower_WiredPin_EmitsUpstreamExpression` and
`Lower_UnconnectedPin_EmitsLiteralDefault` verify the generated code shape. Runtime tests
assert actual component field values. `Spawn_ZeroAllocation` correctly placed in
`Benchmarks/WhenNodePerfTests.cs`.

### M10-T6 (CoverAwarePatrol PreservesSensor): PARTIALLY DONE ⚠️

The test `CoverAwarePatrol_HotReload_SoftReload_PreservesSensor` exists and passes, but
always takes the conditional skip path (see P1 finding above). The test body for the
assert path (when sensor is spawned) is correctly written — it asserts `IsAlive`,
`HasComponent<EqsSensor>`, and `HasComponent<EqsCognitiveBuffer>`. The bug is in the
recipe data (empty Links), not in the test logic.

---

## Debt Tracker Updates

| # | Priority | Description | Target |
|---|----------|-------------|--------|
| new | P2 | `TryResolveFieldCSharpType` (Stage5) and `TryResolvePropertyType` (Stage2) are duplicated AppDomain-scan helpers without caching. Consolidate into shared utility with `ConcurrentDictionary` cache. | BATCH-17+ |
| new | P2 | 99 pre-existing test failures from `BlueprintDispatchKind` JSON deserialization mismatch (numeric vs. string enum values in sample JSON files). Investigate and fix to unblock integration snapshots. | Backlog |

---

## Suggested Git Commit Message

```
fix(blueprints/M10): library defects -- InstanceId, HasComponent guard, vector epsilon, BP2014 type-check, pin-binding tests, sensor-hot-reload test

M10-T1: Replace non-deterministic Guid.GetHashCode() with BlueprintIdHash.Compute() in
Stage2_Validate (BP2032 collision check) and Stage5_Schedule (baked InstanceId literal).
Both validator and emitter now agree on the FNV-1a hash formula.

M10-T2: Emit HasComponent<EqsCognitiveBuffer> guard in ReadEqsResult helper
(InstanceEmitter) before GetComponentRO; prevents fatal ECS exception when ECB playback
for buffer attachment is still pending on the same tick as spawn.

M10-T3: Add Vector2/Vector3 branch in StatementEmitter IrOp_WhenValueChangedCheck for
non-zero epsilon -- emits LengthSquared() > (eps * eps) instead of MathF.Abs which is
a C# compile error on vector operands. Stage5 resolves FieldCSharpType via AppDomain
reflection to feed the branch.

M10-T4: Narrow BP2014 emission in Stage2_Validate to fire only when the resolved
property type is not float/double/Vector2/Vector3; suppress for unknown types.

M10-T5: Add Lower_WiredPin/UnconnectedPin lowering tests and three runtime tests
(LiteralParameters, WiredParameters, ZeroAllocation) for SpawnEqsSensorNode pin binding.

M10-T6: Add CoverAwarePatrol_HotReload_SoftReload_PreservesSensor test; fix pending
in next batch (recipe Links array is empty -- sensor never spawns).

Tests: +20 new tests; 626 passing, 100 pre-existing failures unchanged.
```

---

## Task Tracker Updates

- [x] WHEN-M10-T1 ✅ Complete
- [x] WHEN-M10-T2 ✅ Complete
- [x] WHEN-M10-T3 ✅ Complete
- [x] WHEN-M10-T4 ✅ Complete
- [x] WHEN-M10-T5 ✅ Complete
- [⚠️] WHEN-M10-T6 Partially complete — `PreservesSensor` test exists but takes skip path; corrective in BATCH-17
