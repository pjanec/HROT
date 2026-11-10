<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-10
current-answer: the whole file
-->

# HANDOFF — coordinator → ui (Windows, hidden, OVERNIGHT): the user's findings from testing on Windows

**For:** ui, on the user's Windows machine, **every run hidden** (`HROT_HIDDEN_WINDOW=1`; ⛔ no window may appear — the user works on this
machine). **From:** coordinator. **Dispatched at `00d67d6ac`**, scope frozen there; documents that change later are FYI only (a later one that
INVALIDATES an item ⇒ stop THAT item and report it). ⭐ **Start when [the render-under-load batch](HANDOFF_Coordinator_To_Ui_Windows_Render_Under_Load.md)
is done** (or parked waiting on something). Merge the lanes you build on first; push `chore: started HANDOFF_Coordinator_To_Ui_User_Findings_Overnight at <sha>`;
status file `STATUS_Ui_User_Findings_Overnight.md`, updated as each item lands. Same [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md).

⛔⛔ **OVERNIGHT AND AUTONOMOUS — never stop for a question** (R-4156). A premise that fails stops THAT item only (R-106): record what you
measured, file the remainder as a row in your own block, go on. A design choice you would normally ask about ⇒ take the lean written here (or
your own, stated with its claim table in the status file) and say so — the user reviews in the morning.

**Why:** 🔒 user, `2026-11-10`, after an afternoon of testing the editor on Windows — every item below is the user's own finding, quoted in its
tracker row ([Blueprint_Issues_Tracker.md](../Blueprint_Issues_Tracker.md), coordinator block). ⚠ **The tracker row is the item's spec** — read
it whole; this table only orders the work and hands over what the coordinator measured. ⛔ "Not yet measured" in a row means exactly that: find
the cause first (graph + grep, CLAUDE.md), name it with `file:line` in the status file, then fix.

## The items, in order (biggest user pain first)

| # | id | what | measured start (coordinator) |
|---|---|---|---|
| **U-1** | **CE-4412** | ⭐ a console line per sim step — *Run to* crawls | `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs:390` (`[TimeSync] STEP #` at **Info**), `:383` (`[TC3][Master] STEP #` Debug), per-step allocation `:381` + `string.Join`. ⚠ Backend's FDP time code — a STATED cross-lane edit; the time integration suites (`SimTimeSyncIntegrationTests`, CLAUDE.md gate row 8) gate it |
| **U-2** | **CE-4407** | ⭐ replaying the 1 000-man recording at < 1 fps | not measured. Record one: `scale-rig-scale-1-1000` live for ~2 min → End → save to AAR; then profile replay frames with `HROT_FRAME_STATS` (your per-layer split + the per-system profile) and attribute: seek / apply, snapshot ring, managed restore, the map. Fix biggest first; a cost outside ui (the recorder / playback in `Fdp.Core`) ⇒ fix it if narrow (stated cross-lane) or file it with the number for backend. Also decide (lean: **no**) whether a replay should start playing on its own |
| **U-3** | **CE-4411** | delete refused after an undo | `FDP/Toolkits/Fdp.Toolkits/Replication/Edits/Edits.cs:101-104` refuses `netId <= 0` ⇒ the undo re-creates the entity without its network identity. Find the undo of a delete; ⭐ fix at the restore (the entity comes back as it was), ⛔ not by loosening the refusal |
| **U-4** | **CE-4410** | no Delete in the 2-D map's entity menu | the one delete path: `Edits.Submit(EditOp.Delete(…))` — `SelectionInteractionSystem.cs:305`. Menu start: `ScenarioEditor/Map/SharedHostActions.cs`, `MapInteractionPack.cs` — one menu for 2-D and 3-D |
| **U-5** | **CE-4404** | the Units ▸ Labels checkbox does nothing (2-D and 3-D) | `Hrot.Common/Diagnostics/Gizmos/LayerControlGizmo.cs`; the cards are drawn by the entity presentation gizmos (`ScenarioEditor/Gizmos/EntityPresentationGizmoShared.cs`) — find why the switch is not read |
| **U-6** | **CE-4408** | the 3-D map's mouse wheel (tied to a zoom value; the card does not come back; no slow-down near the ground) | `Hrot.Presentation/Map3D/` (the camera; `FollowCameraDriver.cs`, `Film.cs` read the wheel). Lean: the wheel flies along the view direction with the KEYS' speed rule (height above the ground); the card level reads the camera's real distance |
| **U-7** | **CE-4405** | ORBAT combat mode: 16 squads per force, the rest at the root | `Hrot.Presentation/Panels/SharedOrbatPanel.cs`; and `scripts/scale-rig.py` (does it attach every squad to its force?). Fix where the defect is; if it is the generator, a stated cross-lane edit (backend's script) |
| **U-8** | **CE-4398** | the Open Asset filter does not reveal nested hits | `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetPickerModal.cs`, `AssetBrowserPanel.cs`. Lean: a non-empty filter shows a FLAT list of hits, each with its folder path greyed after the name (the user's "acceptable solution"); empty restores the tree |
| **U-9** | **CE-4397** | the scenario picker's folders in R-4185's order | its row; same dialog family as U-8 — do them together |
| **U-10** | **CE-4399** | the details panel clips its right column (`bt-window-duel` ▸ *peek and fire* ▸ Node Properties) | not measured — the value column's width. A frame rail measures the item rects at two panel widths (R-4138) |
| **U-11** | **CE-4400** + **CE-4406** | the loading text + Cancel on no background; opening a recording shows nothing until the browser appears | ONE message box (a solid dark panel, darker than the dim) for every load — scenario and recording. Lean: draw the mockup first with `scripts/mockup_kit.py` (R-4143), build it, check the frame against it yourself (R-4146) |
| **U-12** | **CE-4409** | a netconvert failure fails the whole OSM import | ⚠ the coordinator's library — **allowed and stated cross-lane edit**, given to you because only this machine reproduces it: `Netconvert.Run` (`Hrot.Terrain.InputSet/Netconvert.cs:94-99`) passes no `SUMO_HOME` ⇒ set it to the located install; `OsmTerrainPipeline.cs:105` ⇒ a road failure imports the terrain WITHOUT roads and the wizard shows a warning with netconvert's message. Reproduce the user's exit 1 first and record the full netconvert text |

**Not in this batch** (the coordinator's): CE-4401 (scale-rig roads at height 0), CE-4402 (low doors on a slope), CE-4403 (flatten the ground
under buildings).

## Rules

- **Proof is proportionate to risk** (CLAUDE.md): a cosmetic item (U-8 … U-11) red-proves ONE key rail and gates its touched test classes;
  U-1, U-2, U-3 (time, replay, replication) gate their integration suites too and red-prove each new rail.
- **0 B garbage per steady frame** on anything you touch that runs every frame (U-1's step, U-2's replay frame, U-5's label pass, U-6's camera)
  — a `GC.GetAllocatedBytesForCurrentThread()` rail.
- **A mockup is an acceptance spec** (R-4138 / R-4146): U-8 and U-11 change what the user sees — draw first, match the frame yourself, side-by-side in
  the status file and the chat (⛔ never in git).
- Display rails under the hidden window as usual; ⛔ a "passed" that is really "skipped" is not a gate row.
- Each fix folds into its owning design where one exists (CLAUDE.md "the design must reflect the as-built"); new findings ⇒ rows in YOUR block.
- Gates in the status file per the gate-report contract (command · counts · `--no-build` · reds proven pre-existing at the base sha);
  `tracker-counts.py --check`; close each row with its evidence.

**Done when:** every item is closed with its evidence, or names in the status file exactly what blocks it and what was measured.

---
_coordinator lane, 2026-11-10_
