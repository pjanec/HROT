# DEBT-007 — The HSM ECS Access Problem (Revised)

> **This document supersedes the previous version. The architect reviewed the analysis and
> confirmed the root cause, identified a fatal flaw in the originally-proposed fix, and
> specified the correct zero-cost solution.**

---

## One-line summary

> **The FastHSM kernel pins its context struct to a raw pointer using `fixed (TContext* ctxPtr = &context)`,
> which requires `TContext : unmanaged`. `EntityRepository` is a managed class, so it cannot
> live in an unmanaged struct. The correct fix is to store a long-lived `GCHandle` to the
> `EntityRepository` and pass its `IntPtr` through the bridge — giving HSM action delegates
> full ECS world access with zero per-frame allocation.**

---

## 1. The Root Cause (Confirmed from Source)

The FastHSM and FastBTree libraries handle memory completely differently:

| | **FastBTree** | **FastHSM** |
|---|---|---|
| Context passing | Managed generic: `ref TContext context` | Pinned pointer: `fixed (TContext* ctxPtr = &context)` |
| Constraint | `where TContext : struct` | `where TContext : unmanaged` |
| Can hold `EntityRepository`? | ✅ Yes — class fields allowed | ❌ No — `unmanaged` forbids any managed reference |

From the actual `HsmKernel.cs` source (lines 91–92):

```csharp
// HsmKernel.cs — FastHSM library (cannot edit)
fixed (TInstance* instPtr = &instance)
fixed (TContext*  ctxPtr  = &context)   // ← C# requires TContext to be unmanaged to pin it
fixed (CommandPage* cmdPtr = &commandPage)
{
    HsmKernelCore.UpdateBatchCore(definition, instPtr, 1, sizeof(TInstance),
                                  ctxPtr, deltaTime, cmdPtr);
}
```

The `fixed` statement literally pins the context struct in memory and converts it to a raw `void*` that is passed to the unmanaged core. The C# compiler enforces that only `unmanaged` types (no managed references, no class fields) can be pinned this way. This is a CLR-level constraint — it cannot be worked around without modifying the FastHSM library itself.

---

## 2. What BATCH-13 Did — and Why It Left the Problem Unsolved

BATCH-13 introduced `FdpHsmContext` (with `World` field) and `HsmKernelBridge` (without it):

```csharp
// HsmTickSystem.cs

// Full user-facing context — has World, but NOT passed to the kernel
public struct FdpHsmContext
{
    public Entity           Self;
    public EntityRepository World;   // managed class reference — cannot be unmanaged
}

// Thin unmanaged bridge — IS passed to the kernel (satisfies the constraint)
internal struct HsmKernelBridge
{
    public Entity Self;              // only value types — valid as unmanaged
}
```

The per-entity tick code (lines 102–106):

```csharp
var fdpContext = new FdpHsmContext { Self = entity, World = World };  // ← World is here...
var bridge     = new HsmKernelBridge { Self = fdpContext.Self };      // ← but World is NOT copied
                                                                       //   (EntityRepository has no
                                                                       //    unmanaged representation)

HsmKernel.Update(def.HsmDefinition, ref component, bridge, DeltaTime);
// Only 'bridge' reaches the kernel and therefore the action delegates.
// 'fdpContext' is built and immediately discarded. ↑
```

`FdpHsmContext` exists, but it was never threaded through to the delegates. The BATCH-13 comment said *"Phase 3+ wiring"* — that wiring never happened because there was no mechanism to pass a managed reference through the raw pointer API.

The result: the action delegates (`Activity_Cruise`, `OnEnter_Disabled`) are stubs. They compile, run, and do nothing.

---

## 3. The Fatal Flaw in the ApcBrainOutputSystem Approach

The first proposed fix was an external bridge system that reads `BrainHsm128.State.ActiveLeafIds[0]` after `HsmTickSystem` runs and writes channels based on current state. 

**The architect identified this as architecturally broken** for state machines that use `OnEntry` and `OnExit` actions.

FastHSM executes a full **Run-To-Completion (RTC)** pass per frame. Within a single frame, the state machine can execute multiple transitions:

```
Frame N:  [Cruising] → MobilityLost event received → [Disabled] (OnEntry fires here)
```

But if the APC takes another hit that same frame and has a transition through a transient state:

```
Frame N:  [Cruising] → [Recovering] → [Disabled]
```

`ApcBrainOutputSystem` only sees the final state (`Disabled`) at the end of the frame. Any ECS effect that was supposed to fire during entry/exit of `[Recovering]` — say, a partial eject, a specific sound cue, a temporary buff applied on enter and removed on exit — is **permanently lost**. The external system cannot reconstruct what transient states were visited.

For the current APC scenario (two states, no transient intermediates), this bug is latent. Adding a third state with entry actions would immediately expose it. **This is a time bomb.**

---

## 4. The Correct Fix — Cached `GCHandle` (Zero Per-Frame Allocation)

The architect's solution: `EntityRepository` is a long-lived object that lives for the entire simulation. Allocate a `GCHandle` to it **once at startup**, store the resulting `IntPtr` (which *is* unmanaged — it's just a word-sized integer holding a GC table index) in the bridge struct. Action delegates recover the `EntityRepository` from the handle with `GCHandle.FromIntPtr(ptr).Target`.

**This has zero hot-path overhead: no allocation, no GC pressure, no new objects created each frame.**

---

### Implementation

#### Step A — Add `GCHandle` to `EntityRepository`

File: `FDP/Kernel/Fdp.Kernel/EntityRepository.cs`

```csharp
using System.Runtime.InteropServices;

public sealed partial class EntityRepository : IDisposable
{
    // Allocated once at construction; freed in Dispose.
    // Allows HSM action delegates (via HsmKernelBridge.WorldHandle) to recover
    // this EntityRepository through a raw pointer without violating the
    // 'unmanaged' constraint required by HsmKernel.Update<TInstance,TContext>.
    private GCHandle _selfHandle;

    public EntityRepository()
    {
        _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        // ... existing init ...
    }

    /// <summary>
    /// Raw pointer to this repository, valid for passing through unmanaged contexts.
    /// Recover via: <c>(EntityRepository)GCHandle.FromIntPtr(handle).Target!</c>
    /// The handle remains valid until <see cref="Dispose"/> is called.
    /// </summary>
    public IntPtr UnmanagedHandle => GCHandle.ToIntPtr(_selfHandle);

    public void Dispose()
    {
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
        // ... existing dispose ...
    }
}
```

#### Step B — Pass `IntPtr` in the Bridge

File: `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs`

```csharp
// Update HsmKernelBridge — IntPtr is an unmanaged value type
internal struct HsmKernelBridge
{
    public Entity Self;
    public IntPtr WorldHandle;   // ← points to the GCHandle table entry for EntityRepository
}
```

Update the per-entity tick:

```csharp
// Inside HsmTickSystem<T>.OnUpdate():
var bridge = new HsmKernelBridge
{
    Self        = entity,
    WorldHandle = World.UnmanagedHandle,   // one simple property read per tick
};

HsmKernel.Update(def.HsmDefinition, ref component, bridge, DeltaTime);
```

`FdpHsmContext` (the user-facing struct with `EntityRepository World`) is **no longer needed** and can be removed, or kept with a doc comment explaining it was superseded by the bridge pattern.

#### Step C — Use in HSM Action Delegates

```csharp
// ApcHsmActions.cs
public static unsafe void Activity_Cruise(
    void* instance, void* context, HsmCommandWriter* writer)
{
    var bridge = (HsmKernelBridge*)context;

    // Recover EntityRepository from the long-lived GCHandle — zero allocation
    var repo = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;

    // Full ECS access — write locomotion intent every tick while Cruising
    ref var loco = ref repo.GetComponentRW<LocomotionChannel>(bridge->Self);
    loco.ActiveAction       = NavigationConstants.ActionIdFollowRoute;
    loco.DoctrineInstanceId = repo.GetComponent<DoctrineState>(bridge->Self).InstanceId;
}

public static unsafe void OnEnter_Disabled(
    void* instance, void* context, HsmCommandWriter* writer)
{
    var bridge = (HsmKernelBridge*)context;
    var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;

    // Stop locomotion
    ref var loco = ref repo.GetComponentRW<LocomotionChannel>(bridge->Self);
    loco.ActiveAction = 0;

    // Eject passengers — fires exactly once, on entry to Disabled
    // (this is the OnEntry guarantee that ApcBrainOutputSystem cannot provide)
    ref var interact = ref repo.GetComponentRW<InteractionChannel>(bridge->Self);
    interact.ActiveAction      = BehaviorConstants.ActionIdEjectPassengers;
    interact.DoctrineInstanceId = repo.GetComponent<DoctrineState>(bridge->Self).InstanceId;
    unchecked { interact.ActionInstanceId++; }
}
```

---

## 5. Impact on `ApcBrainOutputSystem`

Once `OnEnter_Disabled` fires the eject directly, `ApcBrainOutputSystem` is no longer needed for that purpose. However, `Activity_Cruise` (which fires every tick while Cruising) replaces the continuous locomotion write that `ApcBrainOutputSystem` was doing.

**Disposition of `ApcBrainOutputSystem`:**
- If HSM delegate actions are fully implemented (both `Activity_Cruise` + `OnEnter_Disabled`) → **delete `ApcBrainOutputSystem`** entirely. The HSM owns its entire output surface.
- If there is a desire to keep the external system for logging/observation → keep it read-only (no writes). But any duplicate writes risk ordering issues.

Recommendation: **delete it**. The HSM is the brain — its output belongs with its logic.

---

## 6. Summary

| | Before (BATCH-13) | Wrong Fix (ApcBrainOutputSystem) | Correct Fix (GCHandle) |
|---|---|---|---|
| **Works for `Activity` actions?** | ❌ Stub | ✅ Yes | ✅ Yes |
| **Works for `OnEntry` actions?** | ❌ Stub | ❌ Broken (sees only final state) | ✅ Yes — fires at exact transition |
| **Works for `OnExit` actions?** | ❌ Stub | ❌ Broken | ✅ Yes |
| **Per-frame allocation?** | None | None | None |
| **Library changes required?** | None | None | None |
| **Architecture preserved?** | — | ❌ HSM purpose undermined | ✅ HSM owns its output |
| **Complexity?** | — | Low | Low (3 small changes) |
