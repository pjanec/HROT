<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - HANDOFF_Coordinator_To_Lanes_Mission_Plan.md — P-8 (CE-4304) is defined there
  - HANDOFF_Coordinator_To_Ui_Rules_Bundle_And_Takeover.md — B-17 is defined there
  - ../../UX/DESIGN_Map_Interaction.md · ../../UX/DESIGN_Mission_Plan_Editor.md · ../../UX/DESIGN_Action_Catalogue.md — the designs (mockups ACCEPTED, R-4165)
-->

# HANDOFF — coordinator → behaviors: four items taken over from ui's queue

**For:** behaviors (idle). **From:** coordinator. **Dispatched at `<sha of the commit adding this file>`**. ⭐ Scope FROZEN there.
Merge `coordinator`, `ui`, `backend` first (restore after); the [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md) as before.
⛔⛔ Anything needing the user is skipped (R-4156).

🔒 User, `2026-11-07`: *"behavior is idle, maybe there will be something?"* — ui's queue is long (P-7 7c/7d, A4, B-16, K2/K3/K7, A7, A5, U-23/24 rest),
so the items below, which continue YOUR finished work (P3 placement, RX5 / RX6, *Begin phase*), move to you. ⭐ **Claim each in your status file
and push BEFORE coding** — ⛔ skip any item ui's status file already shows started ([note to ui](NOTE_Coordinator_To_Ui_Items_Moved_To_Behaviors.md)).

| # | item | design basis | done when |
|---|---|---|---|
| **W-1** | **P-8 (CE-4304)** — the mission plan on the map (E2: a numbered marker at each task's first place field, the route, drag = edit that field, right-click *Add task ▸*); the picker's *When a rule fires… · New rule…* (→ your `RulesWindow.OpenGallery`); the Rules window's **Phases** group with *Begin now*; the Timeline's **ONE Tasks lane** (E4) | [Mission plan](../../UX/DESIGN_Mission_Plan_Editor.md) §5.5 M3 M5 M6, §5.8 E2 E4; mockup `mission-plan-detail-visual.svg` (redrawn) | the mission plan handoff's P-8 row; a 1000-unit scenario's timeline stays ONE lane; side-by-side with the mockup |
| **W-2** | **B-17 — map interaction P1, P2, P4, P5, P6**: the armed tool visible (hint line), `ShapeEditSession` (four drawing gizmos), the pick banner, multi-select + `GroupTransform`, shape and route editing — on top of your `PlacementSession` (P3) | [Map interaction](../../UX/DESIGN_Map_Interaction.md) (R-4134); mockup `map-interaction-visual.svg` | each stage's rail; side-by-side with the mockup |
| **W-3** | **A6** — the timeline and the mode indicator as catalogue targets (`Event` / `Moment` / `Range` / `Playhead` / `Rule`): *Add rule at this moment…* (→ `OpenGallery`), the run-mode moves declared once, worded once | [Action catalogue](../../UX/DESIGN_Action_Catalogue.md) A6 | the timeline's right-click menu composed by `MenuComposer`; goldens |
| **W-4** | the **Run to …** mode-menu item over backend's `RunToController` (L-7, CE-4316 — backend's status row says the UI half is unclaimed): a time or an event (*a phase begins*, *a rule fires*) | [Lifecycle review](../../UX/DESIGN_Scenario_Lifecycle_Review.md) LC-10; R-4166 | from Edit: *Run to 10:00* runs the Preview fast and pauses at 10:00 |

⛔ Not yours: H6 / K4 (they sit on ui's H3 / K7, not built yet).

## Gates

The CLAUDE.md gate contract; fidelity checked by you (R-4146). Row 8: W-1 and W-4 touch the run on every node ⇒ the mission plan suites and a run-mode rail.

---
_coordinator lane, 2026-11-07_
