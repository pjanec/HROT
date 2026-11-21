<!--STATUS
state: LIVE
updated: 2026-11-21
current-answer: the task table and DECISIONS
stale-below: nothing
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md §9b, §9b.1 — the design built (K1–K13 binding)
  - HANDOFF_Behaviors_To_Backend_UnderFire.md — the dispatch (frozen at ded921982)
  - NOTE_Backend_To_Behaviors_UnderFire_Contract.md — the ids and signatures behaviors binds to
-->

# STATUS — backend: the `UnderFire` place-knowledge kind (CE-2468, R-2140)

Branch `backend`; merged `behaviors@25431b39c` (a fast-forward: it already held every `backend` commit), started marker `979d55e6a`, contract note
`91da592a6`.

| task | state | evidence |
|---|---|---|
| UF-1 `RecordWriterCore.Retire(key)` (K2) | DONE | `Retire` ends this node's OWNED records of a key: a networked one through `DestroyEntityCommand` (the ELM teardown, the DDS dispose to every ghost) and marked `TearDown` at once, an offline one destroyed. A ghost is never touched; a creation still in flight is ended as it arrives and its queued items are forgotten. The destroy is ONE helper now, `NetworkEntityDestroy.Request` (it was written out in obstacles, charges and gas clouds; all three go through it). Rails `RecordRetireTests` (5: owned networked + offline, a ghost untouched, a pending creation, keyframes before / after seek back and forth), each red-proved by its own mutation (no end · no owner test · a retired pending key drained as usual). ⏳ The ghost's DDS dispose on a SECOND node is proved by UF-2's two-node rail. Orphans: `Retire` acts where `Owns` holds, which the ownership handover makes true on the inheriting node |

## DECISIONS

| # | decision | why · what would change it |
|---|---|---|
| D-UF1a | the network destroy is one helper (`NetworkEntityDestroy.Request`), not a fourth copy | the shared-and-unified rule; each caller keeps its own extra step (a charge spent, a cloud ending flag) around it |
| D-UF1b | `Retire` writes the lifecycle / destroy directly, not through the command buffer | the writer is serial and `EntityQuery` is an index walk, so changing an entity under it is safe; the record leaves the writer's own query (`AllButTearDown`) at once |
