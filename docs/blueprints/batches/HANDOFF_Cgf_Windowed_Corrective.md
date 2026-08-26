<!--STATUS
state: LIVE
build-state: FRAME — UI/CGF lane. Corrective: the USER's visual check of `--mode cgf` found SIX real defects
  the model-level conformance rails missed. Investigation-led; the session root-causes + fixes each and adds
  the RAIL that would have caught it (R-124 ui-probe). SEVERITY-1: the freeze first.
updated: 2026-08-26
current-answer: this handoff is the FRAME. §1 = the six symptoms + likely roots. §2 = approach. The session
  authors any design/UML only if a fix introduces a real new structure; otherwise it is defect work.
known-conflict: edits CgfSubsystem.cs + the AiShared picker/perspective composition (hot files) — rule-4 re-pull.
  ⛔ Disjoint from MCP (DebugApi) and backend (test projects).
-->
# FRAME-HANDOFF — **CGF windowed corrective** *(UI/CGF lane, `CE-`)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm)*. ⭐ Rule 7; **rule 1b started-marker before code.** ⛔ No PR.
> ⭐ Continue `CE-` ids *(last `CE-052`)*; you allocate them *(rule 5)*.
> 🎯 **The harness was GREEN and `--mode cgf` is broken in six ways** *(user visual check, `2026-08-26`)*. The
> conformance rails compare panel MODELS across hosts; they never checked picker CONTENTS, the perspective
> toolbar's PRESENCE, window PERSISTENCE, or the FREEZE. ⇒ ⭐⭐ **fix each, AND add the rail that would have
> caught it** *(`R-124` ui-probe: in-frame measurement, no image compare — `IsPopupOpen`/`GetItemRectSize`/`GetContentRegionAvail`)*.

## 1. ⭐⭐⭐ THE SIX SYMPTOMS *(observed on `--mode cgf`, windowed)*
| # | symptom | likely root *(confirm — R-106: one item at a time)* |
|---|---|---|
| 🔴 **1** | **FREEZE** after switching perspective *(ExCon → IG)* | ⛔⛔ **SEVERITY-1 — reproduce + root-cause FIRST.** A hang blocks everything; suspect the perspective-switch path or an empty-picker interaction. Get a stack/hang location |
| **2** | pinned window in **ExCon** not kept open switching to **IG** | pinned-window persistence across a perspective switch — the `PerspectiveWorkspaceRegistrar`/workspace-restore path on CGF |
| **3** | **no perspective-switch toolbar buttons** shown | CGF (`--mode cgf`) does not compose the perspective-switch toolbar section, or no perspectives are registered for it to show |
| **4** | `File→Edit→Open Scenario` → **EMPTY picker** | ⭐⭐ **one root, symptoms 4–6:** the **scenario** picker source/catalog is not populated on CGF *(the AI-asset catalog IS — Slice 2 — but scenarios are not)*. Measure how the editor populates the scenario picker vs CGF |
| **5** | `File→Open Asset` → picker shows btree/blueprint/hsm but **no scenario** | same root as #4 — scenarios absent from the picker's kinds/source on CGF |
| **6** | `File→Live→Open Scenario` → **EMPTY picker** | same root as #4 |

## 2. ⭐ APPROACH *(you own the how — frame-delegation)*
- ⭐⭐⭐ **The freeze (#1) FIRST** — reproduce, capture where it hangs, fix. ⛔ Do not proceed to the cosmetic items leaving a hang.
- ⭐⭐ **The scenario-picker-empty cluster (#4–6) is likely ONE fix** — populate/ wire the scenario source into CGF's picker the way the editor does. Measure the editor's scenario-picker population path and mirror it *(ruling 9 — reuse, don't fork)*.
- ⭐ **The perspective cluster (#2,#3)** — measure whether `--mode cgf` composes the perspective-switch toolbar section + the pinned-window workspace restore, and compose what's missing. ⚠ **`--mode cgf` standalone may differ from `--mode all`** *(perspectives from the cluster vs CGF composing its own)* — state which.
- ⭐⭐⭐ **EACH fix ships with a RAIL that would have caught it** *(the whole point — the harness was blind)*: a **picker-non-empty-on-CGF** rail, a **perspective-toolbar-section-present** rail, a **window-persists-across-perspective-switch** rail, and if feasible a **no-hang-on-perspective-switch** guard. ⭐ Prefer `ui-probe` in-frame rails *(R-124)* over eyes.
- ⚠ If a symptom turns out CROSS-LANE once measured, STOP that item and report *(R-106)*.

## 3. ⭐ ACCEPTANCE + PROCESS
- Each symptom: fixed + a rail that reddens without the fix *(inverse-edit proof)*; `--mode cgf` no longer freezes on perspective switch; the scenario picker lists scenarios on CGF; the perspective toolbar buttons show; a pinned window survives the switch.
- ⭐ **A tiny eyes re-pass** on `--mode cgf` confirms the subjective residual *(R-21 lifted, scoped small)*.
- affected-project builds; the new rails run *(T2/T3)*; reds pre-existing by `git diff`. If a fix adds a real new structure, author the design+UML *(obligation ④)*; otherwise this is defect work.
- **When done:** the report lists each symptom → root → fix → rail; fold any as-built into the owning E1–E4 design.
