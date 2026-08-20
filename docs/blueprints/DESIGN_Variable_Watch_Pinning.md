<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - it is the consolidated design, written to be built from
stale-below: nothing. History and the decision trail live in Architect_Question_40, which
  accumulated four rounds of correction and must NOT be read as a spec.
supersedes-for-implementation: Architect_Question_40_Watch_Variable_Pinning.md
known-rot: CORRECTED 2026-08-19 by Batch 93. An earlier note here claimed Batch 90's
  live arms made slice 1 nearly free because a pinned row carries its arm. MEASURED FALSE:
  the arms a row SOURCE builds close over that frame's VALUE, not over the provider, so
  liveness in Details comes from REBUILDING the row each frame - and PinnedVariableRowSource
  returns its stored records untouched. A pinned row is a SNAPSHOT. See Q46; section 4's
  VALUE clock is a real problem after all. The TWO CLOCKS rule itself still binds.
-->

> 🛑🛑 **STATUS `2026-08-19` — BATCH 93 ATTEMPTED IT AND STOPPED.** ⛔ **A pinned row is a SNAPSHOT**:
> the arms a row SOURCE builds close over **that frame's value**, and `PinnedVariableRowSource` returns
> its stored records untouched ⇒ **the pin freezes, and `(pending)` freezes with it.**
> ⭐⭐ **The store, the window and the table are FINE** — a hand-built row with a live arm stays live.
> ⇒ 📄 **[`Architect_Question_46`](Architect_Question_46_What_A_VariableRow_Means.md)** must be answered
> before this design can be built. ⛔ **Do not start from §4 until it is.**
>
> ⚠ **STATUS `2026-08-19` (earlier) — NOT BLOCKED, NOT STARTED.** ⛔ `R-27` gates `Q38`/`Q44`; **this is
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
> ✅✅ **Slice 3's two costs are MEASURED — §8. BOTH SMALL**, and its one decision is **RULED**:
> ⭐ **a callback sink on the extractor, wired to the bus by the subsystem** *(user, `2026-08-19`)*.
> ⇒ ⭐⭐ **slices 1–3 are ONE batch**; ⛔ nothing about this design is open any more.

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

### ✅✅ THE TWO COSTS — **MEASURED `2026-08-19` (coordinator).** ⭐ **Both are SMALL**

> ⚠ The previous text said *"both are small in principle; neither has been measured."* ⭐ **Measured now.**

#### ⭐ ① publishing `oldToNewMap` — **small–medium, and the feared hazard is absent**

| 📐 measured | |
|---|---|
| ✅ **the bus accepts the payload AS-IS** | `FdpEventBus.PublishManaged<T>` carries **no `unmanaged` constraint** — its own comment: *"No class constraint — allows managed structs"* ⇒ ⭐ **a `Dictionary<long,long>` travels whole.** ⛔ **No flattening to arrays** |
| ✅ **the pattern is established** | `RegisterManaged<T>()` at bootstrap *(`OrchestrationEventRegistry:17`)* → `PublishManaged` → `ReadManaged`. ⭐ **`EditorApplication` already holds the bus and both READS (`:78`) and PUBLISHES (`:94`)** |
| ⚠ **the map is a LOCAL** | `StagingEntityExtractor:204`, and `Extract` returns only `IReadOnlyList<EntityCreationRequest>` |
| ⚠ **the reach** | `Extract` has **3 production call sites** — `CgfEpisodeLoadHandler:127` · `CgfScenarioLoadHandler:152` · `HrotScenarioLoadHandler` — plus the interface and its explicit impl |
| ⛔ **the handlers hold NO bus** | measured on `CgfScenarioLoadHandler`'s field list ⇒ *"the handler publishes it"* is **not** free |

| ✅✅ **RULED `2026-08-19` (user): the CALLBACK SINK** | |
|---|---|
| ⭐⭐⭐ **an optional `Action<IReadOnlyDictionary<long,long>>` sink on the extractor**, wired to the bus **by the subsystem** | ⭐ **zero new assembly dependencies** — `Hrot.CGF` never learns about the bus; ⭐ **`R-79` intact** *(separately deployable)*; ⭐ the same `Func<>`/callback seam `LiveBlackboardValueProvider` already uses |
| ⛔ **not**: the extractor takes a bus | it stops being a pure transform, and `Hrot.CGF` gains a dependency for one line |
| ⛔ **not**: widen `Extract`'s return | ⚠ **3 call sites + an interface + an explicit impl**, for a value only one caller wants |

⛔ **The remap CODE still does not move or get copied** *(ruling 9)* — ⭐ **only the map is published.**

#### ⭐⭐ ② unifying `FindEntityByNetworkId` — **SMALL. Ten lines, twice**

| | `ReplayBrowserSubsystem:933` | `EditorMissionService:54` |
|---|---|---|
| query | ⛔ `Query().Build()` — **everything**, then `HasComponent` per entity | ⭐ `Query().With<NetworkIdentity>()` — **filtered** |
| read | ⭐ `GetComponentRO` | ⛔ `GetComponent` *(copy)* |
| null repo | ⭐ guarded | ⛔ unguarded |

⇒ ⭐⭐ **Neither is the one to keep — take the best of each:** **the filtered query** + **`GetComponentRO`** + **the null guard**. ⭐ **Home: `FDP/Toolkits/Fdp.Toolkits/Replication/`**, which already has `Extensions/` and `Utilities/` and owns `NetworkIdentity` itself.

⚠⚠ **Both are LINEAR SCANS**, and a watch panel resolving per tick would be **O(rows × entities) per frame**. ⭐⭐⭐ **§4's TWO CLOCKS rule already forbids that** — a binding resolves **only on selection change / load**, never on the tick. ⇒ ⛔ **no index, no cache; do not "optimise" it.** ⭐ **The two-clocks rule is what makes the linear scan correct.**

#### ⭐ VERDICT

⭐⭐ **Slice 3 is SMALL, and the CGF reach the old text feared is not there.**
✅✅ **The one real decision is RULED** *(user, `2026-08-19`: "callback sink")* — ⭐ **an optional
callback sink on the extractor, wired to the bus by the subsystem.**
⇒ ⭐⭐⭐ **Slices 1–3 are now ONE batch.** ⛔ Splitting was only worth it while slice 3 was unsized.

## 9. ⛔ What must NOT be built

| ⛔ | why |
|---|---|
| a second watch window, or work on `WatchPanelWindow` | ⭐ target `AiWatchWindow`; retirement is row **60** |
| a per-variable emitted push | 📌 **`R-49`** |
| an editor-side copy of the id remap | 📌 **`R-79`** — ruling 9 |
| ⚠ **a FIFTH `FindEntityByNetworkId`** | 📌 **`R-77`, COUNT CORRECTED `2026-08-19`: there are FOUR, not two** — `M-26`. ⭐ The intent stands |
| one row per live entity | ⛔ **user: *"unbearable — thousands of entities"*** |
| a panel-wide tick | 📌 *"rows tick at different rates"* |
| touching `EntityWatchPanel` / `FdpEntityWatchWindow` | ⭐ **different concept** — entity components |

## 10. ⚠ Open

⭐ **Watching variables from DIFFERENT ASSETS in one panel** — the key supports it and the store is
shared, ⚠ **but the poll would span debug sessions.** ⛔ **Out of slice 1.**
