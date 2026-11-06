<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-06
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - PROTOCOL_Two_Lanes_Self_Coordinating.md — HOW to run this list beside the ui lane (merge as you go, status file, notes)
  - HANDOFF_Coordinator_To_Ui_Editing_UX_Build.md — the ui lane's list, running at the same time; its ⏳ marks name the items below it waits for
  - ../../UX/DESIGN_Editing_UX.md — the programme
-->

# HANDOFF — coordinator → backend: the data, writes and saves under today's editing-UX designs (long list, runs until done)

**For:** the backend lane (`backend`). **From:** the coordinator lane. **Dispatched at `RESTAMP`** *(re-dispatched before any start, rule 1a: + R-4156 skip-on-user-decision; first stamp `d98efb832`)*. ⭐ Scope FROZEN there; later documents are
FYI. Merge `coordinator` first, push `chore: started HANDOFF_Coordinator_To_Backend_Editing_UX_Build at <sha>`, then follow
[the protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md) — ⭐ **after every item: push, status file, fetch, merge `ui`, answer its notes.**
⭐ **The ui lane waits on several of these** (its ⏳ marks) — say DONE in your status file the moment one lands. ⛔⛔ **Nobody to ask — anything that
needs the user's decision is SKIPPED, never blocking (R-4156):** a QUESTION note to Coordinator with your lean, the item (or that part) marked *SKIPPED — needs user*, and on to the next item.

## Why

🔒 User, `2026-11-06`: *"find out what from our today's designs can be implemented … in autonomous mode. It must be something which do not raise
questions"* · *"put there much more … All what is pretty much clear"* · the two lanes *"merge each other as they go and write status or
requests to the other lanes as they go, without any external coordination."* Every item is APPROVED (ruling cited); build against the design's
diagrams, fold any deviation back into it.

## The items — in order (the ones the ui lane waits on come first)

| # | what | design basis (build against it) | done when | ui waits? |
|---|---|---|---|---|
| **B-1** | the condition language **C1**: `LogicalOperator.None` (true when no child holds; a one-child `None` = NOT), `InUnit { Unit, IncludeSelf }` (the objectives' S1-d walker lifted — never a second walker), `InTheFight {}` (`CombatEffective.IsInTheFight`) — in `SearchPredicateDto` and BOTH compiler entries | [Condition editor](../../UX/DESIGN_Condition_Editor.md) §1.1, §5 (R-4142) | each leaf true / false on a crafted world; `None` of one child = not; a dead man still `Lifecycle`-exists but is not `InTheFight`; the replay search compiles them too | U-14 |
| **B-2** | the edit framework **F1**: `Edits.Submit` + `Gate` (mode + station level) + `EditJournal`; every attribute-shaped edit goes through `Write` (fixes **CE-4274, CE-4275**) | [Edit framework](../../UX/DESIGN_Edit_Framework.md) §2–§4, F1 (R-4126…R-4128) | per kind: a non-owner's edit reaches the owner and reports Applied (a two-node rail); a Review-mode or observer edit is Refused with the reason | U-23, U-26 |
| **B-3** | **F2** `Create` / `Delete` with the request id end to end, answered by `EntityEditOutcome`, `CreateUpdateDeleteEntityAck` retired, the responder a typed `NodeId` (**CE-4276, CE-4278**); **F3** `AddComponent` / `RemoveComponent` owner-side | Edit framework §2.4, F2, F3 | a refused placement shows its reason; Playable toggle and ORBAT detach from a non-owner | U-23 |
| **B-4** | the forms' attributes **D2**: `Stance` (+ `StanceIntent` saved, the body seeded at load, applied at once in a paused world — Q7), `Health.Current`, `Ammo.<mount>`, `Playable.Side` / `SlotName`, `SpawnArea.Side` / `FacingDeg`, the weather attributes — rows in the ONE `AttributeVocabulary` | [Details forms](../../UX/DESIGN_Details_Forms.md) §1.2, §3, D2 (R-4136) | each attribute written from a non-owner lands on the owner; *Prone* at start survives save and reload; set in a paused world, shown at once | U-23 |
| **B-5** | the initial state: **S1** save `PassengerBuffer.Modes` / `.Assistant` (drop the `[ScenarioIgnore]`, genesis boards with them); **S2** `WeaponPosture` saved and seeded + the four `Weapon.*` attributes; **S3** `SeedTargetCommand` applied on every brain node (out of `EditorPerceptionSetupSystem`); **S4** the Point map-graphic type id (next free 88xx); **S6** Character settings as attribute writes on the brain's node | [Initial state](../../UX/DESIGN_Initial_State.md) §2, §3, §3a, S1–S4, S6 (R-4137, narrowing R-1036) | the WS-4 rail extended (a stretcher casualty and its assistant survive save / load); *Aimed at a point* survives a reload; seeded from a CGF station, the knowledge lands on the brain's node | U-24, U-25 |
| **B-6** | **F4** `Interact`: a `RequestId` on the interaction payloads, the owner reports `EntityEditOutcome`; **F6** `Commit` with `Conflict` (**CE-4277**), the Force-Commit modal's need gone | Edit framework F4, F6 | an embark refused shows why; two stations editing one plan ⇒ Conflict | U-26 |
| **B-7** | new scenario **N2** `CreateBlankScenario(name, terrain)` — ① MEASURE where the master writes a new scenario folder, ② a header-only `scenario.json` naming the terrain, ③ the ordinary load; **N5** `POST /scenario/new {name, recipe \| terrain}` | [New scenario](../../UX/DESIGN_New_Scenario_And_Recipe.md) §2, N2, N5 (R-4135) | the AI debug API creates both kinds; every node gets the terrain by the existing consensus | U-19 |
| **B-8** | undo, world side: **U1** MEASURE one authored-mask capture's size and time on the largest scenario through `WorldKeyframes` (+ confirm §1.1's restore list); the `WorldSnapshotRing` (budget 256 MB, depth ≤ 100); **U3** restore listeners for the caches U1 names | [Undo](../../UX/DESIGN_Edit_Mode_Undo.md) §1–§3, U1–U3 (R-4133) | the measurement written into §1; a restore round-trip rail per cache | U-27 |
| **B-9** | seats **H1**: the `station.seat` node feature + `HostDescription.DefaultSeat` (fused server = Instructor, Player = Player, editor = Author, Replay Browser = Reviewer); **H7** `/session/seat`, `/session/hat`, `/orders/side-lock` folded in | [Seats and hats](../../UX/DESIGN_Seats_And_Hats.md) H1, H7 (R-4131) | each host reports its default seat over HTTP | U-28 |
| **B-10** | the remaining **accepted semantic types** (SV-1 CE-4201, SV-4 CE-4202, SV-5 CE-4203, SV-6 CE-4204 — all open) — each with its rail that fails on a new generic field whose name says it should be the type | [Semantic value types](../../DESIGN_Semantic_Value_Types.md) §2–§3 (accepted 2026-11-01) | identical memory + JSON; the rails green | (show on map's Q-M3 shrinks as positions become `WorldPoint`) |

### When blocked — always available

The ui lane's REQUESTs (e.g. the gizmo side of map interaction P2) · fold-backs into designs · the edit framework's remaining kinds once F1–F6
are in, through the same `Edits.Submit` (never a second path).

⛔ **Not in this list**: F5 `Command` (behaviors' `TacticalIntentResolutionSystem`), map interaction P3 placement (CE-4272 first — a later
batch), the mission plan editor (behaviors), the Replay Browser's open questions (Q-B3, Q-B4, Q-B6).

## Gates (report per item — protocol §6)

⭐ Cross-node items (B-2, B-3, B-4, B-5, B-6, B-9) name and run the INTEGRATION suite that guards their invariant (gate contract row 8); save /
load items run the scenario round-trip suites; never the whole solution in the loop.

---
_coordinator lane, 2026-11-06_
