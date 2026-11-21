<!--STATUS
state: LIVE — dispatched, not started — ⭐ OVERNIGHT, UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — ⭐ THE design: §3 decisions (MF-1…MF-17), §3a the fixed ids, §3b UML, §4 the slices per lane, §6.1 the contract, §8 + §10 the reviews (their rows SUPERSEDE the §3 rows they touch)
-->

# HANDOFF — coordinator → `ui`: minefields, the VISUALS and AUTHORING (CE-4624, slices MS-U1…MS-U4)

**For:** the `ui` lane. **From:** coordinator. **Dispatched at `8b02d1a2a`**; ⛔ scope FROZEN there — later documents are FYI; one that
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
`git fetch origin coordinator ui terrain behaviors backend`; on `ui`, merge `origin/coordinator`; restore; push `chore: started minefields at
<sha>`. Status: `STATUS_Ui_Minefields.md`. Read the design END TO END first (R-129). ⭐ The ENGINE is the `terrain` lane's: merge
`origin/terrain` when `NOTE_Terrain_To_All_Minefield_Contract.md` lands; until then stand-ins with the §6.1 names.
⚠ Your animals work still waits on backend's TKB 712 component (the area stand-in 511) — keep that re-bind on your list when backend lands.

## 1. The slices — §4, in order
- **MS-U1** the mockup FIRST (R-4143), house style over `scripts/mockup_kit.py`, committed beside the design: the field on the 2-D map (outline,
  mine dots, suspected / known-clear / known-mined cells, a marked field, lanes), the field's details form, the area tool's minefield arm, the
  clearer. ⭐ Then build to it and check fidelity yourself (R-4146).
- **MS-U2** `MinefieldGizmo` (MR-8, KR-11): ONE gizmo draws the field from its `EditablePolyline`; the field is born WITHOUT an overlay style
  (the generic overlay must not draw it); P-3's gate — off ⇒ every field drawn as truth (Zeus), on ⇒ the seat's side knowledge (`StationMode.SideOf`),
  the instructor every side; mine dots from a per-field cache (0 B a steady frame); 2-D and 3-D.
- **MS-U3** authoring (MR-8, MR-18): the area tool's minefield arm (outline, type AP / AT / mixed, spacing, orientation, seed, known-at-start per
  side, anti-handling); *Add lane* / *Delete lane* in line mode; editing a STRUCK field's shape refused with the reason; undo.
- **MS-U4** the clearer's kit (TKB 116: hull, plough / flail, line-charge launcher), its icon and picker thumbnail (`scripts/thumbnails.py`), the
  mine burst effects as drawn, the flail loop sound as heard.

## 2. Gates — every slice
T-1 first (each touched feature's own suites); every behaviour or per-entity step with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; each new rail red-proved by a compiling mutation; `stepped-hash.py --threads 1,4,8` on the scenario a slice serves; 0 B per steady frame (R-2084, no LINQ on the hot path); invariant output (R-4221); new recorded components unmanaged (CLAUDE.md "NEW COMPONENTS"). Rendering changes: the display rails under `xvfb-run -a`. Report per rule 8 (the gate-report contract). ⭐ The frame rails assert the mockup's layout (R-4138).

## 3. The morning report — `STATUS_Ui_Minefields.md`
per slice built / stopped and the design folded · the mockup-vs-frame side-by-side as an FYI · lane decisions · the gate-report contract · ids allocated.
