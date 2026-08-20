# HANDOFF — Batch 84: **the wiring defects, then row `59c`**

> ⚠⚠ **RE-DISPATCHED `2026-08-18` under rule 1a — this REPLACES the `451d76962` version.**
> 📐 **Checked both halves:** the dispatch sha is **not an ancestor** of your branch, **and the user
> confirmed no run had started.** ⭐ **Item 0 is NEW and comes FIRST**; the old items 1 and 2 are
> unchanged but renumbered **2** and **3**. ⭐ **Item 4 is new and droppable.**
>
> 📌 **Dispatched at `d1e8a0373`.** ⭐ **Branch from it** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ **Documents that change after it are FYI ONLY.**
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT. ⛔ Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids.** ⭐ **Rule 1b: push `chore: started batch 84 at <sha>` FIRST.**
>
> ⭐⭐ **Order is deliberate: `0 → 2 → 3 → 4`.** ⛔ **Item 0 is a PREREQUISITE for item 3** — you cannot
> demonstrate *"editing while paused works"* on a surface whose gestures never attach and whose run
> state is always wrong. ⚠ **If you land only items 0 and 2, that is a GOOD outcome.**

---

## 1. ⭐⭐⭐ Read this first — **the design's open question is ANSWERED, by measurement**

📌 **`Q32_…_ANSWERS.md` §2.2 said the write design could not be settled until one thing was measured**,
and marked it *"the coordinator has NOT run this."* ⭐ **I ran it on `2026-08-18`.**

### ⭐⭐ The mechanism, measured on `HEAD`

| step | code |
|---|---|
| **on hit** | `_postTickSnapshot.SyncFrom(_liveRepo)` then ⭐ **`_liveRepo.SyncFrom(_preTickSnapshot)`** *(rewind)* — `DataBreakpointManager:471-473` |
| **while paused** | ⭐ **`ActiveView` IS `_preTickSnapshot`** — `:123`. **That is the object the panels read and the dialog seeds from** |
| **on step / continue** | ⭐⭐⭐ **`_liveRepo.SyncFrom(_postTickSnapshot)` FIRST, `DrainPendingMutations(_liveRepo)` SECOND** — `:495 :498` · `:514 :517` |

### ⇒ ⛔⛔ **Ruling 15's suggestion is MEASURED FALSE — do not build it**

📌 **Ruling 15 mused:** *"writing DIRECTLY to that view is arguably the correct design… ruling 12's
immediacy falls out for free."*
⛔⛔ **No.** ⭐ **Resume restores `_liveRepo` from the POST-tick snapshot, not the pre-tick one** ⇒ an edit
written into `ActiveView` is **overwritten and silently lost the moment the sim continues.**
⚠ **That is precisely the silent-failure shape this programme keeps finding.**

⇒ ⭐⭐⭐ **THE ECB STAGING PATH IS REQUIRED, and the drain-after-restore ordering is exactly why it
works:** the staged write lands **on top of** the restored post-tick state. 📌 **`R-63`.**

### ⭐⭐ And the `Fdp.Core` half **already exists** — 📌 **`R-64`**

| ✅ ships | where |
|---|---|
| `SetComponentFieldRaw(Entity, int typeId, int byteOffset, void*, int size)` | `IEntityCommandBuffer.cs:57` · `EntityCommandBuffer.cs:256` · `EntityRepository.cs:1720` · playback `:437` |
| the drain already branches on it | `DataBreakpointManager.cs:625-633` — *"only the bytes the designer actually changed are addressed"* |
| the record already carries the offset | `PendingDebugMutation.cs:50` — `IsFieldWrite => ByteOffset >= 0` |
| the red-first test | `SurgicalFieldWriteTests.cs` |
| ⛔ **every production ECB implementer is already gated** | `EntityCommandBufferSurgicalWriteCoverageTests` |

⛔⛔ **DO NOT BUILD A SURGICAL WRITE. IT IS BUILT.** ⭐ **What is missing is a STAGING ENTRY POINT that
sets `ByteOffset`** — `IDataBreakpointManager` exposes **whole-component `StageMutation` only**
*(`:96`, `:116`)*. ⇒ ⭐⭐ **this batch is WIRING, like 82 and 83.**

### ⚠ One correction to carry — **I got this wrong twice, including in Batch 83's handoff**

⛔ **`"a whole-component blackboard write exceeds MaxComponentSize and cannot work"` is FALSE.**
📐 `EntityCommandBuffer:83` is `if (componentSize > MaxComponentSize) throw` and `Blackboard1024.ByteSize
== 1024` ⇒ **`1024 > 1024` is false. It fits, exactly.**
⭐⭐ **The true argument is stronger:** `Blackboard1024` is **ONE component SHARED by BTree, HSM and
Blueprint at disjoint offsets** ⇒ a whole-component write **clobbers other subsystems' state.**
📌 **`R-65`. Cite the sharing, never the size.**

---

## ⭐⭐⭐ ITEM 0 — **the two wiring defects the visual check found** *(NEW, `2026-08-18`)*

📌 **Source: the user's visual check against `GUIDE_Blueprint_Visual_Check.md`, `2026-08-18`.**
⭐ **Both are ONE-LINE causes with a rail problem behind them** — ⛔ **the rail is the real work.**

### 🔴🔴 0a — `facetEditService` is not passed to the Blueprint registrar *(`R-67`)*

```
EditorSubsystem.cs:2120   var facetEditService = new ComponentEditServiceBuilder().Build();
              :2134       _btreeRegistrar     = new …( facetEditService: facetEditService, …)   ✅
              :2158       _hsmRegistrar       = new …( facetEditService: facetEditService, …)   ✅
              :2162       _blueprintRegistrar = new …( …, schemaExporter: sharedSchemaExporter) ⛔ OMITTED
```

⇒ `PerspectiveWorkspaceRegistrar:281`'s `if (facetEditService != null)` is **false** ⇒ `EditGestures`
is **null** ⇒ ⛔ **"Edit value…" and "Properties…" do nothing.** *(The user's finding C/D.)*

📌 **This is `CLAUDE.md`'s SILENT-DEFAULT PATTERN verbatim:** *"a production caller that HAS a dependency
must PASS it."* ⚠ **Built 42 lines above the call that omits it, and passed to two of three siblings.**

### 🔴🔴 0b — `ActiveSession` means *"a document is open"*, not *"the sim is up"* *(`R-66`)*

```csharp
// EditorSubsystem.cs:2180-2186 — SyncActiveDebugSession()
session = _aiDocumentManager?.Active?.Kind switch { AssetKind.Blueprint => _blueprintDebugSession, _ => null };
debugRegistry.SetActiveSession(session);
```

⇒ `RunStateSource.Resolve` returns `Planning` **only when `ActiveSession is null`** ⇒ opening any
blueprint makes it **`Running`** ⇒ `ModeFor` ⇒ **`Current`** ⇒ every row the run has not written renders
**`(pending)`**, forever. ⛔⛔ **The INITIAL arm is unreachable in production.** *(The user's finding C.)*

⚠⚠ **`RunStateSource`'s own doc comment asserts the false premise** — *"a live session is what running
means to this editor."* ⭐ **Fix the premise, and fix the comment with it.**

> ⭐⭐⭐ **THIS IS ALSO A SAFETY PREREQUISITE FOR ITEM 3.** 📌 **Ruling 15: the write surface stays
> DISABLED unless paused or stepping.** ⛔ **That gate reads the same run state.** ⇒ **shipping item 3
> on top of `0b` would permit a runtime write whenever a document is open.** ⚠ **Do item 0 first.**

### 🛠 What to build

1. ⭐ **Pass it.** ⛔ **But do NOT stop there** — 📌 **`R-67`: this is the FOURTH time** *(Batch 80's
   `hostKind`, 82 named it, 83 shipped this one)*.
2. ⭐⭐ **Give run state a signal that means what it says.** ⭐ **`IEngineDebugTimeController` and the
   breakpoint manager already know whether the sim is up** — ⛔ **do not coin a third notion**, and
   ⛔ **do not "fix" it by making `SetActiveSession` conditional**: other consumers rely on
   *"which document's session is active"*, which is a **different, legitimate** question.
   ⇒ ⭐ **`RunStateSource` needs an *is-the-sim-up* input, not a different session registry.**
3. ⭐⭐⭐ **THE RAIL IS THE DELIVERABLE.**
   ⛔⛔ **A rail that constructs its own registrar CANNOT see either defect** — that is exactly why
   Batch 83 shipped green. ⭐ **Two acceptable shapes, in preference order:**
   | ⭐ | shape |
   |---|---|
   | ⭐⭐ **preferred** | **ONE construction site instead of three** — a helper that builds all three perspectives' registrars from one shared-service bundle ⇒ **divergence becomes impossible by construction**, and it is ruling 9's move *(one implementation, not three call sites that must agree)* |
   | ⭐ **fallback** | **a forwarding rail PER DEPENDENCY asserted on the PRODUCTION-CONSTRUCTED object** — 📌 `CLAUDE.md`: *"asserted on the CONSTRUCTED object, not on the registrar's source"* |
   ⚠ **If neither is reachable without a large refactor — STOP AND REPORT with the measurement.**
   ⛔ **Do not ship a one-line fix with a test that would have passed before it.**

---

## 2. 🔴 ITEM 2 — **the staging entry point + the `+8` owned once**

### ⭐ Design basis
📌 **Ruling 14** *(user)*: *"the command buffer might need a special 'change concrete variable in a
concrete blackboard component' … it can not be full component overwrite only, but **chirurgical
change**."*
📌 **`Q32` §2.1 sizing note, verbatim:**
> ⭐ *"the read path uses `8 + OffsetBytes` — there is an **8-byte header** before the fields.*
> ⛔ ***Whoever computes the offset must own that `+8` in exactly one place, not two.***"
> 🔴 *"an out-of-range offset/size is **MEMORY CORRUPTION**, not a wrong value. **Bounds-check against
> the registered component size and fail LOUDLY**."*

### 🛠 Build

1. ⭐ **A field-write staging method on `IDataBreakpointManager`** — the shape the design already sized:
   `StageFieldMutation(Entity, int componentTypeId, int byteOffset, ReadOnlySpan<byte>)`.
   ⛔ **Additive.** ⛔ **Do not change `StageMutation`'s existing whole-component behaviour** — it has a
   production caller *(`ComponentEditWindow:108`)*.
2. 🔴🔴 **Bounds-check and fail LOUDLY** — ⛔ **not `Debug.Assert`, not a silent clamp.** ⭐ **A rail must
   prove a bad offset THROWS**, not that it *"does nothing."*
3. ⭐⭐ **The `+8` in ONE place.** 📐 **Measure who owns it today** — the read path is
   `BlueprintDebugSession:1308-1312` *(`int start = 8 + field.OffsetBytes`)*. ⭐ **The write must reuse
   that computation, not restate it.** ⛔ **If the only way to share it is to duplicate the constant,
   STOP AND REPORT** — 📌 the design names this as the thing to get right.
4. ⭐ **Composition rail:** *"N queued field writes to one component must all land, in order"* — ⛔ **that
   is the property a whole-component write destroys.** ⭐ **Assert it with N ≥ 2 to two different fields.**

### ⭐⭐ The acceptance property — **this is the one that matters**

📌 **`SurgicalFieldWriteTests` already states it:** a field the **simulation** wrote during the paused
tick must **survive** the drain, while the field the **designer** edited lands.
⇒ ⭐ **On `Blackboard1024` that is BTree and HSM state surviving a Blueprint edit.**

---

## 3. 🔴 ITEM 3 — **the editor path: Details and Watch can write while paused**

### ⭐ Design basis

| ruling | |
|---|---|
| **15** *(user)* | ⭐⭐⭐ *"the change of runtime var makes sense **ONLY if sim is paused on breakpoint or deterministic time step**. at that time nothing else changes the blackboard"* ⇒ ⛔ **the write surface stays DISABLED while free-running** |
| **7** *(narrowed by 15)* | *"running ⇒ writes the live blackboard"* — ⭐ **only in the paused/stepping sense above** |
| **11** | ⭐ *"the runtime value change is the same mechanism the Watch panel should provide — **SHARE it**"* |
| **12** | 🔴🔴 *"it must work when the sim is FROZEN on a breakpoint or in deterministic stepping mode, and the change must appear **IMMEDIATELY in both the Details and Watch panels** — ⛔ not on the next step or on resume"* |

### 🛠 Build

1. ⭐ **`IBlueprintDebugSession` gains the write** — 📐 **measured: it has none** *(`SetBreakpoint`,
   `AddWatch`, `SetEntityFilter`, `GetActiveEntities` only)*. ⭐ **Route it to item 1's staging method.**
2. ⭐⭐ **`VariableEditCommit` stops refusing — but ONLY when paused or stepping.**
   📌 **Batch 83 built the refusal deliberately** and asked the same `VariableValue.ModeFor` the Value
   column asks. ⭐⭐ **Keep that single source of truth** — ⛔ **do not add a second notion of "may I
   write?"** ⛔ **Free-running still REFUSES** *(ruling 15)*.
3. ⭐ **The Watch panel writes through the SAME path** *(ruling 11)*. ⛔ **Batch 83 already made both
   panels share one dialog and one formatter** — ⭐ **there should be nothing left to share.**
4. ⭐ **The freeze signal already exists** — `IEngineDebugTimeController.IsPausedByDebugger`, mapped by
   `MasterSyncTimeControllerAdapter:29` and `CgfSubsystem:830`. ⛔ **Do not coin a second one.**

### ⭐⭐⭐ Ruling 12's gate — **`R-55`: it was never carried into any acceptance list. Carry it now.**

> 📌 **Verbatim:** *"with the sim frozen on a breakpoint, a value change is visible in **BOTH panels
> within one frame**."*

⭐ **Assert it, do not assume it.** ⚠ **The mechanism that makes it true is worth stating in your report:**
📌 *"a frozen sim still ticks — behaviours do not tick and `dt == 0`, but the host loop and command-buffer
playback keep running"* ⇒ the staged write plays back on the next tick, **i.e. within a frame.**
⛔ **The special-case flush is WITHDRAWN** — it would be a second write path *(ruling 9)*.

---

## 4. 🟡 ITEM 4 — **the two routing defects** *(NEW, `2026-08-18`)* — ⭐ **DROPPABLE**

> ⭐⭐ **Take this ONLY if items 0, 2 and 3 are landed and gated.** ⛔ **Do not start it otherwise** —
> ⭐ **items 0 and 2 are worth more than a complete batch that is red.**

### 🔴 4a — **no row is highlighted** *(the user's finding `B2`; my guide's `B3`)*

📌 **Design basis, `DESIGN_Variable_Details_And_Editing.md` §1, verbatim:**
> ⭐ *"Clicking any row in *Local Variables* routes Details to the locals-of-this-graph table **with
> that row highlighted**"* ⇒ ⭐⭐ *"the routing key is `(asset, section)` **+ a highlight**."*

📐 **Root cause — the TYPE cannot express it:**
```csharp
public readonly record struct VariableOutlineSelection(string? Heading, IVariableRowSource? Source)
```
⛔ **No clicked-row identity.** ⚠ **`HighlightOf` exists but is a DIFFERENT concept** — the per-tick
CHANGE highlight *(red/yellow)*, keyed by `VariableChangeMonitor`.
⇒ ⛔⛔ **Do NOT overload the change highlight for selection** — 📌 §1b makes a collapsed header inherit
**red if any child changed this tick, yellow if any is pending**; a selection colour mixed into that
makes the monitor lie. ⭐ **Selection is a separate visual state.**

### 🔴 4b — **Details does not follow the graph** *(the user's finding `B4`; my guide's `B6`)*

📐 **Root cause — the selection is a SNAPSHOT.** `BlueprintMyBlueprintWindow:336-346` resolves the local
source **once, at click time**, and publishes the resolved object. ⛔ **Nothing re-publishes when the
canvas changes.** ⭐ **The OUTLINE follows correctly** *(the user confirms)* because it re-reads
`currentGraphId` every frame — ⚠ **the details host was handed a frozen source.**

⭐⭐ **The fix is to make the selection LIVE, not to add a second event.** 📌 Batch 82 built the routing
*"over the interfaces so a BTree/HSM details host wires itself"* — ⛔ **a canvas-change subscription in
`BlueprintDetailsWindow` would be blueprint-specific and would not survive row 61.**
⚖️ **Lean: the graph-scoped arm publishes a source that resolves the graph at READ time** *(the same
delegate shape the outline already uses)*, ⛔ **not a stored `Guid`.**

⚠ **If that turns out to require re-shaping `IVariableRowSource` — STOP AND REPORT.** ⭐ **`4a` alone is
still worth landing.**

---

## 5. ⛔ OUT OF SCOPE

| ⛔ not here | owner |
|---|---|
| **writing while FREE-RUNNING** | ⛔⛔ **ruling 15 forbids it.** Not a later batch — **a decision** |
| **retiring any Variables window** | **`60` = `U-16`** — ⚠ `R-60`: BTree/HSM have no Details window |
| **the shared cross-host outline** · **a BTree/HSM Details host** | **`61`** · **`BP-317`** |
| **stage `D1`–`D4`** | ⛔ own batch. 🔴🔴 **`R-24`: `D2` must preserve field order or every deployed blackboard is wiped** |
| 🟡 **the struct notation split** *(`{"X":1.0}` initial vs `{X=1.0, …}` current)* | ⭐ **take it ONLY if everything else lands and it stays cosmetic** — a formatter change in `VariableValueFormatter.InitialText`. ⛔ **Drop it if it grows** |
| ⛔⛔ **a way to PIN A VARIABLE** | 📌 **`R-68`: it does not exist and was never specified.** The only entry point is `ToggleWatch(PinId)` on a **canvas node pin**. ⭐⭐ **This needs a RULING before it needs a batch** — ⛔ **do not invent a gesture** |
| ⛔ **merging the `Variables` / `Working State` sections** | 📌 **`R-61`: stage `D`.** ⚠ The user saw both sections and both `[+]` dialogs — ⭐ **that is `R-17` WORKING and stage `D` not yet run**, ⛔ **not a defect** |

---

## 6. ⭐ Gates — **the rule 8 contract, all seven rows, PER ITEM**

| # | report |
|---|---|
| **1** | verbatim command · pass/fail/skip · **Δ vs baseline** |
| **2** | ⭐⭐ **the `--no-build` column.** ⛔ **`NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` take NO `--no-build`** |
| **3** | ⭐⭐⭐ **golden movement as a DIFF SHAPE** |
| **4** | ⭐ **every RED confirmed pre-existing against the base sha**, named |
| **5** | ⭐ **working tree CLEAN after every suite run** |
| **6** | ⭐ **both quarantine counts** — ⛔ **a new skip is a finding** |
| **7** | ⭐ **`tracker-counts.py --check`** · ⭐ **`rulings-check.py`** · **every id you allocated** |

⚠⚠ **THIS BATCH TOUCHES `Fdp.Core` AND THE DEBUGGER** ⇒ ⭐⭐ **`Fdp.Toolkits.Tests`,
`Hrot.Diagnostics.Breakpoints.Tests` and the ClusterRunner integration tests are NOT background noise
this time.** ⛔ **`DEBT-AIB-030` is the excuse for `Fdp.Toolkits.Tests` ONLY when the diff cannot reach
it — and this diff CAN.** ⭐ **Name the failing test and run `--filter` in isolation before calling any
red pre-existing.**

⭐ **Baseline** *(Batch 83)*: build **0/69** · AiShared **1369** · Blueprints **3737/3747/10** ·
BTree.Editor **615** · Hsm.Editor **551** · Generators **270** · Breakpoints **134** · Persistence
**136** · Hrot.Editor **194** · Scenarios **56/68 (12 skipped)** · UrbanCombat **29** · Toolkits
**1964** · NodeEditor.Core **211** · NodeEditor.UI **135** · FastHSM **300** · tracker **open 65 /
done 191** · rulings **43/43**.

⭐⭐ **`StructureHash` must not move.** ⛔ **Nothing here is compiler-side.** 📌 If a golden or
`persistence-shape.txt` moves, **that is a STOP.**

---

## 6a. ⭐⭐⭐ ONE EXTRA GATE, FOR ITEM 0 ONLY — **the anti-vacuity check**

⛔⛔ **Batch 83's dialog rails were GREEN while the production dialog did nothing**, because every rail
built its own registrar and passed `facetEditService` itself. ⚠ **That is `R-67`, and it is the FOURTH
time.**

⇒ ⭐⭐ **For item 0, report the REVERT PROBE EXPLICITLY, per defect:**

| probe | must |
|---|---|
| un-pass `facetEditService` at the Blueprint call site | ⭐ **redden** |
| make `ActiveSession` non-null with the sim down | ⭐ **redden** |

⛔ **If either probe leaves the suite green, the rail is vacuous and item 0 is NOT done** — ⭐ **say so
and stop**, exactly as Batch 83 did when it caught its own hex-decoder rail.

---

## 7. ⭐ FYI — **the user is visually checking Blueprint against this**

📄 **[`GUIDE_Blueprint_Visual_Check.md`](GUIDE_Blueprint_Visual_Check.md)** ships with this batch.
⭐ **Its part `F2` records that editing while paused REFUSES, and names Batch 84 as the owner** ⇒ ⭐⭐ **your
item 2 is what turns that row from an expected refusal into a working feature.**
⚠ **`F3` demands every refusal be GREYED WITH A TOOLTIP, not a click that dead-ends** — ⭐ **that applies
to the free-running refusal you are KEEPING.**
