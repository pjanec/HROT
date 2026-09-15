<!--STATUS
state: LIVE
build-state: DISPATCH — MCP diagnostics slice. Per-node log reading (fix the silent-default), per-node
  architecture snapshot, and cluster-wide collection. All on the SHARED DebugApiService so every node
  (incl. a SimHost-only node) answers for itself.
updated: 2026-08-25
current-answer: pointer + autonomy. Design (with UML + federation topology): DESIGN_Mcp_Diagnostics_Federation.md.
known-conflict: ⚠ touches the generated MCP catalog. If any authoring/other MCP slice runs concurrently,
  coordinate the ONE catalog regen (tool-catalog.mjs / SKILL.md / DebugApiRouteDocs / src/index.mjs).
-->
# HANDOFF — **MCP diagnostics + federation** *(MCP lane)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Continue the **`MA-`** series *(Area M)*; state every id *(rule 5)*. ⚠ **Freeze is LIFTED** *(`2026-08-25`)*.

## 0. AUTONOMY PROTOCOL
Decide-and-log on unknowns *(DECISION LOG in the report)*; stop the item not the batch; DONE = §3 rails green.
⚠ **If the codebase-memory MCP tools are not connected this session, use the CLI** — `codebase-memory-mcp cli
<tool> '<json>'` *(see `.claude/CLAUDE.md`)* — ⛔ do NOT downgrade to grep-only.

## 1. ⛔ THE DESIGN IS THE SOURCE
📄 **[`DESIGN_Mcp_Diagnostics_Federation.md`](../../DESIGN_Mcp_Diagnostics_Federation.md)** *(READY-TO-BUILD)* —
§1 the federation *(each node hosts its own DebugApi; diagnostics go on the SHARED service)*, §2 the three
capabilities, §4 classDiagram, §5 sequenceDiagram, §6 items, §7 gates. Build §4/§5; report the match *(obligation
③)*; fold deviations into the design *(obligation ⑤)*.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §6)*
| # | task | the one thing not to get wrong |
|---|---|---|
| 🔴 **①** | **Wire `MessageLog.Sources` into `DebugApiService`** at BOTH composition roots — `EditorSubsystem.cs` **and** `Hrot.ClusterRunner/Program.cs` *(which builds `DebugApiService(dispatcher)` with NO sinks today)* | ⛔⛔ **the cluster path is the real gap** — a SimHost-only node must answer `get_logs`. ⭐ Rail: after a logged line, `get_logs` is non-empty on a **cluster-limited** node; RED by reverting the wiring *(the caller-had-the-value silent-default — measure the construction site)* |
| ⭐ **②** | **`GET /diagnostics/architecture`** on the shared `DebugApiService`, reading `IArchitectureDiagnosticsService(kernel)` — modules/translators/stats, per node | a `RouteDoc` + a handler in `src/index.mjs`; `test-catalog` green; per-node kernel *(not a global)* |
| ⭐ **③** | **Cluster (a):** `POST /cluster/diagnostics/dump` *(select nodes)* + `GET /cluster/diagnostics/status` — **reuse the dump-diag CQRS pipeline** *(`docs/designs/dump-diag/DESIGN.md`)* | ⛔ do NOT reinvent collection — trigger the built pipeline; it fans out via CQRS intent and pulls to NAS over SMB |
| ⚠ **④** *(after ①)* | **Cluster (b):** an orchestrator-side aggregator that fans out `/logs` + `/diagnostics/architecture` to each node's endpoint and merges JSON | records, not files; needs the per-node endpoints reachable; log what it could not reach *(no silent truncation)* |

## 3. ⭐ DONE — rails *(design §7)*
- `get_logs` non-empty after a logged line on a **cluster-limited** node *(the SimHost-node proof; red by reverting the sink wiring)*;
- `GET /diagnostics/architecture` returns a node's modules/translators/stats;
- the cluster route triggers a dump / aggregates across `--mode all` nodes;
- `gen:catalog`/`gen:skill`/`test-catalog` green; `GET /capabilities` reflects the new routes *(R-133 inversion)*;
- affected-project builds *(`Hrot.Editor`, `Hrot.ClusterRunner`, `Fdp.ModuleHost`, `Hrot.SystemTests`)*; system suite named + run *(T3, background)*.

## 4. ⭐ LANE, SCOPE & COLLISION
⭐ **Yours:** `DebugApiService` *(+ the log-sink wiring at both composition roots)* · new diagnostics route files ·
`Hrot.ClusterRunner/Program.cs` *(the cluster composition)* · `EditorSubsystem.cs` *(the editor composition — the log-sink pass)* ·
the generated catalog · `Hrot.SystemTests/**`. ⛔ **Do NOT touch** `DebugApiService.Authoring.cs` *(authoring slices)* or the
CGF asset-service dict. ⚠ **You own the catalog regen** — if an authoring/other MCP slice is dispatched concurrently, coordinate
the ONE regen with the coordinator *(rule 4 re-pull)*. ⭐ **Wire diagnostics on the SHARED `DebugApiService`, never editor-only** —
else a SimHost node cannot answer for itself *(design §1)*.

## 5. GATES *(rule 8 contract)* + WHEN DONE
One row per gate · counts · Δ vs the started-marker · `--no-build` column · reds by `git diff` · `tracker-counts.py` ·
`rulings-check.py` · `design-digest.py --check` · the `MA-` ids. **When done:** fold the as-built into
`DESIGN_Mcp_Diagnostics_Federation.md` §8; state the ids; the report points at the design and carries the DECISION LOG.
