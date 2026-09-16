<!--STATUS
state: LIVE
build-state: BUILT
updated: 2026-08-24
current-answer: §1 what shipped · §2 obligation ③ (the deviations, all six folded into the design) ·
  §3 the §Gates contract · §4 the MUTATION TABLE · §5 the ids allocated · §6 what is NOT covered.
design-basis: 📄 docs/DESIGN_Regression_Net.md §7 (N2–N6), §7b (the AS-BUILT this batch wrote), §4/§4b
  (D7 and the golden key), §6 (the capture contract), §8/§8b (what "prove" means) ·
  docs/blueprints/batches/HANDOFF_Regression_Net_Part_C.md (dispatched 07fad323e).
known-rot: none. ⚠ This report is EPHEMERAL — the durable record is §7b/§8b of the design.
-->
# REPORT — **the regression net, part C** *(`N2`–`N6`)*

> 🔒 **User, `2026-08-24`:** *"we are already refactoring without the harness… we need to focus on the
> harness."*

⛔⛔ **This report is EPHEMERAL.** ⭐⭐⭐ **The durable record is
[`DESIGN_Regression_Net.md`](../../DESIGN_Regression_Net.md) §7b *(the as-built and its six deviations)* and
§8b *(the mutation table)*** — both written **before** this batch closed, per obligation ⑤. ⭐ Read those.

## 1. ⭐⭐ WHAT SHIPPED

| item | ⭐ |
|---|---|
| ✅ **`N2`** | `Goldens/PanelNormalizer.cs` · `Goldens/GoldenStore.cs` · `Goldens/GoldenCaptureFixture.cs` — `PANEL_GOLDEN_CAPTURE=1`, `Goldens/<scenario>/<panelId>.json`, canonical key order, and a diff that **names the JSON path**. ⭐ Follows the house `<FAMILY>_GOLDEN_CAPTURE` convention *(`EqsGolden`)* rather than inventing a second mechanism |
| ✅ **`N3`** | `PanelGoldenRails.cs` + **6 goldens** across **all four** perspectives, each paired per `D7`, plus **3 control rails** |
| ✅ **`N4`** | §4 below — **two mutations, each reddening exactly one case** |
| ✅ **`N5`** | `PlatoonBaselineRails.cs` — *the platoon approaches the computed baseline, not the origin*, **shown red on the reverted tree** |
| ✅ **`N6`** | `CrossHostPanelKindRails.cs` — `ST-017`'s replacement as a **structural** claim plus an **enumeration**, ⛔ not "four instead of five" |

⭐ **Suite: `58 → 76` cases, 76/76 green.**

## 2. ⭐⭐⭐ OBLIGATION ③ — **the design's UML was checked, and the build deviated in SIX places**

⭐ §5 carried a `classDiagram`, a `graph TD` and a `sequenceDiagram`; all three were read before building.
⛔ **Six deviations, every one folded into [§7b](../../DESIGN_Regression_Net.md) with its measurement** — the
report points at the durable record rather than being it:

| # | short form |
|---|---|
| **①** | ⛔ **a panel id can contain a SLASH** *(`editor/_gizmo`)* ⇒ §4b's literal layout threw on the first capture; ids are encoded `/`→`~` with an **injectivity rail** |
| **②** | ⚠⚠ §4's *"golden the 200-row structures"* yields almost nothing here: **2 of 41** dumps exceed 10 KB and **both are excluded** ⇒ the rule is about SEMANTIC DENSITY, not bytes |
| **③** | ⭐⭐⭐ the ignore-list is **EMPTY, measured** *(one panel carries a path and a timestamp, and it is already a declared-volatile kind)*, with a rail that **re-derives** that from the committed goldens |
| **④** | ⭐⭐ `HN-011` forced a **fourth capture rule**: first load, fresh process — a golden captured after a reload bakes the leak in |
| **⑤** | ⛔⛔ **§9 `Q1` overturned by measurement** — on the shared editor the `R-132` assertion **passed in the full suite while failing in its own process** ⇒ `N5` owns a fresh process |
| **⑥** | ⭐⭐⭐ `N6`'s kind agreement is now **structural** *(no host can pass a kind)* plus an **enumeration of the four host sites**, which is stronger than the per-host rail that was lost |

⭐ **The as-built UML was corrected too:** the pre-build class diagram carried a **`MutationProof` class** and
a `ScenarioAssertions` class. ⛔ Neither exists nor should — `N4` is a method plus a table, and a class named
after a proof would be a rail that cannot fail.

## 3. ⭐⭐⭐ §GATES — **the standing contract**

| # | gate | verbatim command | `--no-build`? | result · delta vs `07fad323e` |
|---|---|---|---|---|
| 1 · 8 | ⭐⭐⭐ **THE INTEGRATION GATE — this batch IS it** | `bash scripts/run-system-tests.sh` | builds | ⭐ **76 / 76 pass, 0 fail, 0 skip** *(baseline `58/58` ⇒ **+18**, all new: 6 golden theories + 6 pairing cases + 3 controls + `N5` + 2 `N6`)*. ⭐ Run **five** times this batch *(2 mutated, 3 clean)* |
| 1 | build | `dotnet build IOS-IG-SimHost.sln --no-restore` | must build | ⭐ **succeeded, 0 errors** *(4 times — every mutation and every restore)* |
| 2 | out-of-solution / stale bin | — | — | ⭐ `Hrot.SystemTests` is **in** the solution by design; every `--no-build` run followed a build of the same tree. ⚠⚠ **The mutation runs were verified against the BINARY timestamp, not the source** — 📌 `ST-019`'s stale-binary near-miss is why |
| 3 | ⭐⭐ **golden movement as a DIFF SHAPE** | `git status` · `wc -c Goldens/hill-attack/*` | — | ⭐ **6 goldens CREATED, 0 modified, 0 deleted.** Shape of the first capture: **434 B – 1 450 B each, 5 895 B total**; ⛔ **no wall-clock field, no absolute path, no frame counter** *(asserted by a rail, not by eye)*; keys sorted, LF endings, one trailing newline |
| 4 | every RED pre-existing, by name | — | — | ⭐ **no reds on the clean tree.** ⚠ One flake, filed as **`HN-023`**: `DeterminismRails.Two_fresh_processes_agree_on_the_entity_mapping` failed in **1 of 4** full-suite runs and passed in isolation twice — ⛔ **not filtered, not skipped** *(`R-131`)* |
| 5 | working tree clean after every suite | `git status --short` | — | ⭐ clean — only the batch's own 4 new paths *(3 rails + `Goldens/`)*. ⭐⭐ **`ScenarioBehaviorTests.cs` ends byte-identical to the dispatch sha** *(the `N5` case was written there, then moved out — deviation ⑤)* |
| 6 | quarantine counts | — | — | ⭐ **0 skips before, 0 skips after.** ⛔ No new filter, no `[Skip]` |
| 7 | doc gates + ids | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` | — | ⭐ **OK (open 99 / done 333)** · **24/24 verified** *(the 2 known staleness WARNs — not mine)* · **all 85 designs OK, buildable designs carry both diagrams** · **3/3 mermaid blocks parse** |

⚠ **Known baseline quirks, unchanged and not re-derived:** `tracker-counts.py` is blind to `HN-`/`MX-` rows ·
`tools/ai-debug-mcp` `verify.mjs` needs `npm install` · `Fdp.Presentation.Tests` crashes ~18–20 cases
*(`BP-419`)* · the 2 `rulings-check` WARNs.

## 4. ⭐⭐⭐ THE MUTATION TABLE — **`N4`**

📄 **The durable copy is [§8b](../../DESIGN_Regression_Net.md).** Baseline: **76/76 green**.

| # | mutation *(reverted after)* | what reddened | expected? |
|---|---|---|---|
| **①** | **`9aa790d57` reverted** — `ApplyResolverOverlay` back to `if (def.ParseParams == null)`, restoring the `R-132` defect | ⭐ **exactly ONE case** — `PlatoonBaselineRails`; 75 passed. Message: *"closest 98.5 m, was 77.6 m before the run; distance to the local origin went 613.5 m → **2.6 m**"* | ✅ yes |
| **②** | **one UN-ASSERTED panel field flipped** — `EditorOrbatAdapter.CanAcceptSubordinates` → `false` | ⭐ **exactly ONE golden** — `editor_shared_orbat`; 75 passed. Diff: **`$.nodes[0].canAcceptSubordinates: golden=true actual=false`** | ✅ yes |

⭐⭐⭐ **Why row ② is the one that matters:** `canAcceptSubordinates` is covered by **no** pairing assertion
*(they pin node/root/child counts)* ⇒ **the golden caught a field nobody thought to name** — §4's whole
argument, demonstrated. ⭐ Row ① is its mirror: the **assertion** caught what no golden could.
⚠ **Neither mutation reddened more than one case**, which is §8's own test of whether the goldens are
over-coupled. They are not.

⭐ Both mutations were **inverse edits**, rebuilt in full, then restored and re-verified green — ⛔ never
`git checkout --`.

## 5. ⭐ RULE 5 — **the ids allocated** *(and a correction to the handoff's series)*

⚠⚠ **The handoff says *"the series stands at `HN-011` / `MX-012`, so start at `HN-012` / `MX-013`"* — the
`HN-` half is STALE.** 📐 The preview batch *(merged into the coordinator line before this dispatch — the
handoff itself credits `HN-017` with the 58th case)* filed **`HN-017`, `HN-018`, `HN-019`**. ⇒ ⭐ **this batch
starts at `HN-020`**, and `MX-013` as instructed.

| id | |
|---|---|
| ✅ **`HN-020`** | `N2`/`N3` — the goldens exist, as a budget; the slash-in-id and dump-size measurements |
| ✅ **`HN-021`** | `N4`/`N5` — the mutation table, and the shared-editor measurement that moved `N5` to its own process |
| ✅ **`HN-022`** | `N6` — `ST-017`'s replacement, plus the honest gap *(SimHost/CGF have no per-host profiler rail)* |
| ⚠ **`HN-023`** | **open** — the determinism rail flaked 1 in 4; `R-131` says find it, do not filter it |
| 🔴 **`MX-013`** | **open** — no endpoint opens an AI asset ⇒ the authoring perspectives can only be captured EMPTY |
| ⚠ **`HN-024`** | **open** — the `variables`/`watch` panels publish `columns` as a CLR `ToString()`, so the visible-column set is unreadable |

## 6. ⛔⛔ WHAT IS **NOT** COVERED — **so silence is not read as coverage**

| ⛔ | ⭐ |
|---|---|
| **the authoring perspectives' POPULATED state** | `MX-013` — no API opens an asset. ⭐ **30 of 41** panels are pinned only in their empty shape |
| **the two volatile kinds** *(`message-log`, `event-browser`)* | ⭐ deliberately never goldened *(`N1`)*; `editor/_gizmo` excluded per §9 `Q2` *(128 KB, high churn)* |
| **the other two curated scenarios** *(`test-fire`, `test-move`)* | ⭐ `N3` is a **budget**: one scenario, six panels. ⛔ Widen only when a widening is asked for *(§9 `R2`)* |
| **SimHost / CGF per-host profiler snapshot rails** | `HN-022` — absent, and **never present**, before or after the mock |
| **`HN-011`'s fix** | ⛔ out of scope by the handoff; ⭐ its tripwire holds it visible, and §6 now carries the capture rule that keeps goldens clear of it |
