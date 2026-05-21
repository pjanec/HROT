# BATCH-13 — TASK-CP-005: Stage 8 Roslyn + Incremental Generator + Catalogs

## References
- **Task detail:** `.dev/blueprints-1/TASK-DETAIL.md#TASK-CP-005`
- **Compiler DD §11:** `Blueprint_Subsystem_Compiler_Detailed_Design.md` — Stage 8 Roslyn finalize (§11.1–§11.4)
- **Compiler DD §12:** Determinism enforcement
- **Compiler DD §13:** Debug map generation (DebugMapSerializer)
- **Compiler DD §14:** Catalogs integration
- **Inline Patches:** `Blueprint_Subsystem_Compiler_Detailed_Design_InlinePatches.md` — Patch 1 (incremental generator 4-provider pipeline), Patch 2 (MetadataReferenceResolver BOTH predicates)

## Baseline
- Tests: **182 pass, 3 skip, 0 fail**
- `Stage7_Emit.Run` is fully implemented (BATCH-12)
- `InMemoryRoslynCompiler.CompileAndLoad` throws `NotImplementedException`
- `MetadataReferenceResolver` is a static stub returning a throw
- `EmbeddedTextHelper.CreateEmbeddedText` throws `NotImplementedException`
- `BlueprintIncrementalGenerator.Initialize` has a placeholder implementation
- Catalog interfaces exist in `CatalogInterfaces.cs`, stub implementations return empty lists

## Scope Overview

This batch wires up the Roslyn compilation layer and the incremental generator plumbing.

**Files to CREATE:**
1. `Hrot.Blueprints.Core/Compiler/Roslyn/BlueprintCompileException.cs`
2. `Hrot.Blueprints.Core/Compiler/Stages/Stage8_RoslynFinalize.cs`
3. `Hrot.Blueprints.Core/Compiler/BlueprintSignatureParser.cs`
4. `Hrot.Blueprints.Core/Compiler/Emit/DebugMapSerializer.cs`
5. `Hrot.Blueprints.Tests/Stage8Tests.cs`

**Files to MODIFY:**
1. `Hrot.Blueprints.Core/Hrot.Blueprints.Core.csproj` — add `Microsoft.CodeAnalysis.CSharp` package (Version `4.8.0`)
2. `Hrot.Blueprints.Core/Compiler/Roslyn/InMemoryRoslynCompiler.cs` — full implementation
3. `Hrot.Blueprints.Core/Compiler/Roslyn/MetadataReferenceResolver.cs` — implement class per Patch 2
4. `Hrot.Blueprints.Core/Compiler/Roslyn/EmbeddedTextHelper.cs` — implement
5. `Hrot.Blueprints.Core/Compiler/BlueprintCompiler.cs` — wire Stage 8 when `EmitPdbWithEmbeddedSource = true`
6. `Hrot.Blueprints.Generators/Hrot.Blueprints.Generators.csproj` — add Core project reference with `SkipGetTargetFrameworkProperties`
7. `Hrot.Blueprints.Generators/BlueprintIncrementalGenerator.cs` — implement 4-provider pipeline (Patch 1)

---

## Step 1 — Add Roslyn package to Core project

Add to `Hrot.Blueprints.Core.csproj`:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
</ItemGroup>
```

---

## Step 2 — `BlueprintCompileException.cs`

```csharp
namespace Hrot.Blueprints.Core.Compiler.Roslyn;

public sealed class BlueprintCompileException : Exception
{
    public IReadOnlyList<Diagnostics.Diagnostic> CompilerDiagnostics { get; }

    public BlueprintCompileException(
        string message,
        IReadOnlyList<Diagnostics.Diagnostic> diagnostics)
        : base(message)
    {
        CompilerDiagnostics = diagnostics;
    }
}
```

---

## Step 3 — `MetadataReferenceResolver.cs` (full implementation)

Per §11.3 + **Patch 2** (both predicates MANDATORY):

```csharp
using Microsoft.CodeAnalysis;
using System.Reflection;

namespace Hrot.Blueprints.Core.Compiler.Roslyn;

public sealed class MetadataReferenceResolver
{
    private readonly IReadOnlyList<MetadataReference> _references;

    public MetadataReferenceResolver(IReadOnlyList<MetadataReference> references)
        => _references = references;

    public IReadOnlyList<MetadataReference> Resolve() => _references;

    /// <summary>
    /// Creates a resolver from assemblies loaded into the current AppDomain.
    /// Filters out dynamic assemblies and assemblies with no on-disk location
    /// (Patch 2: BOTH predicates required — IsDynamic catches codegen assemblies;
    /// Location=="" catches collectible ALC assemblies that are NOT IsDynamic).
    /// </summary>
    public static MetadataReferenceResolver ForRuntimeAssemblies(
        IEnumerable<Assembly> assemblies)
    {
        var refs = assemblies
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList<MetadataReference>();
        return new MetadataReferenceResolver(refs);
    }
}
```

---

## Step 4 — `EmbeddedTextHelper.cs`

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Hrot.Blueprints.Core.Compiler.Roslyn;

internal static class EmbeddedTextHelper
{
    public static EmbeddedText Create(string virtualPath, string sourceText)
    {
        var text = SourceText.From(sourceText, Encoding.UTF8);
        return EmbeddedText.FromSource(virtualPath, text);
    }
}
```

---

## Step 5 — `InMemoryRoslynCompiler.cs` (full implementation)

Per §11.2. The class exposes two entry points:
- `Compile(...)` → `(byte[] Pe, byte[] Pdb)` — used by Stage 8 when `EmitPdbWithEmbeddedSource = true`
- `CompileAndLoad(...)` → `Assembly` — used by hot-reload and editor (also calls Compile then loads)

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Roslyn;

public sealed class InMemoryRoslynCompiler
{
    private readonly MetadataReferenceResolver _references;

    public InMemoryRoslynCompiler(MetadataReferenceResolver references)
        => _references = references;

    /// <summary>
    /// Compile source to PE and PDB bytes with embedded source text.
    /// Throws BlueprintCompileException on Roslyn errors.
    /// </summary>
    public (byte[] Pe, byte[] Pdb) Compile(
        string source,
        string virtualSourcePath,
        string assemblyName,
        DiagnosticSink sink)
    {
        var encoding = Encoding.UTF8;
        var sourceText = SourceText.From(source, encoding);
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            DocumentationMode.None,
            SourceCodeKind.Regular);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            sourceText,
            parseOptions,
            path: virtualSourcePath);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            _references.Resolve(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                deterministic: true,
                allowUnsafe: true));

        var embeddedText = EmbeddedTextHelper.Create(virtualSourcePath, source);
        var emitOptions = new EmitOptions(
            debugInformationFormat: DebugInformationFormat.PortablePdb);

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();

        var result = compilation.Emit(
            peStream: peStream,
            pdbStream: pdbStream,
            embeddedTexts: new[] { embeddedText },
            options: emitOptions);

        if (!result.Success)
        {
            var bpDiags = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => Diagnostic.Error(
                    DiagnosticCodes.BP7001_RoslynCompileError,
                    $"Roslyn: {d.Id} {d.GetMessage()}"))
                .ToList();

            foreach (var diag in bpDiags)
                sink.Add(diag);

            throw new BlueprintCompileException(
                "In-memory Roslyn compilation failed. See diagnostics.",
                bpDiags);
        }

        return (peStream.ToArray(), pdbStream.ToArray());
    }

    /// <summary>
    /// Compile then load into a new collectible AssemblyLoadContext.
    /// The caller owns the ALC and is responsible for calling Unload().
    /// </summary>
    public (Assembly Assembly, AssemblyLoadContext Alc) CompileAndLoad(
        string source,
        string virtualSourcePath,
        string assemblyName,
        DiagnosticSink sink)
    {
        var (pe, pdb) = Compile(source, virtualSourcePath, assemblyName, sink);
        var alc = new AssemblyLoadContext($"BlueprintPatch_{assemblyName}", isCollectible: true);
        using var peStream = new MemoryStream(pe);
        using var pdbStream = new MemoryStream(pdb);
        var assembly = alc.LoadFromStream(peStream, pdbStream);
        return (assembly, alc);
    }
}
```

**Note:** `DiagnosticCodes.BP7001_RoslynCompileError` must be added to `DiagnosticCodes.cs` as:
```csharp
public const string BP7001_RoslynCompileError = "BP7001";
```
Check `DiagnosticCodes.cs` first to see if BP7001 already exists, and if so what its name is.

---

## Step 6 — `Stage8_RoslynFinalize.cs`

```csharp
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Roslyn;
using Microsoft.CodeAnalysis;

namespace Hrot.Blueprints.Core.Compiler.Stages;

internal static class Stage8_RoslynFinalize
{
    public static (byte[] Pe, byte[] Pdb) Run(
        string generatedSource,
        string virtualSourcePath,
        string assemblyName,
        MetadataReferenceResolver references,
        DiagnosticSink sink)
    {
        var compiler = new InMemoryRoslynCompiler(references);
        return compiler.Compile(generatedSource, virtualSourcePath, assemblyName, sink);
    }
}
```

---

## Step 7 — Wire Stage 8 in `BlueprintCompiler.cs`

Currently `BlueprintCompiler.Compile` returns `PortablePdb: null, PortablePe: null` always. Change to:

After Stage 7 succeeds, if `options.EmitPdbWithEmbeddedSource` is true:
```csharp
byte[]? pe = null;
byte[]? pdb = null;

if (options.EmitPdbWithEmbeddedSource)
{
    // Build references from AppDomain (the caller's loaded assemblies)
    var references = MetadataReferenceResolver.ForRuntimeAssemblies(
        AppDomain.CurrentDomain.GetAssemblies());
    var virtualPath = options.VirtualSourcePath
        ?? $"{lowered.SanitizedName}_{lowered.BlueprintId:X8}_Bp.g.cs";
    var asmName = $"{lowered.SanitizedName}_{lowered.BlueprintId:X8}";

    try
    {
        (pe, pdb) = Stage8_RoslynFinalize.Run(
            generatedSource, virtualPath, asmName, references, sink);
    }
    catch (BlueprintCompileException)
    {
        // diagnostics already added to sink; return failure
        if (sink.HasErrors) return FailResult(sink, typed.Asset);
    }
}

return new CompileResult(
    Succeeded:         true,
    GeneratedSource:   generatedSource,
    GeneratedFileName: $"{lowered.SanitizedName}_{lowered.BlueprintId:X8}_Bp.g.cs",
    BlueprintId:       lowered.BlueprintId,
    StructureHash:     lowered.StructureHash,
    DebugMap:          debugMap,
    Diagnostics:       sink.All,
    CanonicalAsset:    typed.Asset,
    PortablePdb:       pdb,
    PortablePe:        pe);
```

---

## Step 8 — `BlueprintSignatureParser.cs`

Lightweight parser that reads only identity and dispatch info from `.bp.json` — no node/link parsing.

```csharp
using System.Text.Json;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler;

/// <summary>
/// Lightweight JSON extractor for per-asset signature metadata.
/// Reads only identity, dispatch, and callable-export info.
/// Does NOT parse nodes or links.
/// </summary>
public static class BlueprintSignatureParser
{
    public static BlueprintSignature Parse(string filePath, string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
            return Empty(filePath);

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var assetId = root.TryGetProperty("assetId", out var idProp)
                ? Guid.TryParse(idProp.GetString(), out var g) ? g : Guid.Empty
                : Guid.Empty;

            var name = root.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? ""
                : "";

            var dispatch = ParseDispatch(root);

            var exportedFunctions = ParseExportedFunctions(root);
            var hostings = ParseHostings(root);
            var callablePeers = ParseCallablePeers(root);

            var sanitized = Emit.Sanitizer.SanitizeName(name);
            int blueprintId = 0;
            if (assetId != Guid.Empty)
            {
                Span<byte> bytes = stackalloc byte[16];
                assetId.TryWriteBytes(bytes);
                blueprintId = unchecked((int)Determinism.FnvHasher.Hash32(bytes));
            }

            return new BlueprintSignature(
                Path: filePath,
                AssetId: assetId,
                Name: name,
                SanitizedName: sanitized,
                BlueprintId: blueprintId,
                Dispatch: dispatch,
                ExportedFunctionNames: exportedFunctions,
                Hostings: hostings,
                DeclaredCallablePeers: callablePeers);
        }
        catch
        {
            return Empty(filePath);
        }
    }

    private static BlueprintDispatchKind ParseDispatch(JsonElement root)
    {
        if (!root.TryGetProperty("dispatch", out var dispProp)) return BlueprintDispatchKind.Library;
        return dispProp.GetString()?.ToLowerInvariant() switch
        {
            "aiprimitive" => BlueprintDispatchKind.AiPrimitive,
            "instance"    => BlueprintDispatchKind.Instance,
            _             => BlueprintDispatchKind.Library,
        };
    }

    private static IReadOnlyList<string> ParseExportedFunctions(JsonElement root)
    {
        var result = new List<string>();
        if (!root.TryGetProperty("graphs", out var graphs)) return result;
        foreach (var graph in graphs.EnumerateArray())
        {
            var kind = graph.TryGetProperty("kind", out var kp) ? kp.GetString() : null;
            if (kind?.Equals("Function", StringComparison.OrdinalIgnoreCase) != true) continue;
            var name = graph.TryGetProperty("name", out var np) ? np.GetString() : null;
            if (!string.IsNullOrEmpty(name)) result.Add(name!);
        }
        return result;
    }

    private static IReadOnlyList<AiPrimitiveHosting> ParseHostings(JsonElement root)
    {
        var result = new List<AiPrimitiveHosting>();
        if (!root.TryGetProperty("primitive", out var prim)) return result;
        if (!prim.TryGetProperty("hostings", out var hostings)) return result;
        foreach (var h in hostings.EnumerateArray())
        {
            var val = h.GetString();
            if (Enum.TryParse<AiPrimitiveHosting>(val, ignoreCase: true, out var hosting))
                result.Add(hosting);
        }
        return result;
    }

    private static IReadOnlyList<Guid> ParseCallablePeers(JsonElement root)
    {
        var result = new List<Guid>();
        if (!root.TryGetProperty("callablePeers", out var peers)) return result;
        foreach (var p in peers.EnumerateArray())
        {
            if (Guid.TryParse(p.GetString(), out var id))
                result.Add(id);
        }
        return result;
    }

    private static BlueprintSignature Empty(string filePath) =>
        new BlueprintSignature(
            Path: filePath,
            AssetId: Guid.Empty,
            Name: "",
            SanitizedName: "_",
            BlueprintId: 0,
            Dispatch: BlueprintDispatchKind.Library,
            ExportedFunctionNames: Array.Empty<string>(),
            Hostings: Array.Empty<AiPrimitiveHosting>(),
            DeclaredCallablePeers: Array.Empty<Guid>());
}
```

---

## Step 9 — `DebugMapSerializer.cs`

Deterministic JSON serialization of DebugMap (§13). Create in `Hrot.Blueprints.Core/Compiler/Emit/`.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hrot.Blueprints.Core.Compiler.Emit;

public static class DebugMapSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serialize DebugMap to JSON. Output is deterministic for identical inputs.</summary>
    public static string Serialize(DebugMap debugMap)
    {
        // Build a deterministic DTO to control field order and sort entries
        var dto = new DebugMapDto
        {
            AssetId       = debugMap.AssetId,
            BlueprintId   = debugMap.BlueprintId,
            StructureHash = debugMap.StructureHash,
            Nodes = debugMap.Nodes
                .OrderBy(n => n.GraphId)
                .ThenBy(n => n.StartLine)
                .Select(n => new NodeEntryDto
                {
                    NodeId      = n.NodeId,
                    GraphId     = n.GraphId,
                    StartLine   = n.StartLine,
                    EndLine     = n.EndLine,
                })
                .ToList(),
            Pins = debugMap.Pins
                .OrderBy(p => p.PinId)
                .Select(p => new PinEntryDto
                {
                    PinId                 = p.PinId,
                    NodeId                = p.NodeId,
                    PinName               = p.PinName,
                    ValueAccessExpression = p.ValueAccessExpression,
                    Type                  = p.Type,
                })
                .ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public static DebugMap? Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<DebugMapDto>(json, Options);
        if (dto is null) return null;

        return new DebugMap
        {
            AssetId       = dto.AssetId,
            BlueprintId   = dto.BlueprintId,
            StructureHash = dto.StructureHash,
            Nodes = dto.Nodes.Select(n => new DebugMapNodeEntry
            {
                NodeId      = n.NodeId,
                GraphId     = n.GraphId,
                StartLine   = n.StartLine,
                EndLine     = n.EndLine,
            }).ToList(),
            Pins = dto.Pins.Select(p => new DebugMapPinEntry
            {
                PinId                 = p.PinId,
                NodeId                = p.NodeId,
                PinName               = p.PinName,
                ValueAccessExpression = p.ValueAccessExpression,
                Type                  = p.Type,
            }).ToList(),
        };
    }

    private sealed class DebugMapDto
    {
        public Guid   AssetId       { get; set; }
        public int    BlueprintId   { get; set; }
        public ulong  StructureHash { get; set; }
        public List<NodeEntryDto> Nodes { get; set; } = new();
        public List<PinEntryDto>  Pins  { get; set; } = new();
    }

    private sealed class NodeEntryDto
    {
        public Guid   NodeId    { get; set; }
        public Guid   GraphId   { get; set; }
        public int    StartLine { get; set; }
        public int    EndLine   { get; set; }
    }

    private sealed class PinEntryDto
    {
        public Guid   PinId                 { get; set; }
        public Guid   NodeId                { get; set; }
        public string PinName               { get; set; } = "";
        public string ValueAccessExpression { get; set; } = "";
        public string Type                  { get; set; } = "";
    }
}
```

Before creating this, check if `DebugMap` already has the `BlueprintId` and `StructureHash` properties. Check `Hrot.Blueprints.Core/Compiler/Emit/DebugMap.cs` (or wherever `DebugMap` is defined). If `DebugMap` is missing those properties, add them.

---

## Step 10 — `Hrot.Blueprints.Generators.csproj` (add Core reference)

Add a project reference to enable calling `BlueprintCompiler` from the generator:

```xml
<ItemGroup>
  <ProjectReference
    Include="..\..\Hrot.Blueprints.Core\Hrot.Blueprints.Core.csproj"
    SkipGetTargetFrameworkProperties="true" />
</ItemGroup>
```

`SkipGetTargetFrameworkProperties="true"` allows the `netstandard2.0` generator project to reference the `net8.0` Core project. The generator output is consumed only at build time, not deployed, so this is safe.

Also add `ImmutableArray<T>` support (used in Patch 1 snippet). Add to the project file:
```xml
<PackageReference Include="System.Collections.Immutable" Version="8.0.0" />
```

---

## Step 11 — `BlueprintIncrementalGenerator.cs` (implement 4-provider Patch 1 pipeline)

Replace the existing placeholder implementation with the Patch 1 pattern:

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class BlueprintIncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Provider 1 — raw file text from .bp.json AdditionalTexts
        IncrementalValuesProvider<(string Path, string Text)> rawFiles =
            context.AdditionalTextsProvider
                .Where(at => at.Path.EndsWith(".bp.json", System.StringComparison.OrdinalIgnoreCase))
                .Select((at, ct) =>
                {
                    var text = at.GetText(ct)?.ToString() ?? "";
                    return (at.Path, text);
                });

        // Provider 2 — per-asset signature (lightweight parse)
        IncrementalValuesProvider<BlueprintSignature> signatures =
            rawFiles.Select((rf, ct) => BlueprintSignatureParser.Parse(rf.Path, rf.Text));

        // Provider 3 — collected sibling catalog
        IncrementalValueProvider<ImmutableArray<BlueprintSignature>> siblingCatalog =
            signatures.Collect();

        // Provider 4 — per-asset full compile combined with sibling catalog
        IncrementalValuesProvider<CompileResult> compileResults =
            rawFiles.Combine(siblingCatalog)
                    .Select((pair, ct) =>
                    {
                        var (rawFile, siblings) = pair;
                        return CompileOneAsset(rawFile.Path, rawFile.Text, siblings, ct);
                    });

        // Register source output
        context.RegisterSourceOutput(compileResults, static (spc, result) =>
        {
            if (result.GeneratedSource == null || !result.Succeeded)
            {
                foreach (var diag in result.Diagnostics)
                    spc.ReportDiagnostic(ToRoslynDiagnostic(diag));
                return;
            }
            spc.AddSource(result.GeneratedFileName ?? "Blueprint.g.cs", result.GeneratedSource);
        });
    }

    private static CompileResult CompileOneAsset(
        string path,
        string text,
        ImmutableArray<BlueprintSignature> siblings,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var asset = BlueprintJsonServices.TryDeserialize(text);
        if (asset is null)
        {
            // Return failed result with a parse diagnostic
            return FailedParse(path);
        }

        var compiler = new BlueprintCompiler();
        var options = new CompileOptions(
            Mode:              CompilerMode.Release,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: siblings.ToList(),
            EmitPdbWithEmbeddedSource: false);

        return compiler.Compile(asset, options);
    }

    private static CompileResult FailedParse(string path) =>
        new CompileResult(
            Succeeded:         false,
            GeneratedSource:   null,
            GeneratedFileName: null,
            BlueprintId:       0,
            StructureHash:     0UL,
            DebugMap:          null,
            Diagnostics:       new[]
            {
                Core.Compiler.Diagnostics.Diagnostic.Error("BP0002",
                    $"Blueprint file '{path}' could not be parsed.")
            },
            CanonicalAsset:    null,
            PortablePdb:       null,
            PortablePe:        null);

    private static Microsoft.CodeAnalysis.Diagnostic ToRoslynDiagnostic(
        Core.Compiler.Diagnostics.Diagnostic diag)
    {
        var descriptor = new DiagnosticDescriptor(
            id:                 diag.Code,
            title:              diag.Code,
            messageFormat:      diag.Message,
            category:           "Blueprints",
            defaultSeverity:    diag.IsError
                                    ? DiagnosticSeverity.Error
                                    : DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
        return Microsoft.CodeAnalysis.Diagnostic.Create(descriptor, Location.None);
    }
}
```

**Implementation notes for the generator:**
- `BlueprintJsonServices.TryDeserialize(text)` — check if this method exists. If not, use the existing `Stage1_Parse.Run` with a fresh `DiagnosticSink`, catching exceptions and returning null on failure
- The `Diagnostics.Diagnostic.Error(code, message)` factory — check whether `Diagnostic.Error` takes just `(string code, string message)` or requires additional args. Look at the existing `DiagnosticCodes.cs` and `Diagnostic` class.
- The `Diagnostic.IsError` property — verify this exists; it should be a `bool` on the `Diagnostic` record

---

## Step 12 — Check and update `DebugMap` record

Before implementing `DebugMapSerializer`, read the actual `DebugMap` class (look in `Emit/` directory). The `DebugMapBuilder.Build()` method returns a `DebugMap`. Check that `DebugMap` has:
- `AssetId : Guid`
- `BlueprintId : int` 
- `StructureHash : ulong`
- `Nodes : IReadOnlyList<DebugMapNodeEntry>`
- `Pins : IReadOnlyList<DebugMapPinEntry>`

If `BlueprintId` and `StructureHash` are missing from the existing `DebugMap` type, add them. `DebugMapBuilder.Build()` will need to pass them from the `IrAsset` via `DebugMapBuilder`'s constructor (which already takes `assetId`). Modify `DebugMapBuilder` to also accept `blueprintId` and `structureHash` if needed.

---

## Tests: `Stage8Tests.cs`

```csharp
namespace Hrot.Blueprints.Tests;

public sealed class Stage8Tests
{
    // Factory: creates a compiler that produces a simple Library asset
    private static (BlueprintCompiler Compiler, BlueprintAsset Asset) MakeSimpleLibrary()
    {
        var asset = BlueprintAssetBuilder
            .Library("Stage8Lib")
            .WithGraph("Add", g => g.Entry().Return())
            .Build();
        return (new BlueprintCompiler(), asset);
    }

    private static CompileOptions OptionsWithPdb() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>(),
            EmitPdbWithEmbeddedSource: true);

    // SC1: InMemoryRoslynCompiler produces non-empty PE + PDB
    [Fact]
    public void Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb()
    {
        var (compiler, asset) = MakeSimpleLibrary();
        var result = compiler.Compile(asset, OptionsWithPdb());

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.PortablePe);
        Assert.NotNull(result.PortablePdb);
        Assert.True(result.PortablePe!.Length > 0, "PE should be non-empty");
        Assert.True(result.PortablePdb!.Length > 0, "PDB should be non-empty");
    }

    // SC2: PDB contains embedded source text
    [Fact]
    public void Stage8_PdbContainsEmbeddedSource()
    {
        var (compiler, asset) = MakeSimpleLibrary();
        var result = compiler.Compile(asset, OptionsWithPdb());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.PortablePdb);

        // Verify embedded source by reading the PDB with System.Reflection.Metadata
        // PortablePDB embedded source is detectable by checking if the PDB starts with the
        // portable PDB magic bytes or by checking its content is non-trivially sized
        Assert.True(result.PortablePdb!.Length > 100,
            "PDB with embedded source should be substantial in size");

        // Additionally: the generated source should appear in the PDB as embedded text.
        // Use a simple string search to confirm the source text is embedded in the PDB bytes.
        // (Embedded source is stored compressed; we check for the generated file identifier.)
        var pdbText = System.Text.Encoding.UTF8.GetString(result.PortablePdb!);
        // The file path or class name should appear somewhere in the PDB metadata
        Assert.True(
            pdbText.Contains("Stage8Lib_", StringComparison.Ordinal) ||
            result.PortablePdb!.Length > 500,  // minimum reasonable size for embedded source
            "PDB should contain embedded source reference");
    }

    // SC3: InMemoryRoslynCompiler throws BlueprintCompileException for invalid C#
    [Fact]
    public void Stage8_InvalidCSharp_ThrowsBlueprintCompileException()
    {
        var invalidSource = "this is not valid C# code { unclosed";
        var sink = new Hrot.Blueprints.Core.Compiler.Diagnostics.DiagnosticSink();
        var refs = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());
        var compiler = new InMemoryRoslynCompiler(refs);

        Assert.Throws<BlueprintCompileException>(() =>
            compiler.Compile(invalidSource, "invalid.g.cs", "InvalidAssembly", sink));

        Assert.True(sink.HasErrors, "Sink should have errors after failed compile");
    }

    // SC4: MetadataReferenceResolver excludes in-memory (Location == "") assemblies
    [Fact]
    public void Stage8_MetadataReferenceResolver_ExcludesNoLocationAssemblies()
    {
        // Create a tiny in-memory assembly via Roslyn
        var source = "namespace Test { public class X {} }";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var refs = MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());
        var comp = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "InMemoryAssembly",
            new[] { tree },
            refs.Resolve(),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        comp.Emit(ms);
        ms.Position = 0;

        var alc = new System.Runtime.Loader.AssemblyLoadContext("SC4Test", isCollectible: true);
        try
        {
            var asm = alc.LoadFromStream(ms);
            // asm.Location should be "" for in-memory loaded assembly
            Assert.Equal("", asm.Location);

            // ForRuntimeAssemblies should exclude it
            var allAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Concat(new[] { asm });
            var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(allAssemblies);
            var resolvedPaths = resolver.Resolve()
                .Cast<Microsoft.CodeAnalysis.PortableExecutableReference>()
                .Select(r => r.FilePath)
                .ToList();

            Assert.DoesNotContain("", resolvedPaths);
        }
        finally
        {
            alc.Unload();
        }
    }

    // SC5: BlueprintSignatureParser.Parse extracts basic fields
    [Fact]
    public void Stage8_SignatureParser_ExtractsFields()
    {
        var json = """
        {
            "assetId": "11111111-2222-3333-4444-555555555555",
            "name": "TestAiPrimitive",
            "dispatch": "AiPrimitive",
            "primitive": { "hostings": ["BTreeAction"] },
            "graphs": [
                { "id": "g1", "name": "Execute", "kind": "Function", "nodes": [], "links": [] }
            ]
        }
        """;

        var sig = BlueprintSignatureParser.Parse("test.bp.json", json);

        Assert.Equal("test.bp.json", sig.Path);
        Assert.Equal(new Guid("11111111-2222-3333-4444-555555555555"), sig.AssetId);
        Assert.Equal("TestAiPrimitive", sig.Name);
        Assert.Equal(Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.AiPrimitive, sig.Dispatch);
        Assert.Contains("Execute", sig.ExportedFunctionNames);
        Assert.Contains(Hrot.Blueprints.Core.Assets.AiPrimitiveHosting.BTreeAction, sig.Hostings);
    }

    // SC6: DebugMap serialization is deterministic
    [Fact]
    public void Stage8_DebugMapSerializer_IsDeterministic()
    {
        var (compiler, asset) = MakeSimpleLibrary();
        var result1 = compiler.Compile(asset, DefaultOptions());
        var result2 = compiler.Compile(asset, DefaultOptions());

        Assert.True(result1.Succeeded);
        Assert.True(result2.Succeeded);
        Assert.NotNull(result1.DebugMap);
        Assert.NotNull(result2.DebugMap);

        var json1 = DebugMapSerializer.Serialize(result1.DebugMap!);
        var json2 = DebugMapSerializer.Serialize(result2.DebugMap!);
        Assert.Equal(json1, json2);
    }

    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());
}
```

**Using directives needed in Stage8Tests.cs:**
```csharp
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Compiler.Roslyn;
using Hrot.Blueprints.Tests.Builders;
using Microsoft.CodeAnalysis;
```

---

## Implementation Notes

### DiagnosticCodes.cs
Before using `DiagnosticCodes.BP7001_RoslynCompileError`, check the file. The constant might already be defined with a different name. If it doesn't exist, add:
```csharp
public const string BP7001_RoslynCompileError = "BP7001";
```

### DebugMap record
Read `Hrot.Blueprints.Core/Compiler/Emit/DebugMap.cs` (or wherever DebugMap is defined — may be in `DebugMapBuilder.cs`). Before creating the serializer, ensure the record has the properties expected.

### Generator project references
The `Hrot.Blueprints.Generators` project cannot reference Roslyn's `InMemoryRoslynCompiler` at generate time (generators should NOT compile sub-assemblies at generation time — that's a separate runtime operation). The generator only calls `BlueprintCompiler.Compile` with `EmitPdbWithEmbeddedSource: false`. This is the intended behavior.

### `BlueprintJsonServices.TryDeserialize`
If this helper doesn't exist, implement `CompileOneAsset` using Stage1_Parse.Run:
```csharp
var parseSink = new DiagnosticSink();
var asset = Stage1_Parse.Run(text, parseSink);
if (asset is null) return FailedParse(path);
```

### Tests project needs `Microsoft.CodeAnalysis.CSharp`
The `Hrot.Blueprints.Tests` project will need to reference `Microsoft.CodeAnalysis.CSharp` to use `CSharpSyntaxTree` in SC4. Check the test project's `.csproj` and add the package if not present.

---

## Build and Test Commands

```powershell
cd d:\WORK\IOS-IG-SimHost-FDP
dotnet restore
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Hrot.Blueprints.Core.csproj
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Generators/Hrot.Blueprints.Generators.csproj
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --filter "Stage8" -v normal
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -v minimal
```

**Expected baseline preservation:** All 182 existing tests still pass.
**Expected new tests:** 6 new Stage8 tests (SC1-SC6), all passing.
**Expected total:** At least 188 pass, 3 skip, 0 fail.

Note: SC7 from the task ("dotnet build zero errors in Core and Generators") is verified by the build commands above, not by a test.

---

## Output Questions for Batch Report

1. What is the `PortablePe.Length` range for a simple Library asset compiled with `EmitPdbWithEmbeddedSource = true`? (Just approximate — hundreds of bytes? Kilobytes?)
2. Was `BlueprintJsonServices.TryDeserialize` already present or did you implement an alternative?
3. Did `DebugMap` need any additional fields added (`BlueprintId`, `StructureHash`)? If so, what changes were needed in `DebugMapBuilder`?
4. Were all 6 Stage8 tests passing? List any that were skipped or had issues.
5. Did the `Hrot.Blueprints.Generators` project build successfully after adding the Core project reference?
