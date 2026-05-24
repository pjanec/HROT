# Distributed File Deployment & Synchronization System
## Architecture Design Document — Revision 2

*For a distributed game/simulation engine*
*C# / .NET 8 · ASP.NET Core · SignalR · NAS-authoritative · May 2026*

*Supersedes the original document. Carries forward unchanged content where applicable; reworks terminology, identity, distribution topology, recordings, configuration, and garbage collection.*

---

## 1. Purpose and Scope

This document defines the architecture of a content deployment and file synchronization system for a distributed game/simulation engine. The system manages up to ~200 Windows nodes across up to ~10 network segments and coordinates file distribution from a central NAS. The control plane is built on ASP.NET Core and SignalR (.NET 8). The sync system is fully decoupled from the simulation engine's middleware and integrates only via HTTP and SignalR.

### 1.1 In Scope

- NAS → fleet distribution of runtime assets, configs, datasets, and large pre-built data blobs
- Fleet → NAS upload of session recordings
- Per-segment cascading distribution managing inter-segment bandwidth and Windows CAL limits
- Operator-visible Intents for all desired work, surviving master and agent restarts
- HTTP/REST integration surface for group orchestrators, session managers, app processes, and operator UI
- Site-wide configuration distribution from master to agents

### 1.2 Out of Scope (explicitly deferred)

- **Authentication / Authorization** — the entire system runs open. To be addressed separately.
- **Master agent high availability** — extended master outage is a stop-the-line condition; no standby or failover designed in.
- **Executable / agent updates** — file data distribution only. Executables follow a separate update cycle, out of this document.
- **Multi-master or multi-NAS topologies** — single NAS, single master.

---

## 2. Core Design Principles

- The transfer method is never the centre of the design. Direct HTTP, NAS-side SMB or local-FS staging, chunked huge-file transport are implementation strategies behind a single higher-level model.
- The central abstraction is: **Bundle version X should be active on node set Y under policy Z.**
- Transfer and activation are always separate pipeline stages: `Stage → Verify → Activate`.
- The NAS is the single source of truth. No version is valid until explicitly published via a publish gate.
- The control plane (SignalR and `/api/...` HTTP) carries only commands, status, and events. File bytes never travel over the control plane.
- **The master is the only SMB client in the system.** All other inter-machine file transfer is HTTP.
- Group membership is application-layer context consumed by the sync orchestrator, not owned by it.
- Fleet sync and group-session sync are mutually exclusive operational modes, enforced by a hard gate.
- All desired work is represented as persisted Intents. Intents survive restarts, are visible via API, and are cancellable by the operator.

---

## 3. Operational Modes

### 3.1 Session Window

Groups are active. Nodes belong to at most one group at a time. Only group-scoped and capability-scoped sync runs. Fleet sync is blocked.

- Triggered by: group formation, node joining a group, operator-triggered patch.
- Characteristics: fast, targeted, must complete before session start.
- Blocking sync (config, scenario data) must finish before the group is allowed to start.

### 3.2 Fleet Sync Window

No groups active. No new groups may be created until fleet sync completes. The sync orchestrator has exclusive access to all nodes.

- Triggered by: scheduled overnight job, operator command.
- Characteristics: background, parallel across segments, no session coordination needed.
- The `FleetSyncMode` gate is set via a REST call to the master agent by the session manager or operator.

No collision handling between modes is required. The gate is enforced at the application layer above the sync orchestrator.

---

## 4. Terminology

| Term | Definition |
|---|---|
| **Bundle** | A versioned, immutable set of files on the NAS (e.g. `TerrainTextures-v42`). Has a manifest. Identified by a stable `bundleId`. |
| **bundleId** | Stable string name for a kind of content. Versions belong to a bundle. |
| **version** | Free-form filename-safe string identifier for a snapshot of a bundle (e.g. `v42`, `r12345`, `a1b2c3d4`, `2026-05-17-001`). Newness is determined by publish timestamp, not string ordering. |
| **DeploymentScope** | Targeting policy: which nodes should have a given bundle. |
| **Agent** | The sync service installed on a machine. Connects to the master via SignalR. One agent per computer. |
| **agentId** | Stable identifier of an agent / machine. SignalR client identity. |
| **logicalNodeId** | Integer identifier used by the consuming app. Multiple logical nodes may live on one agent. Mapping declared in site config. |
| **Segment** | A single sub-LAN or broadcast domain. Intra-segment bandwidth is plentiful; inter-segment bandwidth is the constrained resource. Has one designated relay agent. |
| **Relay** | A regular agent additionally designated to serve bundles over HTTP within its segment. One per segment (or implicit, when the master sits in the segment). |
| **Master** | The single ASP.NET Core process hosting the control plane (REST + SignalR) and the data plane HTTP for bundle and recording bytes. The only SMB client to the NAS. Optionally co-located with the NAS. |
| **GroupId** | Application-layer session group identifier. Consumed by the sync orchestrator; not owned by it. |
| **DesiredState** | The orchestrator's computed view of what version each agent should have active for each bundle, given current memberships and policies. |
| **Publish Gate** | A `published.json` sentinel file written last on the NAS. No version is valid until the gate is written. |
| **FleetSyncMode** | Boolean flag set on the master via REST. When `true`, fleet-scoped jobs are eligible; when `false`, they queue. |
| **Safe Window** | A signal from the consuming app that it is not using a given bundle, permitting mid-session activation. |
| **Intent** | A persisted, operator-visible unit of desired work. Lifecycle: Pending → Executing → Complete / Stale / Failed / Cancelled. Survives master and agent restarts. Cancellable via REST. |
| **Stale Intent** | An intent whose `createdAt` age exceeds its category staleness threshold. Flagged as warning; not auto-cancelled. |

*Naming note*: "Package" is reserved for installation packages that update node software (out of scope here). The runtime-data unit is consistently called **Bundle**.

---

## 5. Data Categories

Four downward categories (NAS → nodes) plus one upward category (nodes → NAS). Each has distinct transfer, container, activation, and targeting semantics.

| Category | Direction | Size profile | Container on NAS | Transfer engine | Activation | Scope |
|---|---|---|---|---|---|---|
| Runtime assets (textures, meshes, terrain) | NAS → nodes | Many small files | Zip archive | `DirectHttp` | Atomic directory swap, optionally cooperative hot-swap | Capability |
| Configs / scenario definitions | NAS → nodes | Few small files | Direct (or zip if many) | `DirectHttp` | Staged, blocking | Group |
| Datasets (AI tables, lookup data) | NAS → nodes | Many small files | Zip archive | `DirectHttp` | Staged swap | Capability / Fleet |
| Large pre-built data blobs | NAS → nodes | Few huge files (up to ~0.5 TB), already compressed at source | None (raw) | `ChunkedHugeFile` | In-place or atomic directory swap | Fleet / Capability |
| Recordings | Nodes → NAS | Few medium-to-huge files per node, compressible ~10× | Per-node zip (compressed at the node before upload) | Chunked HTTP upload | In-place on NAS | Per-session, per-node |

Notes:
- Already-compressed files inside a zip use `CompressionLevel.NoCompression` (store mode); text content uses `CompressionLevel.Optimal`.
- Large pre-built blobs are already compressed at source — no extra zip envelope.
- Recordings are an upload/collection flow, architecturally distinct from distribution.

---

## 6. System Architecture

### 6.1 Layers

```
NAS  (Windows file server or COTS appliance)
  Immutable published bundle versions, manifests
  Publish gate (published.json written last)
  Session recording archive
  Site config canonical file (may live next to the master)

Master  (ASP.NET Core, single process, single machine — optionally co-located with NAS)
  SignalR Hub /hubs/sync                    ← all agents connect here
  REST API  /api/...                        ← control plane
  Data plane HTTP /content/...              ← bundle bytes, recording chunk PUTs
  Orchestrator logic (in-process)
  Bundle registry, intent store, fleet state (in-memory + JSON snapshot)
  Pull cache of bundles already fetched from NAS
  Site config canonical store

Relays  (regular agents designated by site config; one per segment)
  SignalR client → master
  Local state DB (SQLite)
  Data plane HTTP /content/...              ← serves cached bundle bytes to nodes in segment
  Bundle cache (zipped or extracted per bundle category)
  Standard agent transfer / activation engines

Agents  (every other machine, Windows service)
  SignalR client → master (automatic reconnect)
  Local state DB (SQLite)
  Transfer engine (HTTP pull from relay or master)
  Activation engine
  Recordings uploader (HTTP push to master)

Data Plane (transports)
  NAS → master:   SMB (1 session) or local filesystem (if co-located)
  Master → relay: HTTP, chunked, byte-range capable
  Relay → node:   HTTP, chunked, byte-range capable
  Node → master:  HTTP chunked PUT (recordings)
  Master → NAS:   SMB or local filesystem (recordings storage)
```

### 6.2 Topology and Segments

The fleet is divided into segments. A segment is a single sub-LAN / broadcast domain where intra-segment bandwidth is plentiful and the link to other segments is the constrained resource. Typical scale: up to 10 segments, up to ~20 agents per segment.

Each segment has one designated **relay agent**, declared statically in site config — the operator picks the machine with the best uplink to the master. If the master itself sits in a segment that also contains active nodes, the master acts as that segment's relay; no separate relay machine is needed in the master's segment.

The cascade design is optimised around the inter-segment bottleneck:

- Each bundle crosses any given inter-segment link **at most once per segment**.
- The NAS is touched by **one** SMB client (the master).
- Windows CAL pressure on the NAS is bounded at 1; CAL pressure within segments is 0 (HTTP, not SMB).

### 6.3 Master as Data Plane Gateway

The master is the only SMB client in the system. It pulls bundle bytes from the NAS once into a local pull cache, then serves those bytes to relays over HTTP. Relays serve to nodes over HTTP. Recordings flow the other way by the same principle: nodes upload to master over HTTP; master writes to NAS via SMB or local filesystem.

Master endpoints split cleanly:

- **Control plane HTTP** (`/api/...`) — commands, status, events. Small request/response bodies.
- **Data plane HTTP** (`/content/...`) — bundle bytes and recording chunks. Streaming, byte-range capable.

Both run inside the same ASP.NET Core process under different route prefixes. Kestrel handles streaming and byte-range natively.

When the master is co-located with the NAS, it accesses bundle storage via local file paths — no SMB at all. Configuration distinguishes via `nas.localPath` versus `nas.uncPath`.

### 6.4 Orchestrator–Agent Interface

The interface between external callers (group orchestrators, session manager, operator UI, app process) and the sync system is deliberately narrow HTTP/REST. Callers do not know about transfer methods, segments, staging, or manifests:

```csharp
// External callers → master
EnsureActive(logicalNodeId, bundleId, version, priority, deadline?)
EnsureSetActive(capabilityFilter, bundleId, version)
QueryStatus(logicalNodeId, bundleId) → SyncState
RegisterBundle(bundleDefinition)
Publish(bundleId, version, manifestPath)
FinalizeRecording(sessionId, logicalNodeId, files)
FinalizeSession(sessionId, force?)
SignalSafeWindow(logicalNodeId, bundleId, windowOpen)
```

All mutating calls return immediately with an `intentId`; status is observed via `GET /api/intents/{intentId}`.

---

## 7. Identity and Targeting

### 7.1 Two-Tier Identity

- **agentId** — stable identifier of the sync agent process / machine. One per computer. SignalR client identity.
- **logicalNodeId** — integer used by the consuming app. Many-per-agent. Useful in testing (multiple logical nodes on one box) and in normal operation when one machine simulates multiple logical participants.

Mapping is declared in site config:

```json
{
  "agents": [
    { "agentId": "SIM-03", "logicalNodeIds": [42, 43] },
    { "agentId": "SIM-04", "logicalNodeIds": [44] }
  ]
}
```

External callers pass `logicalNodeId`; the master resolves to `agentId` internally for SignalR dispatch. Status responses include both:

```json
{ "agentId": "SIM-03", "logicalNodeIds": [42, 43], "bundles": { /* ... */ } }
```

When multiple logical nodes share an agent and submit potentially conflicting safe-window signals for the same bundle, the master ANDs the signals: the agent activates only when *all* logical nodes mapped to it report safe.

### 7.2 Targeting Model

Two orthogonal axes:

**Agent attributes (static):** `agentId`, `segmentId`, `capabilities[]`, `role`.

**Group membership (dynamic):** `currentGroupId` — nullable, updated by REST call from the session manager when a node joins or leaves a group. A node belongs to at most one group at a time. Membership changes trigger `DesiredState` recomputation.

**DeploymentScope:**

```
DeploymentScope:
  type:                 Fleet | Group | Capability | LogicalNode
  groupId:              (if type = Group)
  capabilityFilter:     (if type = Capability or Fleet)
  logicalNodeIds:       (if type = LogicalNode)
  requiredForSession:   bool
```

When a node joins a group, the master:
1. Updates `agent.currentGroupId`
2. Recomputes `DesiredState` for the node
3. Issues staging + activation intents for any bundles the node is now missing
4. Marks the node session-ready only when all `requiredForSession` bundles are `Active`

### 7.3 Bundle Registry (Dynamic)

Bundles are managed at runtime via REST API. The site config may optionally declare bundles that are guaranteed present.

```
POST   /api/bundles                              register a bundle (returns 201 / 409)
GET    /api/bundles                              list registered bundles
GET    /api/bundles/{bundleId}                   detail (including all published versions)
PUT    /api/bundles/{bundleId}                   update scope, retention, staleAfter
DELETE /api/bundles/{bundleId}                   deregister (refused if any Active version)

POST   /api/bundles/{bundleId}/versions          publish a new version (called by the publish CLI)
                                                 ?autoRegister=true creates the bundle if missing
```

Bundle definition shape:

```json
{
  "bundleId": "TerrainTextures",
  "dataCategory": "RuntimeAsset",
  "defaultScope": { "type": "Capability", "capabilityFilter": "render" },
  "activationMode": "atomic-directory-swap",
  "retentionCount": 3,
  "staleAfter": "24h",
  "chunkSize": "64MB"
}
```

Bundle definitions live in the master's JSON snapshot and survive master restart. Each agent's site-config slice on `Register` includes the bundle definitions relevant to it.

`bundleId` is distinct from `intentId`. `bundleId` is a stable name for content. `intentId` is a per-action UUID for a specific desired action. Many intents may reference the same bundle.

---

## 8. Control Plane: ASP.NET Core + SignalR

### 8.1 Master Endpoints

```
SignalR Hub
  /hubs/sync                                       agents connect here

REST API — Integration
  POST /api/membership                             session manager: node joins/leaves group
  POST /api/fleet-sync-mode                        session manager: enable/disable fleet sync window
  POST /api/bundles                                register a bundle
  PUT  /api/bundles/{bundleId}                     update bundle metadata
  POST /api/bundles/{bundleId}/versions            publish a new version
  POST /api/deploy                                 group orchestrator: EnsureActive → returns intentId
  POST /api/recordings                             app: declare per-node recording ready
  POST /api/sessions/{sessionId}/finalize          app: collection-complete signal
  POST /api/safe-window                            app: signal safe-window for (logicalNodeId, bundleId)
  POST /api/config/reload                          operator: re-read site config

REST API — Operator: Status
  GET  /api/status                                 full fleet state
  GET  /api/status/{agentId}                       single agent state
  GET  /api/intents                                all intents (filterable)
  GET  /api/intents/{intentId}                     single intent
  GET  /api/messages                               operator message queue
  GET  /api/sessions                               list recording sessions
  GET  /api/sessions/{sessionId}                   session detail
  DELETE /api/messages/{messageId}                 dismiss a message

REST API — Operator: Control
  DELETE /api/intents/{intentId}                   cancel a pending or stale intent
  POST   /api/intents/{intentId}/retry             retry a failed intent
  DELETE /api/sessions/{sessionId}                 delete a recording session
  POST   /api/gc/preview                           dry-run GC report
  POST   /api/gc/run                               execute GC

Data Plane HTTP (bytes only, byte-range capable)
  GET  /content/bundles/{bundleId}/{version}/{path...}                      bundle bytes
  PUT  /content/recordings/{sessionId}/{logicalNodeId}/chunks/{n}           recording chunk upload
```

Relays serve the same `/content/bundles/...` paths from their local cache.

### 8.2 SignalR Hub Methods

**Agent → Master**

| Method | Payload | Purpose |
|---|---|---|
| `Register` | `agentId, segmentId, capabilities[], currentVersions[]` | Called on connect / reconnect. Master rebuilds agent state and replies with the agent's site-config slice. |
| `ReportStatus` | `bundleId, state, version, progressPercent` | Ongoing state updates during transfer; throttle to every 5 % or 2 s. |
| `AckCommand` | `commandId, result, errorDetail?` | Confirms a command was received and acted on. |

**Master → Agent**

| Method | Payload | Purpose |
|---|---|---|
| `ReceiveCommand` | `commandId, action, bundleId?, version?, priority?, sourceUrl?, sessionId?` | Stage / Activate / Rollback / Verify / Abort / UploadRecording / EvictSession |
| `ReceivePublishEvent` | `bundleId, version, manifestPath` | New version available; agent may begin staging if policy allows. |
| `SetFleetSyncMode` | `enabled` | Gate: fleet-scoped jobs eligible or queued. |
| `ConfigUpdate` | `agentSlice` | Push new site-config slice after `POST /api/config/reload`. |
| `SignalSafeWindow` | `bundleId, windowOpen` | Forwarded from app's `POST /api/safe-window` — tells the agent the app's bundle-usage state. |

### 8.3 Reconnection and Intent Reliability

SignalR does not queue messages for disconnected clients. Reliability is handled at the application layer via Intents:

- Every desired action (deploy, activate, upload recording, evict session) is created as a persisted Intent on the master before any SignalR message is sent.
- Intents are stored in the master's JSON snapshot and survive master restarts.
- On agent reconnect, `Register` is called; the master checks all Pending intents for that agent and re-delivers them.
- Agents persist their in-progress work in local SQLite and resume on restart without waiting for re-delivery.
- Agents are idempotent on `intentId`; receiving the same intent twice is harmless.
- Agents use `HubConnectionBuilder.WithAutomaticReconnect()`. No custom retry logic.

**Staleness:** each intent has a `createdAt` timestamp and a per-category `staleAfter` threshold. When an intent ages past its threshold it is flagged `Stale` and a warning is written to the operator message queue. The intent is *not* cancelled or escalated — execution proceeds when the agent reconnects or the safe window opens. The operator may cancel it explicitly via `DELETE /api/intents/{intentId}`.

**Offline agents and recording upload:** when an agent is offline at finalize time, the master creates a Pending intent and retains it. On reconnect, the intent is delivered. The agent reports `Complete`, `NotApplicable` (files no longer exist), or `Failed`. The master tracks collection completeness per session and exposes it via `GET /api/sessions/{sessionId}`.

### 8.4 Agent Presence

- `OnConnectedAsync` → mark agent `Online`; deliver all Pending intents.
- `OnDisconnectedAsync` → mark agent `Unreachable`; write a warning to the operator message queue if a deployment transaction was in progress.

### 8.5 Targeting Individual Agents and Segments

- Unicast: `Clients.Client(connectionId)` — most deployment commands.
- Segment broadcast: agents joined to a SignalR group per `segmentId` on connect via `Groups.AddToGroupAsync(connectionId, segmentId)`; then `Clients.Group(segmentId)`.
- Fleet broadcast: `Clients.All` — `SetFleetSyncMode`, `ReceivePublishEvent`.

---

## 9. NAS Layout and Publish Gate

### 9.1 Directory Structure

```
/NAS/SyncRoot/
  bundles/
    {bundleId}/
      versions/
        v41/                                  immutable once published
        v42/
      manifests/
        v41.json
        v42.json
      latest.json                             points to the current published version
  recordings/
    {sessionId}/
      _session.json
      {logicalNodeId}.zip
  _draft/                                     work in progress, never read by agents
  _publishing/                                transitional state
  policies/
    targeting.json
```

No `blocks/` directory in this revision: with zip-per-version archives, content-addressed dedup is not used.

### 9.2 Publish Gate Protocol

No version is valid until the publish gate is written. The gate eliminates "agent synced a half-written version" bugs.

1. Files are prepared in `_draft/`
2. Manifest and any zip archives are generated; all hashes computed
3. Directory is moved to `versions/{version}/`
4. `published.json` is written **last** — atomically
5. Master is notified via `POST /api/bundles/{bundleId}/versions`
6. Master reacts: validates the bundle is registered (or auto-registers if `?autoRegister=true`), records the new version with its `publishedAt` timestamp, recomputes `DesiredState` across the fleet

The publish gate tool is a small CLI callable from automated build pipelines or human operators. It is the only thing allowed to write `published.json`.

### 9.3 Manifest Format

```json
{
  "bundleId": "TerrainTextures",
  "version": "2026-05-15-001",
  "publishedAt": "2026-05-15T11:00:00Z",
  "groupHash": "sha256:...",
  "container": "zip",
  "files": [
    { "relativePath": "grass/a.dds", "size": 123456, "hash": "sha256:..." }
  ],
  "chunks": [
    { "offset": 0, "size": 67108864, "hash": "sha256:..." }
  ],
  "activation": {
    "mode": "atomic-directory-swap | in-place | cooperative-hot-swap"
  }
}
```

- `version` is a free-form filename-safe string.
- `publishedAt` is the source of truth for "is this newer than that". String comparison of `version` is not used.
- `chunks` is present for the chunked huge-file category; absent otherwise.
- `container` is `zip` for many-small-files bundles, `none` for pre-zipped huge files.

---

## 10. Cascading Data Distribution

The fleet sits behind variable-quality inter-segment links and CAL-limited file servers. Cascading is mandatory from day one.

### 10.1 Distribution Flow

```
NAS  →  Master  →  Relay-A  →  Nodes in Segment A
                →  Relay-B  →  Nodes in Segment B
                →  Relay-C  →  Nodes in Segment C
                →  (Nodes in master's own segment, served directly by master)
```

- NAS → Master: SMB (one session) or local filesystem (if co-located).
- Master → Relay: HTTP, byte-range capable, chunked.
- Relay → Node: HTTP, byte-range capable, chunked.
- In the master's own segment: master is the relay; no separate machine.

### 10.2 Bundle Distribution to a Relay

When a new bundle version is published:

1. Master pulls the bundle into its local pull cache (SMB read, or local copy from NAS).
2. Master sends `ReceiveCommand(action=Stage, bundleId, version, sourceUrl=<master>/content/bundles/...)` to each relay.
3. Relay pulls from the master's data-plane URL into its segment cache. Resumable via HTTP byte-range.
4. Relay reports `ReadyToActivate(bundleId, version)`.
5. Master sends `ReceiveCommand(action=Stage, ...)` to every node in the segment with `sourceUrl=<relay>/content/bundles/...`.
6. Nodes pull from relay over HTTP into local staging.
7. Nodes report `ReadyToActivate`.

### 10.3 Relay Election and Fallback

- Relay election is **static** — declared in site config (`segments[].relayAgentId`).
- If a relay is offline when a bundle needs to ship, nodes in that segment fall back to pulling directly from the master (one additional inter-segment hop per node — accept as an exception).
- Two relays per segment is not designed in.
- Cross-segment failover (pulling from a relay in a neighbouring segment) is not designed in.

### 10.4 Bandwidth and CAL Implications

- NAS sees exactly one SMB session at any time (the master's).
- Inter-segment links each carry every bundle exactly once (master → relay).
- Relays serve HTTP only — no CAL pressure on relay machines.
- For nodes in the master's own segment, the master serves directly over HTTP.

### 10.5 Session-Window Sync

Session-window syncs are small (configs, small datasets) but urgent. The protocol is identical to fleet sync — master → relay → node — but small bundles transit much faster. If master is co-located with NAS, pulling is essentially free (local filesystem).

---

## 11. Agent State Machine

Each agent tracks an explicit state per bundle. States are persisted in local SQLite so the agent can resume after reboot.

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

The orchestrator UI shows per-agent per-bundle state:

```
Agent SIM-03  (segment seg-A, logicalNodes [42, 43])
  RuntimeAssets:    ReadyToActivate v42
  TerrainTextures:  Transferring 67%
  Config-ScenarioA: Active v18
  AITables:         Queued (FleetSyncMode=false)
```

---

## 12. Activation Strategies

### 12.1 Cooperative Hot-Swap (runtime assets, mid-session)

Used when the consuming app is running but not currently using the bundle. The app signals the master over HTTP; the master forwards to the agent over SignalR. There is no local pipe between the app and the agent.

1. Agent: bundle v43 staged and verified. Intent state: `AwaitingSafeWindow`.
2. App: calls `POST /api/safe-window` with `{ logicalNodeId, bundleId, windowOpen: true }`.
3. Master: resolves `logicalNodeId → agentId`. If *all* logical nodes mapped to that agent currently report safe for this bundle, calls `SignalSafeWindow(bundleId, windowOpen=true)` on the agent.
4. Agent: performs atomic directory swap on local active link.
5. Agent: `ReportStatus(bundleId, Active, v43)` on master.
6. App: resumes using the bundle (now pointed at v43).

Safe-window requests have no hard deadline. If the intent ages past `staleAfter`, a warning is written to the operator message queue, but execution is not cancelled.

### 12.2 Atomic Directory Swap

```
C:/App/Data/GroupA/
  active/                                  symlink or junction → current version
  versions/
    v41/
    v42/                                   staged and verified

Activation: repoint active/ → versions/v42
```

Windows symlink/junction behaviour with open file handles must be validated against the consuming app. If open handles block the repoint, fall back to rename-based swap in a maintenance window.

### 12.3 In-Place Activation (huge pre-zipped blobs)

For single huge already-compressed files, activation may be:
- A repoint of `active/` to the new version (if disk allows two copies), or
- An in-place overwrite at a fixed path (with the consuming app guaranteed not to be reading during overwrite).

The manifest's `activation.mode` declares which.

### 12.4 Coordinated Fleet Activation (two-phase)

For group-scoped bundles where all nodes must activate simultaneously:

```
Phase 1 — Prepare:
  Each node: transfer → verify → stage → report ReadyToActivate.

Phase 2 — Commit (when all required nodes are Ready):
  Master sends ReceiveCommand(action=Activate, version=v43) to all nodes.
  Each node activates locally, reports Active(v43).

Failure policy (configurable per DeploymentScope):
  RollbackAll | OperatorIntervention

All failures and warnings are written to the operator message queue
regardless of which policy is chosen.
```

---

## 13. Transfer Engines

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
| `DirectHttp` | Standard zip-per-version bundles | Single huge files | Streaming HTTP download with byte-range resume; per-file hash verification after unzip |
| `ChunkedHugeFile` | Pre-zipped 0.5 TB blobs, multi-GB recordings | Many small files | 64 MB chunks with per-chunk SHA-256 sidecar; resume from last verified chunk |
| `MasterSmbStage` | NAS → master pull only | Inter-machine transfer | Internal to the master; uses SMB or local filesystem |

Robocopy is not part of the transfer engine set in this revision. It may be used internally by `MasterSmbStage` if the operator prefers, but is an implementation detail invisible to the rest of the system. The pluggable interface accommodates other engines later (relay/cascade variants, BITS) without architectural change.

---

## 14. Desync Detection

### 14.1 Normal vs Error States

| Condition | Classification | Action |
|---|---|---|
| Agent version < latest published version | Normal | Queue sync intent per policy |
| All group agents on same version as expected | OK | No action |
| Group agents disagree on active version | **Error: DeploymentInconsistent** | Alert operator; block session start |
| Agent reports version X but file hashes mismatch | **Error: Corrupt** | Mark Corrupt; trigger re-sync |
| Agent silent / SignalR disconnect | **Error: NodeUnreachable** | Mark Unreachable; write warning to operator message queue |

### 14.2 Verification Levels

- **Level 1 — Manifest version equality** — fastest; routine monitoring.
- **Level 2 — File metadata** — size, mtime, file count. Medium confidence.
- **Level 3 — Chunk hash** — per-chunk SHA-256 from the manifest. Routine verification for huge files; enables partial re-transfer.
- **Level 4 — Full file hash** — per-file SHA-256. Post-transfer for non-huge bundles.
- **Level 5 — Group Merkle hash** — single hash over an entire bundle. Fast fleet-wide equality check.
- **Level 6 — Whole-file hash of huge files** — expensive (≈ 30 min for 500 GB). On-demand only after suspected corruption.

---

## 15. Local Storage Layout (per agent)

```
C:/ProgramData/YourAppSync/
  agent.db                                  SQLite: jobs, states, verification results,
                                            activation history, pending intents
  manifests/
    {bundleId}/
      {version}.json
  staging/
    {bundleId}/{version}/
  versions/
    {bundleId}/
      v41/
      v42/
  active/                                   symlinks/junctions to current active versions
    {bundleId}/  →  versions/{bundleId}/v42
  recordings/
    {sessionId}/                            extracted recording files (LRU-managed)
      ...
  agent-config-cache.json                   site-config slice cached for restart resilience
  logs/

Agent SQLite tables:
  AgentSyncState, TransferJobs, InstalledVersions,
  VerificationResults, BundleCache, ActivationHistory,
  FailedFiles, PendingIntents
```

Recordings on the agent are kept extracted (no permanent local zip archive). A temporary zip is created during upload and deleted on success.

---

## 16. Recordings Protocol

Recordings flow nodes → NAS. Architecturally distinct from downward distribution.

### 16.1 Identification

- `sessionId` — 128-bit GUID, app-generated.
- `logicalNodeId` — integer; identifies one node's contribution to the session.
- Per-recording-file sidecar files (the app's own metadata) are **opaque payload** to sync — the sync system does not read them; they ride along with the recording data.

### 16.2 NAS Layout

```
/NAS/Recordings/
  {sessionId}/
    _session.json                           sync-side marker, written by master at finalize
    {logicalNodeId}.zip                     one per per-node contribution
    ...
```

One zip per per-node contribution, containing all of that node's recording files plus their per-file sidecars. The NAS never decompresses; the zip remains the canonical NAS form.

### 16.3 Sync-Side Session Marker

`_session.json` is the authoritative on-NAS record that a session is complete (independent of master in-memory state):

```json
{
  "sessionId": "5b2f...",
  "finalizedAt": "2026-05-17T10:20:49Z",
  "participatingNodes": [42, 43, 44],
  "status": "complete",
  "missingNodes": []
}
```

Written by the master on collection-finalize. No other writer.

### 16.4 Upload Flow

1. App calls `POST /api/recordings` with `{ sessionId, logicalNodeId, files: [{ path, size }, ...] }`. The body declares the on-disk paths for one node's recording files. Returns `intentId`.
2. Master creates a Pending upload intent. Sends `ReceiveCommand(action=UploadRecording, ...)` via SignalR when the agent is online.
3. Agent reads from the app-supplied paths (no scanning). Creates a temporary local zip containing all files plus their per-file sidecars. Compression: deflate Optimal (data is highly compressible, typically ~10×).
4. Agent chunk-uploads the zip to the master via `PUT /content/recordings/{sessionId}/{logicalNodeId}/chunks/{n}`. Chunk size 64 MB, per-chunk SHA-256.
5. Master streams chunks to NAS staging. On the final chunk, master moves staging → final NAS location at `/NAS/Recordings/{sessionId}/{logicalNodeId}.zip`.
6. Agent reports `AckCommand(intentId, Complete)`. Local temp zip is deleted.

The agent never scans directories. All paths come from the app.

### 16.5 Session Finalize

1. App calls `POST /api/sessions/{sessionId}/finalize` when it considers the session done.
2. Master checks all expected nodes' upload intents.
3. If all `Complete`, master writes `_session.json` with `status: "complete"` and returns 200.
4. If some still pending, master returns 409 with the missing-node list. `?force=true` writes `status: "partial"`, lists missing nodes, and proceeds.

### 16.6 Listing and Deletion

- `GET /api/sessions?since=...&until=...` — list with status
- `GET /api/sessions/{sessionId}` — detail
- `DELETE /api/sessions/{sessionId}` — deletes the NAS folder; notifies any agents holding extracted copies to evict via `ReceiveCommand(action=EvictSession, sessionId)`

### 16.7 Recordings GC

- **On the agent**: extracted recordings are LRU-evicted when free disk drops below the configured watermark. Default retention: "last N sessions". Configurable.
- **On the NAS**: kept indefinitely by default. Operator-triggered deletion only.

### 16.8 Replay From NAS

When an agent wants to replay a session not currently extracted locally, it downloads the per-node zip from the master (which streams it from the NAS pull cache, or fetches afresh) over HTTP and extracts locally. Once extracted, the recording participates in the LRU cache.

NAS recordings are never decompressed by the NAS; the NAS is a pure store.

---

## 17. Site Configuration

A single canonical JSON file on the master holds site-wide configuration. The master derives a per-agent slice and pushes it via SignalR.

### 17.1 Canonical Schema

```json
{
  "topology": {
    "master":   { "agentId": "MASTER-01", "dataPlaneUrl": "http://10.0.0.1:8080" },
    "nas":      { "uncPath": "\\\\nas\\sync", "localPath": "D:/sync" },
    "segments": [
      { "segmentId": "seg-A", "relayAgentId": "REL-A1" }
    ],
    "agents": [
      { "agentId": "SIM-03", "hostname": "sim03.local",
        "segmentId": "seg-A",
        "capabilities": ["render", "physics"],
        "isRelay": false,
        "logicalNodeIds": [42, 43] }
    ]
  },
  "bundles": {
    "ensured": [
      { "bundleId": "GlobalAITables", "dataCategory": "Dataset",
        "defaultScope": { "type": "Fleet" },
        "retentionCount": 5, "staleAfter": "24h" }
    ]
  },
  "categoryDefaults": {
    "RuntimeAsset":    { "chunkSize": "64MB", "verifyMode": "ChunkHash" },
    "ChunkedHugeFile": { "chunkSize": "64MB", "verifyMode": "ChunkHash" },
    "Config":          { "verifyMode": "FullHash", "staleAfter": "1h" },
    "Dataset":         { "verifyMode": "FullHash" },
    "Recording":       { "chunkSize": "64MB", "compressionLevel": "Optimal" }
  },
  "operational": {
    "fleetSyncWindow":      "01:00-05:00",
    "diskWatermarkPercent": 10,
    "sessionQueueDepth":    5,
    "agentRetention": {
      "bundles":    { "keepActiveAndPrevious": true, "keepLastN": 2 },
      "recordings": { "keepLastNSessions": 5 }
    }
  },
  "appSettings": {
    "_comment": "free-form pass-through to consuming app via the agent"
  }
}
```

### 17.2 Distribution and Reload

- **Editing** — operator edits the JSON file on the master. May be tracked in git for change history; this is not a system feature.
- **Reload** — operator calls `POST /api/config/reload`. Master re-reads the file, computes diffs, pushes `ConfigUpdate(agentSlice)` to affected agents via SignalR.
- **Slice per agent** — each agent's slice contains its own agent record, segment info, NAS or relay pull URL, relevant `bundles.ensured` entries, the category defaults it needs, and the entire `appSettings` block.
- **Agent cache** — agent persists its current slice to `agent-config-cache.json` so it can start cold without master availability.

### 17.3 Bundles via API vs Bundles in Config

Bundles are primarily managed via REST API at runtime. The `bundles.ensured` section of site config is for bundles that must always exist (operationally critical, baseline content). On reload, the master:
- Creates any `ensured` bundle that does not already exist.
- Validates that any `ensured` bundle that already exists matches the declared shape; logs a warning to the operator message queue if it does not, and ignores the config-declared shape (operator resolves explicitly).

---

## 18. Garbage Collection

Three independent GC scopes: agent-local, master cache, NAS.

### 18.1 Agent-Local

For each bundle, the agent keeps:
- The currently `Active` version
- The immediately previous version (rollback safety)
- Any version currently in `{Staged, ReadyToActivate, ActivationPending}`
- Additionally, up to `keepLastN - 2` recent unreferenced versions (from `operational.agentRetention`)

When free disk drops below `diskWatermarkPercent`, the agent evicts unreferenced versions LRU until above the watermark.

For recordings, the agent keeps the last N sessions (LRU) with the same watermark-based eviction.

### 18.2 Master Pull Cache

- Keep all currently published versions of every bundle.
- Keep all versions currently in transit to any relay.
- Evict the rest LRU when free disk drops below the master's watermark.

### 18.3 NAS

Per-bundle retention:
- Keep the version `latest.json` points to.
- Keep any version reported `Active` by any agent (master holds fleet state).
- Keep the last N published versions per bundle (`retentionCount` from the bundle definition).
- Delete the rest.

Recording sessions on the NAS are kept indefinitely. Operator-triggered deletion only.

GC runs as a scheduled job on the master with a dry-run mode: `POST /api/gc/preview` produces a report; `POST /api/gc/run` executes deletions. Same pattern as docker registry GC, nix store GC, restic prune: keep what's referenced, plus N most recent of the rest.

---

## 19. Orchestrator State

At up to ~200 nodes the orchestrator maintains state in-memory with periodic JSON snapshot. Agents report their full state on connect and on change; the orchestrator reconstructs its view from those reports after restart.

```
Master in-memory model:
  Fleet:                Dictionary<agentId, Agent>
  LogicalNodeMap:       Dictionary<logicalNodeId, agentId>
  Membership:           Dictionary<agentId, groupId?>
  Bundles:              Dictionary<bundleId, BundleDefinition>
  PublishedVersions:    Dictionary<bundleId, List<Version>>
  DesiredState:         Dictionary<(agentId, bundleId), DesiredVersion>
  ActiveTransactions:   Dictionary<transactionId, DeploymentTransaction>
  Intents:              Dictionary<intentId, Intent>
  Sessions:             Dictionary<sessionId, RecordingSession>
  MessageQueue:         List<OperatorMessage>

Intent lifecycle:
  Pending → Executing → Complete
                      → Failed     (written to MessageQueue)
                      → Stale      (warning written to MessageQueue; still executes)
                      → Cancelled  (operator DELETE /api/intents/{intentId})

Snapshot: periodic JSON write to disk (~60s).
          Bundles, Intents, Sessions, MessageQueue all included.
Rebuild:  on startup, agents reconnect and call Register() with full
          current state; master reconstructs fleet view from those reports.
          Pending intents and pending session-upload intents are re-delivered.
```

---

## 20. C# Interface Summary (.NET 8)

```csharp
// Planning
public interface ISyncPlanner {
    Task<IReadOnlyList<Intent>> PlanAsync(
        Agent agent, DesiredState desired, CancellationToken ct);
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
        StagedBundle staged, ActivationContext ctx, CancellationToken ct);
}

// Intent store (master-side, persisted in JSON snapshot)
public interface IIntentRepository {
    Task<Intent> CreateAsync(IntentRequest request, CancellationToken ct);
    Task<Intent?> GetAsync(string intentId, CancellationToken ct);
    Task<IReadOnlyList<Intent>> GetPendingForAgentAsync(string agentId, CancellationToken ct);
    Task UpdateStateAsync(string intentId, IntentState state, CancellationToken ct);
    Task CancelAsync(string intentId, CancellationToken ct);
}

// Bundle registry (master-side, persisted in JSON snapshot)
public interface IBundleRegistry {
    Task<BundleDefinition> RegisterAsync(BundleDefinition definition, CancellationToken ct);
    Task<BundleDefinition?> GetAsync(string bundleId, CancellationToken ct);
    Task<IReadOnlyList<BundleDefinition>> ListAsync(CancellationToken ct);
    Task UpdateAsync(string bundleId, BundleDefinitionPatch patch, CancellationToken ct);
    Task DeleteAsync(string bundleId, CancellationToken ct);
}

// Recording session tracking
public interface ISessionRepository {
    Task<RecordingSession> CreateAsync(Guid sessionId, IReadOnlyList<int> expectedNodes, CancellationToken ct);
    Task<RecordingSession?> GetAsync(Guid sessionId, CancellationToken ct);
    Task MarkNodeCompleteAsync(Guid sessionId, int logicalNodeId, CancellationToken ct);
    Task FinalizeAsync(Guid sessionId, bool force, CancellationToken ct);
}

// Site config
public interface ISiteConfigStore {
    Task<SiteConfig> LoadAsync(CancellationToken ct);
    Task ReloadAsync(CancellationToken ct);
    AgentSlice ComputeSlice(string agentId);
}

// Operator message queue
public interface IOperatorMessageQueue {
    Task EnqueueAsync(OperatorMessage message, CancellationToken ct);
    Task<IReadOnlyList<OperatorMessage>> GetAsync(MessageFilter filter, CancellationToken ct);
    Task DismissAsync(string messageId, CancellationToken ct);
}
```

---

## 21. Recommended Build Sequence

Each phase is independently useful and testable.

**Phase 1 — Schema and publish gate.** Define the bundle manifest schema covering all four downward categories. Implement the publish gate CLI. Implement the NAS directory layout. No transfer code yet.

**Phase 2 — Master skeleton and agent state machine.** Implement the agent state machine with SQLite persistence. Stub transfer engine (direct HTTP from a static file server). Wire up SignalR hub (`Register`, `ReportStatus`, `ReceiveCommand`). Implement the bundle registry (`POST /api/bundles`) and the orchestrator monitoring UI. Fleet visibility before any transfer complexity.

**Phase 3 — Site config and identity mapping.** Implement the site-config JSON file, reload endpoint, slice computation, and `ConfigUpdate` SignalR push. Implement two-tier identity (`agentId` ↔ `logicalNodeIds`) and `POST /api/membership`. Operator UI shows segment topology.

**Phase 4 — Direct HTTP transfer (single-segment).** Implement `DirectHttp` engine with byte-range and zip extraction. Master pulls from NAS into pull cache and serves over `/content/bundles/...`. Single-segment fleet (master-segment only) end-to-end.

**Phase 5 — Cascading via segment relays.** Designate relays in site config. Master fans out to relays; relays serve nodes. Add relay-cache GC. Validate CAL counts.

**Phase 6 — Cooperative safe-window.** Implement `POST /api/safe-window` and the master's logical-node → agent fan-in with AND semantics. Implement `SignalSafeWindow` on the agent. Atomic directory swap activation.

**Phase 7 — Chunked huge-file transfer.** Implement `ChunkedHugeFile` engine with per-chunk hashes and resume from last verified chunk. Apply to large pre-zipped blobs.

**Phase 8 — Recordings upload.** Implement `POST /api/recordings`, agent temp-zip + chunked upload, master chunk-receive + NAS write, `POST /api/sessions/{id}/finalize`, `_session.json` writer, session listing and deletion APIs.

**Phase 9 — Coordinated deployment (two-phase commit).** Prepare/Commit state machine for group-scoped bundles. Rollback. Session-ready gating.

**Phase 10 — Fleet sync scheduler.** Overnight fleet sync window with `FleetSyncMode` gate.

**Phase 11 — Garbage collection.** Node-local, master-cache, and NAS GC with dry-run and run endpoints.

---

## 22. Resolved Design Decisions

Summarised for traceability.

- **Recordings upload — collection tracking**: master creates an upload intent per node, tracks completeness per session, exposes via `GET /api/sessions`. Offline nodes receive their intent on reconnect and evaluate feasibility locally.
- **Safe-window policy**: infinite deadline. The intent waits with no hard expiry. A per-category `staleAfter` threshold generates a warning in the operator message queue; does not cancel.
- **Failure policy**: `RollbackAll` or `OperatorIntervention` — no `ContinueDegraded`. All failures and warnings are written to the operator message queue.
- **Master restart resilience**: intents and bundle definitions are persisted in the master's JSON snapshot; agent state in SQLite. Both sides resume independently. On reconnect, master re-delivers Pending intents.
- **Packaging strategy**: zip per bundle per version. `CompressionLevel.NoCompression` for already-compressed file types; `Optimal` for text content. No content-addressed block scheme; no cross-version dedup.
- **Versioning**: `version` is a free-form filename-safe string. `publishedAt` timestamp is the source of truth for ordering. Per-bundle version schemes may be SVN revisions, git short SHAs, date stamps, or sequential integers.
- **Windows symlink/junction behaviour**: to be validated against the consuming app. If open handles block junction repoint, fall back to rename-based swap.
- **Master as data plane gateway**: master is the only SMB client. All other inter-machine file transfer is HTTP. Master is co-located with the NAS in the typical case; design supports remote NAS.
- **Identity**: `agentId` and `logicalNodeId` are two tiers. API surface accepts `logicalNodeId`; master resolves to `agentId` for SignalR dispatch.
- **Recordings on NAS**: stored as one zip per per-node contribution; NAS never decompresses; agents extract on replay.
- **Bundle registry**: dynamic via REST API; site config may declare `ensured` bundles that must always be present.
- **Security, master HA, executable updates**: explicitly out of scope for this revision.
