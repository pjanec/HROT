# CGF-1-BATCH-25: CI truth + fail-loud E2E harness + CGF CLI parity

**Batch number:** CGF-1-BATCH-25  
**Goal:** Close **P1–P2 debt** from [CGF-1-BATCH-24 review](../reviews/CGF-1-BATCH-24-REVIEW.md) so **tech debt does not accumulate**: green default unit tests, **no silent skips** in S0310 handlers, **reachable `cgf`** mode, and **proven** E2E execution path.  
**Phase:** Post Phase 3 hardening; optional P3 if capacity  
**Estimated effort:** 4–10 h (Part A) + 4–8 h (Part B) + 4–8 h (Part C optional)  
**Priority:** **P1** Part A — `Hrot.ClusterRunner.Tests` currently fails one mode test  
**Dependencies:** [CGF-1-BATCH-24 review](../reviews/CGF-1-BATCH-24-REVIEW.md); [CGF-1-BATCH-24 report](../reports/CGF-1-BATCH-24-REPORT.md)

---

## Sequencing (tech debt first)

1. **Part A (P1)** — Restore **`Hrot.ClusterRunner.Tests` green** and align **`RunMode` / parser** with product (`All` includes Orchestrator; optional explicit `cgf`).  
2. **Part B (P2)** — **Fail-loud** fixes in [`OrchestratorActionHandlers.cs`](../../../Hrot.ClusterRunner/Testing/OrchestratorActionHandlers.cs) + **`AssertionRule`** rename (CS0108).  
3. **Part C (P2)** — **CI / integration** for `DsmE2eScriptTests` (or documented pipeline + traits).  
4. **Part D (P3)** — IG `TestHook_ClusterSlave` harness; TASK-DETAIL vs `MovingTestTag` placement.

---

## Onboarding

1. [CGF-1-BATCH-24 review](../reviews/CGF-1-BATCH-24-REVIEW.md)  
2. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-25**  
3. [`HrotRunnerConfiguration.cs`](../../../Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs) — `ParseModeString`  
4. [`RunMode.cs`](../../../Hrot.ClusterRunner/Configuration/RunMode.cs)

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-25-REPORT.md`  
**Review:** `.dev/cgf-1/reviews/CGF-1-BATCH-25-REVIEW.md`

---

## Part A — Runner mode tests + CGF CLI (P1 / P2)

### A.1 — Fix `RunnerConfigurationTests` vs `RunMode.All` (P1)

- **`ParseMode_ComboAllThree_EqualsAllFlag`:** Either rename and assert **`RunMode.SimHost | RunMode.IG | RunMode.IOS`** only, **or** extend combo to **`simhost,ig,ios,orchestrator`** if the test name must stay “equals `All`”.  
- **`ParseMode_AllMode_HasAllThreeFlags`:** Rename / extend to assert **Orchestrator** as well (and document **CGF** not in `All` unless product changes).

**Acceptance:** `dotnet test Hrot.ClusterRunner.Tests` — **0 failures**.

### A.2 — Parse `cgf` in `HrotRunnerConfiguration` (P2)

- Support **`cgf`** as standalone `ModeString` and in **comma-separated** lists (e.g. `orchestrator,cgf`), mirroring `simhost` / `ig` / `ios`.  
- Update **CLI help** in [`HrotRunnerConfiguration`](../../../Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs) / [`Program.cs`](../../../Hrot.ClusterRunner/Program.cs) comments if needed.  
- Add tests: standalone `cgf`; combo `orchestrator,cgf`; invalid token still rejects whole string.

**Acceptance:** `RunMode.CGF` reachable from CLI; Part B node-id offsets for **`CgfSubsystem`** become **product-relevant**, not mock-only.

---

## Part B — S0310 fail-loud + hygiene (P2 / P3)

### B.1 — `AssertEntityCountActionHandler` / `AddMovingTagActionHandler`

- When **`EntityRepository`** is null: **`throw InvalidOperationException`** (fixture mis-wiring) — **no** warn-and-success.  
- When **entity not alive** for `add_moving_tag`: **throw** — **no** silent return.

### B.2 — `AssertionRule.Equals` → rename

- Rename property to e.g. **`Exactly`** (or add `new` with strong XML warning). Update JSON deserialization / executor if the property is bound from scripts. Eliminate **CS0108**.

### B.3 — TASK-DETAIL alignment (pick one)

- Move **`MovingTestTag`** next to **`MovingEntitySystem`**, **or** update **CGF-1-TASK-DETAIL** §S0310 to describe the Runner Testing location.

---

## Part C — E2E DSM tests in CI (P2)

- Add **integration test** stage (or document existing) that runs **`Hrot.ClusterRunner.Integration.Tests`** with DDS-friendly settings (serial collection, domain isolation).  
- Optionally **`[Trait("Category", "DsmE2e")]`** on `DsmE2eScriptTests` so default fast runs can skip while **nightly** runs full suite — **document** in report if that is the chosen policy.

**Acceptance:** Lead-signed definition of “S0310 verified in CI” (either every PR or scheduled).

---

## Part D — IG handler harness (P3)

- From BATCH-23: enable **handler registration** tests for IG without full DDS boot, or document **explicit deferral** with lead sign-off and close DEBT row.

---

## Success criteria

- [ ] `Hrot.ClusterRunner.Tests` **all pass**.  
- [ ] **`cgf`** mode parseable from CLI; tests cover combos.  
- [ ] No **silent skip** paths in S0310 handlers listed in review.  
- [ ] **`AssertionRule`** CS0108 resolved.  
- [ ] E2E CI policy **implemented or explicitly documented** in BATCH-25 report.  
- [ ] DEBT-TRACKER rows for BATCH-24 → **closed** or **re-targeted** with justification.

---

## Reference

- [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §**CGF1-S0310**  
- [CGF-1-BATCH-24 instructions](./CGF-1-BATCH-24-INSTRUCTIONS.md)
