# HANDOFF — Batch 51: ⭐ **`U-11`'s editor bucket — the last thing before `U-12`**

> 📌 **Dispatched at `<STAMP>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1672+` is the next free diagnostic.
>
> 📄 [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md) §2 · `U-11` — **D3**, editor half.
> ⭐⭐ **On the critical path:** `U-11` → `U-12` → `U-10`'s wiring, ⛔ **and `U-12` cannot delete the
> views while anything still reads them.**

---

## 0. ⭐ One bucket, deliberately alone

⚖️ **`U-12` is NOT in this batch, and that is a judgement call worth stating.** It carries **three rail
restatements** (`BP1024` gone · `BP1031` split · `BP1011` restated) ⭐ **and the store flip** — your own
Batch 50 note: *"`BlueprintCompiler`'s six-line storage copy stays: it builds an asset's storage, which
is what does not move until `U-12` flips the store."*

⇒ ⛔ **Pairing a mechanical sweep with a store flip and three rail changes puts two very different
revert stories in one batch.** ⭐ **This one is small. Let it be small.**

📐 **If you disagree — if the editor sweep turns out trivial enough that `U-12` fits — say so and take
it.** ⚠ **But not silently: the bucket boundary is the audit unit.**

---

## 1. The scope — coordinator-counted on the merged tree today

**Files under `Hrot.Blueprints.Editor` still reading `asset.Parameters` / `.WorkingState` / `.Variables`:**

| refs | file | note |
|---|---|---|
| **18** | `Variables/BlueprintVariablesWindow.cs` | ⚠ **see §2 — this one is special** |
| **12** | `Host/BlueprintDocumentFactory.cs` | |
| **9** | `Host/NodePinSchema.cs` | |
| **4** | `Host/BlueprintNodeModel.cs` | |
| **3** | `Host/BlueprintPickerSources.cs` | |
| **2** | `Windows/BlueprintMyBlueprintModel.cs` | |
| **1** | `Windows/BlueprintMyBlueprintWindow.cs` · `Host/BlueprintGraphModel.cs` | |

⭐ **~50 references, 8 files.** ⚠ **Raw counts — expect the same incidental-vs-semantic split you found
last batch, and report the real number.**

⛔ **The compiler files that still reference the three lists are NOT in scope** — `FieldLayout`,
`StructureHashComputation`, `AiPrimitiveLowering`, `CSharpEmitter`, `EmissionContext`,
`WhenLowering_Instance`, both emitters. ⭐ **Your Batch 50 finding: those are `IrAsset`'s same-named
lists, they set struct offsets and feed `StructureHash`, and sweeping them would move the hash.**

---

## 2. ⚠⚠ `BlueprintVariablesWindow.cs` — the biggest count, and the one to touch least

```
:14   BlueprintEditableAssetAdapter   — adapter
:45   BlueprintVariableSchemaSource   — ⭐ U-4/U-5 rebuilt it. SURVIVES U-16
:377  BlueprintVariablesWindow        — ⛔ the standalone window U-16 RETIRES
```

⭐ **The source and the window share a file, not a fate.** ⇒ 📐 **Move the SOURCE properly; give the
WINDOW the minimum that keeps it correct and compiling.** ⛔ **Do not rewrite a window `U-16` deletes.**

⚠ **This is now forced rather than advisory:** ⛔ **`U-12` deletes the views**, so the window must move
off them regardless — ⭐ **but "must compile against `Declarations`" is a much smaller change than the
rewrite the plan's stale note implies.** **Say what you left alone.**

---

## 3. Gates

| | |
|---|---|
| ⭐⭐ **Golden Tier 1 + Tier 2 unchanged at EVERY sub-step** | ⛔ not only at the end |
| ⭐ **`persistence-shape.txt` unchanged** | ⚠ **consumers, not storage** — ⛔ **if it moves, something wrote through that should not have** |
| ⭐ **AiShared 1216 unchanged** | ⛔ **this is blueprint-editor only** |
| 🔴 **Revert-goes-red** | per sub-step, as last batch |
| ⭐⭐ **A grep assertion: nothing under `Hrot.Blueprints.Editor` reads the three lists** | ⭐ **`U-12` needs this to be TRUE, not believed** — and a grep is the only thing that can say so |

**Baseline — coordinator-run on the merged Batch-50 tree (`2a8188dd`):**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| Blueprints | **3515 total / 3505 passed / 0 failed / 10 skipped** |
| ⭐ **AiShared 1216** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |
| ⭐⭐ **Golden + persistence-shape** | ⛔ **UNCHANGED** |
| `tracker-counts.py --check` | clean **nineteen** batches running |

⭐ **Run the five `--no-build` suites in parallel; keep `\[FAIL\]` in the grep.**
⚠⚠ **The two NodeEdit gates take NO `--no-build`.**

⛔ **Say plainly what is NOT covered headlessly.** ⚠ **The visual check has not run for FOURTEEN
batches**, and this bucket touches the panel, the picker sources and the pin schema. ⭐ **The moves are
mechanical and the gates are real — but *"the panel still draws what it drew"* is not among them.**

---

## 4. ⚡ How to work

**You are on Opus.** 🟢 **Sonnet fits the mechanical sweep once you have fixed the pattern per file.**
⛔ **Opus keeps the `Variables ∪ WorkingState` sites** — ⭐ **you found three in the compiler where
`ById()` would have widened the search to `Parameters`; expect the same shape here.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the tracker is yours — ⭐ **`U-11` closes here if the grep assertion holds** |
| ⚠ **Stop point** | **at a file boundary**, and say which files moved |

---

## 5. Reporting

Per-suite numbers · ⭐⭐ **golden + `persistence-shape` unchanged, per sub-step** · ⭐ **the real
semantic count** · ⭐⭐ **what you left alone in `BlueprintVariablesWindow` and why** · ⭐ **whether the
grep assertion holds — i.e. whether `U-12` is now unblocked** · `tracker-counts.py --check` ·
⭐ **every id you allocated** · ⭐ **where you stopped** · anything here **wrong against the code**.

⭐⭐ **Batch 50's most valuable output was not the sweep — it was the MEASUREMENT that deleted two
buckets from the plan.** *"~34 semantic sites"* was 135 across 24 files, ~31 of them on a different
type where sweeping would have moved `StructureHash`. ⭐ **The plan was wrong in a way that would have
been found only by breaking something.**

⚠ **This batch's equivalent is the grep assertion.** ⛔ **`U-12` deletes the views on the strength of
*"nothing reads them any more"* — and if that is a belief rather than a checked fact, `U-12` is the
batch that finds out.**

📌 **And one recurring nudge, now four batches old:** `BP-236` was an **order-dependent green**.
⭐ **When a gate passes, it is worth one thought about what else had to be true for it to pass.**
