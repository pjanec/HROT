# WHEN-BATCH-16 — Corrective: library defects (M10)

**Batch Number:** WHEN-BATCH-16  
**Tasks:** WHEN-M10-T1, WHEN-M10-T2, WHEN-M10-T3, WHEN-M10-T4, WHEN-M10-T5, WHEN-M10-T6  
**Phase:** M10 — Corrective: library defects  
**Estimated Effort:** 12-16 hours  
**Priority:** HIGH  
**Dependencies:** WHEN-BATCH-15 (M9 completed)

---

## Onboarding

### Required Reading (IN ORDER)

1. **Task Details (M10):** `.dev/blueprints-3-when-node/TASK-DETAIL.md` — Phase M10
   section, tasks WHEN-M10-T1 through WHEN-M10-T6. Read every task fully; each has
   non-negotiable constraints and explicit success conditions.
2. **Design:** `.dev/blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md`
   — §6.1, §6.2, §7.1, §4.1, §15.1, §15.3, §15.9 (the sections referenced by M10 tasks).
3. **Debt Tracker:** `.dev/blueprints-3-when-node/DEBT-TRACKER.md`

### Primary Work Areas

| Task | File(s) to change |
|------|-------------------|
| M10-T1 | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs` and `Stage5_Schedule.cs` |
| M10-T2 | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/InstanceEmitter.cs` |
| M10-T3 | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs` |
| M10-T4 | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs` |
| M10-T5 | Investigation only + tests in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage6_LoweringTests/SpawnEqsSensorLoweringTests.cs` and `Runtime/SpawnEqsSensorRuntimeTests.cs` |
| M10-T6 | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Integration/CoverAwarePatrolEndToEndTest.cs` |

**Test project:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj`  
**Build command:** `dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -c Debug`  
**Test command:** `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -c Debug --no-build`

---

## Task Details

### M10-T1 — Deterministic `PartMetadata.InstanceId` via `BlueprintIdHash.Compute()`

**Full task spec:** TASK-DETAIL.md `WHEN-M10-T1`

**IMPORTANT DISCREPANCY:** The task detail says "StatementEmitter.cs: replace
`int bakedInstanceId = ssn.Id.GetHashCode()`" — but that code is actually in
`Stage5_Schedule.cs` line ~692, not in StatementEmitter. StatementEmitter just emits
the pre-computed `op.BakedInstanceId` literal. Fix the real site.

**Two sites to change:**

1. `Stage2_Validate.cs` — find `V_SpawnEqsSensorNodeRules`, the BP2032 collision check:
   ```csharp
   // BEFORE (line ~956):
   .GroupBy(x => x.Node.Id.GetHashCode())
   // AFTER:
   .GroupBy(x => (int)BlueprintIdHash.Compute(x.Node.Id))
   ```

2. `Stage5_Schedule.cs` — the spawn lowering case:
   ```csharp
   // BEFORE (line ~692):
   int bakedInstanceId = ssn.Id.GetHashCode();
   // AFTER:
   int bakedInstanceId = (int)BlueprintIdHash.Compute(ssn.Id);
   ```

   `BlueprintIdHash` is already imported in Stage5_Schedule.cs (used for the template
   ID on the next line). In Stage2_Validate.cs, check if it's already imported;
   if not, add `using Hrot.Blueprints.Core.Compiler;` (or wherever BlueprintIdHash lives —
   check the usings in Stage5_Schedule.cs for the correct namespace).

**New tests to add in `SpawnEqsSensorLoweringTests.cs`:**

1. `Lower_PartMetadataInstanceId_StableAcrossProcessRestart` — invoke the lowering
   pipeline twice in the same test process (two separate `Compile()` calls with the
   same `SpawnEqsSensorNode.Id`); assert both produce the same `InstanceId` literal.
   (The two-call approach tests stability within a single process, which is what we
   can verify in a unit test context. The real determinism across processes is
   guaranteed by FNV-1a which is deterministic by definition — document this rationale
   in the test comment.)

2. `Lower_PartMetadataInstanceId_MatchesValidatorComputation` — for the same
   `SpawnEqsSensorNode.Id`, compute `(int)BlueprintIdHash.Compute(nodeId)` directly in
   the test; compile the asset; parse the emitted `InstanceId = <literal>` from the
   generated source; assert `literal == directComputation`.

The existing `Lower_PartMetadataInstanceId_IsDeterministicAndNonZero` test (already in
SpawnEqsSensorLoweringTests.cs) should continue to pass.

---

### M10-T2 — `HasComponent<EqsCognitiveBuffer>` guard in `ReadEqsResult` helper

**Full task spec:** TASK-DETAIL.md `WHEN-M10-T2`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/InstanceEmitter.cs`

**Method:** `EmitReadEqsResultHelpers` (around line 390)

**Current code** (after `IsAlive` check):
```csharp
e.WriteLine($"ref readonly var buffer = ref view.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
```

**Change:** Insert the `HasComponent` guard BEFORE the `GetComponentRO` call:
```csharp
e.WriteLine($"if (!view.HasComponent<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId))");
e.Indent();
e.WriteLine("return result;");
e.Outdent();
e.WriteLine();
e.WriteLine($"ref readonly var buffer = ref view.GetComponentRO<global::Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>(handle.ChildId);");
```

The `result` local is already initialized to `default(...)` before this guard chain (verify
this is the case — if not, hoist the default initialization to the top of the helper body).

**New tests to add:**

1. **In `ReadEqsResultLoweringTests.cs`:**

   `Lower_LivenessGuardFails_ReturnsSafeDefault` — build an asset with a
   `ReadEqsResultNode`; compile it; extract the generated helper source; assert it
   contains both `IsAlive` check AND `HasComponent<...EqsCognitiveBuffer>` check (text
   search the generated source for the relevant substrings).

   `Lower_BufferComponentMissing_ReturnsSafeDefault` — same generated-source check:
   assert the `HasComponent` guard appears BEFORE the `GetComponentRO` call in the
   emitted text.

2. **In `ReadEqsResultNodeRuntimeTests.cs`:**

   `ReadEqs_ImmediatelyAfterSpawn_NoCrash` — build a Blueprint graph that on its Tick
   branch:
   - Calls `SpawnEqsSensorNode` (creates child via ECB, result stored to variable)
   - Immediately calls `ReadEqsResultNode` reading from that variable
   
   Use `BlueprintTestFixture`; tick ONE frame only. Because ECB playback for the
   `AddComponent<EqsCognitiveBuffer>` occurs inside `TickFrame` (post-playback),
   on the very first tick when both nodes fire in the same frame, the `IsAlive` check
   will be true but `HasComponent` will be false (ECB not yet played back).
   Assert: no exception thrown, `IsReady == false` from the read result.

   **Note:** Achieving the "ECB not yet played back" window requires understanding how
   `BlueprintTestFixture.TickFrame` works. Specifically, the `SpawnEqsSensorNode`
   runs during Tick, the ECB is played back at the end of TickFrame. So within the
   single tick execution (before playback), the child does not yet have
   `EqsCognitiveBuffer`. If the fixture plays back synchronously, both `IsAlive` and
   `HasComponent` will be true immediately, negating the test value. Read
   `BlueprintTestFixture.cs` and `MockEntityCommandBuffer.cs` to understand the order.
   If the child doesn't yet exist at all (ECB not played back), `IsAlive` itself will
   return false — which also satisfies "no crash, IsReady == false". Either scenario
   passing without crash is acceptable for this test.

---

### M10-T3 — Vector-aware epsilon comparison in Value Changed lowering

**Full task spec:** TASK-DETAIL.md `WHEN-M10-T3`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs`

**Location:** `IrOp_WhenValueChangedCheck` case (around line 306). Current code for
the non-zero-epsilon path:
```csharp
e.WriteLine($"bool __t{idx}_changed = global::System.MathF.Abs(__t{idx}_cur - {sv}.{op.SynthFieldName}) > {op.Epsilon}f;");
```

**Replace with a type-branching emission:**
```csharp
bool isVector2 = op.FieldCSharpType.Contains("Vector2");
bool isVector3 = op.FieldCSharpType.Contains("Vector3");
if (isVector2 || isVector3)
{
    e.WriteLine($"bool __t{idx}_changed = " +
        $"(__t{idx}_cur - {sv}.{op.SynthFieldName}).LengthSquared() > " +
        $"({op.Epsilon}f * {op.Epsilon}f);");
}
else
{
    e.WriteLine($"bool __t{idx}_changed = " +
        $"global::System.MathF.Abs(__t{idx}_cur - {sv}.{op.SynthFieldName}) > " +
        $"{op.Epsilon}f;");
}
```

Use `"Vector2"` and `"Vector3"` substring checks (NOT just `"Vector"` to avoid matching
`Vector256` etc.).

**New tests to add** (in
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage6_LoweringTests/WhenNodeLoweringTests.cs`):

**IMPORTANT:** The existing `Lower_ValueChanged_Vector2_EmitsLengthSquaredComparison`
test uses `epsilon=0` and actually tests the direct-equality path only (the comment in
the test says so). Update that test to use `epsilon > 0` so it actually exercises the
vector branch. Verify the test asserts `LengthSquared` appears in the output.

New tests:

1. `Lower_ValueChanged_Vector3_EmitsLengthSquaredComparison` — same pattern as the
   Vector2 test but with `ComponentTypeId` resolved to a type that has a `Vector3` field.

2. `Compile_ValueChanged_OnVector2Field_ProducesValidCSharp` — use
   `Hrot.Blueprints.Core.Compiler.Stages.Stage7_Emit` to emit a full C# source for an
   asset whose WhenNode targets a Vector2 field (epsilon > 0); verify the source string
   does NOT contain `MathF.Abs(` combined with a Vector2 operand pattern.
   Note: For a full Roslyn compile check, you can use `Microsoft.CodeAnalysis.CSharp`
   if it's already referenced in the test project, otherwise assert the generated
   source contains `LengthSquared` and does not contain `MathF.Abs` near the
   prev-field name.

3. `Lower_ValueChanged_ScalarPath_UnchangedAfterVectorBranchAdded` — regression:
   `float` field with `epsilon > 0` still emits `MathF.Abs` and does not emit
   `LengthSquared`.

**Mock type for Vector2 testing:** Add a new test component to
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockTestTypes.cs`:
```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(255)]
public struct VectorTestComponent
{
    public System.Numerics.Vector2 Position2D;
    public System.Numerics.Vector3 Position3D;
}
```
Also register it in `MockTestComponents.Register`. Use
`"Hrot.Blueprints.Tests.Mocks.VectorTestComponent"` as the `ComponentTypeId` and
`"Position2D"` / `"Position3D"` as property paths in the vector tests.

For the `FieldCSharpType` to be set correctly, check how `Stage4_TypeResolve.cs` sets
`IrOp_WhenValueChangedCheck.FieldCSharpType` for the resolved property. If type
resolution relies on `StaticTypeRegistry`, the new `VectorTestComponent` type must be
registered. Check `StaticTypeRegistry.cs` for how types are registered.

---

### M10-T4 — `BP2014` epsilon warning must check resolved property type

**Full task spec:** TASK-DETAIL.md `WHEN-M10-T4`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs`

**Current code** (around line 747-752):
```csharp
// BP2014 -- epsilon on non-float field (warning, best-effort)
if (vc.Epsilon != 0)
    ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP2014, ...));
```

**Required change:** Only emit BP2014 when the resolved property type is NOT a float,
double, Vector2, or Vector3. Use `ctx.TypeRegistry` to resolve the type.

Pattern (adapt to actual Stage2 API):
```csharp
// BP2014 -- epsilon on non-float field (warning, best-effort)
if (vc.Epsilon != 0 && vc.Source != ValueChangedSource.PeerBlueprintVariable)
{
    // Attempt type resolution; suppress if we can't resolve (BP2003 already fired).
    var resolvedType = TryResolvePropertyType(ctx, vc.ComponentTypeId, vc.PropertyPath);
    bool isFloatingPoint = resolvedType == typeof(float)
        || resolvedType == typeof(double)
        || resolvedType == typeof(System.Numerics.Vector2)
        || resolvedType == typeof(System.Numerics.Vector3);
    if (resolvedType != null && !isFloatingPoint)
        ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP2014, ...));
}
```

Look at how BP2003/BP2009 do type resolution via `ctx.TypeRegistry` — use the same
helper or pattern. If type resolution at Stage 2 is not feasible, defer to Stage 4 and
add a `// deferred to Stage 4` comment in Stage 2 (see TASK-DETAIL.md for guidance).

**Tests to update and add** in
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/ValidationTests/WhenNodeValidatorTests.cs`
(or wherever `Validate_EpsilonNonZero_ValueChanged_BP2014Warning` lives):

1. **Update** `Validate_EpsilonNonZero_ValueChanged_BP2014Warning` — ensure the test
   fixture uses an `int` or `bool` field (not float) as the observed property. The test
   must continue to pass (BP2014 fires for non-float field).

2. **Add** `Validate_EpsilonNonZero_OnFloatField_NoBP2014` — same setup but property
   path resolves to `float`; assert BP2014 is NOT in the diagnostics.

3. **Add** `Validate_EpsilonNonZero_OnDoubleField_NoBP2014` — same for `double`.

4. **Add** `Validate_EpsilonNonZero_OnVector2Field_NoBP2014` — same for `Vector2`
   (depends on M10-T3 landing first; the mock `VectorTestComponent.Position2D` from
   M10-T3 can be reused here).

---

### M10-T5 — `SpawnEqsSensorNode` pin-binding test coverage

**Full task spec:** TASK-DETAIL.md `WHEN-M10-T5`

**Investigation step:** Read `Stage5_Schedule.cs` around line 700-730 (the
`SpawnEqsSensorNode` case). Look at the `ResolveParamPin` helper. It returns `null` for
unconnected pins (which causes the emitter to use literal defaults) and returns an
`IrValue` for connected pins (which causes the emitter to emit the upstream expression).
Both branches ARE implemented.

Since both branches exist, add the following tests:

**In `SpawnEqsSensorLoweringTests.cs`:**

1. `Lower_WiredPin_EmitsUpstreamExpression` — build a graph where a source node
   provides a float value wired to the `SearchRadius` pin; compile; assert the generated
   source does NOT contain `SearchRadius = 0f` but instead contains a reference to the
   upstream expression (e.g., a `__t<N>` register).

2. `Lower_UnconnectedPin_EmitsLiteralDefault` — build a graph with no wired pins;
   compile; assert the generated source contains `SearchRadius    = 0f,`.

**In `SpawnEqsSensorRuntimeTests.cs`:**

3. `Spawn_LiteralParameters_AppliedCorrectly` — compile + run a Blueprint where
   SearchRadius pin is unconnected; after tick, read the child entity's `EqsSensor`
   component; assert `SearchRadius == 0f`.

4. `Spawn_WiredParameters_ReadFromExpression` — compile + run a Blueprint where
   SearchRadius is wired from a constant-value variable (use a float variable set to
   `5.0f`); after tick, assert the child's `EqsSensor.SearchRadius == 5.0f`.

5. `Spawn_ZeroAllocation` — host in
   `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Benchmarks/WhenNodePerfTests.cs`.
   Using the existing perf test fixture pattern, compile a SpawnEqsSensor Blueprint;
   tick once to spawn the entity (allocation expected); tick again; measure allocation
   on the second tick. Assert second-tick allocation is zero (spawning is one-time;
   after the sensor exists the spawn branch is not re-entered in the same frame).

---

### M10-T6 — Strengthen `CoverAwarePatrol_HotReload_SoftReload_*` to assert sensor preservation

**Full task spec:** TASK-DETAIL.md `WHEN-M10-T6`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Integration/CoverAwarePatrolEndToEndTest.cs`

**Current state:** The test `CoverAwarePatrol_HotReload_SoftReload_PreservesStructure`
only asserts structure hash equality — it does NOT assert child sensor survival.

**Change:** Either rename the existing test and add sensor-preservation assertions, OR
keep `PreservesStructure` and add a NEW `PreservesSensor` test. Both options are
acceptable; the new test `CoverAwarePatrol_HotReload_SoftReload_PreservesSensor` MUST
exist and pass.

**Required flow for `PreservesSensor`:**
1. Load and compile the CoverAwarePatrol recipe; register EQS components.
2. Create parent entity, attach blueprint, tick once (this spawns the sensor child).
3. Query all entities with `PartMetadata` — assert at least one exists.
4. Capture the child entity handles: `var childBefore = childEntities.First();`
5. Perform a Soft Reload (reload the identical asset JSON — same structure, triggers
   same-hash soft reload path in `CompileAndLoad`).
6. Tick several frames.
7. Assert `fixture.World.IsAlive(childBefore)` — child entity survived the soft reload.
8. Assert the child still has `EqsSensor` component: call
   `fixture.World.HasComponent<EqsSensor>(childBefore)`.
9. Assert the child still has `EqsCognitiveBuffer`:
   `fixture.World.HasComponent<EqsCognitiveBuffer>(childBefore)`.

**Note:** For the Soft Reload to not destroy the child, `CompileAndLoad` must detect
same StructureHash and skip re-spawning. Verify the blueprint runtime actually preserves
instance state on soft reload (it should per the design). If the child IS destroyed
during a soft reload (hard reload scenario), investigate: the structural change might
be causing a hard reload even though the JSON is identical. Check
`AiHotReloadCoordinator` logic for the same-hash case.

---

## Mandatory Workflow

### Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **M10-T1:** Fix InstanceId formula → add new tests → **ALL tests pass** ✅
2. **M10-T2:** Add HasComponent guard → add new tests → **ALL tests pass** ✅
3. **M10-T3:** Add Vector epsilon branch → update/add tests → **ALL tests pass** ✅
4. **M10-T4:** Fix BP2014 type check → update/add tests → **ALL tests pass** ✅
5. **M10-T5:** Investigate pin-binding, add tests → **ALL tests pass** ✅
6. **M10-T6:** Add PreservesSensor test → **ALL tests pass** ✅

**DO NOT** move to the next task until the current task's tests are green and the full
test suite passes. Run `dotnet test ... --no-build` after each task to verify.

**DO NOT stop to ask permission** for obvious things like running tests and fixing
compilation errors. Work autonomously until all tasks are done, then write the report.

---

## Quality Standards

**Test Quality:** Every new test must assert behavioral correctness:
- T1 tests: assert the actual emitted literal value matches `BlueprintIdHash.Compute`
- T2 tests: assert no exception AND the correct return values (IsReady/ResultCount)
- T3 tests: assert the specific generated code shape (LengthSquared vs MathF.Abs)
- T4 tests: assert BP2014 FIRES for int/bool and does NOT FIRE for float/double/Vector2
- T5 tests: assert actual component field values after runtime ticks (not just "compiles")
- T6 test: assert `IsAlive` AND component presence AFTER soft reload

Tests that only verify "it compiled" or "it didn't throw" are not sufficient when
the spec calls for value/behavior assertions.

---

## Success Criteria

- [ ] `Lower_PartMetadataInstanceId_IsDeterministicAndNonZero` still passes
- [ ] `Lower_PartMetadataInstanceId_StableAcrossProcessRestart` new test passes
- [ ] `Lower_PartMetadataInstanceId_MatchesValidatorComputation` new test passes
- [ ] `Lower_LivenessGuardFails_ReturnsSafeDefault` new test passes (source check)
- [ ] `Lower_BufferComponentMissing_ReturnsSafeDefault` new test passes (source check)
- [ ] `ReadEqs_ImmediatelyAfterSpawn_NoCrash` new runtime test passes
- [ ] `Lower_ValueChanged_Vector2_EmitsLengthSquaredComparison` updated and passes
- [ ] `Lower_ValueChanged_Vector3_EmitsLengthSquaredComparison` new test passes
- [ ] `Compile_ValueChanged_OnVector2Field_ProducesValidCSharp` new test passes
- [ ] `Lower_ValueChanged_ScalarPath_UnchangedAfterVectorBranchAdded` new test passes
- [ ] `Validate_EpsilonNonZero_ValueChanged_BP2014Warning` still passes (int/bool case)
- [ ] `Validate_EpsilonNonZero_OnFloatField_NoBP2014` new test passes
- [ ] `Validate_EpsilonNonZero_OnDoubleField_NoBP2014` new test passes
- [ ] `Validate_EpsilonNonZero_OnVector2Field_NoBP2014` new test passes
- [ ] `Lower_WiredPin_EmitsUpstreamExpression` new test passes
- [ ] `Lower_UnconnectedPin_EmitsLiteralDefault` new test passes
- [ ] `Spawn_LiteralParameters_AppliedCorrectly` new runtime test passes
- [ ] `Spawn_WiredParameters_ReadFromExpression` new runtime test passes
- [ ] `Spawn_ZeroAllocation` new benchmark test passes
- [ ] `CoverAwarePatrol_HotReload_SoftReload_PreservesSensor` new test passes
- [ ] Full test suite `dotnet test` passes with 0 failures

---

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** For M10-T3: was `FieldCSharpType` already set to the full type name (e.g.,
`"System.Numerics.Vector2"`) after Stage 4 type-resolve, or did you need to change how
it's populated? Describe what you found.

**Q3:** For M10-T4: was type resolution at Stage 2 feasible, or did you have to defer
BP2014 to Stage 4? What lookup mechanism did you use?

**Q4:** For M10-T5 `Spawn_ZeroAllocation`: what was the actual allocation profile?
Was the second tick zero-alloc as expected, or did you find unexpected allocations?

**Q5:** For M10-T6 `PreservesSensor`: did the soft reload actually preserve the child
entity, or did it destroy and re-create it? What did you find in the coordinator code?

**Q6:** Did you spot any other weak points in the codebase that should be tracked in the
DEBT-TRACKER? What would you improve if you could?

---

## Report Submission

When done, write your report to:  
`.dev/blueprints-3-when-node/reports/WHEN-BATCH-16-REPORT.md`

If you have questions, create:  
`.dev/blueprints-3-when-node/questions/WHEN-BATCH-16-QUESTIONS.md`
