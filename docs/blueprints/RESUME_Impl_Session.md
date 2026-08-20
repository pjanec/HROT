# RESUME — implementation session · ⭐⭐ **the `U-` sequence has started**

> **Written for a fresh session. Self-contained; assumes no prior conversation.**
> **You are the *implementation* session.** A separate *coordinator* session owns the tracker and
> writes the handoffs. Last updated **2026-08-14** (Batch 53).
>
> ✅ **Batch 53 is COMPLETE. ⭐⭐ `U-12` IS DONE — the store is flipped.** `BlueprintAsset` holds ONE
> `List<BlueprintDeclaration>`; `Parameters`/`WorkingState`/`Variables` are **live windows** onto its
> runs. ⭐⭐ **`persistence-shape.txt` unchanged: the store moved, the bytes did not.**
> ⚖️ **Ruled: the three properties SURVIVE as public members** — ~400 of the 431 measured call sites
> are test assertions, and they are what makes the flip verifiable. See §0.
>
> ✅ **Batch 52 is COMPLETE.** ⭐⭐ **§1 — the compiler stopped lying about the PDB (`BP1672`) and the
> suite stopped depending on assembly load order.** ⛔ **The handoff's headline was wrong against the
> code: the Blueprints gate was NOT red** — the full suite is green at `d2cde7c`; a *filtered* run is
> red, and that difference IS the defect. ✅ **§2 — `U-12`'s three RAILS landed** (`BP1024` retired ·
> `BP1031` split · `BP1011` restated) **plus `BP1673`**, the rail their removal makes necessary.
> ⛔ **The STORE FLIP is deliberately NOT done** — see §1.
>
> ✅ **Batch 51 is COMPLETE and reported.** ⭐⭐ **`U-11` IS DONE** — the editor bucket landed, and
> `ViewsAreUnreadTests` turns *"nothing reads the views"* from a belief into a **checked fact**.
> ⇒ ⭐ **`U-12` is unblocked.**
> ✅ Batch 50: `U-14` (**`BP-232` closed**) **+ `U-11`'s COMPILER bucket**; `U-11`'s size was
> wrong by ~4× (see §2). `BP-236` found and fixed while running the gates.
> ✅ Batch 49: `U-15` (**the corpus is canonicalised** — `BP-227` closed)
> **+ the `U-10` transform pair**, with ⭐⭐ **`v1 → v2 → v1` byte-identical on all 58** — the gate the
> plan had recorded as *unwritable*. ⛔ **`U-10`'s WIRING is deferred and re-sequenced after
> `U-11`/`U-12`** — see §1. `BP-235` filed.
> ✅ Batch 48: `U-9` — the **tagged declaration** (`D1`), the model change
> the whole `D` programme rests on. ⭐ Built **inverse** to the plan's wording: the tagged type is the
> **view**, the three lists stay the **storage**, so `U-9` is entirely internal and its revert is cheap.
> ✅ Batch 47: `U-7` (the type-existence rail, **`BP1671`**) + `U-8`
> (the type-choice union) — **`BP-228`** closed and stage B′ unblocked.
> ✅ Batch 46: `U-4` + `U-5` — **`BP-230`**, **`BP-231`** closed.
> ✅ Batch 45: `U-3` — the variable index carries its **kind**, closing **`BP-226`**.
> ⭐ **Golden Pass 1 has now held unchanged across `U-2`, `U-3`, `U-4` and `U-5`** — what `U-1` was for.
> ✅ Batch 44: `U-1` (the golden-corpus harness) + `U-2` (`BP-229`). All eight gates green.
> ⭐ **`BP-57` closed in Batch 43**; the locals feature is done end to end, compiler and editor.

| | |
|---|---|
| **Repo** | `pjanec/HROT` |
| **Implementation branch — PUSH HERE** | ⭐ **`claude/hrot-implementation-j1jvin`** |
| **Coordinator branch — do NOT push** | ⭐ **`claude/blueprint-authoring-status-gm0akp`** (was at `a4842db`, merged into mine) |
| **Last handoff** | 📄 **[HANDOFF_Batch55_Schema_Assembly_And_Registry.md](HANDOFF_Batch55_Schema_Assembly_And_Registry.md)** — all three steps delivered; ⚖️ **`--canonicalise` split out per §3.5.2** |
| **Counts** | **60 open · 117 done** — ⚠ *derive, never hand-count:* `python3 scripts/tracker-counts.py --check` |
| **Next free ids** | rows **BP-243+** · diagnostics **BP1674+** — ⭐ **Batch 55 allocated `BP-242` and NO diagnostic** |

⛔ **No PR unless the user explicitly asks.** There has never been one in this programme.
⛔ **Never put a model identifier** in a commit message, code comment, or anything else pushed.

---

## 0 · First actions, in this order

```bash
git fetch origin claude/blueprint-authoring-status-gm0akp
git merge origin/claude/blueprint-authoring-status-gm0akp --no-edit   # rule 7
python3 scripts/tracker-counts.py --check                              # expect 57 / 114
```

Then read whatever handoff is newest on that branch. **No batch is in flight.**

### ⏭ What comes next

⭐⭐ **`U-12`** — and it is **unblocked as a checked fact**, not an assumption (`ViewsAreUnreadTests`).
It carries **three rail restatements** (`BP1024` gone · `BP1031` split · `BP1011` restated) **and the
store flip**; ⚠ two very different revert stories, so consider splitting them.
📌 **Then `U-10`'s wiring**, which Batch 49 re-sequenced to run after `U-12`.
🟢 **`U-13`** is independent — but it needs the visual check, like `U-6`/`U-16`.

⛔⛔ **`U-6` / `U-13` / `U-16` still hard-require the VISUAL CHECK**, which has now not run for
**fourteen batches**. They are a Details table, a read-only view and deleting a whole window — exactly
the shape a headless test passes while the panel draws nothing. **Say so; never imply coverage.**

---

## 0 · Batch 55 — the seam, the migrator, and THE BUMP · ⭐⭐ `U-10` closed

### 0.1 🔴🔴 `StructureHash` unchanged for all 42 — stated first

Golden **131/131**, Tier 1 **and** Tier 2 unchanged. ⭐ **The on-disk shape moved and the compiled
output did not — that separation is the whole claim of the bump**, and Tier 1 records `StructureHash`
per asset, so this is a measurement rather than an inference.

### 0.2 ⭐ The `persistence-shape.txt` diff, reviewed

| | |
|---|---|
| **42 assets, same set** | ⛔ an asset appearing or vanishing would be a different defect; asserted |
| **all 42 hashes moved** | 21 grew · 21 shrank · **0** unchanged in size |
| **total +1 443 bytes (+0.19 %)** | 771 936 → 773 379 |
| ⭐ **the sign correlates with declaration count** | **0 declarations ⇒ always −39** (three empty lists + two order lines collapse into one empty array); **≥6 ⇒ always grows** (~28 B per `"Kind"` line). Extremes check arithmetically: 19 declarations ⇒ +499 ≈ 19×28 − 39 |

⇒ 📌 **That correlation is what makes it *reviewed* rather than regenerated.**

### 0.3 ⛔⛔ `BP-242` — a SEVENTH reader, and a Batch 54 claim of mine was wrong

⭐ **The Generators gate dropped 193 → 192.** `GeneratedBlueprintSchemaCatalog`
(`Hrot.AiEditor.Generators`) parses `*.bp.json` with **its own `JsonDocument` code**, not
`BlueprintJsonServices`.

⛔ **And it does not fail — it returns a WRONG answer.** Its own doc says *"malformed files are
silently skipped (never throws)"*; in fact it looked for a top-level `parameters` array, did not find
one in a v2 file, and returned a schema with **zero parameters**. `StructSizeResolver` then sized the
`Params` struct wrong and a composed BTree wrote shared state with the wrong slot count —
`TotalSlots` **3 instead of 10**.

⚠⚠ **It invalidates what I asserted in Batch 54:** *"all six production read sites funnel through
`BlueprintJsonServices.Deserialize`."* ⭐ **That was measured by grepping for that method — which by
construction cannot find a reader that does not call it.** A search for *callers of X* answers a
different question from *who reads this file*.

✅ v2 blindness fixed (the catalog reads `Declarations` filtered by `Kind`, using the schema
assembly's own constants — ⭐ **exactly what step 1's BCL-only leaf makes possible from a
netstandard2.0 generator**). ⛔ The silent-wrong-answer behaviour is filed, not fixed.

### 0.4 ⭐ Two stop markers INVERTED, not deleted

| test | was | now |
|---|---|---|
| `V2ReaderTests.TheWriterStillEmitsV1` | Batch 54's proof that the stop was deliberate | ⭐ **`TheWriterNowEmitsV2`** |
| `PersistenceShapeTests.TheTagIsNotSerializable` | `U-9`'s proof the tag never reached JSON | ⭐ **`TheTagIsTheFormatNowAndTheModelsViewIsStillNotSerialized`** |

⭐⭐ **The second one's `[JsonIgnore]` half did NOT flip and is now load-bearing in a louder way:**
`Serialize` builds a v1 DOM first, so if the model's union view were serializable that DOM would
already contain `Declarations`, `IsV2` would report true, and **`Up` would throw**.

### 0.5 📌 Corrections and what was left out

| | |
|---|---|
| ⛔ **`JM-P3-003` carries no acceptance criteria for this** | the handoff hoped it might. Measured: it is **closed**, it was **Scenario's** bump, and it mentions Blueprint **zero times**. `BlueprintMigrationModule`'s comment was a stale forward-reference |
| ⚖️ **Is the Scenario precedent as close as it reads?** | ⛔ **No — and the handoff was right to ask.** Scenario's bump added **one optional field to one type** and its down-migrator is **lossy by design**. This moves **every declaration in every asset** and is **lossless both ways**. ⭐ The precedent is real for the *mechanism* — `RegisterDocType`, the migrator layout, the three named tests — and says nothing about the *blast radius* |
| ⚖️ **`--canonicalise` (`Q31-C2`) SPLIT OUT** | per §3.5.2's own option. A doc-type-agnostic tool needs a per-doc-type repair seam; hardcoding blueprint knowledge into `MigrateMode` is the half-doing the handoff warned against. ⭐ `C1` is pinned by a new test instead |
| ⭐ **Three suites joined the gate set with PRE-EXISTING failures** | measured at `70c2a87` in a worktree and unchanged after: Hrot.Common **53/3 failed**, ClusterRunner **249/2 failed** (252/2 with mine). ⛔ **Recording them as clean would have been wrong** |
| ⛔⛔ **And SimHost is not merely failing — it is FLAKY** | **1, 3 and 8 failures across runs of the same binaries**, worst under parallel load. ⚠ **My own baseline number for it was a single sample**, which is the same mistake in miniature that `BP-240` warns about. The constant failure is `BranchedRecording_CapturesHistoricalStateAsKeyframe`; the variable ones are an `Extract_*` family sharing state. ⭐ **None touch blueprints** — worth a row of its own before this suite is trusted as a gate |

### 0.6 ⚠ The analyzer-asset trap, which failed loudly

A `ProjectReference` to the new assembly was **not** enough: the generator runs inside Roslyn's
analyzer load context, which resolves only what is shipped as an `Analyzer` item. Every asset failed
`BP0002 — Could not load file or assembly 'Hrot.Blueprints.Schema'`. ⭐ **Fixed by listing it beside
`.Compiler` in `Hrot.AI.Behaviors`, the one project that ships it that way** — and that the generator
then parsed all 42 corpus assets is the proof the netstandard2.0 target genuinely resolves it.

---

## 0 · Batch 54 — the v2 reader · ⛔ the writer is blocked by a cycle

### 0.1 ⛔⛔ Why `persistence-shape.txt` did NOT move

⭐ **The handoff called this *"the ONLY batch where `persistence-shape` is ALLOWED to move"* and
offered a choice on `BP-235`. ⛔ Measured: there is no choice.** Bumping `$meta.schemaVersion` forces
three things, and the third is impossible:

| | |
|---|---|
| **1.** `BlueprintMigrationModule.CurrentVersion` must move to 2 | ⛔ `PersistentMigrationAdapter` **Case D throws** when the disk version exceeds the registry's, with no down-chain and no snapshot |
| **2.** a **real** 1→2 migrator must be registered, not a passthrough | ⛔ `MigrationPipeline.MigrateTo` returns immediately for a passthrough type, **before any version check** ⇒ a passthrough at 2 silently treats a genuine v1 file as v2 |
| ⛔⛔ **3.** …which cannot be written | registration is in `Hrot.Common`; the transform is in `Hrot.Blueprints.Compiler` — **which already references `Hrot.Common`.** The reverse edge is a **project-reference cycle** |

⇒ ⭐ **The seam is a third assembly, or an injection point in `HrotMigrationBootstrap`** (shared by six
host profiles). ⚠ **Its own batch.** The stop point the handoff itself named is exactly right.

### 0.2 ⭐⭐ What landed — the reader, and why that is the safe half

`BlueprintJsonServices.Deserialize` detects v2 and `Down`s it. **All 58 shipped assets load from their
v2 form into the same model as from v1.**

⭐ **Reader-before-writer is not a half-measure, it is the correct order** — a v2 file is unreadable by
any build predating the reader, so readers must ship first. ⇒ this half is `git revert`-able; the bump
is not, because a migrated file stays migrated. **That is the whole reason the stop point sits here.**

⭐ `V2ReaderTests.TheWriterStillEmitsV1` makes the stop auditable: it reddens the moment anyone flips
the writer. `TheStampedVersionAgreesWithTheMigrationRegistry` pins the two numbers together.

### 0.3 ⭐⭐ `BP-240` asked of the migration — and it bit

**9 constructed fixtures; 4 were mishandled, and the 58-file identity gate could see none of them**,
because every shipped file is canonical *by construction*.

| shape | what happened |
|---|---|
| ⛔⛔ **a declaration carrying its own `Kind`** | `Up` writes the tag, then copies the declaration's members **over it** ⇒ the file's value wins, and `Down` partitions into the **wrong list**. ⭐ **Measured, not reasoned: `Parameters` came back non-empty for a declaration authored in `Variables`.** That is a field moving between structs — **a blackboard wipe from one stray property** |
| **an absent declaration list** | `Up` skips it, `Down` always emits all three ⇒ the round trip **invented** the property |
| **a `null` declaration list** | same asymmetry — `null` and absent are indistinguishable to `Up` |
| ⭐ **the three lists out of model order** | `BP-240`'s shape at the file level: `Up` collapses at the **first** list's slot, `Down` restores in **model** order ⇒ the bytes move |

✅ **Survived already**, and worth knowing: zero declarations of every kind · a stale id in an `*Order`
list · a cross-kind name collision (the shape `BP1673` refuses — ⭐ **the migrator must still read it,
or it cannot be used to fix the assets that do not compile**) · an unknown property on a declaration.

⚖️ **Ruled REFUSALS, not repairs.** Repairing would mean carrying a v1 layout artefact into v2, or
guessing at a list that is not there. ⚠ **Consequence filed as `BP-241`:** `--mode migrate` now has a
failure mode with no way forward and needs a canonicalise-first step.

---

## 0 · Batch 53 — the store flip · ⭐⭐ `U-12` closed

### 0.1 What the model looks like now

```
BlueprintAsset
 └─ internal List<BlueprintDeclaration> DeclarationStore    ⭐ THE STORE — one list
      grouped in KindOrder:  [ Parameter… | WorkingState… | Variable… ]
                                  ↑             ↑              ↑
        Parameters ───────────────┘             │              │      each a LIVE
        WorkingState ───────────────────────────┘              │      DeclarationView<T>
        Variables ─────────────────────────────────────────────┘      onto its own run
```

⭐ **`Declarations` (`DeclarationList`) is unchanged from the caller's side** — under `U-9` it was a
view over three lists, it is now a view over the store. ⚠ **That symmetry is why `U-9` could be built
inverse in Batch 48 and paid for here.**

### 0.2 ⚖️ §1's ruling — the three properties SURVIVE, and why

⛔ **The handoff's premise was that `ViewsAreUnreadTests` licenses deleting them.** That test scans
`Hrot.Blueprints.Editor` and `Compiler/Stages` **only**.

📌 **Measured with the compiler as the oracle** — `[Obsolete]` on all three, one solution build:

| | |
|---|---|
| **431** distinct sites, ~100 files | ⭐ **~400 of them are test assertions** |
| **172** object-initializers · **112** of them `= new()` | ⛔ **rules out `IList<T>`** |
| **83** mutation sites | ⇒ the window must be **LIVE**, not a snapshot |
| **3** `List<T>`-only calls (all `AddRange`) · **0** `List<T>` locals | ⭐ nothing pins the concrete type |

⇒ 📐 **`DeclarationView<T>`** — concrete, parameterless ctor, implicit conversion from `List<T>`,
`AddRange`. ⭐⭐ **ZERO call-site churn**, which is what leaves those ~400 assertions free to act as an
independent check on the change rather than being rewritten by it.

⚠⚠ **The tempting cheap flip is trap #5:** three `List<T>` snapshots rebuilt per `get` compile fine
everywhere and make `asset.Variables.Add(v)` a silent no-op.

### 0.3 ⭐ What the old arrangement was silently holding shut

⭐⭐ **Reference identity of a list.** `BlueprintCompiler`'s copy shared the caller's actual `List`
objects; it now copies the store's **entries**. ⇒ **`U-2`/`BP-229`'s guarantee extends from graphs to
declarations** — a stage that added one can no longer reach the designer's asset.
⚠ **Verified before relying on it:** no compiler stage structurally mutates declarations; every
`Add`/`Remove`/`ReplaceAll` is in the editor. ⚠ The declaration **objects** stay shared on purpose —
Stage 4 writes resolved types back through them.

📌 Also moved, and restated rather than patched: `AtLocal` used to allocate a **fresh facade per
read**. `TaggedDeclarationTests` asserted `NotSame` on two reads — a test of the *mechanism*. It now
asserts the *rule* against its one live production caller (`BlueprintDocumentFactory` removing by a
facade it constructs itself).

### 0.4 🔴🔴 The two revert probes — and the one that lied

| probe | `persistence-shape` | golden |
|---|---|---|
| ⭐⭐ **store made `public`** *(the likely mistake: "it's the model now")* | 🔴 **RED** | ✅ green **131/131** |
| ⛔ **grouping invariant broken** | ✅ green | ✅ green |

⭐ **Row 1 answers the handoff's question** — *which gate catches the mistake you are most likely to
make?* Golden cannot see a persistence-only regression; the baseline can, and only it can.

⛔⛔ **Row 2 is the finding, filed as `BP-240`.** The invariant the entire design rests on was
**unguarded**, because deserialization sets the three properties in `Parameters, WorkingState,
Variables` — which *is* `KindOrder`. ⇒ appending and inserting agree on exactly the path the 42-asset
corpus exercises **and on no other**. ⭐ `StoreFlipTests` drives what the corpus cannot: reverse-order
assignment and interleaved `Add`. It reddens under that probe.

📌 **A green revert probe is a finding about the tests. Never evidence the code was fine.**

### 0.5 ⭐ The order-dependency sweep, re-run after the flip

**370 classes, each run alone: 0 findings.** ⭐ Batch 52's `TestAssemblyModuleInit` still holds, and the
store flip introduced no new order dependence — down from **2** at the Batch-52 baseline.
⚠ **Still class granularity, which under-reports** (`Stage8Tests` was green per-class and red
per-test). Not extended to per-test this batch: ~5 h for 3538 tests. `scripts/order-dependency-sweep.sh`.

---

## 0′ · Batch 52 — the red gate, and `U-12`'s rails

### 0.1 ⛔ The handoff's headline was wrong, and the correction matters

| claim | measured |
|---|---|
| *"the Blueprints gate is RED — 3506 passed / 2 FAILED"* | ⛔ **The full suite is GREEN**: 3518 total / 3508 passed / **0 failed** at `d2cde7c`. The coordinator's number is an **isolated `--filter` run** reported as the full-suite figure |
| *"`ViewsAreUnreadTests` changed the suite's composition enough to break the accident"* | ⛔ **It did not.** Batch 51 changed nothing here; the accident still holds in a full run |

⇒ ⭐⭐ **The real shape is stronger than the reported one:** the suite is green *by accident*, and only
a filtered run exposes it. That is the §1.4 class, not a regression.

### 0.2 §1b — `BP1672`, and the second trap behind it

⛔ `Compile` with `EmitPdbWithEmbeddedSource: true` and no `RoslynFinalizer` returned
`Succeeded == true`, both byte arrays null, **diagnostic list empty**. Now a **precondition**, checked
before Stage 0 and reported alone — the finalizer's absence is a fact about the *host process*, not
about the asset, so it does not belong interleaved with content diagnostics.

⭐ **And the same trap one step deeper went with it:** when the finalizer reported Roslyn errors into
the sink, `pe`/`pdb` stayed null and the method **fell through to `Succeeded: true`** — alone among
the eight stages, every one of which ends `if (sink.HasErrors) return FailResult(...)`.

⭐⭐ **What made an error severity safe:** `QuickReloadService` asked for the PDB *"for debugger
support"* and **never read it**. Measured tree-wide — `PortablePe`/`PortablePdb` have **no production
reader at all**; the debugger support comes from `TriggerFromSourcesAsync`, which Roslyn-compiles the
same source a **second** time. Dropping the request removes a duplicated full Roslyn compilation from
the editor's hot path *and* leaves `BP1672` with no production caller to break. **`BP-239`** carries
the open question: is the option a real capability, or a test-only path?

### 0.3 §1a + §1.4 — the load-order class, retired centrally

`TestAssemblyModuleInit` runs the module ctors of **`Hrot.Blueprints.Core`**, **`Hrot.AI.Behaviors`**
and **`Fhsm.Kernel`** before any test. Five ad-hoc preloads had accumulated, one per class already
caught; ⭐ **they stay, annotated** — removing a guard because a broader one exists is only safe when
the broader one fails *loudly*, and this class fails silently.

⭐ **The sweep is now an instrument, not a one-off:** `scripts/order-dependency-sweep.sh` runs all
**370** classes alone against a green suite. It found **two**: `PdbEmbeddedSourceTests` and
⭐ **`HsmInvokeHelpersTests`** (`BP-238`, new) — whose generated HSM registrar failed **`CS0400`**
because Roslyn's *reference set* is built from loaded assemblies.

| ⛔⛔ **Class granularity UNDER-REPORTS** | `Stage8Tests.Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` is **green per-class, red per-test** — a sibling in its own class loaded the assembly first. Per-test costs ~5 h for 3518 tests |
|---|---|

### 0.4 ⛔⛔ The revert probe that lied — twice

| attempt | result |
|---|---|
| early `return` inside `Initialize()` | ⭐ **stayed green** |
| `throw` inside `Initialize()` | ⭐ probe **provably reached**, tests **still green** |
| remove `[ModuleInitializer]` | ✅ **all four isolated filters go red** |

⇒ ⭐⭐ **The `typeof(...)` arguments load their assemblies when the JIT compiles the method body,
before a single statement executes.** The body does its work merely by existing. ⛔ **A runtime
short-circuit is not an inverse for this file** — and a probe that stays green is a finding about the
probe, not evidence the fix was unnecessary.
📌 One more: a `python` replace of `[ModuleInitializer]` hit the **doc comment's** first occurrence,
so an earlier "probe" never applied at all. **Verify a probe took effect before reading its result.**

### 0.5 §2 — `U-12`'s rails (⛔ NOT the store flip)

| | |
|---|---|
| **`BP1024`** | ⛔ **retired** — it refused an AiPrimitive declaring a `Variable`, but `Variable` and `WorkingState` are the **same cell**, `(State, Asset)`. It enforced a spelling |
| **`BP1031`** | ⭐ **split** — the `WorkingState` half was the same spelling rule and is gone; the `Parameter` half is real (`(Input, Asset)`: nothing supplies it at spawn) and keeps the code |
| **`BP1011`** | ⭐ **restated** to `Declarations.Count > 0`. ⭐ *"Asset scope"* needed **no new vocabulary** — all three lists ARE that scope; graph locals live on `Graph` |
| 🆕 **`BP1673`** | ⛔⛔ **the rail the plan's four passes miss.** `Stage5.FindVariableRef` resolves by **priority across kinds** with a **name** fallback, so once the mixture is legal two same-named declarations bind silently. `U-3` fixes emission, not selection; `U-14` fixes only the **editor's** namer; Stage 2 had **no** duplicate-name rule (grepped) |

⭐⭐ **Measured across all 58 shipped assets:** 0 AiPrimitives carry a `Variable`, 0 Instances carry a
`Parameter`/`WorkingState`, and the 3 Library assets declare **nothing** ⇒ **all three restatements
are corpus-neutral by construction**, which is why golden is unchanged.

⛔ **Why the store flip is left:** `Pass 5` demands `persistence-shape.txt` **unchanged**, so the three
properties must stop being *storage* while remaining *the serialized shape* — serialization-only
projections over the tagged store. Different work, different revert story, and the one gate whose
failure re-initialises every deployed entity's blackboard. 📄 [DESIGN_U12_Rails.md](DESIGN_U12_Rails.md).

---

## 1 · Batch 51 — `U-11` is DONE; `U-12` is unblocked

| commit | |
|---|---|
| `825556d` | ⭐⭐ **the editor bucket + `ViewsAreUnreadTests`** |

### ⭐⭐ The gate `U-12` bets on — now a test, not a belief

`ViewsAreUnreadTests`: **no site under `Hrot.Blueprints.Editor`, and none in the compiler stages,
reads `asset.Parameters` / `.WorkingState` / `.Variables`.**
🔴 **Proved to fail** by reintroducing one read — it reported it by file and line.
⭐ **And it asserts the pattern still matches a KNOWN read** (`DeclarationList` itself), because
⛔ **a grep that matches nothing looks exactly like a grep that is green.**

⚠ **Scope, deliberately:** the three **`*Order`** lists are out (display metadata; they survive the
store flip — `U-12`'s call), and so is **`IrAsset`**'s same-named trio (the *emitted* fields — they set
struct offsets and feed `StructureHash`).

### ⛔ The window needed NOTHING — a correction to the handoff

`BlueprintVariablesWindow` (line 377 on) has **zero** references to the three lists. **All 24 in that
file belonged to `BlueprintVariableSchemaSource`** — the half that survives `U-16`. ⇒ the file's big
count was never the window's, and nothing slated for deletion was rewritten.

### ⭐ What the source's move actually bought

| | |
|---|---|
| ✅ | every `_kind == VariableKind.Parameter` branch — gone |
| ⭐⭐ | `GetOrdered`'s **type-sniffing `GetId`** local, which returned `Guid.Empty` for anything that was neither decl type and **would have collapsed every row onto one dictionary key** |
| ⭐⭐ | `Resolve`'s **six hand-written arms** now read priority from `DeclarationList.ResolutionOrder` — ⛔ **two copies of an ordering that must match the compiler's is how `BP-226` happened** |

### 📌 Three things worth carrying forward

| | |
|---|---|
| **`ReplaceAll(kind, items)`** added | for the undo snapshot restore. ⚠ **Deliberately does not touch the order list**, unlike `Remove` — a snapshot restore puts back a state captured whole, and dropping ids would make undo lose the designer's ordering |
| **The create/undo pair is safe on the view** | its `decl` is created by that command ⇒ never in `VariableOrder` ⇒ the Order-maintaining `Remove` is a no-op for it, and the pair stays an exact inverse |
| ⚠ **One declared behaviour change** | `BlueprintPickerSources.Query`'s no-filter branch returned the **live** list; it is a materialised copy now, matching its other two branches |

---

## 2 · Batch 50 — `U-14` closed; `U-11`'s compiler bucket landed

| commit | |
|---|---|
| `7a45cc1` | ⭐ **`U-14` — one name space across all three kinds (`BP-232` closed)** + the indexed accessors |
| `e39ba38` | ⭐⭐ **`U-11` compiler bucket** — Stage0 · Stage2 · Stage4 · Stage5 · `V_VariableReferenceRules`. **`BP-236`** fixed |

### ⛔⛔ `U-11` is ~4× the size the plan says — measure before you sweep

| | |
|---|---|
| **plan** | *"~34 semantic sites"* · **handoff** | *"46 non-test files"* (upper bound) |
| ⭐ **measured** | **233 raw refs → 135 semantic CODE refs / 24 files** (+20 in doc comments, 30 incidental: `EventDispatcherDecl.Parameters`, the `Blueprints.Editor.Variables` **namespace**, `VariableKind.WorkingState`, palette `Categories.Variables`) |
| ⭐⭐ **and ~31 of the 135 are NOT `U-11`** | they are on **`IrAsset`** — a *different type* whose same-named three lists are the **emitted field** lists. ⛔ **They set the struct offsets and feed `StructureHash`; sweeping them moves the hash.** ⇒ **the plan's *"lowering · emit"* buckets do not exist for this task** |

### ✅ What landed · ⏭ what remains

| | |
|---|---|
| ✅ **compiler** | `Stage0_Rehydrate` · `Stage2_Validate` · `Stage4_TypeResolve` · `Stage5_Schedule` · `V_VariableReferenceRules`. Golden unchanged after **each** of the four sub-steps |
| ⏭ **editor** | `BlueprintDocumentFactory` · `NodePinSchema` · `BlueprintNodeModel` · `BlueprintPickerSources` · `BlueprintMyBlueprintModel` · `BlueprintGraphModel` · 2 drawers · **`BlueprintVariableSchemaSource`** |
| 📌 **stays until `U-12`** | `BlueprintCompiler`'s six-line **storage copy**. It builds an asset's storage — the thing that does not move until the store flips |
| ⛔ **NOT rewritten** | `BlueprintVariablesWindow` — `U-16` deletes it |

### ⭐ What the move actually bought, and the two traps

| | |
|---|---|
| ⭐⭐ **Two pairs of near-duplicate overloads collapsed into ONE each** | `Stage5.BuildIrFields`' two had **byte-identical bodies**, split only because `ParameterDecl` and `VariableDecl` were different types. `Stage4.ResolveFieldTypes`' split had already cost something: **`U-7`'s `BP1671` rail landed on one half and had to be hand-applied to the other** |
| ⚠⚠ **Trap 1 — `ById()` searches one kind too many** | three sites read **Variables ∪ WorkingState only**. ⛔ `Declarations.ById()` also searches `Parameters` ⇒ using it resolves a parameter id where the site never did. ⭐ Written out explicitly at each, with a comment, rather than taking the tidier call |
| 📌 **Trap 2 — one declared widening** | merging `Stage4` applies `BP1504` to every kind. ⭐ **Safe for a reason found UPSTREAM: `Stage2`'s `BP1507` already refuses a fixed-list `Parameter`** ⇒ unreachable for a compile that gets to Stage4 — and measured a corpus no-op first (Capacity > 0: **P 0 · W 0 · V 1**) |

### 🔴 `BP-236` — a green suite that was reporting on the SCHEDULE

`RecipeIntegrityTests.LoadRecipe` falls back to `TestAssets/Recipes` *"if assembly not loaded"* —
⛔ **but that directory holds 9 of the 16 recipes**, and has since long before this programme. So the
suite passed only when something else in the run had already loaded `Hrot.AI.Behaviors`.
⚠ **Reproduced both ways:** alone it fails two recipes; alongside `GoldenCorpusTests` (which
force-loads for the same reason — `BP1602`, Batch 44) all 16 pass. ⭐ **Exposed, not caused** by this
batch's added tests. Fixed with the same one-line preload.

---

## 3 · Batch 49 — `U-15` landed; `U-10` half landed, half re-sequenced

| commit | |
|---|---|
| `a03a02c` | ⭐⭐ **`U-15` — all 58 managed assets canonicalised; the canonical form is now INDENTED.** `BP-227` closed |
| *(this batch)* | ⭐ **`BlueprintSchemaV2.Up`/`.Down` + `v1→v2→v1` byte identity on all 58** |

### ⭐ `U-15` — what it actually took

| | |
|---|---|
| ⭐⭐ **Run BEFORE rewriting anything** | canonicalising round-trips 58 files through the model at once, so **anything the model does not carry is deleted**. Measured first: **exactly two paths** — `Header.SubsystemType`, `Header.SchemaVersion`, in 44 files — both removed from the model by `D-021` and superseded by the `$meta` envelope **all 58 files already carry**. Declared as exceptions so any *other* path still reddens |
| 📐 **Canonical = INDENTED** | ⛔ compact makes each asset one 3–12 KB line. 57/58 were already indented; and it was **already a live defect** — `SaveActiveBlueprintCommand` writes through `Serialize`, so opening a hand-authored asset and saving it **collapsed the file**. `Loco1.bp.json` is what that looks like |
| ⚠⚠ **`ToJsonString()` ignores `WriteIndented`** | it takes its **own** options. The flag had been set on `_options` and had **no effect on net8** — the only target that writes files. Both halves are set now |
| ⚠ **Cost** | 57 test cases / **5** methods asserted **compact** JSON substrings. ⭐ Fixed by reading the discriminator from the DOM, not by re-coupling to the new spelling. One test deleted a property by string-replacing its compact spelling — it would have deleted **nothing** and asserted about an unmodified document |
| ⛔ **`BP-227`'s count was wrong twice** | **ELEVEN**, not 7 — 4 corpus + **7** recipes. The recipes carry both `1` and `2`; only `1` was ever counted. ⚠ The undercount happened **by the same mechanism as the defect** |

### ⛔⛔ `U-10` — why only half shipped

⭐ **What DID ship:** `BlueprintSchemaV2.Up`/`.Down`, a plain `System.Text.Json` DOM pair, with
⭐⭐ **`v1 → v2 → v1` byte-identical for all 58** — *the gate `V1` had declared unwritable.* `U-15` is
what made it writable. **`Down` IS the revert**, so it ships with `Up` as the handoff demanded.
🔴 **Proved to bite:** dropping the order lists in `Down`, and silently skipping one declaration.

⛔ **What did NOT ship: any wiring.** Nothing writes v2, nothing reads it. Three measured reasons:

| | |
|---|---|
| 1️⃣ ⭐⭐ **`U-9` was built inverse, so `U-10`-before-`U-11` translates into a shape nothing uses** | the three lists are still the **storage**. Writing v2 today converts three lists → one array on save and back on load, into a shape **no code in the process consumes**, for zero present benefit — while carrying `Pass 3`, whose failure **resets every deployed entity's blackboard**. ⇒ 📐 **`U-11` → `U-12` → `U-10`** |
| 2️⃣ ⛔ **The migration framework cannot reach the reader that must not break** | `BlueprintIncrementalGenerator` targets **`netstandard2.0`**; the `Fdp.Core`/`Hrot.Common` references are **net8-only** ⇒ `IJsonDocumentMigrator`, `JsonEnvelope`, `MigrationRegistry` are **unreachable from the one production reader of every shipped asset**. And `BlueprintMigrationModule` lives in `Hrot.Common`, which must not reference the compiler ⇒ transform and registration cannot meet without a duplicate or a new seam through a bootstrap shared by **six** host profiles. **Filed as `BP-235`** |
| 3️⃣ ⚠ **There IS a production consumer** *(contrary to a first reading)* | `Hrot.ClusterRunner --mode migrate` walks **every `*.json`**, and `BuildClusterRunnerMigrate` registers the blueprint doc type ⇒ bumping `$meta.schemaVersion` to 2 while `BlueprintMigrationModule.CurrentVersion` stays **1-passthrough** is a **live inconsistency**, not a cosmetic one |

---

## 4 · Batch 48 — `U-9`, the tagged declaration (`D1`)

| commit | |
|---|---|
| `171ef2f` | ⭐⭐ **`BlueprintDeclaration` + `DeclarationList` + `BlueprintAsset.Declarations`** |

### What is there now

| | |
|---|---|
| `BlueprintDeclaration` | one declaration **carrying its kind** — a **facade**, reading and writing straight through to the backing `VariableDecl` / `ParameterDecl` |
| `DeclarationList` | a **live write-through** `IList<BlueprintDeclaration>` over all three lists, in **storage order** (Parameter, WorkingState, Variable) |
| `BlueprintAsset.Declarations` | the view, `[JsonIgnore]`d |
| `Ir.DeclarationRefs` | the **explicit total** `DeclarationKind ↔ VariableKind` mapping, plus `asset.RefOf(decl)` / `asset.Resolve(ref)` |

### ⭐ The four decisions, and why

| | |
|---|---|
| ⚠⚠ **Direction — INVERSE of the plan's wording** | the plan says *"old lists become views"*; the **tagged type is the view** instead. ⭐ **That is what keeps `U-9` internal**: with the lists still the storage, the serializer is untouched and the tag cannot reach JSON *structurally* rather than by discipline. A new store would have needed **write-through** views anyway to survive `U-11` (~34 consumers move one bucket at a time), so it buys nothing `U-10`/`U-12` are not already for. ⭐ **`U-11` is unaffected** — consumers move onto `Declarations` either way |
| ⭐⭐ **A facade, NOT a value copy** | identity is the **backing object**, so `decl.Name = "x"` lands in the stored list. ⛔ A copy would have accepted the edit, reported success and discarded it for the whole of `U-11` — trap #5 at the scale of the editor |
| 📐 **§1 asymmetry — ruled (a)** | `ParameterDecl` lacks `IsEditable` / `IsExposedOnSpawn` / `Category`. They are **editor-presentation**; giving `ParameterDecl` three members is a **persisted-shape** change and belongs to `U-10`. ⇒ enumerated in `MembersAParameterDoesNotCarry`; **reads return the documented default** (a parameter genuinely has no category — `null` says so), **writes throw** `NotSupportedException`. ⭐ The test **derives** the same set by reflection over both backing types |
| 📌 **Graph locals are NOT a kind** | `Q27-C1` makes a local legally **shadow** an asset variable. Folding them in would point `U-14`'s cross-kind uniqueness rule at a space where duplicate names are the rule |

### ⛔⛔ The handoff's Pass 3 was wrong twice — and the second one is the interesting one

`Serialize(Deserialize(j)) == j` for all 42:

1. ⛔ **It does not run on this corpus.** 41 of 42 files are hand-authored 2-space-indented against
   `WriteIndented = false` — it loses on whitespace before reaching the question. ⭐ `U-10`'s Pass 1
   already carries this correction; it simply was not carried forward into `U-9`'s.
2. ⭐⭐ **Even canonicalised it proves nothing about the tag: round-tripping is CLOSED UNDER A LEAK.**
   A written tag is also read back, so the identity still holds either way.
   ⚠ **Measured, not argued:** under the deliberate `[JsonIgnore]`-removal probe, `RoundTripIsStable`
   **passed** while the recorded baseline reddened.

⇒ ⭐ **The gate is a recorded baseline instead** — SHA-256 of all 42 canonical serializations, captured
**on the pre-`U-9` tree**: `Snapshots/Golden/persistence-shape.txt`. Round-trip **stability** is kept
for what it does prove, and `U-15`/`U-10` inherit both.

### 🔴 Four inverse-edit probes, each red on the test that names it

| probe | reddened |
|---|---|
| drop `[JsonIgnore]` | the 42-asset baseline **and** `TheTagIsNotSerializable` — ⚠ **not** `RoundTripIsStable` |
| a no-op setter on `Name` | both `EveryMemberIsCarriedBothWays_*` |
| `Category` removed from the exclusion list | the derived-set assertion, **alone** |
| the view returns a copy, not a facade | `EditingThroughTheViewMutatesTheStoredDeclaration` + 3 identity/ref tests |

⚠⚠ **`git checkout --` is NEVER how a probe is undone** — it resets to HEAD and discards uncommitted
work. Un-apply with the inverse edit.

---

## 5 · Batch 47 — `U-7` + `U-8` (`BP-228` closed)

| commit | |
|---|---|
| `8997d91` | ⭐⭐ **`BP1671` the type-existence rail · the picker's type list is now a real union** |

| | |
|---|---|
| ⛔ **The handoff's *"the seam already exists"* is true for METHODS, not type existence** | `TryResolve` takes a type AND a method and returns one `bool` ⇒ a `false` cannot say which was missing. ➕ one member, `TypeExists`, **no default body** |
| ⭐⭐ **No oracle ⇒ NO OPINION, and that is load-bearing** | measured: **exactly ONE production site supplies a resolver** (`BlueprintIncrementalGenerator`). Of the three `CompileOptions` sites, the editor's (`QuickReloadService`) has **no production caller** ⇒ the rail guards the **build**, where the defect bit |
| ⭐ **`U-8` needs no editor oracle — that is the answer to the open question** | there is no editor compile path to attach one to. Instead the picker is safe **by construction**: primitives ∪ **discovered** `[BlackboardDtoStruct]` FQNs — *discovery IS the existence proof* |
| 🔴🔴 **`BP-87`'s restored lock found a live defect immediately** | **`System.String` was OFFERED and can never compile as a variable** (`BP1503`). Removed; `FixedString32/64/128` were always the supported ones |
| ⚠ **`SelectableTypeIds` is now `Lazy`, not a static initializer** | it reflects over **loaded** assemblies, so a static ctor freezes whatever was loaded at type-load time — nothing, in a test host |
| ⚠ **The picker list is ALSO the list-ELEMENT list** | adding structs silently gave every struct list a *"≈ 4 bytes"* budget. `ElementByteSize` now sizes a discovered struct via `Marshal.SizeOf`. **A repo test caught it the same run** |
| 📌 **`BP1671` needed `[CoversDiagnosticCode]`** | the repo's own coverage rail refused a new code with no test naming it |

---

## 6 · Batch 46 — `U-4` + `U-5` (`BP-230`, `BP-231` closed)

| commit | |
|---|---|
| `7f64724` | ⭐⭐ **three kinds instead of a `bool` · a real reference count · `Role`/`Scope` honest** |

| | |
|---|---|
| ⭐⭐ **The editor now uses the COMPILER's `VariableKind`** | one vocabulary for one three-list model, both ends of the pipeline. `Unresolved` is refused at construction |
| ⭐⭐ **`BP-230` answered from the PANEL CODE, no screenshot** | `VariablesPanelControl:402` gates the Role combo on `!IsReadOnly` **alone**, and the blueprint source returns `false` ⇒ the combo was **drawn, live, and discarded**. That question had been open since Batch 38 *pending the visual check* |
| ⭐ **The fix is a CAPABILITY, not a setter** | `Q-k`: Role/Scope are read-only for blueprints — a MOVE, not a toggle. `SupportsRoleScopeEditing` has ⛔ **no default body** so every implementer must answer; the panel falls back to the existing read-only **text** rendering |
| ⭐ **The setters' defaults now THROW** | *a default body is the interface volunteering to lie on an implementer's behalf* — trap #5 written into the contract |
| ⭐⭐ **The count mirrors `Stage5.FindVariableRef`** | id (with `var:` stripped) then the **name fallback**, in list priority order. ⚠ **It could NOT copy the locals source**, which counts by id only — correct there (`FindLocalIndex` has no name fallback), wrong here |
| ⭐ **`BP-231`: remove drops the id; rename must NOT touch the order list** | it is keyed by id. Test-locked so a later name-keyed "fix" cannot corrupt it |

### ⚠ The gate that did not move, and why that mattered

⛔ **The first full run left `Hrot.Editor.AiShared` at 1213 — unmoved — after changing that very
interface.** The handoff predicted it would move. ⇒ ⭐ **the contract change had no coverage in the
assembly it landed in**; three tests were added there (**1213 → 1216**). *A contract change tested only
through its consumers is a contract change nobody is watching.*

---

## 7 · Batch 45 — `U-3`, the kind-carrying index (`BP-226` closed)

| commit | |
|---|---|
| `189ad05` | ⭐⭐ **`VariableRef(VariableKind, int)` threaded Stage 5 → IR → Stage 7** |

| | |
|---|---|
| ⭐⭐ **`Index` stays LIST-RELATIVE** | nothing rebases it. ⛔ **Do not "fix" it into a combined index** — see the correction below |
| ⭐⭐ **`VarFieldName(int)` no longer exists** | the wrong call is **unwritable**, not merely unwritten |
| ⭐ **`VariableKind.Unresolved` is the DEFAULT (0)** | a zero-initialised ref means *"nobody set this"* and throws. `Variable = 0` would have silently meant `Variables[0]` |
| ⚠ **`BP1670`'s throw survives, restated** | *"index < 0"* → *"no kind resolved"*. Same condition, named for what it means |
| ➕ **The emitter now picks the CONTAINER** | `p.` for a Parameter, state var otherwise. The bare int could not say a parameter lives on a **different struct** |
| ➕ **Out-of-range now THROWS** | was `__var_{index}` — invalid C#, no diagnostic. With the kind carried there is no legitimate way to reach it |

### ⚠⚠ Two corrections worth keeping

| | |
|---|---|
| ⛔ **The handoff's *"the WorkingState arm is not offset"* is WRONG against the code** | it read the arm as needing `ws[index - fields.Count]`. ⛔ **It does not.** The index was **list-relative**, so the un-rebased read was **correct whenever `Variables` was empty** — every shipped AiPrimitive. Subtracting would have **introduced** a defect in the one case that worked. 📌 Source of the belief: `FindParameterIndex`'s doc comment called the result *"a COMBINED index"* — **that comment was wrong**, and is gone with the method |
| ⛔⛔ **My first draft of the Pass 2/3 tests PASSED before the fix** | the fixture used an **Event** graph, which is eliminated whole ⇒ `TickCore` emitted an **empty body**, and every assertion was satisfied by the **struct declarations**. ⭐ Shipped assets use a **`Function` graph named `Tick` (Instance) / `Main` (AiPrimitive)**. ⚠ *"Assert the test is red first"* is what caught it — a `Contains`-only assertion on a name that also appears in a declaration proves nothing |

---

## 8 · Batch 44 — the `U-` sequence opened

| commit | |
|---|---|
| `2275c29` | ⭐⭐ **`U-1` the golden-corpus harness · `U-2` the compiler owns its graphs (`BP-229` closed)** |

### ⭐ Decisions and findings — do not re-derive

| | |
|---|---|
| ⭐⭐ **The corpus compiles as a SET, not asset by asset** | ⛔ `SmokeGuard`/`SmokePatrol` fail **`BP1301`** with empty `SiblingSignatures` — they **call each other**. Production has always parsed every `AdditionalFiles` entry into a sibling catalog. ⚠ **The plan's *"one `typeof(...).Assembly` touch ⇒ 42/42"* is 40/42**: the preload and the catalog are two independent prerequisites and only the first was written down |
| ⭐⭐ **The baseline is `CompilerMode.Release`** | the generator **hardcodes** it — not derived from the MSBuild configuration — so a Debug-mode harness would have baselined output that **never ships** (~40 extra `DebugProbe` lines/asset). 📌 Not a defect: `EditorMetadata.CompilerMode` + `QuickReloadService` are the debugger's **live re-instrumentation** path. 📌 **Known gap: Debug-mode emit is NOT covered** |
| ⭐⭐ **§1.5 parity answer: 42/42 byte-identical** | in-process reflection resolver vs production's `RoslynClrSignatureResolver`, compared via `EmitCompilerGeneratedFiles`. ⚠ The two things that DID differ first time were mine — mode and sibling catalog — not the resolver |
| ⭐ **Two tiers, and the split earns its keep** | Tier 1 = `StructureHash` + field name/type/offset/size + diagnostic **multiset**; Tier 2 = full source as **files, not hashed**. 📌 `StructureHashComputation` hashes `Dispatch` + `name\|type\|offset\|size` and nothing else ⇒ **Tier 1 ⊇ the hash's inputs**, so a hash move is always explicable from Tier 1 |
| ⚠ **The harness MIRRORS `Compile`'s stage sequence** | it must: Tier 1 needs the laid-out `IrAsset` and `CompileResult` exposes only hash/source/diagnostics. ⭐ **So the copy is pinned, not trusted** — `HarnessPipelineMatchesTheRealCompiler` asserts byte-identical source + hash against the real compiler for all 42 |
| ⭐ **`U-2`'s copy sits AFTER Stage 0** | Stage 0's pin rehydration is **contractually visible** to the caller; a copy taken earlier hides it. Fresh containers + fresh **`Link` objects** (⚠ `MacroExpander` rewires links **in place**, `:205`/`:258`), **shared `Node` objects** (nothing mutates one post-Stage-0, and cloning would have to preserve the ids the `DebugMap` is keyed by) |

### 🔴 Two defects found in the test infrastructure itself

| | |
|---|---|
| ⛔⛔ **`ResolveSnapshotsDir` walked up from `bin/`** | so `BLUEPRINT_REGENERATE_SNAPSHOTS=1` wrote baselines **into `bin/`, never into git**. Harmless for existing snapshots (`PreserveNewest` keeps them in step); ⚠ **silently fatal for a NEW one** — green locally, *"snapshot not found"* on a clean checkout. Now anchored on the test project's `.csproj` |
| ⛔ **A bite test must never target a committed baseline path** | under regenerate mode the helper **writes**, so the first run overwrote `ManagedCollectionDemo`'s Tier 1 with the **mutated** layout. Bite tests now compare against a scratch copy |

---

## 9 · What Batches 41–43 shipped — `BP-57` end to end

| commit | |
|---|---|
| `3e79c1c` | **the locals schema source** — `BlueprintLocalVariableSchemaSource`, an `IVariablesSchemaSource` over `Graph.LocalVariables` |
| `748f1f7` | **picker + title** — locals offered in `variables.all`; the raw-GUID bug fixed |
| `57cd616` | **delete refuses while referenced · undo on every gesture** |
| `f116266` | ⭐⭐ **the Local Variables SECTION** — the surface all of the above was built behind |

### ⭐ Decisions taken — do not re-litigate

| | |
|---|---|
| **Written to the SHARED interface** | so the unification's `U-6` **absorbs** it instead of undoing it. ⛔ It *implements* `IVariablesSchemaSource`; it never *changes* it — that is `U-5`'s `V2` and would move the **AiShared** gate. ✅ AiShared held at **1213** across all three batches |
| ⭐⭐ **`CountNodesReferencingVariable` counts BY ID, ACROSS THE ASSET** | by id because `FindLocalIndex` has no name fallback (a node carrying the NAME is not a reference); across the asset because a node in **another graph** carrying the id is the dangling case `BP1670` refuses |
| ⭐ **Delete REFUSES while referenced**, naming the count and the graphs | matches `DeleteItem`'s existing policy, and ⚠ **diverges in one direction deliberately**: a local's references can sit in a graph invisible from the current canvas |
| ⭐ **That ruling makes the undo honest for free** | no nodes are removed ⇒ `BP-225`'s trap is **unreachable**, not merely avoided |
| **Undo = snapshot, all graphs, deep copies** | mirroring `RecordItemEdit`. No-op gestures record no entry (`BP-204`) |
| ⭐⭐ **The section follows the canvas through `Func<Guid>`** | `AiCanvasContext.CurrentGraphId` — **the mechanism `BP-72` already chose** for the signature window. ⛔ Not a second one: the switcher is per-document (factory), the model is per-perspective (composition root), and neither references the other |
| ⭐ **Present and EMPTY, never absent** · **`[+]` present on a Macro graph and refusing out loud** | `Q26-B2`. Forced anyway — `_sections` is `static readonly`, so `CanCreateItems` cannot vary per graph |
| ⭐ **`local:` items route to the source, not to `RenameItem`/`DeleteItem`** | `RecordItemEdit`'s snapshot covers the asset's declaration lists only ⇒ routing locals through it yields an undo that restores nothing |
| **Picker widened to locals and NOTHING else** | ⛔ `WorkingState`/`Parameters` are `BP-226`'s space; struct FQNs are `BP-228`'s. Test-locked |

---

## 10 · Where the code is

| file | |
|---|---|
| `Hrot.Blueprints.Editor/Variables/BlueprintLocalVariableSchemaSource.cs` | ⭐ the source. `record`/`refuse` are optional ctor args (4th/5th) |
| `Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintModel.cs` | ⭐ the section: `SectionLocalVariables`, `CommandCreateLocalVariable`, `CurrentGraph`, `SyncCurrentGraph()` |
| `Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintWindow.cs` | builds the source + modal; `Refuse` → `IEditorIndicators`; `LastRefusal`/`Locals` are the headless seams |
| `Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` | `LocalVariableUndoRecorder` · `TryFindLocal` · `DuplicateLocal` · `MakeUniqueLocalName` · `RegisterCreateLocalVariableCommand` |
| `Hrot.Blueprints.Editor/Host/BlueprintPickerSources.cs` | `RowLabel` is the headless seam for the `(local)` suffix |
| `Hrot.Subsystems/Hrot.Editor/EditorSubsystem.cs` `~:2261` | passes `ctx?.CurrentGraphId` and `ctx?.Indicators` to the My Blueprint window |
| `Hrot.Blueprints.Tests/Golden/GoldenCorpus.cs` | ⭐ **the harness** — corpus glob, sibling catalog, preload, both tiers |
| `Hrot.Blueprints.Tests/Golden/GoldenCorpusTests.cs` | the sweep (42×3) + the three `Bite_*` proofs + the pipeline-equivalence pin |
| `Hrot.Blueprints.Tests/Snapshots/Golden/` | ⭐ **the baseline — 42 Tier 1 + 42 Tier 2 files, 532 KB.** Regenerate with `BLUEPRINT_REGENERATE_SNAPSHOTS=1` |
| `Hrot.Blueprints.Compiler/Compiler/BlueprintCompiler.cs` | `U-2`'s copy + `CloneGraphForCompilation` |
| **tests** | `Tests/Editor/LocalVariableAuthoringTests.cs` (11) · `LocalVariablePickerAndTitleTests.cs` (9) · `LocalVariableDeleteAndUndoTests.cs` (9) · `LocalVariableSectionTests.cs` (15). **44 locals tests, all green** |

---

## 11 · Gates

The eight, solution **`IOS-IG-SimHost.sln`** (⚠ **not** `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** — they silently do not run with it.

**Post-Batch-55** *(full `-t:Rebuild`)*: build **0 errors / 69 warnings** ·
Blueprints **3569 / 3559 passed / 0 failed / 10 skipped** · **AiShared 1216** · BTree **612** ·
Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** ·
⭐⭐ **golden 42/42 both tiers unchanged** · ⭐ **`persistence-shape.txt` MOVED, once, reviewed** (§0.2).
⚠ **Three further suites, with PRE-EXISTING failures** (§0.5): Hrot.Common **53 passed / 3 failed** ·
ClusterRunner **252 passed / 2 failed** · ⛔⛔ **SimHost is FLAKY and load-sensitive — 1, 3 and 8
failures across runs of identical binaries** (8 when eight suites run in parallel). Its baseline
"1 failed" was a single sample of a flaky suite. ⭐ The constant one is
`BranchedRecording_CapturesHistoricalStateAsKeyframe`; the rest are an `Extract_*` family that shares
state. **None touch blueprints.**

**Post-Batch-54, all eight ran** *(full `-t:Rebuild`)*: build **0 errors / 69 warnings** ·
Blueprints **3551 total / 3541 passed / 0 failed / 10 skipped** · **AiShared 1216** · BTree **612** ·
Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** ·
⭐⭐ **golden 42/42 both tiers unchanged — so `StructureHash` is unchanged for every shipped asset** ·
⛔ **`persistence-shape.txt` unchanged, deliberately** (see §0.1).

**Post-Batch-53, all eight ran** *(full `-t:Rebuild`)*: build **0 errors / 69 warnings** ·
Blueprints **3538 total / 3528 passed / 0 failed / 10 skipped** · **AiShared 1216** · BTree **612** ·
Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** ·
⭐⭐ **golden 42/42 both tiers unchanged** · `persistence-shape.txt` **unchanged** ·
⭐ **run BOTH ways** — every class the flip touched also green under its own isolated filter.

**Post-Batch-52, all eight ran** *(full `-t:Rebuild`)*: build **0 errors / 69 warnings** ·
Blueprints **3532 total / 3522 passed / 0 failed / 10 skipped** · **AiShared 1216** · BTree **612** ·
Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** ·
⭐⭐ **golden 42/42 both tiers unchanged** · `persistence-shape.txt` **unchanged**.

⭐ **New standing instrument:** `scripts/order-dependency-sweep.sh` — every test class run **alone**
against a green suite. ~50 min for 370 classes. ⛔ **Class granularity under-reports** (see §0.3);
isolate per-test inside anything it names.

**Post-Batch-51, all eight ran** *(full `-t:Rebuild`, so 69 is honest — an incremental build reports
24, and a partial one 48)*: build **0 errors / 69 warnings** · Blueprints **3518 total / 3508 passed /
0 failed / 10 skipped** *(+3 = Batch 51's own tests)* · **AiShared 1216** · BTree **612** ·
Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** ·
⭐⭐ **golden 42/42 both tiers, unchanged at EVERY sub-step** · `persistence-shape.txt` **unchanged**.

⚠ **`BP-236`:** the Blueprints suite is only order-independent as of this batch. If a suite ever goes
red only in a particular combination, suspect a **load-order** dependency before suspecting the code.

### ⭐ Run the five `--no-build` suites in PARALLEL

Measured this batch: sequential ≈ 3 m 40 s of test execution, parallel ≈ **2 m 05 s**, bounded by
Blueprints. They only read the tree. The two NodeEdit gates must stay sequential (they build).

### ⚠ Two gate-script traps, both paid for

| | |
|---|---|
| ⛔ **`grep -E "Passed!\|Failed!"` DROPS the `[FAIL]` line** | so a flake reports a number and loses its identity — that happened in Batch 42 and the test could not be named. ⭐ **Always include `\[FAIL\]` in the pattern** |
| ⚠ **`--logger "console;verbosity=normal"` prints `Test Run Successful.` + `Total tests:`**, not `Passed!` | grep for both forms |

⚠ **A closing INCREMENTAL build under-reports warnings.** Record honestly rather than printing `69`
from memory.

⛔ **The visual check has not run for SIXTEEN batches.** *"Present and empty"* and *"follows the canvas"*
are exactly what a headless test can pass while the panel draws nothing. **Say so; never imply coverage.**

---

## 12 · Open findings that are mine

| | |
|---|---|
| ~~**BP-228**~~ ✅ | ✅ **CLOSED Batch 47 as `U-7`** |
| ~~**BP-229**~~ ✅ | ✅ **CLOSED Batch 44 as `U-2`** — `Compile` now owns the graphs it rewrites |
| ~~**BP-230**~~ ✅ | ✅ **CLOSED Batch 46 as `U-5`** |
| ~~**BP-231**~~ ✅ | ✅ **CLOSED Batch 46 as `U-5`** |
| **BP-232** 🟠 | `MakeUniqueName` checks `Variables` only ⇒ a `Parameter` and a `Variable` may share a name |
| **BP-233** 🟠 | `BP1650` carries a **fourth** copy of the latency predicate, still missing the inline-action case. Half-closed |
| **BP-234** 🟠 | ⭐ **new, Batch 43** — editing a suspending graph's locals silently re-initialises its blackboard. ⚖️ **Ruled: no per-gesture warning** — add/delete change the same hash by the same mechanism, so warning on the drag would imply the other two are safe |
| ~~**BP-226**~~ ✅ | ✅ **CLOSED Batch 45 as `U-3`** |
| **BP-227** | the numeric `Dispatch` (**7** files) — settled by `U-15`, not yet done |

📌 **One thing the section proved and did NOT patch:** `BlueprintLocalVariableSchemaSource.AddVariable`
appends unconditionally, so a **modal** can create two locals of one name. The guard lives in
`BlueprintMyBlueprintWindow.CreateLocalVariable`, host-side, so the source's contract stays as `U-6`
will find it. ⚠ Reported rather than changed, per the handoff's instruction.

---

## 13 · ⚠ Process lessons — paid for, do not re-learn

| | |
|---|---|
| ⛔⛔ **NEVER `git checkout --` to undo a revert-probe** | It resets the file to **HEAD**, discarding *uncommitted* work. It cost the §2/§3 source edits in Batch 42. ⭐ **Un-apply the probe with the inverse edit instead** — three probes were run that way this batch with no loss |
| ⛔ **`mv $F.bak $F` back-dates the file** | MSBuild then skips the recompile and the reverted binary survives. **`touch` after restoring** |
| ⛔ **Delegation + a dirty tree do not mix** | Sub-agents share ONE working tree ⇒ builds must be sequential ⇒ you hold uncommitted edits while an agent runs. **Commit first, then delegate into a clean window** |
| ⭐⭐ **A revert that stays GREEN is a finding about your TESTS** | never evidence the fix was unnecessary |
| ⭐ **Confirm the handoff's claims before building on them** | this session has corrected the coordinator in **every batch since 29**. This batch: the handoff said `Changed` must fire *"or the panel shows the previous graph's locals"* — ⛔ **wrong against the code**: `MyBlueprintPanel.DrawSections` calls `GetItems` **every frame** and its `Changed` handler is an empty lambda. The delegate is what makes it follow the canvas; `Changed` is the contract |
| ⭐ **Report what you did NOT do** | Batches 41 and 42 both stopped early without saying so, and the coordinator had to measure it |
| ⭐ **Check what a shared menu offers before assuming an item is inert** | `MyBlueprintContextMenu` offers **Duplicate** for every `IsRenamable` item — so the locals rows needed a duplicate arm that no handoff asked for, or the entry would have appeared and done nothing |

---

## 14 · The wider programme

⏭ **The unification is under way** — 📄 [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md),
reviewed by 📄 [REVIEW_Unification_Plan.md](REVIEW_Unification_Plan.md) (**run it, with five named changes**).
✅ **`U-1`, `U-2` (44), `U-3` (45), `U-4`/`U-5` (46), `U-7`/`U-8` (47) are done.** ⏭ **`U-6` is next — and it is the
first task that is NOT headless-provable**: that the Details table renders, and renders read-only, needs
the visual check that has not run for twelve batches. ⭐ `U-3` declared no golden change and
**delivered none** — the harness checked it rather than anyone hoping. 
⚠ **`U-10`'s byte-identity gate still needs `U-15`'s corpus canonicalisation** or it is unwritable.
⭐ **Every later `U-` task's *"the output did not change"* is now falsifiable** — that was the point of `U-1`.

📌 **Still unbuilt from the locals programme, deliberately deferred:** the **node badge** distinguishing
a local from an asset variable on the canvas. It needs a new member on `INodeModel` (`NodeEditor.Core`)
plus rendering in `NodeEditor.UI`, so it **moves the NodeEdit Core (208) and UI (131) gates** — the
reason three handoffs made it the stop point. The picker's `(local)` suffix disambiguates at pick time;
on the canvas the two still render identically.

📌 **Housekeeping the coordinator must do:** delete `claude/batch39-locals-preserved` (fully merged;
this session gets HTTP 403 on branch deletion).
