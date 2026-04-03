# Time Control (time-ctrl-3) — Debt Tracker

Generated from reports in .dev/time-ctrl-3/reports (BATCH-01 → BATCH-05).

 - **FdpLog.Debug overloads (positional args limit)**: Status: Open (confirmed).
	 Evidence: `Fdp.Kernel.Logging.FdpLog<T>` implements Debug overloads up to four args only (`FDP/Kernel/Fdp.Kernel/Logging/FdpLog.cs`).
	 Recommendation: add a `params object[]` overload or a small formatting helper to preserve additional fields. See [BATCH-02-REPORT.md](.dev/time-ctrl-3/reports/BATCH-02-REPORT.md).

 - **Test logging isolation (NLog MemoryTarget global config)**: Status: Open (confirmed).
	 Evidence: `MasterSyncController_Step_EmitsDebugLog` configures `NLog.LogManager.Configuration` directly in tests (`FDP/Toolkits/FDP.Toolkit.Time.Tests/MasterSyncControllerTests.cs`).
	 Recommendation: implement `FdpTestLogSink` or use per-test isolated targets to avoid cross-test contamination. See [BATCH-01-REPORT.md](.dev/time-ctrl-3/reports/BATCH-01-REPORT.md).

- **Hard-snap domain mismatch (`_lastUpdateRawTicks` vs SyncedWallTicks)**: Status: Addressed in BATCH-03. Root cause and corrective patch described; regression test added. See [BATCH-03-REPORT.md](.dev/time-ctrl-3/reports/BATCH-03-REPORT.md).

- **`_isTimeSynced` guard dropped mode-switch events**: Status: Addressed in BATCH-05. Guard removal and test updates restored integration behavior. See [BATCH-05-REPORT.md](.dev/time-ctrl-3/reports/BATCH-05-REPORT.md).

- **`UpdateBarrierPending` advanced sim-time while waiting**: Status: Addressed in BATCH-05. `UpdateBarrierPending` now freezes sim-time until barrier crossed. See [BATCH-05-REPORT.md](.dev/time-ctrl-3/reports/BATCH-05-REPORT.md).

 - **Drain tests vacuous (missing SwapBuffers)**: Status: Partially addressed.
	 Evidence: Many multi-controller E2E tests correctly use `SwapBuffers()` (see `UnifiedControllerE2ETests.cs`), but specific unit tests in `FDP/Toolkits/FDP.Toolkit.Time.Tests/SlaveSyncControllerTests.cs` (e.g. `SlaveSyncController_ContinuousMode_DrainsStrayStepIntents`, `SlaveSyncController_BarrierPendingMode_DrainsStrayStepIntents`) still publish `AdvanceFrameIntent` via `PublishManaged` without an explicit `SwapBuffers()` before calling `ctrl.Update()`.
	 Recommendation: update those unit tests to `SwapBuffers()` after publishing managed events to make the drain observable, or use `PublishManagedRaw`/`InjectIntoCurrent` if intended to write directly to the read buffer. See [BATCH-02-REPORT.md](.dev/time-ctrl-3/reports/BATCH-02-REPORT.md).

 - **Hard-snap hardening: sentinel vs explicit flag**: Status: Open (confirmed).
	 Evidence: `DrainTimeSyncResponses()` uses `hardSnap = _masterWallClockOffset == 0 || ...` in `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`.
	 Recommendation: consider adding an explicit `_firstSyncDone` boolean to distinguish "unknown offset" from a valid zero offset value. See [BATCH-02-REPORT.md](.dev/time-ctrl-3/reports/BATCH-02-REPORT.md).

- **Test helper closure capturing `ref` params**: Status: Addressed (BATCH-04). `CreateMasterSlave` helper removed and replaced with inline setup. See [BATCH-04-REPORT.md](.dev/time-ctrl-3/reports/BATCH-04-REPORT.md).

- **ReadOnlySpan → xUnit assertion incompatibility**: Status: Addressed (tests updated to use `.ToArray()` in BATCH-02). See [BATCH-02-REPORT.md](.dev/time-ctrl-3/reports/BATCH-02-REPORT.md).

 - **Log string fragility (use constant for STEP prefix)**: Status: Open.
	 Evidence: Tests assert `memTarget.Logs` contains the literal `"[TC3][Master] STEP"` and controller formats this string inline (no shared constant). See `MasterSyncControllerTests.cs` and `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs`.
	 Recommendation: expose a `public const string StepLogPrefix` used by controller and tests to avoid fragile string-matching. Mentioned in [BATCH-01-REPORT.md](.dev/time-ctrl-3/reports/BATCH-01-REPORT.md).

- **Periodic resync during long runs**: Status: Open. Tests with many frames may trigger `SyncRefreshIntervalTicks` resyncs; recommend test harness ability to freeze/respect resync timers or configure a longer interval in tests. See [BATCH-02-REPORT.md](.dev/time-ctrl-3/reports/BATCH-02-REPORT.md).

- **Double vs float accumulation precision note**: Status: Informational. `TargetSimTime` uses double accumulation while `fixedDelta` is float — intended design (master authoritative). No action required unless precision drift observed. See [BATCH-01-REPORT.md](.dev/time-ctrl-3/reports/BATCH-01-REPORT.md).

---

If you want, I can:
- Open PR(s) that implement the small fixes (add logging overload / test sink / `StepLogPrefix` constant), or
- Update the vacuous drain tests to include `SwapBuffers()` and verify behavior.

(Report sources: [BATCH-01-REPORT.md](.dev/time-ctrl-3/reports/BATCH-01-REPORT.md) → [BATCH-05-REPORT.md](.dev/time-ctrl-3/reports/BATCH-05-REPORT.md)).