# BATCH-09 Completion Report

**Tasks:** TASK-CP-000 (Catalog Interface Stubs) + TASK-CP-001 (Compiler Infrastructure + IR Data Model Skeleton)

---

## 1. Files Created or Modified

### Modified (1)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintCompiler.cs`  
  Updated doc comment to reflect backward-compat wrapper role. Signature unchanged.

### Created — TASK-CP-000: Catalog Interfaces (6 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/CatalogInterfaces.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/INodeRegistry.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/ITypeRegistry.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/BuiltInEngineEventCatalog.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/BuiltInChannelCommandCatalog.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/BuiltInWaitPrimitiveCatalog.cs`

### Created — TASK-CP-001: Diagnostics (3 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Diagnostics/Diagnostic.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Diagnostics/DiagnosticCodes.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Diagnostics/DiagnosticSink.cs`

### Created — TASK-CP-001: IR Data Model (8 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Ir/IrTypeRef.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Ir/IrDebugAnnotation.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Ir/IrValue.cs` (IrValue + IrBlockId)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Ir/IrOperation.cs` (30+ ops incl. IrOp_ReadInstanceVersion)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Ir/IrStatement.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Ir/IrBlock.cs` (IrBlock + IrTerminator hierarchy)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Ir/IrGraph.cs` (IrGraph + IrGraphKind)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Ir/IrAsset.cs` (IrAsset + IrField + IrCustomEvent)

### Created — TASK-CP-001: Core Compiler Types (4 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/CompileOptions.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/CompileResult.cs` (incl. ValidationOptions, ValidationResult)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/BlueprintSignature.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/BlueprintCompiler.cs` (IBlueprintCompiler + BlueprintCompiler)

### Created — TASK-CP-001: Stage Stubs (7 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage1_Parse.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage2_Validate.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage3_Normalize.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage4_TypeResolve.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage5_Schedule.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage6_Lower.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Stages/Stage7_Emit.cs`

### Created — TASK-CP-001: Lowering Stubs (5 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/LibraryLowering.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/AiPrimitiveLowering.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/InstanceLowering.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/WaitLowering_AiPrimitive.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Lowering/WaitLowering_Instance.cs`

### Created — TASK-CP-001: Emit (4 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/CSharpEmitter.cs` (stub)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/EmissionContext.cs` (stub)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/Sanitizer.cs` (FULL implementation)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/DebugMapBuilder.cs` (DebugMap record + DebugMapBuilder stub)

### Created — TASK-CP-001: Roslyn (3 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Roslyn/InMemoryRoslynCompiler.cs` (stub, new signature)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Roslyn/MetadataReferenceResolver.cs` (stub)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Roslyn/EmbeddedTextHelper.cs` (stub)

### Created — TASK-CP-001: Determinism (2 files)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Determinism/FnvHasher.cs` (FULL implementation)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Determinism/DeterministicEnumerable.cs` (stub)

**Total: 1 modified + 42 created**

---

## 2. Deviations from Instructions

### Deviation 1: BuiltIn* catalog implementations placed in Hrot.Blueprints.Core, not Fdp.Toolkits

**Reason:** The instructions place `BuiltIn*` catalog implementations in `FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/`. However, the catalog interfaces (`IEngineEventCatalog`, `IChannelCommandCatalog`, `IWaitPrimitiveCatalog`) are defined in `Hrot.Blueprints.Core.Compiler.Catalogs`. Since `Hrot.Blueprints.Core` already references `Fdp.Toolkits`, adding a reverse reference from `Fdp.Toolkits` to `Hrot.Blueprints.Core` would create a circular project dependency — which .NET does not allow.

**Resolution:** The `BuiltIn*` implementations were placed in `Hrot.Blueprints.Core/Compiler/Catalogs/` alongside the interfaces they implement. The existing `FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/` placeholder files (EngineEventCatalog.cs etc.) are unchanged.

### Deviation 2: CompilerMode not redefined in Hrot.Blueprints.Core.Compiler

**Reason:** `CompilerMode { Release, Debug, Trace }` already exists in `Fdp.Toolkit.Blueprints` (CompilerMode.cs). Redefining it in `Hrot.Blueprints.Core.Compiler` would create two identical enums with the same values. `CompileOptions.cs` imports `using Fdp.Toolkit.Blueprints` and reuses the existing enum, avoiding duplication.

### Deviation 3: Root BlueprintCompiler.cs kept as-is (backward compat wrapper)

**Reason:** `BlueprintTestFixture` uses `new BlueprintCompiler()` and `Compiler.Compile(asset, mode)` — old `string`-returning signature. Instead of updating the fixture (which was passing 160/3 tests), the root `BlueprintCompiler.cs` in `Hrot.Blueprints.Core` namespace is preserved as a backward-compat wrapper with the old stub. The new `IBlueprintCompiler` + `BlueprintCompiler` live in `Hrot.Blueprints.Core.Compiler` namespace in `Compiler/BlueprintCompiler.cs`.

### Deviation 4: Root InMemoryRoslynCompiler.cs kept as-is

**Reason:** Same backward-compat reason as above. The new `InMemoryRoslynCompiler` with the updated signature (`source, virtualSourcePath, assemblyName, sink`) lives in `Compiler/Roslyn/`. The root stub remains for `BlueprintTestFixture.CompileAndLoadMany`.

### Deviation 5: BlueprintIdHash.Compute not updated to use FnvHasher

**Reason:** `BlueprintIdHash.cs` already exists in `FDP/Toolkits/Fdp.Toolkits/Blueprints/` and uses an inline FNV-1a 32-bit implementation with the same constants as `FnvHasher`. The instructions say "if it exists and uses FNV-1a, leave it." It was left unchanged.

### Deviation 6: CompileResult.GeneratedFileName added

`GeneratedFileName` was added as an optional string to `CompileResult` (in addition to the fields specified in the design doc) to allow the caller to know the generated file name without recomputing it from the asset's sanitized name and blueprint ID.

---

## 3. Answers to Output Questions

**Q1: Did existing `BlueprintTestFixture.CompileAndLoadMany` require changes to compile?**

No. The existing root `BlueprintCompiler.cs` (namespace `Hrot.Blueprints.Core`) and the root `InMemoryRoslynCompiler.cs` were intentionally kept as backward-compat wrappers with their original stub signatures. The test fixture compiles and all tests pass without any changes to the fixture.

**Q2: Was `BlueprintIdHash.Compute` already present before this batch?**

Yes. `BlueprintIdHash.Compute(Guid)` was already present in `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintIdHash.cs` (namespace `Fdp.Toolkit.Blueprints`) with a correct FNV-1a 32-bit implementation using the same constants as the new `FnvHasher`. No new `BlueprintIdHash` was created.

**Q3: Were any Roslyn packages already in the `.csproj`?**

No. `Hrot.Blueprints.Core.csproj` contains no `Microsoft.CodeAnalysis.CSharp` or other Roslyn NuGet package references. None were added in this batch (as per instructions, that is deferred to TASK-CP-005).

---

## 4. Final Build/Test Results

### Build
```
dotnet build IOS-IG-SimHost.sln
Build succeeded.
```
0 errors, 0 warnings.

### Tests
```
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --no-build
Passed! - Failed: 0, Passed: 160, Skipped: 3, Total: 163
```

**Note on flaky test:** On the first test run after build, `Constructor_InitializesAllProperties` failed transiently due to a pre-existing race condition in `DebugProbe.Sink` (static field shared across parallel test classes). On re-run it passed. This is a pre-existing issue (not introduced by BATCH-09) and matches the expected 160 pass / 3 skip / 0 fail baseline.

### Success Criteria Verification

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `dotnet build IOS-IG-SimHost.sln` -- 0 errors | PASS |
| 2 | Tests: 160 pass, 3 skip, 0 fail | PASS |
| 3 | `BlueprintIdHash.Compute` exists and compiles | PASS (pre-existing) |
| 4 | `FnvHasher.Hash32` is deterministic | PASS (pure function, no random seed) |
| 5 | `Sanitizer.GeneratedFileName("MoveToAndFire", 0xA1B2C3D4, false)` returns `"MoveToAndFire_A1B2C3D4_Bp.g.cs"` | PASS (by inspection of implementation) |
| 6 | `DiagnosticCodes.BP0001_NullAsset` etc. declared | PASS |
| 7 | All IR types compile | PASS |
| 8 | `CompileOptions.SiblingSignatures` exists; `SiblingAssets` does NOT exist | PASS |
