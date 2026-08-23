<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-23
current-answer: dispatch pointer for the regression net — N0..N5 of DESIGN_Regression_Net.md, plus ST-017's
  lost PanelSnapshot rail folded in as coverage. ⛔ Carries no design: every item cites a section.
known-conflict: none. Runs in parallel with HANDOFF_Runner_Tick_And_Mode_Rails.md; both touch
  Hrot.SystemTests and the split is by FILE (§3).
-->
# HANDOFF — **the regression net** *(charter steps 2–3)*

> 📌 **Dispatched at `5963fffd4`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`HN-`**/**`MX-`**, tracker **Area J** — ⛔ never `ST-` *(the parallel runner batch owns
> those)*, never `BP-`. ⭐ **You allocate the ids** *(rule 3)*.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`docs/DESIGN_Regression_Net.md`](../../DESIGN_Regression_Net.md)** — `READY-TO-BUILD`, and it carries
**three diagrams** *(the component view · the class view · the capture sequence)*, the **items `N0`–`N5`** with
their dependency order, and **§8, which defines what *"prove the harness works"* means.**

⛔⛔ **Read §3, §4 and §8 before writing code.** ⭐ Report per obligation ③: *"the design carries N classes and
M sequences; what I built matches / deviates HERE and why."* ⭐⭐ **A deviation is a finding you argue in the
report AND fold back into the design** *(obligation ⑤)*.

📄 Also binding: charter **D5** *(granularity)*, **D6** *(deterministic ids)*, **D7** *(the pairing rule)* —
[`PROGRAMME_Unification_And_Harness.md`](../../PROGRAMME_Unification_And_Harness.md) §4.
📄 The runbook you are implementing: [`TESTING_Harness_And_Goldens.md`](../../TESTING_Harness_And_Goldens.md).

## 1. ⭐⭐⭐ THE ITEMS — **`N1` GATES EVERYTHING AFTER IT**

⭐ Full text in the design; this table is the dispatch, not the spec.

| # | task | design | the one thing not to get wrong |
|---|---|---|---|
| ⭐⭐ **N0** | **`GET /perspectives` + `POST /perspective {name}`** on the DebugApi | **§7 N0** | ⭐ **`A0` has LANDED** *(`BP-488`)*, so the validation is a **delegation** to `GetPerspectives()`, ⛔ not a reimplementation. ⭐ And `BP-489` changed how the startup perspective resolves — **read it before assuming** |
| 🔴🔴 **N1** | ⭐⭐⭐ **THE DETERMINISM RAIL, BEFORE ANY GOLDEN EXISTS.** Wire the id-allocator reset on `WorldResetEvent`, then load one scenario in **two fresh processes** and diff the whole id→entity mapping **and** every captured panel dump | **§7 N1** · **D6 + its four caveats** | ⛔⛔ **If it is not byte-identical, FIND THE SOURCE AND FIX IT.** ⛔ **Do NOT widen an ignore-list to make it green** — 📌 D6 caveat ① exists for exactly that temptation. ⚠ Mind caveat ③ *(`Reset()` defaults to `0` while the editor's allocator starts at `1000`)* and caveat ④ *(a private nested `SequentialIdAllocator` shadows the real one)* |
| ⭐ **N2** | **`GoldenStore` + `PanelNormalizer`**, `PANEL_GOLDEN_CAPTURE=1` | **§7 N2** · **§4b** | ⭐ key on **`PanelId`**, not `PanelKind` *(§4b)*. ⭐ Follow the existing `<FAMILY>_GOLDEN_CAPTURE` convention — ⛔ do not invent a second mechanism |
| ⭐⭐ **N3** | **The first slice of goldens** — ⛔ **a BUDGET, not a sweep** | **§7 N3** · **D7** | ⭐⭐ **D7's pairing rule is mandatory: every golden also carries 1–3 assertions on the fields that MEAN something.** ⛔ A golden without them is a re-bless waiting to happen. ⭐ **State the count and what you left out** |
| 🔴🔴 **N4** | ⭐⭐⭐ **THE MUTATION PROOF** — break something on purpose, confirm **exactly** the expected golden reddens, revert | **§8** | ⛔⛔ **Report it as a table: mutation → what reddened → was that expected.** ⚠ **A mutation that reddens 40 files is itself the finding** *(the goldens are coupled to something they should not see)* |
| ⭐⭐ **N5** | **Behaviour assertions on the curated scenarios** | **§7 N5** | ⭐⭐⭐ **First case is falsifiable against a KNOWN defect:** *the platoon approaches the computed baseline, not the origin*. ⇒ **it must FAIL on a tree with `9aa790d57` reverted and pass now — report BOTH results.** ⛔ An assertion never seen to fail is decoration |
| ⭐ **N6** | ⚠ **`ST-017` folded in — a `PanelSnapshot` rail was LOST with the StrideMock removal.** 📐 `PanelIds` named **five** hosts that must agree on the system-profiler kind; **four** remain. ⭐ **Restore equivalent coverage** *(the floor "more than zero kinds" still holds, so this is coverage, not a defect)* | `DESIGN_Stride_Port.md` §7 *(`ST-017`)* | ⛔ **Do not "fix" it by lowering the expected count to four and moving on** — ⭐ that is how coverage leaves quietly. **Say what the rail now asserts and why that is equivalent** |

## 2. ⭐⭐ WHY §8 MATTERS MORE THAN THE GOLDENS

⭐⭐⭐ **A golden nobody has ever seen fail is indistinguishable from one that is not wired up.** 📌 The tally
this programme keeps: batches 94–101, **not one defect** caught by the ~8 000 existing regression tests —
every one found by a **new rail written for that item**, or **by the user opening the editor.**

⚠⚠ **And it happened again this week, twice:** the `--mode all` strict-mode crash and the hill-attack
`(0,0)` platoon were **both** found by the user, **both** with every suite green. ⇒ ⭐ **`N4` and `N5`'s
falsifiability requirements are the whole point of this batch.** ⛔ A green net is not the deliverable; **a net
shown to go red on demand** is.

## 3. ⛔ LANE & SCOPE — **a parallel batch shares your project**

⚠⚠ 📄 `HANDOFF_Runner_Tick_And_Mode_Rails.md` *(ids `ST-`, tracker **Area I**)* also touches
`Hrot.SystemTests`. ⭐⭐ **The split is by FILE:**

| ⭐ yours | ⛔ theirs |
|---|---|
| `Goldens/` · the golden store · the normalizer · the determinism rail · the panel rails · the scenario assertions · `DebugApi/` for `N0` | ⛔ **the mode rails** *(one new file: "every mode starts and ticks")* · `Hrot.NodeComposition` |

⭐ **Rule 4: pull the coordinator branch before your final commit** — they will have landed in the same
project. ⛔ **Do not add a golden for them, and do not touch `Hrot.NodeComposition`.**

⛔ **Not this batch:** cross-host conformance · the capability manifest · anything CGF-side. 📄 Those are
`DESIGN_Headless_Testability.md`'s, and **this net is their prerequisite** *(design §1)*.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs the
dispatch sha** · a `--no-build` column · every RED confirmed pre-existing **by name** · ⭐⭐ **golden movement
as a DIFF SHAPE** *(and for this batch that means: how many goldens were CREATED, and the shape of the first
capture)* · `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · **the ids you
allocated** *(rule 5)*.

⭐⭐ **Row 8 — the integration invariant.** ⭐ **This batch IS the integration gate**, so report the harness
suite *(`bash scripts/run-system-tests.sh`)* with its filter and the Xvfb launch, **plus `N4`'s mutation
table**, which is the row that proves the rest means anything.

⚠ **Known baseline quirks — do not re-derive:** `tracker-counts.py --check` counts only `**BP-` rows ⇒ **it is
blind to your `HN-`/`MX-` rows.** `tools/ai-debug-mcp` `verify.mjs` fails pre-existing *(needs `npm install`;
`node_modules` is gitignored)*. `Fdp.Presentation.Tests` crashes ~18–20 cases in *(`BP-419`, `R-131`)*.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Regression_Net.md`](../../DESIGN_Regression_Net.md)** — the item table,
and **especially anything `N1` taught you about determinism**, which is the fact the rest of the programme
will lean on. ⛔ Design content in the design; the report points at it.
