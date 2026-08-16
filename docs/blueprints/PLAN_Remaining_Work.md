# PLAN — what is left *(revision 3, `2026-08-15`)*

> ⭐⭐⭐ **REVISION 3.** ✅ **Track C's three decisions are RULED and its design is written** —
> 📄 **[`DESIGN_Variable_Details_And_Editing.md`](DESIGN_Variable_Details_And_Editing.md)**.
> **Rev 2** folded in three coordinator subagent scans over `.dev/` (~2887 files) + the implementation
> session's sweep; **rev 1** was written from **code alone**.
> ⛔ **Two of my own rev-1 conclusions were WRONG (§0).** 📄 Sources:
> [`REPORT_Batch64_Dev_Sweep.md`](REPORT_Batch64_Dev_Sweep.md) + the three coordinator scans.

![remaining work](PLAN_Remaining_Work.svg) *(diagram predates this revision — tracks still hold, contents changed)*

---

## 0. ⛔⛔ **I repeated the Batch-63 mistake. Twice, in this plan.**

| my v1 claim | the design record | verdict |
|---|---|---|
| *"delete the dead `IStructEditDrawer`/`DrawerRegistry` chain"* (`C6`) | `_DONE/blueprints-1` DD §7.1/§7.8 + `BATCH-22-INSTRUCTIONS:297-380` **specify it verbatim**; still referenced by `InspectorWindow.cs:10,17` and `BlueprintWindowRegistrar.cs:21,29`; `MVE-BATCH-04-REPORT.md:194` frames removal as **part of retiring the legacy `BlueprintEditorModule`**, not an isolated deletion | 🔴 **WRONG — "designed, pending a larger retirement", not dead code** |
| *"`BlueprintVariablesWindow` is redundant, retire it"* (`C6`) | migrated onto `VariablesPanelControl` (`BATCH-15`), carries the `{DtoTypeFqn}::{FieldName}` rename context (`BATCH-16`); a deliberate *"wrapper instead of modifying it"* decision exists **to avoid rewriting a working, tested class** | 🔴 **WRONG — a re-host, not a retirement** |

⇒ ⭐⭐ **Same shape both times: a grep answered *"is it used?"* and I read it as *"is it wanted?"***

---

## 1. ✅ Done — merged through `9edf13fdf`

Batches **56 · 58 · 57 · 59 · 60 · 61(1–2) · 63**. Phase A correctness is complete except `W6`/`W7`,
which the sweep has now **re-specified** (§4).

---

## 2. ⏭ Track B — struct support ⭐ **all three now have design records. Ready to build.**

| | design record | what changed |
|---|---|---|
| **`S2`** struct size resolution | `.dev/btree-ai-action-binding/reports/BATCH-03-REPORT.md:34` — ⭐⭐ **a stated mandate**: *"`StructSizeResolver` lives in Generators and is **injected via `Func<string,int?>`**; Persistence stays netstandard2.0 / Roslyn-free"*, from a **user decision `2026-06-15`** (`TASK-DETAIL.md:58`) | ✅ **my lean CONFIRMED, with a shipped precedent** (`BTreeBlackboardPackHelper.Pack(vars, Func<string,int?>, out total)`) ⚠ **but `:100` files `DEBT-AIB-012`: the resolver is ALREADY a third copy of `ComputeStructSize`** ⇒ **a naïve `S2` makes a fourth** |
| **`S3`** `MarshalFromBytes` struct arm | `_DONE/blueprints-1/TASK-DETAIL.md:1840` — *"reflection-based for structs (UI decode only, not on the probe path)"*; `blueprint-dbg-1:193` — *"primitives/**small** structs only"* | ✅ **CONFIRMS** — ⭐ designed in from the start, **never built**. The record also **bounds** it |
| **`S4`** fixed-list `Capacity` | ⭐ **outside `.dev/`** — `docs/blueprints/Blueprint_List_Variables_Design.md` §3:63–72 **specifies the exact missing branch**: *"`StaticTypeRegistry.TryResolve`: new branch — when `Capacity > 0` and the element resolves unmanaged, return the list `IrTypeRef` (unmanaged, real size)"* | 🔴 **REFINES** — a **designed-but-unbuilt branch**, not a bug. ⚠ Must honour `SizeReliable = false` and the `__List_{Elem}_{N}` wrapper name |
| **`S5`** one picker | `blueprint-finalize/BF-BATCH-FIXEDSTRING-INSTRUCTIONS.md:33` treats **`SelectableTypeIds`** as *the* picker list; `EditorOfferableTypeIds` is never mentioned | ⭐ **CONFIRMS the defect is real and undocumented** — the second list grew later on the compiler side |

---

## 3. ✅ Track C — the panels. ⭐⭐ **DESIGNED. The three decisions are all ruled.**

📄 **[`DESIGN_Variable_Details_And_Editing.md`](DESIGN_Variable_Details_And_Editing.md)** *(+ SVG)* —
⭐ **that is what gets built.** ⛔ **It supersedes this section and `DESIGN_Variable_Details_And_Live_Values.md` §8.**

| decision | ruling |
|---|---|
| ① **the write path** | ✅ **OPTIMISTIC DISPLAY.** Paint the new value immediately, then **stage** through the existing path. ⛔ **Do NOT write `_liveRepo` while paused** — `Blackboard1024` is `[DataPolicy(NoSave)]`, i.e. **snapshotted AND recorded**, so a non-simulation write breaks Flight Recorder linearity |
| ② **the gesture** | ✅ **two menu items = the two `EditScope`s.** *"Edit value…"* (`ForField`, double-click the **value** cell) · *"Properties…"* (`WholeComponent`, double-click the **name** cell). ⭐ **Run state decides WRITABILITY, not which dialog** |
| ③ **table or form** | ✅ **TABLE**, filtered by section — ⛔ **never a single-variable form.** `D7`'s field list becomes **the dialog's** contents |

### ⭐ What the design settles beyond those three

| | |
|---|---|
| **columns** | `Name` + `Value` **mandatory**, `Type` **one toggle** *(hidden in Watch, shown in Details)*. ⛔ **No general column framework** — the control has **seven** today |
| ⭐⭐ **generic row list** | the control renders `IReadOnlyList<VariableRow>` and **knows nothing about the source.** `SectionSource` (Details) · `PinnedSource` (Watch, **mixed assets and entities**) |
| **identity** | `(AssetId, Entity, VariablePath)` — ⛔ **entity is part of it** |
| ⭐ **grouping** | `GroupBy` = an **ordered facet list** *(`[]`, `[Entity]`, `[Asset]`, `[Asset, Entity]`)* — ⛔ not hardcoded modes. **A uniform facet emits no header.** Folding is `CollapsingHeader` *(already used 3× in that control)*, and ⭐⭐ **a collapsed header inherits its children's red/yellow** |
| ⭐⭐ **change highlight** | 🔴 **red one tick** = the sim changed it · 🟡 **yellow** = your pending edit. ⭐ **The unit is a NON-FROZEN ASSET TICK** — not a frame, not a world tick ⇒ **paused, the highlight persists until you Step.** ⭐ **Diff RAW BYTES** |
| **value rendering** | primitives inline · structs = elided one-line summary + **pretty-printed tooltip** · ⛔ **never raw hex** *(`BP-01`'s symptom)*; undecodable says `<unreadable>` |
| **budget indicator** | ⭐ **planning-only chrome**, on the same run-state switch as the Value column |

⚠ **Track C still needs the VISUAL CHECK** — grouping, folding and colour are surfaces no headless test
can verify. ⭐ **But the change-highlight PREDICATE and the grouping/column rules are headlessly testable.**

---

## 4. ⏭ Track D — ⭐ **the sweep rewrote most of this**

| | verdict |
|---|---|
| **`W6`/`W7`** | 🛑 **`W7` CONTRADICTED.** `Blackboard_Authoring_Detailed_Design.md` §7.7/§9.1–9.6 is a **complete design**: a **suppressible WARNING** (not an error) with per-conflict metadata + an *"Allow concurrent writes"* checkbox; writers classified by **whether the action mutates the ref parameter** (optional annotation, conservative read-write default) — **not** by `W6`'s static projection; and §9.1 says **extend the existing `OutputLaneMask` conflict infrastructure**. §9.5 adds an **Approach B Sync-Out** case we omitted. ⇒ ⭐ **`W6` is downstream of a mechanism the design does not use — re-derive `W6` from §9.6 or drop it.** ✅ `[SharedAiCondition]` re-measured at **0 production usages** |
| **`W8`** | 🔴 **REFINES strongly.** `SLICE1-DESIGN.md:85` is an **architect-CONFIRMED ruling (`2026-06-15`)**: defaults baked into `ParseParamsDelegate`, **overlay scenario JSON (runtime wins)**, heavy tier needs a `StructureHash` init check. **`DEBT-AIB-013` RESOLVED** (defaults shipped); **`DEBT-AIB-021`** *is* the overlay half, **with a prescribed implementation**. ⭐⭐⭐ **And `BlackboardVariableRole { Input=0, State=1 }` + `WorkingStateScope { Node, Behavior, Entity }` ALREADY EXIST** in `Hrot.AiEditor.Persistence` — **the Role × Scope model is already persisted and round-trip-tested** ⇒ **`D2` may be dissolved: the carrier exists** |
| **`W9`** | ⚠ **premise coordinator-verified as REAL but MIS-LOCATED:** `HsmBridgeEmitCore` bakes **no key at all** (post-Batch-59); the simple-name hash is **`HsmActionGenerator:517/630` — `ComputeHash(action.Name)`**, and `MethodInfo` carries both `Name` and `FullName`. ⛔ **And the re-bake is TWO sites, not one** — blob key + thunk key, reconciled *"in lockstep via shared `ResolveStatefulSlotKey`"* |
| **`W10`** | ✅ mechanism **CONFIRMED** — `AN7-REPORT.md:73–95` is the **exact precedent** for *"add a source enum member + contributing catalog, not a new picker"*. 🔴 **But *"persist the catalog `Id`"* CONTRADICTS an architect ruling:** `blueprint-finalize/TASK-DETAIL.md:248` — *"Canonical identity = generated **FQN**, **not** AssetId (architect AQ2)"*. ⚠ `BehaviorActionSource.AiPrimitive` exists but is **never assigned** |
| **`W11`** | 🔴 **NOT a "twin", and not implementable as written.** `FIX-01-REPORT.md:43` — *"the HSM binding model is structurally different: there is **no per-node `ExpressionTargetField`**"*; **`VE-DEBT-001`**: an HSM state hosts **4 action slots** (Entry/Exit/Activity/Timer) so one-DTO-one-variable *"**needs an architect design call — not an autonomous guess**"*; **`VE-DEBT-004`**: **no production `[HsmGuard]` exists** to bind against. ⛔ **`HSM-016` is an UNRESOLVABLE id — zero hits anywhere; nothing defines what it says** |
| **`W12`** | 🔴 **two of four "new" pieces already exist.** ⭐ **"world-singleton" is shipped runtime API** (`BlueprintRegistry.RegisterWorldSingleton`, `TickWorldSingletons`, `Self = Entity.Null`) ⇒ **adopt, do not coin.** ⭐ **geo→cartesian + entity-from-network-id already exist as hand-written code** — the very programmer `W12` removes — and are **already contested on semantics** (an open lead decision on `AttackDir`) |
| **`W13`** | ✅ **DONE** (Batch 63) |

---

## 5. ✅ The two prerequisites — **one is solved, one is a live defect**

| | verdict |
|---|---|
| ✅ **the paused snapshot-vs-live pass** | ⛔ **STRIKE — my concern was wrong.** `universal-breakpoints-DESIGN.md` §8.4 designs against it and it **shipped**: an edit while paused is **staged, not written**; on Step/Continue the manager **restores `_liveRepo` from `_postTickSnapshot` FIRST, then drains** — coordinator-verified at `DataBreakpointManager:495-498` and `:514-517`. **The rewind cannot discard the edit.** Cost: a named **1-tick latency compromise** |
| 🔴🔴 **the surgical ECB field write** | ⭐ **Ruling 14 already rules it in and names the signature** — `SetComponentFieldRaw(Entity, int typeId, int byteOffset, void* src, int size)` in `Fdp.Core`. ⭐⭐ **And it is now a FIX, not an improvement:** `StageMutation:530` takes a **whole component**, `DrainPendingMutations:548-575` writes it with `SetComponentRaw` **(no offset)** *after* the restore ⇒ **every other field of that component is reverted post-tick → pre-tick.** On the shared `Blackboard1024`, **editing one blueprint variable reverts a tick of BTree and HSM state.** ⚠ **The payload's exact origin is unverified — that is the red-first test** |
| ⛔ **correction to v1** | my `MaxComponentSize` argument was **already retracted** in the ANSWERS doc — the check is `>` and the blackboard is exactly 1024, so **it fits**. **The reason is sharing, not size** |

---

## 6. ✅ The three Track C decisions — ALL RULED `2026-08-15`. Kept for the reasoning.

| | the conflict |
|---|---|
| **① `C5` — write both copies, or stage?** | 🛑 **Three records rule AGAINST writing the live copy while paused.** `Slice2_Candidates.md:325-360`: the paused edit *"does **not** mutate `_liveRepo`"* — queued, restored-then-drained at the **N+1 boundary** — justified by **Flight Recorder linearity** and **`DataPolicy` divergence**; `BTree_Editor_..._Design.md:869` gates live edit behind a **"Make Editable" toggle + confirmation banner**; `Blackboard_Authoring_DD:1340` calls live-edit *"orthogonal to this DD"*, Slice 3. ⇒ **Rulings 12 (immediacy) and 16 (both copies) contradict this.** ⚠ **Honest caveats:** that file is titled *"Candidates"* (a proposal menu, not a dispatched design) and reasons about **ECS component** breakpoints, whereas `C5` targets **blackboard variables** |
| **② `C3` — the gesture** | ⛔ **No record of a three-dot or double-click "edit value" gesture**, the `⋮` menu is enumerated **exhaustively without one**, and **double-click is already bound to inline rename**. ⇒ **the requested gestures collide with shipped bindings** |
| **③ `C1`/`C7` — table or form?** | **D7 (authoritative) routes variables as a per-variable FORM with a `Default` row**; the plan says **one TABLE with a Value column**. ⇒ **which shape wins?** |

⭐ **Everything else is now ruled.** `D1` answered · `D2` likely **dissolved** by the existing
`BlackboardVariableRole` carrier · `D3` disposition still open but harmless.

---

## 7. ⭐ Order

**Track B now** *(Batch 65, dispatched)* → **`S5`** *(the dialog's Type picker needs ONE offerable list)* →
**the surgical field write** → **Track C** *(table → dialog → Watch → cross-host outline)* →
**`W7` re-derived from its design** → **Track D last** *(`W11` needs an architect call; `W12` a scope pass)*.
📌 Still filed, not fixed: **`BP-241`** · **`BP-242`** · the **`Fdp.Toolkits.Tests` race**.
