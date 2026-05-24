# Sync System API Specification

*Companion to the Architecture Design Document, Revision 2*
*ASP.NET Core · .NET 8 · May 2026*

This document specifies the REST and data-plane HTTP API surface of the master. SignalR hub methods are documented in §8.2 of the architecture doc and not duplicated here.

---

## 1. Conventions

### 1.1 Base URL and Versioning

All control-plane endpoints live under `/api/...`. All data-plane endpoints under `/content/...`. No URL versioning in this revision — the API is treated as a single in-house surface.

### 1.2 Content Type

All control-plane requests and responses use `application/json; charset=utf-8` unless otherwise stated. Data-plane endpoints use `application/octet-stream`.

### 1.3 Error Format

All errors return RFC 7807 problem details:

```json
{
  "type":     "https://sync/errors/bundle-not-registered",
  "title":    "Bundle not registered",
  "status":   404,
  "detail":   "No bundle with id 'TerrainTextures' exists. Register via POST /api/bundles first, or pass ?autoRegister=true.",
  "instance": "/api/bundles/TerrainTextures/versions"
}
```

`Content-Type: application/problem+json` on error responses.

### 1.4 Idempotency

Three patterns:

- **Client-supplied intent id**: `POST /api/deploy` and `POST /api/recordings` accept an `intentId` (UUID) in the body. Repeating the call with the same id returns the existing intent. This is the primary idempotency mechanism for actions that create work.
- **Natural keys**: `POST /api/bundles/{bundleId}/versions` is idempotent on `(bundleId, version)`. Re-publishing returns the existing version record.
- **State-target endpoints**: `POST /api/membership`, `POST /api/fleet-sync-mode` set state; repeated calls converge on the requested state.

### 1.5 Long-Running Operations

Every mutating endpoint that creates work returns *immediately* with an `intentId` and HTTP 202. Status is observed via:

- Polling `GET /api/intents/{intentId}`
- Or via SignalR push for connected operator UIs (not specified in this REST document)

### 1.6 Pagination

List endpoints use cursor-based pagination:

```
GET /api/intents?cursor=eyJpZCI6...&limit=50
```

Response:

```json
{
  "items": [ /* ... */ ],
  "nextCursor": "eyJpZCI6..."     // null when no more
}
```

Default `limit` is 50, max 500.

### 1.7 Identity in API

Most endpoints accept `logicalNodeId` (app-facing identity, integer). Operator endpoints generally use `agentId` (machine-level identity, string). A few endpoints accept either — documented per endpoint.

When the master resolves `logicalNodeId → agentId` and finds no mapping in site config, it returns 404 with detail `"Unknown logicalNodeId"`.

### 1.8 Standard Status Codes

- `200 OK` — synchronous success
- `201 Created` — resource created
- `202 Accepted` — work accepted, intent created
- `204 No Content` — success, no body
- `400 Bad Request` — malformed request
- `404 Not Found` — referenced resource (bundle, agent, session, intent) does not exist
- `409 Conflict` — state precondition not met (already exists, still in use, partial completion required)
- `500 Internal Server Error` — master fault

### 1.9 Security

No authentication or authorization in this revision. All endpoints open. Operators are expected to deploy on a trusted network segment.

---

## 2. Bundle Registry

### 2.1 `POST /api/bundles` — Register a Bundle

Register a new bundle definition.

Request:
```json
{
  "bundleId":       "TerrainTextures",
  "dataCategory":   "RuntimeAsset",
  "defaultScope":   { "type": "Capability", "capabilityFilter": "render" },
  "activationMode": "atomic-directory-swap",
  "retentionCount": 3,
  "staleAfter":     "24h",
  "chunkSize":      "64MB"
}
```

`dataCategory`: `RuntimeAsset | Config | Dataset | ChunkedHugeFile`
`activationMode`: `atomic-directory-swap | in-place | cooperative-hot-swap`
`staleAfter`: duration string (`24h`, `30m`, `7d`)
`chunkSize`: only meaningful for `ChunkedHugeFile`

Response (`201 Created`):
```json
{
  "bundleId":     "TerrainTextures",
  "dataCategory": "RuntimeAsset",
  "defaultScope": { /* ... */ },
  "createdAt":    "2026-05-17T10:00:00Z",
  "versions":     []
}
```

Errors:
- `409` — bundle with that id already exists

### 2.2 `GET /api/bundles` — List Bundles

```
GET /api/bundles?cursor=...&limit=50
```

Response:
```json
{
  "items": [
    {
      "bundleId":         "TerrainTextures",
      "dataCategory":     "RuntimeAsset",
      "latestVersion":    "v42",
      "latestPublishedAt": "2026-05-15T11:00:00Z",
      "versionCount":     3
    }
  ],
  "nextCursor": null
}
```

### 2.3 `GET /api/bundles/{bundleId}` — Bundle Detail

Response:
```json
{
  "bundleId":       "TerrainTextures",
  "dataCategory":   "RuntimeAsset",
  "defaultScope":   { "type": "Capability", "capabilityFilter": "render" },
  "activationMode": "atomic-directory-swap",
  "retentionCount": 3,
  "staleAfter":     "24h",
  "createdAt":      "2026-05-01T08:00:00Z",
  "versions": [
    { "version": "v40", "publishedAt": "2026-05-01T08:30:00Z", "groupHash": "sha256:..." },
    { "version": "v41", "publishedAt": "2026-05-08T09:00:00Z", "groupHash": "sha256:..." },
    { "version": "v42", "publishedAt": "2026-05-15T11:00:00Z", "groupHash": "sha256:..." }
  ],
  "latest": "v42"
}
```

Errors:
- `404` — bundle not found

### 2.4 `PUT /api/bundles/{bundleId}` — Update Bundle Metadata

Update mutable fields. `bundleId` and `dataCategory` are immutable after creation.

Request (any subset of mutable fields):
```json
{
  "defaultScope":   { "type": "Fleet" },
  "retentionCount": 5,
  "staleAfter":     "48h"
}
```

Response: `200 OK` with the updated bundle detail (same shape as 2.3).

Errors:
- `400` — attempted to change immutable field
- `404` — bundle not found

### 2.5 `DELETE /api/bundles/{bundleId}` — Deregister Bundle

Removes the bundle from the registry. Refused if any version is currently `Active` on any agent.

Response: `204 No Content`.

Errors:
- `404` — bundle not found
- `409` — bundle has Active versions; body lists which agents

---

## 3. Publishing

### 3.1 `POST /api/bundles/{bundleId}/versions` — Publish a Version

Called by the publish CLI after writing files and the publish gate on the NAS. Tells the master a new version exists.

Request:
```json
{
  "version":      "v43",
  "manifestPath": "bundles/TerrainTextures/manifests/v43.json",
  "publishedAt":  "2026-05-17T12:00:00Z"
}
```

`manifestPath` is relative to the NAS sync root. The master reads the manifest, validates it, and records the version.

Query parameters:
- `?autoRegister=true` — create the bundle if it does not exist, using defaults inferred from the manifest's data category. Default: false.

Response (`201 Created`):
```json
{
  "bundleId":    "TerrainTextures",
  "version":     "v43",
  "publishedAt": "2026-05-17T12:00:00Z",
  "groupHash":   "sha256:...",
  "fileCount":   1247,
  "totalBytes":  524288000
}
```

Side effects: master pulls the bundle into its pull cache (asynchronously), recomputes `DesiredState` across the fleet, dispatches stage commands to relays for any segment that should receive this bundle.

Errors:
- `404` — bundle not registered (and `autoRegister` not set)
- `400` — manifest invalid or unreadable
- `409` — version already published (idempotent: returns existing record with 200)

---

## 4. Deployment

### 4.1 `POST /api/deploy` — Request a Bundle Be Active

Create an Intent that the named bundle version be active on the resolved target set.

Request:
```json
{
  "intentId":  "11111111-1111-1111-1111-111111111111",
  "bundleId":  "TerrainTextures",
  "version":   "v42",
  "target":    { "type": "LogicalNode", "logicalNodeIds": [42, 43] },
  "priority":  "Normal",
  "deadline":  "2026-05-17T20:00:00Z"
}
```

`intentId`: client-generated UUID. Acts as the idempotency key.
`target.type`: one of:
- `Fleet` — no further fields
- `Group` — `groupId` required
- `Capability` — `capabilityFilter` required (string match against agent capabilities)
- `LogicalNode` — `logicalNodeIds` array required
- `Agent` — `agentIds` array required
`priority`: `Critical | High | Normal | Background`. Default `Normal`.
`deadline`: optional ISO8601. Past the deadline, the intent is flagged Stale.

Response (`202 Accepted`):
```json
{
  "intentId":       "11111111-1111-1111-1111-111111111111",
  "state":          "Pending",
  "createdAt":      "2026-05-17T10:00:00Z",
  "resolvedAgents": ["SIM-03", "SIM-04"],
  "deadline":       "2026-05-17T20:00:00Z"
}
```

`resolvedAgents` is the set of agentIds the target resolved to at intent creation. Membership changes after this point do not retroactively expand the intent.

Side effects: one sub-intent per resolved agent. SignalR commands dispatched to online agents; queued on master for offline agents until reconnect.

Errors:
- `404` — bundle or version not found, or all logical nodes unknown
- `400` — invalid target shape
- `409` — `intentId` already exists with a different request body (idempotency violation; 200 if same body)

---

## 5. Intent Management

### 5.1 `GET /api/intents` — List Intents

```
GET /api/intents?state=Pending&agentId=SIM-03&bundleId=TerrainTextures&since=...&cursor=...&limit=50
```

Filters (all optional, combinable):
- `state`: `Pending | Executing | Complete | Failed | Stale | Cancelled`
- `agentId`
- `logicalNodeId`
- `bundleId`
- `sessionId` (for upload intents)
- `since`: ISO8601, filters by `createdAt`

Response:
```json
{
  "items": [
    {
      "intentId":     "11111111-...",
      "kind":         "Deploy",
      "agentId":      "SIM-03",
      "bundleId":     "TerrainTextures",
      "version":      "v42",
      "state":        "Executing",
      "progressPct":  67,
      "createdAt":    "2026-05-17T10:00:00Z",
      "updatedAt":    "2026-05-17T10:05:00Z"
    }
  ],
  "nextCursor": null
}
```

`kind`: `Deploy | Activate | Rollback | Verify | UploadRecording | EvictSession`

### 5.2 `GET /api/intents/{intentId}` — Intent Detail

Response:
```json
{
  "intentId":   "11111111-...",
  "kind":       "Deploy",
  "agentId":    "SIM-03",
  "bundleId":   "TerrainTextures",
  "version":    "v42",
  "state":      "Executing",
  "progressPct": 67,
  "createdAt":  "2026-05-17T10:00:00Z",
  "updatedAt":  "2026-05-17T10:05:00Z",
  "deadline":   "2026-05-17T20:00:00Z",
  "history": [
    { "at": "2026-05-17T10:00:00Z", "state": "Pending" },
    { "at": "2026-05-17T10:01:00Z", "state": "Executing", "note": "Transfer started from relay REL-A1" },
    { "at": "2026-05-17T10:05:00Z", "state": "Executing", "note": "Progress 67%" }
  ],
  "parentIntentId": "00000000-..."    // present if this is a sub-intent of a multi-target deploy
}
```

Errors:
- `404` — intent not found

### 5.3 `DELETE /api/intents/{intentId}` — Cancel an Intent

Cancel a Pending or Stale intent. Executing intents are *not* cancellable mid-flight; the body explains why.

Response: `204 No Content` on success.

Errors:
- `404` — intent not found
- `409` — intent in `Executing | Complete | Failed | Cancelled` state, cannot cancel

### 5.4 `POST /api/intents/{intentId}/retry` — Retry a Failed Intent

Resets a Failed intent to Pending. Master re-dispatches.

Response: `200 OK` with the intent (state now Pending).

Errors:
- `404` — intent not found
- `409` — intent not in Failed state

---

## 6. Membership and Fleet Mode

### 6.1 `POST /api/membership` — Set Group Membership

Called by the session manager when an agent joins or leaves a group. Membership is set at the agent level.

Request (one of `agentId` or `logicalNodeId` required):
```json
{
  "agentId":       "SIM-03",
  "logicalNodeId": 42,
  "groupId":       "session-2026-05-17-A"
}
```

To clear membership, set `groupId: null`.

Response: `200 OK`:
```json
{
  "agentId":          "SIM-03",
  "previousGroupId":  null,
  "currentGroupId":   "session-2026-05-17-A",
  "triggeredIntents": ["aaaa-...", "bbbb-..."]
}
```

`triggeredIntents` is the set of intents created by `DesiredState` recomputation (any bundles the agent is now missing).

Errors:
- `404` — unknown agent or logical node
- `400` — both `agentId` and `logicalNodeId` provided and inconsistent
- `409` — agent in another group, body lists current group (caller must clear first)

### 6.2 `POST /api/fleet-sync-mode` — Enable / Disable Fleet Sync Window

Request:
```json
{ "enabled": true }
```

Response: `200 OK`:
```json
{
  "enabled":      true,
  "changedAt":    "2026-05-17T22:00:00Z",
  "pendingFleetIntents": 47
}
```

Side effects: when set to `true`, queued fleet-scoped intents become eligible for dispatch. When set to `false`, any in-flight transfers complete; no new fleet-scoped commands issued.

Errors:
- `409` — attempted to enable while groups still active; body lists groups

---

## 7. Recordings

### 7.1 `POST /api/recordings` — Declare a Per-Node Recording Ready

Called by the consuming app once per node per session to declare that this node has a recording ready for upload.

Request:
```json
{
  "intentId":      "22222222-2222-2222-2222-222222222222",
  "sessionId":     "5b2f...",
  "logicalNodeId": 42,
  "files": [
    { "path": "C:/AppData/Recordings/5b2f/node42/main.dat",     "size": 12884901888 },
    { "path": "C:/AppData/Recordings/5b2f/node42/main.sidecar", "size":         2048 }
  ]
}
```

Paths are absolute on the agent's filesystem. The agent will read directly from them — no directory scanning.

Response (`202 Accepted`):
```json
{
  "intentId":   "22222222-...",
  "agentId":    "SIM-03",
  "sessionId":  "5b2f...",
  "logicalNodeId": 42,
  "state":      "Pending",
  "createdAt":  "2026-05-17T10:30:00Z"
}
```

Side effects:
- Creates an `UploadRecording` intent.
- If session does not exist in master state, creates a session record with `expectedNodes = [42]` (additional nodes added as each posts).
- Dispatches `ReceiveCommand(action=UploadRecording, ...)` to the agent over SignalR.

Errors:
- `404` — unknown logical node
- `409` — intent id collision with different body
- `400` — empty files list, or path that does not look absolute

The agent uploads via the data-plane endpoints (§13).

---

## 8. Sessions

### 8.1 `GET /api/sessions` — List Sessions

```
GET /api/sessions?status=complete&since=2026-05-01&until=2026-05-31&cursor=...&limit=50
```

Filters:
- `status`: `pending | complete | partial`
- `since`, `until`: ISO8601 against `createdAt`

Response:
```json
{
  "items": [
    {
      "sessionId":          "5b2f...",
      "status":             "complete",
      "createdAt":          "2026-05-17T10:30:00Z",
      "finalizedAt":        "2026-05-17T10:45:00Z",
      "participatingNodes": [42, 43, 44],
      "missingNodes":       [],
      "totalBytes":         42949672960
    }
  ],
  "nextCursor": null
}
```

### 8.2 `GET /api/sessions/{sessionId}` — Session Detail

Response:
```json
{
  "sessionId":   "5b2f...",
  "status":     "pending",
  "createdAt":  "2026-05-17T10:30:00Z",
  "finalizedAt": null,
  "expectedNodes":      [42, 43, 44],
  "completedNodes":     [42, 43],
  "pendingNodes":       [44],
  "uploadIntents": [
    { "intentId": "22222222-...", "logicalNodeId": 42, "state": "Complete",  "bytesUploaded": 12884903936 },
    { "intentId": "33333333-...", "logicalNodeId": 43, "state": "Complete",  "bytesUploaded": 11811160064 },
    { "intentId": "44444444-...", "logicalNodeId": 44, "state": "Executing", "bytesUploaded": 1073741824, "progressPct": 8 }
  ]
}
```

Errors:
- `404` — session not found

### 8.3 `POST /api/sessions/{sessionId}/finalize` — Mark Session Collection-Complete

Called by the app when the session is done from its side. Master verifies all expected nodes have completed their uploads.

Request body (optional):
```json
{
  "expectedNodes": [42, 43, 44]
}
```

If `expectedNodes` is supplied, the master uses it as the authoritative set (potentially including nodes that have not yet posted recordings — they will be missing). If omitted, the master uses the union of nodes that have posted via `POST /api/recordings`.

Query parameters:
- `?force=true` — finalize even if some expected nodes are incomplete; result is `status: "partial"`.

Response (`200 OK`):
```json
{
  "sessionId":     "5b2f...",
  "status":        "complete",
  "finalizedAt":   "2026-05-17T10:45:00Z",
  "missingNodes":  [],
  "sessionMarker": "/NAS/Recordings/5b2f/_session.json"
}
```

Side effects: master writes `_session.json` to NAS at the recorded location.

Errors:
- `404` — session not found
- `409` — some expected nodes still pending and `force` not set; body lists `missingNodes` and `pendingIntents`

### 8.4 `DELETE /api/sessions/{sessionId}` — Delete a Session

Deletes the session folder on the NAS and notifies all agents holding extracted local copies to evict.

Response: `204 No Content`.

Side effects:
- Removes `/NAS/Recordings/{sessionId}/` and all contents.
- Creates an `EvictSession` intent for each agent known to hold an extracted copy.

Errors:
- `404` — session not found
- `409` — session still has pending upload intents (cancel them first)

---

## 9. Safe Window

### 9.1 `POST /api/safe-window` — App Signals Safe-Window State

The consuming app declares whether it is currently using a bundle on a given logical node.

Request:
```json
{
  "logicalNodeId": 42,
  "bundleId":      "TerrainTextures",
  "windowOpen":    true
}
```

Response: `200 OK`:
```json
{
  "logicalNodeId":   42,
  "bundleId":        "TerrainTextures",
  "windowOpen":      true,
  "agentId":         "SIM-03",
  "agentSwapEligible": false,
  "agentSwapEligibleReason": "Other logical nodes on SIM-03 (43) have windowOpen=false"
}
```

`agentSwapEligible` is `true` only when *all* logical nodes mapped to the agent currently report `windowOpen: true` for the named bundle.

Side effects: master updates its per-(logicalNode, bundle) safe-window state. If `agentSwapEligible` flips to `true` and the agent has a `ReadyToActivate` intent for this bundle in `AwaitingSafeWindow`, master sends `SignalSafeWindow(bundleId, windowOpen=true)` to the agent.

Errors:
- `404` — unknown logical node, or no bundle definition with that id
- `400` — malformed body

---

## 10. Status

### 10.1 `GET /api/status` — Full Fleet State

Heavy endpoint; intended for the operator UI's initial load.

Response:
```json
{
  "fleetSyncMode":  false,
  "asOf":           "2026-05-17T10:00:00Z",
  "segments": [
    {
      "segmentId":      "seg-A",
      "relayAgentId":   "REL-A1",
      "relayState":     "Online",
      "agentCount":     20
    }
  ],
  "agents": [
    {
      "agentId":         "SIM-03",
      "segmentId":       "seg-A",
      "logicalNodeIds":  [42, 43],
      "capabilities":    ["render", "physics"],
      "presence":        "Online",
      "currentGroupId":  "session-2026-05-17-A",
      "lastSeen":        "2026-05-17T10:00:00Z",
      "bundles": {
        "TerrainTextures":  { "state": "Active",        "version": "v42" },
        "AITables":         { "state": "Transferring",  "version": "v18", "progressPct": 67 },
        "ScenarioA-Config": { "state": "Active",        "version": "r12345" }
      }
    }
  ],
  "summary": {
    "agentsOnline":      198,
    "agentsUnreachable": 2,
    "intentsPending":    47,
    "intentsExecuting":  12,
    "messagesUnread":    3
  }
}
```

### 10.2 `GET /api/status/{agentId}` — Single Agent State

Response:
```json
{
  "agentId":        "SIM-03",
  "segmentId":      "seg-A",
  "hostname":       "sim03.local",
  "logicalNodeIds": [42, 43],
  "capabilities":   ["render", "physics"],
  "isRelay":        false,
  "presence":       "Online",
  "currentGroupId": "session-2026-05-17-A",
  "lastSeen":       "2026-05-17T10:00:00Z",
  "bundles": {
    "TerrainTextures":  { "state": "Active", "version": "v42", "activatedAt": "2026-05-15T11:30:00Z" },
    "AITables":         { "state": "Transferring", "version": "v18", "progressPct": 67, "startedAt": "2026-05-17T09:55:00Z" }
  },
  "intents": [
    { "intentId": "11111111-...", "kind": "Deploy", "state": "Executing", "bundleId": "AITables" }
  ],
  "diskFreePct": 42
}
```

Errors:
- `404` — agent not found

---

## 11. Operator Message Queue

### 11.1 `GET /api/messages` — List Messages

```
GET /api/messages?severity=warning&dismissed=false&cursor=...&limit=50
```

Filters:
- `severity`: `info | warning | error`
- `dismissed`: `true | false`
- `since`: ISO8601

Response:
```json
{
  "items": [
    {
      "messageId":   "msg-...",
      "severity":    "warning",
      "title":       "Intent stale",
      "detail":      "Intent 11111111-... is past staleAfter (24h). Bundle: TerrainTextures v42, agent SIM-03.",
      "intentId":    "11111111-...",
      "agentId":     "SIM-03",
      "bundleId":    "TerrainTextures",
      "createdAt":   "2026-05-18T10:01:00Z",
      "dismissedAt": null
    }
  ],
  "nextCursor": null
}
```

### 11.2 `DELETE /api/messages/{messageId}` — Dismiss a Message

Response: `204 No Content`.

Errors:
- `404` — message not found

---

## 12. Configuration

### 12.1 `POST /api/config/reload` — Reload Site Config

Triggers the master to re-read the canonical site config JSON file and push updated slices to affected agents.

Request body: empty.

Response (`200 OK`):
```json
{
  "reloadedAt":          "2026-05-17T11:00:00Z",
  "configSourcePath":    "C:/ProgramData/SyncMaster/site-config.json",
  "diffSummary": {
    "agentsAdded":      [],
    "agentsRemoved":    [],
    "agentsChanged":    ["SIM-03"],
    "bundlesEnsured":   ["GlobalAITables"],
    "categoryChanged":  ["Config"]
  },
  "slicesPushed": 1
}
```

Side effects: master computes per-agent slice diffs and sends `ConfigUpdate(agentSlice)` to changed agents via SignalR.

Errors:
- `400` — config file invalid JSON or fails schema validation; body details the error
- `500` — config file unreadable

---

## 13. Garbage Collection

### 13.1 `POST /api/gc/preview` — Dry-Run GC Report

Request body (optional, defaults shown):
```json
{
  "scope": "all",                              // all | agents | masterCache | nas
  "agentIds": null                             // restrict agents scope to a subset
}
```

Response (`200 OK`):
```json
{
  "asOf": "2026-05-17T03:00:00Z",
  "nas": {
    "bundlesToDelete": [
      { "bundleId": "TerrainTextures", "version": "v38", "bytes": 12345678,
        "reason": "older than retentionCount=3 and not Active on any agent" }
    ],
    "sessionsToDelete": [],
    "bytesReclaimable": 12345678
  },
  "masterCache": {
    "bundlesToEvict": [
      { "bundleId": "OldDataset", "version": "v3", "bytes": 5000000, "reason": "no longer published, no in-flight transfers" }
    ],
    "bytesReclaimable": 5000000
  },
  "agents": [
    {
      "agentId": "SIM-03",
      "bundlesToEvict": [
        { "bundleId": "TerrainTextures", "version": "v40", "bytes": 9876543, "reason": "older than keepLastN=2" }
      ],
      "recordingsToEvict": [
        { "sessionId": "5b2f...", "bytes": 12000000000, "reason": "older than keepLastNSessions=5" }
      ],
      "bytesReclaimable": 12009876543
    }
  ]
}
```

### 13.2 `POST /api/gc/run` — Execute GC

Same request body as preview.

Response (`202 Accepted`):
```json
{
  "intentId":       "gc-2026-05-17-03",
  "state":          "Executing",
  "previewMatched": true,
  "startedAt":      "2026-05-17T03:00:00Z"
}
```

GC runs as a single intent visible via `GET /api/intents/{intentId}` of kind `Gc`.

Side effects:
- NAS deletions performed directly by the master.
- Master cache deletions performed locally.
- Agent evictions delivered via `ReceiveCommand(action=EvictBundle | EvictSession, ...)`.

---

## 14. Data Plane

### 14.1 `GET /content/bundles/{bundleId}/{version}/{path...}` — Download Bundle Bytes

Served by master and (for cached bundles) by relays. Identical URL shape at both.

Supports HTTP byte-range requests for resume:

```http
GET /content/bundles/TerrainTextures/v42/bundle.zip HTTP/1.1
Range: bytes=536870912-

HTTP/1.1 206 Partial Content
Content-Type: application/octet-stream
Content-Range: bytes 536870912-1073741823/1073741824
Content-Length: 536870912
ETag: "sha256-aabb..."
```

Path resolution: `{path...}` is the file path within the version directory as declared by the manifest. For zip-container bundles this is typically a single `bundle.zip`. For `ChunkedHugeFile` bundles it is the single file name (e.g. `dataset.zip`). For `Config` bundles it may be individual files.

The manifest is also retrievable:
```
GET /content/bundles/{bundleId}/{version}/manifest.json
```

Headers:
- `ETag`: SHA-256 of the requested resource. Stable for the lifetime of the version.
- `Content-Type`: derived from extension (`application/zip`, `application/json`, `application/octet-stream`).
- `Accept-Ranges: bytes`

Errors:
- `404` — bundle, version, or path not found
- `416 Range Not Satisfiable` — invalid range

### 14.2 `PUT /content/recordings/{sessionId}/{logicalNodeId}/chunks/{n}` — Upload Recording Chunk

Called by the agent during recording upload. `{n}` is the zero-based chunk index.

Request:
```http
PUT /content/recordings/5b2f.../42/chunks/17 HTTP/1.1
Content-Type: application/octet-stream
Content-Length: 67108864
X-Chunk-SHA256: aabb...

<chunk bytes>
```

Headers:
- `X-Chunk-SHA256`: required. SHA-256 of the chunk body. Master verifies before persisting.

Response (`204 No Content`) on success.

Errors:
- `400` — missing or malformed `X-Chunk-SHA256`
- `404` — no upload intent exists for `(sessionId, logicalNodeId)` (call `POST /api/recordings` first)
- `409` — chunk hash mismatch; body details the expected vs received
- `416` — chunk index outside the expected range

Side effects: master persists the chunk to its staging area on NAS. Master `ReportStatus` flows through the agent's SignalR connection.

### 14.3 `POST /content/recordings/{sessionId}/{logicalNodeId}/complete` — Finish Upload

Called by the agent after the last chunk to commit the upload.

Request:
```json
{
  "totalChunks": 192,
  "totalBytes":  12884901888,
  "finalSHA256": "ccdd..."          // SHA-256 of the assembled file
}
```

Response (`200 OK`):
```json
{
  "sessionId":     "5b2f...",
  "logicalNodeId": 42,
  "intentId":      "22222222-...",
  "nasPath":       "/NAS/Recordings/5b2f.../42.zip",
  "completedAt":   "2026-05-17T10:42:00Z"
}
```

Side effects: master assembles chunks, verifies `finalSHA256`, writes the zip to NAS at the canonical path, marks the upload intent `Complete`, removes chunk staging.

Errors:
- `404` — no upload intent for `(sessionId, logicalNodeId)`
- `409` — chunk count mismatch (some chunks missing), or `finalSHA256` mismatch after assembly
- `500` — NAS write failed; intent stays Executing for retry

---

## 15. Summary Table

| Method | Path | Purpose | Returns intent? |
|---|---|---|---|
| POST | `/api/bundles` | Register bundle | No |
| GET | `/api/bundles` | List bundles | No |
| GET | `/api/bundles/{bundleId}` | Bundle detail | No |
| PUT | `/api/bundles/{bundleId}` | Update bundle | No |
| DELETE | `/api/bundles/{bundleId}` | Deregister bundle | No |
| POST | `/api/bundles/{bundleId}/versions` | Publish version | No (sync record) |
| POST | `/api/deploy` | Request bundle active | Yes |
| GET | `/api/intents` | List intents | — |
| GET | `/api/intents/{intentId}` | Intent detail | — |
| DELETE | `/api/intents/{intentId}` | Cancel intent | — |
| POST | `/api/intents/{intentId}/retry` | Retry failed intent | — |
| POST | `/api/membership` | Set group membership | Possibly |
| POST | `/api/fleet-sync-mode` | Toggle fleet sync | No |
| POST | `/api/recordings` | Declare per-node recording | Yes |
| GET | `/api/sessions` | List sessions | — |
| GET | `/api/sessions/{sessionId}` | Session detail | — |
| POST | `/api/sessions/{sessionId}/finalize` | Finalize session | No |
| DELETE | `/api/sessions/{sessionId}` | Delete session | Yes (per agent) |
| POST | `/api/safe-window` | Signal safe-window state | No |
| GET | `/api/status` | Full fleet state | — |
| GET | `/api/status/{agentId}` | Single agent state | — |
| GET | `/api/messages` | List messages | — |
| DELETE | `/api/messages/{messageId}` | Dismiss message | — |
| POST | `/api/config/reload` | Reload site config | No |
| POST | `/api/gc/preview` | Dry-run GC | No |
| POST | `/api/gc/run` | Execute GC | Yes |
| GET | `/content/bundles/{bundleId}/{version}/{path...}` | Download bundle bytes | — |
| PUT | `/content/recordings/{sessionId}/{logicalNodeId}/chunks/{n}` | Upload recording chunk | — |
| POST | `/content/recordings/{sessionId}/{logicalNodeId}/complete` | Finish recording upload | — |

---

## 16. Notes for Implementers

- All ISO8601 timestamps are UTC. Master stores and returns UTC; clients render local.
- All sizes are bytes (integer, can exceed 2³¹; use `long` / `Int64` in C# bindings).
- All hashes are lowercase hex SHA-256 unless otherwise stated.
- All UUIDs are lowercase, hyphen-separated.
- Pagination cursors are opaque to the client; do not parse.
- The data-plane endpoints are served by Kestrel using `IResult` streaming (e.g. `Results.Stream` for downloads, manual chunk reception for uploads). The control plane uses standard MVC controllers or minimal-API endpoints.
- For very high-throughput downloads, consider `sendfile` / `TransmitFile` via Kestrel's `IHttpResponseBodyFeature.SendFileAsync`.
