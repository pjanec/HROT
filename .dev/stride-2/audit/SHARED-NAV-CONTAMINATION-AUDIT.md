# Shared-navigation contamination audit — Stride integration vs main

**Date:** 2026-06-15
**Branch:** `stride-integ-1`   **Baseline:** `main`
**Trigger:** Hill-attack-2 tanks (non-stride `clusterrunner -m editor`) drive to the baseline,
overshoot, stop, and never run the next behavior. Main is unaffected (golden test).

## TL;DR
- **Root cause of the tank regression = ONE line**: `NavigationExecutionSystem` got `.Without<VehicleState>()`
  added during the Stride "all-in-one" integration (`8b8cc439`). That shared/SimHost system is the
  *only* thing that reports vehicle arrival in the non-stride editor. Excluding vehicles → no arrival →
  behavior stalls. **FIXED**: file restored byte-identical to `main`.
- The **nav-v2 refactor** (`93259b6b`, *"all responsibility on muscle, brain just gives requests"*) is a
  **separate workstream that is already on `main`** (in the common merge-base). It is **not** the regression.
- Several *other* shared `Fdp.Toolkits/Navigation` files were also modified by the integration. They are
  **inert in SimHost today** (SimHost registers no DtCrowd provider and does not register
  `CrowdAgentUpdateSystem`; the tanks are `OrientedBox` so the TKB-translator gate doesn't drop their
  `VehicleState`). But they **will land in `main` on merge** and several embed Stride/split-authority
  concepts into shared code — they need a decision before the stride branch merges.

## How the regression was localized
1. `93259b6b` (nav-v2) is an ancestor of `merge-base(main, stride-integ-1)` → **both branches have it** →
   not branch-unique → not the cause.
2. `git diff main...stride-integ-1` over `Fdp.Toolkits/Navigation`, `CarKinem`, `Hrot.SimHost` →
   10 shared files differ. Only **`NavigationExecutionSystem.cs`** sits on the **vehicle arrival path that
   SimHost actually runs**.
3. The Stride vehicle executor it defers to (`VehicleNavigationIntentSystem`) lives **only** in
   `Hrot.Stride.Core` and is **not** registered in SimHost → in the non-stride editor nothing replaced the
   excluded logic.

## Why the exclusion was added (and why it was the wrong place)
`NavigationExecutionSystem` detects **vehicle** arrival by reading `NavState.HasArrived`, which is written
by **`CarKinematicsSystem`**. `StrideKinematicsModule` deliberately omits `CarKinematicsSystem` (Bullet
replaces it), so in Stride `HasArrived` is never set → the system fell through to its frustration guard and
fired a premature `FailedBlocked` (the comment cites `rb.Simulation == null → SimVelocity=0 → 120-tick
frustration`). The "fix" suppressed that by carving vehicles out of the **shared** system — which also
removed the logic SimHost depends on.

The Stride muscle already reports vehicle arrival correctly **on its own**:
`VehicleNavigationIntentSystem.AdvanceCorner()` writes `NavigationStatus = Arrived` directly when the final
corner is reached. So Stride never needed the shared system for vehicle arrival — the exclusion only existed
to silence the spurious frustration during the body-not-yet-in-sim startup window.

## File-by-file classification

| # | File | Δ | SimHost impact today | Verdict |
|---|------|---|----------------------|---------|
| 1 | `CarKinem/Systems/NavigationExecutionSystem.cs` | `.Without<VehicleState>()` | **BROKE tanks** | **MASK — RESTORED to main ✅** |
| 2 | `CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs` | +50 | Tanks unaffected (OrientedBox keeps VehicleState). **Infantry lose VehicleState** | **REVIEW (high)** — shared TKB now makes a muscle-specific decision |
| 3 | `Navigation/Systems/CrowdAgentUpdateSystem.cs` | ~50 | Inert (not registered in SimHost) | **REVIEW (high)** — split-authority refactor: writes `CrowdMotorIntent` instead of `SimVelocity`/`SimTransform` |
| 4 | `Navigation/CrowdMotorIntent.cs` (new) + `NavigationContractsComponentIds` (265) | +65/+10 | Inert (not registered) | **REVIEW (med)** — Stride/Bullet split-authority component placed in shared toolkit |
| 5 | `Navigation/Systems/NavigationIntentBridgeSystem.cs` | +92 | Inert (gated on `_dtCrowd != null`; SimHost has none) | **REVIEW (low)** — crowd-registration retry + GPU diagnostics; behavior-safe but noisy |
| 6 | `Navigation/IDtCrowdProvider.cs` + Engine/Fake impls | +9/+3/+5 | Inert | **KEEP (low-risk)** — additive, backward-compatible overload (`RegisterAgent(..., startPositionFdp)`) |
| 7 | `Hrot.SimHost/.../EcsRecordReplayController.cs` | +15 | Active (record/replay) | **KEEP / promote separately** — unrelated record→replay file-handle release fix (BATCH-16), not navigation |

### Notes on the "REVIEW (high)" items
- **#2 VehicleKinematicsTkbTranslator** is the *same anti-pattern* as the tank bug: a **shared** translator
  now hard-codes a **muscle-specific** decision — "infantry must not carry `VehicleState`" — because the
  Stride crowd bridge uses `!HasComponent<VehicleState>` as its crowd-eligibility guard. In SimHost,
  infantry historically *did* carry `VehicleState` (driven by `CarKinematicsSystem`). If any non-stride
  scenario moves infantry via the kinematic path, this silently changes their behavior on merge.
  → **Verify whether SimHost infantry rely on `VehicleState`** before keeping this. If they do, the gate
  belongs on the Stride side (e.g. strip `VehicleState` only in the Stride muscle), not in the shared TKB.
- **#3/#4** are a coherent, *designed* split-authority refactor (P2-T4 / STR-D12): under Bullet, steering
  output must not be written to `SimVelocity` (which Bullet owns post-step), so it goes to `CrowdMotorIntent`.
  This is sound **for Stride**, but it changes a **shared** system's output contract. Inert in SimHost only
  because SimHost doesn't register `CrowdAgentUpdateSystem`. Decision: either (a) accept it as a shared,
  muscle-agnostic contract (then `main` consumers must be audited), or (b) move the crowd-steering system +
  `CrowdMotorIntent` into `Hrot.Stride.Core` since they exist solely to feed the Bullet character motor.

## Recommended end state (keeps brain == main; muscle owns muscle concerns)
1. **#1 NavigationExecutionSystem** — restored to `main`. ✅ (SimHost tanks arrive again; Stride still reports
   arrival via `VehicleNavigationIntentSystem`.)
2. **Stride residual risk** — confirm the restored shared system does **not** false-frustrate Stride vehicles
   during the body-not-in-sim startup. Empirically checked via `STRIDE_SELFTEST=1` (drive PASS + no
   `FailedBlocked`). If it ever does, fix it on the **Stride side** (skip/defer the frustration guard while
   `rb.Simulation == null`, or ensure `SimVelocity` is reverse-synced from frame 1), **never** by editing
   the shared system.
3. **#2** — decide infantry `VehicleState` ownership (see above). Preferred: move the strip to the Stride muscle.
4. **#3/#4** — decide shared-vs-muscle home for split-authority crowd steering.
5. **#5/#6** — low-risk; keep, but consider trimming the GPU `[BridgeReg]` diagnostics before merge.
6. **#7** — unrelated; can be promoted to `main` on its own merits.

## Guardrail added
A regression test asserts `NavigationExecutionSystem` **processes** an entity carrying `VehicleState`
(so the `.Without<VehicleState>()` exclusion cannot silently return). See the navigation system tests.
