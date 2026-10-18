<!--STATUS
state: LIVE — backend → every lane, and a question for the USER (component-id blocks, with a lean).
updated: 2026-10-19
current-answer: the whole note.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/Blueprint_Issues_Tracker.md — its header's "ID blocks" table is the precedent (CE- / R- ids per lane, user 2026-10-02 / 2026-10-12)
-->

# NOTE — backend → every lane: `GlobalComponentIds` collided twice in one day, and the space is nearly full

**What happened (2026-10-19).** Each lane measured the next free component id on every other lane, took it, and pushed. That was
correct, and it still collided twice, because a measurement is only true until someone else pushes:

| id | behaviors | backend | resolved |
|---|---|---|---|
| 415 | `CombatProficiency` (`CE-2273`) | `Lean` (`CE-3257`) | backend merged second → `Lean` 416 |
| 416 | `EngagementArea` (`CE-2282`) | `Lean`, moved | ⚠ BOTH lanes moved at once — backend `Lean` → 417, behaviors `EngagementArea` → 417 — a third clash; backend adopted behaviors' resolution: **`Lean` 416, `EngagementArea` 417** |

⇒ Even the repair collided: two lanes each "merging second" from their own point of view. ⛔ And then it PING-PONGED: each lane's next
merge adopted the other's STALE resolution. ⭐ **CONVERGED (backend, merging `behaviors` at `a43b4bd3c`): `Lean` 416, `EngagementArea` 417
— the values behaviors declared final; both lanes now hold them.** ⇒ ⛔ do not move them again. That is the
strongest argument for blocks.
Moving one was cheap: `Lean` is a runtime id and NoScenario, so nothing persisted. ⚠ A component that is persisted or used on the wire
by id would not be.

**Why it will get worse:** the component mask is `BitMask512` (`FDP/Engine/Fdp.Core/BitMask512.cs:12` — *"up to 512 component
types"*). Ids 0–417 and 450–454 are used, so **about 85 ids are left** (418–449, 455–511), and every lane is drawing from the same top.

**⭐ Lean — the user decides:** give component ids the same per-lane blocks tracker and ruling ids already have (user, `2026-10-02` /
`2026-10-12`), carved from what is left:

| lane | block | |
|---|---|---|
| behaviors | 418–433 | 16 |
| backend | 434–449 | 16 |
| (traffic, reserved already) | 450–459 | the file's own convention |
| ui | 460–475 | 16 |
| coordinator / a new lane | 476–495 | 20 |
| ⛔ held back | 496–511 | for growing the mask |

The table would live in `GlobalComponentIds.cs`'s header, beside the ids. ⛔ Existing ids are never renumbered.
- **What would change it:** widening the mask to 1024 bits first, so the blocks can be larger. That costs a cache line per entity in the
  hot index (`EntityIndex.cs:12`: *"64 bytes / 1 cache line"*), so it is a performance decision, not a bookkeeping one.

---
_backend lane, 2026-10-19_
