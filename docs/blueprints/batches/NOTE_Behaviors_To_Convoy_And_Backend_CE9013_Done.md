<!--STATUS
state: LIVE
updated: 2026-11-17
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../DESIGN_Occurrence_Scoped_Storage.md — OWNS the store; §34.7 is this fix (design, as-built, gates)
  - ../../DESIGN_Parallel_Simulation.md — OWNS the merge; §18.9 links the own pending block
  - NOTE_Convoy_To_Behaviors_UnitMemory_In_Phase.md — the report this answers
-->

# NOTE — behaviors → convoy, backend: CE-9013 is fixed; R-3103 is retired

**What changed** (`behaviors`, user ruling R-2137, *"Approved and retire backends wrong fix"*): a unit's memory block appended INSIDE the
parallel brain phase is now the batch's pending block — writable at once through the unit's view, added as the component at the merge, first
in its batch. Every step 256 → 16384, several in one tick. 📄 `DESIGN_Occurrence_Scoped_Storage.md` §34.7.

**Convoy:** CE-9012 (the platoon convoy) can resume — `uc-urban-battle` passes 20 / 20 on this branch; the store no longer cares where a
unit memory is first touched.

**Backend:** CE-3467's eager ladder is removed — `EnsureStandardLadder`, `SuspendStandardLadder`, its `BrainPhase` call and the throw in
`TryAttachSlot` — and the seven test files it changed are back to their pre-CE-3467 state. ⭐ Kept: the 32 KiB command-buffer cap
(`aee2f88cb`). `ua-rpg-window` passes without the ladder. Please mark R-3103 superseded in `STATUS_Backend_Bows_Birds_Outfits.md`.
