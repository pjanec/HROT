<!--STATUS
state: LIVE
updated: 2026-08-23
current-answer: the whole file — the batch report for HANDOFF_Runner_Tick_And_Mode_Rails.md.
  Design content is NOT here: it is folded into DESIGN_Stride_Port.md §8 (T1) and §9 (T2).
known-conflict: none.
-->
# REPORT — the runner's tick path and its mode rails

> 📌 **Dispatch:** [`HANDOFF_Runner_Tick_And_Mode_Rails.md`](HANDOFF_Runner_Tick_And_Mode_Rails.md),
> frozen at **`5963fffd4`**. Branch `claude/blueprint-macro-feature-sdmspn` (BACKEND lane).
> **ids allocated: `ST-019`, `ST-020`, `ST-021`** *(rule 5)*. Order per §1: **T2 before T1**.

## 1. OUTCOME

| item | outcome |
|---|---|
| **T2** — `ST-019` | ✅ **8 mode rails**, `Hrot.SystemTests/ModeStartupRails.cs`. Revert-goes-red **proven** |
| **T1** — `ST-021` | ✅ **`#pragma` GONE**, `Kernel.Update()`. ⚠ one premise of the old comment measured **FALSE** |
| ⛔ **new finding** — `ST-020` | 🔴 **`--mode ig` dies during bootstrap.** A contract cascade with **no local fix**; policy call **not taken** |

📄 **Design fold-back:** `DESIGN_Stride_Port.md` **§8** now RESOLVED *(prior text moved to its own
`⛔ HISTORY` heading)* and **§9** records where the mode rails live and what they do and do not prove.

## 2. ⭐⭐ §GATES — one row per gate

⚠ **The dispatch's process note is accepted:** the previous batch delivered its gate table only in chat,
so it did not exist for the coordinator. This is that table, in the repo.

| # | gate — verbatim command | `--no-build`? | result | delta vs `5963fffd4` |
|---|---|---|---|---|
| 1 | `dotnet build IOS-IG-SimHost.sln` | builds | ✅ **0 errors**, 24 warnings | none |
| 2 | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build --filter Category=SystemModes` | `--no-build` | ✅ **8 / 0** | **+8 new** |
| 3 | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build` *(whole suite)* | `--no-build` | ⚠ **47 / 1** | **39 → 47 passing**; the 1 red is pre-existing (row A) |
| 4 | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --no-build --filter FullyQualifiedName~TimeControlIntegrationTests` | `--no-build` | ✅ **9 / 0** | none |
| 5 | `dotnet test FDP/Engine/Fdp.ModuleHost.Tests --no-build` | `--no-build` | ⚠ **192 / 6** | none — all 6 pre-existing (row B) |
| 6 | `dotnet build Hrot/Subsystems/Hrot.NodeComposition` | builds | ✅ 0 errors | T1's real gate — see §3 |
| 7 | `dotnet build Stride/HrotStrideApp.Game -p:EnableWindowsTargeting=true` | builds | ✅ **0 errors** | ⛔ **compiled, NOT run** |
| 8 | `python3 scripts/tracker-counts.py --check` | n/a | ✅ OK — open **99** / done **333** | ⚠ blind to `ST-` rows |
| 9 | `python3 scripts/rulings-check.py` | n/a | ✅ **24/24** + 2 staleness WARNs | see §4 |
| 10 | `python3 scripts/design-digest.py --check` | n/a | ✅ clean (81 docs) | none |

**Mode matrix** *(gate 2 is now the automated form of this row)*: `editor` ✅ · `all` ✅ · `simhost` ✅ ·
`cgf` ✅ · `excon` ✅ · `orchestrator` ✅ · `replaybrowser` ✅ · `ig` 🔴 **`ST-020`**.
⛔ `stridemock` correctly **refused** (`ST-015`).

### Every RED, confirmed pre-existing **by name**

| | red | evidence |
|---|---|---|
| **A** | `PanelSnapshotTests.A_panels_model_can_be_read_and_a_field_asserted` | same single failure before this batch; also confirmed at `c6f54318c` last batch |
| **B** | `ProviderAssignment_AsyncSoD_MultipleModules_Convoy` · `AutoGrouping_SameTierAndFreq_SharesProvider` · `UnionMask_Expansion_NewSodModule_ExpandsSharedProvider` · `BatchInstall_SodModules_ActivatedAtomically` · `ConvoyIntegration_5Modules_ShareSnapshot` · `ConvoyIntegration_MemoryUsage_Reduced` | ⭐ **the same 6 names fail at the dispatch sha `5963fffd4`**, run in a throwaway worktree. ⭐ Also structurally unreachable: `Fdp.ModuleHost.Tests` references only `Fdp.ModuleHost` + `Fdp.Toolkits`, and this batch touches neither |

⭐ **Working tree CLEAN after every suite run** — no golden or fixture was regenerated. **No goldens moved**
(this batch adds none — they belong to the parallel regression-net batch).
**Quarantine counts:** 0 skips added; `ig` is a **tripwire case, not a skip** (§3).

## 3. ⭐⭐⭐ REVERT-GOES-RED — the rail can fail, and precisely

⛔ *"A rail that has never failed is not evidence."* Reverted the coordinator's **`0defc1074`**
*(`ClusterSlave` registering the events it publishes)* in the working tree, rebuilt, re-ran:

| | |
|---|---|
| ⭐⭐ **exactly `--mode all` reddened** | *"died with an unhandled exception"* — **6 / 1**, the other six modes stayed green ⇒ the rail is **specific**, not a blanket smoke alarm |
| ✅ restored and re-verified | **8 / 8** |

⚠⚠ **And a stale binary nearly made me report the opposite.** My first probe showed `--mode all` still
crashing *after* merging the fix — because I had merged and **not rebuilt**. The fix was fine; the dll was
old. 📌 Third instance of this trap in this lane's history; the discipline that caught it was rebuilding
before drawing a conclusion, not reasoning about the diff.

⚠ **T1's gate is the `#pragma`'s absence, as the dispatch specified** — and it is stronger than it looks:
`Hrot.NodeComposition` sets `TreatWarningsAsErrors=true`, so the obsolete overload would now be a hard
**error** in that project. The suppression cannot creep back silently.

## 4. WHAT I DID NOT DO, AND WHY

| ⛔ | |
|---|---|
| **`ST-020` — did not fix `--mode ig`** | ⭐ I tried the local fix and stopped **on measurement**: adding `NavigationIntent` to `IgRoleComponentRegistry` (mirror-pattern, Phase 2, correctly ordered) moved the failure to `BrainBlackboard` ⇒ **a cascade, not a one-liner.** All three candidate fixes are policy — a new `Hrot.IG` → `Hrot.SimHost` edge, moving those registries down, or making `StatelessGizmoRegistry` skip absent components (⛔ which trades a loud failure for a silent one, the very trap `BP4005` exists to close). ⇒ **partial fix reverted rather than half-applying a policy I do not own.** Full measurement in the tracker row |
| **did not add a golden or touch `Goldens/`** | the parallel regression-net batch owns them (§3 of the dispatch) |
| **did not touch `ST-017`** | assigned to the other batch |
| **did not run the Stride suites** | ⛔ cannot — `Microsoft.WindowsDesktop.App` has no linux-x64 build (`ST-006`) |

⚠ **Shared-file touch, declared:** `EditorProcessFixture.cs` was edited (≈55 lines removed) so the Xvfb
logic could move to `XvfbDisplay.cs` and be reused instead of copied. It is **not** in the parallel batch's
named surface (goldens / determinism / panel rails), but it **is** in the same project — flagged for the
merge. Its suite went **39 → 47 passing with the same single pre-existing red**, so the refactor is proven
behaviour-neutral.

⚠ **`rulings-check` staleness WARNs** (quotes still match; surrounding text moved): `.claude/CLAUDE.md`
*(the coordinator's own lane-table re-point)* and `docs/projects/SOLUTION-OVERVIEW.md` *(this lane's
`ST-014` doc sweep)*. Neither is a ledger failure; naming them so the next session does not re-derive it.

## 5. RULE COMPLIANCE

| rule | |
|---|---|
| **1b** started-marker | ✅ pushed **before any code**, naming `5963fffd4` |
| **3 / 5** ids | ✅ **I** allocated `ST-019`/`ST-020`/`ST-021`. ⚠ The `ST-` series was at **013**; the `S1…S5`-style placeholders sit in a range where **`BP-217`–`219` already exist**, so a naive `ST-21x` would have read as a collision |
| **4 / 7** re-sync | ✅ merged the coordinator branch at the start **and** again before the final commit |
| **8** gate report | ✅ §2 — the table the process note asked for |
