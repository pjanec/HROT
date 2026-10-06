<!--STATUS
state: LIVE — the behaviors lane's running reply to HANDOFF_Danger_Crossing_Behaviors.md (dispatched at de30b58cb).
  🔒 User, 2026-10-06: "merge the peer lane work as you go, write them requests to handoff doc so when they merge they find
  it and can act on it, making you 2 synchronizing and coordinating autonomously."
updated: 2026-10-06
current-answer: §1 (requests — act on these) and §2 (status per item). Read §1 at the start of every backend slice.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Danger_Crossing_Behaviors.md — the dispatch this answers (scope frozen at de30b58cb).
  - docs/DESIGN_Utility_AI_Demo_Scenarios.md — §10.4 / §10.5 / §10.6 own the items; as-built notes are folded there.
  - docs/DESIGN_Sensors_And_Doctrine.md — §7.10 owns N1–N4; the H6 detail design (UML) lives there (§7.10a).
-->

# REPLY — behaviors → backend, the danger-crossing demo

**How to use this file.** The behaviors lane appends here whenever it needs something from the backend lane or learns
something the backend's slices depend on. ⭐ **§1 is the inbox: act on each OPEN row, then mark it ✅ with your commit sha**
(either lane may edit the row's status cell). §2 is status, for information.

## 1. Requests to the backend *(act on these)*

| # | state | what | why / where | blocks |
|---|---|---|---|---|
| **Q1** | OPEN | ⭐ **Keep ONE reading surface on every sensor result component**: `int Count`, `bool IsReady`, `uint LastUpdateTick`, `ReadOnlySpan<TElement> GetSpanRO()` — `EqsCognitiveBuffer` and your B0 `DangerAreaCognitiveBuffer` (`4633ed73a`) both already have it. Please STATE it as the contract on `SensorKindInfo` / `SensorKindRegistry` (a doc comment is enough; an interface is welcome but not needed — the blueprint lowering writes text against these member names) | N2 `ReadSensorResult(kind, i)` lowers to the same four member reads for every kind; a new kind whose buffer names them differently would compile-fail in generated code (Sensors §7.10a) | H6 N2 (not today — both buffers comply) |
| **Q2** | OPEN | ⭐ **Name the spawn entry point per kind on `SensorKindInfo`** — e.g. `string? EnsureMethod` (FQN of a static `Entity Ensure(EntityRepository, Entity owner, int site, in TSettings settings, long key)`): `Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.Ensure` for `DangerArea`; `null` for the perception kinds (TKB-owned, not spawned by a behaviour) and for `EqsQuery` (spawned by template through the existing `SpawnEqsSensor`) | N3 `SpawnSensor(kind)` lowers to that call; the compiler (netstandard2.0) cannot reflect, so the editor bakes the FQN into the node from the registry (Sensors §7.10a). `null` ⇒ the kind is not offered in the `SpawnSensor` palette | H6 N3 |
| **Q3** | OPEN | ⭐ **Give each family trigger its DATA, not just its name**: `Triggers` as `(string Name, string? ElementField, TriggerShape Shape)` with `TriggerShape { Header, FieldChanged, FieldCrossed }` — header ones `FirstReady` / `Changed` / `BecomesStale` (Shape Header), area `("NextAreaChanged", "FeatureId", FieldChanged)`, `("ThreatCrossed", "ThreatRating", FieldCrossed)`, ranked `TopChanged` / `ScoreCrossed` stay on the existing `When EqsResult` | N4 `When SensorResult(kind, trigger)` then lowers GENERICALLY (entry 0's field changed / crossed a threshold) — without it the compiler has to hard-code `FeatureId` / `ThreatRating`, i.e. a per-family branch in the compiler for every new family | H6 N4 |

⚠ **If you prefer another shape for Q1–Q3, change B0 and note it here** — the H6 design (§7.10a) is written against these
three and will follow whatever B0 ships.

## 2. Status *(for information)*

| item | state | commit | note |
|---|---|---|---|
| H1 `ThreatsAtLeast.WithinMetres` | ✅ BUILT | `cbe8bf06f` | ground distance (XY) from the unit's `SimTransform` to each REMEMBERED position; 0 = any distance; a unit with no `SimTransform` counts none |
| H2 `Sentry` BTree | 🟡 in progress | — | ⚠ **deviation:** the kernel `Wait` takes a CONSTANT duration (`BTreeWaitPayloadDto.Duration`), so `WaitSeconds` is NOT a param — the tree waits 15 s (§10.5's value); `WithinMetres` IS a param (the `sentry` variable, overridable in the order's params JSON: `{"sentry":{"WithinMetres":90,...}}`). Tell me if B6 needs the wait tunable — that is a kernel Wait-from-variable change (a new item) |
| ⭐ **CE-2112** (found by H2) | 🟡 fixing | — | 🔴 **every brain-ticked BTree's `Wait` and `Cooldown` were dead**: `BTreeRunner` never set `BTreeContext._time`, so `ctx.Time` was always 0 — a `Wait` never completed. Fixed to `repo.SimulationTime` (= `GlobalTime.TotalTime`, `ModuleHostKernel.cs:498`). ⚠ **Affects B6**: the hostile's `Sentry` → task 2 hand-over only works with this fix — merge `behaviors` before B6 |
| ⭐ **CE-2111** (found by H2) | 🟡 fixing | — | the BTree JSON generator silently DROPPED a decorator authored as a node kind (`"kind": "UntilSuccess"` → a `// Unknown node type` comment, and its subtree vanished). Now `#error`. A decorator is authored as a PILL on its host node. No shipped asset used the node form |
| H3 / H4 / H5 / H6-build / H7 | ⏳ waiting | — | on `feat(CE-3072 B0)` (seen: `wip` `4633ed73a` only) |
| H6 step 1 (detail design) | 🟡 in progress | — | Sensors §7.10a, written against Q1–Q3 |
