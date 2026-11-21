<!--STATUS
state: LIVE
updated: 2026-10-12
doc-type: STEER — behaviors lane to the ui lane, for HANDOFF_Embark_I2d_And_Unit_Orders.md (dispatched at a38e359d0). Written at behaviors@c7cc4fe85.
current-answer: the table below
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/blueprints/HANDOFF_Embark_I2d_And_Unit_Orders.md — the handoff this steers (not edited: rule 1)
  - docs/DESIGN_Orders_Params_Entity_Classes.md — OWNS the orders programme that overtook the handoff's §3-A (§1 rules, §6 tasks, §7 D2)
  - docs/blueprints/Architect_Question_90_Unit_Orders_Carry_Capacity.md — the handoff's item 0, already written and ruled (R-273..R-282 (behaviors))
  - docs/DESIGN_Entity_Interactions.md — OWNS embarkation (§7.1 I-2d), the work the ui lane takes
-->

# Steer — the embark handoff, after the orders programme

The handoff was written before the AQ90 rulings. ⭐ **Its items 1 and 2 stand; items 0 and 3 are overtaken.** The handoff
text is not edited (rule 1); this note says what changed since its dispatch sha.

| handoff item | now | why |
|---|---|---|
| **0** — write `Architect_Question_90` | ✅ **done** — written and ruled: R-273 … R-282 (behaviors) | [`Architect_Question_90`](Architect_Question_90_Unit_Orders_Carry_Capacity.md) |
| **1** — I-2d: `Embark(vehicle)` / `Disembark` nodes; a `Dismount` squad drill | ⭐ **the ui lane's work** — not deferred (R-282) | Entity Interactions §7.1 |
| **2** — convoy-ambush scenario | ⭐ **the ui lane's work** | catalogue P1, D8 |
| **3-A** — orders to a unit (lean: unit-aware mappers Move → `SquadMarch` …) | ⛔ **do not build** — superseded by R-273 / R-279: the RECEIVER picks `<Class><Order>` by its class (no mappers). Track D of the orders programme: CE-2141 ✅ built (`InfantrySquadMove/Hold/Attack`, the member release), **CE-2142 (D3, the receiver resolves `Order.<name>`) is the behaviors lane's next task**, CE-2143 (D4, generic menu / RTS / HTTP) after it | orders design §6, §7 |
| **3-B / 3-C** — vehicle-type capacity and seats; carrying a person | ⛔ **not now** — decided (R-274), built later, outside the orders programme | orders design §6 (last line) |

## What changed under the handoff's feet *(merge `behaviors` first — at or after `c7cc4fe85`)*

| | |
|---|---|
| the squad runner | `SquadManeuverRunner.Hold` / `Move` take the COMMANDER; every order a drill gives is recorded and **released when the commander's run ends** (not at a pause) — a `Dismount` drill gets that for free. A member that is a `UnitEntity` receives `Order.Hold` / `Order.Move`. 📄 orders design §7 |
| the handoff's §3-A "Stop does not recall the members" | ✅ fixed by CE-2141 (A5) |
| new behaviours | follow the orders design §1: ONE params DTO, flat JSON; one that takes nothing names `NoParams`. Do not name them `<Class><Order>` unless they fulfil an order DTO |
| ids | the handoff's "next is `CE-2127`" was the behaviors block. ⭐ The ui lane allocates from its own block (`CE-1000`…, tracker header) and rulings from `R-1000`…; embarkation stays on `CE-3110` |
| the fences | "the behaviors lane builds all of it" named the original addressee — the ui lane builds items 1–2 now; a cross-lane edit is allowed and named in the commit. ⛔ "No per-host initialisation" still holds (§2 fences, trap ⑫) |
