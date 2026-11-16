<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: §1 where we are (resume from its first open row) · §2 lane decisions · §3 gates
stale-below: nothing
related-designs:
  - ../designs/vehicle-tactics/DESIGN_Road_Convoy.md — THE design this lane builds (UML, decisions C-1…C-11, slices S-1…S-6)
  - batches/HANDOFF_Coordinator_To_Convoy.md — the dispatch (frozen at 74c53dd69)
-->

# STATUS — the `convoy` lane (CE-4631, R-4282)

Branch `convoy`, started from `origin/coordinator` at `ea4c01af8` (handoff dispatched at `74c53dd69`). Ids: CE-9000…CE-9999 (next free **CE-9006**),
R-9000…R-9999 (next free **R-9011**), components 108 / 109 / 157 / 158, descriptor ordinals 198–203.

## 1. Where we are — resume from the first open row

| step | state | what | evidence |
|---|---|---|---|
| 0 start | ✅ | branch, started marker | `chore: started convoy at ea4c01af8` |
| 1 MEASURE | ✅ | ①–⑧ + three more (road-use bits, mode 2's emission point, the upstream rule) | design §1 INVENTORY, each with file:line |
| 2 DESIGN | ✅ | `DESIGN_Road_Convoy.md` — proven solution, C-1…C-11, class / 2 sequences / module diagram (all parse); draft SUPERSEDED; links both ways (AQ93, Navigation v2, DA position, City Battle) | `mermaid-check` 4/4 |
| 3 S-1 priority lead (CE-9000) | ⏳ next | upstream H14 + sync; `RoadUse.InTrafficPriority`; handover Priority / MaxSpeed | |
| 4 S-2 column-follower (CE-9001) | ⏳ | | |
| 5 S-3 no cut-in (CE-9002) | ⏳ | | |
| 6 S-4 mode 2 lane (CE-9003) | ⏳ | | |
| 7 S-5 order + switch (CE-9004) | ⏳ | | |
| 8 S-6 scenarios (CE-9005) | ⏳ | | |

## 2. Lane decisions (for the user's review) — R-9000…R-9010

Design §3 C-1…C-11 (each with its rejected alternatives and what would change it); ledger rows in `RULINGS.md`.

## 3. Gates

| gate | result |
|---|---|
| `rulings-check.py` | 746/746 |
| `tracker-counts.py --check` | OK |
| `design-digest.py --check` | fails identically at the base (pre-existing; none of this lane's files) |
