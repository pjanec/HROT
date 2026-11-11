<!--STATUS state: LIVE · updated: 2026-11-10 · current-answer: the table -->
# STATUS — ui, the user's findings overnight ([HANDOFF](HANDOFF_Coordinator_To_Ui_User_Findings_Overnight.md), dispatched at `00d67d6ac`)

⚠ Run in the ui lane's **Linux cloud session** (not the Windows machine the handoff names): every item that reproduces here is built here; an item
that needs Windows (U-12's netconvert exit 1) is marked so. Display rails under `xvfb-run -a`.

| item | id | what | state | sha | notes |
|---|---|---|---|---|---|
| U-1 | CE-4412 | a console line per sim step | ✅ DONE | see git log | cause `MasterSyncController.cs:383/390` (Info+Debug line per step, `new HashSet` `:381`, UnionWith + foreach-over-interface each allocate); Trace behind a level check, reused set, index loops. Time suites 297/0 + integration (SimTimeSync+Time) 31/0; rail red-proved |
| U-2 | CE-4407 | 1 000-man replay < 1 fps | QUEUED | — | |
| U-3 | CE-4411 | delete refused after an undo | ✅ DONE | see git log | cause: undo snapshot mask = Authored = !NoScenario drops NetworkIdentity/NetworkAuthority/ReplicationRoute (`WorldKeyframes.cs`); fixed at the restore; rail red-proved; Fdp.Toolkits.Tests 426/0 |
| U-4 | CE-4410 | Delete in the 2-D map's entity menu | ✅ DONE | see git log | cause: `MapMenuActions.cs` declared Delete for `Graphic` only; now `Everything` + shortcut; one path `SelectionInteractionSystem.DeleteEntity`; rail red-proved; menu goldens regenerated (+Delete on unit kinds); Presentation.Tests 118/0 for the touched classes |
| U-5 | CE-4404 | Units ▸ Labels checkbox | QUEUED | — | |
| U-6 | CE-4408 | 3-D mouse wheel | QUEUED | — | |
| U-7 | CE-4405 | ORBAT combat: 16 squads per force | ✅ DONE | see git log | cause: engine `UnitRoster.Capacity`=16 + `GenesisMaterializationSystem.cs:200-208` drops the 17th subordinate; `scale-rig.py` attached all squads to the force ⇒ generator fixed (platoons/companies), `scripts/test_scale_rig_roster.py` |
| U-8 | CE-4398 | Open Asset filter shows nested hits | ✅ DONE | see git log | cause `TreeLayout.cs` `state.FolderOpen[fullPath] = open` per frame ⇒ closed folders never opened by a search; flat hit list + greyed folder; mockup `docs/UX/picker-filter-visual.svg` matched on a real xvfb frame; rail red-proved |
| U-9 | CE-4397 | scenario picker folder order | ✅ DONE | see git log | FolderOrder by full path + "My scenarios"; rails green |
| U-10 | CE-4399 | details panel clips its right column | ◐ PARTLY (R-106) | see git log | measured: 0 px overflow at 260/340/520 on Linux (real drawers, draw-list rail); the cut text = `PickerField.Draw` centred a long FQN in a fixed button ⇒ elided (`PickerField.ElideToWidth`), rail red-proved; remainder CE-1476 (Windows measurement). AiShared.Tests whole suite 2348/3: 3 reds are in files this batch never touched (`Netconvert.cs`/`OsmWizard` repo-root scans from the coordinator, `SaveSlotTests` text) |
| U-11 | CE-4400 + CE-4406 | one loading message box | QUEUED | — | |
| U-12 | CE-4409 | netconvert failure fails the OSM import | QUEUED | — | Windows reproduces it; code half here |
