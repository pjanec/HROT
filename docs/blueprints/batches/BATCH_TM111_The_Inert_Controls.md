<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this file is a BATCH — scope, items, gates, verdicts. It carries NO design.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐ BATCH TM-111 — **`T5`: the pause notions, enumerated — and two of them did nothing**

> ⛔ **A batch, not a design** *(CLAUDE.md ①b)*. ⭐ **Design:**
> [`DESIGN_Time_Architecture.md` **§16**](../DESIGN_Time_Architecture.md) — the full roll, the query
> that produced it, and the per-site verdicts.

## ⭐⭐ The enumeration came first — **`T5` was never one refactor**

```
search_graph(name_pattern=".*(IsPaused|IsFrozen|IsStopped|PausedBy|IsHalted).*")
  -> total 98, has_more false
```

⇒ **20 production time notions.** ⭐ Six were already closed by `TM-009`/`TM-012`/`TM-027`/`TM-030`.
⭐⭐ **Two were open and in this lane. Both turned out to be doing nothing at all.**

| id | site | verdict |
|---|---|---|
| **`TM-033`** | `ExConLogic` — 3 properties never written, `IsPaused` a 4th fold copy | ✅ |
| **`TM-034`** | `SimHostSimulationControlsPanel` — Play/Pause/Step **inert** | ✅ |
| **`TM-035`** | `ClusterTimeObservation.SnapshotSimTime` — the second reading | ✅ |
| **`TM-036`** | `Hrot.SimHost.Tests` is not gateable by count | ⚠ **recorded** |
| **`TM-037`** | `PerspectiveWorkspaceServices.IsFrozen` | ⛔ **NOT TAKEN — cross-lane** |

## ⛔⛔ Why `T7` missed `ExCon` — **worth writing down**

⭐ `T7` swept for **`SwitchTimeModeEvent`**. ⛔ `ExConLogic` folds the **`SwitchTimeModeWireDto`**.
📌 **Same message, different type ⇒ invisible to that search**, and it had a fourth copy of the fold
plus three documented properties that reported `0 / 0 / 1` forever.

⇒ ⭐⭐ **The name-keyed enumeration found what the type-keyed one could not.** ⚠ Neither is sufficient
alone, which is the standing rule and now has a fourth measured instance.

## ⛔⛔ Why the SimHost panel read as wired

📐 `ConsumeStepRequest` had **no caller anywhere in the repo**. ⚠ **But the `FDP/Examples` `MainUI` has
the identical members and `CarKinemApp` genuinely consumes them** *(`:203` · `:311` · `:324`)*.
⇒ ⭐⭐ **a control looks alive because its twin elsewhere is** — and that is the third time this
programme has hit `AS-9`'s shape.

⭐⭐⭐ **The fix needed no new seam**: `SimHostSubsystem:263` already builds a
`ClusterTimeTransportAdapter` and already **pumps it** *(`:195`)*. ⇒ the caller **had** the dependency
and did not pass it — CLAUDE.md's silent-default rule, exactly. ⭐ Rail asserts on the **constructed
object**, not the registrar's source.

## Gate results

| gate | `--no-build`? | baseline | after | Δ |
|---|---|---|---|---|
| solution build *(`IOS-IG-SimHost.sln`)* | builds | 0 errors | ✅ **0 errors** | **0** |
| `Fdp.Toolkits.Tests` — time filter | `--no-build` | 62 / 0 | ✅ **64 / 0** | **+2 rails** |
| `~TimeControlIntegrationTests` ×2 | `--no-build` | 9 / 0 | ✅ **9 / 0**, **9 / 0** | **0** |
| `~ThePauseFlagOnTheClockIsFalseWhilePausedTests` | `--no-build` | 4 / 0 | ✅ **4 / 0** | **0** |
| `Fdp.ModuleHost.Tests` | `--no-build` | 192 / 6 | ✅ **192 / 6** | **0** — same six names |
| ⭐ **`Hrot.ExCon.Tests`** *(NEW row — `TM-033` touches it)* | `--no-build` | — | ✅ **378 / 0**, three runs, stable | — |
| ⭐ `~ExConLogicTimeTests` | `--no-build` | 7 / 0 | ✅ **10 / 0** | **+3 rails** |
| `~ClusterUiCacheTests` | `--no-build` | 12 / 0 | ✅ **12 / 0** | **0** |
| `Hrot.Editor.Tests` | `--no-build` | 209 / 0 | ✅ **209 / 0** | **0** |
| `Hrot.Presentation.Tests` **FILTERED** *(`TM-032`)* | `--no-build` | 17 / 0 | ✅ **17 / 0** | **0** |
| ⚠ **`Hrot.SimHost.Tests`** — **FILTERED**, see below | `--no-build` | — | ✅ **3 / 0** *(the new rails)* | new |
| `tracker-counts.py --check` | — | OK | ✅ **OK** | — |
| `mermaid-check.mjs` on the design | — | 16 blocks | ✅ **16 blocks parse** | **0** |
| working tree after every suite | — | clean | ✅ **clean** | — |
| goldens | — | — | ⛔ **none moved** | — |

### ⚠⚠ A stale binary nearly went into this table

📌 My first `Hrot.ExCon.Tests` run reported **85 / 0** and I was about to record it. ⛔ **It was a
STALE dll** — I had built `Hrot.ExCon.csproj` and the SimHost test project, **not the solution**, so
`--no-build` ran a partially-rebuilt assembly. ⭐ After the solution build the same command reports
**378 / 0**, three runs running. ⚠ **`--no-build` tests whatever is on disk and says PASSED either
way** — 📌 the hazard CLAUDE.md's tier table names, hit for real.

⚠ **`tracker-counts.py` counts only `**BP-` rows** — ⛔ **`TM-` ids are invisible to it by design**
*(the lane id split)*. ⭐ Its OK is therefore **not** evidence about my rows; said here so nobody reads
it as such.

### ⚠⚠ `TM-036` — **`Hrot.SimHost.Tests` cannot be gated by a whole-suite count**

📐 **Four full runs of the identical binary — THREE of them at CLEAN HEAD, nothing of mine present:**

| run | failures |
|---|---|
| clean HEAD #1 | **12** |
| clean HEAD #2 | **14** |
| clean HEAD #3 | **13** |
| with my change | **10** |

⛔ **The NAMES rotate every run.** 📐 The rotating tail is `StagingEntityExtractorTests` ·
`EcsRecordReplayControllerTests` · `JsonToRecordCompilerTests` · `EditLoadClusterOpHandlerTests` ·
`FullBranchPipelineTests` — ⭐⭐ **and all 18 `StagingEntityExtractor` tests PASS in isolation, three
runs out of three** ⇒ **an order/parallelism flake, not a regression.**

⭐ **The STABLE core is 8, name-for-name identical in all four runs:** `CgfLogicPackTests` ×3 ·
`HillAttackIntegrationTests` ×4 · `HillAttackNodeTests.SC_HA016_7`.

⚠⚠ **Why this needed four runs and not one.** 📌 My first full run showed **15** against a clean-HEAD
**12** with the same passed count — ⛔ **which reads exactly like a fix-one-break-one**, and the four
differing `StagingEntityExtractor` names would have looked like the culprits. ⭐ Only the repeated
clean-HEAD sampling showed the identity rotating with no code change at all. 📌 `TM-015`'s lesson,
applied.

## ⛔ What I did NOT take, and why — `TM-037`

⭐ `PerspectiveWorkspaceServices.IsFrozen` **is** a `T5` site, and its supplier at
`EditorSubsystem:2298` is `HaltReasonResolver` written by hand. ⛔⛔ **But `!= Running` is a different
predicate** — it would newly report frozen for `NotPublishing` and `Unknown` ⇒ **a contract change to a
consumer in `Hrot.Editor.AiShared`, the frozen area, whose basis is ruling 15.**
⇒ ⭐ **STOP-and-report, per the lane rule.** 📄 Proposed shape in §16.5; the UI lane decides.
