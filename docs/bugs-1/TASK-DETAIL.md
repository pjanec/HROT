# BUG1 — Task Detail

**Reference:** See [DESIGN.md](./DESIGN.md) for architectural context and rationale.

---

## Phase 1 — Infrastructure & Configuration

---

### BUG1-F001 Fix SimHost DDS Domain Zero Guard

**Design Reference:** [§1.1 Fix SimHost DDS Domain Zero Guard](./DESIGN.md#11-fix-simhost-dds-domain-zero-guard)

**Scope**

- **In:** Replace the `> 0` guard in `SimHostSubsystem.Initialize()` with a direct pass-through.
- **Out:** Changes to `NodeConfiguration.cs` fallback defaults, `RunnerConfiguration` nullability
  changes, or any subsystem other than SimHost.

**Files**

| File | Change |
|---|---|
| `Bagira.Runner/Services/SimHostSubsystem.cs` | Line ~119: replace `config.DomainId > 0 ? config.DomainId : (int?)null` with `config.DomainId` (or nullable cast if `SimHostApp` constructor requires nullable) |

**Constraints**

- Do not change the `SimHostApp` constructor signature if it would require cascading changes to
  unrelated call sites.
- The fix must preserve existing behaviour when `--node-id` is not supplied (falling back to
  reading `config.json`).

**Success Conditions**

1. **Happy path — domain 0 accepted:**  
   *Setup:* Create a `SubsystemConfig { DomainId = 0 }`. Call `SimHostSubsystem.Initialize(cfg)`
   with a mock or fake `SimHostApp`.  
   *Assert:* `SimHostApp` receives `domainOverride = 0` (not `null`).

2. **Non-zero domain preserved:**  
   *Setup:* `SubsystemConfig { DomainId = 5 }`.  
   *Assert:* `SimHostApp` receives `domainOverride = 5`.

3. **Regression — existing test suite still passes:**  
   All tests in `Bagira.SimHost.Tests` continue to pass without modification.

---

### BUG1-F002 Add `--node-id` CLI Option to Runner

**Design Reference:** [§1.2 Add `--node-id` CLI Option to Runner](./DESIGN.md#12-add---node-id-cli-option-to-runner)

**Scope**

- **In:** Add `NodeId` int property through the full chain: `RunnerConfiguration` → `RunnerOptions`
  → `SubsystemConfig` → `SubsystemOrchestrator` → each subsystem initialiser → `SimHostApp` /
  `IgApplication` network init.
- **Out:** Support for `-m ig,ig` multi-instance within a single process. Changing DDS
  `DomainParticipant` identity or `NodeIdMapper` internals beyond passing the injected value.

**Files**

| File | Change |
|---|---|
| `FDP/Framework/FDP.Framework.Runner/RunnerConfiguration.cs` | Add `[Option('n', "node-id", Default = 0)] public int NodeId` |
| `FDP/Framework/FDP.Framework.Runner/RunnerOptions.cs` | Add `public int NodeId { get; set; }` |
| `FDP/Framework/FDP.Framework.Runner/SubsystemConfig.cs` | Add `public int NodeId { get; set; }` |
| `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs` | Store `_nodeId` from options; resolve per-subsystem ID in `Initialize()` with legacy fallback |
| `Bagira.Runner/Program.cs` | Map `config.NodeId` into `RunnerOptions.NodeId` |
| `Bagira.Runner/Services/SimHostSubsystem.cs` | Pass `config.NodeId` into `SimHostApp` instead of static constant |
| `Bagira.Runner/Services/IgSubsystem.cs` | Pass `config.NodeId` into `IgApplication` instead of static constant |

**Deterministic offset table (when base `--node-id` != 0):**

```
SimHost  → base + 0
IG       → base + 100
IOS      → base + 200
_        → base + 300
```

**When `--node-id` = 0 (default):** fall back to legacy constants
(`SimHostNetworkConstants.LocalNodeId`, `IgNetworkConstants.InstanceId`) to preserve backwards
compatibility.

**Constraints**

- The `NodeIdMapper` and `DdsIdAllocator` must receive the resolved dynamic ID during subsystem
  `Initialize`, not a static constant.
- Do not break existing headless integration tests that currently rely on single-instance defaults.

**Success Conditions**

1. **Legacy default — no flag supplied:**  
   *Setup:* Parse `args = ["-m", "simhost"]` (no `--node-id`).  
   *Assert:* `SubsystemConfig.NodeId == 0`; SimHost falls back to `SimHostNetworkConstants.LocalNodeId`.

2. **Explicit node-id flows through to SubsystemConfig:**  
   *Setup:* Parse `args = ["-m", "simhost", "--node-id", "10"]`.  
   *Assert:* `SubsystemConfig.NodeId == 10` for the SimHost subsystem; resolved ID passed to
   `SimHostApp` = `10 + 0 = 10`.

3. **Offset applied per subsystem type:**  
   *Setup:* `RunnerOptions { NodeId = 5 }` with IG subsystem.  
   *Assert:* `SubsystemConfig.NodeId` passed to `IgSubsystem.Initialize` = `5 + 100 = 105`.

4. **Short alias `-n` accepted:**  
   *Setup:* Parse `args = ["-m", "ig", "-n", "42"]`.  
   *Assert:* `config.NodeId == 42`.

---

### BUG1-F003 Fix Batch Script Working Directory

**Design Reference:** [§1.3 Fix Batch Script Working Directory](./DESIGN.md#13-fix-batch-script-working-directory)

**Scope**

- **In:** `run_all_standalone.bat`, `run_SimHost.bat`, `run_IG.bat`, `run_IOS.bat`.
- **Out:** Build scripts, `run_all_together.bat` (which runs a single process and is unaffected by
  the CWD issue), CI pipeline scripts.

**Constraints**

- Use `%~dp0` to compute the script's own directory so the fix works regardless of where the
  console is opened.
- Preserve the commented-out `--wait-for` variants already in the scripts.
- The `RUNNER` variable must point to just the executable name (not a full path) after the `cd`.

**Success Conditions**

1. **`cd` is present:**  
   *Inspect:* `run_all_standalone.bat` contains a `cd /d` command targeting the net8.0 output
   directory before any `start` commands.

2. **Assets are found at runtime (manual test):**  
   Running `run_all_standalone.bat` from the solution root results in the road network being visible
   in the SimHost window and all three processes joining DDS Domain 0.

3. **`-d %DOMAIN%` flag is passed explicitly:**  
   *Inspect:* Each `start` invocation includes `-d %DOMAIN%` so the domain is set via CLI (not
   relying on `config.json` at all, per the defence-in-depth principle).

---

## Phase 2 — Network Correctness

---

### BUG1-N001 Enforce Silent Bystander Rule in `UpdateEntityDescriptorRequestSystem`

**Design Reference:** [§2.1 Enforce Silent Bystander Rule](./DESIGN.md#21-enforce-silent-bystander-rule-in-updateentitydescriptorrequestsystem)

**Scope**

- **In:** Remove the four anti-pattern `WriteAck` calls that emit failure codes on the non-
  authoritative path. Add `Debug`-level log statements for the silent discards.
- **Out:** Changes to the `Success` ACK paths, the `WriteAck` helper itself, or the
  `UpdateEntityAttributeRequestSystem` (already correct).

**Files**

| File | Location | Change |
|---|---|---|
| `Bagira.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` | `ProcessRequest` — entity not found | Remove `WriteAck(EntityNotFound)`, add debug log, return |
| `Bagira.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` | `ProcessRequest` — unsupported type | Remove `WriteAck(NotSupported)`, add debug log, return |
| `Bagira.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` | `ProcessGeoSpatialUpdate` — not authoritative | Remove `WriteAck(NotOwner)`, add debug log, return |
| `Bagira.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` | `ProcessMapVisualOverlayUpdate` — not authoritative | Remove `WriteAck(NotOwner)`, add debug log, return |

**Constraints**

- `WriteAck(Success)` paths must remain untouched.
- Log messages must include the `EntityId` and the reason for the silent discard.

**Success Conditions**

1. **Non-authoritative node emits no ACK:**  
   *Setup:* Create a fake `ISimulationView` where `HasAuthority` returns `false` for any entity.
   Inject it into `UpdateEntityDescriptorRequestSystem`. Publish a `UpdateEntityDescriptorRequest`
   for a GeoSpatial update with a valid EntityId.  
   *Assert:* No `UpdateEntityDescriptorAck` is written to the DDS writer mock.

2. **Entity not found — silent discard:**  
   *Setup:* `_entityMap.TryGetEntity` returns `false`.  
   *Assert:* No ACK written. A `Debug`-level log line is emitted containing the entity ID.

3. **Unsupported descriptor type — silent discard:**  
   *Setup:* Request with `DescriptorType` set to an enum value not handled by the switch.  
   *Assert:* No ACK written. Debug log emitted.

4. **Authoritative node emits Success ACK:**  
   *Setup:* `HasAuthority` returns `true`. Valid GeoSpatial update request.  
   *Assert:* Exactly one `UpdateEntityDescriptorAck` with `ErrorCode = Success` is written.

5. **Existing tests pass:**  
   All tests in `Bagira.Map.Common.Tests` (if present) and `Bagira.IG.Tests` continue to pass.

---

### BUG1-N002 Fan-Out Entity Descriptor Disposal

**Design Reference:** [§2.2 Fan-Out Entity Descriptor Disposal](./DESIGN.md#22-fan-out-entity-descriptor-disposal-in-cyclenetworkdiscardcleanuplsystem)

**Scope**

- **In:** Refactor `CycloneNetworkCleanupSystem` to accept `IEnumerable<IDescriptorTranslator>`.
  Update `NetworkCleanupModule`. Update the SimHost bootstrap registration to pass the full
  translator collection.
- **Out:** Changes to any descriptor translator implementation; changes to `EntityMaster` lifecycle
  logic; changes to how the BDC SST protocol dictates existence.

**Files**

| File | Change |
|---|---|
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs` | Replace single `_translator` with `_translators` array; fan-out loop with per-translator try/catch |
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/NetworkCleanupModule.cs` (if it exists) | Update constructor to accept `IEnumerable<IDescriptorTranslator>` |
| SimHost bootstrap / `SimHostApp.cs` | Pass all active egress translators to the cleanup module |

**Constraints**

- Each translator's `Dispose` call must be wrapped in a `try/catch`; failure of one must not
  prevent the remaining translators from running.
- The error log must include the translator type name and the network entity ID.
- All existing translator implementations already implement `IDescriptorTranslator.Dispose(long)`.
  Do not modify them.

**Success Conditions**

1. **All translators receive Dispose on entity death:**  
   *Setup:* Inject three mock `IDescriptorTranslator` instances. Create an ECS entity with
   `NetworkIdentity` + `NetworkOwnership(HasAuthority=true)`. Tick the system. Then mark the entity
   as dead (remove from view). Tick again.  
   *Assert:* All three mock translators each had `Dispose(netId)` called exactly once.

2. **One translator throwing does not block others:**  
   *Setup:* Translator 1 throws on `Dispose`. Translators 2 and 3 are mock-tracked.  
   *Assert:* Translators 2 and 3 still have `Dispose` called. An `Error`-level log is emitted.

3. **Non-authoritative entities are not disposed:**  
   *Setup:* Entity has `NetworkOwnership(HasAuthority=false)`.  
   *Assert:* No translator's `Dispose` is called.

4. **Existing SimHost integration tests pass:**  
   `Bagira.SimHost.Integration.Tests` suite passes without modification.

---

## Phase 3 — IG Continuous Drag Mode

---

### BUG1-I001 Add Continuous Drag Update Toggle to IG

**Design Reference:** [§3.1 Add Continuous Drag Update Toggle](./DESIGN.md#31-add-continuous-drag-update-toggle)

**Scope**

- **In:**  
  - `MapUserConfig.ContinuousDragUpdates` bool property.  
  - `IgApplication.SendGeoSpatialUpdate(Entity entity, Vector2 worldPos)` private helper method.  
  - `IgApplication._continuousDragTimer` float field.  
  - Updated `OnEntityMoved` subscription (entity param used, throttle logic).  
  - Simplified `OnEntityDragEnded` delegating to helper.  
- **Out:** Changes to the ghost-preview rendering pipeline, DDS QoS settings, or any non-drag
  path (creation tool, teleport, etc.).

**Files**

| File | Change |
|---|---|
| `Bagira.IG/Systems/MapUserConfig.cs` | Add `public bool ContinuousDragUpdates { get; set; }` |
| `Bagira.IG/IgApplication.cs` | Add field `_continuousDragTimer`; add `SendGeoSpatialUpdate` helper; update `OnEntityMoved`/`OnEntityDragEnded` subscriptions |

**Constraints**

- The `SendGeoSpatialUpdate` helper must guard for `!_networkEnabled || _commandGateway == null ||
  _geoTransform == null` and bail silently — identical to the existing guards in `OnEntityDragEnded`.
- The throttle interval is **0.1 s** (10 Hz). This value may be a private constant rather than
  configurable.
- When `ContinuousDragUpdates == false` (default), the `OnEntityMoved` callback must behave
  **identically to today** — no network calls, only `_lastDragWorldPos` tracking.
- `_continuousDragTimer` must be reset in `OnEntityDragEnded` regardless of whether continuous mode
  is enabled, to prevent a stale timer from firing at the start of the next drag.
- The existing test hook methods (`SimulateDropAt`, `SimulateDragAndDrop`) must not require
  modification — they go through `OnEntityDragEnded` which continues to call `SendGeoSpatialUpdate`.

**Success Conditions**

1. **Continuous mode off — no network call during drag:**  
   *Setup:* `_userConfig.ContinuousDragUpdates = false`. Mock `_commandGateway`. Fire
   `OnEntityMoved` 60 times (simulating 60 frames).  
   *Assert:* `_commandGateway.SendUpdateDescriptor` call count = 0.

2. **Continuous mode on — throttled to ~10 Hz:**  
   *Setup:* `ContinuousDragUpdates = true`. Simulate 30 `OnEntityMoved` calls each with
   `GetFrameTime()` returning `0.016667f` (60 fps) → total elapsed = 0.5 s.  
   *Assert:* `SendUpdateDescriptor` called exactly 5 times (≈ 10 Hz × 0.5 s).

3. **Drop always sends exactly one update:**  
   *Setup:* `ContinuousDragUpdates = true` or `false`. Call `OnEntityDragEnded`.  
   *Assert:* `SendUpdateDescriptor` called exactly once (for the final drop position).

4. **Timer reset on drag end:**  
   *Setup:* Accumulate `_continuousDragTimer` to 0.09f (just below threshold). Call
   `OnEntityDragEnded`.  
   *Assert:* After `OnEntityDragEnded`, `_continuousDragTimer == 0f`.

5. **Existing drag-related IG tests pass:**  
   Tests in `Bagira.IG.Tests` that exercise `SimulateDropAt` or `SimulateDragAndDrop` pass without
   modification.

---

## Phase 4 — Mission System Fixes

---

### BUG1-M001 Default `DoctrineFinished` Trigger on Task Creation

**Design Reference:** [§4.1 Default `DoctrineFinished` Trigger on Task Creation](./DESIGN.md#41-default-doctrinefinished-trigger-on-task-creation)

**Scope**

- **In:** Change `HandleAddTask()` in `MissionPanel` to initialise `Triggers` with a single
  `MissionTrigger { Type = "DoctrineFinished" }` instead of an empty list.
- **Out:** `HandleEditBehaviorId` (no workaround to inject per-behavior triggers needed).
  SimHost-side `MissionControlRequestSystem`, `MissionDirectorSystem`. DDS data model.

**Files**

| File | Change |
|---|---|
| `Bagira.IOS/Panels/MissionPanel.cs` | `HandleAddTask()`: `Triggers = new List<MissionTrigger>()` → `Triggers = new List<MissionTrigger> { new MissionTrigger { Type = "DoctrineFinished" } }` |

**Constraints**

- Do not modify `HandleEditBehaviorId` to inject spatial triggers — the design talk explicitly
  rejects this approach in favour of `DoctrineFinished`.
- The `Type` string must exactly match what `MissionControlRequestSystem` parses — verify it is
  the string `"DoctrineFinished"` via grep of `MissionControlRequestSystem`.

**Success Conditions**

1. **New task has DoctrineFinished trigger:**  
   *Setup:* Construct a `MissionPanel`. Call `HandleAddTask()`.  
   *Assert:* `GetDraftTasks()[0].Triggers` has exactly one entry with `Type == "DoctrineFinished"`.

2. **Multiple tasks each have the trigger:**  
   *Setup:* Call `HandleAddTask()` twice.  
   *Assert:* Both tasks have `Triggers.Count == 1` and `Triggers[0].Type == "DoctrineFinished"`.

3. **Existing MissionPanel unit tests pass:**  
   All tests in `Bagira.IOS.Tests` that exercise `MissionPanel` pass without modification (update
   those that assert `Triggers` is empty — they should now assert `DoctrineFinished`).

---

### BUG1-M002 Track Control Commands for OCC Version Sync

**Design Reference:** [§4.2 Track Control Commands for OCC Version Sync](./DESIGN.md#42-track-control-commands-for-occ-version-sync)

**Scope**

- **In:**  
  - Add `Task<MissionCommitResult> SendControlCommandAsync(long entityId, eMissionCommandType type, Guid taskId)` to `IMissionEditorService`.  
  - Implement it in `MissionEditorService` using the existing `_pendingCommits` TCS dictionary.  
  - Update `HandleAbort` and `HandleJump` in `MissionPanel` to use the async method and set
    `_pendingCommit` + `_commitInFlight`.  
  - Remove the old synchronous `SendControlCommand` from the interface (or mark it obsolete) and
    remove its standalone callers.  
- **Out:** `CommitMissionAsync` logic, `PollCommitCompletion` logic, the ACK ingestion path
  (`OnMissionControlAckReceived`), timeout handling. No SimHost changes.

**Files**

| File | Change |
|---|---|
| `Bagira.IOS/Services/IMissionEditorService.cs` | Add `Task<MissionCommitResult> SendControlCommandAsync(...)` |
| `Bagira.IOS/Services/MissionEditorService.cs` | Implement `SendControlCommandAsync` mirroring `CommitMissionAsync` (TCS in `_pendingCommits`, `BaseVersion = 0`) |
| `Bagira.IOS/Panels/MissionPanel.cs` | `HandleAbort`: replace fire-and-forget with `_pendingCommit = ... .SendControlCommandAsync(...)` + `_commitInFlight = true`. Same for `HandleJump`. |

**Constraints**

- `BaseVersion` in the control command request must be `0`. Control commands deliberately bypass
  OCC; the version check is server-side for the `CMD_COMMIT` path only.
- The timeout applied to `SendControlCommandAsync` must reuse the same `_commitTimeoutMs`
  constant already used by `CommitMissionAsync`.
- Removing `SendControlCommand` from the interface is permitted if there are no other callers.
  Verify with a project-wide reference search before removing.
- `PollCommitCompletion` already handles `result.NewVersion` and updates `_draftBaseVersion` —
  do not duplicate that logic.

**Success Conditions**

1. **Abort updates `_draftBaseVersion`:**  
   *Setup:* Construct `MissionPanel` with `_draftBaseVersion = 1`. Create a mock
   `IMissionEditorService.SendControlCommandAsync` that returns
   `MissionCommitResult { Success = true, NewVersion = 2 }`.  
   Call `HandleAbort(logic)`.  
   Tick `PollCommitCompletion()` until the task completes.  
   *Assert:* `MissionPanel._draftBaseVersion == 2`.

2. **Abort locks UI during in-flight:**  
   *Setup:* `SendControlCommandAsync` never completes (pending TCS).  
   Call `HandleAbort`. Inspect `CommitInFlight`.  
   *Assert:* `CommitInFlight == true`. Calling `HandleAbort` again has no effect.

3. **Jump updates `_draftBaseVersion`:**  
   *Same pattern as success condition 1 but calling `HandleJump`.*

4. **Subsequent Commit proceeds without version conflict:**  
   *Integration-level test:*  
   *Setup:* Working SimHost (in-process headless). IOS sends two tasks. SimHost version = 1.  
   *Action:* IOS sends ABORT. Polls until `_draftBaseVersion == 2`. Then sends COMMIT with two
   tasks.  
   *Assert:* `MissionControlAck.ErrorCode == 0` (Success). No `ERR_VERSION_CONFLICT`.

5. **Existing IOS tests pass:**  
   All tests in `Bagira.IOS.Tests` that exercise `HandleAbort`, `HandleJump`, or
   `SendControlCommand` pass or are updated to use the async signature.
