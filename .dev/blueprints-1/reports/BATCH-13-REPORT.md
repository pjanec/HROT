# BATCH-13 Report — TASK-CP-005: Stage 8 Roslyn + Incremental Generator + Catalogs

## Status: APPROVED

**Test result:** 188 pass, 3 skip, 0 fail (baseline was 182 pass, 3 skip, 0 fail; +6 new Stage8 tests)

---

## Files Created

| File | Purpose |
|------|---------|
| `Hrot.Blueprints.Core/Compiler/Roslyn/BlueprintCompileException.cs` | Exception thrown when in-memory Roslyn compilation fails; carries `IReadOnlyList<Diagnostics.Diagnostic>` |
| `Hrot.Blueprints.Core/Compiler/Stages/Stage8_RoslynFinalize.cs` | Stage 8 pipeline step; delegates to `InMemoryRoslynCompiler.Compile` |
| `Hrot.Blueprints.Core/Compiler/BlueprintSignatureParser.cs` | Lightweight JSON parser for blueprint signatures used by the incremental generator |
| `Hrot.Blueprints.Core/Compiler/Emit/DebugMapSerializer.cs` | Deterministic JSON serialization of `DebugMap`; sorts entries by GraphId then StartLine |
| `Hrot.Blueprints.Tests/Stage8Tests.cs` | 6 Stage8 tests (SC1-SC6); all pass |

---

## Files Modified

| File | Changes |
|------|---------|
| `Hrot.Blueprints.Core/Hrot.Blueprints.Core.csproj` | Added `Microsoft.CodeAnalysis.CSharp` 4.8.0 package reference |
| `Hrot.Blueprints.Core/Compiler/Roslyn/InMemoryRoslynCompiler.cs` | Full implementation: `Compile()` -> `(byte[] Pe, byte[] Pdb)` with embedded PDB source; `CompileAndLoad()` -> `(Assembly, AssemblyLoadContext)` |
| `Hrot.Blueprints.Core/Compiler/Roslyn/MetadataReferenceResolver.cs` | Implemented as `public sealed class`; `ForRuntimeAssemblies()` with both predicates (`!IsDynamic && !string.IsNullOrEmpty(Location)`) per Patch 2 |
| `Hrot.Blueprints.Core/Compiler/Roslyn/EmbeddedTextHelper.cs` | Implemented `Create(string, string)` using `SourceText.From` + `EmbeddedText.FromSource` |
| `Hrot.Blueprints.Core/Compiler/Diagnostics/Diagnostic.cs` | Added `public bool IsError => Severity == DiagnosticSeverity.Error;` property |
| `Hrot.Blueprints.Core/Compiler/BlueprintCompiler.cs` | Wired Stage 8 when `EmitPdbWithEmbeddedSource = true`; sets `PortablePe` and `PortablePdb` on success result |
| `Hrot.Blueprints.Core/Compiler/Emit/DebugMapBuilder.cs` | Added `AssetId`, `BlueprintId`, `StructureHash` fields to `DebugMap` record and new constructor to `DebugMapBuilder` |
| `Hrot.Blueprints.Core/Compiler/Emit/CSharpEmitter.cs` | Updated `DebugMapBuilder` constructor call; fixed `BlueprintRegistrar` attribute to correct namespace `Fdp.Toolkit.Blueprints.Attributes` |
| `Hrot.Blueprints.Core/Compiler/Emit/TerminatorEmitter.cs` | Fixed `IrTerm_ReturnStatus` emitter: changed `global::Fdp.Toolkit.Blueprints.NodeStatus` to `global::Hrot.Blueprints.Core.Assets.NodeStatus` |
| `Hrot.Blueprints.Core/Compiler/Emit/LibraryEmitter.cs` | Added NodeStatus return type detection for Library functions using `IrTerm_ReturnStatus` |
| `Hrot.Blueprints.Generators/Hrot.Blueprints.Generators.csproj` | Added `System.Collections.Immutable` 8.0.0; added Core project reference with `SkipGetTargetFrameworkProperties`, `PrivateAssets="all"`, `ExcludeAssets="build;buildTransitive;analyzers"`; added `AssetTargetFallback` for net8.0 |
| `Hrot.Blueprints.Generators/BlueprintIncrementalGenerator.cs` | Full 4-provider incremental generator pipeline (Patch 1): raw files -> signatures -> sibling catalog -> compile results |
| `Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj` | Added `Microsoft.CodeAnalysis.CSharp` 4.8.0 package reference (needed by SC4 test) |
| `Hrot.Blueprints.Tests/Stage8Tests.cs` | Added `using Fdp.Toolkit.Blueprints;` and disambiguation aliases for `RoslynCompileException` and `AssetDispatchKind` |

---

## Output Questions

**Q1: PortablePe.Length range for a simple Library asset**
Approximately 2,000–4,000 bytes (2–4 KB) for a single-function Library with one Entry/Return graph. The PE contains the IL for the blueprint class and its registrar. Actual size is deterministic per asset.

**Q2: Was `BlueprintJsonServices.TryDeserialize` present?**
No. `BlueprintJsonServices.TryDeserialize` does not exist in the codebase. Used `BlueprintJsonServices.Deserialize(text)` with a try/catch in the generator's `CompileOneAsset` method.

**Q3: Did DebugMap need additional fields?**
Yes. `DebugMap` did not initially have `AssetId`, `BlueprintId`, or `StructureHash` fields. Added all three to the `DebugMap` record and to `DebugMapBuilder` via a new constructor `DebugMapBuilder(Guid assetId, int blueprintId, ulong structureHash)`. Updated `CSharpEmitter` to use the new constructor. The serializer uses these fields in the output JSON.

**Q4: Were all 6 Stage8 tests passing?**
Yes. All 6 tests (SC1-SC6) pass. Final count: 188 pass, 3 skip, 0 fail.

**Q5: Did Generators project build successfully?**
Yes. Generators builds with 0 errors and 0 warnings after adding `System.Collections.Immutable`, fixing the Core project reference with `ExcludeAssets="build;buildTransitive;analyzers"` and `AssetTargetFallback`, and resolving all type ambiguities with using aliases.

---

## Deviations from Instructions

1. **`DiagnosticCodes.BP7001_RoslynCompileError` does not exist.** `DiagnosticCodes.cs` has `BP7001 = "BP7001"` without the `_RoslynCompileError` suffix. Used `DiagnosticCodes.BP7001` throughout.

2. **`BlueprintJsonServices.TryDeserialize` does not exist.** Used `BlueprintJsonServices.Deserialize(text)` inside a try/catch block in the generator.

3. **`DebugMap` record had no `AssetId/BlueprintId/StructureHash`.** Added these fields; updated `DebugMapBuilder` constructor and `CSharpEmitter` accordingly. The `DebugMapSerializer` was also designed to include these fields.

4. **`DebugMap` has `Entries` (not `Nodes`/`Pins`).** The `DebugMapSerializer` was adapted to serialize `Entries` (sorted by `GraphId` then `StartLine`), not a separate `Pins` collection.

5. **Parent namespace shadowing with `InMemoryRoslynCompiler` and `BlueprintCompileException`.** The root-level `Hrot.Blueprints.Core.InMemoryRoslynCompiler` stub and the existing `Fdp.Toolkit.Blueprints.BlueprintCompileException` caused CS0104 ambiguity. Resolved using relative namespace qualifiers (`Roslyn.InMemoryRoslynCompiler`, `Roslyn.BlueprintCompileException`) in `Stage8_RoslynFinalize.cs` and `BlueprintCompiler.cs`.

6. **NuGet NU1201 bypass required `AssetTargetFallback` + `ExcludeAssets`.** `SkipGetTargetFrameworkProperties` alone is insufficient at the NuGet restore phase. Added `<AssetTargetFallback>$(AssetTargetFallback);net8.0</AssetTargetFallback>` to allow restore, plus `ExcludeAssets="build;buildTransitive;analyzers"` to prevent CycloneDDS build tooling from being transitively applied to the generator project.

7. **Emitter bugs discovered during Stage 8 compilation.** Three pre-existing bugs in the emit layer were exposed when Roslyn actually compiled the generated C#:
   - `CSharpEmitter.cs`: `[global::Fdp.Toolkit.Blueprints.BlueprintRegistrar]` should be `[global::Fdp.Toolkit.Blueprints.Attributes.BlueprintRegistrar]`
   - `TerminatorEmitter.cs`: `global::Fdp.Toolkit.Blueprints.NodeStatus` should be `global::Hrot.Blueprints.Core.Assets.NodeStatus`
   - `LibraryEmitter.cs`: Library functions using `IrTerm_ReturnStatus` need return type `global::Hrot.Blueprints.Core.Assets.NodeStatus` (not `void`)
   These were fixed as part of this batch because they are required for Stage 8 to function.

8. **Generator using aliases required.** Ambiguities between `Hrot.Blueprints.Core.BlueprintCompiler` (root stub) and `Hrot.Blueprints.Core.Compiler.BlueprintCompiler` (real impl), and between `Microsoft.CodeAnalysis.Diagnostic`/`DiagnosticSeverity` and the Hrot equivalents, required using aliases `BpCompiler` and `BpDiagnostic` in `BlueprintIncrementalGenerator.cs`.

---

## Build Summary

All three projects build successfully:
- `Hrot.Blueprints.Core` — 0 errors, 0 warnings
- `Hrot.Blueprints.Generators` — 0 errors, 0 warnings
- `Hrot.Blueprints.Tests` — 0 errors, 0 warnings
