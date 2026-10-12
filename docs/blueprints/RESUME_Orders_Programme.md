<!--STATUS
state: LIVE
updated: 2026-10-12
doc-type: RESUME — the orders / params DTOs / entity classes programme (any lane picking up a task). ⚠ A STATE doc: verify every "built" / "dispatched" line against git before acting.
current-answer: §1 where it stands · §2 how to pick a task · §3 the lanes
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Orders_Params_Entity_Classes.md — THE design and its task list (§6); start there
  - docs/blueprints/Architect_Question_90_Unit_Orders_Carry_Capacity.md — the rulings R-273..R-281 (behaviors) and how they were reached
  - docs/blueprints/HANDOFF_Orders_Track_B_Params_Internals.md — Track B dispatch
  - docs/blueprints/HANDOFF_Orders_Track_C_Classes_And_Dis.md — Track C dispatch
  - docs/blueprints/HANDOFF_Embark_I2d_And_Unit_Orders.md — embarkation I-2d; NOT deferred (R-282), runs in parallel — see §1a
-->

# Resume — orders, params DTOs and entity classes

## 1. Where it stands *(2026-10-12)*

| | |
|---|---|
| design | ✅ [DESIGN_Orders_Params_Entity_Classes.md](../DESIGN_Orders_Params_Entity_Classes.md), READY-TO-BUILD; rulings R-273..R-281 (behaviors) |
| built | ✅ CE-2130 (rules rail, 48 allow-listed) · ✅ CE-2140 (order DTOs) — behaviors lane; ⏳ Track B started CE-2133 (backend lane) |
| embarkation (CE-3110 I-2d, the S3 dismount) and the other behaviour work planned before | ▶ **NOT deferred any more** — 🔒 user, 2026-10-12: *"if the embarkation and other behavior stuff … can continue in some session, it does not need to stay deferred anymore"* (R-282 (behaviors), superseding R-277's postponement). Hand a session [HANDOFF_Embark_I2d_And_Unit_Orders.md](HANDOFF_Embark_I2d_And_Unit_Orders.md) §2 items 1–2 (its item 0 / AQ90 and item 3 are done or absorbed here) plus the note below |
| Track B / Track C | ✅ assigned 2026-10-12 by the user: **Track B → the `backend` lane** (it started CE-2133) · **Track C → the `ui` lane** |

### 1a. A note for a session doing embarkation or other behaviour work in parallel

- New nodes and behaviours (`Embark(vehicle)`, `Disembark`, the squad Dismount drill) follow the design's §1 rules from the start: ONE params DTO each, flat JSON; a behaviour that takes nothing names `NoParams` (or an empty DTO of its own until CE-2140 adds `NoParams`).
- Do NOT name them `<Class><Order>` yet and do not add order DTOs; Track D (CE-2140 … CE-2143) does that.
- The handoff's §3 (orders to a unit, carry by vehicle type, carrying a person) is superseded by this programme: R-273 … R-281, and R-274 for carrying. Carry capacity and seats stay out of scope until asked.

## 2. How to pick a task

1. Open the design's §6. Its graph shows what each task waits for, and its table gives the tracker id and the acceptance.
2. Take a task whose dependencies are `[x]` in the [tracker](Blueprint_Issues_Tracker.md); push a `chore: started CE-xxxx at <sha>` commit first (rule 1b).
3. Read the design's §1 rules: the conformance rail (CE-2130) enforces them on every behaviour.
4. Before closing: rails red-proved; the as-built folded back into the design (§2–§4 diagrams, §5 table); the tracker row and this file's §1 updated.

## 3. The lanes (lean)

| lane | tasks | first |
|---|---|---|
| behaviors | CE-2130 R0 · CE-2131 A1 · CE-2140 D1 · CE-2141 D2 · then CE-2132 A2 (after B1) · CE-2142 D3 (after C2/C3) · CE-2143 D4 (after D3 + B2) | **CE-2130** |
| Track B (`backend` lane) | CE-2133 B1 · CE-2134 B2 · CE-2135 B3 · CE-2136 B4 · CE-2129 B5 | **CE-2133** (A2 waits for it) |
| Track C (`ui` lane) | CE-2137 C1 · CE-2138 C2 · CE-2139 C3 | **CE-2137** |
| any | CE-2144 E1 (Health in the cross-role set) | — |
