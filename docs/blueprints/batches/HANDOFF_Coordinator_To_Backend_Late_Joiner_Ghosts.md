<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-05
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Cluster_Load_Phase.md — §8.2 OWNS CE-4000's fix F2 (R-4019) and the measured root cause
  - docs/DESIGN_Cluster_Membership_Use_Cases.md — UC-13 (a late joiner after a reload) and UC-17…24, the multiplayer acceptance
-->

# HANDOFF — coordinator → backend: a late joiner must not receive the previous run's entities (CE-4000, CE-4007)

**For:** the backend lane (`backend`). **From:** the coordinator lane. **Dispatched at `22b255f3b`**. ⭐ Scope FROZEN
there; later documents are FYI. A later document that invalidates an item ⇒ STOP that item, report it (R-106), keep the rest. Questions ⇒
a NOTE to coordinator. Merge `coordinator` first (it carries ui's CE-3370 and the thumbnails).

## Why

🔒 User, `2026-11-05`, asked what multiplayer gameplay work is still open and approved this as next. ⭐ With Deploy S1–S3 built, the worst
open multiplayer defect is that **a Player who joins after a scenario was reloaded receives the OLD run's entities** — ghosts in a live game.

## The items

| # | row | what | design basis | done when |
|---|---|---|---|---|
| **①** | ⭐ **CE-4000** | 📐 measured live: `hill-attack` (ids 1000–1007) then `test-move` (world: 1000); a Player started afterwards received 1000–1007, `ddsmonitor` showed `EntityMaster` 1001–1007 `Alive` from the CGF process. Root cause measured: the world-boundary clear is DELIBERATELY silent (`CycloneNetworkCleanupSystem.cs:46`, CE-2101 — ids restart at 1000). ⭐ **Fix CHOSEN by the user (`R-4019`, *"F2 approved."*):** entering `Idle` wipes the world on every ECS host and sends a delete for every id the node owned; a node ignores a delete from a node that is not the entity's owner (the sender is measured to be known on a dispose) | Cluster Load Phase §8.2; R-4019; the row's full text | UC-13 measured with `ddsmonitor`: after a reload a late Player receives only the new run's entities; a rail for "a delete from a non-owner is ignored" |
| **②** | **CE-4007** — the residual | `CycloneNetworkCleanupSystem.DisposeAll` calls every translator's `Dispose(netId)`; only `EntityMaster`, `MapVisualOverlay` and the two BDC translators call `DisposeInstance` — the others only drop a send-on-change cache. ⛔ Not measured: which of those topics are DURABLE (`TransientLocal`, only those leak) and whether a late joiner turns such a sample into a ghost. **Measure first** (`ddsmonitor` after a reload, RUNBOOK §5a), then dispose in each durable translator — ⭐ one shared path (R-174), not 20 copies | the row; RUNBOOK §5a | the measured list of durable topics in the row; after a reload and after an in-game destruction no durable topic holds a dead entity's sample; a rail per topic family or one generic rail over the translator set |

⚠ Fences: no change to the orchestrator's op protocol beyond what F2 needs; hot path R-2084 (no allocation on the egress frame);
fold the as-built into Cluster Load Phase §8.2 and re-measure UC-13 in the Use Cases doc.

## Gates (report these rows)

The touched projects' suites, `--no-build` after building the TEST project · the new rails, red-proved · the cluster integration suite
that covers load / reload / late join (named, run, filtered if the batch is still slow — CE-3369) · the live UC-13 run with its `ddsmonitor`
capture summary · every red confirmed pre-existing against the base sha · `tracker-counts.py --check` · the ids you allocate.

---
_coordinator lane, 2026-11-05_
