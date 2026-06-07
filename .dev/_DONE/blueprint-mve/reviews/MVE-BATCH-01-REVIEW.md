# MVE-BATCH-01 Review — headless "run an Instance Blueprint on an entity"
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
First MVE slice (the RUN stage): a headless proof that an Instance Blueprint is created on an entity and ticked by the **real** `BlueprintTickSystem`/`BlueprintMaintenanceSystem`, with an observable per-frame state change, plus a reusable `BlueprintRunHarness` for the future editor run-button.

## Key finding (substrate)
The **ClusterRunner kernel does NOT schedule the blueprint runtime** — `EditorHarness` (lines 118–221) loads only SimHost/CGF/EQS/editor modules; no `BlueprintTickSystem`/`MaintenanceSystem`, no `BlueprintBlackboard*` components, no `BlueprintRegistry`. So MVE-01 used the proven `BlueprintTestFixture` substrate (which runs the real tick/maintenance systems, not a mock). **Consequence:** the editor "Run Opened Blueprint" button (MVE-06) needs a **`BlueprintModule` wired into the kernel** first (register tier components + tick/maintenance + registry). Documented in the report.

## Verification (ran myself)
- `dotnet build IOS-IG-SimHost.sln` **0 Warnings / 0 Errors**.
- New MVE tests **6/6** (`BlueprintRunMveTests`). Full `Hrot.Blueprints.Tests` **1126 / 10 / 8** (10 = DEBT-006, unchanged). `EditorSubsystemBoot` **10/10**. (No production code changed — only `.dev/` + two test files — so AiShared/BTree/HSM cannot regress.)

## Test quality (real execution)
- `InstanceBlueprint_RunsOnEntity_CounterAdvancesByFrameCount` (1/3/10): asserts counter == frames pumped (sanity 0 before tick).
- `InstanceBlueprint_TwoEntities_AdvanceIndependently`: A attached 5 frames → 5, B last 2 → 2 (per-slot isolation, not a shared static).
- `WorldSingletonBlueprint_LazyInitsAndTicks…` (1/4): no singleton blackboard until first tick; after N → `HasSingleton` true, header `SlotCount==1`, counter == N (verifies the lazy-init + world-singleton path).
- `BlueprintRunHarness.ReadIntField` throws on a missing slot/field, so a silent miss can't masquerade as 0. Reuses the fixture's tiered `TryAttach` path — no parallel tick.

## Notes / next gaps (documented in report)
- Harness production home named: a `BlueprintRunService` in `Hrot.Blueprints.Editor` (takes live `EntityRepository` + `BlueprintRegistry` + frame-pump), for MVE-06.
- MVE-02 (compile-on-demand via QuickReloadService), MVE-03 (save), MVE-04 (hot-reload), MVE-05 (debug-observe), MVE-06 (button + the kernel `BlueprintModule`).

## Verdict
APPROVED. The run slice is proven headlessly and runnable in the dev loop. The kernel-doesn't-load-blueprints finding is the key driver for the next steps.

## Commit Message
```
test(blueprint-mve): headless run-an-Instance-Blueprint-on-an-entity slice (MVE-BATCH-01)

First MVE slice (RUN stage): BlueprintRunMveTests proves an Instance Blueprint is created on an entity
and ticked by the real BlueprintTickSystem/MaintenanceSystem with an observable per-frame counter
(advances by frames pumped; two-entity slot isolation; world-singleton lazy-init+tick). Adds reusable
BlueprintRunHarness (SpawnAndAttach/Pump/ReadIntField) the editor run-button (MVE-06) will reuse.

Substrate: BlueprintTestFixture (runs the real tick/maintenance systems). FINDING: the ClusterRunner
kernel (EditorHarness) does NOT schedule the blueprint runtime — so MVE-06's button needs a
BlueprintModule wired into the kernel first (documented). + .dev/blueprint-mve design/plan.

Build 0/0. New MVE tests 6/6; Blueprints 1126/10 (DEBT-006), EditorSubsystemBoot 10/10. No production
code changed.
```
