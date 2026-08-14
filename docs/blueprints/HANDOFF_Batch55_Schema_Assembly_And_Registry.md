# HANDOFF — Batch 55: ⭐⭐ **`Q31`'s steps 1 and 2 — the seam, and the real migrator**

> 📌 **Dispatched at `PENDING`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1674+` is the next free diagnostic.
>
> ⛔⛔ **STEP 3 IS NOT IN THIS BATCH.** ⭐ **Steps 1 and 2 only** — both fully revertable by
> `git revert`. The bump is held; §5 says why and what would release it.

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

## 4. Gates

**Baseline — coordinator-run at `c5550ff9`, ⭐ green both ways:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| Blueprints | **3551 / 3541 passed / 0 failed / 10 skipped** |
| AiShared **1216** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit **208 / 131** | ⛔ **none should move** |
| ⭐⭐ **Golden Tier 1 + Tier 2** | ⛔ **UNCHANGED** — steps 1-2 are not a compile change |
| ⭐⭐ **`persistence-shape.txt`** | ⛔⛔ **UNCHANGED. It moves in step 3 and only in step 3** — if it moves here, step 3 leaked |
| ⚠ **New:** `Hrot.ClusterRunner.Tests` + `Hrot.SimHost.Tests` | ⭐ **this batch is the first to touch either — record their numbers, they join the baseline** |
| `tracker-counts.py --check` | clean **twenty-three** batches running |

⚠ **Both ways** — full suite **and** isolated filters, as Batches 52-54.

---

## 5. ⛔⛔ The stop point, and it is not arbitrary

**Step 3 rewrites all 58 assets and moves `persistence-shape.txt`. It is the one irreversible act in
the programme, and two things about the ruling that authorises it are still open:**

| | |
|---|---|
| ⚠ **The ruling is engineering, not architect** | you said so yourself, plainly, in your own header. ⭐ **For steps 1-2 that is plenty** — they revert with `git revert`. ⛔ **For an irreversible rewrite of every shipped asset, the user decides whether the engineering ruling stands in for the architect** |
| ⚠ **Your check-back #2 is a solution-policy question I cannot settle from code** | *"is a new assembly acceptable at all"* — ⛔ **nothing in the repo answers it.** 📌 **Your #1 IS settled: M-2 is policy, not optimisation** — `:10` says *"Enforces"*, and T04 makes it fail-loud. Your `A2` rejection holds |

⇒ ⭐ **Build steps 1 and 2. Stop. Report.** The user is being asked both questions in parallel; if the
answers land while you are still running, **they arrive in Batch 56, never in this document** (rule 1).

---

## 6. ⚡ How to work

**Opus for step 2's fixture carry-across and the `.csproj` multi-targeting** — ⭐ **a reference that
resolves on one target and not the other is exactly the silent-half class this programme keeps
finding.** 🟢 Step 1's file move and the tag-pinning test are mirror-pattern work.

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7 — see §0) |
| **Rule 6** | the tracker is yours — ⭐ **`BP-235` closes here**; `U-10`'s row moves to *writer held, reason recorded* |

---

## 7. Reporting

⭐⭐ **`persistence-shape.txt` unchanged, stated FIRST** · **golden 42/42 both tiers unchanged** ·
⭐ **the ns2.0 target genuinely resolves the new assembly, and how you proved it** · ⭐⭐ **Batch 54's
nine fixtures behaving identically through the REGISTRY path** · **M-2's assertions still green** ·
the two new suites' numbers · ⭐ **whether `JM-P3-003` exists as a written work item** · per-suite
numbers **full and filtered** · `tracker-counts.py --check` · ⭐ **every id you allocated**.

⭐⭐⭐ **The best thing in your `Q31` answer was refusing the question's framing four times over and
measuring instead** — *"six host profiles"* was mine and it was wrong by three. ⛔ **The same
scepticism is owed to your own §0.1:** *"this has already been done once"* is the argument carrying
`D1`, and `ScenarioMigrationModule`'s bump moved **one optional field on one type**. 📐 **This one moves
every declaration in every asset.** ⭐ **Say in your report whether the precedent is as close as it
reads, or whether it is a smaller thing wearing the same shape.**
