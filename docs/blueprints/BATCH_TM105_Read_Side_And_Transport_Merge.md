<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this file is a BATCH — scope, items, gates, verdicts. It carries NO design.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐ BATCH TM-105 — **`T1` the read side · `T2` the transport merge**

> ⛔ **This is a batch, not a design** *(CLAUDE.md obligation ①b)*. ⭐ **The design is
> [`DESIGN_Time_Architecture.md`](DESIGN_Time_Architecture.md) — §9 the target APIs, §9a the buildable
> classes and the read sequence, `AS-11` the transport duplicate.** ⛔ Do not restate it here.
>
> ⭐ **Lane:** TIME, self-directed. ids **`TM-`**, tracker **Area H**.
> ⭐ **Unblocked by `T0`** *(Batch 104 — `TimeControlIntegrationTests` 9/9)*, 📌 `R-127`.

## Items

| id | item | design basis |
|---|---|---|
| **`105a`** | **`GlobalTime.IsAdvancing`** = `DeltaTime > 0`; **`IsPaused` marked `[Obsolete]`** | §9a.1 — `M-42`: ⛔ **never `!IsPaused`**; the flag is `TimeScale == 0` and no pause sets it |
| **`105b`** | **`ISimClock` + `SimClock.Of(view)` + `WorldSimClock`** in `Fdp.Toolkits/Time/` | §9a — reads the **live world's singleton** *(`AS-1b`)*; casts the view per `Blueprint_Subsystem_Runtime_Detailed_Design.md` §12.2 |
| **`105c`** | **Delete `EditorTimeTransportAdapter`; point `TimeControlStatusBarSection` at `EditorTimeTransportFacade`** | `AS-11` **as corrected `2026-08-21`** — ⛔ *"only the Adapter is constructed"* was false; **both** are. ROUTE, not delete-the-dead-one |

## Not in this batch

⛔ **`HaltReason`** → `T6` *(needs `AS-10`'s `NotPublishing`; a stubbed `Reason` is the silent-default
pattern)* · ⛔ **`ITimeCommands`** → `T4` *(needs `T3`'s bus move)* · ⛔ **`W0`** → the UI lane *(`R-128`)*
· ⛔ **the twelve notions repointed** → `T5`, **one site at a time**.

⚠ **`IsAdvancing` must not become the thirteenth notion** — 📌 `R-126`. It is a **read of the one
source**, not a new arm.

## Gates

| gate | baseline |
|---|---|
| ⭐⭐⭐ `~TimeControlIntegrationTests` *(standing row, run twice)* | **9 / 0** |
| ⭐⭐ `~ThePauseFlagOnTheClockIsFalseWhilePausedTests` | **4 / 0** — 📌 pins `M-42` + `AS-1b`; ⚠ it asserts the BROKEN flag deliberately ⇒ needs a local obsolete-suppression, ⛔ not a rewrite |
| ⭐ `Fdp.Toolkits.Tests` full | **1973 / 0** — ⚠ `DEBT-AIB-030` rotating flakes: confirm any red by filter |
| ⭐ `Hrot.ClusterRunner.Tests` | **260 / 2** — the 2 are `DataDrivenGizmoPredicateTests.D003_*`, pre-existing |
| ⭐ solution build | **0 errors** |

## Verdicts

| item | verdict |
|---|---|
| `105a` | — |
| `105b` | — |
| `105c` | — |
