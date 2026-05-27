# Blueprint Subsystem Architecture v1.2 — Inline Patches

> **Status:** Architectural clarifications surfaced during Implementation Roadmap review. These are small precisions to v1.2, not new decisions.
> **Effect:** Tightens three areas of v1.2 to match the engine team's existing patterns more precisely. Implementation Roadmap v1.1 already reflects these.
> **Reads alongside:** `Blueprint_Subsystem_Architecture_v1.2.md` + `Blueprint_Subsystem_Architecture_v1.2_FinalResolutions.md`.

---

## Patch 1 — Remove `BlueprintAiWorkingStateAccess` wrapper class

**v1.2 sections affected:** §3.2, §4.4, §6.5

**The clarification:** The architect (Implementation Roadmap review) confirmed that AiPrimitive working state should follow the engine's native `[SharedAiHeavy*]` projection idiom — no helper class. The compiler emits the projection and StructureHash header check **inline** in each thunk.

**Removed:** `BlueprintAiWorkingStateAccess.GetOrAttach<T>` static helper class. No such class is needed.

**Layout convention** (compile-time documented):

```
Blackboard1024.Memory layout when hosting an AiPrimitive working state:
  Offset 0..7   : ulong  StructureHash    (8 bytes)
  Offset 8..    : T      WorkingState     (struct of the asset's declared working-state fields)
```

The first 8 bytes are reserved for the StructureHash header. The working-state struct projects starting at offset 8.

**Generated thunk pattern** (replacing the v1.2 sample in §4.4):

```csharp
public static NodeStatus BTreeTick(
    ref BrainBlackboard bb, ref BehaviorTreeState state,
    ref BTreeContext ctx, int paramIndex)
{
    // Parameters: project from BrainBlackboard.BehaviorParameters slice
    ref var p = ref Unsafe.As<byte, Params>(
        ref bb.BehaviorParameters[paramIndex * sizeof(Params)]);

    // Working state: inline projection over Blackboard1024 with hash check
    ref var bb1024 = ref ctx.World.GetComponentRW<Blackboard1024>(ctx.Self);
    unsafe
    {
        fixed (byte* memory = bb1024.Memory)
        {
            // Header at first 8 bytes
            ulong storedHash = *(ulong*)memory;
            if (storedHash != StructureHash)
            {
                // Hard reset: zero everything, write our hash, run init
                Unsafe.InitBlock(memory, 0, (uint)sizeof(Blackboard1024));
                *(ulong*)memory = StructureHash;
                InitDefaultWorkingState((WorkingState*)(memory + 8));
            }

            ref var ws = ref Unsafe.AsRef<WorkingState>(memory + 8);
            return TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.Time);
        }
    }
}

private static unsafe void InitDefaultWorkingState(WorkingState* dst)
{
    *dst = default;  // or per-asset specific init
    // ... any non-zero default initialization
}
```

**HSM thunks** follow the same pattern — projection inline, no wrapper:

```csharp
public static unsafe void HsmActivity(void* instance, void* context, HsmCommandWriter* writer)
{
    var bridge = (HsmKernelBridge*)context;
    var world = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
    ref var p = ref *(Params*)instance;

    ref var bb1024 = ref world.GetComponentRW<Blackboard1024>(bridge->Self);
    fixed (byte* memory = bb1024.Memory)
    {
        if (*(ulong*)memory != StructureHash)
        {
            Unsafe.InitBlock(memory, 0, (uint)sizeof(Blackboard1024));
            *(ulong*)memory = StructureHash;
            InitDefaultWorkingState((WorkingState*)(memory + 8));
        }
        ref var ws = ref Unsafe.AsRef<WorkingState>(memory + 8);
        TickCore(ref p, ref ws, bridge->Self, world, world.Time);  // status discarded
    }
}
```

**Implicit Slice 1 constraint:** Only one AiPrimitive working-state Blueprint can occupy an entity's `Blackboard1024` at a time, because the StructureHash header is at a fixed location. If two AiPrimitives with working state are attached to the same entity, the second one's first invocation will overwrite the first's hash and zero the working memory — Slice 1 explicitly documents this and Slice 2 lifts it via the `Blackboard1024` partition allocator.

**Detection:** The compiler can detect static conflicts (one BTree references two AiPrimitives with `WorkingState != null` and both can target the same entity) and emit a warning diagnostic. Runtime detection is not free; documenting it as authoring discipline for Slice 1.

**Removed from v1.2 §3.2 contents list:**
```
× BlueprintAiWorkingStateAccess (Blackboard1024 helpers, single-slot in Slice 1)
```

---

## Patch 2 — `BlueprintTickSystem` phase declaration with `UpdateBefore`

**v1.2 section affected:** §6.7

**The clarification:** Instance Blueprints can write to channel components (`LocomotionChannel`, `WeaponChannel`, `InteractionChannel`) via `ChannelCommandNode`. These writes must complete *before* the dispatcher systems run in the same frame, otherwise commands sit idle for a full frame, creating one-frame jitter.

**Updated phase declaration:**

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
[UpdateBefore(typeof(LocomotionDispatcherSystem))]
[UpdateBefore(typeof(WeaponDispatcherSystem))]
[UpdateBefore(typeof(InteractionDispatcherSystem))]
public sealed class BlueprintTickSystem : IEcsModuleSystem, IProfiledSystem
{
    public string ProfileName => "BlueprintTickSystem";
    // ... rest as in v1.2 §6.7 ...
}
```

**Note:** the three dispatcher type names above should be confirmed against the engine codebase during M10 implementation. If the engine has a sub-phase abstraction like `SystemPhase.SimulationCognitive` that runs deterministically before `SystemPhase.SimulationDispatch`, that may be cleaner than enumerating dispatchers. Until verified, `[UpdateBefore]` per-dispatcher is the safe explicit form.

**For BlueprintMaintenanceSystem** (§6.9): the phase is `BeforeSync` and no `UpdateBefore` is needed — it's a maintenance pass, not a producer of channel writes.

---

## Patch 3 — Debug PDB+EmbeddedSource emission for both paths

**v1.2 sections affected:** §9.1, §9.3, §9.4, §12.3

**The clarification:** Debug Strategy B+C requires that .NET debuggers can step through generated Blueprint C# code. This works in two distinct compilation paths, and they need explicit and separate PDB-emission handling.

### Path A — MSBuild Full Rebuild

- `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` makes Roslyn write `.g.cs` files to disk under `CompilerGeneratedFilesOutputPath`.
- `<DebugType>portable</DebugType>` + `<DebugSymbols>true</DebugSymbols>` make MSBuild emit a portable PDB alongside the DLL.
- The PDB references the `.g.cs` files by absolute path.
- `AiHotReloadCoordinator.LoadAssemblyInto` loads both PE and PDB streams.
- Attached debuggers resolve source from the on-disk `.g.cs` files via PDB path references.

This is standard MSBuild Roslyn output and needs no extra Blueprint-side code.

### Path B — Quick Reload (in-memory)

The in-process compiler library (used by the Editor's Quick Reload button and by the test harness) does **not** go through MSBuild. It calls `CSharpCompilation.Emit` directly. PDB and source-embedding must be explicitly configured. Otherwise PDBs are not generated and debugging the patch ALC fails.

**Required in-memory compilation pattern:**

```csharp
public sealed class InMemoryRoslynCompiler
{
    public (byte[] PE, byte[] PDB) CompileWithSymbols(string generatedSource,
                                                       string virtualSourcePath,
                                                       string assemblyName,
                                                       IReadOnlyList<MetadataReference> references)
    {
        var sourceText = SourceText.From(generatedSource, Encoding.UTF8);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            sourceText,
            new CSharpParseOptions(LanguageVersion.Latest),
            path: virtualSourcePath);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                deterministic: true));

        // Embed source text directly into the PDB so debuggers can find it
        // even though the .cs file may not exist on disk in Quick Reload mode.
        var embeddedTexts = new[] { EmbeddedText.FromSource(virtualSourcePath, sourceText) };

        var emitOptions = new EmitOptions(
            debugInformationFormat: DebugInformationFormat.PortablePdb);

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();

        var result = compilation.Emit(
            peStream: peStream,
            pdbStream: pdbStream,
            embeddedTexts: embeddedTexts,
            options: emitOptions);

        if (!result.Success)
            throw new BlueprintCompileException(/* with diagnostics */);

        return (peStream.ToArray(), pdbStream.ToArray());
    }
}
```

Key elements:

- `CSharpSyntaxTree.ParseText` is called with `path: virtualSourcePath`. The path is a logical identifier (e.g., `"DoorActor_1A2B3C4D_Bp.g.cs"`) — it doesn't need to exist on disk because the source is embedded next.
- `EmbeddedText.FromSource(virtualSourcePath, sourceText)` stores the actual source text **inside the PDB**. Debuggers look here when the path is not findable on disk.
- `EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb)` is mandatory; the default is `Pdb` (Windows-only legacy format) which doesn't work cross-platform.
- `OptimizationLevel.Debug` keeps the local variables and step-through behavior debugger-friendly.
- `deterministic: true` keeps repeated compiles byte-identical.

**Loading the patch ALC:**

```csharp
var (peBytes, pdbBytes) = compiler.CompileWithSymbols(/* ... */);
var alc = new AssemblyLoadContext($"BlueprintPatch_{Guid.NewGuid():N}", isCollectible: true);
using var peStream = new MemoryStream(peBytes);
using var pdbStream = new MemoryStream(pdbBytes);
Assembly loaded = alc.LoadFromStream(peStream, pdbStream);  // two-arg overload
```

The two-arg overload is what gives the attached debugger access to symbols. Without it, debugger attach finds the patch assembly but cannot step into it.

### Update to v1.2 §9.4 — compile modes table

```
| Mode    | Probes emitted | PDB              | Source on disk (Full)   | Source embedded (Quick) |
|---------|----------------|------------------|-------------------------|-------------------------|
| Release | No             | Yes (Portable)   | Yes (for symbol resolve)| Yes                     |
| Debug   | At node enter  | Yes              | Yes                     | Yes                     |
| Trace   | + pin values   | Yes              | Yes                     | Yes                     |
```

In both modes (Full Rebuild and Quick Reload), the same set of compile modes (Release / Debug / Trace) applies. The difference is purely *where the source comes from when the debugger asks*: Full Rebuild → on-disk file via PDB path reference; Quick Reload → embedded text inside the PDB.

### Implementation note

Both the **test harness** and the **editor's Quick Reload** use this same `InMemoryRoslynCompiler` class. The test harness can choose to skip PDB generation for speed (its tests don't need debugger attach), but the editor must always emit PDBs in Quick Reload mode for the debug-by-attach workflow to function.

The compiler library exposes both modes:

```csharp
public sealed record CompileToAssemblyOptions(
    CompilerMode Mode,
    bool EmitPdbWithEmbeddedSource);

public sealed record CompiledAssembly(
    byte[] PE,
    byte[]? PDB,        // null if EmitPdbWithEmbeddedSource was false
    DebugMap? DebugMap);
```

---

## Summary of changes

| Patch | Affects | Effort change |
|---|---|---|
| 1: Drop `BlueprintAiWorkingStateAccess` wrapper | §3.2 listing, §4.4 thunk samples, §6.5 helper class section | Slightly smaller runtime surface; emission pattern documented inline |
| 2: Phase ordering with `UpdateBefore` | §6.7 phase declaration | One annotation per dispatcher; no other changes |
| 3: PDB + EmbeddedSource for Quick Reload | §9.1 strategy, §9.4 compile modes, §12.3 Quick Reload | Adds `InMemoryRoslynCompiler` shape spec; ~50 lines of compiler code |

None of these patches changes the architectural shape of v1.2. They tighten specific emission patterns and clarify boundaries. The Compiler Detailed Design (next document) will incorporate them directly.

---

*End of v1.2 inline patches. With these and Final Resolutions, v1.2 is fully consolidated and architect-approved.*
