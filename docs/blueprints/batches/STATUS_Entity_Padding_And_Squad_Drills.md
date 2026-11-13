<!--STATUS
state: LIVE — DONE (one residue open: CE-3445)
updated: 2026-11-13
current-answer: the whole file (NEXT is the resume point)
related-designs:
  - HANDOFF_Coordinator_To_Backend_Entity_Padding_And_Squad_Drills.md — the work E-1 … E-4 (CE-4507 … CE-4510), dispatched at 1eb35f201
  - STATUS_Mode_And_Serial_Systems.md — M-D12 (Entity padding) and §5 OPEN (SquadManeuverTests 78 red with parallel brains): the questions this answers
  - STATUS_Windows_Findings.md — the stepped gate this batch must keep equal
  - ../../DESIGN_Parallel_Simulation.md — §18.6 the brains and squads as phases; R-4212 the one-tick delay on cross-entity effects
-->

# STATUS — a canonical `Entity`, and the squad drills under parallel brains

Branch `exp/threaded-build`. Started while the Windows batch's F-3 probe runs (the contract's order for an item that waits on a measurement).

## 1. DONE

| item | what | commit |
|---|---|---|
| E-1 `CE-4507` | ⭐ `Entity` has no undefined bytes: a private `ushort _zero` after `Generation`, set by every constructor; size stays 8 (recordings read). 📐 Measured hidden from the usual UI first: the inspector's StructEdit builder (`ReflectionEditDocumentBuilder` — public fields), the scenario serializer (`ComponentFieldDiscovery` — public unless the remap asks; `EntityRemapPlan` stops at `Entity`), System.Text.Json `IncludeFields` (public only); the HTTP API and the pickers use `Entity`'s own converters / renderer. Rails `EntityTests.E1_*` (the fields cover all 8 bytes — red-proved; any-way-built bytes canonical; the public surface and its JSON unchanged) | `d8e47f3c0` |
| E-2 `CE-4508` | ⭐ the squad harness runs ONE frame in `ModuleHostKernel.Update`'s order (`SquadManeuverTests.Squad.Tick`: the world's Tick, the Input phase + playback, the swap, the stand-in harvests, the tick-start view, Simulation, playback) — serial AND `HROT_PARALLEL_SIM=1` | `1c07af9f0` |
| E-3 `CE-4509` ② (behaviour) | ⭐ R-195 — an SOP wakes when `RecentSenses` RECORDS a change (the record lands in Input, one frame after the event was readable): the SOP had woken a frame early, read nothing, and reacted on its 0.2 s cadence. `RecentSenses.RecordedAt`; the event-frame wake kept. Red-proved: 12 drills | `1c07af9f0` |
| E-3 ① (frame budgets) | the drills' budgets to the production order — +1 per hop (an order into the brain, a test-injected sense, a pool edge, a platoon's orders to its squads); `SopReactionFrames` = 6, measured; no assertion changed | `1c07af9f0` |
| E-3 ③ (per-entity-rule defects, all three in the product) | ⭐⭐ "own entity: direct access" held for writes, not for ADDS: ① a managed component a job adds to its own entity — its own later reads now see it (`BatchContext.RememberOwnManaged`, the repository's managed read path) — `SubordinateOrders.Record` had kept only the tick's last order, so plays re-sent the rest; ② the unmanaged twin — `EntityRepository.TryGetOwnPendingAdd` — `ReportQueue.Enqueue` had lost a tick's earlier reports; ③ a child sensor ensured inside a phase now lands DRESSED (`EqsChildSensor.Ensure` hands back the buffer's pending child; ⚠ a buffer entity has a negative index, so `!= default`, never `IsNull`). Rails red-proved: `PerEntityRuleTests.E3_*` (1/2/4/8 threads), `DangerAreaSensorSystemTests.E3_…` | `d0437b497` |

## 2. GATES

| gate | command | result |
|---|---|---|
| ⭐⭐ E-2/E-3 the drills | `SquadManeuverTests`, serial and `HROT_PARALLEL_SIM=1` | **228 / 228 both ways** (were 228 / 150: 78 red with parallel brains) |
| E-3 the touched suites | `PerEntityRuleTests` · Toolkits `DangerAreaSensorSystemTests`, `WorldSurvey*`, `Report*`, `Staff*` · Toolkits behaviour suites · `CgfLogicPackTests` | 10 / 0 (1 skip, pre-existing) · 85 / 0 · 640 / 0 · 17 / 1 (`CE454`, red before) |
| E-1 | `Fdp.Core.Tests` whole | 1366 passed / 6 red, all pre-existing or load-dependent: three source-policy scanners naming files this batch did not touch (`BridgeRoads.cs`, `DeployProcessorSystem.cs`, …), `N10` / `P0` / `Publish_StressTest` pass alone (run beside a stepped series) |

## 3. DECISIONS

| # | question | decision | where |
|---|---|---|---|
| E-D1 | raw-blitted rows from OLD recordings may carry non-zero bytes into `_zero` | accepted: `Equals` / `GetHashCode` read Index and Generation only; a world loaded from one recording hashes the same every time; new writes are canonical | `Entity.cs` |
| E-D2 | the squad harness in the production order — imitate it, or drive a real kernel? | the harness's `Tick()` is ONE frame in `ModuleHostKernel.Update`'s order (Tick → Input phase + playback → swap → harvest stand-ins → tick-start view → Simulation → playback), documented step by step; ⛔ a kernel per rail would change what every rail sets up | `SquadManeuverTests.Squad.Tick` |
| E-D3 | the order alone reddens 61 SERIAL drills — which half? | 📐 bisected: `Repo.Tick()` alone → 0 red; the order without it → 61. Mechanism: a sense edge raised in Simulation is swapped at the NEXT frame's swap, after that frame's Input-phase `RecentSensesSystem` ⇒ recorded one frame later than the old order (swap first) modelled. ⇒ class ① tick budgets, written to the wrong order | E-3 |

## 4. OPEN

| item | why | what it needs |
|---|---|---|

## NEXT (the resume point)
⭐ DONE: E-1 … E-4 (tracker ticked). The stepped gate on these fixes: switch off 12 / 12 equal at 1 / 2 / 4 / 8; mode 3 35 of 36 — the one
residue is OPEN as CE-3445 (STATUS_Windows_Findings F-D4).
