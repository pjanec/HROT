<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this file is a BATCH — scope, items, gates, verdicts. It carries NO design.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐ BATCH TM-106 — **`T3` · `T3a` · `T3b`: one bus per node**

> ⛔ **This is a batch, not a design** *(CLAUDE.md ①b)*. ⭐ **Design:**
> [`DESIGN_Time_Architecture.md`](../DESIGN_Time_Architecture.md) **§11** *(the uniform bus pattern,
> the editor's one-line deviation, and the two smaller findings)*. Implemented intent:
> [`docs/designs/time-ctrl-unif`](../../designs/time-ctrl-unif/docs/DESIGN.md) §4.4/§4.6.
> ⭐ **Lane:** TIME, self-directed. ids **`TM-`**, tracker **Area H**.
> ⛔ **`W0`–`W5` are the coordinator's** *(user, `2026-08-21`)* — not touched.

## Items

| id | item | verdict |
|---|---|---|
| **`106a`** | **`T3`** — build the editor's `MasterSyncController` on `_orchestrationBus`, not `_world.Bus` | ✅ **done** — one line, `EditorSubsystem:727` |
| **`106b`** | **`T3a`** — verify SimHost's adapter bus vs CGF's | ✅ **done** *(measurement)* — ⛔ **no divergence to fix** |
| **`106c`** | **`T3b`** — verify the intents are registered on the bus that carries them | ⛔⛔ **CONFIRMED A LIVE PRODUCTION DEFECT — fixed** |

## `106b` — the answer is "they already agree"

📐 `SimHostApp.cs:466` — `_eventBus = _context.EventBus;` · `:667` — `OrchestrationEventBus => _eventBus`.
⇒ ⭐⭐ **one bus, two names.** The design's *"CGF and SimHost hand the adapter DIFFERENT buses"* was a
**naming artifact**. ⛔ **Nothing changed.** ⭐ Exactly the "one line to check" the design predicted —
and the value was in checking, not in fixing.

## `106c` — **pressing pause on a CGF/SimHost/IG toolbar THREW**

| 📐 measured | |
|---|---|
| `ClusterTimeTransportAdapter` publishes **5 intent types** on the node's bus | `Pause` · `Resume` · `Step` · `SetTimeScale` · `TransitionState` |
| `OrchestrationEventRegistry.RegisterAll` is called on… | the Orchestrator's `_bus` · ExCon ×2 · the editor's `_orchestrationBus` · **`CgfApplication:115`, whose only caller is a unit test** |
| ⛔ **never on `HrotNodeContext.EventBus`** | the bus CGF, SimHost and IG actually use |
| ⛔⛔ **and production runs STRICT** | `Hrot.ClusterRunner/Program.cs:52` sets `FdpConfig.EnforceExplicitEventRegistration = true` ⇒ `PublishManaged` **throws** for an unregistered type |

⚠⚠ **The design guessed this would *"make a toolbar silently do nothing"*. ⛔ It is louder and worse:
`InvalidOperationException`.** ⭐ **Reproduced RED before fixing** *(`R-124` discipline)* —
`Build_NodeEventBus_HasTheTimeControlIntentsRegistered` failed with
*"Strict Mode Violation: Managed event type 'PauseTimeIntent' was published without being explicitly
registered"*, then went green on the one-line fix in `HrotNodeBuilder.Build()`.

## ⚠⚠ THE HARNESS WAS MIRRORING THE DEFECT — **and that is its own finding**

📐 `EditorHarness` **does not construct `EditorSubsystem`** — it **mirrors** its wiring *(`:215`
"Mirror the EditorSubsystem wiring…")*. 📌 It reproduced **both** defects:
`:156` built the master on `Bus` while `OrchBus` sat separate, it never called `RegisterAll`, and
⛔ **`PumpFrames` never swapped `OrchBus` at all** — so every orchestration intent was silently dropped.

⇒ ⭐⭐⭐ **A mirror that has drifted from the thing it mirrors is worse than no harness: its tests stay
green while production breaks.** ⭐ Moved with production, in the same commit, and its pump now mirrors
`EditorSubsystem.Update`'s ordering *(kernel first, then swap the control-plane bus)*.
⚠ **Stated plainly: the new rails guard the SHAPE, not the real composition root.** ⛔ Constructing
`EditorSubsystem` headlessly is the honest fix and is **not** in this task — 📌 `TM-016`.

## Gate results

| gate | baseline | after | Δ |
|---|---|---|---|
| solution build | 0 errors | ✅ **0 errors** | **0** |
| `~TimeControlIntegrationTests` ×2 | 9 / 0 | ✅ **9 / 0**, **9 / 0** | **0** — no flake |
| `Hrot.ClusterRunner.Tests` | 262 / 2 | ⚠ **262 / 2** *(total 264)* | **+2 rails**; the 2 reds are the documented `DataDrivenGizmoPredicateTests.D003_*` |
| `EditorSubsystemBootTests` | 10 / 0 | ✅ **12 / 0** | **+2 rails** |
| `Hrot.Editor.Tests` | 206 / 0 | ✅ **206 / 0** | **0** |
| ⭐⭐ **every `EditorHarness`-dependent class** *(13, in isolation)* | per-class baseline | ✅ **identical, class by class** | **0** — the harness change regressed nothing |
| `Fdp.Toolkits.Tests` ▸ `Fdp.Toolkit.Time.Tests` | — | ✅ **166 / 0** | my area is clean |
| `Fdp.Toolkits.Tests` full | 1981 / 0 | ⚠ **see below** | ⛔ **a FLAKE, characterised — not a regression** |

### ⚠ `Fdp.Toolkits.Tests` — **characterised rather than waved at `DEBT-AIB-030`**

📐 Full-suite runs gave **1978/3**, then **1980/1**, with a **different identity** each time.
⚠ **A single clean-HEAD sample passed**, which looked like "my change broke it" — ⛔ **it proved
nothing.** 📐 **Five runs of the named test on the IDENTICAL binary: 1 fail, 4 pass.**
⇒ ⭐⭐ **`Fdp.Toolkit.Squad.Tests.DangerAreaProviderTests.FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup`
is non-deterministic on identical input** — a zero-allocation assertion, so GC/JIT-sensitive.
⛔ **Unrelated to the time lane** *(different namespace; this batch touches no `Squad` file)*.
⭐ Filed as **`TM-015`**, because it is a **named, reproducible-rate** flake and `DEBT-AIB-030`'s list
is not a licence to stop naming them.
