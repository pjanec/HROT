<!--STATUS
state: LIVE
updated: 2026-11-18
current-answer: the whole note
related-designs:
  - docs/designs/minefields/DESIGN_Minefields.md — KG-7 / KR-10 (known fields priced ×200, not closed), MS-B4 (MinefieldAhead)
-->

# NOTE behaviors → terrain: `mf-no-way-round` (CE-2447) is your CE-4655 — routes hug a known field's edge

**Measured** (`behaviors@aa633308c`, editor, live, `2026-11-18`): the march's route does NOT cross Field B (130–150, 50–110) — it goes round its
NORTH edge with zero clearance. The leader walks at y ≈ 110.1 (the outline is y 110); the squad's own position (y ≈ 109) and the men behind
trail INSIDE the edge, and `MinefieldAhead` fires only at 55.5 s, when the squad's own position clips the corner (x 130.7). The row
"nobody enters the field (x stays below 130)" fails at 49.5 s (the leader, x 130.9, y 110.1).

| what | measured |
|---|---|
| leader, t 48 → 50 s | (128.1, 109.2) → (130.9, 110.1) — along the outline |
| squad position at the reaction (55.5 s) | (130.7, 109.1) — inside the edge |
| why it passed `2026-11-16` | the planner did not price fields yet: the route went straight through, the route sensor reported the field 30 m ahead |

**Tried here, reverted:** extending the field over test-town's whole walkable height (y −10 to 300) — the planner then detours north and the
squad sticks at the building corner (99, 106) for the rest of the run; the route never crosses the field, so nothing reports it.

**What behaviors needs from terrain** (your CE-4655 lean says it): a route round a KNOWN or SUSPECTED field keeps a CLEARANCE outside the
outline (≥ the field's spacing, ≥ 5 m). With that, `mf-no-way-round` either goes cleanly round (then the scenario needs a real barrier — we will
re-author it) or reports the field ahead. CE-2447 is BLOCKED on it in our queue.
