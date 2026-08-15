# HANDOFF — Batch 57 (**S1**): ⭐⭐⭐ **AiPrimitive emits NO state metadata — a whole dispatch kind is invisible**

> 📌 **Dispatched at `STAMP`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⛔⛔ **RUNS AFTER BATCH 56 LANDS AND IS VERIFIED.** ⭐ **56 unifies what the emitters WALK; this batch
> depends on that union being in place.** 📐 **If 56 is not merged when you start, do 56 first.**
> ⭐ **Rule 7:** branch from this branch. ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1674+` is the next free diagnostic.

---

## 0. Why this was pulled forward

⭐ **User ruling, `2026-08-15`: pull `S1` ahead of the panel work.** ⛔ **Without it the Details and
Watch value columns cannot work for ANY AiPrimitive asset** — and that would have surfaced in the
middle of the panel batches as a mystery, blamed on the panel.

📄 **Design: [DESIGN_Variable_Details_And_Live_Values.md](DESIGN_Variable_Details_And_Live_Values.md) §4, blocker B2.**

---

## 1. ⭐⭐ The gap — coordinator-verified by reading, not inferred

| | |
|---|---|
| **`CSharpEmitter:77`** | `if (asset.Dispatch == AssetDispatch.Instance)` — ⛔ **gates BOTH the variable `AddPin` calls AND `AddStateLayoutField`.** ⇒ **an AiPrimitive contributes NOTHING to `DebugMap.StateLayout`** |
| **`EmitAiPrimitiveRegistration:329`** | ⛔⛔ **`StateSize = 0`, and NO `StateFields =` block at all** |
| **`EmitInstanceRegistration:413`** | ✅ emits the full `StateFields` dictionary — ⭐ **this is your template** |

⇒ 🔴🔴 **`BlueprintDefinition.StateFields` is empty and `DebugMap.StateLayout` is empty for every
AiPrimitive asset.** ⇒ **`TryGetField<T>` cannot resolve a name; `MarshalFromBytes` is never reached;
the panel shows `—` for every working-state variable of every type.**

### ⭐⭐ The sharpest part: **a reader already exists for data nobody emits**

`BlueprintDebugSession.CaptureAiPrimitiveState` is **written, shipped, and named for this exact case** —
it validates `storedHash == def.StructureHash`, then reads `mapIndex?.StateLayout.Fields` **or**
`def.StateFields`. ⛔ **Both are empty for an AiPrimitive, so it silently reads nothing and returns.**
⭐ **Trap #5 at system scale: a consumer with no producer, green because nothing ever asked it for a
value.** 📌 **32 shipped assets are `(Parameter, WorkingState)` — this is not a corner.**

---

## 2. Scope

| | |
|---|---|
| ⭐ **Emit `StateFields` for AiPrimitive** | mirror `EmitInstanceRegistration:413`; **real `StateSize`, not `0`** |
| ⭐ **Lift the `:77` gate** | the debug map's variable pins + `AddStateLayoutField` must be produced for AiPrimitive too, ⚠ **walking the UNION Batch 56 establishes — not `Variables` again** |
| 🔴🔴 **DO NOT EMIT METADATA YOU CANNOT TRUST** | ⛔ **AiPrimitive working state is sized at RUNTIME** (`StaticTypeRegistry:71-73`: *"for an AiPrimitive WorkingState var the slot is sized at RUNTIME (`Marshal.SizeOf<WorkingState>()`), so SizeBytes here is cosmetic"*). ⇒ ⭐ **the `layoutFromRuntime` treatment (`CSharpEmitter:412`, `Marshal.OffsetOf<…>` / `Unsafe.SizeOf<…>`) must extend to the working-state path.** ⛔ **Wrong offsets are WORSE than none — they read the wrong bytes and look plausible** |
| 📐 **Check for a SECOND metadata source before adding a third** | ⚠ **`GeneratedBlueprintSchemaCatalog` (`BP-242`) is an independent `*.bp.json` parser**, and `LiveBlackboardValueProvider` reads `BrainBlackboard` at `BehaviorParameters + byteOffset` from the BTree/HSM schema. 📐 **Report whether AiPrimitive params/state are ALREADY described somewhere** — ⛔ **ruling 9: do not create a third description of one layout** |
| ⛔ **NOT in this batch** | `S2` (the 4-byte guess / `StructSizeResolver` reuse) · `S3` (`MarshalFromBytes` struct arm) · `S4` · `S5` · any panel work |

---

## 3. Gates

**Baseline — coordinator-run at `ee4d134ab`:** build **0 errors / 69 warnings** · Blueprints
**3572 / 3562 / 0 / 10** · AiShared **1216** · BTree **612** · Breakpoints **130** · Generators **193** ·
NodeEdit **208 / 131**. ⚠ **Re-baseline against Batch 56's merged numbers, not these.**

| | |
|---|---|
| 🔴🔴 **`StructureHash` unchanged for all 42** | ⛔ **this batch adds METADATA, not struct fields. If a hash moves you changed the layout** |
| ⭐⭐ **Golden Tier 1 UNCHANGED** | Tier 1 is `StructureHash` + emitted struct fields + the diagnostic multiset — ⛔ **none of which a registration block touches** |
| ⭐ **Golden Tier 2 MOVES — declared, and only for AiPrimitive assets** | 📐 **review the diff and say how many assets moved.** ⚠ **If an Instance asset's source moves, something leaked** |
| **`persistence-shape.txt`** | ⛔ **UNCHANGED** — this batch does not touch persistence |
| `tracker-counts.py --check` | clean |

### ⭐⭐⭐ The gate that matters — **prove the data is now READABLE**

⛔ **"The dictionary is emitted" is not the deliverable.** ⭐ **`BP-223`'s lesson: verify the CONSUMER.**

📐 **A runtime test that, for a real AiPrimitive asset with working-state variables, reads a value back
through `BlueprintDebugSession` / `TryGetField<T>` and asserts the ACTUAL VALUE** — ⭐ **not that a
descriptor exists.** ⛔ **It must be RED before the change** *(today it can only return nothing)*.

📐 **Include one struct-typed working-state variable** — `MemberSlotList` and `WaveState` ship in
`HillAssault2_MemberSlotListSmoke` and `HillAssault2_IsWaveCompleted`. ⚠ **If the struct arm is
missing from `MarshalFromBytes` (`S3`), say so and let it read as raw bytes** — ⛔ **do not fix `S3`
here**, but ⭐ **do record whether the offset/size were right**, because that is what this batch owns.

---

## 4. ⚡ How to work

**Opus.** ⭐ **Emitting a wrong offset is a wrong-values bug that looks like working software.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the tracker is yours — ⭐ **file B2 as a row and close it here** |

---

## 5. Reporting

🔴🔴 **`StructureHash` unchanged for all 42, stated FIRST** · ⭐⭐ **the runtime read-back test, and
that it was RED before** · ⭐ **how many assets moved in Tier 2, and that no Instance asset did** ·
⭐ **whether working-state offsets are runtime-derived or baked, and why that is safe** ·
📐 **whether a second description of this layout already exists** · `persistence-shape.txt` unchanged ·
per-suite numbers **full and filtered** · `tracker-counts.py --check` · ⭐ **every id you allocated**.

⭐⭐⭐ **The question to carry:** ⛔ **`CaptureAiPrimitiveState` has been shipped and green for its
entire life while reading nothing.** 📐 **What else in the debug path is a consumer with no producer?**
⚠ **That is `BP-240`'s shape one level up: not "the corpus does not exercise it" but "nothing ever
produced the input at all."**
