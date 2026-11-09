<!--STATUS
state: LIVE — dispatched, not started (RE-STAMPED, rule 1a: O-1 amended — ui had already built CE-4339; first stamp aa602a3e7 never reached behaviors)
updated: 2026-11-08
current-answer: the whole file
related-designs:
  - ../../UX/DESIGN_Map_Interaction.md — §2.6 the gizmo looks (P7, approved by the user in your lane), §2.7 the mockup
  - ../../UX/DESIGN_Mission_Plan_Editor.md — the task picker (O-2)
  - ../../UX/DESIGN_Action_Catalogue.md — `Requires` (R-4125), the one availability filter (O-2)
-->

# HANDOFF — coordinator → behaviors: the overnight batch

**For:** behaviors. **From:** coordinator. **Dispatched at `55229b8ef`** (re-stamped; first stamp `aa602a3e7`). ⭐ Scope FROZEN there. Merge `coordinator`, `ui` and `backend` first, push
`chore: started HANDOFF_Coordinator_To_Behaviors_Overnight at <sha>`, then the [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md): a status file
(`STATUS_Behaviors_Overnight.md`) after every item, fetch and merge `ui` and `backend` after every item.

🔒 User, `2026-11-08`: *"i need them to have enough work with no question for an overnight run"*. ⇒ ⛔⛔ **NEVER stop for a question tonight**
(R-4156): an item that needs the user is SKIPPED with its question written into its row, and you go on to the next. A premise that fails stops that
item only (R-106). Leans you would have asked about: take the one that keeps ONE implementation (ruling 9) and say so in the report.

| # | id | what | basis | done when |
|---|---|---|---|---|
| **O-1** | B-17 P7 | ⭐ **the gizmo looks, as the user approved them in your chat** (§2.6: placement ghost = map symbol 2-D / footprint 3-D / aircraft at altitude with a drop line · one drawing style · 3-D handles · pick crosshair + banner · rotate ring) **+ a backdrop behind the cursor hint and the banner** + ⭐ the placement direction line is ALREADY BUILT by ui (CE-4339, `PlacementDirectionLine`, [Map interaction — U-8 / U-9](../../UX/DESIGN_Map_Interaction.md)): ⛔ do not rebuild it — restyle it within the P7 looks, ONE drawer | §2.6, §2.7; R-4146 fidelity | frame rails under `xvfb-run` per look; the side-by-side with your approved images in the report |
| **O-2** | CE-4350 | the mission plan editor offers only tasks that FIT the entity: classify each task by what it needs (mover, role, equipment) and filter through the action catalogue's `Requires` — the same `ActionAvailability` the Details tabs use (ui's Z-3), ⛔ not a second filter | Mission plan editor; R-4125 | a rail per class: a tank is not offered a dismount task, an infantry squad not a vehicle one; the picker's frame rail |
| **O-3** | CE-4344 | a possessed shot that cannot fire NOW (reloading) is dropped — measure which side latches it (`PossessionStation` input or the weapon); fix it if it is the station; if it is the weapon, write the measured cause into the row (backend's X-2 builds it) | possession (yours) | a rail: trigger during a reload → no shot after it |
| **O-4** | CE-4123 · CE-4124 · CE-4132 · CE-4133 · CE-4134 · CE-4136 · CE-4137 · CE-4139 | the filming findings, each ⚠ MEASURE FIRST (some may be intended tuning): run the scenario, decide defect vs description, fix the behaviour or correct the scenario's description — each row closed with its cause | each row | one line per row in the status file; a rail where a defect was fixed |
| **O-5** | CE-4041 · CE-4055 | three demos differ in the stepped Preview · `ua-tank-ammo` fails its own checks and golden | each row | each green, or the measured cause in the row |
| **O-6** | CE-4109 | your half of the explicit-ack writers (② ⑤) | its [NOTE](NOTE_Coordinator_To_Backend_Explicit_Ack_Writers_And_Reflection.md) | its acceptance |
| **O-7** | CE-2423 | the timeline's RULE target through the action catalogue (yours, from W-3 / A6) | the row | its acceptance |
| **O-8** | CE-4035 | the demo scenarios the films need (G-1 …) — as many as the night allows, each with its checks | the row | each scenario loads, runs and passes its checks |

⛔ **Not here:** recording storage (backend), E7 bookmarks (ui), the Start screen / chrome (ui, done).

## Gates

The CLAUDE.md gate contract, rows 1–8; display rails under `xvfb-run -a` (a skip is not a pass). Row 8: O-1 touches every map host's gizmos ⇒ the
Presentation frame rails; O-4 / O-5 change behaviours ⇒ the scenario regression for the touched scenarios. Fold every as-built deviation into its
design (prior text SUPERSEDED) before the batch closes.

---
_coordinator lane, 2026-11-08_
