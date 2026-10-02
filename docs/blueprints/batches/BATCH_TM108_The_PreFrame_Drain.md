<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this file is a BATCH — scope, items, gates, verdicts. It carries NO design.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐ BATCH TM-108 — **`W1` + `W2`: the PreFrame drain**

> ⛔ **A batch, not a design** *(CLAUDE.md ①b)*. ⭐ **Designs:**
> [`DESIGN_Time_Architecture.md`](../DESIGN_Time_Architecture.md) **§10** *(the drain)* ·
> [`DESIGN_Staged_Live_Write.md`](../DESIGN_Staged_Live_Write.md) **§5/§6** *(the seam + lane split)*.
> 📄 Dispatch: [`HANDOFF_Time_W1_W2_The_Drain.md`](HANDOFF_Time_W1_W2_The_Drain.md), stamped `68c1855d5`.
> ⭐ **Started marker pushed** *(rule 1b)*: `chore: started TM W1/W2 at 68c1855d5`.

| id | item | verdict |
|---|---|---|
| **`W1`** | `SystemPhase.PreFrame` + the kernel line | ✅ **done** *(`TM-020`)* |
| **`W2`** | `ResumeAndDrainSystem` — the PULL | ✅ **done** *(`TM-021`)* |
| — | production composition | ⛔ **NOT in this batch, by instruction** — ⚠ `TM-022` |

## ⚠⚠ INTAKE — **I took the handoff and its two prerequisites, NOT the branch**

🔒 **User: *"do not merge whole branch, take just handoff."*** ⭐ The dispatch is not executable alone, so
I also took the two files it **names as prerequisites** — stated rather than quietly widened:

| taken | why |
|---|---|
| `batches/HANDOFF_Time_W1_W2_The_Drain.md` | the dispatch |
| `FDP/Engine/Fdp.Core/Abstractions/IStagedWrites.cs` | ⭐ the seam I **compile against** |
| `DESIGN_Staged_Live_Write.md` | the seam contract + the W-task lane split |
| ⭐⭐ **`DESIGN_Time_Architecture.md` §10 ONLY** *(spliced)* | ⛔ **their whole file predates `TM-105`/`106`/`107`** and would have reverted `AS-11`'s correction, `AS-13`/`AS-14`'s measurements, §9a's diagrams and the `T3a`/`T3b` resolutions |

## ⛔⛔ THE DESIGNS CONTRADICTED EACH OTHER — **and the diagram was the odd one out**

📐 §10's sequence had the drain call **`RestorePostTick()`** then **`DrainPendingMutations(repo)`**.
⛔ **Neither is on the `IStagedWrites` that shipped** *(`HasPending` · `IsRewound` · `DrainInto` ·
`TryGetPending`)*. ⚠ And it contradicted **`DESIGN_Staged_Live_Write.md` §6**, which gives `W2`
*"`DrainInto` when advancing, **skip while rewound**"* and gives **the restore to `W5`, the UI lane**.

⇒ ⭐⭐ **Three sources agreed with each other and one did not**: the handoff, §6 and the interface all say
*skip*; only the diagram said *restore*. ⭐ **Built to the three; corrected the diagram** *(it predated
the lane split)*.

## `W1` — **the hazard I checked before shipping `PreFrame = 0`**

⚠ `default(SystemPhase)` is now `PreFrame`, so an **unattributed** system could in principle fall into
it. 📐 **It cannot:** `SystemScheduler.GetPhaseAttribute` **throws** when `[UpdateInPhase]` is missing.
⛔ Not assumed — read.

⭐⭐ **Railed END TO END, not by attribute.** `TheKernel_ExecutesPreFrame_AndDoesSoBeforeInput` registers
two spies and asserts the **observed** order. ⛔ **An enum value that sorts first but is never scheduled
would have passed every other rail in the file and drained nothing, forever** — 📌 that is `W1`'s actual
deliverable, and the attribute test does not cover it.

## `W2` — **the gate, and why the parameter**

📐 **Verified the phase parameter IS the frame's delta**: `Update()` → `UpdateInternal(globalTime.DeltaTime, …)`.
⇒ the parameter and the clock singleton are the same number, so gating on it satisfies `AS-10` — ⛔ while
asking a *controller* would not: `GetCurrentState()` hard-codes its delta to zero and reports **halted on
every frame, including the running ones**.

⭐ **PULL, not a release event** *(`R-126`)*: the loop asks every frame ⇒ ⛔ **no resume/step/continue
path can forget to raise what is never raised.**

⚠ **Deviation from §10's diagram, argued not hidden** *(obligation ③)*: §10 draws
`ResumeAndDrainSystem ..> SimClock`. ⛔ **The system does not use `SimClock`** — it gates on the
`Execute` parameter, which is the same value and does not depend on singleton push-ordering. ⭐ It also
keeps the system in `Fdp.ModuleHost`, which **cannot** reference `Fdp.Toolkits` where `SimClock` lives.

## ⚠ `TM-022` — **built, NOT composed**

⛔ **Nothing constructs `ResumeAndDrainSystem` in production yet** — it needs an `IStagedWrites`, and the
only implementer will be `DataBreakpointManager` *(UI lane, `W4`)*. ⭐ **That is the handoff's own
instruction.** ⚠⚠ **But a built-and-unwired system is exactly the `R-67` shape this programme keeps
hitting** — 📌 `AS-9`'s no-op coordinator was one *two batches ago* — ⇒ ⭐ **it gets a tracker row, not a
sentence in a report nobody re-reads.**

## Gate results

| gate | baseline | after | Δ |
|---|---|---|---|
| solution build | 0 errors | ✅ **0 errors** | **0** |
| `~TimeControlIntegrationTests` ×2 | 9 / 0 | ✅ **9 / 0**, **9 / 0** | **0** — no flake |
| `~ThePauseFlagOnTheClockIsFalseWhilePausedTests` | 4 / 0 | ✅ **4 / 0** | **0** |
| ⭐⭐ **`Fdp.ModuleHost.Tests`** *(NEW gate row — `W1` touches this scheduler)* | **183 / 6** | ⚠ **192 / 6** | **+9 rails, reds unchanged** |
| `~ResumeAndDrainSystemTests` | — | ✅ **9 / 0** | the batch's own rails |
| working tree after every suite | clean | ✅ **clean** | — |
| goldens | — | ⛔ **none moved** | — |

### ⭐⭐ The 6 reds — **pre-existing, confirmed BY NAME**

📐 `ConvoyAutoGroupingTests.AutoGrouping_SameTierAndFreq_SharesProvider` ·
`ConvoyIntegrationTests.ConvoyIntegration_5Modules_ShareSnapshot` ·
`ConvoyIntegrationTests.ConvoyIntegration_MemoryUsage_Reduced` ·
`HonestSodGdbTests.BatchInstall_SodModules_ActivatedAtomically` ·
`HonestSodGdbTests.UnionMask_Expansion_NewSodModule_ExpandsSharedProvider` ·
`ProviderAssignmentTests.ProviderAssignment_AsyncSoD_MultipleModules_Convoy`

⚠⚠ **Confirmed by NAME at clean HEAD, not by arithmetic.** ⭐ The counts alone *(189→198 total,
183→192 passed, 6→6 failed)* are consistent with a **fix-one-break-one**; the name-for-name match is
not. ⛔ All six are Convoy/SoD provider-assignment — unrelated to `SystemPhase`.
⭐ **This suite had no standing gate row before `W1` touched it. It has one now** *(`TM-023`)*.
