# BATCH-08 Review (S2-3)
**Status:** ✅ APPROVED   **Date:** 2026-06-16

## Summary
Ghost-slot hazard (SLICE2-DESIGN §10 Flaw 2) fixed: `BehaviorIngressSystem` now detach+reattaches a stateful slot whose `PayloadSize`/`StructureHash` differs from the manifest (with correct tier-refit accounting), idempotent on a match; the manifest `StructureHash` is now size-sensitive (DEBT-AIB-027 resolved); the runtime coordinator exposes `OnHardReloadCompleted(reloadedBehaviorIds)` so a host can re-publish `AssignBehaviorEvent` (no inline `ResetSlot`). Verified by source read + running the gates.

## Verification (lead-run)
- `Fdp.Toolkits.Tests --filter Behavior`: **153/0** (deterministic).
- Byte-identity gate `Hrot.AiEditor.Persistence.Tests`: **129/0**.
- Read all 3 new tests' assertions — strong:
  - `HardReload_GrowsWorkingState_NoNeighborCorruption`: grows keyA 4→32, asserts new `PayloadSize`, neighbor keyB sentinel `0xDEADBEEF` intact.
  - `HardReload_SameSize_PreservesWorkingState`: same manifest → slot NOT detached, working-state + InstanceVersion preserved (idempotent path).
  - `HardReload_RepublishesAssignBehaviorEvent`: drives the REAL `DrainPendingCallbacks→ApplyReload→OnHardReloadCompleted→republish→ingress` path (via existing `EnqueueReloadForTest` seam); asserts slot re-provisioned to 16B + `InstanceVersion==1` (proves detach+reattach, not inline ResetSlot).

## Issues Found (non-blocking; recorded as debt)
### Note 1 (→ DEBT-AIB-030): full-suite test flakiness (pre-existing infra, now touches behavior tests)
4 stateful behavior tests pass under `--filter Behavior` (153/0) and in isolation, but a **non-deterministic** subset fails in the FULL unfiltered `Fdp.Toolkits.Tests` run (run A: StatefulPrimitive×2+GhostSlot×2; run B: Republish×1). Root cause = the pre-existing DEBT-AIB-010 issue: xUnit cross-collection parallelism + process-global ECS/component-id/registry state corrupted by unrelated collections (Replication/Gizmos/Combat/Geographic/…, ~22 of the 26 full-suite failures). NOT a logic defect and NOT introduced by BATCH-08 — the implementation is correct (filtered + isolated green). Validate this workstream with `--filter Behavior`. Proper fix is suite-wide (isolate global state or disable cross-collection parallelism) — out of this workstream's scope.

### Note 2 (→ DEBT-AIB-031): coordinator re-publish is dormant in production
`OnHardReloadCompleted` is defined + tested, but **no production subscriber** wires it (grep: only coordinator + test). Task-1 ghost-slot-safe provisioning IS active (runs on any `AssignBehaviorEvent`); but the hard-reload *trigger* won't fire in the real app until the host (which owns both coordinator and world) subscribes, enumerates entities by `BehaviorState.ActiveBehaviorHash`, and re-publishes. Architecturally correct that the coordinator doesn't own the world; wiring is a host integration step.

## Deviations (ratified)
- Hard reload re-publishes with `JsonParams = ""` → resets params to baked defaults (acceptable hard-reload semantic; runtime per-assignment JSON override is DEBT-AIB-021).
- StructureHash = `typeNameHash ^ (uint)Marshal.SizeOf<T>()` (size-sensitive; PayloadSize comparison is the primary growth guard). Sufficient for the size-growth ghost-slot case; full field-layout hashing not needed.

## Verdict
APPROVED. DEBT-AIB-027 resolved; DEBT-AIB-010 expanded; DEBT-AIB-030/031 added. Proceed to S2-G (close DEBT-AIB-026 there).

## Commit Message
```
fix(btree-binding): S2-3 hot-reload ghost-slot — re-provision grown WorkingState (BATCH-08)

Completes S2-3.
- BehaviorIngressSystem: ghost-slot-safe re-provision — detach+reattach a stateful slot
  when manifest PayloadSize/StructureHash differs (idempotent on match, preserves
  working state); tier-refit accounting credits to-be-freed payload/slots
- BTreeBridgeEmitCore: manifest StructureHash now size-sensitive (^ Marshal.SizeOf) — DEBT-AIB-027
- AiHotReloadCoordinator: OnHardReloadCompleted(reloadedBehaviorIds) event on hard reload
  (ApplyReload only); host subscriber re-publishes AssignBehaviorEvent (no inline ResetSlot)
Tests: 3 new (grow/no-corruption, same-size/preserve, republish end-to-end via EnqueueReloadForTest);
Behavior filter 153/0; byte-identity 129/0.
Notes: full-suite flakiness is pre-existing DEBT-AIB-010 (validate via --filter Behavior, DEBT-AIB-030);
coordinator re-publish dormant until host wires subscriber (DEBT-AIB-031).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
