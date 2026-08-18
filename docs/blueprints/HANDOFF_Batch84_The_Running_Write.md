# HANDOFF — Batch 84: **row `59c` — the running write**

> 📌 **Dispatched at `451d76962`.** ⭐ **Branch from it** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ **Documents that change after it are FYI ONLY.**
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT. ⛔ Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids.** ⭐ **Rule 1b: push `chore: started batch 84 at <sha>` FIRST.**
>
> ⭐⭐ **Shorter than 83 on purpose.** ⛔ **This one touches `Fdp.Core` and the debugger.** ⚠ **If you
> can only land item 1, that is a GOOD outcome** — item 1 is the safety property, item 2 is the feature.

---

## 0. ⭐⭐⭐ Read this first — **the design's open question is ANSWERED, by measurement**

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

## 1. 🔴 ITEM 1 — **the staging entry point + the `+8` owned once**

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

## 2. 🔴 ITEM 2 — **the editor path: Details and Watch can write while paused**

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

## 3. ⛔ OUT OF SCOPE

| ⛔ not here | owner |
|---|---|
| **writing while FREE-RUNNING** | ⛔⛔ **ruling 15 forbids it.** Not a later batch — **a decision** |
| **retiring any Variables window** | **`60` = `U-16`** — ⚠ `R-60`: BTree/HSM have no Details window |
| **the shared cross-host outline** · **a BTree/HSM Details host** | **`61`** · **`BP-317`** |
| **stage `D1`–`D4`** | ⛔ own batch. 🔴🔴 **`R-24`: `D2` must preserve field order or every deployed blackboard is wiped** |
| 🟡 **the struct notation split** *(`{"X":1.0}` initial vs `{X=1.0, …}` current)* | ⭐ **take it ONLY if item 2 lands early and it stays cosmetic** — it is a formatter change in `VariableValueFormatter.InitialText`. ⛔ **Drop it if it grows** |

---

## 4. ⭐ Gates — **the rule 8 contract, all seven rows, PER ITEM**

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

## 5. ⭐ FYI — **the user is visually checking Blueprint against this**

📄 **[`GUIDE_Blueprint_Visual_Check.md`](GUIDE_Blueprint_Visual_Check.md)** ships with this batch.
⭐ **Its part `F2` records that editing while paused REFUSES, and names Batch 84 as the owner** ⇒ ⭐⭐ **your
item 2 is what turns that row from an expected refusal into a working feature.**
⚠ **`F3` demands every refusal be GREYED WITH A TOOLTIP, not a click that dead-ends** — ⭐ **that applies
to the free-running refusal you are KEEPING.**
