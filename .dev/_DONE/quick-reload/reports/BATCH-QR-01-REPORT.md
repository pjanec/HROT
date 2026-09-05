# BATCH-QR-01 REPORT — `InMemoryRoslynCompiler` multi-source overload

**Status:** ✅ Done  
**Date:** 2026-06-13

## Changes

### File 1 — `InMemoryRoslynCompiler.cs`

- Added `using System.Collections.Generic;` and `using System.Linq;` (needed for `IReadOnlyList<T>`, `.Select()`, `.ToArray()`).

- **New multi-source overload** (lines 40–102):
  ```csharp
  public (byte[] Pe, byte[] Pdb) Compile(
      IReadOnlyList<(string Source, string VirtualPath)> sources,
      string assemblyName,
      DiagnosticSink sink)
  ```
  Builds N `CSharpSyntaxTree`s (one per source, each with its own `VirtualPath`), creates one `CSharpCompilation` with the same parse/compile/emit options as the original single-source method, creates N `EmbeddedText`s (one per source), emits, and handles errors identically (collect Error diagnostics → `BP7001` → `sink.Add` → throw `BlueprintCompileException`).

- **Single-source `Compile` refactored to delegate** (lines 29–34):
  ```csharp
  public (byte[] Pe, byte[] Pdb) Compile(
      string source, string virtualSourcePath, string assemblyName, DiagnosticSink sink)
      => Compile(new[] { (source, virtualSourcePath) }, assemblyName, sink);
  ```

- `CompileAndLoad` is unchanged — it still calls the single-source `Compile`, which delegates to the multi-source overload. Behavior is byte-for-byte identical.

### File 2 — `InMemoryCompileTests.cs`

- Added `using System.Reflection;` for `Assembly.Load(pe)`.

- **New test: `MultiSource_TwoFiles_CompilesOneAssembly`** (lines 80–112) — two sources where file B calls `NsA.A.V()` from file A. Compiles via the multi-source overload, loads the PE with `Assembly.Load(pe)`, resolves both `NsA.A` and `NsB.B`, invokes `B.W()` and asserts result == 42. Asserts `!sink.HasErrors`.

- **New test: `MultiSource_BrokenSecondFile_ReportsError`** (lines 114–136) — valid first source + syntactically broken second source. Asserts `ThrowsAny<Exception>`, `sink.HasErrors`, and `sink.All` contains `BP7001`. Mirrors the existing `Stage8_InvalidGeneratedSource_EmitsBP7001` pattern.

- All 3 existing tests unchanged and green.

## Build & Test

```
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Hrot.Blueprints.Compiler.csproj
  Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~InMemoryCompile"
  Passed! Failed: 0, Passed: 5, Skipped: 0, Total: 5
```

5 tests passed:
1. `Stage8_InvalidGeneratedSource_EmitsBP7001` (existing)
2. `Stage8_ValidGeneratedSource_ProducesAssemblyBytes` (existing)
3. `Stage8_FullPipelineToRoslyn_Succeeds_ForLibraryAsset` (existing)
4. `MultiSource_TwoFiles_CompilesOneAssembly` (new)
5. `MultiSource_BrokenSecondFile_ReportsError` (new)

No `BLUEPRINT_REGENERATE_SNAPSHOTS` was set.

## Working Agreement Compliance

- **One task.** Only QR-01 scope touched.
- **No cheating.** No `#pragma warning disable`, no `<Compile Remove>`, no weakened assertions.
- **Headless.** Verified via build + unit tests only.
- **Litter-free.** No scratch files, no `Console.WriteLine`.
- **Touched only the two named files.**
- **No codebase-memory MCP tooling used.**
