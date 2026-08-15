# Architect Question #32 — **ANSWERS**: the variable Details panel

> ⭐⭐ **User ruling, `2026-08-14`, verbatim in substance.** ⛔ **This supersedes every lean in the
> question document, including the two the coordinator argued for.**
>
> ⛔⛔ **AND A SEQUENCING RULING: NO VISUAL CHECKS** until the Details panel is implemented **and** the
> emitters and all access infrastructure are unified.
> ⇒ 📌 **[VISUAL_CHECK_Guide.md](VISUAL_CHECK_Guide.md) is SUSPENDED**, not cancelled. `BP-243` — the
> one defect its first run found — stands as the argument for running it again later, on a surface
> that is finished.

---

## 0. The ruling, as a spec

| # | ruled | was |
|---|---|---|
| **1** | **Details hosts the list of vars**, as designed | `U-6`, unchanged |
| **2** | ⭐ **Selection routes:** click a **global** in My Blueprint ⇒ the list of **globals / working state**. Click a **local** ⇒ the locals of the **currently selected graph** | new, and it is the panel's whole navigation model |
| **3** | ⭐⭐ **ONE Value column, meaning switched by run state** — **initial** when not running, **current** when running or paused, across **live / replay / preview** | ⛔ **`Q32-A1`. The coordinator argued `A2` (two columns) and is overruled** |
| **4** | **Value is READ-ONLY in the cell.** Tooltip shows it **full size and pretty-printed** (structs) | new |
| **5** | ⭐ **A three-dot button** right of the value opens a **StructEdit-based editing window**, **OK / Cancel**, initialised to the variable's current value | `Q32-B3`, promoted from *"vectors only"* to **everything** |
| **6** | ⭐⭐ **The same Details panel is REUSED for every asset type** — HSM, BTree, Blueprint | ⇒ **this is a cross-host deliverable, not a blueprint one** |
| **7** | ⭐ **Write target follows run state:** running ⇒ writes the **live blackboard**; not running ⇒ writes the **initial value in JSON** | new |
| **8** | ⭐⭐ **Unify the emitters** — *"as the global vars and working state vars are the same stuff, it makes no sense to emit them differently"* | ⇒ **`Q32-E`, decided: unify** |
| **9** | ⛔⛔ **"I need a clean solution, no keeping two implementations for the same concept"** | ⇒ **the standing constraint over all of it.** `U-16` is not optional cleanup; it is the acceptance criterion |

---

## 1. ⭐⭐ What this costs — measured, and it is mostly reuse

| piece | state |
|---|---|
| the table + columns | ✅ **exists** — `VariablesPanelControl`, already `Name · Type · Bytes · Value · Role · Scope` |
| initial-value **storage** | ✅ **exists** — `DefaultValueJson`, and ⭐ **already honoured for BOTH kinds** (`AiPrimitiveEmitter:133` / `InstanceEmitter:183`) |
| initial-value **setter** | ✅ `IBlackboardManagedAsset.UpdateVariableDefaultValueJson` — **HSM and BTree implement it**; ⛔ **blueprint does not** |
| live-value **read** | ✅ `ILiveValueProvider.GetLiveVariableValues` drives the column; `MarshalFromBytes` formats. ⛔ **blueprints never supply it** |
| StructEdit editor | ✅ **exists** — `IEditSession` / `EditDocument` / `IComponentEditService`, reflection-driven |
| 🔴🔴 **live-value WRITE** | ⛔⛔ **NO SEAM EXISTS.** See §2 — **this is the only genuinely new mechanism in the ruling** |

---

## 2. ✅ The live write — **all three sub-questions RULED** *(user, `2026-08-14`)*

**`IBlueprintDebugSession` has no write.** Its surface is Attach/Detach · breakpoints · **watches** ·
entity filter · Continue/StepOver/StepInto. ⚠ **`Watch.WriteValue<T>` is the RUNTIME writing into the
watch buffer — not the editor writing into the entity.** ⇒ **ruling 7's running half must be built**,
and now every constraint on it is settled:

| | ⭐ **RULED** |
|---|---|
| **2a — when?** | ⭐⭐ **Queue changes via FDP command buffers.** ✅ **The seam exists:** `IEntityCommandBuffer` / `EntityCommandBuffer` in `Fdp.Core`, with `EntityRepository.SetCommandBufferOverride` and `FlushCommandBuffers` already the playback point. ⇒ ⛔ **no bespoke queue, no mid-tick write** |
| **2b — replay?** | ⭐⭐ **NO changing value during replay.** ⇒ **show it, refuse the write.** ✅ **Coordinator's lean confirmed** — a write during replay diverges the run from the recording it is replaying |
| **2c — cluster?** | ⭐⭐ **The concern does not exist. The brain and blackboard live on a SINGLE CGF node (and the editor), and are NEVER replicated in distributed mode.** ⇒ ⛔ **there is no authoritative-copy problem to solve**, and the coordinator's *"changes locally and nowhere else"* worry was **misinformed about the architecture** |

⇒ ⭐ **Nothing in the ruling is blocked any more.**

## 2.1 ⭐⭐ Rulings 10-12 *(user, `2026-08-15`)* — reuse, share, and **immediacy**

| # | ruled |
|---|---|
| **10** | ⭐ **Reuse the existing StructEdit generic value-editing dialog** — *"used by entity component inspector at least"* |
| **11** | ⭐⭐ **The runtime value change is the same mechanism the Watch panel should provide — SHARE it** |
| **12** | 🔴🔴 **It must work when the sim is FROZEN on a breakpoint or in deterministic stepping mode, and the change must appear IMMEDIATELY in both the Details and Watch panels — ⛔ not on the next step or on resume** |

### ⭐ Ruling 10 — what exists, and ⚠ there appear to be TWO of them

| | |
|---|---|
| ✅ **the FDP-level service** | **`IComponentEditService`** (StructEdit.Core), driven by **`Fdp.Toolkits/Diagnostics/Gizmos/UI/StructInspectorProjector.cs`** — ⭐ **this looks like the entity component inspector the user means**, and it is also what the ReplayBrowser's predicate/event compilers use |
| ⚠ **a blueprint-local one** | **`Hrot.Blueprints.Editor/Inspector/`** — `IStructEditDrawer<T>` · `DrawerRegistry` · `PrimitiveDrawers`, consumed by `InspectorWindow` and `BlueprintDetailsWindow`. ⭐ **Already an EDITING interface** — `bool Draw(string label, ref T value, DrawContext ctx)`, *"returns true if the value was modified"* |
| 📐 **The question, to MEASURE not assume** | ⛔ **Are these two implementations of one concept (ruling 9's target), or two different jobs that look alike?** ⚖️ **Coordinator lean: build the dialog on the FDP-level `IComponentEditService`** — it is the one already shared beyond blueprints, and ruling 6 wants one dialog for three hosts. ⚠ **But the coordinator has NOT proved the blueprint-local registry is redundant, and must not claim it is** |

### 🔴🔴 Ruling 12 vs ruling 2a — **a genuine conflict, and its resolution**

⛔⛔ **These two rulings pull against each other and it must be said plainly:**

> **2a** says *queue the write via FDP command buffers.* **12** says *the change must be visible
> immediately while the sim is FROZEN.* ⇒ ⛔ **A queued write does not flush until a tick runs, and a
> frozen sim runs no ticks. Taken naively, the value would appear only on resume — exactly what
> ruling 12 forbids.**

⚖️ **Coordinator's resolution, and it keeps ONE write path:**

| | |
|---|---|
| ⭐⭐ **When `IsPausedByDebugger`** | **submit to the command buffer, then FLUSH IT ON THE SPOT.** ✅ **`EntityRepository.FlushCommandBuffers()` is public and is the existing playback point**, and ⭐ **a frozen sim has no tick in flight, so the race 2a guards against cannot occur** |
| ⭐ **When running** | submit and let the normal flush happen — **the same call, the same buffer** |
| ⛔ **Rejected: a pending-write overlay in the read path** | it would show the new value without applying it ⇒ **a second source of truth for "what is the value"** — ⛔ **ruling 9's exact prohibition** |

⭐ **So: one write path, and freezing changes only WHEN the flush happens, never WHETHER it goes
through the buffer.** ✅ **The freeze signal exists — `IEngineDebugTimeController.IsPausedByDebugger`,
with `RequestPause`/`RequestResume`/`RequestStepOneTick`.**

### 🔴 Ruling 11/12 — the Watch panel handler that would refresh it is **EMPTY**

```csharp
// WatchPanelWindow.cs:26
private void HandlePinValueChanged(PinValueChanged evt) { /* refresh row data */ }
```

⛔⛔ **A subscribed handler with an empty body and a comment describing what it would do.** ⭐ **Trap #5,
and it sits precisely on ruling 12's path** — *"the change must be shown immediately in the detail and
watch panel."* 📐 **The Watch panel cannot honour ruling 12 until this is real**, and ⭐ **the user's
own words — *"similar to what watch panel SHOULD be providing"* — suggest they already suspect it.**

### ➕ Ruling 5 extended *(same message)*

⭐ **Double-clicking the value cell opens the edit window too** — the three-dot button is an
*affordance*, not the only route. ⚠ **`BP-207`'s lesson applies: the gesture is fine, but it must be
DISCOVERABLE** — the three-dot button is what makes the double-click findable, which is why both exist.

---

## 3. ⭐ The emitter unification is SAFE, and here is the measurement

**Ruling 8 is the deepest change, and it is layout-neutral for every shipped asset.**

```
declaration-kind combinations across ALL shipped assets (458 files):
   193  (Variable)                 ← Instance
    32  (Parameter, WorkingState)  ← AiPrimitive
     7  (Parameter)
     5  (WorkingState)
   221  (no declarations)
   ⭐   0  with BOTH Variable and WorkingState
```

⇒ ⭐⭐ **`Variable ∪ WorkingState` equals the single populated list, in the same order, for all 58.**
⛔ **So `StructureHash` must be byte-identical and the golden corpus must not move** — that is the
gate, and it is a real one precisely because the union is a no-op **today** and will not be tomorrow.

⚠ **Keep the struct NAMES per dispatch kind** (`State` for Instance, `WorkingState`/`Params` for
AiPrimitive) — those are ABI, and renaming them is a separate, larger change nobody asked for.
⭐ **Unify what the emitters WALK, not what they are CALLED.**

---

## 4. Sequencing

| batch | what | why here |
|---|---|---|
| ⏭ **56** | ⭐⭐ **the emitter + access-path unification** (ruling 8) | ⛔ **compiler-side, fully headless, gated on `StructureHash`.** ⭐ **The user made it a precondition for the visual check, and it blocks none of the UI** |
| **57** | `U-6` — Details hosts the **shared** control + ruling 2's selection routing | ⛔ **the shared control, never a blueprint copy** (ruling 9) |
| **58** | the Value column: mode switch, read-only, pretty-printed tooltip (rulings 3-4) + blueprint's `ILiveValueProvider` and `UpdateVariableDefaultValueJson` | needs 57's host |
| **59** | the three-dot StructEdit dialog (ruling 5) + the **not-running** write (ruling 7, half) | needs 58's column |
| **60** | `U-16` — retire `BlueprintVariablesWindow` (ruling 9) | ⛔ **only after Details is proven**, or there is no editing surface at all |
| **?** | the **running** write (ruling 7, other half) | ⛔ **blocked on §2a-2c** |

---

## 5. ⭐⭐⭐ Ownership — **RULED: ONE session builds it, for ALL hosts**

> ⭐⭐ **User ruling, `2026-08-14`:** *"cross host it is. one single implem session (the one we are
> using) will be implementing for all hosts, no other session will implement until this is all done."*

⛔⛔ **The coordinator's proposed split is OVERRULED and is recorded here only so nobody re-proposes it.**

| | |
|---|---|
| ⭐ **`claude/hrot-implementation-j1jvin`** | **builds ALL of it, for HSM, BTree and Blueprint** — including `Hrot.Editor.AiShared` |
| ⛔ **every other session** | **does not implement until this is done.** ⚠ **Design and questions are fine; code is not** |
| ⭐ **Why this is the right call, not just a call** | ruling 9 is *"no keeping two implementations for the same concept."* ⛔ **Two sessions building one shared panel is the surest way to produce exactly two implementations** — the constraint would be violated by the process before a line of code disagreed |

⚠ **Recorded in `.claude/CLAUDE.md`** — that file is the only memory shared between sessions, and this
freeze binds sessions that will never read *this* document.

### ⚠ Consequence for the dispatched Batch 56

📌 **[HANDOFF_Batch56](HANDOFF_Batch56_Emitter_Unification.md) §5 says `Hrot.Editor.AiShared` is
*"the CROSS-HOST session's territory — do not touch it."* ⛔ **That RATIONALE is superseded by this
ruling.** ⭐ **Its SCOPE stands unchanged:** Batch 56 is the emitter unification alone, and it has no
business in `AiShared` either way. ⛔ **The handoff is NOT amended** — rule 1 — and this note is the
correction.
