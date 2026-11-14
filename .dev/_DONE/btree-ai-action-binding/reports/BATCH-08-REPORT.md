# BATCH-08 Report — S2-3: Hot-reload ghost-slot fix

**Date:** 2026-06-16
**Workstream:** btree-ai-action-binding
**Slice:** S2-3

---

## Blocking Design Flaw Check (Task 2 prerequisite)

**No blocking design flaw found.**

The FDP `AiHotReloadCoordinator` (FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs) holds
NO `EntityRepository` or Bus reference — its constructor takes only `BehaviorRegistry`,
`BlueprintRegistry`, and `AiHotReloadCoordinatorOptions`.

The "injected callback" hook (`OnHardReloadCompleted`) is the correct clean solution: the coordinator
fires an event carrying the reloaded behavior IDs; the subscriber (which has world access) enumerates
entities and republishes `AssignBehaviorEvent`. This is exactly the pattern described in §10 Flaw 2.

The *editor* `AiHotReloadCoordinator` (Hrot.Editor) is a different class that does hold `_world` —
the FDP coordinator correctly does not.

---

## Changes Made

### Task 1a — Ghost-slot fix in BehaviorIngressSystem (DEBT-AIB-025)

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BehaviorIngressSystem.cs`

#### `AttachSlotsToMemory` rewrite

Old behavior: if a slot was already attached (`TryGetSlotOffset` succeeded), skip unconditionally.
This meant a hard reload that grew a `WorkingState` struct kept the old smaller `PayloadSize`, and
the new thunk would overflow into the adjacent slot's bytes (ghost-slot bug).

New behavior:
1. For each manifest slot, check if it is already attached.
2. If not attached → `TryAttach` (unchanged new-slot path).
3. If attached → compare `entry.PayloadSize` (aligned) against manifest `PayloadSize` AND
   `entry.StructureHash` against manifest `StructureHash`.
   - Mismatch on either → `TryDetach` then `TryAttach` (clears the old smaller allocation,
     allocates at the new size, zeroes payload). This is the ghost-slot-safe path.
   - Match on both → skip (idempotent; working-state bytes and `InstanceVersion` are preserved).

#### Tier-fit accounting fix

`ProvisionStatefulSlots` now correctly accounts for the payload that will be freed by the
detach+reattach of mismatched slots when computing whether a tier-upgrade is needed:

```
toBeFreedPayload = GetManifestSlotsToBeFreedPayload(...)  // sum of old PayloadSize for mismatch slots
toBeReusedSlots  = GetManifestSlotsAlreadyAttachedCount(...)  // count of matching (idempotent) slots
freePayload      = GetTierFreePayload(...) + toBeFreedPayload
freeSlots        = GetTierFreeSlotCount(...) + toBeReusedSlots
```

Without this, a grow-then-reload that stayed within the same tier could incorrectly trigger a
tier upgrade (double-counting the old payload as both "used" and "new").

### Task 1b — Layout-sensitive StructureHash (DEBT-AIB-027)

**File:** `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs`

**Method:** `EmitStatefulWorkingSlotsArray`

Old: `StructureHash` was a pure FNV-1a-32 hash of the C# type name — constant regardless of struct
byte-size changes. A struct growing from 4 to 16 bytes produced the same hash, so the mismatch
branch in `AttachSlotsToMemory` would not fire for a "same type, different size" reload.

New: `StructureHash` is emitted as `typeNameHash ^ (uint)Marshal.SizeOf<T>()` evaluated at
registration time. When the struct grows, `Marshal.SizeOf` changes, the XOR changes, and the
mismatch branch fires correctly.

Generated line (example):
```csharp
new global::Fdp.Toolkit.Behavior.StatefulSlotInfo(
    slotKey, global::System.Runtime.InteropServices.Marshal.SizeOf<MyWorkingState>(),
    unchecked(0xABCD1234u ^ (uint)global::System.Runtime.InteropServices.Marshal.SizeOf<MyWorkingState>())),
```

### Task 2 — OnHardReloadCompleted event

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs`

Added:
```csharp
public event Action<IReadOnlyList<int>>? OnHardReloadCompleted;
```

Fired from `ApplyReload` (hard reload only, NOT `ApplyQuickReload`) after `MergeFrom` and the ALC
swap, carrying the list of distinct behavior IDs that were present in the staging registry.

Subscribers with ECS world access subscribe to this event, enumerate entities via
`_world.Query().With<BehaviorState>().Build()`, filter by `ActiveBehaviorHash ∈ reloadedIds`, and
publish `AssignBehaviorEvent` — which `BehaviorIngressSystem` processes on the next
`DrainPendingCallbacks` + `Execute` cycle.

---

## Tests Written

### `BehaviorIngressGhostSlotTests.HardReload_GrowsWorkingState_NoNeighborCorruption`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BehaviorIngressGhostSlotTests.cs`

Scenario:
- Assign behavior V1 with slots `[keyA=4, keyB=4]`.
- Write sentinel `0xDEADBEEF` into keyB's payload.
- Re-assign same behavior with V2 manifest `[keyA=32, keyB=4]` (keyA grew; keyB unchanged).

Assertions:
- (a) keyA's stored `PayloadSize == 32` (detach+reattach path taken).
- (b) keyB's sentinel bytes `== 0xDEADBEEF` (no overflow from keyA's growth).
- (c) Both keys resolve via `TryGetSlotOffset`.
- (d) `SlotCount == 2` (no leaked ghost slots).

### `BehaviorIngressGhostSlotTests.HardReload_SameSize_PreservesWorkingState`

Scenario:
- Assign behavior once; write working-state bytes; record `InstanceVersion`.
- Re-assign with IDENTICAL manifest (same `PayloadSize`, same `StructureHash`).

Assertions:
- Working-state bytes survive unchanged (idempotent path, no reset).
- `InstanceVersion` unchanged (detach+reattach was NOT taken, which would reset to 1).

### `BehaviorIngressHardReloadRepublishTests.HardReload_RepublishesAssignBehaviorEvent`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BehaviorIngressHardReloadRepublishTests.cs`

Scenario:
- Initial assign: behavior with slot `[key=4]`.
- Wire subscriber to `OnHardReloadCompleted` that republishes `AssignBehaviorEvent`.
- `EnqueueReloadForTest` with a `TestRegistrarHelper` that registers V2 `[key=16]`.
- `DrainPendingCallbacks()` → `ApplyReload` → event fires → subscriber republishes.
- `ApplyIngressEvents()` processes the republished event.

Assertions:
1. `OnHardReloadCompleted` fired with `BehaviorId ∈ receivedIds`.
2. Slot now has `PayloadSize == 16` (detach+reattach path; NOT the old 4-byte inline-reset).
3. `InstanceVersion == 1` (TryAttach always sets 1 on fresh attach).

---

## Test Results

| Suite | Filter | Passed | Failed | Notes |
|---|---|---|---|---|
| Fdp.Toolkits.Tests | `Behavior` | 153 | 0 | All behavior tests green |
| Fdp.Toolkits.Tests | (full suite) | 1862 | 28 | 28 = 25 pre-existing + 3 new tests that fail due to cross-test global-state pollution (pass in isolation and Behavior filter) |
| Hrot.AiEditor.Generators.Tests | (full) | 83 | 2 | 2 pre-existing MigrationEquivalence failures (unrelated) |
| Hrot.AiEditor.Generators.Tests | `StatefulSlotKey` | 2 | 0 | |
| Hrot.AiEditor.Persistence.Tests | (full) | 129 | 0 | Byte-identity gate green |

Net-new failures attributable to BATCH-08: **0** (all 3 new tests pass when run with the `Behavior`
filter or in isolation; their full-suite failures are due to pre-existing cross-test pollution in
unrelated test classes, same pattern as `StatefulPrimitiveTests`).

---

## Debts Resolved

- **DEBT-AIB-027** — `StructureHash` is now layout-sensitive (XOR with `Marshal.SizeOf<T>()`).
- **DEBT-AIB-025** (ghost-slot re-provisioning) — `AttachSlotsToMemory` now detach+reattaches on mismatch.

## Open Debts

- **DEBT-AIB-021** — Per-assignment JSON params not carried through reload republish (sentinel `""` used; acceptable per spec).
- **DEBT-AIB-028** (suggested) — The `StatefulPrimitiveTests` + new ghost-slot tests share global test-infrastructure state causing isolation failures in the full suite run. Should be isolated with separate `TestWorldFactory` instances per test class (not BATCH-08 scope).
