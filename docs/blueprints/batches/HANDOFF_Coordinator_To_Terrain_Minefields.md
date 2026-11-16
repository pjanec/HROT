<!--STATUS
state: LIVE — dispatched, not started — ⭐ OVERNIGHT, UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — ⭐ THE design: §3 decisions (MF-1…MF-17), §3a the fixed ids, §3b UML, §4 the slices per lane, §6.1 the contract, §8 + §10 the reviews (their rows SUPERSEDE the §3 rows they touch)
-->

# HANDOFF — coordinator → `terrain`: minefields, the ENGINE (CE-4624, slices MS-T1…MS-T4)

**For:** the `terrain` lane (backend is busy with the animals; you built the record family, the crater table on `DoorStates` and the navmesh
areas — this is the same machinery). **From:** coordinator. **Dispatched at `8b02d1a2a`**; ⛔ scope FROZEN there — later documents are FYI; one that
invalidates a slice ⇒ STOP that slice, do the rest (R-106).

🔒 User, `2026-11-16`: *"approved both, fold it in and write the handoffs to the lanes that are free"* · decisions R-4270…R-4274.

## ⭐⭐⭐ OVERNIGHT, UNATTENDED — read this before anything else
| the rule | |
|---|---|
| ⭐⭐⭐ **NOBODY IS THERE TO ANSWER** — never ask, never wait, never end a turn on a question | every open choice: measure it, decide by the claim table (code + design), build it, record an `R-` lane decision *"for the user's review"* with what would change it |
| ⭐⭐⭐ **DO NOT STOP UNTIL EVERY TASK IS BUILT, GATED AND REPORTED** | a task finishing is not the end — go straight to the next |
| ⭐⭐ **a failed premise stops ONE task, never the batch** (R-106) | write why in the status file, do every task that does not depend on it |
| ⭐⭐ **a defect is fixed where it lives**, a rail in that feature's own suite (T-1), its design folded (obligation ⑤) | a large one filed as a row and worked around |
| ⭐⭐ **the other lanes may not have landed** — build against a STAND-IN with the fixed names of §6.1, keep going, re-bind when their work merges; `git fetch` the other lanes at each task's end and merge them when they carry what you need | ⛔ never build the other lane's half |
| ⭐ **commit, push and update the status file at every task's end** | a restart or a compaction resumes from it |
| ⛔ **you may NOT** reverse a user ruling, change an id or a name fixed in §3a / §6.1, or build another lane's slice | file it as a finding instead |

## 0. Start
`git fetch origin coordinator terrain behaviors ui`; on `terrain`, merge `origin/coordinator`; restore the test projects you run; push
`chore: started minefields at <sha>`. Status: `STATUS_Terrain_Minefields.md`. Read the design END TO END first (R-129) — §8 and §10 rows supersede §3.

## 1. ⭐ FIRST commit after the start marker — the contract note
`NOTE_Terrain_To_All_Minefield_Contract.md`: the component ids, field layouts and DDS ordinals of `Minefield`, `MinefieldStrikes`,
`MinefieldKnowledge` (+ `PlaceKnowledgeHeader`), the event types of §6.1, `DangerAreaKind.Minefield = 8` with `DangerAreaKinds` as `ushort`,
and the read API (`PlaceKnowledgeTable.Of(view).Knows`, `MinePattern.MinesIn`, `MinefieldKnowledgeOps.CellOf`) — behaviors and ui bind to it.
Declare these as compiling stubs in the same commit so the other lanes can merge and build.

## 2. The slices — §4, in order
- **MS-T1** the field (TKB 8830, DIS 5.0.0.91, class `Minefield`; the outline = its ONE `EditablePolyline`) · `MinePattern` (staggered rows,
  the same on every node) · `MinefieldStrikes` (per FIELD, a lattice list — MR-1, KR-3) · `MineTriggerSystem` (serial; the SEGMENT test, MR-14;
  every body but birds, livestock set off AT rarely, MR-16) · the bursts through the munitions 6051 / 6052 / 6053 (effects, sound, hearing, crater —
  MR-17) · the armour BOTTOM facing (MR-10: the array to 5, `FaceOf`, an explicit mine path, a rail per consumer).
- **MS-T2** the place-knowledge core (MF-17 + KR-1…KR-8): `PlaceKnowledgeHeader`; the `RecordWriterCore` EXTRACTED from `CellRecordSystem`
  (pending, drain, owns, creation request — KR-6; ⛔ do not fold `CellRecordSystem` / `BuildingDamageSystem` onto it now: that is CE-4627);
  `PlaceKnowledgeSystem` registered on EVERY deciding node (SimHost and CGF), writing only its own entities, merging by the kind's policy;
  `MinefieldKnowledge` (grid ≤ 60 × 64, a SIZE rail); the UNION `PlaceKnowledgeTable` riding on `DoorStates` like `Craters` (0 B a steady
  frame); the sharing rules (a witness within ~300 m or a report; a marked field to every side that sees its edge; own fields known at start); the
  translators in the shared pack; the scenario's `knowledge` entry with a merge arm (KR-15); replay restores it.
- **MS-T3** routing: composite navmesh areas (Minefield, + Woods, + Crater) + the polygon → (field, cells) map at bake (MR-3, KR-14); the side in
  `RouteQuery` and the path request (ForceId + 1, KR-9 / KR-13; a wire row); `JudgeBy(doors, side)`; known-mined PRICED very high, suspected
  priced (KR-10); road edges priced (MR-4); the danger solve plans with the unit's side and reports `Minefield` areas from its side's knowledge
  (MR-9); a known / marked road field closed to SUMO (MR-16).
- **MS-T4** lanes and clearing (MR-11, MR-15, MR-18): lanes as segments with `ClearedBy` / `KnownToSides`, opened per side through the filter;
  the serial swath clear for any mover whose plough / flail is active (seeded survival, a proofing pass, the flail immune to its own); the line
  charge = detonations every ~8 m; the clearer type TKB 116 (DIS 1.1.*.9.2, class `MineClearer`) with its equipment switch (the bulldozer's
  outlet pattern). ⛔ The `ClearLane` BRAIN is behaviors'; the 3-D kit is ui's.

Cross-lane edits are allowed and said (`Fdp.Toolkits` Combat / Squad / Navigation, `Hrot.Network.NED`, `Hrot.Traffic`).

## 3. Gates — every slice
T-1 first (each touched feature's own suites); every behaviour or per-entity step with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; each new rail red-proved by a compiling mutation; `stepped-hash.py --threads 1,4,8` on the scenario a slice serves; 0 B per steady frame (R-2084, no LINQ on the hot path); invariant output (R-4221); new recorded components unmanaged (CLAUDE.md "NEW COMPONENTS"). Rendering changes: the display rails under `xvfb-run -a`. Report per rule 8 (the gate-report contract). ⭐ MS-T2 and MS-T3 touch shared seams (`DoorStates`, the path request, the danger solve) — re-run the door, crater, breach, danger-crossing
and march suites and report them unchanged; a save → load → merge round trip; a replay seek.

## 4. The morning report — `STATUS_Terrain_Minefields.md`
per slice built / stopped and the design folded (as-built into §3b / §6.1) · every lane decision with what would change it · the gate-report
contract · ids allocated (your blocks: `CE-5000`…, `R-5000`…).
