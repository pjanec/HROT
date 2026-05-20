# Blueprint Subsystem — Editor Detailed Design — Inline Patches

> **Status:** Patches to `Blueprint_Subsystem_Editor_Detailed_Design.md` from architect's review.
> **Effect:** Three corrections at the seams where the editor integrates with the compiler and hot-reload coordinator: sibling signatures must come from `.bp.json` parsing not the runtime registry; debug-map registration must distinguish Quick Reload from Full Rebuild origin; `ApplyQuickReload` signature corrected to match Hot Reload DD Patch 3.
> **Reads alongside:** the main Editor DD; sections marked here supersede their counterparts.

---

## Patch 1 — Sibling signatures from `IAssetCatalog`, not `BlueprintRegistry` (supersedes §10.5)

### The problem

§10.5 specified building `SiblingSignatures` from `BlueprintRegistry.GetAll()`. The registry holds `BlueprintDefinition` records — compiled runtime delegates and state-size metadata. The compiler's `BlueprintSignature` (per Compiler DD Inline Patches Patch 1) is an authoring-time projection containing fields the registry never sees: declared callable peers, exported function names, the asset's hostings list, the original asset Guid.

You cannot reconstruct authoring metadata from compiled runtime metadata. The information has been transformed and stripped.

Beyond correctness, this also defeats the incremental compile cache: the Compiler DD Patch 1 designed `BlueprintSignatureParser` as a separate cache scope so that body-only edits don't re-emit unchanged sibling signatures. The editor reaching past the parser into the registry would have circumvented that cache scope.

### The fix

The editor builds `SiblingSignatures` by running `BlueprintSignatureParser` over the `.bp.json` files discovered by `IAssetCatalog`, plus the in-memory authoring state of the edited asset itself.

### Updated `QuickReloadService.BuildSiblingSignatures`

```csharp
public sealed class QuickReloadService
{
    private readonly IAssetCatalog _catalog;
    private readonly BlueprintSignatureParser _signatureParser;

    // ... constructor injection ...

    private IReadOnlyList<BlueprintSignature> BuildSiblingSignatures(BlueprintAsset editedAsset)
    {
        var signatures = new List<BlueprintSignature>();

        // Walk all known .bp.json files via the catalog and parse signatures
        foreach (var entry in _catalog.EnumerateAll())
        {
            if (entry.AssetId == editedAsset.AssetId)
                continue;   // we'll add the edited asset's authoritative signature below

            try
            {
                // Lightweight parse — header + signature fields only, no graphs/nodes/links
                var json = File.ReadAllText(entry.Path);
                var sig = _signatureParser.Parse(entry.Path, json);
                signatures.Add(sig);
            }
            catch (Exception ex)
            {
                _output.LogWarning(
                    $"Failed to parse signature from {entry.Path}: {ex.Message}. " +
                    "Skipping; callable-peer references to this asset may fail to resolve.");
            }
        }

        // Add the edited asset's CURRENT in-memory signature (not the on-disk version,
        // which is stale until Save & Rebuild)
        signatures.Add(BlueprintSignatureBuilder.FromInMemoryAsset(editedAsset));

        return signatures;
    }
}
```

### `BlueprintSignatureBuilder.FromInMemoryAsset`

A small helper that projects a `BlueprintAsset` into the lightweight `BlueprintSignature` without going through JSON:

```csharp
public static class BlueprintSignatureBuilder
{
    public static BlueprintSignature FromInMemoryAsset(BlueprintAsset asset)
    {
        return new BlueprintSignature
        {
            Path = asset.EditorMetadata.SourcePath ?? "(in-memory)",
            AssetId = asset.AssetId,
            Name = asset.Name,
            SanitizedName = SanitizerHelper.ToCSharpIdentifier(asset.Name),
            BlueprintId = BlueprintIdHash.Compute(asset.AssetId),
            Dispatch = asset.Dispatch,
            ExportedFunctionNames = ExtractExportedFunctionNames(asset),
            Hostings = asset.Primitive?.Hostings.ToList() ?? new List<AiPrimitiveHosting>(),
            DeclaredCallablePeers = asset.CallablePeers.ToList(),
        };
    }

    private static IReadOnlyList<string> ExtractExportedFunctionNames(BlueprintAsset asset)
        => asset.Graphs
            .Where(g => g.Kind == GraphKind.Function)
            .Select(g => g.Name)
            .ToList();
}
```

This builder produces the *same* signature shape that `BlueprintSignatureParser` would produce if it parsed the asset's `.bp.json`. Critical: when the user later saves the asset to disk, the next compile (whether Quick Reload of a different asset or Full Rebuild) will produce a byte-identical signature record from `BlueprintSignatureParser.Parse(diskJson)`. Determinism preserved.

### `IAssetCatalog` becomes a dependency of `QuickReloadService`

The Editor DD §3.2 (DI registration) needs to inject `IAssetCatalog` and `BlueprintSignatureParser` into `QuickReloadService`:

```csharp
services.AddSingleton<BlueprintSignatureParser>();   // from Hrot.Blueprints.Generators

services.AddSingleton<QuickReloadService>(sp => new QuickReloadService(
    compiler: sp.GetRequiredService<IBlueprintCompiler>(),
    roslyn: sp.GetRequiredService<InMemoryRoslynCompiler>(),
    coordinator: sp.GetRequiredService<AiHotReloadCoordinator>(),
    debugSession: sp.GetRequiredService<IBlueprintDebugSession>(),
    dirtyTracker: sp.GetRequiredService<DirtyTracker>(),
    output: sp.GetRequiredService<IOutputConsole>(),
    catalog: sp.GetRequiredService<IAssetCatalog>(),                       // ← added
    signatureParser: sp.GetRequiredService<BlueprintSignatureParser>()));   // ← added
```

### What about catalog freshness?

`IAssetCatalog.EnumerateAll()` reads the file system every call. For Slice 1 this is fine — typical projects have ≤50 `.bp.json` files, and a header-only parse is fast (≤1ms per file).

For larger projects, the catalog could cache parsed signatures with a file-modification-time check. Slice 2 may add this; Slice 1's simple "always re-walk" is acceptable.

### What about Quick Reload of an asset that depends on another asset's signature change?

Example: the user adds a new function graph to asset A (changing its `ExportedFunctionNames`), then Quick Reloads asset B which calls into A. The editor parses A's `.bp.json` for B's compile — but A's on-disk version is the OLD version (A is dirty, unsaved).

This is a real edge case. Two valid approaches:

1. **Reject the Quick Reload with a diagnostic** — "Asset A is dirty and B depends on its signature. Save A first or Quick Reload them together."
2. **Use the dirty in-memory signature for A when reloading B** — extends `BuildSiblingSignatures` to check the dirty tracker.

Slice 1 picks **option 2** as the smoother UX:

```csharp
private IReadOnlyList<BlueprintSignature> BuildSiblingSignatures(BlueprintAsset editedAsset)
{
    var signatures = new List<BlueprintSignature>();
    var addedAssetIds = new HashSet<Guid> { editedAsset.AssetId };

    // 1. First pass — for any dirty asset, use the in-memory signature (not the on-disk version)
    foreach (var dirtyId in _dirtyTracker.DirtyAssets)
    {
        if (dirtyId == editedAsset.AssetId) continue;
        var dirty = _editorState.GetInMemoryAsset(dirtyId);   // may be null if dirty asset isn't currently loaded
        if (dirty is not null)
        {
            signatures.Add(BlueprintSignatureBuilder.FromInMemoryAsset(dirty));
            addedAssetIds.Add(dirtyId);
        }
    }

    // 2. Second pass — for non-dirty assets, parse on-disk .bp.json
    foreach (var entry in _catalog.EnumerateAll())
    {
        if (addedAssetIds.Contains(entry.AssetId)) continue;
        try
        {
            var json = File.ReadAllText(entry.Path);
            signatures.Add(_signatureParser.Parse(entry.Path, json));
        }
        catch (Exception ex)
        {
            _output.LogWarning($"Failed to parse signature from {entry.Path}: {ex.Message}.");
        }
    }

    // 3. The asset being reloaded — its in-memory authoring state
    signatures.Add(BlueprintSignatureBuilder.FromInMemoryAsset(editedAsset));

    return signatures;
}
```

This requires `EditorState` exposing `GetInMemoryAsset(Guid)` — basically a registry of currently-loaded-and-editable assets the editor maintains. Slice 1 ships with: "if it's dirty and currently in `EditorSelectionStore.SelectedAsset`, use in-memory; otherwise on-disk." More than that is over-engineering for Slice 1.

### Implication for testing

A new test pattern in `Editor/QuickReloadServiceTests.cs`:

```csharp
[Fact]
public async Task QuickReload_WithDirtySiblingAsset_UsesDirtyInMemorySignature()
{
    using var fixture = new BlueprintTestFixture();
    var service = MakeService(fixture);

    var assetA = TestData.LoadAsset("LibraryMath");
    await service.TriggerAsync(assetA, CompilerMode.Debug);

    // Edit assetA in-memory (dirty)
    var editedA = assetA with { /* add a function */ };
    fixture.DirtyTracker.MarkDirty(editedA.AssetId);
    fixture.EditorState.SetInMemoryAsset(editedA);

    // Quick Reload a different asset that calls assetA
    var assetB = TestData.LoadAsset("CallsMath");
    var result = await service.TriggerAsync(assetB, CompilerMode.Debug);

    Assert.True(result.Succeeded);
    // Verify signature catalog had the dirty in-memory version of A
    var capturedSignatures = service.LastSignaturesUsedForTesting;
    Assert.Contains(capturedSignatures, s => s.AssetId == assetA.AssetId
                                            && s.ExportedFunctionNames.Contains("MyNewFunction"));
}
```

---

## Patch 2 — Distinguish Quick Reload from Full Rebuild in `OnReloadCompleted` (supersedes §12.3, §12.4)

### The problem

§12.3 said: after Quick Reload, the editor registers the in-memory debug map directly with the session.

§12.4 said: when `coordinator.OnReloadCompleted` fires, the editor walks the DLL directory for `.dbgmap.json` files and registers each.

Hot Reload DD Patch 3 routed Quick Reload through `coordinator.ApplyQuickReload`, so `OnReloadCompleted` now fires after **every** reload — Quick or Full. The §12.4 handler then attempts to read `.dbgmap.json` files that don't exist on disk for Quick Reloads, either:
- Crashing with file-not-found, or
- Succeeding with a stale on-disk version that overwrites the fresh in-memory map just registered by §12.3.

### The fix

`OnReloadCompleted` carries a payload identifying the reload source. The editor's subscriber handles each origin differently.

### Updated event signature in `AiHotReloadCoordinator`

```csharp
namespace Fdp.Toolkit.Behavior;

public sealed class AiHotReloadCoordinator
{
    public event Action<ReloadCompletedInfo>? OnReloadCompleted;
    public event Action<Exception>? OnReloadFailed;

    private void FireReloadCompleted(ReloadSource source, AssemblyLoadContext newAlc, string? dllPath)
    {
        OnReloadCompleted?.Invoke(new ReloadCompletedInfo
        {
            Source = source,
            NewAlc = newAlc,
            DllPath = dllPath,    // null for Quick Reload (no on-disk DLL)
        });
    }
}

public sealed record ReloadCompletedInfo
{
    public required ReloadSource Source { get; init; }
    public required AssemblyLoadContext NewAlc { get; init; }
    public string? DllPath { get; init; }
}

public enum ReloadSource
{
    FullRebuildViaFileWatcher,    // MSBuild → file watcher → ApplyReload
    QuickReloadViaApi,             // editor → ApplyQuickReload
}
```

`ApplyReload` (file-watcher path) fires with `Source = FullRebuildViaFileWatcher` and the DLL path. `ApplyQuickReload` fires with `Source = QuickReloadViaApi` and `DllPath = null`.

### Updated editor subscriber

```csharp
public sealed class BlueprintEditorModule : IEditorModule
{
    private readonly IBlueprintDebugSession _debugSession;
    private readonly IBlueprintAssetIo _assetIo;
    private readonly IOutputConsole _output;
    private readonly AiHotReloadCoordinator _coordinator;
    private readonly QuickReloadDebugMapRegistry _quickReloadMaps;

    public void OnEditorActivated()
    {
        _coordinator.OnReloadCompleted += OnReloadCompleted;
    }

    private void OnReloadCompleted(ReloadCompletedInfo info)
    {
        switch (info.Source)
        {
            case ReloadSource.FullRebuildViaFileWatcher:
                HandleFullRebuildReload(info);
                break;

            case ReloadSource.QuickReloadViaApi:
                // Quick Reload's in-memory debug map was already registered by
                // QuickReloadService.TriggerAsync BEFORE calling ApplyQuickReload.
                // Nothing more to do here — explicitly do NOT read disk.
                _output.LogDebug($"Reload completed (Quick Reload); in-memory debug maps already registered.");
                break;
        }
    }

    private void HandleFullRebuildReload(ReloadCompletedInfo info)
    {
        if (info.DllPath is null)
        {
            _output.LogWarning("FullRebuildViaFileWatcher event without DllPath; skipping debug-map load.");
            return;
        }

        var dllDir = Path.GetDirectoryName(info.DllPath);
        if (dllDir is null || !Directory.Exists(dllDir)) return;

        int loaded = 0, failed = 0;
        foreach (var dbgmapPath in Directory.EnumerateFiles(dllDir, "*.dbgmap.json"))
        {
            try
            {
                var json = File.ReadAllText(dbgmapPath);
                var raw = JsonSerializer.Deserialize<BlueprintDebugMap>(json, _jsonOptions);
                if (raw is not null)
                {
                    _debugSession.RegisterDebugMap(raw.AssetId, raw);
                    loaded++;
                }
            }
            catch (Exception ex)
            {
                _output.LogWarning($"Failed to load debug map {dbgmapPath}: {ex.Message}");
                failed++;
            }
        }

        _output.LogInfo($"Reload completed (Full Rebuild); loaded {loaded} debug maps from disk" +
                       (failed > 0 ? $" ({failed} failed)" : ""));
    }
}
```

### Updated §12.3 — Quick Reload debug map registration

The QuickReloadService registers the debug map **before** calling `ApplyQuickReload`. This ordering matters: by the time `OnReloadCompleted` fires (synchronously inside `ApplyQuickReload`), the in-memory map is already live in the session.

```csharp
// In QuickReloadService.TriggerAsync, after Roslyn compile + ALC load succeed:

// Step A: register the in-memory debug map with the session BEFORE handing off
if (result.DebugMap is not null)
    _debugSession.RegisterDebugMap(asset.AssetId, result.DebugMap);

// Step B: now hand off to coordinator. It will fire OnReloadCompleted with
// Source = QuickReloadViaApi, which the editor's handler explicitly skips disk-reading for.
try
{
    _coordinator.ApplyQuickReload(/* see Patch 3 for signature */);
}
catch (Exception ex)
{
    // Apply failed — roll back the debug map registration
    _debugSession.UnregisterDebugMap(asset.AssetId);
    // ... rest of error handling ...
}
```

The `_debugSession.UnregisterDebugMap` removes the stale in-memory map if the coordinator handoff fails. (This method is added to `IBlueprintDebugSession`; small surface increase.)

### Why register before, not after

Two reasons:

1. **Consistency with `OnReloadCompleted` semantics.** When `OnReloadCompleted` fires, the session must already have the new debug map; otherwise other subscribers (e.g., the Hot Reload Log window inspecting which assets were reloaded) see inconsistent state.
2. **Avoiding race in subscribers' handlers.** If the registration were post-fire, a subscriber that queries `_debugSession.HasDebugMap(assetId)` would see false even though the reload "completed." Pre-fire registration keeps everything consistent.

The downside: if the coordinator's `ApplyQuickReload` fails, the debug map registration must be rolled back. The `UnregisterDebugMap` call handles this. Net code: ~3 lines.

---

## Patch 3 — `ApplyQuickReload` signature corrected (supersedes §10.1, §10.3)

### The problem

§10.3 (and §1.2) call `coordinator.ApplyQuickReload(alc, assembly)`. Hot Reload DD Patch 3 defined the signature differently:

```csharp
public void ApplyQuickReload(
    AssemblyLoadContext quickReloadAlc,
    BehaviorRegistry stagingRegistry)
```

The architect's intent (made explicit in the review): the editor's `QuickReloadService` is responsible for invoking the patch ALC's `[BlueprintRegistrar]` classes against a staging `BehaviorRegistry` (and the static `HsmActionDispatcher.RegisterAction` calls, which target the engine-wide static dispatcher). The editor hands the populated staging registry + the ALC to the coordinator, which performs the atomic `BlueprintRegistry` swap and commits the staging registry.

This split mirrors what `AiHotReloadCoordinator.ApplyReload` does internally for file-watcher-driven reloads: it scans for registrars, populates a `BlueprintRegistryStaging` (per Hot Reload DD Patch 1/2), then commits atomically. For Quick Reload, the editor does the registrar invocation but the coordinator still owns commit timing.

### The fix

Two collaborating parties:

- **Editor's `QuickReloadService`**: reflects over the patch ALC, finds all `[BlueprintRegistrar]` classes, invokes their `Register` methods with a fresh `BehaviorRegistry stagingRegistry` and `BlueprintRegistryStaging blueprintStaging` (since per Compiler DD Patch v2 registrars take these two parameters).
- **Coordinator's `ApplyQuickReload`**: receives the patch ALC + populated staging registries, performs atomic `BlueprintRegistry.CommitStaging` + swaps `BehaviorRegistry`'s contents.

Wait — `BehaviorRegistry` doesn't have an explicit staging API the way `BlueprintRegistry` does. Per Hot Reload DD §5.1, `BehaviorRegistry` uses direct-overwrite semantics: `RegisterAction(name, delegate)` replaces any entry with the same name. For Quick Reload, the editor invoking registrars on a fresh empty `BehaviorRegistry stagingRegistry` produces a complete set of new registrations; the coordinator then merges or swaps that into the live `BehaviorRegistry`.

The cleanest model: the editor passes the *staging* `BehaviorRegistry` directly; the coordinator swaps the live registry with the staging registry. But `BehaviorRegistry` isn't designed for swap — it's a singleton holding the engine's authoritative behavior catalog.

Re-reading Hot Reload DD Patch 3's intent: I think the architect's "stagingRegistry" is a thin staging buffer the coordinator copies into the live `BehaviorRegistry` atomically after `BlueprintRegistry.CommitStaging`. Same pattern as `BlueprintRegistryStaging.Add` → `BlueprintRegistry.CommitStaging`.

### Updated `ApplyQuickReload` signature

```csharp
public sealed class AiHotReloadCoordinator
{
    public void ApplyQuickReload(
        AssemblyLoadContext quickReloadAlc,
        BehaviorRegistry behaviorStaging,
        BlueprintRegistryStaging blueprintStaging)
    {
        // Verify the ALC's registrars were invoked into the staging registries
        // (the editor's QuickReloadService handles this; we just commit).

        try
        {
            // 1. Clear stale HSM function pointers (per Hot Reload DD Patch 1).
            //    The editor's registrar invocations already called HsmActionDispatcher.RegisterAction
            //    statically; ClearAll must happen BEFORE those calls, not after.
            //    Therefore the editor's QuickReloadService is responsible for calling
            //    HsmActionDispatcher.ClearAll() *before* invoking the registrars.
            //    See updated QuickReloadService below.

            // 2. Commit the BlueprintRegistry staging atomically
            _blueprintRegistry.CommitStaging(blueprintStaging);

            // 3. Apply the BehaviorRegistry staging — copy all entries into the live registry
            foreach (var (name, def) in behaviorStaging.GetAllEntries())
                _behaviorRegistry.RegisterAction(name, def);

            // 4. Update _currentAlc (main-thread-only, per Hot Reload DD Patch 1)
            var oldAlc = _currentAlc;
            _currentAlc = quickReloadAlc;

            // 5. Fire OnReloadCompleted with QuickReloadViaApi source (per Editor DD Patch 2)
            FireReloadCompleted(ReloadSource.QuickReloadViaApi, quickReloadAlc, dllPath: null);

            // 6. Unload old ALC
            oldAlc?.Unload();
        }
        catch (Exception ex)
        {
            _options.Logger?.LogError($"Quick Reload apply failed: {ex.Message}", ex);
            OnReloadFailed?.Invoke(ex);
            try { quickReloadAlc.Unload(); }
            catch (Exception innerEx)
            {
                _options.Logger?.LogWarning(
                    $"Failed to unload Quick Reload ALC after failure: {innerEx.Message}");
            }
            throw;
        }
    }
}
```

### Updated `QuickReloadService.TriggerAsync`

```csharp
public sealed class QuickReloadService
{
    public async Task<QuickReloadResult> TriggerAsync(BlueprintAsset asset, CompilerMode mode)
    {
        var sw = Stopwatch.StartNew();

        // 1. Compile (Stages 1-7)
        var siblings = BuildSiblingSignatures(asset);   // per Patch 1
        var compileOptions = new CompileOptions(/* ... */, SiblingSignatures: siblings, /* ... */);
        var result = _compiler.Compile(asset, compileOptions);
        if (!result.Succeeded)
        {
            foreach (var d in result.Diagnostics) _output.LogDiagnostic(d);
            return QuickReloadResult.CompileFailed(result.Diagnostics);
        }

        // 2. Roslyn finalize (Stage 8)
        var (peBytes, pdbBytes) = _roslyn.Compile(
            result.GeneratedSource!, result.GeneratedFileName,
            $"QuickReload_{Guid.NewGuid():N}", new EditorDiagnosticSink(_output));

        // 3. Load patch ALC
        var alc = new AssemblyLoadContext(
            name: $"QuickReload_{Guid.NewGuid():N}",
            isCollectible: true);
        Assembly assembly;
        try
        {
            using var pe = new MemoryStream(peBytes);
            using var pdb = new MemoryStream(pdbBytes);
            assembly = alc.LoadFromStream(pe, pdb);
        }
        catch (Exception ex)
        {
            try { alc.Unload(); } catch { /* swallow */ }
            return QuickReloadResult.LoadFailed(ex);
        }

        // 4. Invoke registrars into staging registries
        var behaviorStaging = new BehaviorRegistry();   // fresh empty staging
        var blueprintStaging = _coordinator.Registry.BeginStaging();   // request staging buffer
        try
        {
            // CRITICAL: clear HSM dispatcher BEFORE the registrars do their static RegisterAction calls
            HsmActionDispatcher.ClearAll();

            InvokeAllRegistrars(assembly, blueprintStaging, behaviorStaging);
        }
        catch (Exception ex)
        {
            try { alc.Unload(); } catch { /* swallow */ }
            return QuickReloadResult.RegistrarFailed(ex);
        }

        // 5. Register debug map BEFORE handoff (per Patch 2)
        if (result.DebugMap is not null)
            _debugSession.RegisterDebugMap(asset.AssetId, result.DebugMap);

        // 6. Hand off to coordinator for atomic commit + ALC swap
        try
        {
            _coordinator.ApplyQuickReload(alc, behaviorStaging, blueprintStaging);
        }
        catch (Exception ex)
        {
            // Roll back debug map registration
            _debugSession.UnregisterDebugMap(asset.AssetId);
            return QuickReloadResult.ApplyFailed(ex);
        }

        // 7. Mark asset clean
        _dirtyTracker.MarkClean(asset.AssetId);

        sw.Stop();
        return QuickReloadResult.Succeeded(sw.ElapsedMilliseconds);
    }

    private void InvokeAllRegistrars(
        Assembly assembly,
        BlueprintRegistryStaging blueprintStaging,
        BehaviorRegistry behaviorStaging)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<BlueprintRegistrarAttribute>() is null) continue;
            var registerMethod = type.GetMethod("Register",
                BindingFlags.Public | BindingFlags.Static);
            if (registerMethod is null) continue;

            var parameters = registerMethod.GetParameters();
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                args[i] = parameters[i].ParameterType switch
                {
                    var t when t == typeof(BlueprintRegistryStaging) => blueprintStaging,
                    var t when t == typeof(BehaviorRegistry)         => behaviorStaging,
                    _ => throw new InvalidOperationException(
                        $"Registrar {type.FullName}.Register has unsupported parameter type " +
                        $"{parameters[i].ParameterType.FullName}.")
                };
            }
            registerMethod.Invoke(null, args);
        }
    }
}
```

### Key ordering rules (recap)

| Step | Who does it | Why |
|---|---|---|
| `HsmActionDispatcher.ClearAll()` | `QuickReloadService` | Must happen before any registrar's static `RegisterAction` call, otherwise stale function pointers from the previous ALC's deleted Blueprints survive. |
| Invoke registrars into staging | `QuickReloadService` | Populates `BehaviorRegistry stagingRegistry` and `BlueprintRegistryStaging blueprintStaging` without touching live state. Static `HsmActionDispatcher.RegisterAction` calls land directly. |
| Register debug map | `QuickReloadService` | Must happen before `ApplyQuickReload` so that `OnReloadCompleted` subscribers see consistent state. |
| Commit `BlueprintRegistry` via `Interlocked.Exchange` | Coordinator | Atomic snapshot swap, lock-free for readers. |
| Commit `BehaviorRegistry` (copy from staging) | Coordinator | Single-thread, just dictionary writes. |
| Update `_currentAlc` field | Coordinator | Main-thread-only per Hot Reload DD Patch 1. |
| Fire `OnReloadCompleted` | Coordinator | After commits succeed. |
| Unload old ALC | Coordinator | Last; all references into it are now stale and can be reclaimed. |

The split of responsibilities reduces what the coordinator needs to know about registrars (it just iterates the staging buffer). The editor handles reflection + parameter injection because the editor is where the patch ALC originates.

### Implication for failure handling

If a registrar throws during step 4 (registrar invocation), the editor catches and unloads the patch ALC. The coordinator's `BlueprintRegistryStaging` is discarded (it's a local variable, not yet committed). The live `BehaviorRegistry` is unchanged. `HsmActionDispatcher` was `ClearAll`'d but no new entries were registered — same partial-state risk as in Hot Reload DD §6.5 for file-watcher reloads, with the same resolution: log, accept temporary HSM dysfunction, recover on next successful reload.

### What about the Hot Reload DD Patch 3 signature exactly

The architect's Hot Reload DD Patch 3 sketched `ApplyQuickReload(AssemblyLoadContext, BehaviorRegistry stagingRegistry)`. The version above adds a third parameter `BlueprintRegistryStaging blueprintStaging`. This is consistent with Hot Reload DD Patch 3's intent — the editor populates both staging containers because the registrars (per Compiler DD Patch v2) take both as parameters. The Hot Reload DD Patch 3 signature was a simplification that the implementation reality requires expanding by one parameter.

If the architect prefers the original two-parameter shape, the coordinator could call `BeginStaging()` internally and pass it back to the editor as an out-parameter — but that's a more awkward API. The three-parameter version is cleaner; the architect can amend Hot Reload DD Patch 3 if they prefer.

---

## Patches summary

| Patch | Affects in Editor DD | Change |
|---|---|---|
| 1: Sibling signatures from catalog | §10.5 + `QuickReloadService` deps + tests | Build `SiblingSignatures` via `BlueprintSignatureParser.Parse(.bp.json)` plus in-memory dirty-aware merging. Drop `BlueprintRegistry`-based reconstruction. |
| 2: Reload-source discriminator | §12.3 + §12.4 + coordinator event signature | `OnReloadCompleted` carries `ReloadCompletedInfo { Source, NewAlc, DllPath }`. Editor handler routes by source: in-memory map for Quick Reload, disk read for Full Rebuild. Debug map registration moves before coordinator handoff. |
| 3: Coordinator handoff signature | §1.2 + §10.1 + §10.3 + Hot Reload DD coordinator | `ApplyQuickReload(alc, behaviorStaging, blueprintStaging)`. Editor's `QuickReloadService` does reflection-driven registrar invocation, including `HsmActionDispatcher.ClearAll()` before the registrars run. Coordinator does atomic commits + ALC swap + event fire. |

### Cascading effects on other DDs

- **Hot Reload DD Patch 3 (§ApplyQuickReload)**: signature gains a `BlueprintRegistryStaging` third parameter. The Hot Reload DD's existing `ApplyReload` implementation already wraps `BlueprintRegistry.BeginStaging` internally for the file-watcher path; for the Quick Reload path the editor supplies the staging buffer. The coordinator's `OnReloadCompleted` event signature also changes to carry `ReloadCompletedInfo`. A small follow-up patch to the Hot Reload DD inline patches doc captures both adjustments.
- **Compiler DD Patch v2**: no change. The generated registrar shape already takes `(BlueprintRegistryStaging, BehaviorRegistry)`.
- **Runtime DD**: no change. `BlueprintRegistry.BeginStaging` / `CommitStaging` semantics are unchanged.
- **Test Harness DD**: the fixture's `SimulateReload` (per Test Harness DD §5.2) already invokes registrars into staging buffers via `InvokeAllRegistrars`; the helper just needs to mirror the editor's `QuickReloadService` pattern exactly. Small alignment patch noted in the implementation agent's M11 task list.
- **Debug Protocol DD**: `IBlueprintDebugSession` gains `UnregisterDebugMap(Guid assetId)`. One method addition; semantics obvious.

### Effect on implementation

Slice 1 implementation gets:
- Cleaner data flow at the editor↔compiler seam: signatures from `.bp.json`, not from a runtime registry that has lost the relevant fields.
- A robust origin discriminator on reload events, preventing the disk-stale overwrite race.
- An explicit responsibility split for Quick Reload: editor does reflection + ALC owns its own creation; coordinator does atomic commit + ALC swap timing. Mirrors file-watcher path's `ApplyReload` structure.

The editor's `QuickReloadService` is the most-touched class; the coordinator's `ApplyQuickReload` is the second-most. Both changes are local; no architectural reshuffling.

---

## What remains open in §16

All seven items from the original Editor DD §16 remain at their resolved state:

- **Q-16.1** — engine time-controller class name: implementation-time identification during M13.
- **Q-16.2** — MSBuild invocation style: `Process.Start("dotnet build")` for Slice 1, programmatic API for Slice 2.
- **Q-16.3** — asset catalog file-watcher: implementation detail of `FileSystemAssetCatalog`.
- **Q-16.4** — concurrent multi-view edits: Slice 2 scope.
- **Q-16.5** — editor frame-time perf gate: manual testing of Roadmap demos.
- **Q-16.6** — save dialog vs auto-save: manual confirmation only for Slice 1.
- **Q-16.7** — IDE handoff for `.cs` editing: no editor work; informational.

All architecturally significant questions resolved. The Editor DD plus this patches doc is the implementable specification for M13.

---

*End of Editor DD inline patches. All Slice 1 design phase artifacts complete.*
