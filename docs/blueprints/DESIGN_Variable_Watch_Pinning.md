<!--STATUS
state: LIVE
build-state: BUILT — slices 94a-94f, the finalization (BP-499..BP-502) and the entity-pinning
  finish (BP-505..BP-507, 2026-08-25).
  ⭐ READ "AS-BUILT — the watch-list finalization" FIRST: it carries five deviations, and two of them
  matter to a reader (the binding lives on the PIN not the row; a concrete pin does NOT survive a
  scenario reload yet). 🔴 Deviation 5 of THAT section carries a CORRECTION: its claim that
  DebugSessionPersistence.Save had no production caller was FALSE. Then read the SECOND AS-BUILT
  section (BP-505..BP-507) — it supersedes the first where they disagree.
updated: 2026-08-25
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

## ⭐⭐⭐ AS-BUILT — **the watch-list finalization (`BP-499`…`BP-502`), `2026-08-24`**

> ⭐⭐ Obligation ⑤. ⛔ Where this disagrees with §1/§1b/§3/§5 above, **it wins** — those are the design,
> this is what the code does.

### ⭐ What shipped

| # | | where |
|---|---|---|
| **`BP-499`** | ⭐⭐ **§1's grouping is WIRED into the Watch** — the window built its model with no `groupBy`, so it fell back to `DetailsDefault` (`[]`) and rendered **one flat list** on the one surface that mixes assets and entities by design | `AiWatchWindow` ctor |
| **`BP-500`** | ⭐⭐ **§1b's group-by selector**, as a SHARED control — the four modes as FACET LISTS | `VariableGroupBySelector` |
| **`BP-501`** | ⭐⭐⭐ **§3's two kinds, with a real shape and a CHOICE** | `EntityBinding` · `PinnedVariableRowSource.Pin(row, binding)` |
| **`BP-502`** | ⭐⭐ **§5's pin set persists** — a fourth list in the existing debug-session file | `PinnedVariableEntry` · `PinnedVariablePersistence` |

### ⛔⛔ The deviations

| # | the design said | 📐 what was measured, and what was built |
|---|---|---|
| **①** | *(the dispatch)* mirror the group-by control on `AiVariablesWindow.GroupBy` | ⛔ **There was nothing to mirror.** 📐 That member is a **property forwarding to `_model.GroupBy`**, and a repo-wide search for a writer found only the model's constructor ⇒ **no group-by UI existed anywhere**. ⭐ So it was built **shared** rather than inside the Watch, since the Variables window needs the identical control *(ruling 9)*. ⚠ Wired to the **Watch only** — adopting it in Variables is a one-line change that window's own batch can make; ⛔ not done to it unasked |
| **②** | §3: the binding lives on the row | ⭐ **It lives on the PIN.** `VariableRowOrigin` is unchanged; `PinnedVariableRowSource` keeps a parallel `(Guid, Entity, string) → EntityBinding` map. ⭐ The binding is a property of *the choice a designer made*, not of a row in general — a section source's rows are always *"the entity this panel is about"*, and widening the row identity would have touched every construction site and the highlight-cache key for a fact only the Watch has |
| **③** | §3: concrete stores the **STAGING** `NetworkIdentity`, resolved through the published `oldToNewMap` | ⚠⚠ **It stores the RUNTIME `NetworkIdentity`, so a concrete pin does NOT survive a scenario RESTART.** ⛔ The remap is still a local inside `StagingEntityExtractor` and publishing it edits `EditorSubsystem`/`EditorApplication` — files the concurrent allocator batch owns. ⇒ deferred by the dispatching handoff §2, and said out loud in `EntityBinding`'s own remarks so nobody reads *"persisted"* as *"restart-proof"* |
| **④** | *(unstated)* the chameleon needs a new encoding | ⭐ **It reuses the sentinel already in place.** `EntityBinding.OriginEntity` projects a chameleon to `default(Entity)` — exactly what `StagedWriteView.EntityFor` and `VariableChangeMonitor` already read as *"ask the selection"*. ⇒ ⛔ nothing downstream changed, and there is no second way to say *"follow the selection"* |
| **⑤** | §5: *"extend `SaveWatches`/`LoadWatches`"* | ⛔ **Those route to the `[Obsolete]` `WatchPersistence` and are breakpoint-only.** `DebugSessionPersistence` was extended instead *(as the dispatch directed)*. ⚠⚠ **The rest of this row was WRONG and is SUPERSEDED — see the correction immediately below.** |

> ## 🔴🔴 **CORRECTION to deviation ⑤ — `2026-08-25`** *(and it was written into three places)*
>
> ⛔ **The claim was:** *"`DebugSessionPersistence.Save` has NO PRODUCTION CALLER — only tests call it;
> the editor's live path still uses the obsolete `SaveWatches`."*
> 🔴 **FALSE.** 📐 `EditorSubsystem.SaveDebugSession()` has called it since `CF-8`, on a 500 ms debounce
> and again at shutdown. ⚠ **The measurement that "proved" otherwise was a `grep` piped through `head`,
> truncated at ten test-file hits before it reached `EditorSubsystem.cs`** — a truncated search presented
> as an exhaustive claim, which is exactly what [`CLAUDE.md`'s inventory rule](../../.claude/CLAUDE.md)
> forbids.
>
> ⭐⭐ **What was actually wrong is narrower and more familiar:** the caller existed and **did not pass the
> optional `pinnedVariables` argument** ⇒ **the SILENT-DEFAULT PATTERN**, not a missing wire.
> ⇒ ⭐ fixed by `BP-506` below; the correction also lands in `PinnedVariablePersistence`'s own remarks.

### ⭐ Two honesty rules the persistence layer enforces

| ⭐ | |
|---|---|
| **an unpersistable pin is SKIPPED and COUNTED** | a concrete pin on an entity with no `NetworkIdentity` has nothing durable to key on. ⛔ Writing it as `NetworkId 0` would restore a pin pointing at nothing, which reads as data loss rather than the within-session pin it always was |
| **an unknown `BindingKind` is SKIPPED, not coerced** | ⛔ the enum's zero value is `Concrete`, so a silent `Enum.TryParse` failure would turn a future kind into a concrete pin on entity 0 and show the wrong entity's value |

### 🔴 Still open after this batch

| ⛔ | ⭐ |
|---|---|
| **restart survival / the `NetworkId` remap** *(slice `94g`)* | ⚠ **not disjoint from the allocator batch** — sequence after it. A concrete pin does not survive a scenario reload until then |
| ~~**the MAP-PICKER for an arbitrary concrete entity** *(§9c)*~~ | ✅ **BUILT** — `AQ55` settled it and `BP-507` shipped it. See the next AS-BUILT section |
| ~~**no production save/load of the pin set**~~ | ✅ **SAVE is WIRED** *(`BP-506`)*. ⚠ LOAD-into-a-window is not — see below |
| ⚠ **the group-by selector is not adopted by the Variables window** | ⭐ deliberate — see deviation ① |

## ⭐⭐⭐ AS-BUILT — **the entity-pinning finish (`BP-505`…`BP-507`), `2026-08-25`**

> ⭐⭐ Obligation ⑤. ⛔ Where this disagrees with anything above, **it wins.**

### ⭐ What shipped

| # | | where |
|---|---|---|
| **`BP-505`** | ⭐⭐⭐ **The session file lives in the USER-LOCAL folder and is force-reset from a git-maintained curated copy on start** | `DebugSessionPaths` · `debug/default/bpsession.json` · `EditorSubsystem.RestoreDebugSession` |
| **`BP-506`** | ⭐⭐⭐ **§5's pin set is actually WRITTEN** — the silent default closed | `EditorSubsystem.WriteDebugSession` / `CapturePinnedVariables` |
| **`BP-507`** | ⭐⭐ **`AQ55`'s "Watch this variable on entity…"** | `WatchEntityPicker` · `AiWatchWindow.PinOnPickedEntityAsync` · `VariableWatchGesture.DecidePinOnEntity` |

### ⭐⭐ `BP-505` — where the file lives

🔒 **User, `2026-08-24`:** *"ad file path - user local folder; BUT during development we need clean env
controlled from git only. let's apply same rule as for curated scenarios and imgui.ini - always overwrite
the user's copy with git maintained curated copy on start."*

| ⭐ | |
|---|---|
| **from** | `<repo>/.debug/bpsession.json` *(`CF-8`'s choice)* |
| **to** | `LocalApplicationData/HROT/bpsession.json` — ⭐ the alternative the same `CF-8` design already named *(`.dev/blueprint-dbg-1/TASK-DETAIL.md:699`: "a gitignored path … **or the editor's per-user data dir**")* |
| ⛔ **why the move was FORCED, not chosen** | 📐 **`.gitignore:65` ignores `.debug/`** ⇒ that location cannot host a git-maintained curated copy. The ruling's two halves are only satisfiable together by moving the user copy out |
| ⭐⭐ **which pattern** | the **`imgui.ini`** one — `LayoutPaths.TryResetUserLayout` copies from the **output directory**, so the reset holds in a deployed build and in CI. ⛔ **Not** `CuratedScenarios`, which walks up to the source tree and is dev-only by construction: a deterministic clean environment is wanted everywhere, which is the point of the ruling |
| ⭐ **the git home** | `debug/default/bpsession.json`, copied to `<output>/debug/` by `Hrot.ClusterRunner.csproj` — ⛔ the same `Content … Link` shape `layout/default/*` already uses |
| ⚠⚠ **a side effect worth naming** | 📄 [`FINDINGS_Empty_Breakpoint_Bricks_The_Editor.md`](FINDINGS_Empty_Breakpoint_Bricks_The_Editor.md): a poisoned session file killed the editor on **every** launch and *"the only recovery was deleting a gitignored file by hand"*. ⭐ With the reset, the poison survives at most one run |

### ⭐⭐ `BP-506` — the pins reach the file

⛔ **The defect was a SILENT DEFAULT, not a missing wire** — see the correction to deviation ⑤ above.
⭐ **The control** is a rail on the **CONSTRUCTED FILE** *(`R-67`)*, which is why `SaveDebugSession` was
split into a fully-parameterised `WriteDebugSession`: the rail drives the real production path rather
than a re-implementation, and the delegation left behind has **no defaultable argument to forget**.

### ⛔ `BP-507` — the ONE deviation from `AQ55`, argued

| | |
|---|---|
| **`AQ55` drew** | `AiWatchWindow ..> IMapPickService : PickEntityAsync` |
| **built** | `AiWatchWindow` takes a `WatchEntityPicker` **delegate**; the composition root implements it by calling `IMapPickService.PickEntityAsync()` and resolving the id through `FindEntityByNetworkId` |
| 📐 **why** | `IMapPickService` lives in `Hrot.Presentation`, which `Hrot.Editor.AiShared` does **not** reference. Adding that edge points the shared editor library at the application layer that composes it. ⭐ This codebase already has a settled shape for *"an AiShared window needs a host capability"* — a host-installed delegate *(`SetRunStateSource`, `SetFacetEditService`, `SetFacetDispatcher`)* |
| ⭐ **what is NOT lost** | `Q55-A`'s ruling was **REUSE the existing pick service** — and it is reused, unchanged, from the composition root. Only the way its type crosses an assembly boundary differs |

⭐ **`Q55-B`** *(the `Hrot.Presentation/Facades` twin)*, **`Q55-D`** *(⛔ not the attribute path)* and
**`Q55-E`** *(no filter in v1)* are built exactly as ruled. ⚠ The two ruling-9 duplicates AQ55 surfaced
are still open and untouched, as it directed.

### 🔴 Still open after THIS batch

| ⛔ | ⭐ |
|---|---|
| **a concrete pin does not survive a scenario RELOAD** *(slice `94g`, `BP-503`)* | ⭐ **now UNBLOCKED** — `HN-037`'s world-boundary/id-remap merged — but it edits `DataBreakpointManager` *(`:1354` still throws for `NetworkId`)* and consumes that remap ⇒ its own slice |
| ⚠ **a restored pin is not re-attached to a Watch window** | `PinnedVariablePersistence.Restore` produces descriptors and nothing consumes them: a row can only be rebuilt by the source that owns its asset, once that asset is open ⇒ the **same** resolution problem as `94g` |
| ⚠ **the two ruling-9 duplicates** *(two `IMapPickService`, two `MapPickableEntityAttribute`)* | `AQ55` §"Two ruling-9 duplicates" — each its own cleanup |


## 10. ⚠ Open

⭐ **Watching variables from DIFFERENT ASSETS in one panel** — the key supports it and the store is
shared, ⚠ **but the poll would span debug sessions.** ⛔ **Out of slice 1.**
