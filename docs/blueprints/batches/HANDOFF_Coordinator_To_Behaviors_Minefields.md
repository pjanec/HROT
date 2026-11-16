<!--STATUS
state: LIVE — dispatched, not started — ⭐ OVERNIGHT, UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — ⭐ THE design: §3 decisions (MF-1…MF-17), §3a the fixed ids, §3b UML, §4 the slices per lane, §6.1 the contract, §8 + §10 the reviews (their rows SUPERSEDE the §3 rows they touch)
-->

# HANDOFF — coordinator → `behaviors`: minefields, the BEHAVIOURS (CE-4624, slices MS-B1…MS-B4)

**For:** the `behaviors` lane. **From:** coordinator. **Dispatched at `8b02d1a2a`**; ⛔ scope FROZEN there — later documents are FYI; one that
invalidates a slice ⇒ STOP that slice, do the rest (R-106).

🔒 User, `2026-11-16`: *"approved both, fold it in and write the handoffs to the lanes that are free"* · decisions R-4270…R-4274.
⭐ Also ruled today: **R-4275** — your dash finding, option A: the M-4 acceptance now reads *"a break escapes the LETHAL radius"* (Motorcycle §6
reworded); no change to the numbers. Fold it into your status and close that row.

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
`git fetch origin coordinator behaviors terrain ui`; on `behaviors`, merge `origin/coordinator`; restore; push `chore: started minefields at
<sha>`. Status: `STATUS_Behaviors_Minefields.md`. Read the design END TO END first (R-129). ⭐ The ENGINE is the `terrain` lane's: merge
`origin/terrain` as soon as `NOTE_Terrain_To_All_Minefield_Contract.md` lands; until then build against stand-ins with the §6.1 names.

## 1. The slices — §4, in order
- **MS-B1** `MineStrike` (MF-5 as amended by MR-5, MR-6, MR-16): declares `Halts | LeavesRoute` (R-4263) — a dismounted squad on a Dash presses
  on, a mobility-killed vehicle yields; halt; care IN PLACE; back out along the mover's breadcrumbs (the per-mover breadcrumb record kept inside a
  field's bounds — terrain declares it in MS-T2 if it lives in the engine, ⚠ agree it through the NOTE); report the field up the chain (the existing
  report path → a `MineStruck` / knowledge event); the vehicle drill (halt, crew stays, followers reverse in their tracks).
- **MS-B2** `ReconMinefield { suspected area }` (MF-13 + MR-7): walk ONLY known-clear cells or breadcrumbs; start at the cell nearest the strike,
  left-hand rule on the 4-neighbourhood, stop at the start or after N cells; a cell known only when FULLY swept; detector pace ~0.1–0.3 m/s (a
  tuning value); raise `MinefieldCellsSwept`; a closed trace or an order ⇒ `MinefieldMarked`.
- **MS-B3** `ClearLane { start, end, width }` on TKB 116 (MF-9, MR-11): the brain drives the lane and switches its plough / flail / line charge on —
  ⛔ it never writes the field; the engine's swath clear does (MS-T4). The breach play (MF-11: suppression on the far side, smoke, then the lane) and
  crossing a marked lane single file.
- **MS-B4** the march asks for the `Minefield` danger kind (`SquadMarchBehavior.cs:218-222`); on a `Minefield` area on its route: halt and request
  engineers (KG-7 — the planner went round when it could; this is the case where it could not).
- **Acceptance demo** (MR-19): a scenario where a patrol strikes, the engineers trace and mark, the clearer cuts a lane, a squad crosses — live,
  cluster, with named checks (the `farm-under-fire` / `ua-drone-shelter` precedent).

## 2. Gates — every slice
T-1 first (each touched feature's own suites); every behaviour or per-entity step with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; each new rail red-proved by a compiling mutation; `stepped-hash.py --threads 1,4,8` on the scenario a slice serves; 0 B per steady frame (R-2084, no LINQ on the hot path); invariant output (R-4221); new recorded components unmanaged (CLAUDE.md "NEW COMPONENTS"). Rendering changes: the display rails under `xvfb-run -a`. Report per rule 8 (the gate-report contract).

## 3. The morning report — `STATUS_Behaviors_Minefields.md`
per slice built / stopped and the design folded · lane decisions with what would change them · the gate-report contract · ids allocated
(`CE-2000`…, `R-2000`…).
