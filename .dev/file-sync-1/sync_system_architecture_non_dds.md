# Distributed File Deployment & Synchronization System
## Architecture Design Document

*For a distributed game/simulation engine*
*C# / .NET 8 · ASP.NET Core · SignalR · NAS-authoritative · May 2026*

---

## 1. Purpose and Scope

This document defines the architecture of a content deployment and file synchronization system for a distributed game/simulation engine. The system manages 10–100+ Windows nodes across multiple subnets and coordinates file distribution from a central NAS master. The control plane is built on ASP.NET Core and SignalR (.NET 8). The sync system is fully decoupled from the simulation engine's DDS infrastructure — it shares no middleware with it and integrates only via HTTP.

The document reflects a design arrived at through detailed requirements analysis. It supersedes generic option surveys and focuses exclusively on decisions relevant to this system's actual constraints.

---

## 2. Core Design Principles

The following principles were agreed during design and must be preserved through implementation:

- The transfer method is never the centre of the design. Robocopy, direct copy, packed blocks, and relay are implementation strategies behind a single higher-level model.
- The central abstraction is: **ContentPackage version X should be active on node set Y under policy Z.**
- Transfer and activation are always separate pipeline stages: `Stage → Verify → Activate`.
- The NAS is the single source of truth. No version is valid until explicitly published via a publish gate.
- The control plane (SignalR / HTTP) carries only commands, status, and events. File bytes never travel over it.
- Group membership is application-layer context consumed by the sync orchestrator, not owned by it.
- Fleet sync and group-session sync are mutually exclusive operational modes, enforced by a hard gate.
- The master agent is the sole HTTP surface. No HTTP server runs on node agents. All status and control flows through the master.
- All desired work is represented as persisted Intents. Intents survive restarts, are visible via API, and are cancellable by the operator.

---

## 3. Operational Modes

### 3.1 Session Window

Groups are active. Nodes belong to at most one group at a time. Only group-scoped and capability-scoped sync runs. Fleet sync is blocked.

- Triggered by: group formation, node joining a group, operator-triggered patch.
- Characteristics: fast, targeted, must complete before session start.
- Blocking sync (config, binaries) must finish before the group is allowed to start.

### 3.2 Fleet Sync Window

No groups active. No new groups may be created until fleet sync completes. The sync orchestrator has exclusive access to all nodes.

- Triggered by: scheduled overnight job, operator command.
- Characteristics: background, parallel across nodes, no session coordination needed.
- The `FleetSyncMode` gate is set via a REST call to the master agent by the session manager or operator.

> **Note:** No collision handling between modes is required. The gate is enforced at the application layer above the sync orchestrator.

---

## 4. Terminology

| Term | Definition |
|---|---|
| **ContentPackage** | A versioned, immutable set of files on the NAS (e.g. `Config-ScenarioA-v3`). Has a manifest. Replaces "Sync Group" for the data dimension. |
| **DeploymentScope** | The dynamic targeting policy: which nodes should have a given ContentPackage. Replaces "Sync Group" for the targeting dimension. |
| **SyncNode** | A single computer in the fleet. Has a fixed `roomId`, `capabilities[]`, and a nullable `currentGroupId`. |
| **GroupId** | Application-layer session group identifier. Consumed by the sync orchestrator; not owned by it. |
| **DesiredState** | The orchestrator's computed view of what version each node should have active, given current memberships and policies. |
| **Publish Gate** | A `published.json` sentinel file written last on the NAS. No version is valid until the gate is written. |
| **FleetSyncMode** | Boolean flag set on the master agent via REST. When `true`, fleet-scoped jobs are eligible; when `false`, they queue. |
| **Safe Window** | A signal from the consuming app that it is not using a given ContentPackage, permitting mid-session activation. |
| **Intent** | A persisted, operator-visible unit of desired work (e.g. "node X should upload recording Y", "node X should have package Z active"). Has a lifecycle: Pending → Executing → Complete / Stale / Failed / Cancelled. Survives master and agent restarts. Cancellable via REST API. |
| **Stale Intent** | An intent whose `createdAt` age exceeds the configured staleness threshold. Flagged as a warning in the operator message queue; not auto-cancelled. Execution still proceeds when the opportunity arises unless explicitly cancelled. |

---

## 5. Data Categories

Five data categories exist, each with distinct transfer, activation, and targeting semantics:

| Category | Direction | Size profile | Transfer method | Activation | Scope |
|---|---|---|---|---|---|
| Runtime assets (textures, meshes, terrain) | NAS → nodes | Many, small | Packed blocks | Atomic directory swap + cooperative hot-swap | Capability |
| Large recordings / logs | Nodes → NAS | Few, huge | Chunked direct copy, resumable | In-place on NAS | Fleet (upload) |
| Config / scenario definitions | NAS → nodes | Small, critical | Direct file-by-file copy | Staged, blocking | Group-scoped |
| Small many-file datasets (AI tables, lookup data) | NAS → nodes | Many, small | Packed blocks | Staged swap | Capability / Fleet |
| Executable / binary updates | NAS → nodes | Medium | Direct copy to staging | Next-launch via bootstrapper | Group-scoped / Fleet |

> **Note:** Recordings (nodes → NAS) are an upload/collection flow, architecturally distinct from distribution. They are designed as a first-class separate workstream and do not share staging or activation machinery with downward sync.

> **Note:** Executable updates cannot be hot-swapped on Windows. A minimal bootstrapper process handles activation on next launch. The agent itself must be updated via a separate updater not part of the agent being replaced.

---

## 6. System Architecture

### 6.1 Layers

```
NAS
  Immutable published versions, manifests, packed blocks
  Publish gate (published.json written last)

Master Agent  (ASP.NET Core + SignalR, one designated machine)
  SignalR Hub /hubs/sync        ← all node agents connect here
  REST API /api/...             ← group orchestrators, session manager,
                                   NAS publisher tool, operator UI
  Orchestrator logic (in-process)
  In-memory fleet state + periodic JSON snapshot

Node Agents  (Windows service, every other machine)
  SignalR client → master (automatic reconnect)
  Local state DB (SQLite)
  Transfer engine (pluggable)
  Activation engine (pluggable)
  Safe-window listener
  Recordings uploader

Data Plane  (separate from SignalR)
  SMB / packed-block HTTP pull from NAS
  Robocopy subprocess for bulk background sync
  Chunked upload for recordings
```

### 6.2 Orchestrator–Agent Interface

The interface between external callers (group orchestrators, session manager, operator UI) and the sync system is deliberately narrow HTTP/REST. Callers do not know about transfer methods, staging, or manifests:

```csharp
// Group orchestrator → Sync orchestrator
EnsureActive(nodeId, packageId, version, priority, deadline?)
EnsureSetActive(capabilityFilter, packageId, version)
QueryStatus(nodeId, packageId) → SyncState
Subscribe(nodeId, packageId) → stream of state changes
```

---

## 7. Targeting Model

Targeting has two orthogonal axes that must not be conflated.

**Node attributes (static):**
- `nodeId`, `roomId`, `capabilities[]`, `role`, `subnet`

**Group membership (dynamic):**
- `currentGroupId` — nullable, updated via REST call from the session manager when a node joins or leaves a group
- A node belongs to at most one group at a time
- Membership changes trigger `DesiredState` recomputation in the master agent

**DeploymentScope targeting policy:**

```
DeploymentScope:
  scope:              Fleet | Group | Capability
  groupId:            (if scope = Group)
  capabilityFilter:   (if scope = Capability or Fleet)
  requiredForSession: bool
```

### 7.1 Node Joins a Group

When the session manager calls `POST /api/membership` with `(nodeId, groupId)`, the master agent:

1. Updates `SyncNode.currentGroupId`
2. Recomputes `DesiredState` for that node
3. Issues staging + activation jobs for any ContentPackages the node is now missing
4. Marks the node session-ready only when all `requiredForSession` packages are `Active`

---

## 8. Control Plane: ASP.NET Core + SignalR

The sync system's control plane is a single ASP.NET Core host running on the master agent machine. All node agents connect to it via SignalR. All external callers (group orchestrators, session manager, operator UI, NAS publisher) interact with it via REST.

### 8.1 Master Agent Endpoints

```
SignalR Hub
  /hubs/sync                        ← node agents connect here (outbound from agent only)

REST API — Integration
  POST /api/membership              ← session manager: node joins/leaves group
  POST /api/fleet-sync-mode         ← session manager: enable/disable fleet sync window
  POST /api/packages/publish        ← NAS publisher tool: new version available
  POST /api/deploy                  ← group orchestrator: EnsureActive request → returns intentId

REST API — Operator: Status
  GET  /api/status                  ← full fleet state (all nodes, all packages)
  GET  /api/status/{nodeId}         ← single node state
  GET  /api/intents                 ← all intents (filterable by node, package, state)
  GET  /api/intents/{intentId}      ← single intent detail
  GET  /api/messages                ← operator message queue (warnings, errors, stale flags)
  DELETE /api/messages/{messageId}  ← dismiss a message

REST API — Operator: Control
  DELETE /api/intents/{intentId}    ← cancel a pending or stale intent
  POST /api/intents/{intentId}/retry ← retry a failed intent
  POST /api/deploy                  ← operator-initiated deploy (same endpoint as orchestrator)
```

### 8.2 SignalR Hub Methods

**Agent → Master (client-to-server):**

| Method | Payload | Purpose |
|---|---|---|
| `Register` | `nodeId, roomId, capabilities[], currentVersions[]` | Called on connect and reconnect; master rebuilds node state from this |
| `ReportStatus` | `packageId, state, version, progressPercent` | Ongoing state updates during transfer; throttle to every 5% or 2s |
| `AckCommand` | `commandId, result, errorDetail?` | Confirms a command was received and acted on |
| `SignalSafeWindow` | `packageId, windowOpen` | App signals it is not using a package; master forwards Activate command |

**Master → Agent (server-to-client):**

| Method | Payload | Purpose |
|---|---|---|
| `ReceiveCommand` | `commandId, action, packageId, version, priority` | Stage / Activate / Rollback / Verify / Abort |
| `ReceivePublishEvent` | `packageId, version, manifestPath` | New version available; agent may begin staging if policy allows |
| `SetFleetSyncMode` | `enabled` | Gate: fleet-scoped jobs eligible or queued |

### 8.3 Reconnection and Intent Reliability

SignalR does not queue messages for disconnected clients. This is handled at the application layer via the Intent system:

- Every desired action (deploy, activate, upload recording) is created as a persisted **Intent** on the master before any SignalR message is sent.
- Intents are stored in the master's JSON snapshot and survive master restarts.
- On agent reconnect, `Register` is called. The master checks all Pending intents for that node and re-delivers them.
- Agents persist their own in-progress work to local SQLite and resume on restart without waiting for re-delivery.
- Agents are idempotent on `intentId` — receiving the same intent twice is harmless.
- Node agents use `HubConnectionBuilder` with `.WithAutomaticReconnect()`. No custom retry logic needed.

**Staleness:** Each intent has a `createdAt` timestamp and a per-category `staleAfter` threshold (configured on the master). When an intent ages past its threshold it is flagged `Stale` and a warning is written to the operator message queue. The intent is **not** cancelled or escalated — execution proceeds when the node reconnects or the safe window opens. The operator may cancel it explicitly via `DELETE /api/intents/{intentId}` if it is no longer relevant.

**Offline nodes and recording upload:** When a node is offline during a session, the master creates a Pending intent ("node X should upload recording from session Y") and retains it. When the node reconnects, the intent is delivered. The node evaluates whether it can fulfil it (files may no longer exist) and reports `Complete`, `NotApplicable`, or `Failed`. The master tracks collection completeness per session and exposes it via `GET /api/status`.

### 8.4 Node Presence Detection

SignalR connection lifecycle provides node health tracking:

- `OnConnectedAsync` → mark node `Online`; deliver all Pending intents for that node.
- `OnDisconnectedAsync` → mark node `Unreachable`; write a warning to the operator message queue if a deployment transaction was in progress.

### 8.5 Targeting Individual Nodes and Rooms

- **Unicast to one node:** `Clients.Client(connectionId)` — used for most deployment commands.
- **Broadcast to a room:** add agents to a SignalR group on connect: `Groups.AddToGroupAsync(connectionId, roomId)`. Then `Clients.Group(roomId)` for room-wide commands.
- **Broadcast to all:** `Clients.All` — used for `SetFleetSyncMode` and `ReceivePublishEvent`.



---

## 9. NAS Layout and Publish Gate

### 9.1 Directory Structure

```
/NAS/SyncRoot/
  packages/
    {packageId}/
      versions/
        v41/          ← immutable once published
        v42/
      manifests/
        v41.json
        v42.json
      latest.json     ← points to current published version
  blocks/             ← content-addressed packed blocks
    sha256-aa11.block
    sha256-bb22.block
  _draft/             ← work in progress, never read by agents
  _publishing/        ← transitional state
  policies/
    targeting.json
```

### 9.2 Publish Gate Protocol

No version is valid until the publish gate is written. The gate eliminates the entire class of "node synced a half-written version" bugs.

1. Files are prepared in `_draft/`
2. Manifest and packed blocks are generated; all hashes computed
3. Directory is moved to `versions/{vN}/`
4. `published.json` is written **last** — atomically
5. Master agent is notified via `POST /api/packages/publish`
6. Master reacts and recomputes `DesiredState`

> **Note:** The publish gate tool is a small CLI callable from both automated build pipelines and human operators. It is the only thing allowed to write `published.json`.

### 9.3 Manifest Format

```json
{
  "packageId": "TerrainTextures",
  "version": "2026.05.15-001",
  "groupHash": "sha256:...",
  "files": [
    {
      "relativePath": "grass/a.dds",
      "size": 123456,
      "hash": "sha256:...",
      "blockId": "sha256-aa11"
    }
  ],
  "chunks": [
    { "offset": 0, "size": 67108864, "hash": "sha256:..." }
  ],
  "activation": {
    "mode": "atomic-directory-swap | in-place | next-launch | cooperative-hot-swap"
  }
}
```

---

## 10. Agent State Machine

Each node agent tracks an explicit state per ContentPackage. States are persisted in local SQLite so the agent can resume after reboot.

```
Unknown → NotApplicable
        → Outdated → Queued → Transferring → TransferFailed
                                           → Transferred → Verifying → VerificationFailed
                                                                     → Staged → ReadyToActivate
                                                                               → ActivationPending
                                                                               → Activating → Active
                                                                                            → ActivationFailed → RollbackPending → RolledBack
        → Corrupt
```

The orchestrator UI shows per-node per-package state:

```
Node SIM-03
  RuntimeAssets:    ReadyToActivate v42
  TerrainTextures:  Transferring 67%
  Config-ScenarioA: Active v18
  OldLogsArchive:   Queued (FleetSyncMode=false)
```

---

## 11. Activation Strategies

### 11.1 Cooperative Hot-Swap (runtime assets, mid-session)

Used when the consuming app is running but not currently using the package. The app signals the agent locally; the agent reports back to the master via SignalR:

1. Agent: package v43 staged and verified. Sets intent state to `AwaitingSafeWindow`.
2. App: signals the agent locally (named pipe or loopback) when not using the package.
3. Agent: receives signal. Performs atomic directory swap.
4. Agent: calls `SignalSafeWindow(packageId, windowOpen=false)` on master to confirm activation complete.
5. App: resumes using package from new version.
6. Agent: calls `ReportStatus(packageId, Active, v43)` on master.

> **Note:** Safe-window requests have no hard deadline — the intent waits indefinitely for the window to open. If the intent ages past the configured `staleAfter` threshold a warning is written to the operator message queue, but execution is not cancelled. The operator may cancel the intent explicitly if it is no longer relevant.

### 11.2 Atomic Directory Swap

```
C:/App/Data/GroupA/
  active/    ← symlink or junction pointing to current version
  versions/
    v41/
    v42/     ← staged and verified

Activation: repoint active/ → versions/v42
```

> **Note:** Windows symlink/junction behaviour with open file handles must be tested for your specific application. If open handles block the swap, use a rename approach with a maintenance window instead.

### 11.3 Next-Launch Activation (executables)

Executables cannot be hot-swapped on Windows. A minimal bootstrapper process (not part of the agent) checks for a staged binary on startup and performs the swap before launching the main agent or application. The sync agent stages the new binary but never activates it directly.

### 11.4 Coordinated Fleet Activation (two-phase)

For group-scoped packages where all nodes must activate simultaneously:

```
Phase 1 — Prepare:
  Each node: transfer → verify → stage → report ReadyToActivate

Phase 2 — Commit (when all required nodes are Ready):
  Master sends ReceiveCommand(action=Activate, version=v43) to all nodes
  Each node activates locally, reports Active(v43)

Failure policy (configurable per DeploymentScope):
  RollbackAll | OperatorIntervention

All failures and warnings are written to the operator message queue regardless
of which policy is chosen. The operator sees the full picture when they next
check status — no silent failures.
```

---

## 12. Transfer Engines

All transfer engines implement a single interface:

```csharp
public interface ITransferEngine {
    string MethodId { get; }
    Task<TransferResult> ExecuteAsync(
        SyncJob job, SyncManifest manifest,
        TransferContext context,
        IProgress<TransferProgress> progress,
        CancellationToken cancellationToken);
}
```

| Engine | Best for | Avoid for | Notes |
|---|---|---|---|
| `DirectCopy` | Config, small critical files, blocking sync | Large trees of small files over SMB | Full progress + hash verification built-in |
| `RobocopySubprocess` | Large directory mirroring, archiving, bulk background sync | Transactional activation (use only for staging) | Parse exit codes carefully; use `/Z` for resumable mode |
| `PackedBlockTransfer` | Many small files (AI tables, textures, lookup data) | Single huge files | Server-side packing at publish time; block-level hash verification; enables deduplication across versions |
| `ChunkedHugeFile` | Large recordings upload, very large single assets | Many small files | Sidecar chunk hashes; resume from last verified chunk |

> **Note:** Relay/cascade distribution is not required at current scale (10–50 nodes, single LAN). Add only if NAS saturation is observed at 100+ nodes. The pluggable interface accommodates it later without architectural change.

---

## 13. Desync Detection

### 13.1 Normal vs. Error States

| Condition | Classification | Action |
|---|---|---|
| Node version < NAS latest version | Normal — NAS updated | Queue sync job per policy |
| All group nodes on same version as expected | OK | No action |
| Group nodes disagree on active version | **Error: DeploymentInconsistent** | Alert operator; block session start |
| Node reports version X but file hashes don't match manifest | **Error: Corrupt** | Mark Corrupt; trigger re-sync |
| Node silent / SignalR disconnect | **Error: NodeUnreachable** | Mark Unreachable; write warning to operator message queue; exclude from active deployment transaction |

### 13.2 Verification Levels

- **Level 1 — Version manifest only:** fastest; good for normal monitoring.
- **Level 2 — File metadata:** size, mtime, file count. Medium confidence.
- **Level 3 — Full file hash (SHA-256):** critical runtime files, post-transfer verification.
- **Level 4 — Chunk hash:** huge files; enables partial re-transfer of only corrupt chunks.
- **Level 5 — Group Merkle hash:** single hash for an entire package; fast fleet-wide equality check.

---

## 14. Local Storage Layout (per node)

```
C:/ProgramData/YourAppSync/
  agent.db              ← SQLite: jobs, states, verification results, activation history,
                                  pending intents (survive agent restart)
  manifests/
    {packageId}/
      {version}.json
  staging/
    {packageId}/{version}/
  blocks/               ← content-addressed packed block cache
    sha256-aa11.block
  versions/
    {packageId}/
      v41/
      v42/
  active/               ← symlinks/junctions to current active versions
    {packageId}/  →  versions/{packageId}/v42
  logs/

Agent SQLite tables:
  NodeSyncState, TransferJobs, InstalledVersions,
  VerificationResults, PackageCache, ActivationHistory,
  FailedFiles, PendingIntents
```

---

## 15. Orchestrator State

At 10–100 nodes, the orchestrator maintains state in-memory with periodic JSON snapshot. SQLite on the orchestrator adds migration overhead for a problem this size does not require. Nodes report their full state on connect and on change; the orchestrator reconstructs its view from those reports after restart.

```
Orchestrator in-memory model:
  Fleet:               Dictionary<nodeId, SyncNode>
  Membership:          Dictionary<nodeId, groupId?>
  PublishedVersions:   Dictionary<packageId, List<Version>>
  DesiredState:        Dictionary<(nodeId, packageId), DesiredVersion>
  ActiveTransactions:  Dictionary<transactionId, DeploymentTransaction>
  Intents:             Dictionary<intentId, Intent>
  MessageQueue:        List<OperatorMessage>   ← warnings, errors, stale flags

Intent lifecycle:
  Pending → Executing → Complete
                      → Failed      (written to MessageQueue)
                      → Stale       (warning written to MessageQueue; still executes)
                      → Cancelled   (operator DELETE /api/intents/{intentId})

Snapshot: periodic JSON write to disk (e.g. every 60s)
          Intents and MessageQueue are included in snapshot.
Rebuild:  on startup, agents reconnect and call Register() with their full
          current state; master reconstructs fleet view from those reports.
          Pending intents are re-delivered to reconnecting agents.
```

---

## 16. C# Interface Summary (.NET 8)

```csharp
// Planning
public interface ISyncPlanner {
    Task<IReadOnlyList<Intent>> PlanAsync(
        SyncNode node, DesiredState desired, CancellationToken ct);
}

// Transfer
public interface ITransferEngine {
    string MethodId { get; }
    Task<TransferResult> ExecuteAsync(
        SyncJob job, SyncManifest manifest, TransferContext ctx,
        IProgress<TransferProgress> progress, CancellationToken ct);
}

// Verification
public interface IVerifier {
    Task<VerificationResult> VerifyAsync(
        SyncManifest manifest, string localPath,
        VerificationMode mode, CancellationToken ct);
}

// Activation
public interface IActivator {
    Task<ActivationResult> ActivateAsync(
        StagedPackage staged, ActivationContext ctx, CancellationToken ct);
}

// Hot-swap coordination (agent ↔ consuming app, local only)
public interface IHotSwapParticipant {
    Task PrepareSwitchAsync(string packageId, string targetVersion);
    Task CommitSwitchAsync(string packageId, string targetVersion);
    Task RollbackSwitchAsync(string packageId, string previousVersion);
}

// Intent store (master-side, persisted in JSON snapshot)
public interface IIntentRepository {
    Task<Intent> CreateAsync(IntentRequest request, CancellationToken ct);
    Task<Intent?> GetAsync(string intentId, CancellationToken ct);
    Task<IReadOnlyList<Intent>> GetPendingForNodeAsync(string nodeId, CancellationToken ct);
    Task UpdateStateAsync(string intentId, IntentState state, CancellationToken ct);
    Task CancelAsync(string intentId, CancellationToken ct);
}

// Operator message queue (master-side)
public interface IOperatorMessageQueue {
    Task EnqueueAsync(OperatorMessage message, CancellationToken ct);
    Task<IReadOnlyList<OperatorMessage>> GetAsync(MessageFilter filter, CancellationToken ct);
    Task DismissAsync(string messageId, CancellationToken ct);
}
```

---

## 17. Recommended Build Sequence

> **Note:** Do not build everything at once. Each phase is independently useful and testable.

#### Phase 1 — Schema and publish gate
Define ContentPackage schema covering all 5 categories. Implement the publish gate CLI. Implement the NAS directory layout. No transfer code yet.

#### Phase 2 — Agent state machine + stub transfer
Implement the agent state machine with SQLite persistence. Use a stub transfer engine (direct file copy). Wire up SignalR hub and the `Register` / `ReportStatus` / `ReceiveCommand` methods. Build the orchestrator monitoring UI. This gives you fleet visibility before any transfer complexity.

#### Phase 3 — Cooperative safe-window protocol
Define and implement the `SignalSafeWindow` SignalR method and the `IHotSwapParticipant` interface. Both the sync agent and the consuming app must agree on this contract early — changing it after deployment is painful.

#### Phase 4 — Packed block transfer
Implement server-side packing at publish time and client-side unpack. Apply to small-many-files categories (AI tables, textures). High leverage, low architectural risk.

#### Phase 5 — Recordings upload
Implement the chunked upload flow from nodes to NAS as a separate workstream. Design the orchestrator's collection-complete tracking if required.

#### Phase 6 — Coordinated deployment (two-phase)
Implement the Prepare/Commit state machine for group-scoped packages. Add rollback. Add session-ready gating.

#### Phase 7 — Executable updates
Implement the bootstrapper/updater. Keep entirely separate from the main agent update path.

#### Phase 8 — Fleet sync scheduler
Implement the overnight fleet sync mode with `FleetSyncMode` gate integration.

---

## 18. Resolved Design Decisions

All questions previously listed here have been resolved and incorporated into the document above. They are summarised here for traceability.

**Recordings upload — collection tracking:** The master creates a Pending intent for each node that should upload a recording. It tracks collection completeness per session and exposes it via `GET /api/status`. Nodes that were offline receive their intent on reconnect and evaluate feasibility locally.

**Safe-window policy:** Infinite deadline. The intent waits for the window with no hard expiry. A per-category `staleAfter` threshold generates a warning in the operator message queue if the intent ages past it, but does not cancel or escalate. The operator cancels explicitly if needed.

**Failure policy:** `RollbackAll` or `OperatorIntervention` — no `ContinueDegraded`. All failures and warnings at any severity are written to the operator message queue and visible via `GET /api/messages`.

**Master restart resilience:** Intents are persisted in the master's JSON snapshot and in the agent's local SQLite. Both sides resume independently on restart. On reconnect, the master re-delivers all Pending intents to the agent. Stale intents are flagged, not cancelled.

**Packaging strategy:** Per-category defaults with optional per-file-extension overrides. Configuration lives in the publish gate tool. The transfer engine is unaware of packing strategy — it receives a block and unpacks it. For already-compressed formats (textures, binaries), uncompressed container is preferred.

**Windows symlink/junction behaviour:** To be validated against the specific application before committing to atomic directory swap as the activation mode. Fall back to rename-based swap if open handles block the junction repoint.

