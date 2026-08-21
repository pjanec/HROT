<!--STATUS
state: LIVE
build-state: READY-TO-BUILD
updated: 2026-08-21
current-answer: this whole file — the TIME lane's next batch after T0/Batch 104: T1 (the ISimClock
  read facade) + T2 (retire the EditorTimeTransportFacade duplicate). Both PROVEN, both independent.
stale-below: nothing.
known-rot: none.
known-conflict: none. This batch does NOT touch the write path (W-tasks). ⚠ R-130 (2026-08-21) shifted
  the write model to STAGED — the W-tasks need a design refresh before dispatch; T1/T2 are unaffected
  (time-read side + a duplicate retirement).
design-basis: DESIGN_Time_Architecture.md §9 (the three-way split; "STORAGE+READ = GlobalTime behind
  ISimClock; IsAdvancing, never IsPaused") + AS-11/§4 (the EditorTimeTransportFacade duplicate).
  Landmines: M-42 (GlobalTime.IsPaused is TimeScale==0, false while paused) + AS-1b (read the VIEW's
  singleton, never GetCurrentState()).
-->
# HANDOFF — TIME lane · **T1 (the read facade) + T2 (retire the duplicate)**

> 📌 **Dispatched at `9f1e27801`** *(coordinator head)*. ⭐ **TIME lane** — branch
> **`claude/time-system-refactor-batch-104-gp617x`**, ids **`TM-`**, tracker **`Area H` ONLY**.
> ⭐⭐⭐ **Rule 7, the two-lane form:** ⛔ **do NOT branch fresh from the coordinator** — that would lose
> Batch 104. ⭐ **CONTINUE on your own branch and MERGE the coordinator branch in for canon:**
> `git fetch origin && git merge origin/claude/blueprint-authoring-status-gm0akp` — 📐 **verified
> CLEAN** *(only the tracker overlaps, Area H vs A–G, and it auto-merges)*.
> ⭐ **Rule 1b: push `chore: started TM T1/T2 at 9f1e27801` FIRST.** ⭐ **Rule 3: you allocate the `TM-` ids.**

## 0. ⭐⭐ WHY THESE TWO — **the sequencing, and why they are safe now**

📄 **`PLAN_Time_System_Refactor.md` §4:** `T0 → T1+T2+W0 → T3 → W1+W2 → …`. ⭐ **T0 is done** *(Batch 104,
the net is 9/0)*. ⭐⭐ **T1 and T2 are the two PROVEN, independent foundations** *(§2 feasibility: both ✅)*.

⚠⚠ **W0 is deliberately NOT in this batch.** 📌 `R-130` *(user, `2026-08-21`)*: *"yellow = a staged-change
indicator … make it yellow when really staged"* ⇒ the write model is now **STAGED**, which reframes the
whole `W`-path *(and puts `MIN`'s direct write at odds with §6)*. ⛔ **The `W`-tasks need a design refresh
before dispatch; T1/T2 do not touch them** — they are the time-READ side and a duplicate retirement.

## 1. ⭐⭐ INVENTORY — *(`R-74` / `R-129`: enumerate, and read §9 before coding)*

| # | query | finding |
|---|---|---|
| I1 | `search_graph(name_pattern=".*ISimClock.*")` | ⭐ **does it exist yet?** If absent, `T1` creates it; if a stub exists, extend it *(report which)* |
| I2 | `search_graph(name_pattern=".*(IsPaused\|IsAdvancing).*", label="Property")` | ⭐ the sites `T1` marks obsolete / re-points — ⛔ enumerate, do not guess the count |
| I3 | `grep -rn "new EditorTimeTransportFacade\|new EditorTimeTransportAdapter"` | 📐 **measured:** only `EditorTimeTransportAdapter` is constructed *(`TimeControlStatusBarSection:31`)*; the **Facade** has no constructor call ⇒ `T2` retires it |

## 2. ⛔⛔⛔ `T1` — **`ISimClock`: one named READ surface** *(design: §9)*

📌 **§9's three-way split:** PRODUCER (`ITimeController`, unchanged) · **STORAGE+READ (`GlobalTime` behind
`ISimClock`)** · CONTROL (`ITimeCommands`, later). ⭐ **`T1` builds only the middle one.**

```mermaid
classDiagram
    class ISimClock {
        <<interface READ — NEW>>
        +bool IsAdvancing
        +bool IsHalted
        +double TotalTime
        +float TimeScale
        +long FrameNumber
    }
    class SimClock {
        <<static view-side facade — NEW>>
        +Of(ISimulationView) ISimClock
    }
    class GlobalTime {
        <<ECS singleton — EXISTING, THE STORAGE>>
        +float DeltaTime
        +float TimeScale
        +bool IsAdvancing
    }
    class ITimeController {
        <<EXISTING PRODUCER — unchanged>>
        +Update() GlobalTime
    }
    ISimClock <|.. SimClock
    SimClock ..> GlobalTime : reads the VIEW singleton
    ITimeController ..> GlobalTime : kernel publishes each frame
```

```mermaid
sequenceDiagram
    autonumber
    participant C as any caller
    participant K as SimClock static
    participant V as ISimulationView
    participant G as GlobalTime singleton

    C->>K: SimClock.Of(view)
    K->>V: GetSingletonUnmanaged GlobalTime
    V-->>K: the live world instance the kernel pushed
    K-->>C: ISimClock over that instance
    C->>K: IsAdvancing
    K-->>C: DeltaTime greater than 0
```

| ⭐ obligation | |
|---|---|
| **①** | ⭐⭐⭐ **`GlobalTime.IsAdvancing`** = `DeltaTime > 0f` — 📌 `M-42`: **`IsPaused` (`TimeScale == 0`) is FALSE while paused**, so mark `IsPaused` `[Obsolete]` and never derive from it |
| **②** | ⭐⭐ **`SimClock.Of(ISimulationView)` reads the VIEW's singleton** — ⛔⛔ `AS-1b`: **NEVER** `ITimeController.GetCurrentState()`, which hard-codes its delta to 0 and answers *"halted"* for ever |
| **③** | ⭐ **`IsHalted` = `!IsAdvancing`**; `TotalTime`/`TimeScale`/`FrameNumber` pass through the singleton |
| **④** | ⚠ **`HaltReason Reason` is `T6`, NOT this batch** — leave it off `ISimClock` for now, or a `Running`/`Halted` two-value placeholder; ⛔ do not build the full enum |
| **⑤** | ⭐ **do NOT re-point the 12 pause notions yet** — that is `T5`. `T1` only ADDS the surface and obsoletes `IsPaused` |

⭐ **Pinned by `Fdp.Toolkits.Tests ▸ ThePauseFlagOnTheClockIsFalseWhilePausedTests` (must stay 4/0)** —
it already proves `M-42`/`AS-1b`; `IsAdvancing` must agree with it.

## 3. ⭐⭐ `T2` — **retire `EditorTimeTransportFacade`** *(design: AS-11 / §4)*

📐 `EditorTimeTransportFacade` ⇄ `EditorTimeTransportAdapter` are **identical but for name, accessibility
and three null-guards**, and **only the Adapter is constructed** *(`TimeControlStatusBarSection:31`)*.

| ⭐ | |
|---|---|
| **①** | ⭐ **delete `EditorTimeTransportFacade`** — ⚠ first confirm by `search_graph`/grep it has **no** constructor call and no other reference *(a test double is not a production use)* |
| **②** | ⛔ if the Adapter lacks a guard the Facade had, **move the guard, do not keep the class** *(`R-65`: one implementation)* |
| **③** | ⭐ **report the reference count you measured** before deleting *(`R-129`: enumerate, don't assume)* |

## 4. ⛔ NOT IN THIS BATCH

⛔ `T3`/`T3a`/`T3b` *(the bus)* · `T4`+ *(control/intents)* · `T5` *(re-point the notions)* · `T6`
*(`HaltReason`)* · `T7` *(the caches)* · ⛔⛔ **all `W`-tasks** *(the write path — `R-130` reframed it)*.

## 5. ⭐ GATES

⭐ Baseline = **Batch 104's table** *(`REPORT_Batch104`)*. ⭐⭐ **The STANDING integration row** *(`TM-003`)*:
```
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/... --no-build \
            --filter "FullyQualifiedName~TimeControlIntegrationTests"
```
⭐ **must stay `9/0`** — a new red is a regression this batch caused. ⭐ `Fdp.Toolkits.Tests ▸
ThePauseFlagOnTheClockIsFalseWhilePaused` **4/0**. ⚠ `DEBT-AIB-030`: the rotating flakes are confirmed by
`--filter`, not by the whole-suite number. ⭐ `tracker-counts.py --check` + the **`TM-` ids you allocated**
*(Area H only)*. ⭐ **Rule 4: merge the coordinator branch again before your final commit.**
