# Distributed File Deployment & Synchronization System
## Architecture Design Document

*For a distributed game/simulation engine*
*C# / .NET 8 · CycloneDDS.NET · NAS-authoritative · May 2026*

---

## 1. Purpose and Scope

This document defines the architecture of a content deployment and file synchronization system for a distributed game/simulation engine. The system manages 10–100+ Windows nodes across multiple subnets, coordinates file distribution from a central NAS master, and integrates with an existing CycloneDDS-based orchestration infrastructure.

The document reflects a design arrived at through detailed requirements analysis. It supersedes generic option surveys and focuses exclusively on decisions relevant to this system's actual constraints.

---

## 2. Core Design Principles

The following principles were agreed during design and must be preserved through implementation:

- The transfer method is never the centre of the design. Robocopy, direct copy, packed blocks, and relay are implementation strategies behind a single higher-level model.
- The central abstraction is: **ContentPackage version X should be active on node set Y under policy Z.**
- Transfer and activation are always separate pipeline stages: `Stage → Verify → Activate`.
- The NAS is the single source of truth. No version is valid until explicitly published via a publish gate.
- DDS is the control plane only. File bytes never travel over DDS topics.
- Group membership is application-layer context consumed by the sync orchestrator, not owned by it.
- Fleet sync and group-session sync are mutually exclusive operational modes, enforced by a hard gate.

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
- A `FleetSyncMode` flag is published on the management DDS domain by the session manager.

> **Note:** No collision handling between modes is required. The gate is enforced at the application layer above the sync orchestrator.

---

## 4. Terminology

| Term | Definition |
|---|---|
| **ContentPackage** | A versioned, immutable set of files on the NAS (e.g. `Config-ScenarioA-v3`). Has a manifest. Replaces "Sync Group" for the data dimension. |
| **DeploymentScope** | The dynamic targeting policy: which nodes should have a given ContentPackage. Replaces "Sync Group" for the targeting dimension. |
| **SyncNode** | A single computer in the fleet. Has a fixed `roomId` (DDS domain), `capabilities[]`, and a nullable `currentGroupId`. |
| **GroupId** | Application-layer session group identifier. Consumed by the sync orchestrator; not owned by it. |
| **DesiredState** | The orchestrator's computed view of what version each node should have active, given current memberships and policies. |
| **Publish Gate** | A `published.json` sentinel file written last on the NAS. No version is valid until the gate is written. |
| **FleetSyncMode** | Boolean flag on the management DDS domain. When `true`, fleet-scoped jobs are eligible; when `false`, they queue. |
| **Safe Window** | A signal from the consuming app that it is not using a given ContentPackage, permitting mid-session activation. |

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

Sync Orchestrator  (.NET 8 service / UI backend)
  Fleet registry: nodes, capabilities, group membership
  Desired state computation
  Deployment transaction management
  Event reactor: membership changes → sync triggers
  Overnight fleet sync scheduler
  FleetSyncMode gate consumer

DDS Control Plane  (per-room domains + 1 management domain)
  Topics: see Section 8

Node Agent  (Windows service on every node)
  DDS participant (room domain + management domain)
  Local state DB (SQLite)
  Transfer engine (pluggable)
  Activation engine (pluggable)
  Safe-window listener
  Recordings uploader

Data Plane  (separate from DDS)
  SMB / packed-block HTTP pull from NAS
  Robocopy subprocess for bulk background sync
  Chunked upload for recordings
```

### 6.2 Orchestrator–Agent Interface

The interface between group orchestrators (application layer) and the sync orchestrator is deliberately narrow. Group orchestrators do not know about transfer methods, staging, or manifests:

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
- `currentGroupId` — nullable, updated by `MembershipEvent` on management DDS domain
- A node belongs to at most one group at a time
- Membership changes are events that trigger `DesiredState` recomputation

**DeploymentScope targeting policy:**

```
DeploymentScope:
  scope:              Fleet | Group | Capability
  groupId:            (if scope = Group)
  capabilityFilter:   (if scope = Capability or Fleet)
  requiredForSession: bool
```

### 7.1 Node Joins a Group

When a `MembershipEvent(nodeId, groupId)` arrives on the management domain, the orchestrator:

1. Updates `SyncNode.currentGroupId`
2. Recomputes `DesiredState` for that node
3. Issues staging + activation jobs for any ContentPackages the node is now missing
4. Marks the node session-ready only when all `requiredForSession` packages are `Active`

---

## 8. DDS Topology (CycloneDDS.NET)

### 8.1 Domain Layout

```
Management domain (domain 0)
  Participants: sync orchestrator, group orchestrators, session manager
  Accepts discovery chattiness — limited, well-known participant set
  Topics: MembershipEvent, PublishEvent, FleetSyncMode, EnsureActive commands

Room domains (domain 1, 2, 3 … per physical room/subnet)
  Participants: all agents in that room + sync orchestrator
  Unicast peer list, no multicast
  Topics: NodeSyncStatus, DeploymentCommand, DeploymentAck, SafeWindowSignal
```

### 8.2 Discovery Configuration

CycloneDDS multicast is disabled on all room domains. Each agent lists the sync orchestrator and one local anchor as static unicast peers:

```xml
<CycloneDDS>
  <Domain id="1">
    <General>
      <AllowMulticast>false</AllowMulticast>
    </General>
    <Discovery>
      <Peers>
        <Peer Address="192.168.1.10"/>  <!-- sync orchestrator -->
        <Peer Address="192.168.1.11"/>  <!-- room anchor -->
      </Peers>
    </Discovery>
  </Domain>
</CycloneDDS>
```

> **Note:** The sync orchestrator participates in all room domains plus the management domain. It is the only node with fleet-wide visibility.

### 8.3 Topic Definitions

| Topic | Publisher | Subscriber | QoS | Purpose |
|---|---|---|---|---|
| `NodeSyncStatus` | Agent | Orchestrator | RELIABLE, TRANSIENT_LOCAL, Key: nodeId+packageId | Per-node per-package state; late-joining orchestrator reconstructs full fleet view |
| `DeploymentCommand` | Orchestrator | Agents | RELIABLE, KEEP_LAST(1) per key, partitioned by roomId | Stage / Activate / Rollback / Verify / Abort commands |
| `DeploymentAck` | Agent | Orchestrator | RELIABLE | Command acknowledgement and result |
| `SafeWindowSignal` | Consuming app (via agent) | Agent | RELIABLE, KEEP_LAST(1), Key: nodeId+packageId | App signals it is not using a package; permits mid-session activation |
| `MembershipEvent` | Session manager | Orchestrator + agents | RELIABLE, TRANSIENT_LOCAL | Node joins or leaves a group; triggers DesiredState recomputation |
| `PublishEvent` | NAS publisher tool | Orchestrator | RELIABLE, TRANSIENT_LOCAL | New ContentPackage version available; triggers planning |
| `FleetSyncMode` | Session manager | Orchestrator | RELIABLE, KEEP_LAST(1) | Boolean gate: fleet sync eligible or blocked |

> **Note:** `NodeSyncStatus` update rate during active transfer: throttle to every 5% or every 2 seconds. At 100 nodes, per-percent updates generate ~10,000 messages per fleet sync — avoid this.

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
5. `PublishEvent` is published on the management DDS domain
6. Orchestrator reacts to `PublishEvent` and recomputes `DesiredState`

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

Used when the consuming app is running but not currently using the package. The app and agent coordinate via the `SafeWindowSignal` DDS topic:

1. Agent: package v43 staged and verified. Requests safe window.
2. App: acknowledges. Publishes `SafeWindowSignal(windowOpen=true)` when not using package.
3. Agent: receives signal. Performs atomic directory swap.
4. Agent: publishes activation-complete signal.
5. App: resumes using package from new version.
6. Agent: reports `Active(v43)` on `NodeSyncStatus`.

> **Note:** Timeout policy per package: if the app does not signal a safe window within the configured deadline, the agent either waits until session end or raises an alert to the orchestrator. Define this policy in the `DeploymentScope`.

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
  Orchestrator publishes DeploymentCommand(action=Activate, version=v43)
  Each node activates locally, reports Active(v43)

Failure policy (configurable per DeploymentScope):
  RollbackAll | ContinueDegraded | RetryNode | BlockSessionStart | OperatorIntervention
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
| Node silent beyond liveliness lease | **Error: NodeUnreachable** | Alert; exclude from deployment transaction |

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
  agent.db              ← SQLite: jobs, states, verification results, activation history
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
  VerificationResults, PackageCache, ActivationHistory, FailedFiles
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

Snapshot: periodic JSON write to disk (e.g. every 60s)
Rebuild:  on startup, wait for agents to re-report NodeSyncStatus
          (TRANSIENT_LOCAL durability ensures no messages are missed)
```

---

## 16. C# Interface Summary (.NET 8)

```csharp
// Planning
public interface ISyncPlanner {
    Task<IReadOnlyList<SyncJob>> PlanAsync(
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

// Hot-swap coordination
public interface IHotSwapParticipant {
    Task PrepareSwitchAsync(string packageId, string targetVersion);
    Task CommitSwitchAsync(string packageId, string targetVersion);
    Task RollbackSwitchAsync(string packageId, string previousVersion);
}

// Agent remote API
public interface INodeAgentClient {
    Task<NodeStatus> GetStatusAsync(CancellationToken ct);
    Task<CommandResult> EnqueueJobAsync(SyncJob job, CancellationToken ct);
    Task<CommandResult> ActivateAsync(ActivationCommand cmd, CancellationToken ct);
}
```

---

## 17. Recommended Build Sequence

> **Note:** Do not build everything at once. Each phase is independently useful and testable.

#### Phase 1 — Schema and publish gate
Define ContentPackage schema covering all 5 categories. Implement the publish gate CLI. Implement the NAS directory layout. No transfer code yet.

#### Phase 2 — Agent state machine + stub transfer
Implement the agent state machine with SQLite persistence. Use a stub transfer engine (direct file copy). Connect DDS topics. Build the orchestrator monitoring UI. This gives you fleet visibility before any transfer complexity.

#### Phase 3 — Cooperative safe-window protocol
Define and implement the `SafeWindowSignal` topic and the `IHotSwapParticipant` interface. Both the sync agent and the consuming app must agree on this contract early — changing it after deployment is painful.

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

## 18. Remaining Open Questions

- **Recordings upload:** does the orchestrator need to know when all nodes have uploaded before proceeding (e.g. before archiving)? What is the policy for nodes that were offline during a session?
- **Safe-window timeout policy:** per-package configured deadline before the agent escalates. Define values per category.
- **Coordinated activation failure policy:** per `DeploymentScope` — `RollbackAll` vs. `ContinueDegraded` vs. `OperatorIntervention`.
- **CycloneDDS.NET binding maturity:** verify `TRANSIENT_LOCAL` durability, per-topic QoS, and liveliness detection are fully supported before finalising the DDS topic design.
- **Packed block format:** ZIP vs. uncompressed container vs. Zstandard-compressed tar. For already-compressed assets (textures, binaries), uncompressed container is likely best.
- **Windows symlink/junction behaviour:** test atomic directory swap with open handles in your specific app before committing to that activation mode.
