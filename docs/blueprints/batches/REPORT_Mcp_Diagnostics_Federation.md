<!--STATUS
state: LIVE
updated: 2026-08-25
current-answer: this is a BATCH REPORT — ephemeral by design. ⭐ The durable record is
  DESIGN_Mcp_Diagnostics_Federation.md §8 AS BUILT (the three corrected premises, the UML match, and the
  two STOPPED items with their measured blockers), folded back per obligation ⑤.
known-conflict: none.
-->
# REPORT — **MCP diagnostics + the per-node federation** *(`MD-001`…`MD-005`)*

> 📌 **Dispatched at `0ee5305a8`** · **started-marker `98a31b364`** *(rule 1b, pushed before any code)*.
> 📄 Handoff: [`HANDOFF_Mcp_Diagnostics_Federation.md`](HANDOFF_Mcp_Diagnostics_Federation.md) ·
> design: [`DESIGN_Mcp_Diagnostics_Federation.md`](../../DESIGN_Mcp_Diagnostics_Federation.md).
> ⭐ **Items ① and ② DONE and probed.** ⛔ **Items ③ and ④ STOPPED**, each with a measured blocker — §3.

## 0. ⭐ THE CLI FALLBACK WORKED — **and it changed the answer**

⭐⭐ The handoff's §0 said *"if the codebase-memory MCP tools are not connected, use the CLI — ⛔ do NOT
downgrade to grep-only."* 📐 **They were not connected** *(fresh cloud VM)*, and the CLI was:
`codebase-memory-mcp cli index_repository --repo-path /home/user/HROT` → **192 316 nodes, 479 827 edges**,
tens of seconds. ⭐ **My previous report's "MCP was not connected so I used grep" is exactly the miss that
rule was written for.**

⚠ **And it earned its keep, in both directions** *(which is why the rule says never one alone)*:

| | |
|---|---|
| ⭐ **the graph found what my grep pattern missed** | `search_graph(".*ArchitectureDiagnostics.*")` returned **30** nodes across 6 construction sites — 📌 that is what showed architecture is per-SUBSYSTEM, not per-node *(§3 correction 3)* |
| ⚠ **grep found what the graph missed** | `query_graph` for `IMPLEMENTS → IMessageLogSource` returned **0 rows** *(the C# interface edges are not resolved)*; grep found the three real implementations |
| ⛔ **the graph over-matched** | it listed `EditorSubsystem.MessageLogOutputConsole` as a message-log class; grep showed it implements `IOutputConsole`, not `IMessageLogSource` |

## 1. ⭐⭐ OBLIGATION ③ — **the design's UML vs what was built**

⭐ The design carries a `classDiagram` *(§4)* and a `sequenceDiagram` *(§5)*. ⭐⭐ **Checked before building.**

| the diagram says | built as |
|---|---|
| `DebugApiService ..> MessageLogRegistry : GetLogs reads sources (FIX the empty default)` | ✅ **matches in substance, ⛔ not in name** — the box is `MessageLogRegistry`, but a headless node has none, so the wiring goes through `MessageLogSinks.ForDiagnostics` which unions the registry with two process-wide statics. §3 correction 1 |
| `DebugApiService ..> IArchitectureDiagnosticsService : GetArchitecture` | ⚠ **DEVIATES**: the edge is not direct. It goes `DebugApiService → PerspectiveScopedDispatcher.AllProviders → ISubsystemDebugProvider.Architecture → IArchitectureDiagnosticsService`, because one node holds several kernels. §3 correction 3 |
| `OrchestratorAggregator ..> DebugApiHost : fan out per node` | ⛔ **NOT BUILT** — §3, item ④ |
| the sequence's `A->>N: GET /logs` / `GET /diagnostics/architecture` | ✅ **built and exercised end-to-end by the rail** |

⭐⭐ **All four deviations are folded into the design as §8**, with the diagram's claims corrected in place
*(obligation ⑤)* — ⛔ not left only here, because this file is ephemeral.

## 2. 🔴🔴 WHAT THE WORK FOUND — **three false premises, all in the design, all measured**

> ⭐⭐⭐ **Every one of them would have produced a build that looked right and did nothing.** That is why
> they are listed before the gates.

### ⑴ ⛔⛔ `MessageLog.Sources` does not exist — and the named fix would not have fixed the named gap

📐 The registry is **`MessageLogRegistry`, an INSTANCE**, reached via `WindowManager.MessageLogRegistry`.
⇒ ⭐⭐⭐ **a headless node has no registry at all** — which is *precisely* the SimHost case §2.1 exists for.
⭐ What works: `NLogMessageLogTarget.SharedInstance` + `AiBehaviorLogTarget.SharedInstance`, process-wide
statics **`Program.Main` installs as NLog rules for every mode**. ⚠ De-duplicated **by reference**, because
`MessageLogHostWiring.CreateAndRegister` seeds the registry with the very same NLog instance — without
that, the editor would read the global ring **twice** and every line would appear duplicated.

### ⑵ ⛔ The seam had to become LAZY, or the fix would have been inert

📐 The editor builds `DebugApiService` in `Initialize`; its `MessageLogRegistry` appears in
`RegisterWindows`, **which runs later** — and which is when subsystems register their own sources.
⇒ an eager list latches the empty pre-registration state **forever**. ⭐ `logSinks` is now a
`Func<IReadOnlyList<IMessageLogSource>>`, re-read per request. 📌 The identical lesson
`SubsystemDebugProvider`'s lazy accessors already carry, and its doc-comment says so in as many words.

### ⑶ ⛔⛔ Architecture is per SUBSYSTEM, not per node

📐 `--mode all` expands to `orchestrator,simhost,ig,excon,cgf`, and **SimHost, IG and CGF each construct
their own `ArchitectureDiagnosticsService`**. ⇒ *"each node answers for its own kernel"* would have had to
pick one and drop the rest **silently**.
⭐⭐⭐ **So no new attach seam was invented.** `ISubsystemDebugProvider` already carries four
nullable-by-design members whose capability cells are measured in ONE place; `Architecture` is the fifth,
same shape. ⭐ **`architecture: null` is written out explicitly in ExCon** — which genuinely has no kernel
— rather than omitted, so the absence is a statement rather than an oversight.

### ⚠ ⑷ A gate that was never there

📐 **`DebugApiBatch11Tests.cs` — the only test that exercises the `logSinks` seam — is
`<Compile Remove>`-excluded from its csproj**, with nine sibling `DebugApiBatch*` files.
⇒ ⛔ **nothing has ever gated this seam.** ⭐ Its call site was updated to the new `Func<>` shape so it is
correct whenever it is re-enabled; ⚠ **it does not compile today and is NOT counted as coverage.**

## 3. ⚠⚠ ITEMS ③ AND ④ — **I GOT BOTH WRONG; corrected `2026-08-25`**

> 🔒 **User:** *"in the UI as a user i can click and data gets collected and saved. the cluster wide
> collection works. No further aggregation needed. What is missing from the MCP?"*
> ⭐⭐ **The correction lives in [`DESIGN_Mcp_Diagnostics_Federation.md`](../../DESIGN_Mcp_Diagnostics_Federation.md) §8.5** — durable, not only here.

| | ⛔ what I claimed | 📐 what is true |
|---|---|---|
| **`MD-004`** | *"no status read-model exists"* | 🔴 **I measured the wrong class.** `DiagnosticsDumpProcessManager` does expose only `Tick()` — ⛔ but the panel never reads status from it. It reads **`ClusterUiCache.LastDiagnosticManifest`**, `HasInFlightTransaction`/`TxHistory` and `ActiveNodes`. ⭐⭐⭐ **The provider seam already reaches that cache** *(ExCon passes `clusterState:` and `availableScenarios:` from it)*. 📌 The seam law: under-adopted, not missing |
| **`MD-005`** | *"blocked — no node → endpoint registry"* | ⚠ **True, and irrelevant.** That is a blocker for the design's option **(b)** *(an HTTP fan-out aggregator)* — a duplicate of the dump pipeline that already collects cluster-wide. ⇒ ⛔ **WITHDRAWN as not needed**, not deferred |

⭐⭐⭐ **What is actually missing from MCP is two routes and no new mechanism:**
**①** `POST /cluster/diagnostics/dump` — build the `DiagnosticDumpPayloadDto` and publish
`ExecuteDiagnosticDumpIntent`, **exactly what the Execute button does**;
**②** `GET /cluster/diagnostics/status` — read `ClusterUiCache`.
⚠ The one real constraint that survives: the `Hrot.Orchestrator`/`Hrot.ExCon` provider wiring is outside
this handoff's §4 lane — ⛔ **a scheduling fact, which the earlier text wrongly conflated with a
capability gap.**

⭐ **The general lesson, worth more than the item:** ⛔ **a correct measurement aimed at the wrong question
still misinforms.** Both findings were individually true; both answered something nobody asked.

## 4. GATES *(rule 8 contract)*

> 📌 **Base: the started-marker `98a31b364`** *(dispatch `0ee5305a8`)*.
> ⭐ **Built ONCE per affected project, then `--no-build`.** ⛔ **No full-solution build at any point.**
> ⚠ **A T3 probe rebuilds `Hrot.ClusterRunner`** — the rails launch that binary. Both probes below did.

| # | gate | verbatim command | `--no-build`? | result | Δ vs base |
|---|---|---|---|---|---|
| 1 | **affected-project builds** | `dotnet build {Fdp.Core,Hrot.Presentation,Hrot.SimHost,Hrot.IG,Hrot.ExCon,Hrot.CGF,Hrot.Editor,Hrot.ClusterRunner,…Tests}.csproj --no-restore -v q -nologo` | ⛔ builds *(once each)* | ✅ **0 errors** | — |
| 2 | ⭐⭐⭐ **the T3 federation rail** — the acceptance vehicle | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build --filter "FullyQualifiedName~A_non_editor_node_reports_its_own_logs"` | ✅ | ✅ **1 / 1, 2 s.** 📐 `17` log records · `SimHost — 10 modules / 56 systems / 37 translators` · `diagnostics.architecture` present in `/capabilities` | **+1 rail** |
| 3 | ⭐⭐⭐ **revert probe A** *(item ①, the SimHost gap)* | drop `logSinks:` from `ClusterRunner/Program.cs`, rebuild it, re-run | ✅ | ✅ 🔴 **red: `logs: 0 record(s)`** — ⭐ **that zero IS the pre-fix state on every cluster-limited node** | — |
| 4 | ⭐⭐ **revert probe B** *(item ②)* | null out `architecture:` in `SimHostSubsystem`'s provider, rebuild, re-run | ✅ | ✅ 🔴 **red**, the route refusing and naming the missing accessor | — |
| 5 | **the editor unit suite** *(carries `EveryRouteIsDocumentedTests` + `CapabilityManifestRails` — the gates the new route and the new `/diagnostics` prefix had to satisfy)* | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/…csproj --no-build -v q --nologo` | ✅ | ✅ **251 / 0 / 1 skipped** | **none** |
| 6 | ⭐⭐⭐ **the INTEGRATION suite** *(rule 8 row 8)* — a route on the shared table, a new member on a seam FOUR subsystems implement, and changes at BOTH composition roots ⇒ nothing smaller can show the cross-host contract holds | `scripts/run-system-tests.sh --no-build` *(**T3**, BACKGROUNDED — ⛔ never a foreground blocker)* | ✅ | ✅ **104 / 0**, 6 m 32 s — §4b | **+1 rail** |
| 7 | **the MCP catalog is GENERATED** | `npm run gen:catalog` · `npm run gen:skill` | — | ✅ **90 → 91 tools** from 91 endpoints; `SKILL.md` regenerated *(501 lines)*. ⚠ **The generator reads the BUILT binary** — the first regen produced 90 because `Hrot.ClusterRunner` had not been rebuilt | **+1 tool** |
| 8 | **every catalogued tool has a handler** | `node test-catalog.mjs` | — | ✅ **729 / 0** | **+8 assertions** |
| 9 | **golden movement** | — | — | ⭐ **ZERO** | **none** |
| 10 | 🔴 **tree CLEAN after every suite run** | `git status --short --untracked-files=all` | — | ✅ **only this batch's own files.** ⚠ The new rail reads only — it writes no asset and needs no sentinel folder | — |
| 11 | **quarantine / skips** | — | — | ⚠ **This batch adds no skip** — ⛔ **but it FOUND one that was already there and undeclared:** `DebugApiBatch11Tests.cs` and nine siblings are `<Compile Remove>`-excluded, so the `logSinks` seam had no gate at all *(§2 ⑷)* | **none added** |
| 12 | **tracker** | `python3 scripts/tracker-counts.py --check` | — | ✅ `open 102 / done 346 (+1 refuted)` — ⭐ unchanged: `MD-` rows carry no `BP-` id, by design | — |
| 13 | **the ledger** | `python3 scripts/rulings-check.py` | — | ✅ **25 / 25.** ⚠ **2 staleness WARNs** *(`.claude/CLAUDE.md`, `CapabilityManifest.cs`)* — 📐 **confirmed PRE-EXISTING**: `git stash` → re-run reproduced both at the started-marker, before this batch touched either. ⚠ `CapabilityManifest.cs` is now ALSO touched here *(the `/diagnostics` prefix)*, so the warn persists for a second reason | **none** |
| 14 | **design-doc format + UML** | `python3 scripts/design-digest.py --check` | — | ✅ **87 documents.** ⭐ The design moved to `build-state: BUILT (items ①②)` and already carried both diagrams | — |
| 15 | **mermaid parses** | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Mcp_Diagnostics_Federation.md` | — | ✅ **2 / 2** | — |
| 16 | ⭐ **capability manifest** *(`R-133`)* | — | — | ⭐⭐ The new `/diagnostics` prefix required a `CapabilityManifest` line — **the designed inversion**, an unclassified prefix REDDENS `CapabilityManifestRails`. ⛔ Not a hand-authored cell: it returns the SAME key the providers derive from a non-null kernel | — |

### 4b. ⭐ The full T3 suite

📐 **`scripts/run-system-tests.sh --no-build` — `104 / 0 / 0 skipped`, 6 m 32 s.**

⭐ **`103 → 104`: the one new coverage rail, nothing removed and nothing skipped.**

⚠⚠ **This row carried more weight here than in any recent batch, and the reason is the SHAPE of the
change, not its size.** ⛔ It was not a route added beside other routes: it changed
`DebugApiService`'s `logSinks` **parameter type** *(a list → a `Func<>`)*, added a **member to
`ISubsystemDebugProvider`** — a seam **FOUR subsystems implement** — and edited **both** composition
roots plus `EditorProcess`'s launch arguments. ⇒ ⭐⭐ every existing rail runs against a host built by one
of those two roots, so this suite is the only thing that shows the editor-owned surface *(authoring,
assets, save/reload, the union backbone, perspectives)* still holds after a seam moved underneath it.

📐 **Tree re-checked CLEAN after the suite**, not only after the filtered runs.

## 5. ⭐ IDS ALLOCATED *(rule 5)*

**`MD-001`…`MD-005`**, tracker **Area M** *(a new `MD-` prefix for the diagnostics lane, so it cannot
collide with the `MA-` authoring series)*.
✅ `MD-001` the log-sink wiring · `MD-002` `/diagnostics/architecture` + the provider member ·
`MD-003` the non-editor conformance rail · ⛔ `MD-004` cluster dump **STOPPED** ·
⛔ `MD-005` aggregator **STOPPED**.

## 6. ⚠ LANE EXCURSIONS — **declared, because §4 named a narrower lane**

The handoff's lane was *"`DebugApiService` · new diagnostics route files · `ClusterRunner/Program.cs` ·
`EditorSubsystem.cs` · the generated catalog · `Hrot.SystemTests/**`."* ⭐ Item ② as designed could not
stay inside it:

| file | why it had to change |
|---|---|
| `Hrot.Presentation/DebugApi/ISubsystemDebugProvider.cs` | ⭐⭐ the per-subsystem seam — **the alternative was a parallel attach seam, i.e. ruling 9's duplicate** |
| `SimHostSubsystem` · `IgSubsystem` · `CgfSubsystem` · `ExConSubsystem` | one line each: pass the kernel they already build |
| `Fdp.Core/Logging/MessageLogSinks.cs` *(new)* | the shared sink rule; ⛔ two hand-written lists at two roots is how the empty default returns |
| `Hrot.SystemTests/EditorProcess.cs` | `--no-wait` for a single non-editor mode — in lane |
| `ClusterRunner.Integration.Tests/DebugApiBatch11Tests.cs` | the `Func<>` signature. ⚠ **Excluded from compilation** *(§2 ⑷)* |

⛔ **`DebugApiService.Authoring.cs` and the CGF asset-service dict were NOT touched**, as §4 required.

## 7. ⛔ WHAT THIS BATCH DID **NOT** DO

| | |
|---|---|
| ⛔ **`POST /cluster/diagnostics/dump` · `GET .../status`** | `MD-004` — stopped, §3 |
| ⛔ **the orchestrator records aggregator** | `MD-005` — stopped, §3; **no endpoint registry exists** |
| ⛔ **re-enabling the quarantined `DebugApiBatch*` files** | ⚠ ten files, unknown red count, and re-homing their claims is real work — ⭐ **reported (§2 ⑷) rather than rushed**, per the no-rush-removals discipline applied in reverse |
| ⚠ **a `--mode simhost` rail** | 📐 measured impossible: a standalone SimHost node dies in `DdsIdAllocatorHelper` — *"Hrot.Orchestrator must be running before this node starts"*. ⭐ `orchestrator,simhost` is the smallest mode that boots **and** has no editor |
