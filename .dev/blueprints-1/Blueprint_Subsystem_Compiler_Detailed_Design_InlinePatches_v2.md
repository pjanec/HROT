# Blueprint Subsystem — Compiler Detailed Design — Inline Patches v2

> **Status:** Cascading patches to `Blueprint_Subsystem_Compiler_Detailed_Design.md` derived from Hot Reload DD Inline Patches Patch 2 (`HsmActionDispatcher` is a static class).
> **Effect:** Updates AiPrimitive registrar emission template (§10.4) and the MoveToAndFire worked example (§15.8) to drop the `HsmActionDispatcher` parameter and use static calls.
> **Reads alongside:** Compiler DD + Compiler DD Inline Patches v1; supersedes the AiPrimitive registrar shape in those documents.

---

## Patch C1 — AiPrimitive registrar emits 2-parameter signature with static HSM call

### What changes

The AiPrimitive emission template in Compiler DD §10.4 was specified with a 3-parameter `Register` method:

```csharp
public static void Register(
    BlueprintRegistry registry,                  // ← was BlueprintRegistry, should be BlueprintRegistryStaging
    BehaviorRegistry behReg,
    HsmActionDispatcher hsmDispatcher)           // ← REMOVE
```

The Hot Reload DD v1 patches established that `HsmActionDispatcher` is a `public static unsafe class` in the FastHSM kernel — cannot be injected as a parameter. The Hot Reload DD also forbids `BlueprintRegistry` as a registrar parameter (only `BlueprintRegistryStaging` is allowed).

So the AiPrimitive emission template's correct shape is:

```csharp
public static void Register(
    BlueprintRegistryStaging staging,
    BehaviorRegistry behReg)
{
    staging.Add(MoveToAndFire_Bp.BlueprintId, new BlueprintDefinition { /* ... */ });
    behReg.RegisterAction("MoveToAndFire_Bp", MoveToAndFire_Bp.BTreeTick);

    // Static call — no parameter, no instance reference
    HsmActionDispatcher.RegisterAction(
        MoveToAndFire_Bp.BlueprintId,
        (IntPtr)(delegate* unmanaged<void*, void*, HsmCommandWriter*, void>)
            &MoveToAndFire_Bp.HsmActivity);
}
```

### Updated emission rule (§10.4)

In the emitter's per-asset code path, the registrar class generation logic must follow these rules based on dispatch kind and hostings:

| Asset | Required parameters | Static calls |
|---|---|---|
| Library | `BlueprintRegistryStaging staging` | — |
| AiPrimitive with no BTree hostings, no HSM hostings | `BlueprintRegistryStaging staging` | — |
| AiPrimitive with BTree hostings only | `BlueprintRegistryStaging staging, BehaviorRegistry behReg` | — |
| AiPrimitive with HSM hostings only | `BlueprintRegistryStaging staging` | `HsmActionDispatcher.RegisterAction(...)` |
| AiPrimitive with both BTree and HSM hostings | `BlueprintRegistryStaging staging, BehaviorRegistry behReg` | `HsmActionDispatcher.RegisterAction(...)` |
| Instance | `BlueprintRegistryStaging staging` | — |

The parameter list is emitted dynamically based on whether any BTree-style hosting (`BTreeAction`, `BTreeCondition`) is declared. The static `HsmActionDispatcher.RegisterAction` call is emitted only if any HSM-style hosting (`HsmAction`, `HsmGuard`) is declared.

### Emitter pseudocode change

```csharp
// In the AiPrimitive registrar emitter:
bool needsBehReg = hostings.Any(h => h is AiPrimitiveHosting.BTreeAction or AiPrimitiveHosting.BTreeCondition);
bool needsHsmCalls = hostings.Any(h => h is AiPrimitiveHosting.HsmAction or AiPrimitiveHosting.HsmGuard);

var paramList = new List<string> { "BlueprintRegistryStaging staging" };
if (needsBehReg) paramList.Add("BehaviorRegistry behReg");

var paramSig = string.Join(", ", paramList);

emit.WriteLine($"public static void Register({paramSig})");
emit.WriteLine("{");
emit.WriteLine($"    staging.Add({className}.BlueprintId, new BlueprintDefinition {{ /* ... */ }});");

if (hostings.Contains(AiPrimitiveHosting.BTreeAction))
    emit.WriteLine($"    behReg.RegisterAction(\"{className}\", {className}.BTreeTick);");
if (hostings.Contains(AiPrimitiveHosting.BTreeCondition))
    emit.WriteLine($"    behReg.RegisterCondition(\"{className}\", {className}.BTreeEvaluate);");

if (hostings.Contains(AiPrimitiveHosting.HsmAction))
{
    emit.WriteLine($"    HsmActionDispatcher.RegisterAction(");
    emit.WriteLine($"        {className}.BlueprintId,");
    emit.WriteLine($"        (IntPtr)(delegate* unmanaged<void*, void*, HsmCommandWriter*, void>)");
    emit.WriteLine($"            &{className}.HsmActivity);");
}
if (hostings.Contains(AiPrimitiveHosting.HsmGuard))
{
    emit.WriteLine($"    HsmActionDispatcher.RegisterGuard(");
    emit.WriteLine($"        {className}.BlueprintId,");
    emit.WriteLine($"        (IntPtr)(delegate* unmanaged<void*, void*, ushort, bool>)");
    emit.WriteLine($"            &{className}.HsmGuard);");
}

emit.WriteLine("}");
```

### Library and Instance registrars also use Staging

For consistency, **all** dispatch kinds emit registrars that take `BlueprintRegistryStaging staging` — never `BlueprintRegistry registry`. The original Compiler DD §10.3 (Library) showed `BlueprintRegistry`; that was incorrect under the Hot Reload DD's strict-injector rule.

Updated Library registrar emission:

```csharp
[BlueprintRegistrar]
public static class BlueprintRegistrar_MathLib_A3F791D2_Bp
{
    public static void Register(BlueprintRegistryStaging staging)
    {
        staging.Add(MathLib_Bp.BlueprintId, new BlueprintDefinition
        {
            Name = "MathLib",
            Kind = BlueprintDispatchKind.Library,
            StructureHash = 0,
            StateSize = 0,
        });
    }
}
```

Updated Instance registrar emission (from Compiler DD §16.2):

```csharp
[BlueprintRegistrar]
public static class BlueprintRegistrar_HealthRegen_B2C3D4E5_Bp
{
    public static void Register(BlueprintRegistryStaging staging)
    {
        staging.Add(HealthRegen_Bp.BlueprintId, new BlueprintDefinition
        {
            Name = "HealthRegen",
            Kind = BlueprintDispatchKind.Instance,
            StructureHash = HealthRegen_Bp.StructureHash,
            StateSize = HealthRegen_Bp.StateSize,
            StateClrType = typeof(HealthRegen_Bp.State),
            InitDefault = HealthRegen_Bp.InitDefault,
            Tick = HealthRegen_Bp.TickThunk,
            EventHandlers = new Dictionary<string, EventHandlerDelegate>
            {
                ["BeginPlay"] = HealthRegen_Bp.BeginPlayThunk,
                ["OnHit"]     = HealthRegen_Bp.OnHitThunk,
            },
        });
    }
}
```

(Was `BlueprintRegistry registry` and `registry.RegisterInstance(...)` in the original; now `BlueprintRegistryStaging staging` and `staging.Add(...)`.)

The `staging.Add` method signature is per Runtime DD §2.6 — it takes `(int blueprintId, BlueprintDefinition def)` and throws on collision. The emitter's job is just to construct the `BlueprintDefinition` and call `staging.Add`.

### Updated worked example (§15.8)

The MoveToAndFire generated code in Compiler DD §15.8 ended with this registrar:

```csharp
// Was (per main Compiler DD §15.8):
[BlueprintRegistrar]
public static unsafe class BlueprintRegistrar_MoveToAndFire_A1B2C3D4_Bp
{
    public static void Register(
        BlueprintRegistry registry,
        BehaviorRegistry behReg,
        HsmActionDispatcher hsmDispatcher)
    {
        behReg.RegisterAction("MoveToAndFire_Bp", MoveToAndFire_Bp.BTreeTick);
        hsmDispatcher.RegisterAction(
            MoveToAndFire_Bp.BlueprintId,
            (IntPtr)(delegate* unmanaged<void*, void*, HsmCommandWriter*, void>)
                &MoveToAndFire_Bp.HsmActivity);
        registry.RegisterAiPrimitive(MoveToAndFire_Bp.BlueprintId, new BlueprintDefinition
        {
            Name = "MoveToAndFire",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = MoveToAndFire_Bp.StructureHash,
            StateSize = 0,
        });
    }
}
```

Replace with:

```csharp
[BlueprintRegistrar]
public static unsafe class BlueprintRegistrar_MoveToAndFire_A1B2C3D4_Bp
{
    public static void Register(
        BlueprintRegistryStaging staging,
        BehaviorRegistry behReg)
    {
        staging.Add(MoveToAndFire_Bp.BlueprintId, new BlueprintDefinition
        {
            Name = "MoveToAndFire",
            Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = MoveToAndFire_Bp.StructureHash,
            StateSize = 0,
        });
        behReg.RegisterAction("MoveToAndFire_Bp", MoveToAndFire_Bp.BTreeTick);
        HsmActionDispatcher.RegisterAction(
            MoveToAndFire_Bp.BlueprintId,
            (IntPtr)(delegate* unmanaged<void*, void*, HsmCommandWriter*, void>)
                &MoveToAndFire_Bp.HsmActivity);
    }
}
```

Three differences:
1. `BlueprintRegistry registry` → `BlueprintRegistryStaging staging`.
2. `registry.RegisterAiPrimitive(...)` → `staging.Add(...)`.
3. `hsmDispatcher.RegisterAction(...)` parameter call → `HsmActionDispatcher.RegisterAction(...)` static call.
4. The `HsmActionDispatcher hsmDispatcher` parameter is dropped from the method signature.

Also note the ordering: the original had `behReg.RegisterAction` first, then `hsmDispatcher.RegisterAction`, then `registry.RegisterAiPrimitive`. The new shape leads with `staging.Add` because that's the definition itself; BTree and HSM hostings follow. This is purely cosmetic for readability — no semantic difference, since none of the three calls observe each other's results.

### Updated golden snapshots

The `Hrot.Blueprints.Tests/Snapshots/Emit/MoveToAndFire.cs.txt` golden file (per Compiler DD §17.6) must be regenerated to match the new registrar shape. Same for any other AiPrimitive sample's emit snapshot. Re-run with `BLUEPRINT_REGENERATE_SNAPSHOTS=1` after the emitter change.

### Determinism note

The emit ordering (staging.Add first, then BTree calls, then HSM static calls) is part of the determinism contract per Compiler DD §12. The emitter must produce the same output bytes given the same input, so the ordering is fixed by the emitter logic, not by the asset's authoring.

---

## Patch C2 — `BlueprintDefinition` field count for Instance dispatch

While reviewing the cascading changes from Patch C1, I noticed the Instance dispatch `BlueprintDefinition` construction in §16.2 references several fields (`InitDefault`, `Tick`, `EventHandlers`, `StateClrType`) that Compiler DD §16.2 mentioned without fully spelling out which delegate types they reference.

These all align with **Runtime DD §3.2** (the `BlueprintDefinition` record shape) and **Runtime DD Inline Patches Q-12.1** (which finalized the `TickDelegate` signature as including `uint instanceVersion`). No new emit work is needed; this is just a cross-reference reminder for the implementation agent that the emitted delegates must match Runtime DD's exact signatures:

```csharp
public delegate void InitDefaultDelegate(Span<byte> stateBytes);

public delegate void TickDelegate(
    Span<byte> stateBytes,
    ISimulationView view,
    IEntityCommandBuffer ecb,
    Entity self,
    float time,
    float deltaTime,
    uint instanceVersion);                              // per Compiler DD Patch v1 Q-18.1

public delegate void EventHandlerDelegate(
    Span<byte> stateBytes,
    ISimulationView view,
    IEntityCommandBuffer ecb,
    Entity self,
    float time,
    float deltaTime,                                    // per Compiler DD Patch v1 Q-18.3
    ReadOnlySpan<byte> payload);
```

The generated `TickThunk` for HealthRegen must match these signatures exactly. The Compiler DD §16.2 example shows the signature correctly; this patch just affirms.

---

## Patches summary

| Patch | Affects in Compiler DD | Change |
|---|---|---|
| C1 (Library, AiPrimitive, Instance registrars) | §10.3, §10.4, §15.8, §16.2 | All registrars take `BlueprintRegistryStaging staging` (never `BlueprintRegistry`). AiPrimitive drops `HsmActionDispatcher` parameter; calls `HsmActionDispatcher.RegisterAction(...)` statically. Library and Instance use `staging.Add(...)` not direct registry methods. |
| C2 (cross-reference) | §3.2 / §16.2 delegate signatures | Confirms generated thunk signatures must include `uint instanceVersion` (TickDelegate) and `float deltaTime` (EventHandlerDelegate) per Compiler DD Patch v1. |

### Effect on implementation

Roughly:
- **Compiler implementation** (M3-M7): the emit template logic for registrars becomes a few lines shorter and clearer. The `RegisterAll` boilerplate that was hand-shown in some examples is consolidated under "always emit a `[BlueprintRegistrar]` class with a static `Register(BlueprintRegistryStaging, [BehaviorRegistry]?)` method".
- **Hot Reload coordinator implementation** (M11): unchanged from Hot Reload DD Inline Patches.
- **Compiler tests**: all `Emit/*.cs.txt` golden snapshots need regenerating after the emit-template change. One-time cost.

### Effect on test data

Per Compiler DD §17.6, golden source snapshots live in `Hrot.Blueprints.Tests/Snapshots/Emit/`. Each snapshot file (`LibraryMath.cs.txt`, `MoveToAndFire.cs.txt`, `HealthRegen.cs.txt`, etc.) must be regenerated.

### No effect on

- `BlueprintRegistryStaging.Add` API — already specified in Runtime DD.
- Runtime systems (`BlueprintTickSystem`, `BlueprintMaintenanceSystem`) — they consume `BlueprintDefinition` from the committed registry, unaffected by registrar signatures.
- Test harness mock contracts — already specified in Test Harness DD + Inline Patches.
- The `HsmKernelBridge` / function pointer dispatch path in HSM-hosted AiPrimitives — generated thunk shape unchanged; only the registrar's call to register it.

---

*End of Compiler DD Inline Patches v2. The Compiler DD plus its v1 + v2 patches is now the implementable specification for M3-M7.*
