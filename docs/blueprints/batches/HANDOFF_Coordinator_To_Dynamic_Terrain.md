<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-14
current-answer: the whole file
related-designs:
  - ../../PLAN_Dynamic_Terrain.md — ⭐ THE task list (P-0…P-6, B-1…B-6), their design sections, acceptance, prerequisites and the shared gates
  - ../../DESIGN_Ground_Patches_And_Road_Blockage.md — P-0…P-6; ⭐ §9 wins over the rows it names
  - ../../DESIGN_Vehicle_Breaching.md — B-1…B-6; ⭐ §7 wins over the rows it names; §3 is drawn to §7
  - ../../UX/ground-shaping-and-ditches.svg · ../../UX/vehicle-breaching.svg — the mockups (acceptance specs, R-4146)
-->

# HANDOFF — coordinator → `dynamic-terrain`: build the dynamic terrain programme, autonomously (CE-4604)

**For:** the new `dynamic-terrain` lane (branch `dynamic-terrain`, ids **`CE-5000`…`CE-5999`** and **`R-5000`…`R-5999`**). **From:**
coordinator. **Dispatched at `<sha>`**; ⛔ scope FROZEN there — later documents are FYI; one that invalidates an item ⇒ STOP that item, do the
rest (R-106).

🔒 User, `2026-11-14`: *"I would like to dedicate one lane for following this dynamic terrain program. In a way that the lane will be working
mostly autonomously overnight, doing its own decision based on its best judgment."*

## 0. Start — every session of this lane

1. `git fetch origin coordinator dynamic-terrain`; on `dynamic-terrain`, `git merge origin/coordinator`; restore the test projects you will run
   (the third stale-binary trap); push `chore: started dynamic terrain at <sha>` (rule 1b).
2. Read [PLAN_Dynamic_Terrain.md](../../PLAN_Dynamic_Terrain.md), then the two designs' review sections (ground §9, breaching §7) in full, then
   the section each task cites. Check every design diagram against the code before building on it (obligation ③).
3. Keep **[RESUME_Dynamic_Terrain.md](../RESUME_Dynamic_Terrain.md)** (create it on the first run): its `current-answer` is the task in
   flight, the last green sha, the decisions taken and what is next. Update it at every task's end — a later session (or a compaction)
   starts from it.

## 1. How you work — autonomous

| | |
|---|---|
| ⭐⭐⭐ **decide, never wait** | the user is asleep. When a design leaves a choice open, or a measurement contradicts a design row, pick by your best judgment — the claim table (code + design columns), the proven solution for the problem class, the shared-and-unified rule — and BUILD. ⛔ No question to the user blocks the night |
| ⭐⭐ **every decision is written down** | a lane ruling `R-5xxx` in `RULINGS.md` (your block) marked *"lane decision — for the user's review"*, with its claim table in the owning design's as-built section. ⭐ The morning report lists them first, each with what would change it |
| ⭐⭐ **a design that is wrong is fixed, not worked around** | fold the as-built truth into the owning design (obligation ⑤), the prior text marked SUPERSEDED, reciprocal links kept |
| ⭐ **a failed premise stops ONE task** | report it in the resume doc and the status file; go on with the next task whose prerequisites hold (§2) |
| ⭐ **large blast radius** | a change to the wire format of an EXISTING record, the save format, or another lane's public seam: do it if the design calls for it (D-1 widens `BuildingDamage` — designed), else take the smaller reversible choice and flag it |
| ⭐ **cross-lane edits are allowed and said** | UI drawing (ui's files), the bulldozer (showcase-brains' `ClearObstacleBehavior`) — name the lane and the file in the commit |
| ⭐ **Sonnet delegation** | per CLAUDE.md: one warm worker for well-specified build steps; you keep design, root-causing and review |
| ⭐ **Windows-only** | a frame-time budget that needs a real GPU: measure what Linux can, leave a numbered note for the Windows session — never a blocker |

## 2. The order

[The plan's §1](../../PLAN_Dynamic_Terrain.md) is the dependency graph. One lane runs both chains, so take them in this order — each when its
prerequisites are green:

**B-1** (CE-4598 — the hull sees every wall; needs nothing, and it is the user's observed bug) → **P-0** (CE-4596 — the root) → **B-2**
(CE-4599) → **P-1** (CE-4403) → **P-2** (CE-4590) → **B-3** (CE-4600) → **P-3** (CE-4597) → **P-4** (CE-4591) → **B-6** (CE-4603) →
**B-4** (CE-4601) → **B-5** (CE-4602) → **P-5** (CE-4592) → **P-6** (CE-4593).

⭐ A task is DONE only when every V-row its slice lists is done, its acceptance items pass as rails, and its row is ticked with evidence. Push
after every task (green), never mid-task red.

## 3. The gates — every task

The plan's [§3](../../PLAN_Dynamic_Terrain.md) table, in full: T-1 (the feature's own suites first, new rails INTO them) · the parallel checks
(`HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1`, `stepped-hash.py --threads 1,4,8`) · R-2084 (0 B steady) · recorded state (save, seek, the Replay
Browser) · drawing rails under `xvfb-run -a` · mockup fidelity for P-5, P-6, B-3, B-4 (render, compare, fix — R-4146, never a pause) · as-built
into the design · the row in [CAPABILITIES §2b](../../CAPABILITIES.md) moves from 📐 to ✅ with its scenario. Build the affected project, never
the solution, inside a fix loop; the full solution once per task at most, in the background.

## 4. The report — `STATUS_Dynamic_Terrain.md`, refreshed at every task's end

| # | section |
|---|---|
| 1 | ⭐ **decisions taken** (`R-5xxx`) — each with its claim table link and *"what would change it"* |
| 2 | tasks done (rows ticked, evidence), tasks stopped (the failed premise), the next task |
| 3 | the gate-report contract (CLAUDE.md rule 8): one row per gate, counts, `--no-build` column, reds proven pre-existing at a base sha |
| 4 | designs updated (doc + section), ids allocated, notes left for the Windows session |
