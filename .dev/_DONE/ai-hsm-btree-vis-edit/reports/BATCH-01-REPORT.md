# BATCH-01 Report

## Tasks Completed

- [x] TASK-BB-K-01
- [x] TASK-BB-K-02
- [x] TASK-BB-K-03
- [x] TASK-BB-K-04

---

## Files Changed / Created

### Modified

- `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/BTreeDefinitionAttribute.cs`
  - Added `bool BlackboardManaged { get; set; }` (default `false`)
  - Added `Type? HeavyDtoType { get; set; }` (default `null`)

- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmDefinitionAttribute.cs`
  - Added `bool BlackboardManaged { get; set; }` (default `false`)
  - Added `Type? HeavyDtoType { get; set; }` (default `null`)

### Created

- `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BlackboardAnnotations.cs`
  - `BlackboardDtoStructAttribute` (`AttributeTargets.Struct`, `AllowMultiple = false`)
  - `BlackboardReadOnlyAttribute` (`AttributeTargets.Parameter`, `AllowMultiple = false`)
  - `BlackboardReadWriteAttribute` (`AttributeTargets.Parameter`, `AllowMultiple = false`)
  - Namespace: `Fbt.Kernel` (co-located with `SharedAiAttributes.cs` per instructions)

- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/BlackboardAttributeTests.cs`
  - 11 tests covering K-01 (BTree), K-02 (BTree), K-03, K-04

- `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Kernel/HsmDefinitionAttributeTests.cs`
  - 7 tests covering K-01 (HSM) and K-02 (HSM), plus regression guards for MachineName/AssetId

---

## Test Results

### New tests - all green

```
Fbt.Tests.Unit.BlackboardAttributeTests (11 tests)
  Passed: BTreeDefinitionAttribute_BlackboardManaged_DefaultsFalse
  Passed: BTreeDefinitionAttribute_BlackboardManaged_RoundTripsTrue
  Passed: BTreeDefinitionAttribute_HeavyDtoType_DefaultsNull
  Passed: BTreeDefinitionAttribute_HeavyDtoType_CanBeSet
  Passed: BTreeDefinitionAttribute_HeavyDtoType_NullMeansNoHeavyComponent
  Passed: BlackboardDtoStructAttribute_DecoratedStruct_IsDiscoverable
  Passed: BlackboardDtoStructAttribute_UndecoratedStruct_IsNotDiscovered
  Passed: BlackboardDtoStructAttribute_CanBeReadBackFromDecoratedStruct
  Passed: BlackboardReadOnlyAttribute_IsReadableViaParameterInfo
  Passed: BlackboardReadWriteAttribute_IsReadableViaParameterInfo
  Passed: UnannotatedParameter_HasNeitherAttribute

Fhsm.Tests.Kernel.HsmDefinitionAttributeTests (7 tests)
  Passed: HsmDefinitionAttribute_BlackboardManaged_DefaultsFalse
  Passed: HsmDefinitionAttribute_BlackboardManaged_RoundTripsTrue
  Passed: HsmDefinitionAttribute_HeavyDtoType_DefaultsNull
  Passed: HsmDefinitionAttribute_HeavyDtoType_CanBeSet
  Passed: HsmDefinitionAttribute_HeavyDtoType_NullMeansNoHeavyComponent
  Passed: HsmDefinitionAttribute_MachineNameIsPreserved
  Passed: HsmDefinitionAttribute_AssetIdDefaultsNull
```

### Pre-existing failures (not introduced by this batch)

```
Fbt.Tests (9 pre-existing failures, all source-generator related):
  AutoDiscoveryTests.ScanAndRegister_FindsBothActionAndCondition
  AutoDiscoveryTests.ScanAndRegister_RegisteredAction_IsCallable
  AutoDiscoveryTests.ScanAndRegister_FindsGeneratedRegistrar_InTestAssembly
  SharedAiGeneratorTests.SharedAiCondition_CompoundKey_IsCallable_ReturnsExpectedStatus
  SharedAiGeneratorTests.SharedAiCondition_SequentialOffset_RegisteredUnderCompoundKey
  SharedAiGeneratorTests.GroupAnchorAction_RegisteredUnderMethodName
  SharedAiGeneratorTests.SharedAiAction_ExplicitOffset_RegisteredUnderCompoundKey
  GeneratorOutputTests.GeneratedRegistrar_RegisterAll_PopulatesRegistry
  BuilderValidationTests.DtoTooLarge_ThrowsBehaviorTreeBuildException

Fhsm.Tests (2 pre-existing failures, unrelated to attributes):
  OrthogonalRegionTests.OutputLane_Conflict_Detected
  FailSafeTests.InfiniteLoop_Detected_And_Stops
```

Build: `dotnet build IOS-IG-SimHost.sln` -- succeeded, 0 errors, 9 pre-existing warnings (all in
Hrot.Blueprints.Tests, unrelated to this batch).

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

No blocking issues. The main decision point was where to place the new K-03/K-04 attributes: the design
document mentions a `Fbt.Annotations` assembly but no such assembly exists in the committed codebase.
Following the BATCH-01-INSTRUCTIONS override ("alongside SharedAiAttributes.cs in Fbt.Kernel"), I
placed all three new attributes in a new file `BlackboardAnnotations.cs` in `Fbt.Kernel` under the
`Fbt.Kernel` namespace. This keeps them co-located with the existing shared attribute patterns.

For the K-03 reflection test, I used private nested structs inside the test class as fixtures.
`Assembly.GetTypes()` in .NET returns all types including private nested types, so
`DecoratedDtoStruct` (with `[BlackboardDtoStruct]`) is discoverable while `UndecoratedDtoStruct`
(without the attribute) is correctly excluded.

**Q2: Did you spot any weak points, inconsistencies, or improvement opportunities?**

1. `BTreeDefinitionAttribute` uses `///` XML doc comments while `HsmDefinitionAttribute` uses `//`
   plain comments. The style inconsistency is pre-existing. I matched each file's existing style.

2. The design-era name `Fbt.Annotations` / `Fhsm.Annotations` (TASK-DETAIL.md K-04) has no
   corresponding assembly in the codebase. This is noted in ONBOARDING.md §0 as a known divergence.
   A future P3 item could be extracting these annotation attributes into a proper thin
   `Fbt.Annotations` assembly so that user code can reference only annotations without pulling in
   the entire kernel.

3. `HsmDefinitionAttribute` does not have `[AttributeUsage]` specifying `AllowMultiple`. Looking at
   the declaration it has `AllowMultiple = false` as the default, which is correct, but it would be
   cleaner to make this explicit (mirrors `BTreeDefinitionAttribute`). Out of scope for this batch.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- Grouped all three K-03/K-04 attributes in a single file `BlackboardAnnotations.cs` rather than
  three separate files. Rationale: they are all "blackboard editor annotation" attributes with no
  behavior; keeping them together reduces file count and makes the pattern easy to discover.
  Alternative considered: one file per attribute (as `BTreeActionAttribute.cs` etc. do), but that
  felt excessive for three zero-body marker attributes.

- Added two regression-guard tests in `HsmDefinitionAttributeTests`: `MachineNameIsPreserved` and
  `AssetIdDefaultsNull`. These confirm that adding two new properties did not accidentally break
  the existing constructor or `AssetId` default. The spec did not require these but they cost nothing
  and increase confidence.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- The `BlackboardDtoStructAttribute` reflection test (`Assembly.GetTypes()`) depends on the test
  fixture struct being compiled into the same assembly as the test class. If the fixture were in a
  separate assembly, the test would need to reference that assembly. The current approach of using
  a private nested struct in the test class ensures both the attribute and the fixture travel in the
  same assembly, making the test self-contained and reliable.

- `Type?` on `HeavyDtoType` in both attributes requires `Nullable` to be enabled in the project.
  Both `Fbt.Kernel.csproj` and `Fhsm.Kernel.csproj` already have `<Nullable>enable</Nullable>`,
  so this was not an issue but worth noting for future kernel additions.

**Q5: Suggested git commit message for this batch?**

```
feat(kernel): add Phase 0 blackboard authoring attribute prerequisites (BATCH-01)

- BTreeDefinitionAttribute: add BlackboardManaged (bool, default false) and
  HeavyDtoType (Type?, default null) opt-in properties
- HsmDefinitionAttribute: same two properties with identical semantics
- Fbt.Kernel/BlackboardAnnotations.cs: new BlackboardDtoStructAttribute
  (AttributeTargets.Struct), BlackboardReadOnlyAttribute and
  BlackboardReadWriteAttribute (AttributeTargets.Parameter)
- Tests: 11 new tests in Fbt.Tests + 7 new tests in Fhsm.Tests
- All defaults preserve existing behavior; runtime ignores new attributes

No behavioral changes. Unblocks Phase 1.5a schema exporter work.
```
