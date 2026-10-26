<!--STATUS
state: LIVE — DONE 2026-10-26 by ui (all three items; report NOTE_Ui_To_Coordinator_Replay_Ui_State_Done.md)
updated: 2026-10-25
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Replay_Census_H0.md — THE basis: the replay census, §2 findings
  - docs/DESIGN_Entity_Brain_View.md — YOURS: R-1040 / BV-62 (a Brain-tab input is a recorded component), §12r BV-65 (UnitDebugSettings — the pattern item ① follows)
  - docs/DESIGN_Agent_Shared_View.md — YOURS: GET /replay/selection (CE-1265), withdrawn by item ①
  - docs/blueprints/DESIGN_Editor_Entity_Selection_Source.md — OWNS the viewport selection (SelectEntityCommand → SelectionRequestSystem, CE-051)
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — behaviors' squad drills; SquadDrillTrace (item ②) is their component
-->

# HANDOFF — coordinator → ui: UI state out of the recording, the Brain tab's squad section into it (CE-4049, CE-4047)

**For:** the ui lane (`ui`). **From:** the coordinator lane. **Dispatched at `876f32057`**. ⭐ Scope FROZEN there;
later documents are FYI. A later document that invalidates an item ⇒ STOP that item, report it (R-106), keep the rest. Questions ⇒ a
NOTE to coordinator.

## Why

🔒 User, `2026-10-25`: *"I would like to make the replay working (not just in replay browser, in general)"*. The coordinator's replay
census ([H0](../../DESIGN_Replay_Census_H0.md), CE-4045) asked every component type *"must replay read this from the recording?"* — two
findings land on your surfaces.

## The items

| # | item | design basis | acceptance |
|---|---|---|---|
| **①** | ⭐⭐ **CE-4049 — selection is UI state, not simulation state.** 🔒 User: *"selection is not the simulation state of an entity, it is a UI state having nothing to do with simulation and recording"*; *"debug pins have been converted to singleton … maybe selection need to follow same convention"*. 📐 `SelectionState` (`SelectionState.cs:34`) is `NoScenario` only, so it is RECORDED by default — never by decision; a seek overwrites the viewer's own selection with the recorded one. ⏳ **Lean:** follow `UnitDebugSettings` (your CE-1329, Brain View §12r): ONE selection store as a `Transient` world SINGLETON keyed by network id, untouched by a rewind or seek; `SelectionState` per entity retired, or kept as a `Transient` copy rewritten each frame if readers need the per-entity form (as `DebugState` is). Withdraw `GET /replay/selection` (CE-1265 — it rested on the accidental recording) and regenerate the MCP catalog (R-1030). Same item, same reason: `ContextMenuState` (the IG's menu, recorded; its sibling `CanvasContextMenuState` is NoReplay) → `NoReplay` or the same store pattern | Census §2; Brain View §12r BV-65; Selection Source design | a selection survives a seek in the Replay Browser and in editor replay; no selection type is recorded; the catalog matches the routes |
| **②** | **The UI command events in the recording.** `SelectEntityCommand` (8103), `CenterOnEntityCommand` (8104), `WorldResetEvent` (8101), `OpenRenameDialogCommand`, `ActivateEditorToolEvent`, `MapShortcutKeyEvent` carry no `NoReplay`, while their sibling UI events do ⇒ `PlaybackSystem.ReadAndInjectEvents` may RE-FIRE them during a replay (re-select, re-centre, reset). ⭐ Measure first (one replay of a run where you selected and centred), then mark the UI ones `NoReplay` | Census §2 (CE-4049) | the measurement in the report; no UI command fires during playback |
| **③** | **CE-4047 — `SquadDrillTrace` is not recorded, so the Brain tab's Squad section (`EntityBrainView.cs:117`) and `/entities/{id}/squad`'s `maneuver` are absent in replay** — against your R-1040 / BV-62; §12e's audit table never listed it. `[DataPolicy(Transient)]` (`SquadDrillTrace.cs:17`); the class is immutable, so dropping `Transient` would record an empty shell. Two ways: a fixed-size recorded component (the `CommandOrderHistory` pattern you built), or the header line derived from the recorded `BrainJournal` (it already carries the play phase changes, `BrainCommandView.cs:112`). ⚠ The component is the behaviors lane's (`Fdp.Toolkits/Squad`) — a cross-lane edit, say it; ⚠ a NEW recorded managed component must pass the record rail (`RecordedManagedComponentsAreImmutableTests`, CE-4046): a record, or an unmanaged struct | Brain View R-1040 / BV-62 / §12e; Census §2 | the Squad section shows in a replay at a scrubbed frame (extend `CE1319_ARecording_RebuildsTheHistoriesAndTheWhy_AtTheScrubbedFrame`) |

## FYI — what this does to your Timeline handoff

**The coordinator builds the playback-fidelity measurement** (CE-4045 H1: each scenario's replay compared with its live snapshots). That
is Timeline **R0 ①** (CE-1301, *"a recording plays back faithfully"*) — ⛔ do not build a second one; consume H1's report (R-174 /
R-4077). R0 ② (*which events a recording holds* — the golden's shots and blasts come from `ShotLog`/`DetonationLog`, beside the world)
stays yours.

## Gates (report these rows)

The touched projects' suites, `--no-build` after building the TEST project · the Brain View replay rail for ③ · the record rail and
`EveryComponentTypeDeclaresItsDataPolicyTests` (`Hrot.ClusterRunner.Tests`, filtered) · every red confirmed pre-existing against the
base sha · `tracker-counts.py --check` · the ids you allocate.

---
_coordinator lane, 2026-10-25_
