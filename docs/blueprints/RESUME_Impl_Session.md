# RESUME — implementation session · ⭐⭐ **the `U-` sequence has started**

> **Written for a fresh session. Self-contained; assumes no prior conversation.**
> **You are the *implementation* session.** A separate *coordinator* session owns the tracker and
> writes the handoffs. Last updated **2026-08-14** (Batch 49).
>
> ✅ **Batch 49 is COMPLETE and reported.** `U-15` (**the corpus is canonicalised** — `BP-227` closed)
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
| **Coordinator branch — do NOT push** | ⭐ **`claude/blueprint-authoring-status-gm0akp`** (was at `2d4b10f`, merged into mine) |
| **Last handoff** | 📄 **[HANDOFF_Batch49_Canonicalise_And_Migrate.md](HANDOFF_Batch49_Canonicalise_And_Migrate.md)** — ⭐ **`U-15` in full; `U-10` half, deliberately** |
| **Counts** | **58 open · 112 done** — ⚠ *derive, never hand-count:* `python3 scripts/tracker-counts.py --check` |
| **Next free ids** | rows **BP-236+** · diagnostics **BP1672+** — *(Batch 49 allocated `BP-235`; no new diagnostic)* |

⛔ **No PR unless the user explicitly asks.** There has never been one in this programme.
⛔ **Never put a model identifier** in a commit message, code comment, or anything else pushed.

---

## 0 · First actions, in this order

```bash
git fetch origin claude/blueprint-authoring-status-gm0akp
git merge origin/claude/blueprint-authoring-status-gm0akp --no-edit   # rule 7
python3 scripts/tracker-counts.py --check                              # expect 58 / 112
```

Then read whatever handoff is newest on that branch. **No batch is in flight.**

### ⏭ What comes next

⭐⭐ **`U-11` is the natural next batch** — ~34 consumers move onto `BlueprintAsset.Declarations`,
one bucket per commit (compiler stages · lowering · emit · editor), golden unchanged **at every
sub-step**. ⭐ It is also now on `U-10`'s critical path: Batch 49 re-sequenced `U-10` to run
**after** `U-11`/`U-12`, so that the on-disk v2 shape mirrors an in-memory shape that exists.
🟢 **`U-14`** (`BP-232`) stays the cheap one and is independent.

⛔⛔ **`U-6` / `U-13` / `U-16` still hard-require the VISUAL CHECK**, which has now not run for
**fourteen batches**. They are a Details table, a read-only view and deleting a whole window — exactly
the shape a headless test passes while the panel draws nothing. **Say so; never imply coverage.**

---

## 1 · Batch 49 — `U-15` landed; `U-10` half landed, half re-sequenced

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

## 2 · Batch 48 — `U-9`, the tagged declaration (`D1`)

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

## 3 · Batch 47 — `U-7` + `U-8` (`BP-228` closed)

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

## 4 · Batch 46 — `U-4` + `U-5` (`BP-230`, `BP-231` closed)

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

## 5 · Batch 45 — `U-3`, the kind-carrying index (`BP-226` closed)

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

## 6 · Batch 44 — the `U-` sequence opened

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

## 7 · What Batches 41–43 shipped — `BP-57` end to end

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

## 8 · Where the code is

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

## 9 · Gates

The eight, solution **`IOS-IG-SimHost.sln`** (⚠ **not** `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** — they silently do not run with it.

**Post-Batch-49, all eight run** *(full `-t:Rebuild`, so 69 is honest — an incremental build reports
24, and a partial one 48)*: build **0 errors / 69 warnings** · Blueprints **3505 total / 3495 passed /
0 failed / 10 skipped** *(+14 = Batch 49's own tests)* · **AiShared 1216** · BTree **612** ·
Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** ·
⭐⭐ **golden 42/42 both tiers, unchanged — across a batch that rewrote all 58 shipped assets.**

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

⛔ **The visual check has not run for FOURTEEN batches.** *"Present and empty"* and *"follows the canvas"*
are exactly what a headless test can pass while the panel draws nothing. **Say so; never imply coverage.**

---

## 10 · Open findings that are mine

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

## 11 · ⚠ Process lessons — paid for, do not re-learn

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

## 12 · The wider programme

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
