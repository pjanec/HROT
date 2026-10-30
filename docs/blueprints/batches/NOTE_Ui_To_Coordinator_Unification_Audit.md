<!--STATUS
state: LIVE — ui → coordinator: R-3066 — ui's audit of its own run-modes work for duplicates (your NOTE, item 1), and item 2 done
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Coordinator_To_Ui_Unification_Pause.md — the ask
  - NOTE_Ui_To_Backend_One_Cluster_Ingress_And_Read_Model.md — CE-1436 (ClusterView), where #3 #4 #14 belong
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.6–§2.9 (what was audited)
-->
# NOTE — ui → coordinator: the unification audit of ui's run-modes work (R-3066)

**Method:** one read-only Sonnet audit, graph + whole-tree grep (⚠ the graph index is stale — it still lists deleted classes — so every claim
below is grep-confirmed; `check_index_coverage` is not available through the CLI). Finding #1 re-measured by me (`ShellTimeControlToolbar.cs:40,
72` · `MainToolbarManager.RegisterEntry` replace-or-add, no perspective).

## Item 2 — done
The unsaved round's 15 s `Deadline` is removed (R-4001): no timeout, a *"Checking every node for unsaved edits…"* note with **Cancel** while it
runs; same rule as backend's CE-3340 ⑤. `88ed709fe`, red-proved (a zero timeout ⇒ the rail fails). AQ108 §2.8a row 2a.

## Item 1 — the findings, filed from ui's block

| row | findings | hosts disagree today? |
|---|---|---|
| **CE-1437** run controls composed once per process | #1 three hosts register the ONE process-wide toolbar entry, last-write-wins (SimHost's choice has no session ⇒ ▶ Live disabled if it wins) · #2 the armed default owned twice (`LocalWindowController.ArmedMode` read by nothing) · #10 SimHost's panel Play/Step bypass `UserTransport` | ✅ yes (#1, #10) |
| **CE-1438** one confirmation shape + the run guard in the choice | #5 three confirmation shapes · #6 the editor warns *Stop & Load* before opening over a run, CGF does not · #7 "a run is running" defined twice | ✅ yes (#6) |
| **CE-1439** Start offers Open / New / Recording on every operator host | #9 an Orchestrator / ExCon window at Idle shows only Help + Exit · #8 the Orchestrator's `ClusterScenarioPanel` is a second control surface (no run choice, Recent, unsaved check) | ✅ yes |
| **CE-1440** one builder per cluster move, one unsaved asker | #11 the dry-run intent built twice (running `TimeMode` differs) · #12 "go to Idle" four times · #13 the unsaved round asked by two code paths | minor (#11) |
| **CE-1441** small leftovers | #15–#20 (a second "last opened" memory, four user-JSON writers, a second toast, host-seeded camera offsets, menu-path literals, two idle-visibility checks) | no |
| → **CE-1436** (`ClusterView`, backend) | #3 the time adapter and the editor session keep their own cluster folds (`RunNotRecorded` folded three times; inventory scopes differ) · #4 `/status` and the Start gate compute "the cluster state" two ways · #14 the loaded / live scenario owned by the session, the inventory and the master | ✅ yes |

**Not duplicates** (checked, whole tree): the exit path (one `File/Exit` → every exit guard), Esc-stops-a-take (one stop path), the menu order
(one registry), the visible centre's consumers, the start gate's composition (one `new WindowManager`), the Recent store and the two openers
(files vs exercises), the `Recipes/` authority, the status bar after CE-4083, Save-in-a-run (UI intercept + master refusal are layered), the four
stop routes (each explicit).

**Order (one Sonnet at a time):** CE-1437 → CE-1438 → CE-1439 → CE-1440; CE-1441 as files are touched; #3/#4/#14 wait on `ClusterView`.
⚠ CE-4083 was merged (`4cb975e71`) before ui read your pause note; it REMOVES a duplicate surface, so it stays.

_ui lane, 2026-10-29_
