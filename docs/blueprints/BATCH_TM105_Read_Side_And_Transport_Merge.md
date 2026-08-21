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
| ⭐⭐ `~ThePauseFlagOnTheClockIsFalseWhilePausedTests` | **4 / 0** — 📌 pins `M-42` + `AS-1b`; ⚠ it asserts the BROKEN flag deliberately ⇒ if it warns, suppress locally, ⛔ never rewrite. 📐 **It did not warn** — it names the flag only in prose |
| ⭐ `Fdp.Toolkits.Tests` full | **1973 / 0** — ⚠ `DEBT-AIB-030` rotating flakes: confirm any red by filter |
| ⭐ `Hrot.ClusterRunner.Tests` | **260 / 2** — the 2 are `DataDrivenGizmoPredicateTests.D003_*`, pre-existing |
| ⭐ solution build | **0 errors** |

## Verdicts

| item | verdict | |
|---|---|---|
| `105a` | ✅ **done** | `GlobalTime.IsAdvancing` / `IsHalted` added as **computed properties** *(⛔ no field, so the `[StructLayout(Sequential)]` the flight recorder depends on is untouched)*; `IsPaused` obsoleted with the reason in the message |
| `105b` | ✅ **done** | `ISimClock` · `SimClock` · `WorldSimClock` in `Fdp.Toolkits/Time/`. ⛔ **`ISimulationView` NOT widened** *(§9a)* |
| `105c` | ✅ **done** | `EditorTimeTransportAdapter` deleted, status bar repointed at the Facade. ⭐ **Two surfaces, one implementation** |

### Gate results

| gate | baseline | after | Δ |
|---|---|---|---|
| solution build | 0 errors | ✅ **0 errors** | ⭐ **3 new `CS0618`s, all in `Hrot.Editor.Tests`** — see the finding below |
| `~TimeControlIntegrationTests` run 1 | 9 / 0 | ✅ **9 / 0** (57 s) | **0** |
| `~TimeControlIntegrationTests` run 2 | 9 / 0 | ✅ **9 / 0** (56 s) | **0** — no flake |
| `~ThePauseFlagOnTheClockIsFalseWhilePausedTests` | 4 / 0 | ✅ **4 / 0** | **0** — `M-42`/`AS-1b` still pinned |
| `Fdp.Toolkits.Tests` full | 1973 / 0 | ✅ **1981 / 0** | **+8 rails** *(`SimClockTests`)* |
| `Hrot.ClusterRunner.Tests` | 260 / 2 | ⚠ **260 / 2** | **0** — named: `DataDrivenGizmoPredicateTests.D003_Predicate_True_AllowsUpdateAndDraw` and `…_False_SkipsUpdateAndDraw_ForFilteredEntity`, both pre-existing |
| `Hrot.Editor.Tests` | — | ✅ **206 / 0** | ⭐ run because `105c` touches that assembly |
| working tree after every suite | clean | ✅ **clean** | — |
| goldens | — | ⛔ **none moved** — 7 files, hand-written, no generated artefact | — |

## ⛔⛔ CROSS-LANE FINDING — **reported, NOT fixed** *(`R-128` rule ③)*

📄 **`Hrot/Subsystems/Hrot.Editor.Tests/ThePausedClockIsTheRunStateTests.cs`** — the UI lane's file.
⛔ **Its premise is measurably false**, and it now emits 3 obsolete warnings that point straight at it.

| it says | 📐 measured |
|---|---|
| *"the pause a designer presses is `ITimeTransportFacade.TogglePlayPause`, **which sets the clock's `TimeScale` to 0**"* | ⛔ **FALSE.** `EditorTimeTransportFacade.TogglePlayPause()` calls **`SwitchToDeterministic()`** and never touches `TimeScale`. ⭐ `SetTimeScale` is a separate method with a separate caller |
| *"`GlobalTime.IsPaused` is the authority and `RunStateSource` honours it"* | ⛔ **production does not use it.** `EditorSubsystem.ClockIsHalted():594` reads **`GlobalTime.DeltaTime <= 0f`** |

⇒ ⭐⭐ **The test only passes because it FABRICATES `new GlobalTime { TimeScale = 0.0f }` by hand.**
⚠ **In production that state never occurs**, so what it pins would refuse the edit while paused —
📌 the exact `M-40` defect it was written to prevent. ⭐⭐⭐ **The good news: `HANDOFF_MIN` and the
shipped `ClockIsHalted()` already use `DeltaTime`**, so the *fix* is right and only the *rail* is stale.
⇒ ⭐ **Suggested for the UI lane:** point it at `IsAdvancing`/`IsHalted`, which is now the named form of
the predicate its own production code already uses. ⛔ **Not edited here** — different lane, and `MIN`
is live in that exact region.

## ⭐ FOLLOW-UP FOR THIS LANE — **not taken, and why**

⭐ `EditorSubsystem.ClockIsHalted()` is now **literally `SimClock.Of(world).IsHalted`** — a routing
candidate the moment `105b` landed. ⛔ **Deliberately not routed:** `MIN` is in flight and edits
`isFrozen` at `EditorSubsystem:2278`, three lines from it. ⇒ ⭐ **`T5`'s first site, after `MIN` merges.**
