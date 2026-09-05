<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this file is a BATCH — scope, items, gates, verdicts. It carries NO design.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐ BATCH TM-110 — **`T7`: the two remote caches, and the barrier window they exposed**

> ⛔ **A batch, not a design** *(CLAUDE.md ①b)*. ⭐ **Design:**
> [`DESIGN_Time_Architecture.md` **§15**](../DESIGN_Time_Architecture.md) — the measurement, the `P3`
> correction and both UML diagrams live there. ⭐ Self-directed under the user's *"ok t7 then"*, with
> *"the measurement is the first deliverable."*

## ⭐⭐ The measurement came first — **and it REVERSED the change the design implied**

| asked | measured | verdict |
|---|---|---|
| do the two caches serve different processes? | ⭐ **`ClusterUiCache`**: Orchestrator · Editor · ExCon. **`ClusterTimeTransportAdapter`**: CGF · SimHost | ⛔ **disjoint — no node holds both** |
| are they a duplicate to collapse? | 17-field read-model vs an `ITimeTransportFacade` that also carries the **commands** | ⛔ **NO** — duplicate **SURFACE** ⇒ keep |
| then what IS duplicated? | ⭐ the `SwitchTimeModeEvent` fold — **the same four lines, twice** | ✅ **duplicate CODE ⇒ route it** *(`ClusterTimeObservation`)* |
| ⭐⭐⭐ does it inherit `AS-2`, as `P3` says? | ⛔⛔ **BACKWARDS.** The event is published at the **TOP** of `SwitchToDeterministic` and `_totalTime` freezes at the same instant; `GetMode()` is the one that lags — **200 ms** | ⭐ **`P3` corrected in §15.2** |

⇒ ⛔ **Acting on `P3` as written — "refine it towards the controller" — would have made the answer LATE
by the whole lookahead window.** ⭐ **This is why the measurement was the first deliverable.**

## Items

| id | item | verdict |
|---|---|---|
| **`TM-027`** | `ClusterTimeObservation` — the one fold, owned by both caches | ✅ |
| **`TM-028`** | `P3` corrected; `PauseRequested` named for what it is | ✅ |
| **`TM-029`** | `IsPauseBarrierPending` + `HaltReason.PauseBarrierPending` | ✅ |
| **`TM-030`** | `EditorTimeTransportFacade` — the resolver's first production caller | ✅ |
| **`TM-031`** | a step in the barrier window is **queued**, not dropped | ✅ |
| **`TM-032`** | `Hrot.Presentation.Tests` is not gateable as a whole-suite count | ⚠ **recorded, not fixed** |

## ⛔⛔ The finding `T6` asked for — **`Unknown` was reachable, in normal operation**

⭐⭐⭐ `HaltReason.Unknown`'s own documentation, written **last batch**: *"If this appears in practice,
the missing probe is the finding."*

📐 In the barrier window: publishing ✅ · advancing ❌ · rewound ❌ · acks ❌ · deterministic ❌
*(`GetMode()` hides `BarrierPending` **by design** — `:161-169`)* ⇒ ⛔ **`Unknown`, for 200 ms after
every pause.** ⭐ **The probe was missing, exactly as predicted.**

⚠ **And it was hiding a real defect** *(`TM-031`)*: `Step()`'s mode guard sat **above** `AS-14`'s
deferral queue, so a step pressed in that window was **refused and lost**. ⭐ Short window ⇒
intermittent ⇒ it would have read as *"step sometimes does nothing"*, which is the hardest report to
act on.

## ⚠ Obligation ③ — **what I built vs what the design says**

⭐ §15.8's `classDiagram` and `sequenceDiagram` were drawn **from this measurement**, in the design, and
what shipped matches them. ⚠ **One thing the diagram does NOT show and the code does:**
`ClusterUiCache.IsPaused` keeps its **name** and forwards to `PauseRequested` — 📌 renaming a read-model
property consumed by `ClusterScenarioPanel:415,421` buys nothing the doc-comment does not, and would
have widened the diff for no measured benefit.

## Gate results

| gate | `--no-build`? | baseline | after | Δ |
|---|---|---|---|---|
| solution build *(`IOS-IG-SimHost.sln`)* | builds | 0 errors | ✅ **0 errors** | **0** |
| `Fdp.Toolkits.Tests` — `~ClusterTimeObservationTests\|~HaltReasonTests\|~MasterSyncControllerTests` | `--no-build` | 50 / 0 | ✅ **62 / 0** | **+12 rails** |
| `~TimeControlIntegrationTests` ×2 | `--no-build` | 9 / 0 | ✅ **9 / 0**, **9 / 0** | **0** — no flake |
| `~ThePauseFlagOnTheClockIsFalseWhilePausedTests` | `--no-build` | 4 / 0 | ✅ **4 / 0** | **0** |
| `Fdp.ModuleHost.Tests` | `--no-build` | 192 / 6 | ✅ **192 / 6** | **0** — same 6 names as `TM-023` |
| `~ClusterUiCacheTests` *(ClusterRunner.Tests)* | `--no-build` | 12 / 0 | ✅ **12 / 0** | **0** |
| ⭐ `Hrot.Presentation.Tests` **FILTERED** *(`~ClusterTimeControl\|~MainToolbarTimeControl\|~TransportIcons`)* | `--no-build` | — | ✅ **see table below** | new gate row |
| `Hrot.Editor.Tests` | `--no-build` | — | ✅ **see table below** | — |
| `tracker-counts.py --check` | — | OK | ✅ **OK — open 81 / done 243** | — |
| `mermaid-check.mjs` on the design | — | 14 blocks | ✅ **16 blocks, all parse** | **+2** |
| working tree after every suite | — | clean | ✅ **clean** | — |
| goldens | — | — | ⛔ **none moved** | — |

### ⚠⚠ `TM-032` — **why `Hrot.Presentation.Tests` has a FILTERED row and not a whole-suite one**

📐 **Three unfiltered runs of the identical binary at CLEAN HEAD, with nothing of mine present:**

| run | result |
|---|---|
| 1 | **29 discovered**, 0 failed |
| 2 | **99 discovered**, 3 failed |
| 3 | **99 discovered**, 4 failed |

⇒ ⛔⛔ **the DISCOVERED TOTAL rotates, not just the failures.** ⚠ The reds are
`EntityDragGizmoTests` ×3 and `ScenarioFileServiceTests.SaveLoad_RoundTrip_PreservesEntitiesAndComponents`
— all ImGui-fixture-dependent, none touching time.

⭐⭐ **Recorded honestly rather than reported as a green:** I ran the unfiltered suite, saw 3 reds, and
**stashed to clean HEAD and re-ran three times** before claiming anything. ⛔ **A single clean-HEAD
sample would have proved nothing** — 📌 the same trap `TM-015` nearly fell into.
