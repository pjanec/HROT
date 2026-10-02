<!--STATUS
state: LIVE
build-state: DISPATCH — BACKEND lane. QA-013: the 52 stable integration reds are now VISIBLE (the leak
  fix let the suite finish). This batch CLASSIFIES them, FIXES the stale-assertion ones, and REFILES the
  real defects as area-scoped follow-ups. ⛔ NOT "fix all 52" — triage + split.
updated: 2026-08-26
current-answer: this handoff. Basis: REPORT_Test_Suite_Reliability.md §3a/§4 + TESTING_Harness_And_Goldens.md §7.
  Durable output: a classified table in TESTING_Harness_And_Goldens.md + tracker Area N + area-scoped QA- follow-ups.
known-conflict: reads/edits Hrot.ClusterRunner.Integration.Tests (backend-owned). ⚠ Real defects in
  REPLICATION production neighbour the merged Axis-B/Q59 work; in MISSION production neighbour the MCP lane
  (MX4b). ⇒ classify freely; FIX only test-side + clearly-owned production; REFILE cross-lane real defects.
-->
# HANDOFF — **The 52 integration reds: classify, fix the stale, refile the real** *(BACKEND lane — QA-013)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Continue **`QA-`** ids, tracker **Area N**; you allocate the numbers, state them *(rules 3/5)*.
> 🎯 The suite now FINISHES *(QA-001..006)*, so for the first time the 52 reds are all visible at once. **Turn 52 opaque reds into a classified ledger: fix the stale assertions, refile the real defects bounded by area.** ⛔ This is a TRIAGE batch — no feature UML needed; the basis is the two docs above.

## 0. ⛔ DISCIPLINE
Investigation-led *(decide-and-log; stop the ITEM not the batch — R-106)*. ⛔⛔ **R-131 — no permanent filter-around; no new skip** *(a red left standing must be a FILED real defect with a repro, never a shrug)*. **Build once, then `--no-build`** *(`run 3-5 in the report are the same binary)*. ⛔ Full-solution build is banned in the fix loop — build `Hrot.ClusterRunner.Integration.Tests` + only the project a real fix touches. Codebase-memory not connected ⇒ the **CLI**, not grep-only.

## 1. ⭐⭐⭐ WHAT TO BUILD *(four items)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐⭐⭐ **①** | **ENUMERATE** — run the completing suite *(`dotnet test Hrot.ClusterRunner.Integration.Tests --no-build`)*, capture the **52 failing names**, group by **class → area** *(the 5: replication · cluster transition · recording · mission control · EQS — report §4)* | ⚠ the union is 55; the extra 3 are the varying `Eqs` cases *(named in report §4)* — list them separately, they are a smaller residual |
| ⭐⭐⭐ **②** | **CLASSIFY** each: **real-defect** \| **stale-assertion** \| **environmental**, each with **(a)** a base-proof *(the report's filtered-subset-on-`dbdc5e783` method — 38/52 already confirmed red at base; establish the other 14)* and **(b)** the owning DESIGN cited *(§2 pointers — sweep-before-triage, R-129)* | ⛔ *"the code does X"* ≠ *"X is intended"* — read the owning design; a red may be the CODE behind the design, not a wrong test |
| ⭐⭐ **③** | **FIX the stale-assertion group** *(correct the assertion; **inverse-edit red-proof** each)* + any **real defect whose fix is contained in the test project or clearly backend-owned production** | ⛔⛔ a fix that touches REPLICATION production *(Axis-B/Q59 — merged)* or MISSION production *(MX4b — MCP lane building it now)* is CROSS-LANE ⇒ **refile it (item ④), do NOT fix here** |
| ⭐⭐ **④** | **REFILE the remaining real defects as area-scoped `QA-` follow-ups** — one bucket per area, each with a crisp repro + the design basis + the owning lane, so the next batch is bounded and dispatchable | ⭐ this is the deliverable that makes the 52 actionable; ⛔ a real defect with no follow-up id is a shrug (R-131) |

## 2. ⭐ SWEEP-BEFORE-TRIAGE — the owning designs per area *(cite one per classified red — R-129)*
| area | owning design(s) to read for intent |
|---|---|
| **replication** | `DESIGN_Cgf_AxisB_Rotation_Slice.md` §13-16 *(AX-009/Q59 as-built — NetworkTransform-at-birth, egress boundary)* · `DESIGN_Deterministic_Network_Ids.md` |
| **cluster transition / scenario load** | `MCP_Integration.md` §Group U *(HN-029 scenario-load modes, AS-BUILT)* · `DESIGN_Deterministic_Network_Ids.md` |
| **recording / replay** | `docs/designs/replay-browser-2/DESIGN.md` · `DESIGN_Perspective_Unification.md` · ⚠ overlaps **`QA-012`** *(branched-recording Prepare/Finalize write path — already filed)* |
| **mission control** | `docs/designs/tactical-intent/DESIGN.md` · `docs/designs/group-maneuvers/Squad_Coordination_Design_v1_1.md` · ⚠ neighbours **MX4b** *(mission editing — MCP lane)* |
| **EQS** | `docs/designs/eqs-2/EQS_Design_v1.3_final.md` |

## 3. ⭐ DONE — acceptance
- the **52** are enumerated + classified *(a table: name · area · verdict · base-proof · design-basis · fix-or-refile)*; the **stale-assertion group is FIXED + red-proved**; every remaining real defect has an **area-scoped `QA-` follow-up** with a repro; the 3 varying `Eqs` cases are captured separately.
- suite still finishes + is stable across the repeats *(no regression from the assertion fixes)*; working tree clean; **no new skip** *(R-131)*. Base-sha stated for every "pre-existing."
- ⚠ Also route the two **`EcsPatchContextTests`** reds *(report §4 — pre-existing, from the Q59/Axis-B merge)* to the same refile bucket, noted as that merge's owner.

## 4. ⭐ LANE & GATES
⭐ **BACKEND lane.** ⭐ **Yours:** `Hrot.ClusterRunner.Integration.Tests` + any backend-owned production a contained fix touches. ⛔ **Do NOT fix replication/mission production** *(cross-lane — refile)*; ⛔ do NOT touch DebugApi *(MCP lane)*, scenario/menu/AiShared *(UI lane)*. rule-4 re-pull.
**Gates (rule 8):** the enumerate/classify table itself · before/after counts across the repeats · `--no-build` column · base-sha per pre-existing · `tracker-counts.py` · `rulings-check.py` · the `QA-` ids. **When done:** fold the classified table into **`TESTING_Harness_And_Goldens.md`** *(a new §8, or extend §7)* — ⛔ the report is ephemeral; the durable record is the testing design + the tracker + the refiled ids.
