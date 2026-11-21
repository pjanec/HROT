# Architect Question #31 — **ANSWERS**: the migration registration seam (`BP-235`)

> ⚠ **Answered by the implementation session acting as architect, at the user's request** — ⛔ **not
> relayed from the NotebookLM architect.** Treat it as an engineering ruling grounded in the code, and
> overrule it if the design system says otherwise.
> 📌 **Method:** codebase-memory graph (`home-user-HROT`, 167 583 nodes / 414 716 edges, indexed at
> `c5550ff`) + targeted greps. Every number below is measured.

---

## 0. ⭐⭐ Four corrections to §1 — and one of them changes the answer

| § | the question says | measured |
|---|---|---|
| §1.2, Q31-A | *"six host profiles"* | ⛔ **Five** `Build*` profiles — and **Blueprint is registered in exactly TWO**: `BuildEditor` and `BuildClusterRunnerMigrate`. `BuildSimHostCgf` / `BuildIg` / `BuildClusterRunnerCi` **deliberately do not** (*"Blueprint and BehaviorTree are intentionally NOT registered (M-2)"*), and `NodeBootstrapperMigrationTests` asserts `DoesNotContain` for each. ⇒ **the bump's blast radius is 2 profiles, not 6** |
| §1.1 | — | ⭐ A host that has not registered the doc type does not silently misread it: `MigrationRegistry.GetEntry` **throws** *"Doc type 'x' is not registered."* ⇒ SimHost / IG / CI never touch a blueprint through the framework at all |
| §1.3 | *"`BlueprintIncrementalGenerator` — the one production reader"* | ⛔ **Six production read sites** (`Stage1_Parse`, `BlueprintDocumentFactory`, `NewFromRecipeService`, `BlueprintEditorBootstrap`, `EditorSubsystem`, the generator) — ⭐ **but all six funnel through `BlueprintJsonServices.Deserialize`, so there is exactly ONE reader path, and Batch 54 already made it v2-capable.** Writers: **two**, both in the Editor, both net8 |
| §4 | *"the reason `Fdp.Core`/`Hrot.Common` are net8-only is not established"* | ⭐ **Established: there isn't one in the migration code.** Both are a bare `<TargetFramework>net8.0</TargetFramework>`. The framework is **34 files / 3 805 lines**, and its only non-BCL `using`s are `Fdp.Core.Logging` and `Fdp.Core.Serialization`. `System.Text.Json.Nodes` demonstrably works on netstandard2.0 — ⭐ **`BlueprintSchemaV2` is proof by existence.** ⇒ the constraint is **packaging, not capability** |

### 0.1 ⭐⭐⭐ The finding that settles Q31-A: **this has already been done once**

`ScenarioMigrationModule` is at **`CurrentVersion = 2` with a real `RegisterDocType` chain** —
`V1ToV2_EntityInfo_AddTags` / `V2ToV1_EntityInfo_RemoveTags`.

```
Hrot.Common/Scenario/Migrations/Migrators/Scenario/V1ToV2_EntityInfo_AddTags.cs
    using System.Text.Json.Nodes;          ← a pure DOM transform
    using Fdp.Core.Serialization.Migrations;
    using Hrot.Common.Scenario.Migrations.Helpers;
    ⛔ NO reference to the scenario MODEL types
```

⇒ ⭐ **The house pattern is: a migrator is a DOM transform that lives next to its registration in
`Hrot.Common` and never touches the model.** `BlueprintSchemaV2` is already exactly that shape — the
only thing stopping it from moving is **three enum members**.

---

## Q31-A — Where does the registration live? ⇒ ⭐⭐ **A1, and it is far smaller than the question assumes**

### The ruling

📐 **Extract `BlueprintSchemaV2` into a new multi-targeted assembly — `Hrot.Blueprints.Schema`
(`netstandard2.0;net8.0`) — depending on `System.Text.Json` and nothing else.**

```
Hrot.Blueprints.Schema        ← the transform. no deps but System.Text.Json
      ▲                    ▲
      │                    │
Hrot.Common          Hrot.Blueprints.Compiler        ⭐ no cycle, no duplication
 (net8, registers)    (ns2.0 + net8, reads)
```

### Why not the other two

| | verdict |
|---|---|
| ⛔ **A2 — self-registration at startup** | **Rejected on measured evidence.** It contradicts **M-2**, the codebase's stated policy that *"each host registers only the formats it actually loads"* — enforced by `NodeBootstrapperMigrationTests` asserting that three profiles **must not** see Blueprint. Self-registration registers wherever the assembly loads, which is the opposite. ⚠ The coordinator's own worry was right and is worse than stated: A2's failure mode is not "a profile forgets" but "a profile that deliberately opted out gets it anyway" |
| ⛔ **A3 — move the transform into `Hrot.Common`** | **Rejected — it strands the generator.** `Hrot.Common` is net8-only, and `BlueprintIncrementalGenerator` is netstandard2.0 and must `Down` a v2 file to read it. ⭐ Tempting because `.Compiler` already references `Hrot.Common` **on its net8 target** — but that is precisely the target that does not need it |
| ✅ **A1 — a third assembly** | The question calls this *"a new project in a solution with six host profiles… and it must be net8+netstandard2.0"*. ⭐ **Measured: it is one file, ~270 lines, whose only coupling to the blueprint model is `DeclarationKind` used in a 3-row tuple table and a dictionary key — and it already compiles on both targets today.** Multi-targeting it is not work; it is a `.csproj` |

### ⚠ The one real cost, and how it is paid

The three v2 tag strings (`"Parameter"`, `"WorkingState"`, `"Variable"`) are `DeclarationKind`'s member
names. After extraction the new assembly must own them as literals.

📐 **Pin them, do not share them.** Declare the tags in `Hrot.Blueprints.Schema` as the authority, and
add a test in `Hrot.Blueprints.Tests` asserting `Enum.GetNames(typeof(DeclarationKind))` equals them,
in order. ⭐ **This is the pattern the programme already uses** —
`BlueprintDeclaration.MembersAParameterDoesNotCarry` is a hand-written list cross-checked by reflection
in `TaggedDeclarationTests`, precisely so the two cannot drift in either direction.

---

## Q31-B — Does the generator ever need to read v2? ⇒ ✅ **B1, and the stated downside does not exist**

⛔ **B1's *"two code paths that must agree forever"* is false against the code.** There is **one**
reader path — `BlueprintJsonServices.Deserialize` — shared by all six production read sites and by both
target frameworks. The migration framework and the DOM transform never both read a blueprint: the
framework's only blueprint consumer is `--mode migrate`, which rewrites files rather than loading them
into the model.

⭐ Under **A1** they would call the *same* transform assembly, so the question of two paths agreeing
disappears entirely rather than needing a test.

📌 **B2 stays available and is smaller than assumed** (34 files, 2 internal deps) — ⛔ but it is
unnecessary, because the generator does not need the framework, only the transform.

---

## Q31-C — What should `--mode migrate` do with a refused file? ⇒ ✅ **C2, on a corrected premise**

⛔ **§C's *"a failure mode with no way forward"* overstates it.** `MigrateMode` already:

```
catch (Exception ex) { failed++; … }        // per file
Console: "[migrate] Completed: N migrated, N skipped, N failed."
return failed > 0 ? 1 : 0;                  // non-zero exit
```

⇒ ⭐ **C1 is already implemented and behaves correctly** — one bad file is reported and counted, the
run continues over the rest, and CI fails. The gap is not the tool's behaviour; it is that the
**operator has nothing to run next**.

📐 **So: keep C1's behaviour, add C2 as an opt-in.** A `--canonicalise` flag that pipes the offending
file through `Deserialize → Serialize` before retrying. ⭐ `U-15` already did this to 58 assets and
proved it a semantic no-op via the golden harness, so the tool and its evidence both exist.

⛔ **C3 rejected, emphatically.** One of Batch 54's four refused shapes — a declaration carrying its own
`Kind` — silently moved a field between structs. **A repair whose failure mode is a blackboard wipe
must not be the default.**

---

## Q31-D — Is the bump worth doing? ⇒ ⭐ **D1 — bump, once A1 lands. Do not park indefinitely.**

⚖️ **I disagree with the coordinator's D2 lean, on three measured grounds:**

| | |
|---|---|
| **1. The risk is smaller than assessed** | 2 profiles, not 6. Non-registering hosts **throw** rather than misread. The reader already ships everywhere |
| **2. The path is trodden** | `ScenarioMigrationModule` performed this exact bump — passthrough → `RegisterDocType` with a real chain — and its tests (`CanMigrateV1ToV2`, `CanMigrateV2ToV1`, `CurrentVersion_Is2`) are the template |
| **3. D2's cost compounds silently** | ⛔ A proved-but-unreachable transform is the *"green because nothing runs it"* shape this programme has spent nine batches removing. `BP-240` is the standing warning: **a gate is only as good as the inputs that reach it.** Parked, the migrator's adversarial suite guards code no file ever meets |

⛔ **But do not bump in one commit.** Three, in order — each independently revertable until the last:

1. **A1** — extract `Hrot.Blueprints.Schema`; tag-pinning test. *No behaviour change.*
2. **The migrator + registry** — `V1ToV2_Blueprint_UnifyDeclarations` and its down twin in
   `Hrot.Common/…/Migrators/Blueprint/`; `BlueprintMigrationModule` → `RegisterDocType`,
   `CurrentVersion = 2`. ⚠ **Still nothing writes v2**, so `V2ReaderTests.TheWriterStillEmitsV1`
   stays green and the disk is untouched.
3. ⛔ **The bump** — `Serialize` stamps 2 and emits the v2 shape; rewrite all 58 assets; regenerate
   `persistence-shape.txt` **with the diff reviewed**. 🔴 **This is the irreversible one.**

⭐ **Sequencing note:** step 2 is safe *only because* step 1 makes a real migrator writable. A
`RegisterPassthroughDocType(…, 2)` at step 2 would silently treat a genuine v1 file as v2 —
`MigrationPipeline.MigrateTo` returns before any version comparison for passthrough types.

---

## 3. What I would most want checked back

1. ⚠ **M-2's intent.** My rejection of A2 rests on reading *"each host registers only the formats it
   actually loads"* as a **policy** rather than an optimisation. If it is the latter, A2 reopens.
2. ⚠ **Whether a new project is acceptable at all.** A1 is right on the code; if the solution has a
   standing rule against new assemblies, the fallback is A3 **plus** multi-targeting `Hrot.Common`,
   which is a far bigger change than A1.
3. 📌 **`BP-241`'s flag name and default** — `--canonicalise` opt-in is my call; opt-out is defensible.
