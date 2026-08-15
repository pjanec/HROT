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

### ⛔⛔ Ruling 12 — **the "conflict" was the coordinator's error. WITHDRAWN.**

> ⚠ **The first draft of this section claimed:** *"a frozen sim runs no ticks, so a queued write would
> only appear on resume; therefore when paused, flush the command buffer on the spot."*
> ⛔⛔ **Both halves were wrong, and the user corrected the premise:** *"breakpoint or step frozen sim
> does not mean nothing is ticking — behaviors should not tick and dt==0 so no physics applies."*

| the claim | the truth |
|---|---|
| ⛔ *"a frozen sim runs no ticks"* | ⭐⭐ **It ticks.** Freezing means **behaviours do not tick** and **`dt == 0`** so nothing integrates. **The host loop, its systems and command-buffer playback keep running** |
| ⛔ *"`FlushCommandBuffers()` is the existing playback point"* | 🔴 **`EntityRepository.View:43` has NO CALLERS AT ALL.** ⭐ **Playback is `ecb.Playback(world)`**, called from the systems and host loop. ⚠ **The coordinator named a dead API as "existing"** — the *"verify the consumer, not just the definition"* rule, broken again |

⇒ ⭐⭐ **There is no conflict, and the design gets SIMPLER:** the write is queued to the command buffer
**always**, and because ticks continue while frozen, it plays back **on the next tick — i.e. within a
frame** ⇒ **ruling 12 is satisfied by the plain path.** ⛔ **The special-case flush is withdrawn: it
would have been a SECOND write path, which is ruling 9's prohibition.**

| | |
|---|---|
| ⭐ **The write primitive already exists** | **`IEntityCommandBuffer.SetComponentRaw(Entity, int typeId, void* ptr, int size)`** — raw bytes into a component. ⭐ **And the interface already knows blackboards are components:** `AddEmptyComponent` is documented as *"bypasses the 1024-byte ECB payload limit for large components like blackboards"* |
| ⭐ **The freeze signal** | `IEngineDebugTimeController.IsPausedByDebugger` — `MasterSyncTimeControllerAdapter:29` maps it to `TimeMode.Deterministic`; `CgfSubsystem:830` to `_bpManager.IsPaused` |
| 📐 **Make it a GATE, not an assumption** | ⛔ **Do not assume "next tick" is fast enough — MEASURE it.** ⭐ **Gate: with the sim frozen on a breakpoint, a value change is visible in BOTH panels within one frame.** ⚠ **If it is not, that is a finding, and the fix is in the loop — not a second write path** |

### 🔴 Ruling 13 — **the Watch panel must EDIT, and must show nothing before the run** *(user, `2026-08-15`)*

> ⭐⭐ *"watch panel MUST allow for value changes (and show nothing when exercise not running yet) —
> add to plan if this is not the case now."*

⛔ **It is not the case now. Both halves go in the plan.**

| | today | required |
|---|---|---|
| **editing** | ⛔ **read-only.** `WatchPanelWindow` exposes `LastRenderedWatches` and draws; ⭐ **`IBlueprintDebugSession` has no write at all** | ⭐ **the same edit path as the Details panel** — same dialog, same command buffer, same `SetComponentRaw` |
| **refresh** | 🔴🔴 **`WatchPanelWindow.cs:26` — `HandlePinValueChanged(PinValueChanged evt) { /* refresh row data */ }`** — ⛔ **a subscribed handler with an EMPTY BODY and a comment describing what it would do.** ⭐ **Trap #5, sitting exactly on ruling 12's path** | ⭐ **real** — ruling 12's immediacy runs through it |
| **before the run** | 📐 **unmeasured** | ⭐⭐ **shows NOTHING** |

### ⭐⭐ The asymmetry, stated so nobody "unifies" it by mistake

⛔ **The two panels behave DIFFERENTLY when the exercise is not running, and both are correct:**

| | not running | running / paused |
|---|---|---|
| **Details** | ⭐ **the INITIAL value**, editable (ruling 3) | the current value |
| **Watch** | ⭐⭐ **NOTHING** (ruling 13) | the current value, editable |

📌 **Why they differ:** ⭐ **Details is an AUTHORING surface that also shows runtime; Watch is a
RUNTIME surface only.** ⚠ **A watch on a value that does not exist yet has nothing to show, and
showing the JSON default there would be inventing a "current" value for an entity that has not been
spawned.** ⛔ **Do not "fix" this into consistency — ruling 9 forbids two implementations of one
concept, not two behaviours of two different concepts.**

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
| **59** | the StructEdit dialog — ⭐ **three-dot button AND double-click** (rulings 5, 10) + the **not-running** write (ruling 7, half) | needs 58's column |
| ⭐ **59b** | 🔴 **the Watch panel: make `HandlePinValueChanged` real · EDITING through the same dialog · show NOTHING before the run** (rulings 11, 13) | ⛔ **ruling 12's immediacy runs through this handler** |
| **59c** | the **running** write — `SetComponentRaw` via the command buffer, ⭐ **gated on "visible in BOTH panels within one frame while frozen"** (rulings 2a, 12) | ✅ **unblocked — no open questions left** |
| **60** | `U-16` — retire `BlueprintVariablesWindow` (ruling 9) | ⛔ **only after Details is proven**, or there is no editing surface at all |


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
