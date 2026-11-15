<!--STATUS
state: LIVE — RE-SCOPED and re-dispatched (rule 1a: the first stamp cf0fb9ea6 was never started) — not started
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../UX/DESIGN_Map_Interaction.md — P5 (the group drag) and its CE-4342 as-built row (the claim this batch re-tests)
  - ../../UX/DESIGN_Edit_Framework.md — the group's ONE journal step, the writes through the edit path
  - ../../UX/UX_Feature_Multi_Select.md — UXI-24, the selection set's semantics (its STATUS still reads NOT-BUILT — fold the as-built)
-->

# HANDOFF — coordinator → `ui`: DRAGGING A MULTI-SELECTION — reproduce it LIVE, fix it (CE-4342 reopened)

**For:** the `ui` lane. **From:** coordinator. **Dispatched at `40cb8d4df`** *(re-scoped; supersedes `cf0fb9ea6`)*; ⛔ scope FROZEN there.

🔒 User, `2026-11-15`: *"I changed my mind. I wanted to address the multi-select drag and drop issue specifically."* · the original report
(`2026-11-08`): *dragging one of several selected entities moves only that one — every selected entity should move with it, keeping their offsets.*

⭐ **Order:** take this while CE-4618's U-2 waits for backend's B-2; ⛔ stay out of backend's radar-SAM files.

## 1. Why it is reopened — measured `2026-11-15`
[CE-4342](../Blueprint_Issues_Tracker.md) was closed `2026-11-09` (`8b2e7fbec`) as *"measured: works"* **with no production fix**: a headless rail
(`RtsCommandToolTests.CE4342_DraggingOneOfThreeSelected_MovesAllThree_ThroughTheMapsBus`) passes, and the report was put down to an older build.
The same row says: ⚠ *"not run against a live editor"* and ⚠ *"not reproduced with the editor's `EntityWriteRouter`"* (the rail passes no
writer; `GroupTransform.Commit` writes per unit through it). ⇒ ⛔ **the user's path was never exercised.** A green rail on a path the user does not
take is not the answer (T-1 ③: a suite green while the feature is broken IS the finding).

## 2. The item
| step | what | done when |
|---|---|---|
| **①** | ⭐⭐ **REPRODUCE IN THE REAL PRODUCT**: a real `--mode editor` under `xvfb-run -a` (and `--mode all`), a scenario with several units; select 3 (band, Shift-click, Ctrl-click), drag one — on the **2-D map** and the **3-D map**, in **edit** and in a **live run**, with the editor's `EntityWriteRouter` in the path. ⚠ The debug API may not drive a gizmo drag (CE-259al) — if not, drive it through the same input path the canvas uses, and say how. Record which combinations move all, which move one, which move none | a table: view × mode × selection gesture → result, with the run's evidence |
| **②** | **root-cause each failing combination** — where the group is lost (the gizmo, `GroupTransform`, the writer, the edit journal, replication on a perspective) — the WHOLE-FIELD read of whatever decides the group | each cause at `file:line` |
| **③** | **fix it in the one shared path** so every view and host gets the group by construction (no per-view copy); keep the offsets; ONE undo step restores all; a unit that is not editable (another station's, a running AI unit in live) is handled by the edit path's own rule, never silently skipped — say what the rule is | rails for each combination that failed, through the REAL writer (`EntityWriteRouter`), red-proved; the 3-D one under `xvfb-run -a` |
| **④** | ⭐ **the live check**: repeat ① after the fix — all move | the ① table all green, the frames side by side in the report (⛔ not in git) |
| **⑤** | fold the as-built into `DESIGN_Map_Interaction.md` P5 (the CE-4342 row marked SUPERSEDED) and `UX_Feature_Multi_Select.md` (UXI-24's STATUS true); reopen-then-close CE-4342 with the evidence | the designs true; the row closed with live evidence |

⏸ **Moved out of this batch** (the user's choice): the menu intersection + keys with no primary ([CE-4620](../Blueprint_Issues_Tracker.md)), copy /
paste plan (CE-4314), the deselected-units RTS move (CE-1133) — they stay READY / OPEN for a later batch.

## 3. Gates
T-1 first (`RtsCommandToolTests`, `GroupTransformTests`, the edit-framework and selection suites); display rails under `xvfb-run -a` (report
counts — a skip is not a pass); each new rail red-proved by a compiling mutation; 0 B per steady drag frame (R-2084); invariant output. Build the
TEST project in a fix loop.

## 4. The report — `STATUS_Ui_Group_Drag.md`
the ① table before and after · each cause · lane decisions with what would change each · the gate-report contract (rule 8) · ids allocated.
