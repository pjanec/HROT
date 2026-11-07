<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../UX/DESIGN_Editing_UX.md — the programme; §4.6 every mockup ACCEPTED (R-4165)
  - PROTOCOL_Two_Lanes_Self_Coordinating.md — the protocol, now with a THIRD lane (this one)
  - HANDOFF_Coordinator_To_Ui_Todays_Rulings.md · HANDOFF_Coordinator_To_Lanes_Mission_Plan.md — what ui and backend are building meanwhile (⛔ not yours)
-->

# HANDOFF — coordinator → behaviors: the editing-UX remainder

**For:** the behaviors lane (`behaviors`, idle). **From:** the coordinator lane. **Dispatched at `b76ddfb4`**. ⭐ Scope
FROZEN there; later documents are FYI.

🔒 User, `2026-11-07`: *"There is the behavior lane, which is idle now. So if it can take some of these tasks in parallel to the UI lane and
backend lane, which are still running, we can write a handoff for them. And if the mockups have not changed since the time they were
presented, I am accepting them So they could be part of the handoff as well."* (R-4165)

## Start

1. ⚠ Your branch is two days behind: merge `coordinator`, then `backend` and `ui` (**merge, never rebase**), then `dotnet restore` the test
   projects you will run (the third stale-binary trap — backend added packages). Push `chore: started HANDOFF_Coordinator_To_Behaviors_Editing_UX_Remainder at <sha>`.
2. Read [the protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md) — it names two lanes; ⭐ you are the third: write
   `STATUS_Editing_UX_Build_Behaviors.md` after every item; fetch `ui backend coordinator` and merge them after every item; ui and backend
   read your status file too ([NOTE_Coordinator_To_Ui_And_Backend_Behaviors_Joins](NOTE_Coordinator_To_Ui_And_Backend_Behaviors_Joins.md)).
3. ⛔⛔ **Anything that needs the user's decision is skipped, never blocking (R-4156)** — a QUESTION note to coordinator with your lean, then the
   next item.
4. ⭐ A mockup is the acceptance spec and is ACCEPTED (R-4165): render the real frame, compare, fix every deviation yourself (R-4146). Where a
   later ruling differs from a picture, the ruling wins.

## The items — in order

| # | id | what | design basis | done when |
|---|---|---|---|---|
| **V-1** | — | your own two leftovers: `ABehaviourAdvertisesItsRealParametersTests.TriggerAdvertisesItsAuthoredKeys` red in `Hrot.Editor.Tests` (the Trigger behaviour's `Do` lacks `"format":"json"`, rules S2 `4302e2d9`) · [NOTE_Backend_To_Behaviors_SV1_Blueprint_Instants](NOTE_Backend_To_Behaviors_SV1_Blueprint_Instants.md) (4 blueprint-facing float instants for the compiler's type map) | that note; rules S2 | the test green; the note answered |
| **V-2** | CE-4306 | **edit framework F5**: an order carries its request id to the brain's owner; `TacticalIntentResolutionSystem` answers **accepted / refused with the reason** through `EntityEditOutcome` | [Edit framework](../../UX/DESIGN_Edit_Framework.md) F5 (R-4128) | a lower-ranked order refused with *"a higher-ranked order is running"*, shown by the notifications backend built (B-12, `EditNotifications`) |
| **V-3** | CE-4307 | **rule editor RX5**: actions as sentences; *Order* by order kind (`OrderMenu.OfferedFor` — the one list); *Begin phase* = `RuleRequest.Fire` on a phase trigger — ⚠ the phase field is backend's P-6 (CE-4302): build Begin phase only if P-6 is in, else skip that part | [Rule editor UX](../../UX/DESIGN_Rule_Editor_UX.md) §2.4, RX5 (R-4130) | side-by-side with `rule-list-visual.svg` |
| **V-4** | CE-4308 | **rule editor RX6**: rules reachable from tasks, units, areas and the timeline (catalogue actions — ui's A-stages own the catalogue: ADD declarations, never a second menu path); the gallery | Rule editor §2.5, RX6 | side-by-side with `rule-list-visual.svg` (the gallery) |
| **V-5** | CE-4309 | **map interaction P3 — placement**: ① MEASURE CE-4272 first (does an A-10C spawned at 300 m keep flying?) ② `PlacementSession` — airborne by `Mobility`, the 2-D level choice from `SurfacesAt`, optional heading drag ③ Create through `Edits.Submit` | [Map interaction](../../UX/DESIGN_Map_Interaction.md) P3 (R-4134) | side-by-side with `map-interaction-visual.svg` (the placement ghost + level / airborne panel) |
| **V-6** | CE-4310 | **details forms D6**: the raw Components tab fixes (it stays the debugging tool) | [Details forms](../../UX/DESIGN_Details_Forms.md) §3.1, D6 | the design's §3.1 list, each with a rail |

⛔ **Not yours** (ui is on them, or they collide with ui's open work): K4 / K7 input, H6 seats, A6 timeline actions, the Orders tab, the
mission plan's map / timeline (P-7 / P-8). ⛔ Not here either: P7 gizmo looks (needs the user), Replay Browser Q-B3 / Q-B6 (open).

## Gates (report per item — protocol §6)

The CLAUDE.md gate contract, rows 1–8. ⭐ Row 8: V-2 changes order handling on every brain node ⇒ name and run the order suites and the
SimHost integration rails that issue orders.

---
_coordinator lane, 2026-11-07_
