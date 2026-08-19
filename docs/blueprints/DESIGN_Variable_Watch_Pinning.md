<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: this whole file - it is the consolidated design, written to be built from
stale-below: nothing. History and the decision trail live in Architect_Question_40, which
  accumulated four rounds of correction and must NOT be read as a spec.
supersedes-for-implementation: Architect_Question_40_Watch_Variable_Pinning.md
known-rot: section 4's cost model predates Batch 90. It assumes the VALUE clock needs
  new per-tick polling machinery. Batch 90 built the live-value arms (GetLiveObjects /
  GetLiveBytes plus the readRaw seam), read per frame, and a pinned row carries its arm
  with it - so slice 1 is materially cheaper than this document assumes. The TWO CLOCKS
  rule itself (value vs binding) is unaffected and still binds.
-->

> ⚠⚠ **STATUS `2026-08-19` — NOT BLOCKED, NOT STARTED.** ⛔ `R-27` gates `Q38`/`Q44`; **this is
> neither**, and it targets `AiWatchWindow`, the window `Q38-E` already picks as the survivor ⇒
> ⭐ **building it is aligned with that merge, not in conflict.**
>
> 📐 **Measured `2026-08-19`:** the **store** exists *(`PinnedVariableRowSource.Pin`, only caller is
> `TrackCWiringTests:235`)* · the **surface** exists · the **render** is shared. ⛔ **The GESTURE does
> not exist:** `CommandCatalog.ToggleWatch` is absent, and `CanvasRenderer:684`'s
> `MenuItem("Watch this Value")` sits **inside `BeginDisabled()`** with no handler — ⚠ **and it is a
> PIN menu, not a variable row.**
>
> ⭐ **Slices 1–2 are unblocked and now cheaper than §8 assumes** *(see `known-rot`)*.
> ⚠ **Slice 3 still carries the two costs this document flags as unmeasured.**

# DESIGN — **pinning a variable to the Watch panel**

> ⭐⭐ **This is what gets built.** 📌 The decision trail — options, my wrong turns, the user's four
> corrections — is [`Architect_Question_40`](Architect_Question_40_Watch_Variable_Pinning.md).
> ⛔⛔ **Do NOT build from `Q40`**: it states a recommendation in §3, amends it in §0, replaces it in §9
> and corrects itself again in §9e. ⭐ **This file is the flattened result.**
>
> ⭐ **User requirements, `2026-08-18`:** a context-menu entry on a My Blueprint variable row · works for
> **any** variable type **including locals** · **evaluates every brain tick** · **works for HSM and
> BTree too** · **watch content shared across perspectives** · **survives scenario restart** ·
> ⛔ **not one row per entity** *(thousands of them)* · entity is part of the row id, with explicit
> selection **and** a **chameleon** id that follows the current selection.

---

## INVENTORY — *what already exists* 📌 `R-74`

```
search_graph(label="Class", name_pattern=".*Watch.*(Window|Panel|Source|Store).*")  → 7
search_graph(label="Class", name_pattern=".*(RowSource|DebugSession)$")             → 17
search_graph(query="persistent stable entity id across restart mapping")            → 751
```

| ✅ exists | where | ⭐ role here |
|---|---|---|
| **`AiWatchWindow`** | `Hrot.Editor.AiShared` | ⭐⭐ **the target surface** — shared registrar ⇒ all three perspectives |
| **`PinnedVariableRowSource`** | `AiShared/Variables/VariableRowSources.cs` | ⭐⭐ **the store already exists**, with a public `Pinned` accessor |
| `VariableTableControl` · `VariableValueFormatter` · the edit dialog | `AiShared` | ⭐ render + edit, shared since Batches 82–83 |
| **`VariableRow.AssetTick`** | per-row delegate | ⭐⭐ **the host-neutral tick seam, cut open on purpose in Batch 68** |
| **`EditorSelectionStore.SelectedEntity`** | `AiShared/Selection` | ⭐⭐ **the chameleon's source** |
| **`NetworkIdentity { long Value }`** | `Fdp.Toolkits/Replication` | ⭐ the stable entity identity |
| **`SaveWatches` / `LoadWatches`** | `DataBreakpointManager` | ⭐ persistence to extend |
| ⚠ **`WatchPanelWindow`** | `Hrot.Blueprints.Editor` | ⛔ **the OTHER watch window** — blueprint-only. ⛔ **not the target**; retirement is row 60 |
| ⚠ **`EntityWatchPanel` · `FdpEntityWatchWindow`** | `Fdp.Presentation` · `Hrot.Presentation` | ⛔ **entity-COMPONENT watching, a different concept.** Named so nobody "unifies" them by mistake |
| ⚠ **`FindEntityByNetworkId`** | ⛔ **TWICE** — `ReplayBrowserSubsystem:933`, `EditorMissionService:54` | ⭐ **unify into one resolver; this design is the third caller** |

---

## 1. ⭐⭐ The model — **a watch row is a `VariableRow` with an entity binding**

⛔ **The Watch panel stops being *"pin values pushed from generated code"*** and becomes a list of rows.

```
WatchPin
  Asset       : AssetId
  Variable    : VariablePath          ← asset variable OR graph local; ⭐ identical treatment
  Entity      : EntityBinding         ← §3
  (row value) : resolved per tick     ← §4
```

⭐ **Everything downstream is already shared** — table, formatter, grouping, stale-greying, edit dialog.

## 2. ⭐⭐⭐ Two feeds, one row type

| feed | for | why |
|---|---|---|
| **PUSH** *(exists — `OnPinValueChanged`)* | **pins** | ⭐ **a pin is a transient value on an edge — there is no address to poll** *(user)* |
| ⭐ **POLL, every brain tick** | **variables**, locals included | a variable **is** a stored field at a byte offset. 📌 **`R-49` forbids per-variable codegen**, and a push would need a **recompile to pin something** |

⛔ **Not a ruling-9 violation** — 📌 the design already has `SectionSource` and `PinnedSource` feeding one
control. ⭐ **Two SOURCES, not two implementations.**

⭐⭐ **Locals need no special case:** `GraphLocalSlots` are laid out in the **same emitted struct**
*(both emitters + `FieldLayout`)* ⇒ a local has a byte offset like any other field.

## 3. ⭐⭐⭐ The entity binding — **TWO kinds** 📌 `R-78`

| kind | stored | resolved |
|---|---|---|
| ⭐ **concrete** | ⭐⭐ **the STAGING `NetworkIdentity.Value`** — ⛔ **never an `Entity` handle** *(slot/generation, recycled)* | staging id → runtime id via the published map *(§5)*, then `FindEntityByNetworkId` |
| ⭐⭐ **chameleon** | a sentinel | `EditorSelectionStore.SelectedEntity`, **on selection change** *(§4)* |

⛔⛔ **There is NO third, entity-less kind.** 📐 **Every runtime value is entity-bound** — even shared
state: `BlueprintSharedState` is *"an **ENTITY-scoped** shared working-state slot"* taking `self`;
*"shared"* = across blueprints on **one entity**. ⚠ **`Scope=Asset` is VISIBILITY, not storage**
*(`R-07`)*. ⭐ **The only entity-less value is `DefaultValueJson` — constant, not worth watching.**

⛔ **No auto-expansion to all entities** — the designer picks, and ⭐ **the chameleon is the answer to
*"I don't want to pick"***.

## 4. ⭐⭐⭐ TWO CLOCKS — **the rule that must not be collapsed** 📌 `R-76`

| clock | answers | fires |
|---|---|---|
| ⭐ **VALUE** | *what does this field hold?* | ⭐ **every brain tick** — all rows, chameleon included |
| ⭐⭐ **BINDING** | *which entity is this row about?* | ⛔ **not the tick** — ⭐ **only on selection change**, and **only** for the chameleon |

⛔⛔ **Re-resolving the binding per tick would churn the row's identity under the cursor.**
⭐ **A concrete row's binding never changes at all.**

## 5. ⭐⭐ Persistence and restart survival — **BY TRANSLATION** 📌 `R-75`

📐 **`StagingEntityExtractor` Pass 1 allocates a NEW network id on every load** and records
`oldToNewMap` — ⛔ **which is a LOCAL that dies inside the extractor.**
⇒ ⛔⛔ **A watch keyed on the RUNTIME id breaks on every scenario restart.**

| ⭐ the design | |
|---|---|
| **key on the STAGING id** | ⭐ the stable authoring artefact — **what the designer was looking at when they pinned** |
| **resolve at BIND time** through the published map | ⭐ re-binds automatically on each load |
| ⭐⭐ **publish `oldToNewMap` on the ORCHESTRATION BUS** | 📌 **`R-79`: `EditorSubsystem` and `CgfSubsystem` are separately deployable** ⇒ ⛔ **an in-process shared object is wrong**; the bus is the channel `EditorApplication:78` already reads |
| ⛔⛔ **do NOT move or copy the remap CODE** | ⭐ **ruling 9 on the most safety-critical mapping in the system** — a divergent copy would silently point watches at the wrong entity |
| **persist the pin set** | ⭐ **extend `SaveWatches`/`LoadWatches`** — ⚠ today they persist breakpoints marked `IsWatch`, keyed by `PropertyMatchDto`, **not entity-keyed at all**. ⛔ **Do not invent a second file** |

## 6. ⭐⭐ Cross-host — **shared by construction**

| ✅ already shared | the store · the window · the table · the formatter · the dialog · the row identity · the tick seam · **watch AND breakpoint content across perspectives** *(one `_bpManager`, all three registrars)* |
|---|---|
| ⭐ **host-specific — DATA, not machinery** | ① the **blackboard base offset** *(`Blackboard1024` is ONE component shared at disjoint offsets — `R-65`)* · ② where the **field layout** comes from |
| ⚠ **not needed for the poll** | 📌 **`R-70`: `HsmDebugSession`/`BTreeDebugSession` are built and never constructed** — ⛔ that blocks **breakpoints, pause, step** on BTree/HSM, ⭐ **not this** |

⭐⭐⭐ **The base offset must be owned in ONE place** — 📌 the same *"whoever computes the offset must own
that `+8` in one place, not two"* the running write is held to. ⭐ **Solve once, for both.**

## 7. ⭐ The gesture

| | |
|---|---|
| **where** | ⭐ **My Blueprint row context menu** *(as requested)* **AND the Details table row** — 📌 design §4: *"identical everywhere"*; a one-surface gesture re-creates the split `U-6` removed |
| **what** | a **toggle** — *"Watch this variable"* / *"Stop watching"* |
| ⭐ **when it may be USED** | **Planning** ✅ · **Paused / stepping** ✅ · ⛔ **free-running: FORBIDDEN** *(the poll list is read by the tick)* · ⛔ **replay: forbidden** |
| ⭐⭐ **how it refuses** | **greyed + a tooltip saying why** — 📌 user, `2026-08-17`: *"same information value, no false expectations."* ⛔ **never a click that dead-ends** |
| **run state from** | 📌 **`R-69`: the CLUSTER STATE** — ⛔ not a new notion of "running" |
| ⭐ **the canvas stub** | `"Watch this Value"` leaves `BeginDisabled()` and invokes `CommandCatalog.ToggleWatch` ⇒ **one command, two entry points** |

## 8. ⭐ Slices, and the two costs to size FIRST

| slice | | acceptance |
|---|---|---|
| ⭐ **1** | the mechanism in `AiShared` — gesture → `Pinned.Add`, poll per row via `AssetTick`, base offset as host data | ⛔ **NO blueprint-specific code in `AiShared`** — ⭐ *not* "it works on Blueprint" |
| ⭐ **2** | each host supplies tick source + base offset | ⭐⭐ **if this is not nearly free, slice 1 leaked host knowledge** |
| ⚠ **3** | publish `oldToNewMap`; unify `FindEntityByNetworkId` | ⭐ restart survival + the third caller |
| ⚠ **4** *(separate)* | wire `HsmDebugSession`/`BTreeDebugSession` | for breakpoints/step, not the poll |

> ⚠⚠ **MEASURE BEFORE COMMITTING — two costs, neither yet sized:**
> ① **publishing `oldToNewMap`** reaches into the **CGF orchestration path**, outside the editor.
> ② **unifying `FindEntityByNetworkId`** touches ReplayBrowser and the editor mission service.
> ⭐ **Both are small in principle. Neither has been measured.** ⛔ **Do not assume — that assumption is
> what produced four corrections to this design.**

## 9. ⛔ What must NOT be built

| ⛔ | why |
|---|---|
| a second watch window, or work on `WatchPanelWindow` | ⭐ target `AiWatchWindow`; retirement is row **60** |
| a per-variable emitted push | 📌 **`R-49`** |
| an editor-side copy of the id remap | 📌 **`R-79`** — ruling 9 |
| a fourth `FindEntityByNetworkId` | 📌 **`R-77`** |
| one row per live entity | ⛔ **user: *"unbearable — thousands of entities"*** |
| a panel-wide tick | 📌 *"rows tick at different rates"* |
| touching `EntityWatchPanel` / `FdpEntityWatchWindow` | ⭐ **different concept** — entity components |

## 10. ⚠ Open

⭐ **Watching variables from DIFFERENT ASSETS in one panel** — the key supports it and the store is
shared, ⚠ **but the poll would span debug sessions.** ⛔ **Out of slice 1.**
