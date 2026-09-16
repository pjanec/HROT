# CHECKLIST — Track C: **what was planned, what was built, what you can actually SEE**

> ⭐ **Purpose (user, `2026-08-17`):** *"a list of changes to be verified… the feature list first, to see
> if all what was planned was also developed."* ⛔ **Not yet a step-by-step guide.**
>
> 🔴🔴 **HEADLINE — read this before planning any session at the editor:**
> ⭐⭐ **Everything planned WAS developed.** ⛔⛔ **But FIVE of seven deliverables are NOT REACHABLE in
> the running editor — no window hosts them.** 📐 **Coordinator-measured `2026-08-17`.**
> ⇒ **A visual check today would find almost nothing to look at.** ⭐ **The wiring is one batch.**

---

## 0. 📐 Reachability — measured, per deliverable

| # | deliverable | batch | built? | reachable in the editor? | the measurement |
|---|---|---|---|---|---|
| **1** | **`C-sections`** — Variables split per kind in My Blueprint | 66 | ✅ | ✅ **YES** | `BlueprintMyBlueprintModel` carries the section list; `BlueprintMyBlueprintWindow:326` constructs the panel with it |
| **2** | **Inspector `DEFAULT VALUE — {var}`** *(was "STATIC PARAMETERS")* | 74 | ✅ | ✅ **YES** | wired at `EditorSubsystem:2135/2153` via `ResolveExpressionTargetField` |
| **3** | **`C-table`** — the generic variable table | 68 | ✅ | ⛔⛔ **NO HOST** | `VariableTableControl` is referenced **only** by its own folder and `VariableEditGestureBinder` |
| **4** | **`C-dialog`** + the launcher wiring | 68 · 77 | ✅ | ⛔ **only via #3** | `VariableEditGestureBinder` binds the table's gestures — and nothing constructs the table |
| **5** | **`C-tick`** — the per-`(asset, entity)` change highlight | 69 | ✅ | ⛔ **feeds #3** | the counter and `VariableChangeMonitor` are live; the surface that would show red/yellow is not |
| **6** | **`C-watch`** — the Watch panel on the shared row renderer | 69 | ✅ | ⛔⛔ **NO** | `AiWatchWindow.DrawClientArea` draws its **own** 3-column table — `Name / Enabled / Hits` over breakpoints. ⛔ **It uses neither `PinnedSource`, the row renderer, nor `VariableValueFormatter`** |
| **7** | **`C-outline`** — BTree/HSM get their own My Blueprint outline | 69 | ✅ | ⛔⛔ **NO** | `BlackboardMyBlueprintModel` is **constructed by nothing**; the only `MyBlueprintPanel` outside the demo is the blueprint one |

⭐⭐ **This is the same pattern the programme has been filing all week** — the producer picker *(Batch 70,
parked)*, `VariableEditLauncher` *(Batch 68, wired in 77)*, and now **the whole table stack**.
⇒ ⭐ **The audit you asked for has already paid: the gap is not in the features, it is in the wiring.**

---

## 1. ✅ VERIFIABLE TODAY — **two surfaces**

### 1a. My Blueprint — sections *(`C-sections`)*

| # | what to see |
|---|---|
| 1.1 | **Variables** is split per kind — no longer one undifferentiated list |
| 1.2 | **`Local Variables`** is present and **graph-scoped** — it follows the canvas as you switch graphs |
| 1.3 | ⭐ **an empty section renders EMPTY, not absent** *(the rule: "a section that appears and disappears reads as a broken feature")* |
| 1.4 | each section's **`[+]`** creates a declaration **of that section's kind** |
| 1.5 | on a **Macro** graph the `[+]` **refuses out loud** (an indicator), rather than doing nothing |
| 1.6 | ⛔ **there is NO `Role`/`Scope` control anywhere** — the section is the classification |
| 1.7 | section **order** on screen matches `SortOrder` |

### 1b. Inspector — the default-value panel *(Batch 74)*

| # | what to see |
|---|---|
| 1.8 | select a BTree/HSM node whose facet has an **`ExpressionTargetField`** ⇒ the section appears |
| 1.9 | its title reads **`DEFAULT VALUE — {var}`** *(⛔ not "STATIC PARAMETERS")* |
| 1.10 | the subtitle names **`ExpressionTargetField`** |
| 1.11 | editing a field and committing **persists to `DefaultValueJson`** — reopen the asset and it holds |
| 1.12 | ⭐ **the tooltip** explains static-vs-dynamic: *"applied once at behavior assignment; bind a variable for live/dynamic values"* |

---

## 2. ⛔ BUILT, NOT REACHABLE — **the feature list, for the wiring batch**

> ⭐ **Everything below has headless tests asserting its MEANING.** ⛔ **What no test covers is that it
> DRAWS — and today nothing draws it at all.** ⇒ **this doubles as the wiring batch's acceptance list.**

### 2a. The table *(`C-table`)*

| # | planned | built |
|---|---|---|
| 2.1 | **`Name` + `Value` always; `Type` a single toggle** — hidden in Watch, shown in Details | ✅ |
| 2.2 | ⛔ **`Bytes`, `Role`, `Scope` columns GONE** *(seven columns → three)* | ✅ |
| 2.3 | **grouping** = an ordered facet list: `[]` · `[Entity]` · `[Asset]` · `[Asset, Entity]` | ✅ |
| 2.4 | ⭐ **a UNIFORM facet emits no header** — watching one asset shows no asset header | ✅ |
| 2.5 | **folding** via `CollapsingHeader` | ✅ |
| 2.6 | ⭐⭐ **a COLLAPSED header inherits its children's red/yellow** | ✅ |
| 2.7 | `GroupBy`, fold state and the `Type` toggle **persist per panel** | 🔴 **NOT BUILT** *(Batch 79, from code)* — `VariableTableModel.GroupBy` is a plain settable property *(`VariableTableModel.cs:79`)*; ⚠⚠ **its own doc comment CLAIMS *"Persisted per panel in the editor layout"* and nothing implements that.** Fold is ImGui's `CollapsingHeader` *(`VariableTableControl.cs:66`)*, so ImGui persists it in `imgui.ini` by window+label — ⛔ not by the editor layout. `ShowType` is a **ctor-time** `VariableTableColumns` with **no toggle UI anywhere**. ⇒ ⛔ **do not look for this in the visual check** |
| 2.8 | **heterogeneous rows** — several assets and entities in one table | ✅ |
| 2.9 | selection yields a **SECTION**, not a variable — Details re-filters, row highlighted | ✅ |

### 2b. Value rendering *(`4b`)*

| # | planned | built |
|---|---|---|
| 2.10 | **primitive** inline and formatted — `80`, `12.5`, `true` | ✅ |
| 2.11 | **struct** = one-line elided summary `{X=1.0, Y=2.0, …}` + **pretty-printed multi-line tooltip** | ✅ |
| 2.12 | **fixed list** `{Count=3: 1, 2, 3}`, elided | ✅ |
| 2.13 | ⛔⛔ **NEVER raw hex** — `BP-01`'s original symptom | ✅ |
| 2.14 | undecodable says **`<unreadable>`** in words, with the reason in the tooltip | ✅ |
| 2.15 | Watch, before the first write: **`(pending)`** | ✅ |
| 2.16 | **one line, never wrapping, never growing the row** | ⚠ **the eye only** |
| 2.17 | ⭐ tooltip and dialog share **ONE formatter** | ✅ |

### 2c. Change highlighting *(`C-tick`)*

| # | planned | built |
|---|---|---|
| 2.18 | 🔴 **red for one tick** = the simulation changed it | ✅ |
| 2.19 | 🟡 **yellow** = your pending edit — ⭐ **visually distinct from red** | ✅ |
| 2.20 | ⭐⭐ the unit is **a non-frozen ASSET tick**, not a frame ⇒ **paused, the highlight PERSISTS until Step** | ✅ |
| 2.21 | two entities running one asset highlight **independently** | ✅ |
| 2.22 | a row with no tick source is **inert** — never wrongly red | ✅ |
| 2.23 | ⚠ **BTree/HSM rows are inert by design** *(no per-asset counter on those hosts yet)* | ⭐ **expected, not a defect** |

### 2d. Gestures and dialogs *(`C-dialog`, launcher)*

| # | planned | built |
|---|---|---|
| 2.24 | **double-click the VALUE cell** ⇒ value dialog *(`ForField`)* | ✅ |
| 2.25 | **double-click the NAME cell** ⇒ full properties *(`WholeComponent`)* | ✅ |
| 2.26 | **`⋮` menu** carries both, plus **Rename** | ⭐ **BUILT in Batch 79 — minus Rename.** Right-click the **name cell**: *Edit value…* and *Properties…*, both disabled on a stale or node-owned row. ⛔ **Rename is absent BY DESIGN, not missing:** a `VariableRow` is an OBSERVATION *(`(AssetId, Entity, Section, VariablePath)` + a byte reader)* with no asset handle, schema source or undo recorder — nothing there could rename a declaration. ⭐ **Rename belongs to the OUTLINE**, which holds the asset; the blueprint side does exactly that via `BlueprintDocumentFactory.RegisterMyBlueprintItemCommands` |
| 2.27 | **F2** renames inline, and the refactor service still runs | ✅ |
| 2.28 | ⭐ **run state decides WRITABILITY, not which dialog opens** | ✅ |
| 2.29 | ⭐⭐ the value dialog opens **scoped to that field** — ⚠ **it opened EMPTY until Batch 77 fixed the path space** | ✅ |
| 2.30 | ⭐ **exactly one call site opens a variable edit session** *(the panel and the table are two entry points, one implementation)* | ✅ |

### 2e. Watch *(`C-watch`)*

| # | planned | built |
|---|---|---|
| 2.31 | rows from **arbitrary assets and entities, mixed** | ✅ |
| 2.32 | default grouping **`[Asset, Entity]`**, `Type` column **hidden** | ✅ |
| 2.33 | ⭐ a **stale** row shows its last value **greyed** and **refuses its dialog** | ✅ |
| 2.34 | ⭐ a **136-byte struct** pins and renders *(the old 64-byte buffer limit is gone)* | ✅ |
| 2.35 | ⛔⛔ **and today `AiWatchWindow` shows none of this** — it draws `Name / Enabled / Hits` | 🔴 **wiring** |

### 2f. Outline for BTree/HSM *(`C-outline`)*

| # | planned | built |
|---|---|---|
| 2.36 | BTree and HSM assets each get **their own section list** | ✅ |
| 2.37 | sections show **only what the asset has**, and **EMPTY rather than absent** | ✅ |
| 2.38 | creating in a section produces a declaration **of that kind** | ✅ |
| 2.39 | ⛔ still **no `Role`/`Scope` control** on either host | ✅ |

### 2g. Run state and planning chrome *(§5)*

| # | planned | built |
|---|---|---|
| 2.40 | **planning mode**: values editable, the **budget indicator** visible | ⭐ **values editable: BUILT** — `VariableEditing.cs:151`, `(_, Planning) ⇒ Editable`. 🔴 **budget indicator: NOT BUILT in Track C** — the only budget UI in the codebase is the OLD `BlackboardAuthoringWindow`'s *(`InlineBudget`/`HeavyBudget`, `:485`)*, which the new table does not carry |
| 2.41 | **running mode**: the Value column switches to live, the budget indicator hides | ⭐ **live values: BUILT** — rows re-read every frame and `VariableChangeMonitor.Observe` returns `None` in Planning *(`:87`)*, so the highlight only appears once running. 🔴 **budget hides: NOT BUILT** — ⛔ **there is no run-state input to the budget display at all**: `BlackboardAuthoringWindow` has zero occurrences of `RunState`/`IsRunning`, so its budget is drawn unconditionally, including mid-run |
| 2.42 | ⛔ **no `_liveRepo` write while paused** *(Flight Recorder linearity)* | ✅ |
| 2.43 | ⭐ **optimistic display** — the new value paints immediately, then stages through the normal path | ✅ |

---

## 3. ⭐ What this checklist is NOT covering

| | |
|---|---|
| ⛔ **anything multi-level** | `E3` · `E5` · `E7a` · `Q36` · `Q37` · blueprint multi-occurrence — **parked by the user** |
| ⛔ **the producer picker** | **parked as asserted-inert**; its runtime does not exist |
| ⚠ **`BP-306` / `BP-307`** | Batch 78, not UI |

---

## 4. ⇒ The recommendation

⭐⭐⭐ **Do NOT spend a session at the editor yet.** ⛔ **Five of seven surfaces would show you the OLD
control**, because `VariablesPanelControl` is still what `BlueprintVariablesWindow` and
`BlackboardAuthoringWindow` draw.

⇒ ⭐ **Wire first, then look.** §2 is the acceptance list for that batch; §1 is what you could check
today if you wanted an early look at sections and the Inspector panel.
