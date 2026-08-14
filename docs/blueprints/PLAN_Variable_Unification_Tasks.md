# PLAN — the variable unification, as tasks with gates

> **Coordinator, 2026-08-13.** Decomposition of
> [Variable_Model_Unification.md](Variable_Model_Unification.md) ·
> [Variable_Editing_UI.md](Variable_Editing_UI.md), as re-ordered by
> [the Batch 38 review](REVIEW_Unified_Variable_Design.md).
>
> ✅ **REVIEWED — [Batch 40](REVIEW_Unification_Plan.md), `2026-08-13`. Verdict: run it, with five named
> changes. No re-cut — the task boundaries are right.** ⭐ **This plan is updated to match.**
>
> | | what changed |
> |---|---|
> | 🔴 **V1** | ⛔ **`U-10`'s byte-identity gate was UNWRITABLE** — **0 of 58** shipped files survive a serializer round trip. ⇒ ⭐ **new `U-15`: canonicalise the corpus first** |
> | 🔴 **V2** | **`U-5` is not a blueprint-editor task** — the capability flag is a **shared-interface** addition that moves the **AiShared gate** |
> | 🟠 **V3** | ⭐ **new `U-16`: retire the standalone Variables window** — without it, stop-after-45 ships **two ways to edit a variable** |
> | 🟠 **V4** | `U-1`'s corpus is **the generator's `AdditionalFiles`**, not a count — plus a preload |
> | 📌 **V5** | `IrAsset` now has a **fourth** list (`GraphLocalSlots`, Batch 39) — wording only |
>
> ⭐ **The two things this plan most feared are fine:** the harness costs **634 ms for all 42**, and
> stage `C`'s seam already exists and is used by shipped tests.
>
> ⚠ **`U-n` are PLAN LABELS, not tracker ids** (rule 3: the coordinator allocates none). The
> implementation session allocates `BP-` rows and diagnostic codes as it goes.

---

## 0. ⭐⭐ The idea that makes the rest verifiable — build the net first

Every task below is a refactor of code that already works. ⇒ **the primary success condition for
almost all of them is "the output did not change."** That is only checkable if something *records*
the output first.

⭐ **`U-1` builds a golden-corpus harness before anything is touched.** After that, each task's gate is
**"the golden set is unchanged, except where this task declares a change"** — plus its own specific
assertions. ⛔ **Without `U-1` the whole programme is unfalsifiable.**

---

## 1. The tasks

| | task | touches | gate in one line | depends on |
|---|---|---|---|---|
| **U-1** | ⭐ **golden-corpus harness** | tests only | the baseline exists and **provably catches a change** ⚠ **V4: corpus = the generator's `AdditionalFiles`, + a preload** | — |
| **U-2** | compiler owns its graphs (`BP-229`) | compiler | the caller's `Graph` is unchanged after `Compile` | U-1 |
| **U-3** | ⭐ **`(kind, index)`** — stage **C** (`BP-226`) | compiler | a `WorkingState` index no longer resolves to `Variables[i]` | U-1 |
| **U-4** | `Variables` as a third schema source; kill `bool isParams` — stage **A** | editor | all three kinds project the right list | U-1 |
| **U-5** | make the schema source honest (`BP-230`, `BP-231`) | ⚠ **editor + `Hrot.Editor.AiShared`** | the reference count is **real**; order lists survive remove/rename ⚠ **V2: moves the AiShared gate** | U-4 |
| **U-6** | Details hosts the table; selection routes — stage **B** | editor | the provider handles `Variable` **and** `LocalVariable` | U-4, U-5 |
| **U-7** | ⭐ **type-existence rail** (`Q-j`, `BP-228`) | compiler | `Totally.Made.Up.Type` is **refused**; no oracle ⇒ unchanged | U-1 |
| **U-8** | type-choice union — stage **B′** | editor | **every offered type compiles** | U-7 |
| **U-9** | tagged declaration + projections — **D1** | model | old lists become views; **golden unchanged** | U-3 |
| **U-15** 🆕 | ⭐ **canonicalise the corpus** — re-serialize every asset through `BlueprintJsonServices` | assets | ⭐ **a semantic no-op the golden harness proves**; settles `BP-227` | U-1, U-9 |
| **U-10** | migrator **pair** + envelope 1→2 — **D2** | persistence | ⭐ **v1→v2→v1 is the identity**, `StructureHash` unmoved ⚠ **only meaningful AFTER `U-15`** | U-15 |
| **U-11** | consumers moved off the views — **D3** | ~34 sites | golden unchanged **at every sub-step** | U-9 |
| **U-12** | rails restated; views deleted — **D4** | compiler | `BP1024` gone · `BP1031` split · `BP1011` restated | U-11 |
| **U-13** | shared-state read-only view (`Q-i`) | editor | lists exactly the referenced slot names | U-4 |
| **U-14** | `MakeUniqueName` across all kinds (`BP-232`) | editor | a `Parameter` and a `Variable` cannot share a name | U-9 |
| **U-16** 🆕 | 🟠 ⭐ **retire the standalone `BlueprintVariablesWindow`** | editor | ⛔ **exactly ONE editing surface remains** for the model | U-6 |

---

## 2. The gates, in full

⭐ **Every gate is a headless xunit test.** ⛔ **No gate is "it looks right."** Where something is not
headless-testable it is called out as such rather than papered over.

### U-1 · Golden-corpus harness ⭐ *no product change*

| | |
|---|---|
| ⭐ **The corpus** *(V4)* | ⛔ **NOT "all shipped `.bp.json`."** It is **the generator's inputs** — `<AdditionalFiles Include="Assets\Blueprints\**\*.bp.json" />`. `Recipes/Blueprints` is `Content` and **production never compiles it**; globbing both **throws**, because assets exist in each root sharing an `AssetId`. **42 is the right number and the wrong definition** |
| ⚠ **The preload** *(V4)* | three `HillAssault2I_*` assets fail `BP1602` under a bare `Compile` — a null `ClrSignatureResolver` makes Stage 0 reflect over **loaded** assemblies. ⭐ **One `typeof(...).Assembly` touch on `Hrot.AI.Behaviors`** fixes those three — ⛔ **but that alone gives 40/42, not 42/42 (corrected Batch 44, measured)** |
| 🔴⭐ **The corpus compiles as a SET** *(Batch 44)* | ⛔ **`SmokeGuard` and `SmokePatrol` fail `BP1301`** — *"CallablePeer {id} not found among compiled assets"* — because they **call each other**, and a per-asset compile with `SiblingSignatures: Array.Empty<>` cannot see the peer. ⭐ Production has always done this: `BlueprintIncrementalGenerator` parses **every** `AdditionalFiles` entry through `BlueprintSignatureParser` and hands the whole catalog to every compile. ⇒ **the harness builds the same catalog first**; the preload and the catalog are two independent prerequisites and only the first was in this plan |
| 🔴⭐ **`CompilerMode.Release`, not Debug** *(Batch 44)* | ⛔ `BlueprintIncrementalGenerator.CompileOneAsset` **hardcodes `Mode: CompilerMode.Release`** — ⚠ not derived from the MSBuild configuration — so a Debug build still emits Release blueprint code. A harness on `CompilerMode.Debug` records ~40 extra `DebugProbe.NodeEnter` lines per asset and would have baselined **output that never ships**. 📌 Not a defect: `EditorMetadata.CompilerMode` + `QuickReloadService` are the debugger's live re-instrumentation path, so Debug emit is produced on demand rather than at build time. 📌Known gap: **Debug-mode emit is NOT covered by the baseline** |
| ⭐⭐ **What it records** *(the reviewed two-tier invariant)* | **Tier 1, never moves undeclared:** `StructureHash` · every emitted struct field (name, type, offset, size) · the **diagnostic multiset** (code × count). **Tier 2, moves only with a regenerated baseline:** ⭐ **the full generated source stored as FILES** (~250 KB total), **not hashed** — *"a hash names the asset; a stored file names the LINE"* |
| ✅ **Pass** | the baseline is committed and the test is green against it |
| ⭐ **Prove it BITES** | mutate one field's order in a scratch run ⇒ **the test must fail**, naming the asset and the field. ⛔ **A harness that has never failed is not a harness** |
| 📌 **Close one gap inside this task** | ⚠ the harness runs the **in-process** path (reflection resolver); production runs the **semantic-model** resolver. Diagnostic sets match, but **byte parity was never compared** — do it once via `EmitCompilerGeneratedFiles` |
| ✅ **Cost, measured** | **634 ms for all 42**, ~5 ms/asset warm, against a Blueprints gate already ~95 s ⇒ ⭐ **a gate, not a nightly**. ✅ **Confirmed Batch 44: 849 ms** for the whole golden suite — 131 tests / ~170 compilations, i.e. ~4× the work the 634 ms figure covered |

### U-2 · Compiler owns its graphs — `BP-229`

| | |
|---|---|
| ✅ **Pass 1** | after `Compile(asset)` on an asset whose graph holds a `MacroCallNode`: the **caller's** graph still holds it · same node count · same link count |
| ✅ **Pass 2** | golden unchanged |
| 🔴 **Revert-goes-red** | remove the copy ⇒ **Pass 1 fails** |

### U-3 · `(kind, index)` — stage C, closes `BP-226`

| | |
|---|---|
| ✅ **Pass 1** | golden unchanged — this is a pure refactor for every shipped asset |
| ⭐ **Pass 2** | an asset with **both** `Variables` and `WorkingState` populated (constructed in-memory, past Stage 2) ⇒ a `WorkingState`-targeting read emits **the WorkingState field's name**, not `Variables[i]`. ⛔ **This test fails today** |
| ⭐ **Pass 3** | a `Parameters`-targeting read emits **the parameter's name**, never `__var_{index}` |
| 🔴 **Revert-goes-red** | restore the bare `int` ⇒ **Pass 2 and 3 fail** |
| ⭐ **Declares NO golden change** | ⭐⭐ **the entrenchment worry is dead:** `BP1024`/`BP1031` mean **no shipped asset has both lists populated**, so `BP-226`'s wrong resolution **never fires inside the golden corpus**. Pass 2 lives on an in-memory asset outside it |
| ⚠ **Keep `BP1670`'s throw** | `VarFieldName` now **throws on a negative index** (Batch 39) — the assertion that the Stage-2 rail is complete. ⛔ **The refactor must preserve it, not smooth it away** |

### U-4 · Third schema source; kill `bool isParams` — stage A

| | |
|---|---|
| ✅ **Pass 1** | a source built for each of the three kinds projects exactly that list |
| ✅ **Pass 2** | asking for a kind **illegal for the asset's dispatch** (an Instance's `Parameters`) is refused, not empty-and-silent |
| ✅ **Pass 3** | both construction sites updated — **grep asserts zero remaining `isParams`** |
| 🔴 **Revert** | reinstating the bool is a signature change ⇒ compile break; Pass 1 is the behavioural gate |

### U-5 · Make the schema source honest — `BP-230`, `BP-231`

| | |
|---|---|
| ⭐ **Pass 1** | `CountNodesReferencingVariable` returns the **real** count — asserted at **0, 1 and 3** references, the 3 spread across **two graphs**. ⛔ **Returns `0` today** |
| ✅ **Pass 2** | role/scope authoring reports **unsupported** via the capability flag — ⛔ **not a silent no-op** (`Q-k`: read-only for blueprints) |
| ✅ **Pass 3** | `RemoveVariable` drops the id from the matching `*Order`; `RenameVariable` leaves order untouched |
| 🔴 **Revert** | each independently |
| 🔴🔴 **V2 — this is NOT a blueprint-only task** | ⛔ `UpdateVariableRole`/`UpdateVariableScope` are **default-bodied members of the shared interface** — the silent no-op is **the interface's** contract. ⇒ the capability flag is an **`Hrot.Editor.AiShared` addition**: the **AiShared gate (1213) moves**, and `BTreeHsmSchemaSource` + the HSM source are touched. ⚠ **`R3` stands** — `UpdateVariableScope` takes `WorkingStateScope`, which cannot carry a blueprint scope |

### U-6 · Details hosts the table — stage B

| | |
|---|---|
| ✅ **Pass 1** | a Blueprint `IDetailsViewProvider` is registered and `CanHandle` is true for **`DetailsTarget.Variable`** *and* **`DetailsTarget.LocalVariable`** |
| ✅ **Pass 2** | `Build` returns a view bound to the requested id — asserted on the id, not on pixels |
| ✅ **Pass 3** | the locals source follows the **current graph**: retarget the canvas ⇒ the projected set changes |
| ⛔ **NOT headless** | that the columns *render*, and render read-only. ⭐ **This needs the visual check that has not run for five batches — say so in the report rather than implying coverage** |

### U-7 · Type-existence rail — `Q-j`, `BP-228`

| | |
|---|---|
| ⭐ **Pass 1** | with an oracle knowing only `…StructDemoData`: a variable typed `Totally.Made.Up.Type` ⇒ **`Succeeded == false`** and a diagnostic **naming the variable and the type**. ⛔ **Compiles clean today** |
| ⭐ **Pass 2** | with **no** oracle (`null`) the same asset compiles **exactly as today** — the fallback contract |
| ✅ **Pass 3** | golden unchanged — every shipped asset still compiles |
| 🔴 **Revert** | remove the check ⇒ **Pass 1 fails** |

### U-8 · Type-choice union — stage B′

| | |
|---|---|
| ⭐ **Pass 1** | ⭐ **every offered type compiles** — for each entry, build a variable of that type and compile against a real oracle. **This is `BP-87`'s lock, restored** |
| ✅ **Pass 2** | the list contains every `[BlackboardDtoStruct]` FQN **and** every primitive |
| ✅ **Pass 3** | ⛔ **no short names are offered** — a short name is `BP1500` |
| 🔴 **Revert** | drop the struct contributor ⇒ Pass 2's count fails |

### U-9 · Tagged declaration + projections — D1

| | |
|---|---|
| ✅ **Pass 1** | ⭐ **golden unchanged — nothing has moved yet** |
| ✅ **Pass 2** | a **reflection** test asserts every member of the new decl type is carried by **both** projections — the `Graph_CopyShape_PreservesEveryMember` pattern, which has already caught one real miss |
| ✅ **Pass 3** | round-trip: `Serialize(Deserialize(j)) == j` for all 42 |
| 🔴 **Revert** | cheap — no persisted change yet |

### U-10 · Migrator pair + envelope 1→2 — D2 ⚠ *the risky one*

| | |
|---|---|
| ⛔⛔ **Pass 1 — REWRITTEN by V1** | ⭐ **Measured: 0 of 58 shipped files survive even `Deserialize→Serialize` byte-identically** — **41 of 42** are hand-authored 2-space-indented and `BlueprintJsonServices` sets `WriteIndented = false`. ⇒ **the gate would fail on indentation before the migration logic ran once.** ⭐ **`U-15` canonicalises first; THEN v1→v2→v1 byte-identity is meaningful and is the strongest possible gate** |
| ✅ **Pass 2** | a v1 file loads through the v2 reader |
| 🔴 **Pass 3** | ⭐⭐ **`StructureHash` is unchanged for every shipped asset.** ⛔ **This is the no-blackboard-wipe gate — a failure here resets every deployed entity's state** |
| ✅ **Pass 4** | ⭐ **answered by `U-15`:** the numeric `Dispatch` normalises to the string, **asserted there**. 📌 `BP-227`'s count corrected by the review: **7 files** — 4 golden + 3 recipes |
| 🔴 **Revert** | ⛔ **`git revert` does not work — the down-migrator IS the revert.** It must ship and be tested **in this task** |

### U-11 · Consumers moved — D3

| | |
|---|---|
| ✅ **Pass** | golden unchanged **at every sub-step**, not only at the end |
| 📐 **Shape** | one commit per bucket — compiler stages · lowering · emit · editor — so a regression bisects to a bucket |
| ⚠ | ~34 semantic sites; **`BlueprintVariablesWindow` is a rewrite, not a line fix** |

### U-12 · Rails restated; views deleted — D4

| | |
|---|---|
| ✅ **Pass 1** | `BP1024` is gone — an AiPrimitive with `(State, Asset)` entries compiles |
| ✅ **Pass 2** | `BP1031` split — an Instance with an `Input` entry ⇒ diagnostic |
| ✅ **Pass 3** | `BP1011` restated — a Library with **any** `Asset`-scope entry ⇒ diagnostic |
| ✅ **Pass 4** | golden unchanged · **grep asserts the old views are gone** |

### U-13 · Shared-state read-only view — `Q-i`

| | |
|---|---|
| ✅ **Pass** | the view lists **exactly** the slot names referenced by `Get`/`SetShared` in the asset — asserted against the **8 shipped assets** and their known counts (58 `"state"`, 3 `"rally"`) |

### U-14 · `MakeUniqueName` across all kinds — `BP-232`

| | |
|---|---|
| ✅ **Pass** | creating a `Variable` named `Health` when a `Parameter` `Health` exists is refused |
| 📌 | trivial **after** `U-9`; awkward before, which is why it is sequenced there |

### U-15 🆕 · Canonicalise the corpus — ⭐ **V1's fix, and it must land BEFORE `U-10`**

| | |
|---|---|
| **Do** | re-serialize every asset in the corpus through `BlueprintJsonServices`, once, as its own commit |
| ⭐ **Pass 1** | ⭐⭐ **a semantic NO-OP, proved by the golden harness** — `StructureHash`, every struct layout and the diagnostic multiset **unchanged for all 42** |
| ✅ **Pass 2** | re-running it is idempotent — the second pass changes nothing |
| ✅ **Pass 3** | ⭐ **`BP-227` settled deliberately**: the numeric `Dispatch` normalises to the string, **asserted** |
| ⚠ **Cost** | every asset file churns in one commit. ⭐ **That is the point** — it happens once, visibly, with the harness proving nothing semantic moved, instead of leaking through a migration |

### U-16 🆕 · Retire the standalone Variables window — ⭐ **V3: what makes stop-after-45 honest**

| | |
|---|---|
| ⭐ **Why** | after `U-6` the same table lives in Details **and** in `BlueprintVariablesManagedWindow`. ⛔ **Two live editing surfaces for one model — the exact sprawl this programme exists to remove** |
| ✅ **Pass 1** | **grep asserts one surface**: the standalone window is gone or re-points at the shared source |
| ✅ **Pass 2** | every affordance it had is reachable from the new one — enumerated, not assumed |
| ⛔ **NOT headless** | that the survivor is usable. ⭐ **the visual check** |

---

## 3. Batches

⭐ **Grouped so each batch is one lane and one revert story.**

⚠⚠ **RENUMBERED `2026-08-13` (+2).** `BP-57`'s authoring half took **three** batches, not two — the
Local Variables section was skipped in 41 and 42 and landed in 43. ⭐ **Every `U-` batch below shifted
by two. The task groupings and their reasons are unchanged;** only the numbers moved.

| batch | tasks | why together |
|---|---|---|
| ~~40~~ | ✅ **plan review — DONE** | [`REVIEW_Unification_Plan.md`](REVIEW_Unification_Plan.md) |
| ~~41–43~~ | ✅ **`BP-57`'s authoring half — DONE, `BP-57` CLOSED** — [41](HANDOFF_Batch41_Local_Variables_Authoring.md) *(source + picker)* · [42](HANDOFF_Batch42_Local_Variables_Wiring.md) *(delete + undo)* · [43](HANDOFF_Batch43_Local_Variables_Section.md) *(the section)* | ⭐⭐ **Its schema source is an `IVariablesSchemaSource`, so `U-4`…`U-6` ABSORB it** ⚠ **and `U-6` inherits one known gap: `AddVariable` does not reject a duplicate name — the guard currently sits in the window** |
| ~~44~~ | ✅ **`U-1` · `U-2` — DONE.** The golden net is built and bites; `BP-229` closed | ⭐ **the net, then the first thing it protects.** Both compiler-only, both small |
| ~~45~~ | ✅ **`U-3` — DONE, `BP-226` closed.** Golden 42/42 unchanged | ⭐ **closes `BP-226` alone** — the highest-value single task, kept unmixed |
| ~~46~~ | ✅ **`U-4` · `U-5` — DONE, `BP-230` + `BP-231` closed.** AiShared 1213 → 1216 | ⚠ **V2: this is NOT one lane** — `U-5` reaches into `Hrot.Editor.AiShared` and **moves that gate**. Kept together anyway because `U-5` is what makes `U-6` honest |
| **👁 UNSCHEDULED** ⚠ | `U-6` · `U-13` · ⭐ **`U-16`** | Details/panel work ⚠ **all three need the visual check** · ⭐ **`U-16` is what makes the exit point real** |
| ~~47~~ | ✅ **`U-7` · `U-8` — DONE, `BP-228` closed.** ⭐ **The oracle question is RETIRED, not answered: there is no editor compile path to attach one to.** `BP-87`'s restored lock found `System.String` in the picker on its first run | rail then picker — `U-8` is meaningless without `U-7` |
| **48** ⏭ | `U-9` | ⭐ **the model change, alone.** Golden must not move · ⚠ **its serializer must keep writing the OLD three-list shape** — the tag must not reach JSON until `U-10`, or `U-9` and `U-10` collapse into one |
| **49** | ⭐ **`U-15`** · `U-10` | ⭐ **canonicalise, then migrate.** ⚠ **the only batch whose revert is code it ships** |
| **50** | `U-11` · `U-14` | ⭐ **one batch, TWO sub-steps** (compiler buckets · editor remainder) — the review's ruling; `U-4`/`U-5` already rewrote most of the scary file |
| **51** | `U-12` | the rails, once nothing reads the old views |

⚠⚠ **RE-ORDERED TWICE, `2026-08-14`, for one reason: the visual check has not run for TWELVE batches.**
⭐ **`U-6`/`U-13`/`U-16` hard-require it and are now UNSCHEDULED** — they run when the user is at a
screen. ⭐ **Everything else is headless and unblocked**, so the sequence continues past them:
`U-8` needs `U-7` ✅ · `U-9` needs `U-3` ✅ · `U-6` needs `U-4`/`U-5` ✅ — **nothing waits on `U-6`.**

⚠⚠ **The exit point moved with them.** ⭐ **The plan's *"stop after the 44–48 block and everything is
coherent"* rested on `U-16` retiring the second editing surface.** ⛔ **`U-16` is now unscheduled**, so
the coherent-stop claim is **suspended, not lost**: until it runs, a designer meets **two editors for
one concept**. 📌 **That is the standing cost of deferring the visual check, and it should be paid
before the programme is called done.**

### 👁 Ordering when the visual check is unavailable

⭐ **Only ONE batch hard-requires it: 47.** ⇒ **44 · 45 · 46 · 48 can all run headless**, and **48 may
be pulled ahead of 47** — `U-7` is compiler-only and `U-8`'s gate is *"every offered type compiles"*,
which is a headless assertion about the picker's **contents**, not its appearance.
⛔ **47 must not be run blind:** its three tasks are a Details table, a read-only view and the deletion
of a whole window — *"the panel draws it"* is the entire deliverable, exactly as in Batch 43.

---

## 4. 📐 Open, and deliberately not decided here

| | |
|---|---|
| ✅ ~~Is `U-11` one batch or three?~~ | **RULED: one batch, two sub-steps** — the buckets separate because the old views survive until `U-12`, and `U-4`/`U-5` shrink the editor share first |
| ⚠ **Does the editor get a type oracle at all?** | `Q-j`'s lean was *not at first*. ⭐ **The review pushes back:** `IClrSignatureResolver` is already semantic-model-backed in the generator **and reflection-backed in-process** — mirror it, and *"no oracle"* becomes a unit-test corner instead of the editor's reality. 📐 **Still open** |
| **Does `U-13` earn a batch?** | it is small and independent; it is in 44 for lane affinity, not need |
| ⛔ **The visual check** | ⚠ **has not run for NINE batches** *(as of `2026-08-13`)*, and `U-6`/`U-13`/`U-16` — **plus Batch 43's Local Variables section, which is entirely a panel surface** — are exactly what it would catch. **Not a task here because the coordinator cannot specify it headlessly — it needs the user.** ⭐ **See the 👁 note under §3: only batch 47 hard-requires it** |
