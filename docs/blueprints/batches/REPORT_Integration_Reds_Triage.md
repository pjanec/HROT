<!--STATUS
state: LIVE
doc-type: batch report (EPHEMERAL — the durable record is docs/TESTING_Harness_And_Goldens.md §8 and
  the tracker Area N rows QA-013..QA-026).
updated: 2026-08-26
current-answer: §1 the split · §2 what was fixed · §3 the refile · §4 gates · §5 what is NOT fixed
design-basis: HANDOFF_Integration_Reds_Triage.md (dispatch dc2a0efbf) · TESTING_Harness_And_Goldens.md
  §7-§8 · R-131 (no permanent filter-around) · R-129 (read the owning design) · rule 8 gate contract.
-->
# REPORT — **The integration reds: classified, three stale fixed, one real defect fixed** *(BACKEND — `QA-013`)*

> 📌 **Dispatched at `dc2a0efbf`.** Rule-1b started-marker `defc2a091`. Branch
> `claude/blueprint-macro-feature-sdmspn`. ⛔ No PR.
> ⭐ **IDs allocated (rule 5): `QA-014` … `QA-026`**; `QA-013` itself closes.
> 🎯 **The handoff's frame — "⛔ NOT *fix all 52* — triage + split" — is what this delivered.**

---

## 1. ⭐⭐⭐ THE COUNT IS **51**, AND ONE MEASUREMENT SPLITS IT

⚠ **Not 52.** On the merged tree the suite reports **267 discovered / 51 failed / 3 skipped, 0 OOM**.
The prior batch's stable 52 minus the two `EventSerializationHelperTests` that `QA-007` fixed, plus the
`Eqs` family's usual churn.

### ⭐⭐ Run each failing class ALONE — nothing in the failure TEXT tells you this

```bash
dotnet test <proj> --no-build --filter "FullyQualifiedName~<Class>"
```

| | n | verdict |
|---|---:|---|
| red in the SUITE **and** ALONE | **43** | ⛔ **genuine, deterministic** |
| red in the SUITE, **GREEN** ALONE | **8** | ⚠ **environmental** — timing under suite load |

⇒ ⭐⭐⭐ **~20 minutes, and it is the difference between filing a defect and filing noise.** Every one of
the 8 is timeout-shaped; so are many of the 43. **Only the isolation run separates them.**

### 📐 BASE-PROOF — *(base `dbdc5e783`, the tree before `QA-001..006`)*

⚠⚠ **The base tree cannot run these classes TOGETHER**: a 7-class filtered subset still **ABORTS**
(`Total tests: Unknown`, 1 passed). ⇒ base-proving had to go **one class at a time**.

| | n |
|---|---:|
| ✅ red at base | **47** |
| ✅ **green** at base ⇒ environmental, not a regression | 1 — `EqsContextSlotTests` **7/7** |
| ⚠ not evaluable *(base host aborts first)* | 4 — `ClusterOpE2eScriptTests` |

⇒ ⭐ **No red is attributable to `QA-001..006`**, and the 4 unevaluable ones now have a named root cause
anyway (`QA-018`).

---

## 2. ⭐⭐ WHAT WAS FIXED — **4 buckets, 5 tests recovered**

### 2a. Three STALE assertions *(`QA-014` · `QA-015` · `QA-016`)*

⭐ Each decided by **measuring which side was wrong**, per `R-129` — ⛔ never by assuming the test was.

| test | asserted | 📐 why stale |
|---|---|---|
| `EqsResult_FlagsMeaningful_StructSizeUnchanged` | `sizeof == 24` | `P3D-201` added `PositionZ` ⇒ 8+16+4 = 28 → **32**. ⚠ **This project's own csproj already defines `EQS_HAS_POSITIONZ`** for that promotion — only the assertion lagged. ⭐ The invariant it guards **still holds** (the old `_pad` gave 32 too), and is now written down so the literal number cannot drift again |
| `CaptureLiveState_WithoutDebugMap_…EmptyFields` | `FieldValues` empty | `CaptureStateSnapshot` reads `_debugMaps` **only for the asset name**; fields come from `_registry`. ⭐ And the design says `RegisterDebugMap` binds **BREAKPOINTS** — never a precondition for capture. The test pinned an implementation detail |
| `SaveScenario_SubsystemTypeIsHrotScenario` | `Header.SubsystemType` | `ScenarioSerializer:199-200` writes `Header` with only `TkbName`, then the **`$meta`** envelope; the serializer's own comment calls `Header.SubsystemType` the **LEGACY** shape the LOAD path still accepts ⇒ a freshly saved file never had it. ⚠ The test also **swallowed its own fallback**, so the failure read *"Operation is not valid due to the current state of the object"* — naming nothing |

⭐ **Red-proof:** all three were red in the enumeration run on the pre-fix binary and green after; the
assertion is the only delta in each file. Verified **10/10** across the three affected classes.

### 2b. ⭐⭐⭐ One REAL defect, fixed — and it was hiding a second *(`QA-018`)*

🔴 **`SimHostApp.TestHook_AddSystem`'s contract was impossible to satisfy.** It documented *"must be
called AFTER `InitializeEmbedded`"* and threw `if (!_initialized)`; `RegisterGlobalSystem` throws
`if (_initialized)`. ⇒ **the test was doing exactly what the hook's own comment told it to.**

⭐⭐ Fixed at the seam that already existed, **and whose own comment states the rule** —
`SimHostApp`'s Phase 6d `ApplicationSystemsRegistrar` (*"RegisterModule / RegisterGlobalSystem throw
after Initialize() — must run in Phase 6d"*). `TestHook_AddSystem` → **`TestHook_QueueSystem`**:
queue before the boot, drain in that callback. ⭐ One mechanism, one caller, no post-Initialize path
left to misuse *(ruling 9)*.

🔴🔴 **And the first defect was hiding a second.** With the ordering fixed, registration reached a
validation it had never got to: *"System `MovingEntitySystem` must have `[UpdateInPhase]`"*. Added as
**`PostSimulation`** — ⛔ deliberately not `Simulation`, which the kernel runs for **module** systems on
background threads only, so a global system marked with it would **silently never execute**.

📐 **`ClusterOpE2eScriptTests` 0/4 → 2/4.** ⚠ The residual two (`RecordAndReplaySeek_Passes`,
`LiveFromReplayBranch_Passes`) fail on a script-step assertion (`Expected 0, Actual 1`) — a **third**
layer, and both are **record/replay** ⇒ they belong with **`QA-012`**'s branched-recording write path.

---

## 3. ⭐⭐ THE REFILE — **`QA-017` … `QA-026`, bounded by area**

📄 Full table with root causes and design citations: **`TESTING_Harness_And_Goldens.md` §8.3**.

| id | n | one-line |
|---|---:|---|
| **`QA-017`** | 7 | cluster transition — the 2PC never leaves state `0`. ⚠ **two obvious causes already refuted** *(the roster IS populated; the bootstrap latch is NOT the gate)*, and 📐 `MCP_Integration.md` §Group U AS-BUILT has `--mode all` reaching `OperatingLive` ⇒ **suspect the in-process harness's drive, not the state machine** |
| **`QA-019`** | 4 | `SwitchToExternalAsync` uninstalls `SimHostCoreLogicPack`, which is not installed ⇒ the uninstall list and the install list diverged. ⚠ editor production — **UI-lane neighbours** |
| **`QA-020`** | 17 | 🔴🔴 replication: created but never **promotes** / never takes **authority** / never **moves**. ⭐⭐ **ONE investigation, not seventeen** — every message is the same shape. ⛔ **CROSS-LANE** |
| **`QA-021`** | 1 | `MissionControlRequest` never reaches DDS. ⚠ neighbours `MX4b` (MCP lane) |
| **`QA-022`** | 3 | map/area authoring: the creation tool never activates; `EditablePolyline` never attaches |
| **`QA-023`** | 1 | `BlueprintStateTranslator.Inject` does not set `InitialBlueprintsIntent` for the mixed-keys case |
| **`QA-024`** | 3 | EQS phase machine never reaches `_AwaitingRaycasts` / never publishes |
| **`QA-025`** | 8 → **9** | ⚠ **environmental — ⛔ NOT defects.** Green alone, red under load. ⭐ The 9th was added by this batch's own confirming run and cleared 3/3 ×3 in isolation |
| **`QA-026`** | 2 | the `EcsPatchContextTests` pair, routed here per the handoff — ✅ base-proved, owned by the `Q59`/Axis-B merge |

⛔ **Every real defect has an id, a repro and an owning design.** ⛔ No new skip, no quarantine
(`R-131`); the batch removed none of the reds by hiding them.

---

## 4. ⭐ GATES *(rule 8)*

⭐ **Build once per change, then `--no-build`.** ⚠ Working tree clean after every run; **no golden
moved** (nothing here changes panel content).

| gate | before | after |
|---|---|---|
| `Hrot.ClusterRunner.Integration.Tests` *(full, `--no-build`)* | 267 / 213 P / **51 F** / 3 S · **0 OOM** | ✅ 267 / **217 P** / **47 F** / 3 S · **0 OOM** — §4a |
| `--filter` the 3 stale classes | 7 / 10 | ✅ **10 / 10** |
| `--filter ClusterOpE2eScriptTests` | 0 / 4 | ⭐ **2 / 4** |
| per-class isolation sweep *(24 classes)* | — | 43 genuine / 8 environmental |
| base-sha sweep *(7 classes, one at a time, `dbdc5e783`)* | — | 47 red · 1 green · 4 unevaluable |
| `python3 scripts/tracker-counts.py --check` | — | ✅ `open 102 / done 346 (+1 refuted)` |
| `python3 scripts/rulings-check.py` | — | ✅ 25/25 · ⚠ **2 staleness WARNs, both explainable**: `DataBreakpointManager.cs` *(`QA-005`, previous batch)* and `.claude/CLAUDE.md` *(changed by the coordinator's own `0d3fbadbb`, not by this batch)*. Neither quote moved |
| `python3 scripts/design-digest.py --check` | — | ✅ |

⚠ **`tracker-counts.py` still counts only `BP-` rows** — reported last batch, unchanged, so the new
`QA-` rows are correctly absent from the table.

### 4a. The final full run — **the confirming run for all four fixes together**

📐 **`267 discovered / 217 passed / 47 failed / 3 skipped · 0 OOM · 10m31s`** *(same binary, `--no-build`)*.

| | |
|---|---|
| ⭐ **recovered, diffed by NAME** | **5** — the three stale assertions (`QA-014/015/016`) + `OverlappingCheckpoints_Passes` and `PreviewStateRestore_Passes` (`QA-018`) |
| ⚠ **one NEW red, and it is NOT a regression** | `EyesAndMuscleIntegrationTests.Module_EyesAndMuscleTicks_IncrementAfterPumping` — *"EyesTicks expected > 0, was 0"*, a **background-thread** tick count. Green in runs 1 and 2; 📐 **run alone three times: 3/3, 3/3, 3/3** ⇒ **environmental**, filed into `QA-025`. ⭐⭐ **This is §8.1's isolation check earning its keep on its first day** — without it this reads as damage from the change in the same run |

⇒ 51 → **47**, and the delta is fully accounted for by name: −5 fixed, +1 environmental.

---

## 5. ⚠ WHAT IS **NOT** FIXED — plainly

- **47 reds remain** *(51 − 5 recovered + 1 newly-observed environmental)*, of which **9 are environmental and should never be filed as
  defects**. The other 38 are real and now carry an id, a repro and an owning design.
- ⛔ **`QA-020` (17) and `QA-021` (1) were NOT touched** — the handoff fences replication and mission
  production as cross-lane, and this batch respected that.
- ⭐ **`QA-017` is the highest-leverage next batch after `QA-020`**: 7 tests, one symptom, and two wrong
  hypotheses already eliminated so the next session does not repeat them.

## 6. ⭐ HANDBACK

1. ⭐⭐⭐ **`QA-020` is one investigation, not seventeen.** Scope it to the replication lane whole.
2. ⭐⭐ **`QA-017`'s production path is already proven green in `--mode all`** — start from the harness,
   not the state machine, and read §8.3's refuted-hypotheses note first.
3. ⭐ **`QA-019` and `QA-022` neighbour the UI lane**, `QA-021` neighbours the MCP lane — sequence, don't
   race.
4. ⚠ **`QA-025` needs a decision, not a fix**: should this suite bound its own concurrency? **Nine**
   tests are green alone and red under load, and today every batch pays to re-diagnose them.
5. ⚠ **Rule-4 re-pull, done before the final commit.** The coordinator branch moved
   `dc2a0efbf → 89d59cbeb` **during** this batch *(UI Axis-C E2 / `CE-049/050`, the E3 handoff, `AQ61`)*.
   ⛔ **Scope stayed FROZEN at the dispatch sha** — nothing there was adapted to. 📐 **One file overlaps**
   and is flagged for the merge: `Blueprint_Issues_Tracker.md` *(they added rows in the CE areas; this
   batch rewrote `QA-013` and appended `QA-014`…`QA-026` in Area N — disjoint regions)*.
