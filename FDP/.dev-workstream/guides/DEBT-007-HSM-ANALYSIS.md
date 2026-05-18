# DEBT-007: HSM Architectural Analysis — GCHandle / External Bridge

> **STATUS: ✅ FULLY RESOLVED in BATCH-17.**  
> `EntityRepository.UnmanagedHandle` (GCHandle.Normal, one-time alloc in constructor, freed in Dispose);  
> `HsmKernelBridge.WorldHandle : IntPtr`; `FdpHsmContext` deleted; `ApcBrainOutputSystem` deleted;  
> `ApcHsmActions.Activity_Cruise` and `OnEnter_Disabled` fully implemented with ECS writes;  
> 4 new tests pass. T9 `UrbanAmbush_SimulationRunsToCompletion_WithExpectedMilestones` still passes.  
> This document is an architectural reference for the GCHandle pattern and the OnEntry/OnExit
> correctness argument that ruled out the external-bridge approach.

---

## Background

DEBT-007 tracked the unresolved question of how HSM action delegates (running inside FastHSM's
unmanaged kernel on a background thread) could safely write back to the ECS world (main thread).

The core tension: FastHSM's `[HsmAction]`-decorated delegates are dispatched by FNV-1a hash
from within the unmanaged HSM kernel. They run on the background thread (PerceptionModule / SoD).
The `EntityRepository` is a managed GC object. Passing a `GCHandle` to unmanaged code is the
standard .NET pattern for this scenario.

---

## Resolution (BATCH-17)

The solution settled on in BATCH-17:

1. **`EntityRepository.UnmanagedHandle`** — a `GCHandle.Normal` allocated once in the constructor
   and freed in `Dispose`. Provides a stable `IntPtr` that does not move across GC compactions.

2. **`HsmKernelBridge.WorldHandle : IntPtr`** — stores the `IntPtr` from `UnmanagedHandle.ToIntPtr()`.
   The HSM action delegates recover the `EntityRepository` via
   `GCHandle.FromIntPtr(WorldHandle).Target as EntityRepository`.

3. **`FdpHsmContext` deleted** — the intermediate context class was removed. The bridge is now
   a direct `IntPtr` without an intermediate managed wrapper.

4. **`ApcBrainOutputSystem` deleted** — this polling system was the "external bridge" approach
   that was ruled out. Direct ECS writes from the action delegate replace it.

5. **Action registration** — `Fhsm.SourceGen` generates `HsmActionRegistrar.RegisterAll()` which
   must be called before any HSM tick. See DEV-GUIDE.md §Common Pitfalls §9 for the full pattern.

---

## OnEntry / OnExit Correctness Argument

The concern was whether `OnEnter_*` and `OnExit_*` delegates could safely write ECS components
given that the HSM runs asynchronously on the SoD module thread.

**Ruling:** Safe under the following conditions (all satisfied in the Urban Ambush demo):
- The action delegate writes via `EntityCommandBuffer` (ECB), not directly to the live world.
- The ECB is replayed on the main thread after the SoD module completes.
- No other system reads the mutated component between ECB submission and replay within the same frame.

The external-bridge approach (`ApcBrainOutputSystem` polling a shared struct) was rejected because:
- It introduced a polling latency (one frame behind actual state transitions).
- It required a shared mutable struct visible to both threads, introducing a data race.
- The ECB approach is already the standard ECS mutation pattern throughout the codebase.

---

*Archived — DEBT-007 is closed. This document is retained for reference.*
