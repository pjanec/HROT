<!--STATUS
state: CLOSED
build-state: PARTIALLY BUILT — N0's doc debt + N1 delivered, then the session was STOPPED by the user
superseded-by: HANDOFF_Regression_Net_Part_C.md   (N2–N6 re-dispatched as a FRESH batch)
updated: 2026-08-23
current-answer: dispatch pointer for the REST of the regression net — N1..N6 of DESIGN_Regression_Net.md,
  which the first run did not reach, plus the doc debt N0 left behind (§0b). ⛔ Carries no design: every
  item cites a section.
known-conflict: none. The parallel runner batch has CLOSED (ST-019/020/021 merged); Hrot.SystemTests is
  now yours alone. ⚠ But read §3 — it landed XvfbDisplay.cs and ModeStartupRails.cs in your project.
-->
# ⛔ CLOSED — **the regression net, part B** *(`N1`–`N6`)*

> ⛔⛔ **CLOSED `2026-08-23`, part-delivered.** ✅ **Item zero + `N1` shipped** *(`HN-007`, `HN-008`,
> `HN-009`, `HN-010`, `HN-011`, `MX-012`)*; the user then **paused and stopped** the session.
> ⇒ ⭐ **`N2`–`N6` are re-dispatched as a FRESH batch, not a resumption** — 📄
> **[`HANDOFF_Regression_Net_Part_C.md`](HANDOFF_Regression_Net_Part_C.md)**, with `N1`'s measurements as
> the new baseline. ⛔ **Do not resume this file: its scope sha and its `N1` row are both out of date**
> *(`N1` refuted charter `D6` — 📄 `DESIGN_Deterministic_Network_Ids.md` §0b)*.

> 📌 **Dispatched at `7677478f4`.** ⛔ **Scope FROZEN at that sha.** ⭐ Re-sync from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`HN-`**/**`MX-`**, tracker **Area J**. ⭐ **You allocate them** *(rule 3)* — 📐 the
> series stands at **`HN-006`** and **`MX-011`**, so start at `HN-007` / `MX-012`.

## 0. ⭐⭐⭐ WHAT THE FIRST RUN DID — **credit where it is due, then the gap**

⭐⭐ **`N0` landed, and it landed a finding worth more than the item.** 📐 Verified on merge: the AI-debug
job queue drained **one line after** `PanelSnapshot.ClearCaptured()`, so `captured` was **structurally
always empty** for every out-of-band reader and `GET /panels/{id}` answered `null` for every panel that
exists. ⭐⭐⭐ **Every golden in this design would have been a golden of nothing.** Finding it *before*
writing one is exactly the order §8 asks for, and the `staleness` field that described the pre-`MX-006`
world was corrected in the same pass.

⭐⭐ **And the cross-lane proof neither session could see:** the parallel runner batch reported
`PanelSnapshotTests.A_panels_model_can_be_read_and_a_field_asserted` as a **pre-existing red**, confirmed
at two shas. 📐 **On the merged tree `Hrot.SystemTests` is `52 / 0`** — ⭐ that red *was* this defect, and
the fix closed it. ⛔ Two lanes measured the same failure; one explained it.

### 0b. ⛔⛔ THE GAP — **`N1`–`N6` are unbuilt, and `N0`'s paperwork is missing**

⚠ **Stated without blame, because the technical call was right** — stopping to fix a defect that made the
whole net vacuous beats delivering six items over a dead read path. ⛔ **But the batch is INCOMPLETE**, and
these five are **item zero of this batch**, before `N1`:

| ⛔ | what is missing | rule |
|---|---|---|
| **①** | ⭐⭐⭐ **`HN-xxx` IS IN PRODUCTION CODE.** 📐 `EditorSubsystem.cs:2038` carries a literal `HN-xxx` placeholder ⇒ **allocate the real id and backfill it** | rule 3 / 5 |
| **②** | ⭐⭐ **tracker rows in Area J** for the frame-order fix, the `staleness` correction and `N0` | rule 6 |
| **③** | ⭐⭐⭐ **the as-built folded into [`DESIGN_Regression_Net.md`](../../DESIGN_Regression_Net.md)** — ⛔⛔ **§6's capture protocol is now LOAD-BEARING, not advice.** *"act, step a tick, then read"* was a convention; 📐 it is now the **contract**, because the reader provably sees the **previous** frame. ⭐ Say so in §6, and record `N0`'s seam choice *(`IPerspectiveSwitcher` extended, not a second seam)* in §7 | **obligation ⑤** |
| **④** | ⭐ **a `REPORT_*.md` with the §Gates table** — ⛔ the commit message is not a gate table, and 📌 **this is the second lane in two rounds to deliver gates only outside the repo.** ⭐ The runner batch's `REPORT_Runner_Tick_And_Mode_Rails.md` §2 is the model: copy its shape | rule 8 |
| **⑤** | ⭐ **name the ids you allocated** | rule 5 |

⭐ **What I verified myself, so you need not re-run it:** combined build **0 errors / 62 warnings**;
`Hrot.SystemTests` **52 / 0**; `tracker-counts --check` OK *(open 99 / done 333)*; `rulings-check`
**24/24** *(2 staleness WARNs, both already named by the runner batch)*; `design-digest --check` clean.

## 1. ⭐⭐⭐ THE ITEMS — **`N1` STILL GATES EVERYTHING AFTER IT**

⭐ Full text in the design; this table is the dispatch, not the spec. ⛔ **Unchanged from the first
dispatch except where a row says so** — re-read §3, §4 and §8.

| # | task | design | the one thing not to get wrong |
|---|---|---|---|
| 🔴🔴 **N1** | ⭐⭐⭐ **THE DETERMINISM RAIL, BEFORE ANY GOLDEN EXISTS.** Wire the id-allocator reset on `WorldResetEvent`, then load one scenario in **two fresh processes** and diff the whole id→entity mapping **and** every captured panel dump | **§7 N1** · **D6 + its four caveats** | ⛔⛔ **If it is not byte-identical, FIND THE SOURCE AND FIX IT. Do NOT widen an ignore-list to make it green** — 📌 D6 caveat ① exists for that temptation. ⚠ caveat ③ *(`Reset()` defaults to `0`, the editor's allocator starts at `1000`)* · caveat ④ *(a private nested `SequentialIdAllocator` shadows the real one)*. ⭐⭐ **AND NOW: the capture you diff must respect §6's frame rule** — a same-frame read is the empty prefix, which would diff as "identical" and prove nothing |
| ⭐ **N2** | **`GoldenStore` + `PanelNormalizer`**, `PANEL_GOLDEN_CAPTURE=1` | **§7 N2** · **§4b** | ⭐ key on **`PanelId`**, not `PanelKind` *(§4b)*. ⭐ Follow the existing `<FAMILY>_GOLDEN_CAPTURE` convention — ⛔ do not invent a second mechanism |
| ⭐⭐ **N3** | **The first slice of goldens** — ⛔ **a BUDGET, not a sweep** | **§7 N3** · **D7** | ⭐⭐ **D7's pairing rule is mandatory: every golden also carries 1–3 assertions on the fields that MEAN something.** ⛔ A golden without them is a re-bless waiting to happen. ⭐⭐ **`N0` measured the reach — Scenario 12 · BTree 9 · HSM 7 · Blueprint 13, with 11 panels reachable only from Blueprint.** ⇒ **spend the budget ACROSS perspectives**, not all inside `Scenario`. ⭐ State the count and what you left out |
| 🔴🔴 **N4** | ⭐⭐⭐ **THE MUTATION PROOF** — break something on purpose, confirm **exactly** the expected golden reddens, revert | **§8** | ⛔⛔ **Report it as a table: mutation → what reddened → was that expected.** ⚠ **A mutation that reddens 40 files is itself the finding.** ⭐⭐ **The runner batch's §3 is the standard to match**: it reverted one named commit, showed **exactly one** of eight rails redden, restored, re-verified. ⭐ And it caught a **stale-binary** near-miss doing it — ⛔ **rebuild before drawing a conclusion** |
| ⭐⭐ **N5** | **Behaviour assertions on the curated scenarios** | **§7 N5** | ⭐⭐⭐ **First case is falsifiable against a KNOWN defect:** *the platoon approaches the computed baseline, not the origin*. ⇒ **it must FAIL on a tree with `9aa790d57` reverted and pass now — report BOTH results.** ⛔ An assertion never seen to fail is decoration |
| ⭐ **N6** | ⚠ **`ST-017` — a `PanelSnapshot` rail was LOST with the StrideMock removal.** 📐 `PanelIds` named **five** hosts that must agree on the system-profiler kind; **four** remain | `DESIGN_Stride_Port.md` §7 | ⛔ **Do not "fix" it by lowering the expected count to four and moving on** — ⭐ that is how coverage leaves quietly. **Say what the rail now asserts and why that is equivalent** |

## 2. ⭐⭐ WHY §8 STILL MATTERS MORE THAN THE GOLDENS

⭐⭐⭐ **A golden nobody has ever seen fail is indistinguishable from one that is not wired up** — and
📌 **`N0` just proved that in the strongest possible form:** the read path was dead, every suite was green,
and a golden written last week would have captured an empty set and passed forever.

⭐⭐ **The tally now has a fourth entry.** Batches 94–101: not one defect caught by the ~8 000 existing
regression tests. This week: the `--mode all` crash *(user)*, the hill-attack `(0,0)` platoon *(user)*,
`--mode ig` dead in bootstrap *(a **new rail**, first run)*, the dead panel read path *(a **new rail**,
`Assert.NotEmpty(captured)`)*. ⇒ ⭐⭐⭐ **two of four found by rails written for the item — which is the
whole argument for `N4` and `N5` being falsifiability requirements and not paperwork.**

## 3. ⛔ LANE & SCOPE — **the parallel batch has CLOSED**

⭐ `Hrot.SystemTests` is **yours alone** now. ⚠ **But the runner batch landed in it — do not be surprised
and do not undo:**

| ⭐ new in your project | |
|---|---|
| **`XvfbDisplay.cs`** | ⭐⭐ the Xvfb ownership **extracted out of `EditorProcessFixture`** *(≈55 lines removed there)* so it could be reused rather than copied. ⛔ **Use it** if `N1` needs a second headless process — ⚠ `xvfb-run` stops its server from an EXIT trap `Process.Kill` never runs, so a hand-rolled second launcher **leaks a display per run** |
| **`ModeStartupRails.cs`** | 8 mode rails. ⛔ **Do not touch**; ⚠ `ig` is a **tripwire** case that asserts the mode is *still* broken and **fails the day it is fixed**, naming `ST-020`. ⭐ That is deliberate *(`R-131`)* |

⭐⭐ **`N1` needs two fresh processes — that is what `XvfbDisplay` was extracted for.** ⛔ **Prior art
first**: read it before writing any process launch.

⛔ **Not this batch:** cross-host conformance · the capability manifest · anything CGF-side · ⛔ **`ST-020`**
*(`--mode ig`)* — 📄 that is now **[`Architect_Question_52_Gizmo_Family_Composition.md`](../Architect_Question_52_Gizmo_Family_Composition.md)**, `build-state: DESIGN`, awaiting the user. ⚠ **Do not fix it, and do not
disarm the tripwire.**

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs the
dispatch sha** · a `--no-build` column · every RED confirmed pre-existing **by name** · ⭐⭐ **golden movement
as a DIFF SHAPE** *(how many goldens CREATED, and the shape of the first capture)* · `tracker-counts.py
--check` · `rulings-check.py` · `design-digest.py --check` · **the ids you allocated**.

⭐⭐ **Row 8 — the integration invariant.** ⭐ **This batch IS the integration gate**: report
`bash scripts/run-system-tests.sh` with its filter and the Xvfb launch, **plus `N4`'s mutation table**.
📐 **Your baseline is `52 / 0`** — ⛔ so *any* red is yours until proven otherwise, which is a better
starting position than the last two batches had.

⚠ **Known baseline quirks — do not re-derive:** `tracker-counts.py --check` counts only `**BP-` rows ⇒ **it
is blind to your `HN-`/`MX-` rows.** `tools/ai-debug-mcp` `verify.mjs` fails pre-existing *(needs `npm
install`; `node_modules` is gitignored)*. `Fdp.Presentation.Tests` crashes ~18–20 cases in *(`BP-419`,
`R-131`)*. `rulings-check.py` emits **2 staleness WARNs** *(`.claude/CLAUDE.md`, `docs/projects/SOLUTION-OVERVIEW.md`)* —
⭐ quotes still match, already named, **not yours to fix**.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Regression_Net.md`](../../DESIGN_Regression_Net.md)** — the item table,
§6's frame contract *(§0b ③)*, and **especially anything `N1` taught you about determinism**, which is the
fact the rest of the programme will lean on. ⛔ Design content in the design; the report points at it.
