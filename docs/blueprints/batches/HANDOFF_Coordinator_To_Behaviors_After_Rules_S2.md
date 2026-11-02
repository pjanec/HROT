<!--STATUS
state: LIVE — coordinator → behaviors: after rules S2 — the prerequisites of rules S4 and small fixes
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — S4 (evaluation per member + debrief / HUD) needs S3 (macros, building on backend) and CE-4155
  - ../Blueprint_Issues_Tracker.md — the rows are the spec
-->
# HANDOFF — coordinator → behaviors: **after rules S2**

**Dispatched at** `b627cb33d` (`coordinator`) · ⛔ scope FROZEN at this commit (R-106).
🔒 **User, `2026-11-02`:** *"Is there anything the behavior branch can work on?"*

| # | item | why now |
|---|---|---|
| ① | **the wire inventory row for `PlayerMessage`** — `scripts/ned-wire-inventory.py --check` fails on `coordinator` after the merge: the type is in code, not in `docs/reference/NED_DDS_Protocol.md` | your rules S2 type; a one-row fix |
| ② | **CE-4155** — every damage event names who caused it (grenades, blasts, indirect fire, …) | ⭐ S4's prerequisite, and `NoHarm`'s indirect harm |
| ③ | **CE-4205** — ONE live evaluation of the loop predicates: `DataBreakpointManager` keeps private spatial / lifecycle / … arms; route it through the shared compiler arms you built in CE-4164 | one implementation per concept |
| ④ | **CE-2377** (yours) — a behaviour resolver's refusal at assignment is only a Warn | found building S2 |
| ⑤ | the filmed scenario findings, ⚠ each MEASURE FIRST (may be intended tuning): CE-4123 · CE-4124 · CE-4132 · CE-4133 · CE-4134 · CE-4135 · CE-4136 · CE-4137 · CE-4055 | each row names the scenario and the clip |

⏳ **Rules S4** starts once backend's macros S3b lands (S4's prerequisite S3) and ② is done.
## Gates *(Rule 8)*
Touched classes, new rails red-proved; for ⑤ the scenario's own checks / golden; the gate scripts incl. `ned-wire-inventory.py --check`.

_coordinator lane, 2026-11-02_
