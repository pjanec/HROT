# BATCH-QR-01 — `InMemoryRoslynCompiler` multi-source overload

**Workstream:** quick-reload (PU-09/EB-E). **Model: pro (Zoo).** **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
**Restate & obey the Working Agreement** in `.dev/quick-reload/TASK-TRACKER.md` (one task; no cheating; finish without
asking until build 0 warnings + tests `Failed:0`; headless; tests assert real values; litter-free; report=truth;
**do NOT use codebase-memory tooling**). Touch ONLY the two files named below.

## Objective (QR-01)
Add a **multi-source** compile overload to `InMemoryRoslynCompiler` so several C# files compile into ONE assembly
(BTree/HSM quick reload emits a topology file + a `[BlueprintRegistrar]` bridge file). Refactor the existing
single-source `Compile` to delegate to it, so behavior stays identical.

## File 1 — `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Roslyn/InMemoryRoslynCompiler.cs`
The existing `Compile(string source, string virtualSourcePath, string assemblyName, DiagnosticSink sink)` builds ONE
`CSharpSyntaxTree` + ONE `EmbeddedText`. Add:
```csharp
public (byte[] Pe, byte[] Pdb) Compile(
    IReadOnlyList<(string Source, string VirtualPath)> sources,
    string assemblyName,
    DiagnosticSink sink)
```
Implementation = the EXACT same logic as the single-source method, generalized to N sources:
- `var encoding = Encoding.UTF8;`
- Build a `CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular)` (same as today).
- For each `(Source, VirtualPath)`: `CSharpSyntaxTree.ParseText(SourceText.From(Source, encoding), parseOptions,
  path: VirtualPath)` → collect into a `syntaxTrees` array.
- `CSharpCompilation.Create(assemblyName, syntaxTrees, _references.Resolve(), new CSharpCompilationOptions(
  OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug, deterministic: true,
  allowUnsafe: true))` (same options).
- `var embeddedTexts = sources.Select(s => EmbeddedTextHelper.Create(s.VirtualPath, s.Source)).ToArray();`
- Same `EmitOptions(DebugInformationFormat.PortablePdb)`, same `compilation.Emit(peStream, pdbStream,
  embeddedTexts: embeddedTexts, options: emitOptions)`, same error handling (collect Error diagnostics → `BP7001` →
  `sink.Add` → throw `BlueprintCompileException`), same `return (peStream.ToArray(), pdbStream.ToArray())`.

Then **refactor the existing single-source `Compile(string, string, string, DiagnosticSink)`** to delegate:
```csharp
=> Compile(new[] { (source, virtualSourcePath) }, assemblyName, sink);
```
(So there's one real implementation. `CompileAndLoad` can keep calling the single-source `Compile` unchanged. Add a
`using System.Linq;`/`System.Collections.Generic;` only if not already present.)

## File 2 — tests: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage8_RoslynTests/InMemoryCompileTests.cs`
This is where the existing `InMemoryRoslynCompiler` tests live. ADD (do not weaken existing):
1. `MultiSource_TwoFiles_CompilesOneAssembly` — two sources where file B references a type from file A (e.g.
   `namespace NsA { public static class A { public static int V() => 41; } }` and
   `namespace NsB { public static class B { public static int W() => NsA.A.V() + 1; } }`). Compile via the new
   overload; load the PE bytes (e.g. `Assembly.Load(pe)` or via a collectible ALC) and assert BOTH
   `NsA.A` and `NsB.B` resolve and `B.W()` returns 42 (proves cross-file references compile into one assembly).
   Assert `!sink.HasErrors`.
2. `MultiSource_BrokenSecondFile_ReportsError` — a valid first source + a syntactically/semantically broken second
   source → expect the `BlueprintCompileException` (or `sink.HasErrors`, matching how the existing tests assert
   failures — mirror them). 
Use the SAME `MetadataReferenceResolver`/`DiagnosticSink` setup the existing tests in this file use (copy their
arrange boilerplate). Keep all existing tests UNCHANGED and green.

## Build & test (no `BLUEPRINT_REGENERATE_SNAPSHOTS`)
```
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Hrot.Blueprints.Compiler.csproj
dotnet test  Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~InMemoryCompile"
```
Both `Failed: 0`; build 0 warnings. (Do not run the full Blueprints suite for this batch; the filtered run is enough.)

## Definition of done
- Multi-source `Compile` overload added; single-source delegates to it; `CompileAndLoad` unchanged.
- Two new tests (cross-file compile + broken-source failure) green; existing InMemoryCompile tests unchanged + green.
- Build 0 warnings. Write `.dev/quick-reload/reports/BATCH-QR-01-REPORT.md` (the overload, the delegation, the tests,
  build/test output).

If anything can't be done as specified, STOP and write the blocker in the report.
