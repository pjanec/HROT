# Blueprint Subsystem — Compiler Detailed Design — Inline Patches

> **Status:** Patches to `Blueprint_Subsystem_Compiler_Detailed_Design.md` from architect's review.
> **Effect:** Two corrections to mechanics that would have caused runtime crashes (in-memory ALC reference resolution) and severe build-time performance degradation (incremental-generator cache invalidation). Plus four Q-18.x open-question resolutions.
> **Reads alongside:** the main Compiler DD; nothing in the main doc is invalidated, only refined.

---

## Patch 1 — Incremental Generator pipeline (corrects §18.7)

### The problem

§18.7 of the Compiler DD called for a "two-pass walk" to resolve sibling-asset references at generator time. As written, a naive interpretation would put both passes inside the same generator-execution callback, parsing all `.bp.json` files to compile each one. This destroys Roslyn's incremental caching: editing one `.bp.json` invalidates *every* asset's cached output, causing O(N²) build time on edits.

### The fix

Use Roslyn's `IIncrementalGenerator` pipeline correctly, with two distinct providers and a `.Combine()` to merge them. The signature parse is a separate pipeline stage from the full compile, so each has its own cache scope.

```csharp
namespace Hrot.Blueprints.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class BlueprintIncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Provider 1 — every .bp.json AdditionalFile, parsed into a raw text+path tuple.
        // Cache scope: per-file. Only re-runs for the file that actually changed.
        IncrementalValuesProvider<(string Path, string Text)> rawFiles =
            context.AdditionalTextsProvider
                .Where(at => at.Path.EndsWith(".bp.json", StringComparison.OrdinalIgnoreCase))
                .Select((at, ct) =>
                {
                    var text = at.GetText(ct)?.ToString() ?? "";
                    return (at.Path, text);
                });

        // Provider 2 — per-asset signature (name, AssetId, dispatch, callable exports).
        // Cache scope: per-file. Only re-runs when that file's text changes.
        IncrementalValuesProvider<BlueprintSignature> signatures =
            rawFiles.Select((rf, ct) => BlueprintSignatureParser.Parse(rf.Path, rf.Text));

        // Provider 3 — the collected sibling catalog.
        // Cache scope: changes only when SOME signature changes (not body-only edits).
        IncrementalValueProvider<ImmutableArray<BlueprintSignature>> siblingCatalog =
            signatures.Collect();

        // Provider 4 — per-asset full compile, paired with the global catalog.
        // Cache scope: re-runs only when (a) that file changed, or
        //              (b) the sibling catalog changed.
        IncrementalValuesProvider<CompileResult> compileResults =
            rawFiles.Combine(siblingCatalog)
                    .Select((pair, ct) =>
                    {
                        var (rawFile, siblings) = pair;
                        return CompileOneAsset(rawFile.Path, rawFile.Text, siblings, ct);
                    });

        // Register the compile results to produce generated source files
        context.RegisterSourceOutput(compileResults, (spc, result) =>
        {
            if (!result.Succeeded)
            {
                foreach (var diag in result.Diagnostics)
                    spc.ReportDiagnostic(diag.ToRoslyn());
                return;
            }
            spc.AddSource(result.GeneratedFileName, result.GeneratedSource!);
            // Note: debug map is NOT added as source; written to obj/ instead via
            // a separate mechanism (file system) since AddSource is C#-only.
        });
    }

    private static CompileResult CompileOneAsset(
        string path,
        string text,
        ImmutableArray<BlueprintSignature> siblings,
        CancellationToken ct)
    {
        var asset = BlueprintJsonServices.Deserialize(text);
        if (asset is null) return CompileResult.FailedParse(path);

        var compiler = new BlueprintCompiler();
        var options = new CompileOptions(
            Mode: CompilerMode.Release,
            NodeRegistry: BuiltInNodeRegistry.Instance,
            TypeRegistry: BuiltInTypeRegistry.Instance,
            EngineEvents: EngineEventCatalog.Instance,
            ChannelCommands: ChannelCommandCatalog.Instance,
            WaitPrimitives: WaitPrimitiveCatalog.Instance,
            // Sibling catalog from .Collect — but compiler sees a lightweight
            // BlueprintSignature, not the full BlueprintAsset. Peer references
            // resolve by AssetId + exported method name without re-parsing siblings.
            SiblingSignatures: siblings,
            EmitPdbWithEmbeddedSource: false);
        return compiler.Compile(asset, options);
    }
}

/// <summary>
/// Lightweight per-asset metadata. Holds just enough info for sibling-reference
/// resolution at compile time: identity, dispatch kind, exported callable methods.
/// Does NOT hold any graph/node/link data.
/// </summary>
public sealed record BlueprintSignature
{
    public string Path { get; init; } = "";
    public Guid AssetId { get; init; }
    public string Name { get; init; } = "";
    public string SanitizedName { get; init; } = "";
    public int BlueprintId { get; init; }
    public BlueprintDispatchKind Dispatch { get; init; }
    public IReadOnlyList<string> ExportedFunctionNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AiPrimitiveHosting> Hostings { get; init; } = Array.Empty<AiPrimitiveHosting>();
    public IReadOnlyList<Guid> DeclaredCallablePeers { get; init; } = Array.Empty<Guid>();
}
```

### What changes in `CompileOptions`

The `CompileOptions.SiblingAssets : IReadOnlyList<BlueprintAsset>` field, as documented in §1.2 of the main Compiler DD, is replaced by `SiblingSignatures : IReadOnlyList<BlueprintSignature>`. The validator's `V_PeerReferences` (Compiler DD §5.6) is updated to look up peer references in the signature catalog rather than the full asset list:

```csharp
internal sealed class V_PeerReferences : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        var siblingsById = ctx.SiblingSignatures.ToDictionary(s => s.AssetId);

        foreach (var graph in asset.Graphs)
            foreach (var node in graph.Nodes.OfType<CallPeerBlueprintNode>())
            {
                if (!asset.CallablePeers.Contains(node.TargetPeerAssetId))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1300,
                        $"CallPeerBlueprintNode targets asset {node.TargetPeerAssetId}, " +
                        "which is not in CallablePeers list.",
                        asset.AssetId, graph.Id, node.Id));
                    continue;
                }

                if (!siblingsById.TryGetValue(node.TargetPeerAssetId, out var peer))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1301,
                        $"CallablePeer {node.TargetPeerAssetId} not found among compiled assets.",
                        asset.AssetId, graph.Id, node.Id));
                    continue;
                }

                if (!peer.ExportedFunctionNames.Contains(node.TargetMethod))
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1302,
                        $"CallablePeer {peer.Name} has no function graph named " +
                        $"'{node.TargetMethod}'.",
                        asset.AssetId, graph.Id, node.Id));
            }
    }
}
```

### Why this is a real fix, not just a theoretical preference

A Slice 1 game with 50 Blueprints. Author edits one Blueprint's `Tick` body and saves.

- **Naive (broken)**: generator sees the AdditionalText change, but because compilation reads all 50 files to build the sibling list, Roslyn invalidates all 50 cached compile outputs and re-runs the generator 50 times. Build takes ~50× longer than necessary.
- **Correct (this patch)**: the signature provider for the edited file re-runs (because its source text changed). The signature catalog re-collects — but the *output* is byte-identical to before (signatures didn't change, only body). Roslyn observes the collected catalog is unchanged and *does not invalidate other assets' compile outputs*. Only the edited asset recompiles. Build is O(1) in number of unchanged sibling Blueprints.

Editing a Blueprint's *exported signature* (e.g., adding a new function graph) invalidates the collected catalog, which fan-outs to recompile any asset that calls that signature. This is the right behavior — peer-callers should re-validate.

### What this means for the Slice 1 generator project

`Hrot.Blueprints.Generators` must:
1. Include `BlueprintSignatureParser` — a lightweight JSON parser that extracts only the signature fields from a `.bp.json`, ignoring graphs/nodes/links. Avoids paying the full deserialization cost during signature-pass.
2. Cache the parsed signature value — it's a record, so structural equality is automatic; if two signature parses produce equal records, Roslyn skips downstream recompilation.
3. Ensure `BlueprintSignature` and all its constituent types implement structural equality (record types do this automatically).
4. Avoid mutable static state in the generator (Roslyn analyzers can be instantiated multiple times per process and shared across compilations).

---

## Patch 2 — `MetadataReferenceResolver` must filter dynamic/locationless assemblies (corrects §11.3)

### The problem

§11.3 of the Compiler DD has:

```csharp
public static MetadataReferenceResolver ForRuntimeAssemblies(IEnumerable<Assembly> assemblies)
{
    var refs = assemblies
        .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
        .Select(a => MetadataReference.CreateFromFile(a.Location))
        .ToList<MetadataReference>();
    return new MetadataReferenceResolver(refs);
}
```

The filter is already correct as written in the main DD. **However**, §11.4 of the main DD describes the editor's Quick Reload using "references from `AppDomain.CurrentDomain.GetAssemblies()`" without showing the filter, which created ambiguity. The architect explicitly called out that the filter is mandatory in the Quick Reload use case.

### The fix

Make the filter rule explicit in the Compiler DD by adding it to §11.4 as well, and document why:

When the editor's Quick Reload path supplies metadata references to the in-memory Roslyn compiler, it MUST filter assemblies that have no on-disk location:

```csharp
// In the editor's Quick Reload path:
var refs = AppDomain.CurrentDomain.GetAssemblies()
    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
    .Select(a => MetadataReference.CreateFromFile(a.Location))
    .ToList<MetadataReference>();
var resolver = new MetadataReferenceResolver(refs);
```

**Why both predicates are required:**

| Predicate | Catches |
|---|---|
| `!a.IsDynamic` | Reflection.Emit-generated assemblies (no PE backing on disk) |
| `!string.IsNullOrEmpty(a.Location)` | Assemblies loaded via `LoadFromStream` — including all previously hot-reloaded patch ALCs |

A patch ALC loaded via `alc.LoadFromStream(peStream, pdbStream)` has `Assembly.Location == ""` (empty string). It is not `IsDynamic` (it has a real PE image, just not from disk). So `!a.IsDynamic` alone does not catch it. Without `!string.IsNullOrEmpty(a.Location)`, the second Quick Reload after a previous one will pick up the first reload's patch assembly, call `MetadataReference.CreateFromFile("")`, and crash with `ArgumentException: 'path is empty'`.

### Symptoms if forgotten

- First Quick Reload of a session: works.
- Second Quick Reload: works (because the patch from #1 is now disposed/unloaded, no longer in `GetAssemblies()`).
- Quick Reload of asset B while asset A's patch ALC is still live: crash on second reference enumeration.

Test:

```csharp
[Fact]
public void ForRuntimeAssemblies_WithDynamicAssemblies_FiltersThem()
{
    var dynAssembly = new System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("Dynamic1"), AssemblyBuilderAccess.Run);

    var all = AppDomain.CurrentDomain.GetAssemblies();
    var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(all);

    // Should not have thrown; references should not include the dynamic assembly
    Assert.NotEmpty(resolver.Resolve());
}

[Fact]
public void ForRuntimeAssemblies_WithInMemoryAlcAssembly_FiltersIt()
{
    // Compile a small assembly into a patch ALC
    var (pe, pdb) = MinimalCompile("public class X { }");
    var alc = new AssemblyLoadContext("patch", isCollectible: true);
    var patchAsm = alc.LoadFromStream(new MemoryStream(pe), new MemoryStream(pdb));

    Assert.Equal("", patchAsm.Location);  // confirms the prerequisite

    var all = AppDomain.CurrentDomain.GetAssemblies();
    var resolver = MetadataReferenceResolver.ForRuntimeAssemblies(all);

    // Must not throw; must not include the in-memory patch
    var refs = resolver.Resolve();
    Assert.DoesNotContain(refs, r => r.Display == "X.dll");  // or whatever the patch was named
}
```

Both belong in `Stage8_RoslynTests/MetadataReferenceResolverTests.cs`.

---

## Resolution for Q-18.1 — InstanceVersion capture (Compiler DD §18.1)

### Decision

The Instance dispatch `Tick` method gains a `uint instanceVersion` parameter at the end:

```csharp
public static void Tick(
    ref State s,
    ISimulationView view,
    IEntityCommandBuffer ecb,
    Entity self,
    float time,
    float deltaTime,
    uint instanceVersion)
{
    // ...
}
```

The `TickDelegate` in `BlueprintRegistry.BlueprintDefinition` (Runtime DD will own this) also takes the extra parameter:

```csharp
public delegate void TickDelegate(
    Span<byte> stateBytes,
    ISimulationView view,
    IEntityCommandBuffer ecb,
    Entity self,
    float time,
    float deltaTime,
    uint instanceVersion);
```

`BlueprintTickSystem`, when iterating slots, reads `slot.InstanceVersion` from the slot table entry and passes it to the delegate. The generated `IrOp_CheckCursorVersion` lowers to:

```csharp
if (s.Cursor.InstanceVersion != instanceVersion)
{
    s.Cursor.ResumeAt = 0;
    return;
}
```

at the start of every resume block. The generated initial-suspend code captures it:

```csharp
s.Cursor.ResumeAt = 1;
s.Cursor.InstanceVersion = instanceVersion;
s.Cursor.WaitUntilTime = time + 5.0f;
return;
```

### Why this resolution

The architect ruled against using `Unsafe.SubtractByteOffset` to look backwards from the State struct payload to the slot header. Reasons:

1. **Brittleness** — the partition allocator header layout could change in Slice 2 (different slot table format, different padding rules). Generated code that assumes a specific offset breaks silently.
2. **Verifiability** — the runtime must explicitly pass the value rather than the compiler emitting pointer arithmetic. Easier to audit, no `unsafe` blocks needed in Instance dispatch generated code.
3. **Zero overhead** — passing one `uint` by value is free; no allocation, no indirection.

### What changes in the Compiler DD

§3.4 `IrOperation` enum is extended to include an explicit projection of `instanceVersion`:

```csharp
public sealed record IrOp_ReadInstanceVersion : IrOperation;
```

Stage 6's `WaitLowering_Instance` uses this op:
- At suspend point: writes `s.Cursor.InstanceVersion = read_instance_version`.
- At resume point: emits the staleness check using `read_instance_version`.

§10.5 Instance emission template is amended — the `Tick` method signature is as shown above. The `TickThunk` thunk is also amended:

```csharp
private static void TickThunk(
    Span<byte> bytes, ISimulationView view, IEntityCommandBuffer ecb,
    Entity self, float time, float deltaTime, uint instanceVersion)
{
    ref var s = ref Unsafe.As<byte, State>(ref MemoryMarshal.GetReference(bytes));
    Tick(ref s, view, ecb, self, time, deltaTime, instanceVersion);
}
```

§16 HealthRegen worked example needs its generated code updated to include the new parameter and to use the staleness check pattern at every resume label.

---

## Resolution for Q-18.3 — Custom event signatures

### Decision

`Event_<CustomName>` follows the same signature shape as engine events:

```csharp
public static void Event_<CustomName>(
    ref State s,
    ISimulationView view,
    IEntityCommandBuffer ecb,
    Entity self,
    float time,
    float deltaTime,
    // custom args from event declaration follow:
    {customArg1Type} {customArg1Name},
    ...)
{
    // body
}
```

`IrOp_RaiseCustomEvent` lowers to a direct call passing all of these arguments through. Because the call is synchronous and same-entity, the caller already has all the context.

### Why deltaTime is included

Custom events can contain ECS reads/writes (impure) and can contain latent nodes (Wait/Delay) which require `deltaTime` for correct frame-timing math. Including `deltaTime` keeps custom event handlers structurally interchangeable with engine event handlers, simplifying the emitter (no per-event signature variant).

The `Tick` method already has `deltaTime` in scope, so the cost of passing it to `Event_<CustomName>` is zero.

### What changes in the Compiler DD

§10.5 Instance emission template — `Event_<EventName>` signature now includes `deltaTime` consistently:

```csharp
public static void Event_{EventName}(
    ref State s,
    ISimulationView view,
    IEntityCommandBuffer ecb,
    Entity self,
    float time,
    float deltaTime,                              // <-- added
    {AdditionalParamsFromCatalogOrCustomEventDecl})
```

The engine event poll loops in `Tick` pass `deltaTime` through:

```csharp
Event_{EventName}(ref s, view, ecb, self, time, deltaTime,
    __e.{Field1}, __e.{Field2}, ...);
```

`IrOp_RaiseCustomEvent` emission:

```csharp
case IrOp_RaiseCustomEvent op:
    var args = string.Join(", ", op.Args.Select(a => $"__t{a.Index}"));
    e.WriteLine($"Event_{e.Ctx.CustomEventName(op.CustomEventIndex)}" +
                $"(ref s, view, ecb, self, time, deltaTime, {args});");
    break;
```

(`deltaTime` is added to the standard prefix of arguments.)

§16 HealthRegen worked example — generated `Event_OnHit` signature is amended to include `deltaTime`. The body doesn't currently use it; that's fine.

---

## Resolution for Q-18.4 — Parameter struct name uniqueness

### Decision

Confirmed safe. No collision concern. FDP engine's `BTreeActionGenerator` and `HsmActionGenerator` reflect over Param DTO types by Fully Qualified Type Name (FQTN). Each Blueprint compiles into a uniquely-named static class (because `BlueprintId` is hashed into the class name as `_{X8}`), so nested `Params` structs live at distinct FQTNs:

```
Hrot.AI.Behaviors.Generated.MoveToAndFire_A1B2C3D4_Bp.Params
Hrot.AI.Behaviors.Generated.HasVisibleTarget_C7145A20_Bp.Params
Hrot.AI.Behaviors.Generated.AttackPattern_E891FF40_Bp.Params
```

The `{X8}` BlueprintId hex suffix is collision-resistant (FNV-1a 32-bit over the asset Guid). If two Blueprints accidentally produce the same BlueprintId, the registration step in the Runtime will detect and reject the collision with a diagnostic.

### What changes in the Compiler DD

Nothing structural. Add a clarifying note in §10.4 (AiPrimitive emission template) and §10.5 (Instance emission template) reminding the implementer that the class name *must* include the `{BlueprintId:X8}` suffix in production output. The Sanitizer (§10.10) already does this via `GeneratedFileName` and the class-name generation should match.

The implementation note: `SanitizedName_Bp` was used as shorthand in the Compiler DD's templates; the actual emitted class name is `SanitizedName_{BlueprintId:X8}_Bp`. The Compiler DD's prose was loose on this; tighten in the consolidation pass.

---

## Resolution for Q-18.7 — Sibling-asset resolution

Resolved by Patch 1. The two-pass model is correct in principle; the implementation must use `IIncrementalGenerator` providers (signature provider + collected catalog + combined per-asset compile) rather than re-parsing all files in a single callback. No separate resolution needed.

---

## Patches summary

| Patch | Affects in Compiler DD | Effort |
|---|---|---|
| 1: IIncrementalGenerator pipeline shape | §18.7 resolved; affects `Hrot.Blueprints.Generators` implementation (M4 onward) | Small; pattern documented above |
| 2: Filter dynamic & locationless assemblies | §11.3 (already correct in code sample); §11.4 prose tightened | Test added to M12 acceptance |
| Q-18.1: `instanceVersion` parameter | §3.4 IrOperation; §10.5 template; §16 worked example | Cascades to Runtime DD `TickDelegate` shape |
| Q-18.3: Custom event signature | §10.5 template; statement emitter for `IrOp_RaiseCustomEvent` | Trivial |
| Q-18.4: Params struct names | §10.4 / §10.5 prose clarification | None structural |
| Q-18.7: Two-pass resolution | Resolved by Patch 1 | — |

The Compiler DD is otherwise architect-approved. With these patches the document is the implementable specification for M3-M7.

Remaining Q-18.x items (18.2 engine event field ordering, 18.5 EmissionContext threading, 18.6 Guid migration, 18.8 diagnostic format) are non-architectural and resolved at implementation time during M3/M5 and via the Editor DD respectively.

---

*End of Compiler DD inline patches. Next document: Runtime Detailed Design.*
