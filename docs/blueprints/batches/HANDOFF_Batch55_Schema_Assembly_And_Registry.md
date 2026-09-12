# HANDOFF — Batch 55: ⭐⭐⭐ **`Q31`'s THREE steps — the seam, the migrator, and the BUMP**

> 📌 **Dispatched at `f8be354c9`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1674+` is the next free diagnostic.
>
> ## ⚠⚠ AMENDED AFTER FIRST DISPATCH — read this before anything else
>
> ⛔ **This document was first dispatched at `1449d25cc` with step 3 HELD.** ⭐⭐ **The user then
> released it** *(`2026-08-14`)*: **"new assembly is fine, go ahead with step 3, assets saved in git so
> all is reversible."** ⇒ **all three steps are in this batch.**
>
> ⚠ **Rule 1 says never amend after dispatch. This is a deliberate, recorded exception, and the
> premise was CHECKED, not assumed:** `origin/claude/hrot-implementation-j1jvin` was still at
> **`70c2a87ee`** — your `Q31` answer, unchanged — when the amendment was written, so **no run had
> started against the `1449d25cc` text.** ⭐ **Re-stamped above**, so this is the dispatch point.
> 📌 **If you had already branched from `1449d25cc`, say so and stop** — that would mean the check was
> wrong, and the collision it exists to prevent is live.

---

## 0. ⚠ Rule 7, factually — not as a complaint

Your `Q31` run started at **`70c2a87ee`, whose parent is `c5550ff9f`** — this branch's *merged code*
head, not its head. ⇒ **the `#31` question document, `CLAUDE.md` rule 3a and the cross-host review were
not in the commit you built from (run starting `70c2a87ee`).** ⭐ **You answered anyway and the answer
is right**, so this costs nothing this time. 📌 **Merged at `d30fbb125`** — you now have all three.
⚠ The one thing worth noting: you named the file `_31_` without having the renumbering commit, so
**that was luck, not the protocol working.**

---

## 1. ⭐⭐ The ruling stands — coordinator-verified, so do NOT re-measure

**Every claim in your §0 was checked against the code before this handoff was written.** All six hold:

| your claim | verified at |
|---|---|
| **Five** `Build*` profiles; Blueprint in exactly **two** | `HrotMigrationBootstrap.cs:18/34/49/66/83` — `BuildEditor:54` and `BuildClusterRunnerMigrate:71` register it; **`BuildClusterRunnerCi:83-92` does not** |
| **M-2 is real and fail-loud** | `:10` *"Enforces M-2"*, `:32` *"intentionally NOT registered (M-2)"*, and `NodeBootstrapperMigrationTests` **T04** asserts `MigrateToCurrent` throws `MigrationException` naming `Hrot.Blueprints` |
| `ScenarioMigrationModule` is the template | `:22 CurrentVersion = 2` · `:33 RegisterDocType` with `V1ToV2_EntityInfo_AddTags` + `V2ToV1_EntityInfo_RemoveTags` |
| `MigrateMode` already implements **C1** | `:62` counters · `:86-89` per-file catch · `:94` the summary line · `:97` `failed > 0 ? 1 : 0` |
| the framework is **packaging, not capability** | bare `<TargetFramework>net8.0</TargetFramework>`; `BlueprintSchemaV2` already compiles on both targets |
| Blueprint is a **passthrough at 1** today | `BlueprintMigrationModule:RegisterAll` → `RegisterPassthroughDocType(Blueprint, CurrentVersion=1)` |

### 1.1 ⭐⭐ One datum NEITHER of us cited, and it supports `D1`

`BlueprintMigrationModule`'s own summary:

> *"A migration chain will be added in **`JM-P3-003`** when the Blueprint format is bumped to version 2."*

⇒ ⭐ **The bump is not a new idea this programme invented — it is a pre-existing planned work item with
an id, and this module is its declared placeholder.** 📐 **Find `JM-P3-003` if it is written down
anywhere** — if it carries acceptance criteria, they outrank both of our reasonings.

### 1.2 ⚠ Your sequencing note is right, by a slightly different mechanism — state it correctly

You wrote that a `RegisterPassthroughDocType(…, 2)` at step 2 *"would silently treat a genuine v1 file
as v2."* **Measured at `MigrationPipeline.cs:49-50`:** the passthrough arm returns
`new MigrationReport(docType, fromVersion, fromVersion, …)` — it returns **before** the
`fromVersion == targetVersion` comparison at `:53`, reporting the file at **its own** version. Combined
with `MigrateMode:141`'s `Skip("already at target")`, the consequence you describe is exactly right —
⭐ **no transform ever runs while `CurrentVersion` advertises 2** — but the file is not *relabelled*, it
is *never visited*. ⇒ **a silent no-op, not a mislabel.** 📌 **Say it the measured way in the commit**;
the distinction matters to anyone later reading why the ordering is load-bearing.

---

## 2. Step 1 — extract `Hrot.Blueprints.Schema`

| | |
|---|---|
| ⭐ **The whole of it** | multi-target `netstandard2.0;net8.0`, depending on **`System.Text.Json` and nothing else**. Move `BlueprintSchemaV2` in. ⛔ **No behaviour change whatsoever** |
| ⭐⭐ **The tag-pinning test** | declare `"Parameter"` / `"WorkingState"` / `"Variable"` in the new assembly as the authority; assert `Enum.GetNames(typeof(DeclarationKind))` equals them **in order**, in `Hrot.Blueprints.Tests`. ⭐ **Mirror `TaggedDeclarationTests`' `MembersAParameterDoesNotCarry` exactly** — you built that pattern for this reason |
| 📐 **Prove the pin bites** | ⛔ **a reflection test that has never failed is not a test.** Add or reorder a `DeclarationKind` member in a scratch run and show it reddens **naming the member** |
| ⚠ **The reference edges** | `.Compiler` (both targets) and `Hrot.Common` (net8) both reference the new assembly. 📐 **Confirm the generator's ns2.0 target actually resolves it** — a `ProjectReference` that silently drops on one target is this programme's trap #5 in `.csproj` form |

---

## 3. Step 2 — the migrator and the registry

| | |
|---|---|
| **The pair** | `V1ToV2_Blueprint_UnifyDeclarations` + its down twin, in `Hrot.Common/Scenario/Migrations/Migrators/Blueprint/` — ⭐ **the layout `ScenarioMigrationModule` already uses** |
| **The registry** | `BlueprintMigrationModule` → `RegisterDocType`, `CurrentVersion = 2`. ⛔ **Not `RegisterPassthroughDocType`** — §1.2 |
| ⭐⭐ **Nothing writes v2** | `V2ReaderTests.TheWriterStillEmitsV1` **stays green**, and that is the batch's proof that step 3 did not leak in |
| **Mirror their tests** | `CanMigrateV1ToV2` · `CanMigrateV2ToV1` · `CurrentVersion_Is2` are the named template |
| ⭐ **Carry Batch 54's nine fixtures across** | the four refusals and the four pinned survivors must behave identically **through the registry path**, not only through the transform called directly. ⛔ **That is the "existence is not wiring" check** — the transform passing in isolation says nothing about what the pipeline hands it |
| ⚠ **The M-2 assertions must still hold** | `NodeBootstrapperMigrationTests` T04 and the `DoesNotContain` cases are the rail that says the blast radius stayed at 2. ⛔ **If registering a real chain moves them, stop** |

---

## 3.5 ⛔⛔ Step 3 — **THE BUMP.** The irreversible one

⭐ **Released by the user.** 📌 **Their reasoning, recorded verbatim so a later reader can weigh it:**
*"assets saved in git so all is reversible."* ⚠ **True for the repo's 58 — and the coordinator's one
caveat, which does not block you:** git does not cover a `.bp.json` **outside** this repo — a
designer's working file, or a deployed asset — written as v2 by a newer editor and then read by an
older build. ⭐ **That is exactly what the down-migrator is for, and step 2 puts it on the registry
chain** ⇒ covered, but by different machinery than git. 📐 **Say in your report that you exercised it.**

| | |
|---|---|
| **The change** | `BlueprintJsonServices.Serialize` stamps `$meta.schemaVersion = 2` and emits the v2 shape |
| **The sweep** | rewrite all **58** (42 corpus + 16 recipes) — ⭐ **`U-15`'s canonicalisation pass is the precedent and the tool** |
| ⭐⭐ **Invert the stop marker, do NOT delete it** | `V2ReaderTests.TheWriterStillEmitsV1` is the test that made Batch 54's deliberate stop **auditable**. ⛔ **Quietly deleting it erases the record that the stop was deliberate.** 📐 **Flip it to `TheWriterNowEmitsV2` in the same commit**, so the history reads as a decision rather than a disappearance |
| **`CurrentVersion` ⇄ `$meta`** | the two numbers must **agree** at the end — Batch 54's live inconsistency closes here |

### 3.5.1 ⭐ Order within step 3 — the one thing that must not be flipped

⛔ **Steps 1 → 2 → 3, in that order, as separate commits.** ⭐ **Step 2 is safe *only because* step 1
makes a real migrator writable** — a `RegisterPassthroughDocType(…, 2)` at step 2 would leave every v1
file **never visited** (§1.2). ⇒ **if step 1 does not land clean, stop; do not proceed to 2 or 3.**

### 3.5.2 `BP-241` — the operator's way forward, per `Q31-C2`

⭐ **The bump is what makes a refusal reachable in production**, so shipping it without an answer is
the gap `BP-241` names. 📐 **Add `--canonicalise` to `--mode migrate` as an OPT-IN** — pipe the
offending file through `Deserialize → Serialize` and retry. ⛔ **Not the default** — Batch 54's
`Kind`-carrying declaration is a repair whose failure mode is a **blackboard wipe**.
⚖️ **If this makes the batch too large, it is the one item to split out** — say so rather than
half-doing it. ⛔ **The other three steps are not separable.**

---

## 4. Gates

**Baseline — coordinator-run at `c5550ff9`, ⭐ green both ways:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| Blueprints | **3551 / 3541 passed / 0 failed / 10 skipped** |
| AiShared **1216** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit **208 / 131** | ⛔ **none should move** |
| 🔴🔴 **`StructureHash` unchanged for EVERY shipped asset** | ⛔⛔ **the no-blackboard-wipe gate. A failure here re-initialises every deployed entity's state.** ⭐ **State it FIRST in the report** |
| ⭐⭐ **Golden Tier 1 + Tier 2** | ⛔ **UNCHANGED, through all three steps.** ⚠ **The on-disk shape changes; the compiled output must not — that separation is the whole claim** |
| ⭐⭐ **`persistence-shape.txt`** | ⛔ **unchanged after steps 1-2** *(if it moves there, step 3 leaked early)*; 📌 **it MOVES at step 3 — once, deliberately, with the diff REVIEWED and described in the commit.** ⭐ **This is the one gate in the programme that guards persistence; a silent regeneration is unauditable later** |
| ⭐⭐ **`v1 → v2 → v1` byte-identical on all 58** | ⭐ **re-run through the REGISTRY chain**, not the transform called directly — Batch 49 proved the transform, step 2 changes what invokes it |
| ⭐ **`--mode migrate` end to end** | on a real v1 file, through `BuildClusterRunnerMigrate`. ⛔ **"The migrator is registered" is not "the migrator runs"** — trap #5, and §3's *existence is not wiring* |
| 🔴 **The revert story** | ⛔ **`git revert` alone does not undo step 3 for anything outside this repo — the DOWN-migrator is that revert.** ⭐ **Prove it works through the registry chain, not against Batch 49's direct call** |
| ⚠ **New:** `Hrot.ClusterRunner.Tests` + `Hrot.SimHost.Tests` | ⭐ **this batch is the first to touch either — record their numbers, they join the baseline** |
| `tracker-counts.py --check` | clean **twenty-three** batches running |

⚠ **Both ways** — full suite **and** isolated filters, as Batches 52-54.

---

## 5. ✅ Your three check-backs — all answered

| | |
|---|---|
| ✅ **#1 — is `M-2` policy or optimisation?** | ⭐ **POLICY, settled from the code by the coordinator.** `HrotMigrationBootstrap:10` says *"**Enforces** M-2"*, and `NodeBootstrapperMigrationTests` **T04** makes it **fail-loud**. ⇒ **your `A2` rejection holds** |
| ✅ **#2 — is a new assembly acceptable at all?** | ⭐⭐ **YES — user ruling, `2026-08-14`: *"new assembly is fine."*** ⇒ **`A1` proceeds; the `A3`-plus-multi-target fallback is dead** |
| ✅ **#3 — `--canonicalise` default** | ⭐ **your call taken: OPT-IN.** See §3.5.2 |

⇒ ⛔ **There is no stop point in this batch.** ⭐ **The only thing that stops you is a RED gate** —
and §3.5.1's ordering: **if step 1 does not land clean, do not proceed to 2 or 3.**

---

## 6. ⚡ How to work

**Opus for step 2's fixture carry-across, the `.csproj` multi-targeting, and ⛔ all of step 3** —
⭐ **a reference that resolves on one target and not the other is exactly the silent-half class this
programme keeps finding**, and 🔴🔴 **step 3's failure mode is every deployed entity's blackboard.**
🟢 Step 1's file move and the tag-pinning test are mirror-pattern work.

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7 — see §0) |
| **Rule 6** | the tracker is yours — ⭐⭐ **`BP-235` closes · `U-10` CLOSES · `BP-241` closes with §3.5.2** |

---

## 7. Reporting

🔴🔴 **`StructureHash` unchanged for all 42, stated FIRST** · ⭐⭐ **the `persistence-shape.txt` diff —
what changed and why** · **golden 42/42 both tiers unchanged** · ⭐ **the ns2.0 target genuinely
resolves the new assembly, and how you proved it** · ⭐⭐ **Batch 54's nine fixtures behaving
identically through the REGISTRY path** · ⭐ **`v1→v2→v1` byte identity through the registry chain** ·
⭐ **that `--mode migrate` actually RAN on a real v1 file** · **M-2's assertions still green** ·
⭐ **the `$meta.schemaVersion` / `CurrentVersion` agreement** · the two new suites' numbers ·
⭐ **whether `JM-P3-003` exists as a written work item** · per-suite numbers **full and filtered** ·
`tracker-counts.py --check` · ⭐ **every id you allocated**.

⭐⭐⭐ **The best thing in your `Q31` answer was refusing the question's framing four times over and
measuring instead** — *"six host profiles"* was mine and it was wrong by three. ⛔ **The same
scepticism is owed to your own §0.1:** *"this has already been done once"* is the argument carrying
`D1`, and `ScenarioMigrationModule`'s bump moved **one optional field on one type**. 📐 **This one moves
every declaration in every asset.** ⭐ **Say in your report whether the precedent is as close as it
reads, or whether it is a smaller thing wearing the same shape.**
