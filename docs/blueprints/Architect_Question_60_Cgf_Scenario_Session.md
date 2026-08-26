<!--STATUS
state: LIVE
build-state: DESIGN — decision-shaped; a RECOMMENDED LEAN per sub-question. Resolve JOINTLY with the user
  (no relay). Not a buildable design — no handoff until the leans (or alternatives) are approved.
updated: 2026-08-26
current-answer: ⭐⭐⭐ §4 — THE GROUNDED DESIGN (post-scan). §3b is the user ruling it implements; the
  old §4/§5 draft is under `## ⛔ HISTORY`. One residual call for the user: §4.4 (build checkpoint restore, or defer).
known-conflict: none. Origin: the §6.2 handback of REPORT_Cgf_Menu_Follows_Focus.md.
-->

<!-- ⭐⭐⭐ 3b inserted at the top so it is read before the now-stale §4/§5. -->
## 3b. ⭐⭐⭐ USER RULING `2026-08-26` — **reshapes the whole AQ** *(intent, near-verbatim)*
> 🔒 **User:** *"There is a big difference between loading for editing and loading for a live run. Editor
> loads for editing, CGF loads for live run — but this could be just a **subsystem-specific DEFAULT**, and
> **each option deserves its own menu item** (MCP supports loading live and loading for edit too).
> Architecturally I want **CGF to support BOTH options as well as the editor — no difference, code shared.**
> Not exactly sure what `IEditorLogic` is, but if it provides editing features, **why not share it with
> editor and CGF and instantiate in both?** **Saving** should be enabled for a scenario opened **for
> editing**, and should be **exactly the save the editor uses**. Saving of a **live running** one is another
> feature called **checkpoint** — this needs its OWN menu item, same as **restoring from a saved checkpoint**
> (which needs a picker) — **all already implemented in the cluster-control panel**, but exposing them also as
> main-menu items would be much more user-friendly. **'New'** again branches: in CGF's live default it means
> **'clear anything currently running and start a fresh empty exercise'** (with a confirmation dialog about
> finishing an already-running exercise); in editing mode it means **starting to edit a new scenario from a
> recipe**. Basically, between CGF and editor there should be **not much difference than distributed vs.
> no-network. Features and UI largely the same, most stuff shared, minimal duplication."*

⇒ ⭐⭐ **What this settles** *(supersedes my §4/§5 leans; §4/§5 kept below as HISTORY until rewritten)*:

| # | the ruling |
|---|---|
| **A′** | ⭐⭐⭐ **Load is TWO commands on BOTH hosts** — *Load for Editing* and *Load for Live Run* — each its own menu item *(both already exist as MCP `/scenario/load/edit` + `/scenario/load/live`, HN-029)*. ⛔ NOT one-per-host; the **default** differs by subsystem *(editor→edit, CGF→live)*, the CAPABILITY does not |
| **C′** | ⭐⭐⭐ **SHARE the editing facade — instantiate `IEditorLogic`/`EditorApplication` in BOTH hosts** *(if feasible)*. ⛔ My C1 *("extract a narrow contract")* is SUPERSEDED — the user wants the real editing object shared, minimal duplication. ⚠ **Feasibility unknown → scanning:** can `EditorApplication` run on a CGF node, or does it hold editor-only deps that must be decoupled first? |
| **B′** | ⭐⭐ **Two DIFFERENT features, not one gated save:** **(1) Scenario Save** = *exactly the editor's save*, enabled for a scenario **opened for editing** *(edit-mode ⇒ CGF authors, same as the editor — my 65↔66 gate mostly DISSOLVES: it only applies to authoring a scenario, which is edit-mode)*; **(2) Checkpoint save + Checkpoint restore** = the **live** running state, a **separate existing feature in the cluster-control panel** ⇒ expose as their own menu items *(+ a restore picker)*. ⚠ ruling 66 already named checkpoint *"a different category"* from scenario authoring — this ruling makes it a first-class menu pair |
| **New′** | ⭐⭐ **'New' branches by mode:** live-default ⇒ *clear the running exercise + start fresh* **with a confirmation dialog**; edit-mode ⇒ *new scenario from a recipe* *(the `RecipePickerSource`/`INewAssetService` path, MA-019..023)* |
| **∴** | ⭐⭐⭐ **The governing principle:** CGF vs editor differ by **distributed-vs-no-network ONLY**; features + UI largely identical; **most stuff shared, minimal duplication.** |

⚠ **Scanning now** *(before the §4/§5 rewrite)*: **(1)** `EditorApplication`/`IEditorLogic` shareability — what it depends on, what is editor-only, can CGF instantiate it; **(2)** the **checkpoint** machinery — where save/restore live in the cluster-control panel, what intents/commands, and the restore picker.

## 4. ⭐⭐⭐ THE GROUNDED DESIGN *(post-scan, `2026-08-26`)* — **the current answer**

### 4.1 ⭐⭐ Feasibility findings *(two read-only scans)*
| finding | ⇒ |
|---|---|
| ⛔ **`EditorApplication` can't be shared INTACT** — assembly wall *(`Hrot.Editor → Hrot.CGF`; CGF can't reference it without a cycle)* **and** its tool/selection/camera/rename/kernel-mode half is editor-window-only *(dead on a headless node)* | ⇒ **C′ refined:** ⛔ not "instantiate the god-object in both" |
| ✅ **the SCENARIO half is cleanly separable** — every collaborator is engine/shared *(`ScenarioFileService`, `FdpEventBus`, `EntityRepository`)*, the **world is a ctor param** *(point it at CGF's own world, no code change)*, and `LoadScenarioByName` **already routes through the cluster orchestrator** *(`TransitionStateIntent → HrotEditLoadHandler`)* — the exact path CGF lives on. Only 2 tiny editor-local bits ride along: `MigrationAlertManager`, the `ScenariosRoot` constant | ⇒ **extract a shared scenario facade, instantiate in both** |
| ✅ **Checkpoint SAVE exists** — "Take Checkpoint" *(`ClusterScenarioPanel.cs:638`)* → `TakeCheckpointIntent` *(EventId 9056)* → `ClusterMaster` fans `TakeSnapshot` to all nodes → `ReferenceCheckpointHandler` → `CheckpointIOWorker` writes LZ4 `.fdp`. Cluster-wide, **present on CGF**, **master-triggered** | ⇒ save is a wiring exposure |
| 🔴🔴 **Checkpoint RESTORE does NOT exist** — `RestoreSnapshot`/`CollectCheckpoint` enum slots are **dead** *(nothing dispatches/handles/reads them)*; no `.fdp` read-back; **no checkpoint list/picker anywhere**. The only `/checkpoint/restore` is a **RAM preview-mode rewind** *(`IPreviewController.ExitPreviewMode`)* — a different, non-persistent mechanism | ⛔ **corrects the §3b premise** — restore is a FEATURE TO BUILD, not a menu exposure |

### 4.2 ⭐⭐⭐ The resolved decisions *(grounded)*
| # | resolved |
|---|---|
| **A′** ✅ | **Two load menu items on BOTH hosts** — *Load for Editing* → `/scenario/load/edit`, *Load for Live Run* → `/scenario/load/live` *(both HN-029 cluster paths; confirmed-at-origin, ruling 59)*. Default differs by subsystem *(editor→edit, CGF→live)*; both hosts offer both. **Wiring over existing capability.** |
| **C′** ✅ *(refined)* | **Extract a shared scenario facade** — an `IScenarioSession`/`IScenarioLogic` slice *(New/Load{Edit,Live}/Save*/GetMigrationSidecars/the deferred-load `Update`)* + the 2 editor-local bits → into **`Hrot.Editor.AiShared`** *(CGF already reaches it)*; **instantiate in BOTH**, each pointed at its own world. The editor keeps the tool/view/mode half. ⭐ **This IS "share it, instantiate in both, minimal duplication" — scoped to what crosses the wall.** Same seam-law move as CE-037. ⚠ HN-037 lesson: measure what the scenario methods capture before lifting. |
| **B′** ⚠ *(corrected)* | **(1) Scenario Save (edit-mode)** = the editor's exact save, via the shared facade, enabled when a scenario is open for editing *(the 65↔66 gate mostly dissolves — edit-mode = CGF authors, same as editor; ruling 65's stale-default risk only bites if you save a LIVE world as a scenario, which is what checkpoint is for)*. ✅ wiring via the facade. **(2) Checkpoint Save** menu item = wiring over the existing `TakeCheckpointIntent` *(cluster-wide, to the master)*. ✅ **(3) Checkpoint Restore** = 🔴 **does not exist ⇒ a NEW FEATURE** *(a `RestoreSnapshot` handler that reads `.fdp` back into the live repo + a checkpoint list/picker + the cluster fan-out)*, ⛔ not a menu exposure. |
| **New′** ✅ | mode-branched — **live-default:** *clear the running exercise + start fresh*, with a **confirmation dialog** *(a cluster-wide op to the master)*; **edit-mode:** *new scenario from a recipe* *(`RecipePickerSource`/`INewAssetService`, MA-019..023)*. |

### 4.3 ⭐⭐ Proposed sequencing *(2 slices + 1 separate feature)*
| | scope | cost |
|---|---|---|
| ⭐ **Slice A** | the shared scenario-facade extraction *(C′)* + **Load-Edit/Load-Live** *(A′)* + **New** mode-branch *(New′)* + **Scenario Save** edit-mode *(B′ 1)* + **Checkpoint Save** menu item *(B′ 2)* — ALL over existing/extracted capability, on both hosts, from the one shared list | moderate — the extraction is the bulk; the menu items are wiring |
| 🔴 **Feature X** *(separate, bigger — its OWN design)* | **Checkpoint RESTORE** *(B′ 3)* — build the `.fdp` read-back handler + the cluster restore fan-out + a checkpoint list/picker, then the *Restore Checkpoint* menu item | real feature work, not a menu slice |

### 4.4 📌 THE ONE PREMISE CORRECTION FOR THE USER
⚠ Your steer said checkpoint save AND restore are *"all already implemented in the cluster-control panel."* Measured: **save yes, restore no** *(the panel has only a "Take Checkpoint" button — no restore, no picker; §4.1)*. ⇒ decide: **build checkpoint restore as Feature X**, or **ship Slice A now *(incl. checkpoint SAVE)* and defer restore**. Everything else in §4.2 is settled and feasible.

<!-- ⛔ EVERYTHING BELOW (old §4/§5) IS THE PRE-RULING DRAFT — superseded by §3b + §4 above. Do NOT quote as current. -->
## ⛔ HISTORY — pre-ruling draft *(superseded by §3b + §4, `2026-08-26`)*
# Architect Question 60 — **Does CGF host a scenario session, and how?** *(cgf==editor feature parity)*

> 🎯 The menu slice *(CE-041..045)* left CGF's `File/Scenario/×6` empty because `ScenarioMenuCommands`
> needs the editor-only `IEditorLogic`. User *(2026-08-26)*: *"I need the feature parity."* ⭐ **I analyse
> and SUGGEST; you APPROVE** *(CLAUDE.md)*. ⚠ **The DIRECTION is already ruled** *(§3)* — this doc resolves
> the three SPECIFICS the rulings leave open, not a yes/no.

## 1. ⭐⭐⭐ INVENTORY *(graph @ 192k nodes + a read-only scan, `2026-08-26`)*
| piece | what it does | LOAD vs SAVE | binding |
|---|---|---|---|
| `IEditorLogic` *(`Hrot.Editor/IEditorLogic.cs`)* | broad editor god-facade; the 6 scenario members a session needs — `NewScenario`, `LoadScenarioByName`, `SaveCurrentScenario`, `SaveScenarioAs`, `LoadedScenarioName`, `GetMigrationSidecarsForCurrentScenario` — plus tool/view/build/mode | both | ⛔ **editor-bound** *(impl `EditorApplication`)* |
| `ScenarioMenuCommands` | registers `File/Scenario/×6`; needs those 6 members + injected picker/save-as/curated seams *(already non-`IEditorLogic`)* | both | ⛔ editor-bound *(takes `IEditorLogic`)* |
| ⭐ `IScenarioCreationSession` *(+`EditorLogicSessionAdapter`)* | **narrow 3-method seam** New/SaveAs/LoadByName — ⭐ **the extraction is half-done** | both | editor-bound TODAY *(impl wraps `IEditorLogic`)*; the interface is narrow |
| `IScenarioLoader` / `IScenarioStorageProvider` *(`Fdp.Toolkits/Orchestration`)* | read scenario JSON / stage files | **LOAD only** | ✅ **engine/shared** |
| ⭐⭐ `CgfScenarioLoadHandler` + `HrotScenarioLoader` + `LocalDiskStorageProvider` *(CGF)* | cluster-slave: load JSON → extract `EntityCreationRequest`s → genesis pipeline; **"CGF-authoritative"** | **LOAD** | CGF, over the engine seams |
| `HrotScenarioSerializerFactory.Build` *(on CGF, `CgfSubsystem.cs:432`)* | the SAVE serializer — ⭐ **already constructed on CGF**, wired into the inspector | SAVE | on CGF |

⭐⭐⭐ **CGF LOADS today** *(HN-029)* — `POST /scenario/load/{edit,live}` → the orchestration bus → `CgfScenarioLoadHandler`; CGF is the cluster-default entity creator during load *(`CreateEntityRequestSystem isDefaultProcessor:true`)*. HN-029 classed load as its **own** capability *(`scenario.load`)*, ⛔ **NOT** `editor.authoring`. ⛔ **CGF does NOT SAVE** — `saveScenario: null`, scenario-CREATE absent, `DebugApiService.SaveScenario`→`_editor.SaveScenarioAs` *(editor-only)*.

## 2. ⛔ THE `IEditorLogic` WALL — why the menu is empty
`ScenarioMenuCommands` binds the whole `File/Scenario` group to `IEditorLogic`, which is one editor god-object CGF cannot supply. ⛔ Handing CGF a fake `IEditorLogic` is a parallel implementation *(ruling 9)*; a per-host scenario menu is what ruling 58 forbids. ⇒ the wiring needs a **narrow contract**, and one **already exists in embryo** — `IScenarioCreationSession`.

## 3. ⭐⭐ WHAT THE RULINGS ALREADY SETTLE *(the direction — quoted, `2026-08-14`)*
| ruling | settles |
|---|---|
| **66** | *"scenario loading is fully possible from CGF alone"* *(initial ECS state travels with entity creation)*; *"scenario editing joins the authoring tier; the gate collapses to **same component mask on both nodes**."* ⚠ **Correction 47 WITHDREW** the earlier save-time completeness check + serializer diff |
| **65** | CGF scenario editing is *welcome*; ⭐ **the save machinery is already on CGF**; ⚠ residual risk: **registered-but-unpopulated** components *(VehicleState/VehicleParams/NavState registered on CGF but never authored ⇒ a naive save emits stale defaults)* |
| **59 ②** | *"Open = cluster-wide… **the only single-node work is the editor**"* ⇒ every non-editor *Open scenario* is a **request to the master** *(a worded cluster-wide exercise restart, confirmed at origin)*, ⛔ never a local load |
| **58** | *"all should allow **opening existing scenarios** and interactive runtime changes (limited by ECS ownership)"* |

⇒ ⭐⭐ **Direction: CGF hosts scenarios. Load = the existing cluster path. Edit/Save = the authoring tier, welcome, gated.** The open work is the three specifics below.

## 4. ⭐⭐⭐ THE THREE DECISIONS

### 60-A — **What do CGF's `New` / `Load` MEAN?**
| | option | |
|---|---|---|
| ⭐ **A1 (lean)** | `Load` → the **existing HN-029 cluster-load** *(`/scenario/load/edit`)*, confirmed-at-origin *(ruling 59)*; `New` → a cluster-wide clear-world *(same master path)* | reuses what HN-029 built; ⛔ NOT `IEditorLogic.LoadScenarioByName`. The menu item routes to the transition intent CGF already claims |
| A2 | a CGF-local load bypassing the master | ⛔ contradicts ruling 59 *("the only single-node work is the editor")* |

### 60-B — **Scenario SAVE on CGF: enable it, behind what gate?** *(the crux — the 65↔66 tension)*
| | option | |
|---|---|---|
| ⭐ **B1 (lean)** | **Enable save** over CGF's already-built serializer, behind a **mask-equality gate** *(ruling 66)* PLUS an **omit-don't-emit** rule for components CGF neither owns nor live-replicates *(ruling 65's registered-but-unpopulated risk)*. A save-side rail asserts CGF's emitted component set ⊆ what it can vouch for | ⭐ reconciles 65 and 66: 66 withdrew the *heavy* completeness-check, but 65's stale-default hazard is real ⇒ the minimal guard is *"emit only what you own or replicate; refuse/omit the rest, and say so"* |
| B2 | enable save unconditionally *(trust ruling 66's "same mask")* | ⛔ ruling 65's stale-default hazard *(VehicleState/etc.)* is measured, not hypothetical — a CGF scenario could silently reset vehicle params on load |
| B3 | defer save; ship Load/New only | ⭐ viable as **sequencing** *(see §5)*, ⛔ not as the end state — parity means save too |

### 60-C — **The wiring shape** *(so the menu is ONE list — ruling 58/9)*
| | option | |
|---|---|---|
| ⭐ **C1 (lean)** | **Grow `IScenarioCreationSession`** *(or a sibling `IScenarioSession`)* to the 6 members `ScenarioMenuCommands` needs; `ScenarioMenuCommands` takes **that**, not `IEditorLogic`; the editor's impl stays `EditorLogicSessionAdapter`, CGF gets a **new impl** over its serializer + the HN-029 load path | ⭐ the extraction is **half-done already** — extend the seam, ⛔ don't invent *(prior-art discipline)*. One contract, two impls *(ruling 9)*; the `File/Scenario` menu then derives on both hosts like the toolbar/menu common-core |
| C2 | a CGF-private scenario menu registration | ⛔ ruling 58 — no per-host menu code |

## 5. ⭐⭐⭐ RECOMMENDED LEAN — **A1 · B1 · C1**, sequenced as TWO slices
⭐ **Slice 1 *(cheap, mostly wiring)*:** C1's contract + **New/Load** *(A1)* on CGF, routing to the HN-029 cluster path. ⇒ `File/Scenario/New` + `Load` light up on CGF from the shared list, no new load machinery.
⭐ **Slice 2 *(the careful one)*:** **Save/Save-As** *(B1)* behind the mask-equality + omit-unowned gate, with the save-side rail written **before** save is enabled *(ruling 65's "settling test")*. Migration-History + Save-Curated ride along where CGF has the data.
⚠ **Why split:** LOAD is ruled-and-mostly-built; SAVE is the "third tier" with a measured stale-default hazard. ⛔ Bundling them lets the easy half wait on the careful half.

📌 **Open sub-question for B, needs your call:** is the *"same component mask on both nodes"* gate *(ruling 66)* **already built** anywhere, or is Slice 2's first task to build it? *(I did not find a mask-equality gate in the scan — the current guard is capability-absence, not a runtime mask check.)*

⇒ ⭐ **Approve `A1 · B1 · C1` + the two-slice split, or name what to change.** On approval I'll write Slice 1 as a READY-TO-BUILD design; Slice 2 stays a design until its gate question is settled.
