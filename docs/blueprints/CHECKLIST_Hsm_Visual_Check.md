<!--STATUS
state: LIVE
doc-type: MANUAL VISUAL-CHECK CHECKLIST for the HSM work built on `ui` 2026-10-06.
build-state: n/a — a test procedure, not a design.
updated: 2026-10-06
current-answer: the whole file. §0 is the setup, §1–§7 are the checks, §8 is the
  DO-NOT-FILE list (known-open items a tester will otherwise report as bugs).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - DESIGN_Hsm_Canvas_Authoring.md — OWNS the canvas slices S1–S3 (§8a as-built) and the
    region/container sizing (§11/§11a). This file only tells a human how to look at them.
  - Architect_Question_84_Hsm_Region_Initial_History_Model.md — OWNS the region / initial /
    history model (§6, §7) that §2 and §3 here exercise.
  - Hsm_Issues_Tracker.md — OWNS the HSM-0xx rows; §4–§6 here check HSM-009/012/017.
  - RESUME_UI_Lane.md — the lane state that says this check is owed.
-->

# Manual visual check — HSM, as built on `ui` (`2026-10-06`)

> **Why this exists.** [`RESUME_UI_Lane.md`](RESUME_UI_Lane.md) records the debt verbatim:
> *"Windows visual check still owed for the canvas AND now for the Events table's new buttons."*
> Everything below is a **human-eye** check: each item was railed, and the rails pass — what no rail
> can see is whether the thing is *usable on screen*.

## 0. Setup

```bash
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj
HROT_DEBUG_API_PORT=8131 Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.exe --mode editor
```

Assets to open live in `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/HSMs/`:

| asset | what it is good for |
|---|---|
| `SampleGuard.hsm.json` | smallest machine — use it for §1 and §7 |
| `HsmOrthogonalRegions.hsm.json` · `HsmTwoChannelRegionsDemo.hsm.json` | §2 regions and band sizing |
| `HsmCuratedBindingDemo.hsm.json` | the "shows two regions, ran three" case `CE-1003` fixed |
| `HsmShowcase.hsm.json` | migrated off history pseudo-nodes by `Q84 C1/D1` — §3 |
| `HsmVariableShowcase.hsm.json` | §5 variable rename |

⚠ **Before you start: `git status` must be clean.** §7 depends on it, and it is also how you tell a
real edit from the old autosave.

---

## 1. Canvas transitions — `CE-1000` / `CE-1001` / `CE-1002`

| # | do this | expect |
|--:|---|---|
| 1.1 | Hover a state's **border** | the cursor/zone reacts on the edge band (`HoverKind.NodeEdge`), not only on invisible left/right pins |
| 1.2 | Drag from a state border to another state | an arrow **border-to-border**, not pin-to-pin; ⛔ no stub hanging off the node's middle-left |
| 1.3 | **Shift**-drag from a state body (or a container header) | same as 1.2 — Shift works where the border band is awkward |
| 1.4 | Click a state **without** dragging | selects it. ⛔ It must NOT create a self-transition |
| 1.5 | Right-click a state → **"Add Transition"** | a *sticky* wire follows the cursor with **no button held**; a left **press** finishes it; `Esc` or right-click cancels |
| 1.6 | Right-click a **composite / region header** | a node context menu appears — ⭐ composites had none before |
| 1.7 | Drag a transition and drop on **empty canvas** | a state **picker** opens; choosing creates state + transition as **ONE undo step** (`Ctrl+Z` once removes both) |
| 1.8 | Create several states from the palette | names are unique — `State`, `State 2`, … ⛔ never two bare `State`s |
| 1.9 | Select a state, press **F2**, type a name | the rename applies on the canvas. Empty/whitespace is refused |
| 1.10 | Look at any transition | label sits on the **outer** side of the arc's bend; the arrowhead is **on the curve's tangent** at the target border |
| 1.11 | Set a breakpoint on a **transition** | the dot sits **~25 % along the arrow** — ⛔ not on the source state's corner, where the state's own dot is |
| 1.12 | Run the machine and watch a transition fire | the fired arrow highlights and a **diamond marker moves along it** |
| 1.13 | Press **Tab** (or "Add Node…") on the HSM canvas | the picker offers **HSM** nodes. ⭐ The `ScopedPickerRegistry` fix: open a BTree **and** a Blueprint **and** this HSM, switch between the three canvases, and each must offer **its own** nodes — previously the last-opened document owned the picker for all of them |

## 2. Regions, initial state, band sizing — `CE-1003` / `CE-1004`

| # | do this | expect |
|--:|---|---|
| 2.1 | Open `HsmOrthogonalRegions` | each declared region is a visible **band**; the top level shows a **start marker** |
| 2.2 | Right-click a state inside a container → **"Set as Initial State"** | it becomes the initial state; the item is **greyed out** on the one that already is |
| 2.3 | Delete a region that has siblings after it | the surviving children stay **where they were** — ⛔ they must not shuffle into the wrong band |
| 2.4 | Add a region | same — existing children keep their band |
| 2.5 | Leave a region with **no** initial child | a `RegionWithoutInitialState` **Error** appears in diagnostics |
| 2.6 | Drag a state into a region | ⭐ the **container does not jump**. It only grows when the state lands **above or left of** the interior |
| 2.7 | Drag the **divider between two bands** | ±4 px grab zone, **NS/EW cursor**, a live preview while dragging, commit on release, one undo step labelled *Resize Region* |
| 2.8 | Drag the **corner grip** of a container (12 px) | **NWSE cursor**, resizes, undo labelled *Resize Container*. ⚠ corner only — there is deliberately **no edge drag** |
| 2.9 | Hover either handle | it draws in the **accent colour** |
| 2.10 | Drop a state where **no container** is under the cursor | it detaches to the root |
| 2.11 | Re-open the asset after a band/container resize | the sizes **persist** (`RegionNodeDto.PreferredSize`) |

## 3. History is now a composite setting — `Q84 C1/D1` (closes `HSM-010`)

| # | do this | expect |
|--:|---|---|
| 3.1 | Open the node palette | ⛔ there are **no** history pseudo-node entries any more |
| 3.2 | Select a **composite**, look at the inspector | a field **"On re-entry"** with three values: *Start at initial* · *Resume last child* · *Resume last leaf* |
| 3.3 | Open `HsmShowcase` | it was **migrated** — the old history pseudo-node is folded into its parent's setting; the graph must not show an orphan node or a dangling transition |

## 4. Events table — `HSM-009` *(the newest surface, and the one most owed a look)*

The Events table is a **Details-panel view** (`HsmEventsDetailsView`), asset-scoped.

| # | do this | expect |
|--:|---|---|
| 4.1 | Open an HSM with **no events at all** | ⭐ **"+ Add Event" is still visible** — it is drawn before the empty-state early-return. This is the whole point of the fix |
| 4.2 | Click **"+ Add Event"**, type a name, **Create** | the event appears in the table with a sensible id |
| 4.3 | Add several | ids take the **lowest free value from 1** — ⛔ never `0` (`0` means "no event") |
| 4.4 | Add an event whose name matches an **engine-raised** one | it takes the **reserved** id, not the next sequential one |
| 4.5 | Try to rename an event **to** a built-in name | refused, with a message |
| 4.6 | Right-click an event → **"Rename..."**, change it, press **Rename** | ⭐ it **applies**. Previously the modal only previewed and had no Apply at all |
| 4.7 | Right-click an event used by transitions → look at the Delete item | it reads **"Delete (N transition(s) will dangle)"** with the real count |
| 4.8 | Delete it | the referencing transitions are **left alone** (their `EventId` is not rewritten to 0); an `EventReferenceDangling` warning reports them |
| 4.9 | Right-click → **"Find References"** | ⭐ it returns **results**. It was always empty before (wrong key) |
| 4.10 | Open a transition's **event picker** | it offers the events you just authored — the Phase-1 bar is "build a working machine without touching C#" |

## 5. Variable rename no longer dangles — `HSM-017`

| # | do this | expect |
|--:|---|---|
| 5.1 | In `HsmVariableShowcase`, bind a variable to a state action slot and to a transition guard | both bindings show the name |
| 5.2 | Rename that variable | ⭐ **every binding retargets** — state slots (×4), transitions, globals. ⛔ none left naming the old string |
| 5.3 | Create `speed` **and** `speedLimit`, bind both, rename `speed` | ⭐ `speedLimit` is **untouched** — the substring trap a text rewrite falls into |
| 5.4 | Find References on a variable | lists the binding sites |

## 6. The Timer facet is withdrawn — `HSM-012`

| # | do this | expect |
|--:|---|---|
| 6.1 | Select a state, look at the inspector | ⛔ **no Timer facet field** — it can no longer be authored |
| 6.2 | Open an asset that still carries an old timer binding | a `TimerActionNotImplemented` warning names it (diagnosable, not silent) |

## 7. Save-only — `BP-93`

🔒 The user's own report: *"I never saved the BTree and HSM so it is very weird they got saved automatically — this is undesired."*

| # | do this | expect |
|--:|---|---|
| 7.1 | `git status` → clean. Open an HSM, move a node, add a state. **Wait 5 s.** Do **not** save | ⭐ `git status` is **still clean**. The old 500 ms debounce no longer writes JSON |
| 7.2 | The document tab / title | shows **dirty** |
| 7.3 | **Close the editor without saving** | `git status` **still clean** — exit does not write either |
| 7.4 | Re-open, edit, **File ▸ Save** (or Save All) | *now* the file changes |

---

## 8. ⛔ DO NOT FILE THESE — known open, already tracked

A tester will hit every one of these and they are **not** regressions.

| what you will see | why | row |
|---|---|---|
| Entering a parallel state **by a transition** starts only **one** region (the others never run) | kernel gap; regions work only for a parallel state active from the start | [`CE-1005`](Blueprint_Issues_Tracker.md) |
| "Output lanes (inferred)" is populated but **nothing is actually arbitrated** | only 4 of 86 `[HsmAction]` sites declare a lane, and all four are test fixtures | [`HSM-023`](Hsm_Issues_Tracker.md) |
| No way to say **how long** a timer runs | the kernel never arms one; arming is unbuilt | [`HSM-021`](Hsm_Issues_Tracker.md) |
| Renaming a **BTree** variable still dangles its bindings | the BTree twin was deliberately not fixed here — `Hrot.BTree.Editor` is the behaviors lane's model | [`HSM-022`](Hsm_Issues_Tracker.md) |
| Event add / rename / delete are **not on the undo stack** | deliberate — the same as the variables surface they follow; one decision for both | `HSM-009`, design §9.1a |
| The **Globals strip** is nowhere in the UI | `HsmGlobalsStrip` has zero production callers | `HSM-009` ⚠ note |
| Saving reformats the whole file | wholesale reformat, separate row | [`BP-94`](Blueprint_Issues_Tracker.md) |

## 9. Reporting

For each failure give **the asset, the step number, and what you saw instead** — and a screenshot
where it is geometry (§1, §2). ⭐ Per `T-1`, a failure here gets a rail **in the feature's own suite**
(`Hrot.Hsm.Editor.Tests`, `NodeEditor.Core`/`.UI` tests, `ContainerDragTests`), never a new parallel
class.
