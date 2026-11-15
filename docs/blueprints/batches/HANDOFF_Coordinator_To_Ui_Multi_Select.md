<!--STATUS
state: LIVE — dispatched, not started (queued behind CE-4618's U-1; work it while U-2 waits for backend's B-2)
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../UX/UX_Feature_Selection.md — §2.4 "AND for visibility, fan-out for execution" (the rule item 1 builds)
  - ../../UX/UX_Feature_Multi_Select.md — UXI-24, the selection set's semantics (⚠ its STATUS still reads NOT-BUILT — item 5 updates it)
  - ../../UX/UX_RESUME_INTERACTION.md — rulings 47 and 49 (incompatible items ABSENT, not greyed, no reason)
  - ../../DESIGN_Entity_Control.md — §4.2 the unit menu's orders, §8a RTS commands (CE-1133), §13.6 the menu shortcuts (CE-1076)
  - ../../UX/DESIGN_Scenario_Lifecycle_Review.md — CE-4314 (copy / paste plan)
-->

# HANDOFF — coordinator → `ui`: the multi-selection's menu and commands (a small batch)

**For:** the `ui` lane. **From:** coordinator. **Dispatched at `cf0fb9ea6`**; ⛔ scope FROZEN there — later documents are FYI; one that
invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-15`: *"is there anything still open from the UI or UX tasks? Like for example, commands for multi-select entities to be sent to
all of them, or the drag and drop for the whole multi-select group"* · *"yes, write the ui handoff"*.

⭐ **Order:** finish CE-4618's U-1 first (done); take this batch while U-2 / U-3 wait for backend's B-2 (the type 114 and missile 6047); return to
U-2 when B-2 lands. ⛔ Stay out of backend's radar-SAM files.

## 0. What is ALREADY built — measured `2026-11-15`, do not rebuild
group drag (CE-4342) · Shift adds / Ctrl toggles / Alt cycles (`SelectionInteractionSystem.cs:196-204`) · the band in 2-D and 3-D (CE-1136) · a menu
ORDER fans out to the whole selection when the clicked unit is in it (`MenuOrders.cs:154-165`, CE-1059) · RTS right-click orders every selected
unit (`RtsCommands.cs:61`) · Delete deletes every selected entity (`SelectionInteractionSystem.cs:292-305`) · designations for every selected unit.

## 1. The items
| # | item | design basis | done when |
|---|---|---|---|
| **1** | ⭐ **[CE-4620](../Blueprint_Issues_Tracker.md) — the unit menu on a MULTI-selection = the INTERSECTION.** Today it is built from the ONE right-clicked entity (`ContextMenuProjectorGizmo.cs:49`, `MenuJsonFor(view, entity)`), so a tank + infantry selection offers *Embark* and each unit refuses it. Right-clicking inside a selection of N > 1 shows only the items EVERY selected entity offers (an action id present in all N menus); the others are ABSENT (no greying, no reason); executing fans out as today. One place computes it — every host's map (2-D, 3-D, the cluster perspectives) gets it by construction. ⚠ per-frame cost: compute on menu OPEN, never per frame (R-2084) | Selection §2.4; rulings 47 / 49 | a rail: a mixed selection's menu has exactly the common items; a single selection's menu unchanged; an order from the intersection reaches every unit |
| **2** | **the menu shortcut keys (M / A / H …) on a selection with no primary** do nothing today (`MenuShortcuts.cs:64-69` returns null when > 1 and no primary). Resolve the key against item 1's intersection and send it like a click. Also MEASURE (a rail) that a key with a primary reaches the whole selection through `MenuOrders.UnitsOf` — fix if not | Entity Control §13.6; Selection §2.4 | rails: a band-selected group (no primary) moves on M; a group with a primary moves on M |
| **3** | **CE-4314 — *Copy plan* / *Paste plan* onto a multi-selection** | Scenario Lifecycle Review (LC-6, R-4166) | as the row and the design say |
| **4** | **CE-1133 — right-click move also orders units no longer selected** (user report; the 2-D rail does NOT reproduce it). Try the paths not tried: the 3-D map, and each perspective of a `--mode all` run (each has its own world and selection); a selection changed while the RTS tool is armed. Root-cause it or close it with the evidence of where it does not happen | Entity Control §8a | reproduced + fixed with a rail, or closed with the measured paths |
| **5** | **bring `UX_Feature_Multi_Select.md` (UXI-24) up to date**: its STATUS says NOT-BUILT; record the as-built (§0 above + items 1–4) and mark the stale text SUPERSEDED (obligation ⑤) | the doc itself | its STATUS true; `related-designs` reciprocal with Selection |

⏸ **Not in this batch:** CE-4404 / CE-1477 (Units ▸ Labels) — it does not reproduce on Linux, it needs the user's Windows machine; CE-2425 (Move
here on the ground) — blocked on the catalogue's Ground target; CE-2424 (rotated gizmo text) — its own row, take it only if all five are done.

## 2. Gates
T-1 first (`MenuOrdersTests`, `RtsCommandToolTests`, the selection and context-menu suites); display rails under `xvfb-run -a` (they skip
silently otherwise — report counts); each new rail red-proved by a compiling mutation; 0 B per steady frame (R-2084); invariant output. Build the
TEST project in a fix loop.

## 3. The report — `STATUS_Ui_Multi_Select.md`
per item built / closed / stopped, the design folded · lane decisions with what would change each · the gate-report contract (rule 8) · ids allocated.
