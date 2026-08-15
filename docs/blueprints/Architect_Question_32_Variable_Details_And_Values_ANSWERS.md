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

## 2. 🔴🔴 The one gap: writing into a running blackboard

**`IBlueprintDebugSession` has no write.** Its surface is Attach/Detach · breakpoints · **watches** ·
entity filter · Continue/StepOver/StepInto. ⚠ **`Watch.WriteValue<T>` is the RUNTIME writing into the
watch buffer — not the editor writing into the entity.** ⇒ **ruling 7's running half must be built.**

📐 **Three sub-questions the coordinator will not answer by guessing:**

| | |
|---|---|
| **2a — when?** | a blackboard write mid-tick races the sim. ⚖️ **Lean: queue it and apply at a tick boundary**, the shape `ecb`/command-buffer already uses |
| **2b — replay?** | ⛔ **A write during REPLAY breaks determinism** — the run diverges from the recording it is replaying. ⚖️ **Lean: SHOW the value in replay (ruling 3 says so) but REFUSE the write, naming the reason.** ⚠ **Ruling 7 says "running"; replay is a different thing wearing the same button** |
| **2c — cluster?** | ⚠ **This is a distributed sim** — SimHost / IG / ClusterRunner. ⛔ **Does an editor-side write replicate to the authoritative node, or silently change one node's copy?** 📌 **A value that changes locally and nowhere else is worse than a refusal** |

⭐ **Everything else in the ruling can be built while 2a–2c are open** — the read path, the column, the
tooltip, the StructEdit dialog and the **not-running** write are all unblocked.

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

## 5. ⚠⚠ Ownership — this is now a CROSS-HOST programme

⭐ **Ruling 6 makes it explicit: one Details panel for HSM, BTree and Blueprint.**

⛔ **`VariablesPanelControl`, `IBlackboardManagedAsset` and `ILiveValueProvider` all live in
`Hrot.Editor.AiShared` — that is `claude/cross-host-variable-model-3k8cfh`'s territory**, and their
`E-A` was explicitly scoped to BTree/HSM with the blueprint mapping left **open**.

📐 **Proposed split, for the user to confirm:**

| | |
|---|---|
| **this session (blueprints)** | ⭐ **Batch 56's emitter unification** — pure compiler, no shared surface, nobody's toes |
| **the cross-host session** | ⭐ **the shared panel, the column, the StructEdit dialog and both write paths** — ⛔ **they own the interfaces all three hosts implement** |
| ⚠ **must not happen** | two sessions editing `Hrot.Editor.AiShared` in the same window — ⛔ **that is ruling 9's failure mode arriving through the process rather than the code** |
