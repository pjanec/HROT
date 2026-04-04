# BATCH-10 Review

**Batch:** BATCH-10  
**Tasks:** PACK2-C002, PACK2-C003, PACK2-R004  
**Verdict:** ✅ APPROVED — no corrective tasks required

---

## Score: 9 / 10

All three tasks are complete, all tests pass, implementation is correct.

---

## Task-by-Task Assessment

### PACK2-C002 — Feature Switch: Eject Local Logic Packs ✅

**Quality: Excellent**

- `SimHostMode.cs` is clean, well-documented.
- `EditorApplication.SwitchToExternalAsync()` correctly guards on `_kernel == null` and `_currentMode == External` before calling `kernel.UninstallModulesAsync(_logicPacks)`.
- Optional `_translatorPacks` install is correctly gated in the same method — smart forward-compat for C003.
- Constructor backward-compat maintained (all optional params, 3-arg callers unaffected).

**Minor note (P3):** `Program.cs` has a duplicate `using Fdp.Kernel;` (line 1 and line 7). Harmless but should be cleaned up.

### PACK2-C003 — Feature Switch: Snap-In ACL Translator Packs + Toggle UI ✅

**Quality: Excellent**

- `SwitchToInternalAsync()` correctly checks `_currentMode == Internal` guard, uninstalls translator packs if present, reinstalls logic packs.
- `EditorToolbarPanel.HandleToggleModeClick` is properly fire-and-forget (`_ = logic.SwitchToExternalAsync()`). Correct for a UI handler.
- Toggle button label "Go External" / "Go Internal" is user-readable.

### PACK2-R004 — OfflineEditorIntegrationTests (IT-1) ✅

**Quality: Excellent**

- `EditorHarness` correctly extended with `EntityLifecycleModule`, `NetworkSpawningSystem`, `SimHostModule`, `SequentialIdAllocator` stub, and `EntityMap` + `Editor` properties.
- Critical deviation found and fixed by developer: **`HrotSharedComponentRegistry.RegisterAll(Repo)` must be called before module registration** to pre-register `NetworkIdentity`, `NetworkOwnership` etc. (not mentioned in batch instructions — good catch!). This pattern matches all other Hrot subsystems.
- `ReliableInitType` namespace correction (actual: `ModuleHost.Core.Network.Interfaces`, not `FDP.Toolkit.Replication.Components` as instructions stated).
- All 3 `OfflineEditorIntegrationTests` use `PumpUntil` correctly (no hardcoded frame counts, 5 s timeout).
- `RecordingDdsWriter` correctly defined as a private nested class inside the test file.

---

## Deviations from Instructions

| Deviation | Verdict |
|-----------|---------|
| `HrotSharedComponentRegistry.RegisterAll(Repo)` added to EditorHarness ctor (not specified) | ✅ Correct — necessary, good discovery |
| `ReliableInitType` using `ModuleHost.Core.Network.Interfaces` (instructions had wrong ns) | ✅ Correct fix |
| `OrchestrationLogicPack` excluded from `_logicPacks` | ✅ Correct — per architecture intent |

---

## Test Results

| Suite | Before | After | Δ |
|-------|--------|-------|---|
| `Hrot.Editor.Tests` | 17 | 20 | +3 |
| `Hrot.ClusterRunner.Integration.Tests` (offline) | 5 | 8 | +3 new |

---

## Issues for DEBT-TRACKER

| ID | Priority | Description | Target |
|----|----------|-------------|--------|
| DEBT-10-01 | P3 | Duplicate `using Fdp.Kernel;` in `Hrot.Editor/Program.cs` (line 1 + line 7) | BATCH-11 cleanup |
| DEBT-10-02 | P2 | `HrotSharedComponentRegistry.RegisterAll` call in `EditorHarness` is undocumented in the test setup. Future contributors may not know to call it. Add an inline comment explaining why it's required. | BATCH-11 |
| DEBT-10-03 | P2 | `ReliableInitType` is in `ModuleHost.Core.Network.Interfaces`, not `FDP.Toolkit.Replication.Components` — the batch instructions had the wrong namespace. Update `BATCH-10-INSTRUCTIONS.md` note for future reference. | housekeeping |

---

## Next Steps

**BATCH-11:** PACK2-R005 + PACK2-R006

- R005: `EditorFileIOIntegrationTests` (4 tests) + `FeatureSwitchRcuIntegrationTests` (4 tests, uses `EditorHarness.Editor.SwitchToExternalAsync`)
- R006: `DistributedBrainMuscleIntegrationTests` (3 tests, CGF + SimHost shared DDS loopback domain)
