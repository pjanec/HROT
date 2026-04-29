# BATCH-03 Report

## Summary

All three tasks completed successfully. 15 new tests added; 0 failures (123 total passing).

---

## Tasks Completed

- [x] FBT-006: BuilderValidationTests (Phase 1 validation test suite)
- [x] FBT-010: Marker attributes (`[BTreeAction]`, `[BTreeCondition]`, `[BTreeDefinition]`, `[FbtRegistrar]`)
- [x] FBT-007: BTreeSchemaExporter + BTreeSchema

---

## Test Results

**Total passing: 123 / 123**
(108 pre-existing + 15 new)

### New test files

| File | Tests | Task |
|------|-------|------|
| `tests/Fbt.Tests/Unit/BuilderValidationTests.cs` | 7 | FBT-006 |
| `tests/Fbt.Tests/Unit/AttributeTests.cs` | 3 | FBT-010 |
| `tests/Fbt.Tests/Unit/BTreeSchemaExporterTests.cs` | 5 | FBT-007 |

### New test names

**BuilderValidationTests (7):**
- `NestedRepeater_ThrowsBehaviorTreeBuildException`
- `NestedParallel_ThrowsBehaviorTreeBuildException`
- `DtoTooLarge_ThrowsBehaviorTreeBuildException`
- `ValidTree_DoesNotThrow`
- `EmptyBuilder_Compile_ThrowsInvalidOperationException`
- `TwoRootNodes_Compile_ThrowsInvalidOperationException`
- `Condition_ReturnsFailure_ActionNotCalled`

**AttributeTests (3):**
- `BTreeActionAttribute_CanBeAppliedToMethod`
- `BTreeDefinitionAttribute_ExposesTreeName`
- `FbtRegistrarAttribute_CanBeAppliedToClass`

**BTreeSchemaExporterTests (5):**
- `Export_FindsAllMarkedMethods_InTestAssembly`
- `Export_FieldOffset_IsNegativeOne_ForAllMethods`
- `ExportToJson_ProducesValidJson_ThatRoundTrips`
- `ExportToJson_EmptyAssembly_DoesNotThrow`
- `Export_WithUnmarkedAssembly_DoesNotThrow`

---

## New Files Created

| File | Purpose |
|------|---------|
| `src/Fbt.Kernel/Attributes/BTreeActionAttribute.cs` | `[BTreeAction]` marker (FBT-010) |
| `src/Fbt.Kernel/Attributes/BTreeConditionAttribute.cs` | `[BTreeCondition]` marker (FBT-010) |
| `src/Fbt.Kernel/Attributes/BTreeDefinitionAttribute.cs` | `[BTreeDefinition(treeName)]` marker (FBT-010) |
| `src/Fbt.Kernel/Attributes/FbtRegistrarAttribute.cs` | `[FbtRegistrar]` class marker (FBT-010) |
| `src/Fbt.Compiler/BTreeSchema.cs` | `ActionDescriptor`, `ConditionDescriptor`, `BTreeSchema` records (FBT-007) |
| `src/Fbt.Compiler/BTreeSchemaExporter.cs` | `BTreeSchemaExporter` static class (FBT-007) |
| `tests/Fbt.Tests/Unit/BuilderValidationTests.cs` | Negative-path validation tests (FBT-006) |
| `tests/Fbt.Tests/Unit/AttributeTests.cs` | Attribute reflection tests (FBT-010) |
| `tests/Fbt.Tests/Unit/BTreeSchemaExporterTests.cs` | Schema exporter tests (FBT-007) |

### Modified files

| File | Change |
|------|--------|
| `src/Fbt.Compiler/BTreeBuilder.cs` | Added `MaxBlackboardByteSize = 128` constant; added DTO-too-large guard to `Condition<TValue>` and `Action<TValue>` overloads |

---

## Developer Insights

**Q1: Issues encountered and how resolved?**

- The `FieldOffset` spec in TASK-DETAIL.md said verify `== 4` for a reusable delegate, but BATCH-03-INSTRUCTIONS.md's "Important" note mandates `FieldOffset = -1` for all scanned methods (runtime reflection cannot determine which struct field a `ref TValue` came from without additional metadata). The batch instructions take precedence; the test was written to assert `-1` and the test name updated accordingly (`IsNegativeOne_ForAllMethods`).
- `Marshal.SizeOf<TBlackboard>()` is used in the DTO-too-large guard. This will throw a `MarshalDirectiveException` for non-blittable blackboard structs. Since expression-binding blackboards must be `[StructLayout(LayoutKind.Sequential)]` to work with `Marshal.OffsetOf` (already required in the existing code), this is an acceptable constraint.

**Q2: Design decisions made beyond the spec?**

- `MaxBlackboardByteSize = 128` added as a `private const` directly in `BTreeBuilder<TBlackboard, TContext>` rather than in a new `Fbt.Kernel/BehaviorConstants.cs` file. The constant is local to where it is enforced, keeping `Fbt.Kernel` free of application-level policy. A public API can be added in Phase 2 if the source generator needs to share the constant.
- `BTreeSchemaExporter` uses `BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic` so it finds both static and instance methods (user code may annotate either).
- `BTreeSchema` uses C# `record` with positional parameters; `System.Text.Json` serialises these cleanly via the primary constructor.

**Q3: Weak points observed?**

- The schema exporter's `FieldOffset = -1` simplification means authoring tools cannot use the schema alone to reconstruct projections -- they must also invoke the source generator. This is documented in the `BTreeSchemaExporter` XML comment.
- `GetTypes()` inside `Export` catches all exceptions for the outer assembly loop, but not at the per-type or per-method level. Types with bad attributes or generic methods without type arguments could still surface exceptions. The current behaviour (catch at assembly level) matches the spec's "skip non-reflectable assemblies" intent.

**Q4: Edge cases discovered?**

- `TwoRootNodes_Compile_ThrowsInvalidOperationException` confirms that the root-count guard in `BTreeBuilder.Compile()` triggers with a clear message when the user chains two top-level fluent calls.
- `DtoTooLarge_ThrowsBehaviorTreeBuildException` requires `LargeBlackboard` to be blittable for `Marshal.SizeOf` to succeed. The 33-int struct (132 bytes) satisfies that requirement.

---

**Suggested commit message:**
```
feat(fluent-btree): BATCH-03 complete -- validation tests, marker attributes, schema exporter

FBT-006: BuilderValidationTests -- 7 negative-path and control tests
  - NestedRepeater, NestedParallel, DtoTooLarge, ValidTree, EmptyBuilder,
    TwoRootNodes, Condition_ReturnsFailure_ActionNotCalled
  - BTreeBuilder: add MaxBlackboardByteSize=128 guard to Condition<TValue>/Action<TValue>

FBT-010: Marker attributes in Fbt.Kernel/Attributes/
  - BTreeActionAttribute, BTreeConditionAttribute (AttributeTargets.Method)
  - BTreeDefinitionAttribute(string treeName) (AttributeTargets.Method)
  - FbtRegistrarAttribute (AttributeTargets.Class)
  - 3 attribute reflection tests

FBT-007: BTreeSchemaExporter + BTreeSchema in Fbt.Compiler
  - BTreeSchema: ActionDescriptor/ConditionDescriptor/BTreeSchema records
  - BTreeSchemaExporter.Export(assemblies): scans for [BTreeAction]/[BTreeCondition]
  - BTreeSchemaExporter.ExportToJson: System.Text.Json serialisation
  - FieldOffset=-1 for all (Roslyn generator resolves real offsets at compile time)
  - 5 schema exporter tests

All 123 tests pass (108 pre-existing + 15 new). 0 warnings.
```
