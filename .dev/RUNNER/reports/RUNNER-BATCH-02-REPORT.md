# RUNNER-BATCH-02 Report

**Batch:** RUNNER-BATCH-02  
**Date:** 2026-03-07  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| R1.1 | ✅ Complete | `Hrot.ClusterRunner` console project + `Hrot.ClusterRunner.Tests` xUnit project created, added to solution |
| R1.2 | ✅ Complete | `RunnerConfiguration` + `RunMode` [Flags] enum with CLI parsing + JSON config merge |
| R1.3 | ✅ Complete | `SubsystemOrchestrator` with Raylib window ownership, frame loop, subsystem lifecycle |
| R1.4 | ✅ Complete | `ISubsystem` interface with `DrawWorld()`/`DrawUI()` phases + 3 stub subsystems |
| R1.5 | ✅ Complete | `SubsystemStatusAnnounce` DDS topic in `Hrot.NED/Runner/` |
| R1.6 | ✅ Complete | `WaitingRoomCoordinator` DDS-based startup synchronisation with timeout |

---

## 🧪 Testing Results

**Unit Tests Passed:** 39 / 39 (`Hrot.ClusterRunner.Tests`)  
**Integration Tests Passed:** 4 / 4 (`Hrot.NED.Tests` — 2 new + 2 existing)

**Regression check — zero failures:**
| Project | Result |
|---------|--------|
| `Hrot.IG.Tests` | 229 / 229 ✅ |
| `Hrot.ExCon.Tests` | 252 / 252 ✅ |
| `Hrot.SimHost.Tests` | 55 / 55 ✅ |
| `Hrot.NED.Tests` | 4 / 4 ✅ |
| `Hrot.ClusterRunner.Tests` | 39 / 39 ✅ |

**Key Test Scenarios Verified:**
- [x] All 4 named modes (`all`, `simhost`, `ig`, `ios`) and comma-separated combos parse correctly
- [x] `--wait-for` required in separate mode without `--no-wait`; invalid peer names rejected
- [x] JSON config file values override CLI defaults; unset JSON fields leave CLI values intact
- [x] `SubsystemOrchestrator.Initialize()` passes correct `SubsystemConfig` to every subsystem
- [x] `RunFrames(N)` calls `Update()` exactly N times; headless mode skips `DrawWorld`/`DrawUI`
- [x] `Run()` pre-stopped via `Stop()` exits immediately (no infinite loop)
- [x] Shutdown invokes subsystems in reverse initialisation order
- [x] `WaitingRoomCoordinator` discovers all required peers and publishes `Ready=true`
- [x] Timeout throws `TimeoutException` naming the missing peers
- [x] Self-announcement (same `NodeId`) is ignored
- [x] TransientLocal late-joiner receives cached `Ready=true` sample
- [x] Empty required-peers set returns immediately without DDS I/O
- [x] `SubsystemStatusAnnounce` round-trip pub/sub (domain 120)
- [x] `SubsystemStatusAnnounce` TransientLocal late-joiner (domain 121)

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

**Issue 1 — `[DdsManaged]` missing on `SubsystemStatusAnnounce`.**  
Build error: `Type 'SubsystemStatusAnnounce' has field 'SubsystemName' of managed type 'string' but is not marked with [DdsManaged]`.  
CycloneDDS schema validation rejects `string` fields in partial structs unless the struct carries `[DdsManaged]`. Fixed by adding `[DdsManaged]` to the struct — same pattern as `NetworkDemo/SquadChat.cs`.

**Issue 2 — Orchestrator infinite loop in headless unit tests.**  
`Run()` was setting `_running = true` at its start, overwriting any `Stop()` call made before `Run()`. In headless mode there is no `Raylib.WindowShouldClose()` fallback, so the loop never exited. Fixed by initialising `private volatile bool _running = true;` at the field declaration and removing the assignment from `Run()`. Added `internal void RunFrames(int frames)` as a test-only entry point that runs N update iterations without touching Raylib.

**Issue 3 — MSTest parallelisation caused DDS test domain interference.**  
MSTest parallelises at method level (up to 24 workers). Both new `SubsystemStatusAnnounceTests` were using domain 12. The `TransientLocal` cache from test 2 (NodeId=200) appeared in test 1's reader, causing an assertion failure on the wrong sample. Fixed by giving each test method its own domain constant (`DomainRoundTrip=120`, `DomainLateJoiner=121`) and searching the loan by `NodeId` rather than assuming index 0.

---

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `DdsParticipant.DomainId` returns `uint` but every model/config in this codebase uses `int` for domain IDs — a silent narrowing cast is required every time they interact. A `DomainIdInt` convenience property or a consistent `uint` type throughout the data model would eliminate this friction.
- `SubsystemOrchestrator` acquires and releases Raylib/ImGui resources but does not implement `IDisposable`. Callers relying on `using` blocks or DI containers (future work) would not get deterministic cleanup.
- The three stub subsystems (`SimHostSubsystem`, `IgSubsystem`, `IosSubsystem`) are `internal sealed` no-ops. They compile but contain no validation that the real implementations will receive the correct `SubsystemConfig` fields (e.g. `OwnWindow = false`). An abstract base class or explicit compile-time contract would catch mismatches earlier.

---

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- **`_running = true` in field initialisation.** The instructions required that `Stop()` before `Run()` prevents the loop from executing. Moving the flag initialisation to the field declaration (instead of inside `Run()`) is the minimal fix that satisfies both the production path and the test path. An alternative — a `CancellationTokenSource` — would be cleaner for async-future work but was out of scope here.
- **`RunFrames(int frames)` internal test helper.** The instructions said tests must not depend on Raylib. Rather than mocking the entire render pipeline, `RunFrames` simply calls `Update()` and (if not headless) the draw methods — none of which are guarded by Raylib calls in the test-injected path. This keeps the real `Run()` path unchanged.
- **Two-constructor pattern on `SubsystemOrchestrator`.** One constructor accepts `RunnerConfiguration` and builds the real subsystem list; the other accepts `IEnumerable<ISubsystem>` for test injection. An alternative would be a factory delegate or DI container, but the two-constructor pattern matched the existing style in `IOS` and `IG`.
- **`StringComparer.OrdinalIgnoreCase` for `WaitForPeers`.** Peer names arrive from both CLI (`--wait-for ig`) and DDS announcements (`SubsystemName = "IG"`). Case-insensitive matching prevents trivial mismatches without requiring callers to normalise casing. Alternative was to normalise to lower-case at parse time, but that would obscure the original announcement value stored in `SubsystemPeerInfo`.

---

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **Multiple Runner instances on the same DDS domain.** Each Runner writes its own `SubsystemStatusAnnounce` using a `NodeId` derived from `Environment.TickCount`. If two Runner processes start within the same tick resolution (~15 ms on Windows), they get the same `NodeId` and one process's writer overwrites the other's DDS instance — the waiting room would see only one peer. The current implementation documents this via the `NodeId` field comment; a stronger fix (GUID-based NodeId) is deferred.
- **`--wait-for` with misspelled peer names.** If a user types `--wait-for simhots`, validation correctly rejects it. But the error message lists valid names (`simhost, ig, ios`) without indicating which token was wrong. A "did you mean?" hint would improve UX — noted but left for a later polish pass.
- **Headless mode + `WaitingRoomCoordinator` timeout.** In headless CI environments, DDS multicast may not be available. The `TimeoutException` message includes all missing peer names so the CI log is actionable, but there is no `--no-wait` escape hatch on the coordinator level (only on the CLI). If needed, the caller can catch `TimeoutException` and re-throw or degrade gracefully.

---

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- **`WaitingRoomCoordinator` uses `Thread.Sleep(100)` polling.** This occupies a thread for the entire wait duration (up to 30 seconds). A DDS listener callback with a `ManualResetEventSlim` would react instantly and free the thread while waiting. For the current use-case (one-time startup) the polling approach is acceptable, but it would be the first thing to change if the coordinator were ever reused at runtime.
- **`SubsystemOrchestrator.Run()` runs at a fixed `SetTargetFPS(60)` unconditionally.** In headless mode no rendering occurs, but the Raylib target-FPS cap still applies (it is guarded by `if (!_headless)` for the init call, so in practice headless uses a busy loop). Making the update rate configurable (or using `Task.Delay` in headless mode) would reduce CPU burn during integration tests.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] **R2.x** — Wire `SimHostSubsystem.Initialize()` to real `EntityRepository` + `ModuleHostKernel` (next batch scope)
- [ ] **R2.x** — Wire `IgSubsystem.Initialize()` to real `IgApplication`
- [ ] **R2.x** — Wire `IosSubsystem.Initialize()` to real `IosLogic`
- [ ] Consider upgrading `WaitingRoomCoordinator` to event-driven DDS listener when startup performance matters
- [ ] Consider `IDisposable` on `SubsystemOrchestrator` to ensure Raylib/ImGui cleanup in all exit paths
