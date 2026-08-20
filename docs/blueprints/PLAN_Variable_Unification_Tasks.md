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
| ~~**U-9**~~ ✅ | tagged declaration + projections — **D1** | model | ✅ **LANDED B48** — ⚠ built inverse: the **tagged type is the view**, the lists stay the storage; **golden unchanged** | U-3 |
| ~~**U-15**~~ ✅ | ⭐ **canonicalise the corpus** | assets | ✅ **LANDED B49** — all 58 (42 corpus + 16 recipes); Tier 1 **and** Tier 2 unchanged; `BP-227` closed. ⭐ **Canonical form is INDENTED** | U-1, U-9 |
| ~~**U-10**~~ ✅ | migrator **pair** + envelope 1→2 — **D2** | persistence | ✅ **DONE B55 — the `D` programme's last task.** transform B49 · reader B54 · ⭐ **seam + registry + BUMP B55**: the 58 shipped assets are **v2 on disk**, `$meta.schemaVersion = 2` agrees with `CurrentVersion`, and ⭐⭐ **`StructureHash` is unchanged for every one**. `BP-235` closed via [`Q31`](Architect_Question_31_Migration_Seam_ANSWERS.md) `A1` | U-15, U-12, Q31-A |
| ~~**U-11**~~ ✅ | consumers moved off the views — **D3** | ⛔ **135 refs / 24 files, not ~34** | ✅ **DONE B50 (compiler) + B51 (editor).** ⭐⭐ **`ViewsAreUnreadTests` asserts nothing reads the three lists — `U-12` is unblocked as a CHECKED FACT** | U-9 |
| ~~**U-12**~~ ✅ | rails restated; store flipped — **D4** | compiler | ✅ **DONE** — rails B52 (`BP1024` retired · `BP1031` split · `BP1011` restated · 🆕 `BP1673`), ⭐⭐ **store flipped B53 with `persistence-shape.txt` unchanged**. ⚠ The three properties **survive** as live windows — see §U-12 | U-11 |
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

### U-9 · Tagged declaration + projections — D1 — ✅ **LANDED, Batch 48**

| | |
|---|---|
| ✅ **Pass 1** | ⭐ **golden unchanged — nothing has moved yet.** 42/42 both tiers |
| ✅ **Pass 2** | a **reflection** test asserts every member of the new decl type is carried by **both** projections — the `Graph_CopyShape_PreservesEveryMember` pattern, which has already caught one real miss |
| ⛔⛔ **Pass 3 — REWRITTEN by Batch 48** | ~~round-trip: `Serialize(Deserialize(j)) == j` for all 42~~. **Two independent problems, both measured.** ⛔ **(i) It does not run on this corpus** — 41 of 42 files are hand-authored 2-space-indented against `WriteIndented = false`, the same fact `U-10`'s Pass 1 already carries; it fails on whitespace before reaching the question. ⭐⭐ **(ii) Even canonicalised it proves nothing about the tag: round-tripping is CLOSED UNDER A LEAK.** A written tag is also read back, so the identity still holds. ⚠ **Measured, not argued** — under the deliberate `[JsonIgnore]`-removal probe, round-trip **passed** while the recorded baseline reddened. ⇒ replaced by a **SHA-256 baseline of all 42 canonical serializations, captured on the pre-`U-9` tree** (`Snapshots/Golden/persistence-shape.txt`), plus round-trip **stability** kept for what it does prove |
| 🔴 **Revert** | ✅ cheap — no persisted change. Four inverse-edit probes, each red on the test that names it |
| 📐 **§1 asymmetry** | ⭐ **ruled (a)** — the three `ParameterDecl` lacks are editor-presentation, the drop is enumerated in `MembersAParameterDoesNotCarry`, reads return the documented default and **writes throw** |
| 📌 **Direction** | ⚠ built the **inverse** of *"old lists become views"*: the **tagged type is the view**, the three lists remain the storage. That is what keeps `U-9` internal — a new store would have needed write-through views anyway to survive `U-11`, and flipping the store is `U-10`/`U-12`'s job. ⭐ **`U-11` is unaffected:** consumers move onto `Declarations` either way |

### U-10 · Migrator pair + envelope 1→2 — D2 ⚠ *the risky one* — 🟠 **READER LANDED B54; WRITER BLOCKED**

#### ⛔⛔ Batch 54 — the writer cannot ship, and the reason is a build constraint

⭐ **The handoff's §1.1 offered a choice — keep Batch 49's sidestep, or solve `BP-235` here.
⛔ Measured: there is no choice.** Bumping `$meta.schemaVersion` to 2 forces three things at once:

| | |
|---|---|
| **1. `BlueprintMigrationModule.CurrentVersion` must move to 2** | ⛔ `PersistentMigrationAdapter` **Case D throws** when the disk version exceeds the registry's current version with no down-chain and no snapshot. ⭐ The handoff's §1.2 is right, and this is the mechanism |
| **2. A REAL 1→2 migrator must be registered, not a passthrough** | ⛔ `MigrationPipeline.MigrateTo` returns immediately for a passthrough type — **before any version comparison** — so a passthrough at 2 would silently treat a genuine v1 file as v2 |
| ⛔⛔ **3. …which cannot be written** | the registration lives in `BlueprintMigrationModule` (`Hrot.Common`) and the transform in `BlueprintSchemaV2` (`Hrot.Blueprints.Compiler`) — **and `Hrot.Blueprints.Compiler` already references `Hrot.Common`.** The reverse edge is a project-reference **cycle** |

⇒ ⭐ **The seam is a third assembly, or an injection point in `HrotMigrationBootstrap` that a host
supplies the migrator through** — a bootstrap shared by six host profiles. **Its own batch.**
⇒ ⚠ **`persistence-shape.txt` deliberately did NOT move.** The batch stopped exactly at its own stated
stop point: *"before bumping `$meta.schemaVersion`."*

#### ⭐ What DID land in Batch 54

| | |
|---|---|
| ⭐⭐ **The reader understands v2** | `BlueprintJsonServices.Deserialize` detects v2 and `Down`s it. **All 58 shipped assets load from their v2 form into the same model as from v1.** ⭐ **Reader-before-writer is the safe order and the reason the stop point sits where it does** — a v2 file is unreadable by any build predating this, so readers must ship first. This half is `git revert`-able; the bump is not |
| ⭐⭐ **`BP-240` asked of the migration — and it bit** | **4 of 9 constructed shapes were mishandled**, and the 58-file identity gate could see none of them, because every shipped file is canonical by construction. ⛔⛔ **The worst: a v1 declaration carrying its own `Kind` property overwrote the v2 tag, so `Down` partitioned it into the wrong list — measured, `Parameters` came back non-empty — moving a field between structs and changing its offset. A blackboard wipe from one stray property.** Also: an absent list and a `null` list were *invented* on the way back; lists out of model order moved the bytes |
| ⚖️ **Ruled refusals, not repairs** | `Up` now requires canonical v1 and names the reason. Repairing would mean carrying a v1 layout artefact into v2, or guessing at a list that is not there. ⚠ **Consequence filed as `BP-241`:** `--mode migrate` now has a failure mode with no way forward, and needs a canonicalise-first step |
| ⭐ **The two version numbers agree, and a test now says so** | `$meta.schemaVersion` and `BlueprintMigrationModule.CurrentVersion` are both **1**, asserted by `V2ReaderTests`. ⛔ `TheWriterStillEmitsV1` makes the stop point auditable — it reddens the moment anyone flips the writer |

---

### U-10 *(Batch 49 record)* — Migrator pair + envelope 1→2

| | |
|---|---|
| ✅ **The transform pair SHIPPED** | `BlueprintSchemaV2.Up` / `.Down`, and ⭐⭐ **`v1 → v2 → v1` is byte-identical for all 58 shipped assets** — *the gate `V1` recorded as unwritable, now written and run.* `U-15` is what made it writable |
| ⛔ **The WIRING is deferred** | nothing writes v2 and nothing reads it. **Two measured reasons, both discovered after the plan was written** |

#### ⛔⛔ Reason 1 — with `U-9` built inverse, `U-10`-before-`U-11` translates into a shape nothing uses

⭐ **Batch 48 built the tagged declaration as a VIEW; the three lists are still the storage.** ⇒ writing v2
today means converting three lists → one array on every save and one array → three lists on every load,
into a shape **no code in the process consumes**, for **zero present benefit** — while carrying
`Pass 3`, the highest-blast-radius gate in the programme (*a failure resets every deployed entity's
blackboard*).

⇒ 📐 **Ruling: `U-11` → `U-12` → `U-10`.** After `U-12` the storage **is** one tagged list, the on-disk
shape mirrors the in-memory shape, and the migrator becomes a thin mapping instead of a bidirectional
translation layer. ⚠ **This is the sequencing question §2 of the Batch 49 handoff invited** — *"this is
the one place in the plan where the sequencing was written before `U-9`'s direction was known."*

#### ⛔⛔ Reason 2 — the migration framework cannot reach the reader that must not break

📌 **Measured:** `BlueprintIncrementalGenerator` targets **`netstandard2.0`**, and
`Hrot.Blueprints.Compiler`'s `Fdp.Core` / `Hrot.Common` project references are **`net8.0`-only**.
⇒ `IJsonDocumentMigrator`, `JsonEnvelope` and `MigrationRegistry` are **unreachable from the one
production reader of every shipped asset**. ⚠ And the registry module (`BlueprintMigrationModule`)
lives in `Hrot.Common`, which **must not** reference the blueprint compiler — so the transform and the
registration cannot meet without either a duplicated transform or a new seam threaded through a
bootstrap shared by six host profiles.

⭐ **So the transform is a plain `System.Text.Json` DOM pair**, shared by both targets, and the registry
question is a decision for whoever cuts the seam — recorded as **`BP-235`**.

📌 ⚠ **There IS a production consumer**, contrary to a first reading: `Hrot.ClusterRunner --mode migrate`
walks **every `*.json`** and `BuildClusterRunnerMigrate` registers the blueprint doc type. ⇒ bumping
`$meta.schemaVersion` to 2 while `BlueprintMigrationModule.CurrentVersion` stays 1-passthrough would be
a **live inconsistency**, not a cosmetic one. That is the third reason the envelope bump waits.

#### The gates, restated against what landed

| | |
|---|---|
| ✅ **Pass 1 — DONE** | ⭐⭐ **`v1 → v2 → v1` is byte-identical for all 58.** *(`V1` had rewritten this as unwritable: 0 of 58 files survived even `Deserialize→Serialize`, because 41 of 42 were hand-authored 2-space-indented against `WriteIndented = false`. `U-15` fixed the premise.)* ⭐ **Proved to bite:** dropping the order lists in `Down`, and silently skipping one declaration, each redden it |
| ⛔ **Pass 2 — waits on the wiring** | a v1 file loads through the v2 reader. There is no v2 reader yet, deliberately |
| ⛔ **Pass 3 — waits on the wiring** | ⭐⭐ **`StructureHash` unchanged for every shipped asset** — the no-blackboard-wipe gate. Vacuous until something writes v2 |
| ✅ **Pass 4 — DONE in `U-15`** | the numeric `Dispatch` normalises to the string, asserted by `CorpusCanonicalisationTests`. ⛔ **`BP-227`'s count was wrong TWICE: ELEVEN files, not 7** — 4 corpus + **7** recipes; the recipes carry both `1` and `2` and only `1` was ever counted |
| ✅ **Revert — DONE** | ⛔ `git revert` does not undo a migration. ⭐ **`BlueprintSchemaV2.Down` IS the revert, and it shipped and is tested with `Up`** — which is the half of this task that was safe to land ahead of the wiring |

### U-11 · Consumers moved — D3 — 🟠 **COMPILER BUCKET LANDED (Batch 50)**

| | |
|---|---|
| ✅ **Pass** | golden unchanged **at every sub-step** — held after each of Stage4+V_, Stage2, Stage5, Stage0 |
| ⛔⛔ **The count was wrong, and by ~4×** | the plan says *"~34 semantic sites"*; the Batch 50 handoff's upper bound was *"46 non-test files"*. ⭐ **Measured: 233 raw references → 135 semantic CODE references across 24 files** (+20 in doc comments, 30 incidental — `EventDispatcherDecl.Parameters`, the `Blueprints.Editor.Variables` **namespace**, `VariableKind.WorkingState`, palette `Categories.Variables`) |
| ⭐⭐ **And ~31 of those 135 are NOT `U-11` at all** | they are on **`IrAsset`**, a *different type* whose same-named three lists are the **emitted field** lists. ⛔ **They set the struct offsets (Params @0, working @8, State @16) and feed `StructureHash`** — sweeping them would move the hash and wipe blackboards. ⇒ 📐 **the plan's *"lowering · emit"* buckets DO NOT EXIST for `U-11`**; `EmissionContext`, both emitters, `FieldLayout`, `StructureHashComputation`, `AiPrimitiveLowering`, `CSharpEmitter` and `WhenLowering_Instance` are all `IrAsset` and stay |
| ✅ **Bucket landed — the compiler** | `Stage0_Rehydrate` · `Stage2_Validate` · `Stage4_TypeResolve` · `Stage5_Schedule` · `V_VariableReferenceRules`. ⭐ **Two pairs of near-duplicate overloads collapsed into one method each** (`ResolveFieldTypes`, `BuildIrFields` — the latter had *byte-identical* bodies), which is the payoff `U-9` was for |
| ✅ **Bucket landed — the editor (Batch 51)** | `BlueprintDocumentFactory` · `NodePinSchema` · `BlueprintNodeModel` · `BlueprintPickerSources` · `BlueprintMyBlueprintModel` · `BlueprintGraphModel` · `ReadEqsResultNodeDrawer` · **`BlueprintVariableSchemaSource`**. ⭐ **54 semantic refs / 9 files** (handoff's raw count: ~50 / 8) |
| ⭐⭐ **The gate `U-12` bets on, now a TEST** | `ViewsAreUnreadTests` — **no site under `Hrot.Blueprints.Editor`, and none in the compiler stages, reads a declaration list.** 🔴 **Proved to fail** by reintroducing one read (reported by file and line), *and* asserts the pattern still matches a known read (`DeclarationList` itself) — ⛔ **a grep that matches nothing looks exactly like a grep that is green** |
| ⛔ **The window needed NOTHING** | ⚠ **a correction to the handoff's framing:** `BlueprintVariablesWindow` (line 377 on) has **zero** references to the three lists. All 24 in that file belonged to **`BlueprintVariableSchemaSource`**, the half that survives `U-16` — so the file's big count was never the window's, and nothing slated for deletion was rewritten |
| ⭐ **What the source's move bought** | every `_kind == VariableKind.Parameter` branch is gone. Two things fell out with them: `GetOrdered`'s **type-sniffing `GetId`** local, which returned `Guid.Empty` for anything that was neither decl type and would have collapsed every row onto one dictionary key; and `Resolve`'s **six hand-written arms**, which now read their priority from `DeclarationList.ResolutionOrder` rather than restating an ordering that must agree with the compiler's — ⛔ **two copies of that ordering is how `BP-226` happened** |
| 📌 **`DeclarationList.ReplaceAll(kind, items)` added** | the undo snapshot restore. ⚠ **Deliberately does NOT touch the display-order list**, unlike `Remove`: a snapshot restore puts back a state captured whole, and dropping ids would make undo lose the designer's ordering |
| 📌 **One declared behaviour change** | `BlueprintPickerSources.Query`'s no-filter branch returned the **live** `_asset.Variables` list; it is a materialised copy now, matching what its other two branches always returned |
| 📌 **Still on the raw lists, correctly** | the three **`*Order`** lists (display metadata, they survive the store flip — `U-12`'s call) and `BlueprintCompiler`'s six-line **storage copy** |
| 📌 **Stays until `U-12`** | `BlueprintCompiler`'s six-line **storage copy** of the three lists + three orders. ⭐ It builds an asset's storage, which is exactly what does not move until the store flips |
| ⚠⚠ **The trap this bucket hit twice** | three sites read **Variables ∪ WorkingState only** (`Stage0.ResolveAnyDecl`, `Stage2.ResolveAnyDecl`, `Stage5`×2). ⛔ **`Declarations.ById()` also searches `Parameters`** — using it would resolve a parameter id where the site never did. ⭐ Written out explicitly with a comment at each, rather than taking the tidier call |
| 📌 **One declared behaviour widening** | merging `Stage4.ResolveFieldTypes` applies the `BP1504` fixed-list check to every kind, where it was on the `VariableDecl` overload only. ⭐ **Safe for a reason found upstream: `Stage2`'s `BP1507` already refuses a fixed-list `Parameter`**, so the widened arm is unreachable for a compile that gets there — and independently measured as a corpus no-op (Capacity > 0: Parameters **0** · WorkingState **0** · Variables **1**) |
| ⚠ | **`BlueprintVariablesWindow` was NOT rewritten** — `U-16` deletes it |

### U-12 · Rails restated; views deleted — D4

| | |
|---|---|
| ✅ **Pass 1** | `BP1024` is gone — an AiPrimitive with `(State, Asset)` entries compiles |
| ✅ **Pass 2** | `BP1031` split — an Instance with an `Input` entry ⇒ diagnostic |
| ✅ **Pass 3** | `BP1011` restated — a Library with **any** `Asset`-scope entry ⇒ diagnostic |
| ✅ **Pass 4** | golden unchanged · **grep asserts the old views are gone** |
| 🆕 ⭐⭐ **Pass 5 — the rail the four passes missed** | ⛔ **`BP1024` and `BP1031`'s `WorkingState` half were silently ALSO keeping cross-kind name collisions unreachable.** `Stage5.FindVariableRef` resolves by **priority across kinds** and falls back to matching **by name** — the path hand-authored assets take — so once the mixture is legal, two declarations sharing a name bind to whichever kind the order reaches first, **silently**. ⚠ `U-3`/`VariableRef` fixes the *emission* half, not *which declaration Stage 5 picks*; `U-14` closes only the **editor's** auto-namer, which a hand-authored `.bp.json` never touches; and Stage 2 had **no** duplicate-name rule at all (grepped). ⇒ **`BP1673`**, `OrdinalIgnoreCase`, cross-kind **and** same-kind, graph locals deliberately excluded (`Q27-C1` shadowing stays legal) *— Batch 52* |

#### ⭐ Batch 52 — what landed, what did not

| | |
|---|---|
| ✅ **Passes 1–3 + the new rail** | `BP1024` retired (kept defined, listed `RETIRED` in the coverage ratchet) · `BP1031` narrowed to the `(Input, Asset)` half · `BP1011` widened to `Declarations.Count > 0` · `BP1673` added |
| ⭐⭐ **Measured, not assumed** | across **all 58** shipped assets: **0** AiPrimitives carry a `Variable`, **0** Instances carry a `Parameter` or `WorkingState`, and the **3** Library assets declare **nothing**. ⇒ all three restatements are **corpus-neutral by construction**, which is why golden Tier 1 + Tier 2 are unchanged |
| ⭐ **"Asset scope" needed no new vocabulary** | all three of `BlueprintAsset`'s lists ARE the `Asset` scope; graph locals live on `Graph`. So *"any `Asset`-scope entry"* is `Declarations.Count > 0` and *"an `Input` entry"* is `CountIn(Parameter) > 0` |
| ⚠ **One reading to confirm** | *"`BP1031` **split**"* is implemented as **one surviving arm**, not two codes — the gate names a single condition, and a code whose only other arm is deleted is a narrowing, not a split |
| ✅ **The STORE FLIP landed — Batch 53** | `BlueprintAsset.DeclarationStore` is one `List<BlueprintDeclaration>`, kept grouped in `KindOrder`; the three properties are **live `DeclarationView<T>` windows** onto its runs. ⭐⭐ **`persistence-shape.txt` unchanged** — the store moved, the bytes did not |

#### ⭐ Batch 53 — the flip

| | |
|---|---|
| ⚖️ **§1's ruling: the three properties SURVIVE as public members** | ⛔ **the handoff's premise is true only of the two directories `ViewsAreUnreadTests` scans.** Measured with the compiler as oracle: **431** sites, ~100 files, **~400 in the test tree**. ⭐ Keeping them is also what makes the flip verifiable — those ~400 assertions were written by earlier batches against the old storage and are untouched by this one |
| ⭐⭐ **Zero call-site churn** | the property type is a concrete `DeclarationView<T>` with a parameterless ctor (**112** sites write `= new()`, which an interface cannot satisfy), an implicit conversion from `List<T>` (**~7** sites), and `AddRange` (**3** sites). **83** mutation sites forced the window to be **live** — three `List<T>` snapshots would have made `asset.Variables.Add(v)` a silent no-op |
| ⭐ **What the old arrangement was holding shut** | **reference identity of a list.** `BlueprintCompiler`'s copy shared the caller's actual `List` objects; it now copies the store's entries ⇒ **`U-2`/`BP-229`'s guarantee extends from graphs to declarations**. ⚠ Verified safe first: no compiler stage structurally mutates declarations |
| 🔴🔴 **The probe that mattered** | making the store `public` ⇒ **`persistence-shape` RED, golden green 131/131** — the handoff's point proved: golden cannot see a persistence-only regression |
| ⛔⛔ **The probe that LIED, and the finding** | breaking the grouping invariant (`ReplaceWith` appending instead of inserting at the kind's run) left **both** gates green — because deserialization sets the properties in `Parameters, WorkingState, Variables`, which is already `KindOrder`. ⇒ the invariant the whole design rests on was **unguarded**. ⭐ `StoreFlipTests` now drives the paths the corpus cannot (reverse-order assignment, interleaved `Add`) and reddens under it |

### U-13 · Shared-state read-only view — `Q-i`

| | |
|---|---|
| ✅ **Pass** | the view lists **exactly** the slot names referenced by `Get`/`SetShared` in the asset — asserted against the **8 shipped assets** and their known counts (58 `"state"`, 3 `"rally"`) |

### U-14 · `MakeUniqueName` across all kinds — `BP-232`

| | |
|---|---|
| ✅ **Pass** | creating a `Variable` named `Health` when a `Parameter` `Health` exists is refused |
| 📌 | trivial **after** `U-9`; awkward before, which is why it is sequenced there |

### U-15 🆕 · Canonicalise the corpus — ✅ **LANDED, Batch 49**

| | |
|---|---|
| ✅ **Done** | all **58** managed assets re-serialized through `BlueprintJsonServices` in one commit |
| ⭐ **Pass 1 — ✅** | ⭐⭐ **a semantic NO-OP, proved by the golden harness** — Tier 1 **and** Tier 2 unchanged for all 42 |
| ✅ **Pass 2 — ✅** | idempotent, and now a **standing gate**: `EveryManagedAssetIsAlreadyCanonical` keeps the corpus from drifting back |
| ✅ **Pass 3 — ✅** | `BP-227` closed. ⛔ **The count was wrong TWICE — ELEVEN, not 7** (4 corpus + **7** recipes; only `Dispatch: 1` was ever counted, and the recipes also carry `2`) |
| 📐 **Scope — ruled 42 + 16** | the corpus **and** the recipes. ⛔ **Not the 41 fixtures** — several are deliberately malformed and a fixture's bytes are frequently the thing under test. ⭐ **What proves the 16 unguarded recipes safe:** the same pre-flight that proved the 42, plus `RecipeIntegrityTests` and `DiscoverRecipesTests`, which are the recipes' own gates |
| ⭐⭐ **Run BEFORE rewriting: what would be DELETED** | canonicalising round-trips through the model, so anything the model does not carry dies in 58 files at once. **Measured: exactly two paths, `Header.SubsystemType` and `Header.SchemaVersion`, in 44 files** — both deliberately removed from the model by `D-021` and superseded by the `$meta` envelope, which **all 58 files already carry** (asserted, not assumed). Listed as declared exceptions so any *other* path still reddens |
| 📐 **The canonical form is INDENTED** | ⛔ **the compact form makes each asset a single 3–12 KB line.** 57 of 58 files were already indented; the corpus is this programme's baseline and every future change would be a whole-file diff; and it was **already a live defect** — `SaveActiveBlueprintCommand` writes through `Serialize`, so opening a hand-authored asset and saving it **collapsed the file**. `Loco1.bp.json` — the one compact corpus asset — is what that looks like |
| ⚠⚠ **`ToJsonString()` was ignoring `WriteIndented` entirely** | it takes **its own** options. The flag has been set on `_options` since the envelope landed and has had **no effect on net8**, the only target that writes files in production. Both halves are set now |
| 📌 **All properties stay explicit** | omitting nulls/defaults would shrink files a further ~30%, but a global `WhenWritingDefault` **drops `Dispatch` from every `Library` asset** (it is enum 0), and each ignore condition is a new way for a value to vanish. **+20% on disk** is the price of not adding one |
| ⚠ **Cost paid** | 57 test cases across **5** methods asserted **compact** JSON substrings (`"kind":"When"`). ⭐ Fixed by reading the discriminator from the **DOM**, not by re-coupling them to the new spelling. ⛔ One test deleted a property by string-replacing its compact spelling — it would have silently deleted **nothing** and then asserted about an unmodified document |

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
| ~~48~~ | ✅ **`U-9` — DONE.** ⚠ **Built inverse: the tagged type is the VIEW, the lists stay the storage.** 🔴 **The plan's Pass 3 was refuted by probe** — a round-trip cannot see a leaked tag; replaced by a SHA-256 persistence baseline | ⭐ **the model change, alone.** Golden must not move · ⚠ **its serializer must keep writing the OLD three-list shape** — the tag must not reach JSON until `U-10`, or `U-9` and `U-10` collapse into one |
| ~~49~~ | ✅ **`U-15` DONE + `U-10` HALF.** ⭐ **Golden Tier 1 and Tier 2 unmoved across 58 rewritten files.** `BP-227` closed, `BP-235` filed. ⛔ **`U-10`'s wiring deferred and re-sequenced after `U-12`** | ⭐ **canonicalise, then migrate.** ⚠ **the only batch whose revert is code it ships** |
| ~~50~~ | ✅ **`U-14` DONE (`BP-232` closed) + `U-11`'s COMPILER bucket.** ⭐ **The site count was wrong by ~4× and two buckets were deleted** — see `U-11`. `BP-236` filed and fixed | ⭐ **one batch, TWO sub-steps** (compiler buckets · editor remainder) — the review's ruling; `U-4`/`U-5` already rewrote most of the scary file |
| ~~51~~ | ✅ **`U-11` editor bucket — DONE, `U-11` COMPLETE.** ⭐⭐ **`ViewsAreUnreadTests` proves nothing reads the views.** 🔴 **But the suite went red — two `PdbEmbeddedSourceTests`, order-dependent, not caused by this batch** | ⭐ **~50 refs / 8 files.** ⛔ **`U-12` cannot delete the views until a grep says nothing reads them** |
| ~~52~~ | ✅ **the red gate FIXED (`BP1672`) + `U-12`'s RAILS.** ⭐⭐ **`BP1673` — retiring `BP1024`/`BP1031` uncovered a defect they were silently holding shut.** ⛔ **Store flip NOT done** | ⛔ **§1 first: the suite must be green before a store flip is verified.** Then the rails **and the store flip**, once nothing reads the old views. ⚖️ **kept alone: three rail restatements + a store flip is its own revert story** |
| ~~53~~ | ✅ **`U-12`'s STORE FLIP — DONE, `U-12` CLOSED.** ⭐⭐ **`persistence-shape.txt` unchanged.** The three properties survive as **live windows** (`DeclarationView<T>`), type chosen by compiler-as-oracle across 431 sites. ➕ **`BP-240`** |
| ~~54~~ | ✅ **`U-10`'s READER + 4 corpus-invisible defects fixed.** ⛔⛔ **The WRITER is BLOCKED by `BP-235` — a project-reference cycle. See [Architect_Question_31](Architect_Question_31_Migration_Seam.md)** · ➕ `BP-241`. *(was: `U-10`'s WIRING* — write + read v2, bump `$meta.schemaVersion` | ⚠⚠ **Re-sequenced Batch 49, measured.** Only after `U-12` does the on-disk shape mirror an in-memory shape that exists, ⇒ the migrator becomes **a thin mapping** rather than a three-lists ⇄ one-array conversion into a shape nothing consumes. 🔴 **`BP-235` (the netstandard2.0 wall) and `ClusterRunner --mode migrate` are both live here** |

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
