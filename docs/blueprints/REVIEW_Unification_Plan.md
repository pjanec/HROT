# REVIEW — the unification task plan

> **Implementation session, Batch 40.** Assessment of
> [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md), commissioned by
> [HANDOFF_Batch40](HANDOFF_Batch40_Unification_Plan_Review.md).
>
> ⭐ **No feature code.** Every claim below that could be probed, was — throwaway xunit probes over the
> real corpus, deleted after. **Measured on the post-Batch-39 tree** (`a4b69e0` merged; Blueprints
> baseline **3259**/0/10). Gates at both ends: §9.

---

## 0. ⭐ Verdict

> **Run it — with five named changes. No re-cut; the task boundaries are right.**

| # | change | task |
|---|---|---|
| **V1** 🔴 | **U-10's "byte-for-byte identity" is unwritable as specified** — ⭐ measured: **0 of 58** shipped files survive even `Deserialize→Serialize` byte-identically. Fix: a **canonicalisation pre-step** (§2 U-10) | U-10 |
| **V2** 🔴 | **U-5 is not a blueprint-editor task** — the capability flag it needs **does not exist on the shared interface**, and adding it moves the **AiShared gate** and touches the BTree/HSM implementers | U-5 |
| **V3** 🟠 | **A task is missing: retire (or re-point) the standalone `BlueprintVariablesWindow`.** Without it, stop-after-45 ships **two ways to edit a variable** — the defect the programme exists to remove | new, batch 44 |
| **V4** 🟠 | **U-1 must define the corpus as "the generator's `AdditionalFiles`", not a count** — and must **preload `Hrot.AI.Behaviors`**, or three assets fail spuriously (§2 U-1) | U-1 |
| **V5** 📌 | The plan is scoped against **three** asset lists and **`IrAsset` now has four** — `GraphLocalSlots`, merged in Batch 39, already in `StructureHash`. Substance unaffected (§6); wording of U-9/U-10/U-11 needs the fourth list |  |

⭐ **The two things the plan most feared are fine:** the golden harness is cheap (**634 ms for all 42**,
§2 U-1) and stage C's seam exists and is already used by shipped tests (§2 U-3).

---

## 1. Per-task: is the gate writable?

| task | verdict | one line |
|---|---|---|
| **U-1** | ✅ **writable, with V4's two conditions** | 42/42 compile in-test, 634 ms, diagnostic set exactly the baseline's 10×`BP3010` |
| **U-2** | ✅ writable | Batch 38's C7 probe already IS Pass 1's red form — the splice removes the caller's `MacroCallNode` today |
| **U-3** | ✅ writable | tests already drive Stage 3→7 directly, bypassing Stage 2 (`InlineAction_EndToEndTests` pattern); no new seam needed |
| **U-4** | ✅ writable | exactly two construction sites (`BlueprintVariablesWindow.cs:238,239`); the grep gate is honest |
| **U-5** | ⚠ **writable with V2** | the reference walk is headless; ⛔ **the capability flag is a SHARED-INTERFACE addition**, not a blueprint override |
| **U-6** | ✅ writable as split | `DetailsViewRegistry` is plain C# — `Register`/`CanHandle`/`Build` need no ImGui; the plan already concedes render to the visual check |
| **U-7** | ✅ writable · 📐 one contract note | golden confirmed safe: all 42 compile clean, so no shipped asset trips the rail |
| **U-8** | ✅ writable | compile-per-offered-type against a reflection oracle |
| **U-9** | ✅ writable · ⭐ separability condition | separable from U-10 **iff U-9's serializer keeps writing the OLD three-list shape** — the tag must not reach JSON until U-10 |
| **U-10** | ⛔ **NOT as specified** — V1 | byte identity is impossible against hand-authored files; DOM identity or canonicalise first |
| **U-11** | ✅ writable | buckets separate cleanly because the views survive until U-12; ⭐ **and the editor share has shrunk** — U-4/U-5 already rewrite most of `BlueprintVariablesWindow` |
| **U-12** | ✅ writable | straightforward; the grep-for-views gate mirrors U-4's |
| **U-13** | ✅ writable | asserted against the 8 assets and their known counts |
| **U-14** | ✅ writable | trivial after U-9, as sequenced |

---

## 2. The six suspects, measured

### U-1 — the golden harness. ⭐ **Achievable, cheap, and two traps found by building it**

| measured | |
|---|---|
| ⭐ **634 ms for all 42**, warm ~5 ms/asset | the slowest single asset is 249 ms and that is first-call JIT. Against a Blueprints gate that already runs ~95 s, **this is a gate, not a nightly** (§3.1 answered) |
| ⛔ **Trap 1 — the corpus is NOT "all shipped `.bp.json`"** | production compiles **`Assets/Blueprints` only** (`AdditionalFiles`); `Recipes/Blueprints` is `Content` and never reaches the generator. ⭐ **Globbing both also throws**: `SmokeGuard` exists in each root **with the same `AssetId`**, and one sibling catalog cannot hold both. The plan's "42" is right — but by count, not by definition. **Define the corpus as the generator's inputs** |
| ⛔ **Trap 2 — three assets fail without a preload** | `HillAssault2I_{DispatchWaveWithTargets, IsAreaQueryResolved, RequestAreaQuery}` fail `BP1602` under bare `Compile`: null `ClrSignatureResolver` ⇒ Stage 0 reflects over **loaded** assemblies, and their CLR-targeting `FunctionCall` pins never rehydrate until `Hrot.AI.Behaviors` is loaded. **One `typeof(...).Assembly` touch fixes it — 42/42** |
| ✅ Prove-it-bites | writable exactly as specified — mutate one field order in memory, assert the failure names asset and field |

⭐ **§3.2 — the invariant I would trust.** Not a generated-source *hash*. Two tiers:

| tier | contents | on change |
|---|---|---|
| **1 — never moves undeclared** | `StructureHash` · every emitted struct field (name, type, offset, size) · the **diagnostic multiset** (code × count) | a task that moves these **declares it in the task text** |
| **2 — moves only with a regenerated baseline** | ⭐ **the full generated source, stored as FILES in the baseline dir** (~6 KB × 42 ≈ 250 KB), not hashed | a comment-only change regenerates the baseline **in the same commit**, and the reviewer reads a real diff. ⭐ **A hash names the asset; a stored file names the LINE** |

📌 **Not established:** whether the in-process harness's emit is byte-identical to the **generator's**
(semantic-model resolver vs reflection resolver). The diagnostic set matches the baseline exactly,
which is strong evidence, but a one-time `EmitCompilerGeneratedFiles` comparison would close it —
worth doing inside U-1.

### U-3 — the seam exists. **And the §4 worry about the golden set dissolves**

Shipped tests already call `Stage2_Validate.Run` → `Stage3` → `Stage4` → `Stage5` → `Stage6` →
`Stage7` individually — constructing an illegal asset and running Stages 3–7 without Stage 2 is the
established pattern, no `InternalsVisibleTo` change needed.

⭐⭐ **The handoff's §4 fear — "`BP-226`'s wrong resolution is IN the golden set, so U-3 must declare a
change" — is FALSE, and that is good news.** No shipped asset has both `Variables` and `WorkingState`
populated (`BP1024`/`BP1031` refuse it, re-confirmed by compile in Batch 38), so **the wrong resolution
never fires inside the golden corpus.** U-3's Pass 2 lives on an in-memory asset outside it.
⇒ **U-3 declares NO golden change. The invariant is not undermined — it never contained the bug.**

⚠ One merged-tree note: `VarFieldName` now **throws on a negative index** (`BP1670`'s assertion,
Batch 39). U-3's `(kind, index)` refactor must preserve that throw, not smooth it away.

### U-6 — headless up to `Build`, exactly as the plan splits it

`DetailsViewRegistry` (`NodeEditor.UI/Panels`) is a plain class — `Register`, `CanHandle`, `GetViewFor`
run without an ImGui context. Only `IDetailsView.Draw` needs pixels, and the plan already sends that to
the visual check. **Confirmed writable as split.**

### U-7 — the fallback contract is right for staging, under-ambitious as an end state

"No oracle ⇒ compiles exactly as today" is the correct *staged* contract — production always has an
oracle (the generator's semantic model), so the pass-through survives only where it already exists.
📐 **But the repo already has the better precedent:** `IClrSignatureResolver` is semantic-model-backed
in the generator and **reflection-backed in-process**. Mirror it — a reflection oracle for the editor
path — and "no oracle" becomes a pure-unit-test corner instead of the editor's reality.

### U-10 — ⛔ **the headline finding: byte identity is unwritable as specified**

Measured: **0 of 58** shipped files are byte-identical after nothing more than
`Deserialize → Serialize`:

```
disk: {\n  "$meta": {\n    "docType": …      ← hand-authored, 2-space indented
re  : {"$meta":{"docType":…                  ← serializer, WriteIndented = false
GateConditionDemo re-serialized: "Dispatch":"AiPrimitive"   ← was the numeric 1
```

Any serializer-based migrator reformats every file it touches, so **v1→v2→v1 can never be
byte-identical with the corpus as it stands** — the gate would fail on indentation before the migration
logic ran once.

📐 **Fix — choose one, and I recommend (b):**

| | | |
|---|---|---|
| (a) | redefine Pass 1 as **DOM equality** + `StructureHash` equality | writable today; weaker — normalisation slips through unasserted |
| **(b)** ⭐ | **a canonicalisation pre-step**: one commit, before U-10, that re-serializes the whole corpus through `BlueprintJsonServices` — a semantic no-op the golden harness proves (hashes, layouts, diagnostics unchanged) | after it, **byte-for-byte v1→v2→v1 is meaningful and is the strongest possible gate**. ⭐ It also settles `BP-227` deliberately: the numeric `Dispatch` normalises to the string, asserted, answering Pass 4's "say which" |

📌 `BP-227`'s count corrected while probing: **7** numeric-`Dispatch` files — 4 in the golden corpus,
3 more under `Recipes/`. The row is updated.

📌 U-10 Pass 3 (`StructureHash` unmoved) is unaffected by Batch 39's fourth list: `GraphLocalSlots` is
empty for every shipped asset (no corpus locals), and goldens are recorded post-merge anyway.

### U-11 — the buckets separate, and the editor share shrank

One commit per bucket works because the old views survive until U-12 — nothing forces compiler and
editor into one commit. ⭐ **§4's open question ("one batch or three?"): ONE batch, two sub-steps**
(compiler buckets · editor remainder). The census's scary number was `BlueprintVariablesWindow`, and
**U-4 + U-5 already rewrite most of it** before U-11 arrives.

---

## 3. What is missing

| | |
|---|---|
| **3-a** 🟠 **V3 — nothing retires the standalone Variables window** | `BlueprintVariablesWindow` lives on inside `BlueprintVariablesManagedWindow` after U-6 puts the same table in Details. ⛔ **Stop-after-45 then means two live editing surfaces for one model** — precisely the sprawl the programme exists to remove. **Name the task; batch 44 is its lane** |
| **3-b** 🔴 **V2 — the shared interface has no capability flag** | `IVariablesSchemaSource.UpdateVariableRole/Scope` are **default-bodied interface members** — the silent no-op is the *interface's* contract, not just the blueprint override's. U-5 Pass 2 therefore **adds a member to `Hrot.Editor.AiShared`**: the AiShared gate (1213) moves, `BTreeHsmSchemaSource` and the HSM source are touched, and **R3 stands** — `UpdateVariableScope` takes `WorkingStateScope`, which cannot carry a blueprint scope. Batch 43's lane description ("one file, one lane") is wrong as written |
| **3-c** 📌 the corpus definition (V4) and the preload | §2 U-1 |
| **3-d** §3.4 | **not established** — whether anything outside this repository reads `.bp.json`. Same gap Batch 38 declared; nothing in this repo can answer it |

---

## 4. The two rulings, pressure-tested

| ruling | verdict |
|---|---|
| ⭐ **U-1 first** | ✅ **holds, strengthened.** The entrenchment worry is empty: the golden corpus **does not contain** `BP-226`'s wrong resolution (§2 U-3), so the harness entrenches only behaviour that is correct today. And it is cheap enough to run on every test pass |
| ⭐ **stop-after-45** | ⚠ **holds only with V3.** As planned, 45 leaves the standalone window and the Details table both alive — a designer meets two editors for one concept. With the retirement task added, the exit point is genuinely coherent: three defects closed, one editing surface, model untouched |

---

## 5. Sequencing against the merged tree (§3.6)

| | |
|---|---|
| **U-2 / U-3 vs the merged locals work** | ✅ **no conflict.** Locals resolve through their own `FindLocalIndex`/`LocalFieldName` path, never through `FindVariableIndex`/`VarFieldName`; U-3 renames the latter pair only. U-2 (graph copy) touches Stage 2.5 splicing, which the locals work never modified |
| **U-9/U-10/U-11 vs `GraphLocalSlots`** (V5) | ⭐ **substance unaffected, wording stale.** The fourth list is **IR-only** — never serialized, so D's migration doesn't see it. But `StructureHashComputation` now appends four lists, the emit bucket includes the slot-emission sites in both emitters, and `FieldLayout` lays the slots out after the asset lists. U-10 Pass 3 and U-11's bucket inventory should name them |
| **U-3 must keep `BP1670`'s throw** | `VarFieldName` now throws on a negative index — the assertion that the Stage-2 rail is complete. The `(kind, index)` refactor keeps it |

---

## 6. Corrections to the plan's numbers

| plan says | measured |
|---|---|
| "42 shipped assets" | ✅ right number, wrong definition — **the generator's `AdditionalFiles`** (Assets/ only). 58 exist on disk; 16 are recipes production never compiles |
| `BP-227`: "four" numeric-`Dispatch` assets | **7 files** — 4 golden + 3 recipes (row updated) |
| U-13's counts ("58 `state`, 3 `rally`") | ✅ confirmed across the 8 assets |

---

## 7. ⭐ What I could not establish

| | |
|---|---|
| **Generator-parity of the golden emit** | my harness runs the in-process path (reflection resolver); production runs the semantic-model resolver. Diagnostic sets match exactly; **byte-level parity of the emitted source was not compared** — close it once inside U-1 via `EmitCompilerGeneratedFiles` |
| **External `.bp.json` readers** | outside this repository — unanswerable from here (§3-d) |
| ⛔ **Anything visual** | the visual check has now not run for **six** batches; U-6/U-13 are exactly what it would catch. The plan's own §4 says it needs the user |

---

## 8. Ids

**No new tracker rows** — every defect the probes surfaced was already filed (`BP-227` corrected,
`BP-228…BP-233` stand). No diagnostic codes allocated. `U-n` remain plan labels.

---

## 9. Gates

| | start | end |
|---|---|---|
| Solution build | **0 errors**, 69 warnings | **0 errors** *(incremental — warning count under-reports, as Batch 38 recorded; no compiled file changed)* |
| Blueprints suite | **3259** / 0 failed / 10 skipped | **3259** / 0 failed / 10 skipped |

Tree measured: post-Batch-39 (`a4b69e0` merged). **No product code changed; both probe files deleted.**
