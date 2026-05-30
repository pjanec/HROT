# Blueprint Subsystem — Hot Reload Detailed Design — Inline Patches

> **Status:** Patches to `Blueprint_Subsystem_Hot_Reload_Detailed_Design.md` from architect's review.
> **Effect:** Three corrections (ALC field is main-thread-only; `HsmActionDispatcher` is a static class and not injected; Quick Reload must go through the coordinator), plus one strict rule (parameter injector throws on `BlueprintRegistry`).
> **Reads alongside:** the main Hot Reload DD; sections marked here supersede their counterparts in the main doc.

---

## Patch 1 — `_currentAlc` is main-thread-only (supersedes §3.3, §5.6, §6.2)

### The problem

The main DD had the background thread participate in `_currentAlc` bookkeeping — capturing the field's value into `PendingReload.OldAlc` at scan time. The architect identified this as a fatal concurrency flaw:

- If a reload fails mid-apply on the main thread, the background-captured `OldAlc` doesn't reflect the current truth.
- A subsequent successful reload would capture the *failed* ALC as its "old" reference.
- Unloading then either leaks the original live ALC or — worse — unloads it while still in use.

### The fix

`_currentAlc` is touched **only on the main thread**, and **only after a successful commit**. The background thread doesn't read it, doesn't write it, doesn't capture it.

#### Updated `PendingReload` record

```csharp
internal sealed class PendingReload
{
    public required AssemblyLoadContext NewAlc { get; init; }
    public required Assembly NewAssembly { get; init; }
    public required IReadOnlyList<ResolvedRegistrar> Registrars { get; init; }
    public DateTime LoadedAt { get; init; } = DateTime.UtcNow;

    // REMOVED: public AssemblyLoadContext? OldAlc { get; init; }
    // OldAlc capture would couple background thread to _currentAlc field.
    // Main-thread ApplyReload reads _currentAlc directly at commit time.
}
```

#### Updated background `LoadAndScan`

```csharp
private PendingReload LoadAndScan(string dllPath)
{
    var alc = new AssemblyLoadContext(
        name: $"AiBehaviors_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}",
        isCollectible: true);

    Assembly assembly = LoadAssemblyInto(alc, dllPath);
    var registrars = ScanForRegistrars(assembly);

    return new PendingReload
    {
        NewAlc = alc,
        NewAssembly = assembly,
        Registrars = registrars,
        // No OldAlc field — main thread owns _currentAlc exclusively.
    };
}
```

#### Updated main-thread `ApplyReload` (success path)

```csharp
private void ApplyReload(PendingReload pending)
{
    // Step 1: clear stale HSM function pointers
    HsmActionDispatcher.ClearAll();   // static call — see Patch 2

    // Step 2: begin staging
    var staging = _blueprintRegistry.BeginStaging();

    // Step 3: invoke registrars
    foreach (var registrar in pending.Registrars)
        InvokeRegistrar(registrar, staging);

    // Step 4: atomically commit
    _blueprintRegistry.CommitStaging(staging);

    // Step 5: ONLY NOW touch _currentAlc, on the main thread, after successful commit
    var oldAlc = _currentAlc;
    _currentAlc = pending.NewAlc;
    oldAlc?.Unload();
}
```

#### Updated main-thread `DrainPendingCallbacks` (failure path)

```csharp
public void DrainPendingCallbacks()
{
    if (!_pendingReloads.TryDequeue(out var pending)) return;

    try
    {
        ApplyReload(pending);
        OnReloadCompleted?.Invoke();
    }
    catch (Exception ex)
    {
        _options.Logger?.LogError(
            $"Hot reload apply failed: {ex.Message}. " +
            "Old code remains live; failed ALC will be unloaded. " +
            "Note: HSM dispatcher and BehaviorRegistry may have partial registrations " +
            "from this attempt; next successful reload restores consistency.",
            ex);

        OnReloadFailed?.Invoke(ex);

        // Unload ONLY the failed ALC. _currentAlc is untouched — still points to live code.
        try { pending.NewAlc.Unload(); }
        catch (Exception innerEx)
        {
            _options.Logger?.LogWarning(
                $"Failed to unload partially-applied ALC: {innerEx.Message}");
        }
    }
}
```

### Why this is robust against chained reloads

Scenario: Reload R1 succeeds, then R2 fails, then R3 succeeds.

| Event | Background does | Main thread does | `_currentAlc` after |
|---|---|---|---|
| Initial state | — | — | A (original) |
| R1 enqueued | enqueue ALC=B | — | A |
| R1 drained | — | apply succeeds; unload A | B |
| R2 enqueued | enqueue ALC=C | — | B |
| R2 drained | — | apply throws; unload C; **`_currentAlc` untouched** | B |
| R3 enqueued | enqueue ALC=D | — | B |
| R3 drained | — | apply succeeds; unload B | D |

At every step, `_currentAlc` reflects the *currently-live* ALC. Failed reloads never leave stale references. No ALC is ever unloaded while still in use, and no ALC is ever leaked (every ALC is eventually unloaded — either as the "failed" ALC immediately, or as the "previous live" ALC on the next success).

### Tests to add

```csharp
[Fact]
public void Reload_Failure_DoesNotMutateCurrentAlc()
{
    using var fixture = new BlueprintTestFixture();
    var v1 = TestData.LoadAsset("LibraryMath");
    fixture.CompileAndLoad(v1);

    var aliveAlcBefore = fixture.GetCurrentAlc();

    // Synthesize a failing reload (throwing registrar)
    var ex = Assert.Throws<HotReloadRegistrarException>(() =>
        fixture.SimulateReloadWithThrowingRegistrar());

    var aliveAlcAfter = fixture.GetCurrentAlc();

    Assert.Same(aliveAlcBefore, aliveAlcAfter);
    Assert.True(fixture.Registry.TryGetByName("LibraryMath", out _));
}

[Fact]
public void Reload_FailureThenSuccess_LiveCodeNeverInterrupted()
{
    using var fixture = new BlueprintTestFixture();
    var v1 = TestData.LoadAsset("LibraryMath");
    fixture.CompileAndLoad(v1);

    var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());
    Assert.NotNull(ex);

    // Original code still runs
    Assert.True(fixture.Registry.TryGetByName("LibraryMath", out var def));
    Assert.Equal(BlueprintDispatchKind.Library, def.Kind);

    // Now a successful reload — should swap cleanly
    var v2 = TestData.LoadAsset("LibraryMathV2");
    fixture.SimulateReload(new[] { v2 });

    Assert.True(fixture.Registry.TryGetByName("LibraryMathV2", out _));

    fixture.ForceGcReclaim();
    // The first live ALC should now be reclaimed; the failed one too.
}
```

---

## Patch 2 — `HsmActionDispatcher` is a static class (supersedes §4.5, §5, §11.1)

### The problem

The main DD treated `HsmActionDispatcher` as an injectable instance — included in the coordinator constructor, threaded through to generated AiPrimitive registrars as a parameter, and listed in the parameter-injection table.

The architect's correction: `HsmActionDispatcher` is `public static unsafe class` in the FastHSM kernel. It cannot be instantiated, injected, or passed as a parameter. Generated code calls into it statically.

### The fix

#### Updated generated AiPrimitive registrar shape

```csharp
// Was (per main DD §4.5):
[BlueprintRegistrar]
public static unsafe class BlueprintRegistrar_MoveToAndFire_A1B2C3D4_Bp
{
    public static void Register(
        BlueprintRegistryStaging staging,
        BehaviorRegistry behReg,
        HsmActionDispatcher hsmDispatcher)   // ← REMOVED
    {
        staging.Add(MoveToAndFire_Bp.BlueprintId, new BlueprintDefinition { /* ... */ });
        behReg.RegisterAction("MoveToAndFire_Bp", MoveToAndFire_Bp.BTreeTick);
        hsmDispatcher.RegisterAction(/* ... */);   // ← was instance call
    }
}

// Now:
[BlueprintRegistrar]
public static unsafe class BlueprintRegistrar_MoveToAndFire_A1B2C3D4_Bp
{
    public static void Register(
        BlueprintRegistryStaging staging,
        BehaviorRegistry behReg)
    {
        staging.Add(MoveToAndFire_Bp.BlueprintId, new BlueprintDefinition { /* ... */ });
        behReg.RegisterAction("MoveToAndFire_Bp", MoveToAndFire_Bp.BTreeTick);

        // Direct static call — no parameter, no instance
        HsmActionDispatcher.RegisterAction(
            MoveToAndFire_Bp.BlueprintId,
            (IntPtr)(delegate* unmanaged<void*, void*, HsmCommandWriter*, void>)
                &MoveToAndFire_Bp.HsmActivity);
    }
}
```

This cascades to the Compiler DD too — the emission template for AiPrimitive registrars (Compiler DD §10.4) must produce the two-parameter signature with a direct `HsmActionDispatcher.RegisterAction` static call rather than the three-parameter version with an instance call. **The Compiler DD will need a small inline patch reflecting this.**

#### Updated coordinator constructor

```csharp
// Was:
public AiHotReloadCoordinator(
    BehaviorRegistry behaviorRegistry,
    HsmActionDispatcher hsmDispatcher,   // ← REMOVED
    BlueprintRegistry blueprintRegistry,
    AiHotReloadCoordinatorOptions options)

// Now:
public AiHotReloadCoordinator(
    BehaviorRegistry behaviorRegistry,
    BlueprintRegistry blueprintRegistry,
    AiHotReloadCoordinatorOptions options)
{
    _behaviorRegistry = behaviorRegistry;
    _blueprintRegistry = blueprintRegistry;
    _options = options;
}
```

Drop the `_hsmDispatcher` field. The coordinator calls `HsmActionDispatcher.ClearAll()` statically:

```csharp
private void ApplyReload(PendingReload pending)
{
    HsmActionDispatcher.ClearAll();   // static call
    // ... rest unchanged
}
```

#### Updated `ResolveRegistrarArgument`

```csharp
private object ResolveRegistrarArgument(Type paramType, BlueprintRegistryStaging staging)
{
    if (paramType == typeof(BlueprintRegistryStaging))  return staging;
    if (paramType == typeof(BehaviorRegistry))          return _behaviorRegistry;

    // BlueprintRegistry is explicitly forbidden — see Patch 4
    if (paramType == typeof(BlueprintRegistry))
        throw new HotReloadRegistrarException(
            $"Registrar requests BlueprintRegistry, but only BlueprintRegistryStaging " +
            "may be injected. Direct registry access would violate the atomic RCU contract. " +
            "Change the registrar's signature to use BlueprintRegistryStaging.");

    // HsmActionDispatcher is static — never injected
    if (paramType == typeof(HsmActionDispatcher))
        throw new HotReloadRegistrarException(
            $"Registrar requests HsmActionDispatcher as a parameter, but it is a static " +
            "class. Call HsmActionDispatcher.RegisterAction statically from inside Register.");

    throw new HotReloadRegistrarException(
        $"Unknown registrar parameter type: {paramType.FullName}. " +
        "Supported types: BlueprintRegistryStaging, BehaviorRegistry.");
}
```

#### Updated parameter list in §3.2

```csharp
internal sealed record ResolvedRegistrar(
    Type DeclaringType,
    MethodInfo RegisterMethod,
    IReadOnlyList<RegistrarParameter> Parameters);

internal sealed record RegistrarParameter(
    string Name,
    Type ParameterType,
    int OrdinalIndex);
```

No change to the records themselves — but the `Parameters` list for any AiPrimitive registrar now has exactly 2 entries instead of 3.

#### Implication for Test Harness DD

Test Harness DD §5.1 (`InvokeAllRegistrars` → `InvokeRegistrarMethod`) was using the same three-parameter switch. The Test Harness DD's `Dispose` already calls `HsmDispatcher.ClearAll()` per Test Harness Inline Patches Q-12.1, so that part stays.

The fixture's `InvokeRegistrarMethod` simplifies:

```csharp
private void InvokeRegistrarMethod(MethodInfo method, BlueprintRegistryStaging staging)
{
    var parameters = method.GetParameters();
    var args = new object[parameters.Length];
    for (int i = 0; i < parameters.Length; i++)
    {
        args[i] = parameters[i].ParameterType switch
        {
            var t when t == typeof(BlueprintRegistryStaging) => staging,
            var t when t == typeof(BehaviorRegistry)         => BehaviorRegistry,
            // No HsmActionDispatcher case — static
            // No BlueprintRegistry case — forbidden
            _ => throw new InvalidOperationException(
                $"Unknown registrar parameter type: {parameters[i].ParameterType}")
        };
    }
    method.Invoke(null, args);
}
```

A small inline patch to Test Harness DD's §5.1 is needed to remove the `HsmActionDispatcher` switch case (or fold this into a Test Harness DD v2 patches doc; for the implementation agent's purposes, the rule is documented here).

### Effect on Q-11.1

The original Q-11.1 asked how to inject `BlueprintRegistry` into the coordinator constructor. With this patch:
- `BlueprintRegistry` injection remains needed (the coordinator owns it).
- `HsmActionDispatcher` injection is removed (static).
- One fewer field on the coordinator.

Net constructor: `(BehaviorRegistry, BlueprintRegistry, AiHotReloadCoordinatorOptions)`.

---

## Patch 3 — Quick Reload goes through the coordinator (supersedes §11.4)

### The problem

The main DD's Q-11.4 proposed extracting a `ReloadApplier` helper class so the editor's Quick Reload could apply reloads without going through the coordinator. The architect's correction: this creates an ownership split. The coordinator's `_currentAlc` tracks only reloads it personally handled. Quick Reload ALCs created outside this tracking become orphans the next time MSBuild fires — at which point the coordinator unloads its `_currentAlc` (an ancient ALC from before all the Quick Reloads), leaking the intervening Quick Reload ALCs permanently.

### The fix

The coordinator owns all ALCs for `Hrot.AI.Behaviors.dll`, regardless of whether they came from MSBuild or in-memory compilation. The editor's Quick Reload calls a public method on the coordinator on the main thread.

#### New coordinator public method

```csharp
public sealed class AiHotReloadCoordinator
{
    // Existing constructor + DrainPendingCallbacks unchanged from Patches 1 + 2

    /// <summary>
    /// Apply a Quick Reload that was prepared by the editor in-memory.
    /// Must be called on the main thread, outside the Simulation phase.
    /// The coordinator takes ownership of the ALC's lifecycle.
    /// </summary>
    /// <param name="newAlc">The patch ALC the editor created via InMemoryRoslynCompiler + LoadFromStream.</param>
    /// <param name="newAssembly">The assembly loaded into the patch ALC.</param>
    public void ApplyQuickReload(AssemblyLoadContext newAlc, Assembly newAssembly)
    {
        // Scan registrars (same logic as background path)
        var registrars = ScanForRegistrars(newAssembly);

        var pending = new PendingReload
        {
            NewAlc = newAlc,
            NewAssembly = newAssembly,
            Registrars = registrars,
        };

        // Apply directly — same path as DrainPendingCallbacks would take
        // for a file-watcher-driven reload, but synchronous.
        try
        {
            ApplyReload(pending);
            OnReloadCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            _options.Logger?.LogError(
                $"Quick Reload apply failed: {ex.Message}", ex);
            OnReloadFailed?.Invoke(ex);
            try { pending.NewAlc.Unload(); }
            catch (Exception innerEx)
            {
                _options.Logger?.LogWarning(
                    $"Failed to unload Quick Reload ALC after failure: {innerEx.Message}");
            }
            throw;   // Re-throw so the editor's UI can surface the error
        }
    }
}
```

#### Updated editor flow (cross-references Editor DD)

```csharp
// Inside the editor's Quick Reload command handler (main thread):
public void OnQuickReloadClicked()
{
    // 1. Compile in-memory
    var (peBytes, pdbBytes) = _inMemoryCompiler.Compile(
        source, virtualSourcePath, assemblyName, sink);

    // 2. Load into a fresh patch ALC
    var patchAlc = new AssemblyLoadContext(
        name: $"QuickReload_{Guid.NewGuid():N}",
        isCollectible: true);
    Assembly assembly;
    using (var peStream = new MemoryStream(peBytes))
    using (var pdbStream = new MemoryStream(pdbBytes))
        assembly = patchAlc.LoadFromStream(peStream, pdbStream);

    // 3. Hand off to the coordinator — it takes over from here
    _coordinator.ApplyQuickReload(patchAlc, assembly);

    // ApplyQuickReload either succeeds (coordinator updated _currentAlc,
    // unloaded the previous one) or threw (failed ALC already unloaded
    // by the coordinator, _currentAlc untouched).
}
```

The editor doesn't manage the patch ALC's lifecycle past the handoff. The coordinator owns it from the moment `ApplyQuickReload` is called.

### What this guarantees

| Event | `_currentAlc` after | Unloaded |
|---|---|---|
| Initial state | A (MSBuild-loaded) | — |
| Quick Reload 1 (via coordinator) | B | A |
| Quick Reload 2 (via coordinator) | C | B |
| MSBuild fires, background loads D | C (unchanged until drain) | — |
| `DrainPendingCallbacks` applies D | D | C |

The coordinator never leaks intermediate ALCs because it always knows the current one.

### Implication for §10 test strategy

The coordinator test cohort gains a new test:

```csharp
[Fact]
public void QuickReload_AfterPreviousQuickReload_UnloadsThePreviousQuickReloadAlc()
{
    using var fixture = new BlueprintTestFixture();
    var v1 = TestData.LoadAsset("LibraryMath");
    fixture.CompileAndLoad(v1);
    var alc1 = fixture.GetCurrentAlc();
    var alc1WeakRef = new WeakReference(alc1);

    // Quick Reload 1
    var v2 = TestData.LoadAsset("LibraryMathV2");
    fixture.SimulateQuickReload(v2);   // calls coordinator.ApplyQuickReload
    var alc2 = fixture.GetCurrentAlc();
    Assert.NotSame(alc1, alc2);

    fixture.ForceGcReclaim();
    Assert.False(alc1WeakRef.IsAlive, "First ALC should be reclaimed after Quick Reload");

    // Quick Reload 2
    var v3 = TestData.LoadAsset("LibraryMathV3");
    var alc2WeakRef = new WeakReference(alc2);
    fixture.SimulateQuickReload(v3);

    fixture.ForceGcReclaim();
    Assert.False(alc2WeakRef.IsAlive, "Second ALC should be reclaimed after second Quick Reload");
}
```

### Drop `ReloadApplier` from §11.4

The helper-class approach is dropped. All apply logic lives in `AiHotReloadCoordinator`. Both `DrainPendingCallbacks` (file-watcher-driven) and `ApplyQuickReload` (editor-driven) feed into the same `ApplyReload` private method, ensuring identical semantics.

---

## Patch 4 — `BlueprintRegistry` parameter injection forbidden (strengthens §4.6)

### The problem

Main DD §4.6 said:

> If a registrar accidentally takes `BlueprintRegistry` as a parameter (instead of `BlueprintRegistryStaging`), the coordinator still injects it — but the registrar's direct calls would mutate the *current* snapshot. That's a bug; the parameter-injection table is the policy enforcement.

This left a door open: "the table is the policy." Future generators or hand-written registrars could be tempted to use the live registry. The architect's correction: make it an explicit throw, not a policy.

### The fix

`ResolveRegistrarArgument` throws if asked for `BlueprintRegistry`. The code is already shown in Patch 2 above. Spelled out here for emphasis:

```csharp
if (paramType == typeof(BlueprintRegistry))
    throw new HotReloadRegistrarException(
        "Registrar requests BlueprintRegistry, but only BlueprintRegistryStaging " +
        "may be injected. Direct registry access would violate the atomic RCU contract. " +
        "Change the registrar's signature to use BlueprintRegistryStaging.");
```

### Updated §4.6 prose

The §4.6 text should be rewritten to:

> Registrars stage into a buffer rather than writing directly to the registry. Why:
>
> 1. **Atomicity.** If one registrar throws partway through registration, the half-staged buffer is discarded; the previous registry snapshot is unaffected.
> 2. **Snapshot construction.** `CommitStaging` builds a fresh immutable snapshot from the staging buffer. The registry's read path is then a single `Interlocked.Exchange` (Runtime DD §2.6).
> 3. **No partial visibility.** Reads from `BlueprintRegistry.TryGetById` during the staging window still see the old snapshot. Only `CommitStaging` makes the new contents visible.
>
> This is fundamental to the "no partial state visible to ticking" guarantee from §1.4.
>
> **Strict rule:** the parameter-injection table forbids `BlueprintRegistry` as a registrar parameter. Asking for it throws `HotReloadRegistrarException` at registrar invocation time. Only `BlueprintRegistryStaging` and `BehaviorRegistry` are valid registrar parameter types.

### Test

```csharp
[Fact]
public void ResolveRegistrarArgument_BlueprintRegistry_ThrowsExplicitly()
{
    using var fixture = new BlueprintTestFixture();
    var staging = fixture.Registry.BeginStaging();

    var ex = Assert.Throws<HotReloadRegistrarException>(() =>
        AiHotReloadCoordinator.ResolveRegistrarArgumentForTesting(
            typeof(BlueprintRegistry),
            staging,
            fixture.Registry,
            fixture.BehaviorRegistry));

    Assert.Contains("BlueprintRegistryStaging", ex.Message);
    Assert.Contains("RCU contract", ex.Message);
}
```

---

## Patches summary

| Patch | Affects in Hot Reload DD | Effect |
|---|---|---|
| 1: `_currentAlc` main-thread-only | §3 background-thread phase, §5.6 chained reloads, §6.2 mid-apply rollback | Background thread never touches `_currentAlc`. Failed reloads unload only `pending.NewAlc`, leaving `_currentAlc` untouched. No risk of unloading live code; no leak of original ALC. |
| 2: `HsmActionDispatcher` is static | §4.5 generated registrar shapes, §5 staging coordination, §11.1 coordinator constructor | AiPrimitive registrar signature becomes 2-param. Coordinator constructor drops `_hsmDispatcher` field. Parameter injection table loses the HSM case. `ClearAll` called statically. |
| 3: Quick Reload through coordinator | §11.4 ownership decision | `ReloadApplier` helper class dropped. New `coordinator.ApplyQuickReload(alc, assembly)` public method. Coordinator owns all ALC lifecycle regardless of source. |
| 4: `BlueprintRegistry` injection forbidden | §4.6 prose strengthening | Strict throw on `BlueprintRegistry` request; only `BlueprintRegistryStaging` and `BehaviorRegistry` valid. |

### Cascading effects on other DDs

- **Compiler DD §10.4** (AiPrimitive registrar emission template) — needs a small inline patch: drop the `HsmActionDispatcher hsmDispatcher` parameter, replace `hsmDispatcher.RegisterAction(...)` with the static `HsmActionDispatcher.RegisterAction(...)`. The Compiler DD's MoveToAndFire worked example (§15.8) should reflect this in its generated code listing. A separate `Blueprint_Subsystem_Compiler_Detailed_Design_InlinePatches_v2.md` will capture this; documenting here for cross-reference.
- **Runtime DD** — no change. `BlueprintRegistry` and `BlueprintRegistryStaging` already specified correctly.
- **Test Harness DD §5.1** — fixture's `InvokeRegistrarMethod` switch loses the `HsmActionDispatcher` case. Already shown above. The fixture continues to call `HsmDispatcher.ClearAll()` in `Dispose` (per Test Harness Inline Patches Q-12.1) — that call is also static, no field needed.

### Effect on implementation

The corrected design is *simpler*, not more complex:

- One less field on the coordinator (`_hsmDispatcher` gone).
- One less parameter on every AiPrimitive registrar (`hsmDispatcher` gone).
- One less case in the parameter injection switch.
- No `ReloadApplier` helper class to maintain.
- All ALC ownership in one place.
- No subtle background/main thread coupling on `_currentAlc`.

The architecture is more robust, the surface area smaller, the invariants easier to verify.

---

## What remains open in §11

After these patches:

- **Q-11.1** — Resolved: coordinator takes `(BehaviorRegistry, BlueprintRegistry, AiHotReloadCoordinatorOptions)`.
- **Q-11.2** — Cross-reference only (Editor DD + Debug Protocol DD); no Hot Reload decision.
- **Q-11.3** — HSM dispatcher snapshot for rollback: still deferred to Slice 2.
- **Q-11.4** — Resolved by Patch 3: Quick Reload goes through `coordinator.ApplyQuickReload`.
- **Q-11.5** — Logger interface: implementation-time confirmation against engine codebase.
- **Q-11.6** — `[InternalsVisibleTo]` for testability: standard pattern.
- **Q-11.7** — Frame-rate impact: confirmed acceptable.

All architecturally significant questions resolved. The Hot Reload DD plus this patches doc is the implementable specification for M11.

---

*End of Hot Reload DD inline patches. Compiler DD will gain a small v2 patches doc to reflect Patch 2's cascade. Next major document: Debug Protocol Detailed Design.*
