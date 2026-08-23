<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-23
current-answer: dispatch pointer for N2–N6 — the goldens and the falsifiability proofs. Part B's session
  was stopped after N1; this is a FRESH batch, not a resumption, so the scope sha is new.
  ⛔ Carries no design: every item cites a section.
known-conflict: none. The gizmo-schema batch (ST-, Area I) may run in parallel and owns
  ModeStartupRails.cs + its own new rail file; everything else in Hrot.SystemTests is yours (§3).
-->
# HANDOFF — **the regression net, part C** *(`N2`–`N6`)*

> 📌 **Dispatched at `2166c1760` *(re-stamped — rule 1a amendment)*.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`HN-`**/**`MX-`**, tracker **Area J** — 📐 the series stands at **`HN-011`** /
> **`MX-012`**, so start at `HN-012` / `MX-013`.

## 0. ⭐⭐⭐ WHERE PART B GOT TO — **and it got further than the items say**

⭐⭐ **`N0` and `N1` are DONE and merged.** 📐 Verified on the merged tree: build **0 errors**,
`Hrot.SystemTests` **57 / 57**. ⭐ The item-zero doc debt is closed *(`HN-007`, `HN-008`, `MX-012`)*, §6 of
the design is a **CONTRACT** now, and `EditorProcess` is extracted from `EditorProcessFixture` so `N1`'s
two-process launch is not a second copy of launch-and-wait.

### ⭐⭐⭐ THE THREE THINGS `N1` ESTABLISHED — **your baseline, do not re-derive them**

| 📐 measured | ⇒ what it means for you |
|---|---|
| ⭐⭐⭐ **two fresh processes agree byte-for-byte on the entity mapping** — 8 entities, ids **1000–1007** | ⭐ **ids are stable, and no allocator reset is needed.** 📄 The premise-refutation is recorded in [`DESIGN_Deterministic_Network_Ids.md`](../../DESIGN_Deterministic_Network_Ids.md) §0b — ⛔ **charter `D6` is NOT to be built** |
| ⭐⭐⭐ **36 of 41 panel dumps are byte-identical across all four perspectives; EXACTLY 2 kinds drift** *(message-log, event-browser — both wall-clock feeds)* | ⭐⭐ **this is your golden budget's map.** ⛔ **Do not golden the two volatile kinds** — ⭐ and note a **control rail already pins the volatile set to exactly those two IN BOTH DIRECTIONS**, so a third one reddens and is named |
| 🔴🔴 **AMENDED `2026-08-23` (rule 1a, unstarted): WHY the ids repeat, and where that STOPS.** 📐 Traced: **`scenarios/hill-attack/scenario.json` CONTAINS `NetworkIdentity` `1000`–`1007`** ⇒ ⭐⭐⭐ **the ids are AUTHORED DATA, not allocations** — `LoadScenario` does `SoftClear` then restores them from the DOM, and the `AllocateId()` path sits behind `HrotEditLoadHandler`, which 🔴 **has no production construction site at all** *(tests only)*. ⛔⛔ **BUT entities SPAWNED AT RUNTIME do go through the allocator** *(`NetworkSpawningSystem`, `EditorSubsystem.cs:1102`)*, and **nothing resets it** ⇒ ⭐⭐ **a golden over a scenario that SPAWNS is safe across two fresh processes and DRIFTS across a reload in one process.** ⇒ ⛔ **`N3`: prefer FULLY AUTHORED scenarios for goldens; if a curated scenario spawns, say so and keep its golden to a fresh process.** 📄 `DESIGN_Deterministic_Network_Ids.md` §0b | ⭐ this is the one caveat that would have bitten silently, months later |
| ⚠ **`HN-011` — a reload leaks `BlueprintAssignments` onto entity `1000`** *(not a settle race — 5 and 40 ticks agree)* | ⛔⛔ **A GOLDEN CAPTURED AFTER A RELOAD WILL BAKE THIS DEFECT IN.** ⇒ ⭐⭐⭐ **capture goldens on a FIRST load in a fresh process**, and say so in the runbook |

⚠ **`HN-009`–`HN-011` were filed by the coordinator**, because the session was stopped and `HN-011` existed
only in a commit message. ⭐ **The reasoning in those rows is the previous session's, quoted** — ⛔ if any of
it reads wrong to you, **say so**; it is not yours to defend.

## 1. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`DESIGN_Regression_Net.md`](../../DESIGN_Regression_Net.md)** — §7 items **`N2`–`N6`**, **§4/§4b**
*(`D7`, the golden key)*, **§6 the capture CONTRACT**, **§8 what *prove* means**.
📄 Charter **`D5`/`D7`**: [`PROGRAMME_Unification_And_Harness.md`](../../PROGRAMME_Unification_And_Harness.md) §4.
⭐ Report per obligation ③, and ⭐⭐ **fold deviations into the design** *(obligation ⑤)* — 📌 part B did this
well; keep it.

## 2. ⭐⭐⭐ THE ITEMS

| # | task | design | the one thing not to get wrong |
|---|---|---|---|
| ⭐ **N2** | **`GoldenStore` + `PanelNormalizer`**, `PANEL_GOLDEN_CAPTURE=1` | **§7 N2** · **§4b** | ⭐ key on **`PanelId`**, not `PanelKind`. ⭐ Follow the existing `<FAMILY>_GOLDEN_CAPTURE` convention — ⛔ **do not invent a second mechanism.** ⭐⭐ **And `N1` already wrote the comparison logic** *(`DeterminismRails`)* — ⛔ **do not write a second normalizer beside it; reuse or lift** |
| ⭐⭐ **N3** | **The first slice of goldens** — ⛔ **a BUDGET, not a sweep** | **§7 N3** · **`D7`** | ⭐⭐⭐ **`D7`'s pairing rule is mandatory: every golden also carries 1–3 assertions on the fields that MEAN something.** ⭐⭐ **Spend the budget ACROSS perspectives** — 📐 `N0` measured Scenario 12 · BTree 9 · HSM 7 · Blueprint 13, with **11 panels reachable only from Blueprint**. ⛔ **Skip the 2 volatile kinds.** ⛔ **Capture on a FIRST load** *(`HN-011`)*. ⭐ State the count and what you left out |
| 🔴🔴 **N4** | ⭐⭐⭐ **THE MUTATION PROOF** — break something on purpose, confirm **exactly** the expected golden reddens, revert | **§8** | ⛔⛔ **Report it as a table: mutation → what reddened → was that expected.** ⚠ **A mutation that reddens 40 files is itself the finding.** ⭐⭐ **The standard to match is `ST-019`'s §3** *(one named commit reverted, exactly one of eight rails red, restored, re-verified)* — ⛔ **and rebuild before drawing a conclusion**; that lane's stale binary nearly inverted its own result |
| ⭐⭐ **N5** | **Behaviour assertions on the curated scenarios** | **§7 N5** | ⭐⭐⭐ **First case is falsifiable against a KNOWN defect:** *the platoon approaches the computed baseline, not the origin*. ⇒ **it must FAIL on a tree with `9aa790d57` reverted and pass now — report BOTH results.** ⛔ An assertion never seen to fail is decoration |
| ⭐ **N6** | ⚠ **`ST-017` — a `PanelSnapshot` rail was LOST with the StrideMock removal.** 📐 `PanelIds` named **five** hosts that must agree on the system-profiler kind; **four** remain | `DESIGN_Stride_Port.md` §7 | ⛔ **Do not "fix" it by lowering the expected count to four and moving on** — ⭐ that is how coverage leaves quietly. **Say what the rail now asserts and why that is equivalent** |

## 3. ⛔ LANE & SCOPE

⭐ **Yours:** `Hrot.SystemTests` — `Goldens/`, the store, the normalizer, the panel rails, the scenario
assertions, `DeterminismRails.cs`, `EditorProcess.cs`.

⚠ **A PARALLEL BATCH MAY RUN** — 📄 `HANDOFF_Gizmo_Schema.md` *(ids `ST-`, tracker **Area I**)* owns
**`ModeStartupRails.cs`** and **one new rail file of its own**. ⛔ **Do not touch either**; ⚠ it will
**remove the `--mode ig` tripwire** *(that is `ST-020` landing, not a regression)*. ⭐ **Rule 4: pull the
coordinator branch before your final commit.**

⛔ **Not this batch:** the allocator reset *(refuted — `DESIGN_Deterministic_Network_Ids.md` §0b)* ·
**`HN-011`'s fix** *(the scenario loader — wide blast radius; ⭐ the tripwire holds it visible)* ·
collapsing the duplicate nested `SequentialIdAllocator` *(⭐ real, ruling 9, ⛔ its own change)* ·
cross-host conformance · the capability manifest · anything CGF-side.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs the
dispatch sha** · a `--no-build` column · every RED confirmed pre-existing **by name** · ⭐⭐ **golden movement
as a DIFF SHAPE** *(how many goldens CREATED, and the shape of the first capture)* · `tracker-counts.py
--check` · `rulings-check.py` · `design-digest.py --check` · **the ids you allocated** *(rule 5)*.

⚠⚠ **AND RULE 5 IN PARTICULAR, without blame:** 📌 part B's commit **used `HN-009`/`HN-010`/`HN-011`** and
its tracker edit filed only three other rows ⇒ ⛔ **an OPEN DEFECT lived in a commit message.** ⭐ **File
every id you allocate, in the same commit that uses it.**

⭐⭐ **Row 8 — the integration invariant.** ⭐ **This batch IS the integration gate**: report
`bash scripts/run-system-tests.sh` *(⭐ it now covers **both** categories — `HN-009`)* **plus `N4`'s
mutation table.** 📐 **Your baseline is `57 / 57`** — ⛔ any red is yours until proven otherwise.

⚠ **Known baseline quirks — do not re-derive:** `tracker-counts.py --check` is **blind to `HN-`/`MX-` rows**.
`tools/ai-debug-mcp` `verify.mjs` fails pre-existing *(needs `npm install`)*. `Fdp.Presentation.Tests`
crashes ~18–20 cases in *(`BP-419`, `R-131`)*. `rulings-check.py` emits **2 staleness WARNs**
*(`.claude/CLAUDE.md`, `docs/projects/SOLUTION-OVERVIEW.md`)* — ⭐ already named, **not yours to fix**.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Regression_Net.md`](../../DESIGN_Regression_Net.md)** — the item
table, the golden layout as actually built, and ⭐⭐ **§6 gains the `HN-011` rule** *(capture on a first
load in a fresh process, and why)*. ⛔ Design content in the design; the report points at it.
