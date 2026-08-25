<!--STATUS
state: LIVE
build-state: READY-TO-BUILD — carries classDiagram + sequenceDiagram (§4/§5). A NEW MCP capability area,
  sibling of DESIGN_Mcp_Authoring.md and MCP_Integration.md. Two things: (1) DOCUMENTS the per-node
  federation topology that already exists (each node hosts its own DebugApi with mode-gated capabilities);
  (2) designs the DIAGNOSTICS surface over MCP — per-node logs, per-node architecture snapshot, and
  cluster-wide collection — all on the SHARED DebugApiService so every node (incl. SimHost) exposes its own.
updated: 2026-08-25
design-basis: Hrot.ClusterRunner/Program.cs (the per-node DebugApiHost — editor-owned vs cluster-limited) ·
  EditorSubsystem.cs (the full-surface host) · DebugApiService.GetLogs + Fdp.Core/Logging (IMessageLogSource,
  MessageLog registry, NLogMessageLogTarget) · Fdp.ModuleHost/Diagnostics (IArchitectureDiagnosticsService) ·
  docs/designs/dump-diag/DESIGN.md (the cluster-wide diagnostic dump: CQRS intent -> per-node gather -> SMB
  pull to NAS; ClusterDiagnosticsPanel) · R-133/HN-030 (routes self-document; GET /capabilities is measured).
known-conflict: none. ⚠ The MCP authoring/create sessions own DebugApiService.Authoring.cs + the generated
  catalog; a diagnostics slice adds its own routes and MUST coordinate the ONE catalog regen if it runs
  concurrently with an authoring slice.
-->
# DESIGN — **MCP diagnostics + the per-node federation** *(logs · architecture · cluster-wide)*

> 🎯 Two parts. **①** Write down the topology that already exists: **every node runs its own MCP/DebugApi
> endpoint**, with capabilities gated by the subsystems it hosts. **②** Add the **diagnostics** surface —
> per-node logs, per-node architecture snapshot, cluster-wide collection — on the SHARED `DebugApiService`,
> so a SimHost node exposes *its own* logs/diagnostics, and the orchestrator can aggregate across nodes.

## 1. ⭐⭐⭐ THE FEDERATION — **each node hosts its own MCP endpoint** *(measured `2026-08-25`)*
| | |
|---|---|
| ⭐⭐ **who hosts** | `Hrot.ClusterRunner/Program.cs` — if the node's mode **includes the editor** *(editor / `--mode all`)*, **`EditorSubsystem`** owns the API with the **FULL** surface; otherwise the runner builds its **own** `DebugApiHost` *(HttpListener, `clusterPort` / `HROT_DEBUG_API_PORT`)* + a **limited** `DebugApiService(dispatcher)` |
| ⭐⭐ **what a node exposes** | the dispatcher is `PerspectiveScopedDispatcher(debugProviders,…)` — the providers for **the subsystems THAT node runs**. ⇒ a SimHost-only node exposes SimHost's providers *(entities, panels, and — once §2.1 lands — logs + diagnostics)*, but **NOT** the editor/authoring routes *(those live in `EditorSubsystem`)* |
| ⭐ **self-report** | `GET /capabilities` is **measured** from the route table *(R-133)* ⇒ an agent asks each node what it actually holds; ⛔ nothing hand-authored |
| ⇒ ⭐⭐⭐ **answer to "does a separate SimHost node run its own MCP server?"** | **YES — its own port, LIMITED capabilities** *(no authoring; entities/panels/logs/diagnostics for its subsystems)*. The editor/CGF/`--mode all` node gets the full surface because it runs `EditorSubsystem` |

⇒ ⭐⭐ **Design consequence, load-bearing:** anything that must work **per node** *(logs, architecture)* goes on the
**SHARED `DebugApiService`** *(reached by both the editor-owned and cluster-limited hosting paths)* — ⛔ NOT the
editor-only path, or the SimHost node cannot answer for itself.

## 2. ⭐⭐ THE DIAGNOSTICS SURFACE
### 2.1 ⭐⭐⭐ `GET /logs` — reads in-memory records, NOT the file *(exists; wiring gap)*
📐 **Route + handler EXIST:** `GetLogs(level, logger, since, max)` iterates `_logSinks`
*(`IReadOnlyList<IMessageLogSource>`)* off-thread, returns `[{timestamp, level, logger, message}]` newest-first.
The sources are real: `Fdp.Core/Logging` — a `MessageLog` registry + `NLogMessageLogTarget` *(the ring buffer that
feeds the on-screen `MessageLogWindow`)* + `AiBehaviorLogTarget` + `HotReloadMessageLogSource`.
🔴 **THE GAP (silent default):** `_logSinks = logSinks ?? Array.Empty<…>()` — if the composition builds
`DebugApiService` **without passing** `MessageLog.Sources`, `get_logs` returns `[]` while the same records feed the
UI window. ⚠ **Sharpest on the cluster path** — `Program.cs` builds `new DebugApiService(dispatcher)` with **no**
sinks ⇒ on a SimHost node `get_logs` is empty today. ⇒ ⭐⭐ **wire `MessageLog.Sources` at BOTH composition roots +
a rail.** *(the caller-had-the-value silent-default pattern.)*

### 2.2 ⭐⭐ `GET /diagnostics/architecture` — the modules/translators/stats snapshot *(per node)*
📐 The "Architecture Diagnostics" window is `ArchitectureDiagnosticsPanel` → **`IArchitectureDiagnosticsService`** /
`ArchitectureDiagnosticsService(ModuleHostKernel)` *(Fdp.ModuleHost)*. It builds a snapshot of **that node's**
modules, translators and stats straight from its kernel *(already JSON-able — `…DumpsItsSnapshot` test)*. ⇒ ⭐ expose
it as a read route on the shared service; **each node answers for its own kernel** ⇒ per-subsystem by construction.

### 2.3 ⭐⭐ Cluster-wide — reuse `dump-diag`, plus a records aggregator
The cluster window is the Orchestrator/ExCon **`ClusterDiagnosticsPanel`**, backed by the built **dump-diag** programme
*(`docs/designs/dump-diag/DESIGN.md`)*: an operator triggers a dump; every selected node gathers entity state, event
history, architecture, and NLog files, stages in `LocalTempRoot`, pulled to NAS over **SMB**; driven through the **CQRS
intent** pathway *(works from any node)*. Two MCP shapes, sequenced:
| | |
|---|---|
| ⭐ **(a) trigger the dump** | `POST /cluster/diagnostics/dump` *(select nodes)* + `GET /cluster/diagnostics/status` — reuse the CQRS pipeline; full archive to NAS. ⭐ Faithful to the window |
| ⭐ **(b) records aggregator** | the orchestrator **fans out** §2.1/§2.2 to every node's endpoint and merges JSON — *"recent records + architecture across nodes"*, no files. ⚠ needs §2.1 wired first |

## 3. ⭐⭐ INVENTORY — measured `2026-08-25`
| ✅ exists | where |
|---|---|
| `DebugApiHost` *(per node)* + `DebugApiService(dispatcher)` *(limited)* / editor full surface | `Hrot.ClusterRunner/Program.cs` · `EditorSubsystem.cs` |
| `DebugApiService.GetLogs` + `_logSinks` default-empty | `Hrot.Editor/DebugApi/DebugApiService.cs` |
| `IMessageLogSource` · `MessageLog` registry · `NLogMessageLogTarget` · `AiBehaviorLogTarget` · `HotReloadMessageLogSource` | `Fdp.Core/Logging` |
| `IArchitectureDiagnosticsService` · `ArchitectureDiagnosticsService(kernel)` · `ArchitectureDiagnosticsPanel` | `Fdp.ModuleHost/Diagnostics` · `Fdp.Presentation` |
| `ClusterDiagnosticsPanel` · `ILogArchiveExtractionService` · the merge worker · the CQRS dump pipeline | `Hrot.Orchestrator` · `Hrot.Core/Diagnostics` · `docs/designs/dump-diag/DESIGN.md` |
| `GET /capabilities` measured from routes | `DebugApi/CapabilityManifest.cs` |

## 4. ⭐⭐⭐ CLASS DIAGRAM
```mermaid
classDiagram
    direction LR
    class DebugApiHost {
        <<exists · per node · HttpListener on its own port>>
    }
    class DebugApiService {
        <<exists · shared · reached by editor-full AND cluster-limited hosting>>
        +GetLogs(level, logger, since, max)
        +GetArchitecture() NEW
    }
    class IMessageLogSource {
        <<exists · Fdp.Core/Logging · in-memory ring, feeds MessageLogWindow>>
    }
    class MessageLogRegistry {
        <<exists · MessageLog.Sources · WIRE into DebugApiService>>
    }
    class IArchitectureDiagnosticsService {
        <<exists · per-node kernel snapshot: modules, translators, stats>>
    }
    class ClusterDumpPipeline {
        <<exists · dump-diag · CQRS intent -> per-node gather -> SMB pull to NAS>>
    }
    class OrchestratorAggregator {
        <<NEW option b · fan out /logs + /architecture to each node, merge>>
    }
    DebugApiHost ..> DebugApiService : dispatches
    DebugApiService ..> MessageLogRegistry : GetLogs reads sources (FIX the empty default)
    MessageLogRegistry ..> IMessageLogSource : collects
    DebugApiService ..> IArchitectureDiagnosticsService : GetArchitecture
    OrchestratorAggregator ..> DebugApiHost : fan out per node
    OrchestratorAggregator ..> ClusterDumpPipeline : or trigger the file dump
    note for DebugApiService "Every NODE runs its own DebugApiHost/Service; capabilities are gated by the subsystems it hosts and self-reported by GET /capabilities. Diagnostics live on the SHARED service so a SimHost node answers for itself."
```

## 5. ⭐⭐⭐ SEQUENCE DIAGRAM
```mermaid
sequenceDiagram
    autonumber
    participant A as AI agent (MCP)
    participant N as a node DebugApi (SimHost or CGF)
    participant Src as MessageLog sources
    participant Arch as ArchitectureDiagnosticsService
    participant Orc as Orchestrator aggregator

    A->>N: GET /logs level=Warning
    N->>Src: query in-memory sinks
    Src-->>A: records newest-first
    A->>N: GET /diagnostics/architecture
    N->>Arch: snapshot this node kernel
    Arch-->>A: modules, translators, stats
    Note over A,Orc: cluster-wide
    A->>Orc: GET /cluster/diagnostics
    Orc->>N: fan out /logs + /architecture per node
    N-->>Orc: this node results
    Orc-->>A: merged across nodes
```

## 6. ⭐⭐ ITEMS
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Wire `MessageLog.Sources` into `DebugApiService`** at BOTH composition roots *(editor + `ClusterRunner/Program.cs`)* + a rail | ⛔ the cluster path builds `DebugApiService(dispatcher)` with no sinks — that is the SimHost-node gap; the rail must assert `get_logs` is non-empty after a logged line on a cluster-limited node |
| ⭐ **②** | **`GET /diagnostics/architecture`** on the shared service, from `IArchitectureDiagnosticsService` | per-node kernel; a `RouteDoc` + handler + `test-catalog` |
| ⭐ **③** | **Cluster (a):** `POST /cluster/diagnostics/dump` + `GET .../status` reusing the dump-diag CQRS pipeline | ⛔ do not reinvent the collection — trigger the built pipeline |
| ⚠ **④** *(sequence after ①)* | **Cluster (b):** an orchestrator-side aggregator that fans out `/logs` + `/architecture` to each node and merges | records, not files; needs the per-node endpoints reachable |

## 7. GATES
rule 8 + build/test rules. **Row 8 rails:** ⭐ `get_logs` non-empty after a logged line on a **cluster-limited** node *(red by reverting the sink wiring — the SimHost-node proof)* · `GET /diagnostics/architecture` returns a node's modules/translators · the cluster route triggers a dump / aggregates across `--mode all` nodes. `gen:catalog`/`gen:skill`/`test-catalog` green for each new route+handler; `GET /capabilities` reflects the new routes *(the R-133 inversion)*. ⚠ coordinate the ONE catalog regen if an authoring slice runs concurrently.

## 8. ⭐ WHEN DONE
Fold the as-built here *(obligation ⑤)*; state the ids; the report points here. ⭐ Add the diagnostics tools to the MCP catalog; note which are per-node vs orchestrator-only.
