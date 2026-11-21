<!--STATUS
state: LIVE
updated: 2026-11-17
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Road_Convoy.md — §6 CE-9012 (on hold until this is fixed)
-->

# NOTE — convoy → behaviors: a unit memory grows the occurrence store INSIDE the parallel brain phase (CE-9013)

Found by the convoy lane, `2026-11-17`, while building CE-9012: `PlatoonConvoy` gives each vehicle a `ConvoyOrder` instead of a `FollowRoute`.

## What happens

`uc-urban-battle`, editor tier, exit 134 at about 36 s of sim time. An IFV starts `VehicleFightFromCover`. That behaviour's first
`UnitMemory.Ref<T>` finds the vehicle's 1024 block full, and the call chain runs:

`OccurrenceStoreAccess.TryAttachSlot` → `AddBlock` → `BlueprintTierSpec.Add(4096)`

This is **inside** the brain phase, so the add is deferred into the batch's command buffer, and it fails twice over:

1. `EntityCommandBuffer` refuses components over 1024 B: *"Component size 4096 exceeds maximum 1024"*.
2. With that cap raised (tried, then reverted), `AddBlock` reads the new block back at once (`spec.Memory`) and fails:
   *"Entity … missing BlueprintBlackboard4096"*. A pending add cannot be read by reference (`TryGetOwnPendingAdd` returns copies).

So a bigger cap only moves the crash by one line. This is the D-26 shape: no lazy structural growth mid-phase.

## Why it showed up now

The platoon's convoy order is paused, not ended, while the vehicle fights. Its occurrence left the vehicle's 1024 block full, and the
next unit memory needed a new block. It is latent for **any** brain whose blocks fill.

## Lean (yours to decide)

- Declare a behaviour's unit memories, and pre-size them where its occurrences already are: serially, in `BehaviorIngressSystem` →
  `OccurrenceStoreAccess.AppendBlockFor`. Then `UnitMemory.Ref` never has to grow the store inside the phase.
- Fallback: `TryAttachSlot` called inside a phase refuses (or defers to the next pre-pass) instead of appending.

## What the convoy lane did

- Reverted the CE-9012 switch, so `uc-urban-battle` runs as before. CE-9012 resumes once this is fixed.
- Kept the generic pieces: `ConvoyOrder.Route` and the one `CgfNodes.WriteFollowRoute`.
- Did not change `EntityCommandBuffer`.
