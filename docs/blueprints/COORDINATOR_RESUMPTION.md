<!--STATUS
state: LIVE
doc-type: coordinator resumption snapshot — point-in-time state for picking up AFTER COMPACTION. ⚠ A STATE
  doc, not canon: every "in flight"/"merged" line is a snapshot dated below — ⛔ VERIFY against git before
  acting, never quote it as settled truth (per "THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-08-26
current-answer: the whole file — read it, then re-derive the live state with the commands in §0.
-->
# COORDINATOR RESUMPTION — `2026-08-26`

RELEARN

> ⭐⭐⭐ **You are the COORDINATOR** on `claude/blueprint-authoring-status-6sr5ld`. ⛔ You do NOT implement —
> you design, write handoffs, and verify+merge returned diffs *(rule 8: the REPORT substitutes for
> re-running the gates; read the diff, spot-check surprising claims, do NOT re-run the suite)*.

## 0. ⭐ FIRST MOVES (re-derive live state — ⛔ don't trust the snapshot below without this)
```bash
python3 scripts/session-design-brief.sh            # ledger · 7-day digest · probe verdict · 3 random rulings
# read docs/blueprints/RULINGS.md IN FULL (RULE ZERO)
git fetch origin
git log --oneline -1 origin/claude/blueprint-authoring-status-6sr5ld     # coordinator HEAD (snapshot: 2223f7200)
git log --oneline -3 origin/claude/blueprint-macro-feature-sdmspn        # BACKEND lane (MCP diagnostics: MD-001..005)
git log --oneline -3 origin/claude/reset-working-branch-qd1qpv           # UI/CGF lane (axis-b egress: AX-005..010)
git branch -r | grep claude/                                            # confirm lane heads by ancestry, not name
```

## 1. ⭐⭐⭐ THE PROGRAMME — `cgf == editor`
Make the CGF subsystem the full editor in `--mode all` *(only difference = network setup + authority)*.
📄 **THE MAP:** [`PROGRAMME_Cgf_Equals_Editor_Gap_Map.md`](PROGRAMME_Cgf_Equals_Editor_Gap_Map.md) *(§0.5
= the pure-sharing framing; §2 = the master status table)* · charter
[`PROGRAMME_Unification_And_Harness.md`](../PROGRAMME_Unification_And_Harness.md).
⭐ **Axis A** *(asset perspectives · diagnostics · editing)* = the active track. **Axis B** *(map/entity
parity)* = later, gated on the **UXI-30** engine-authority design *(the ONE genuinely-open design item)*.

## 2. ✅ DONE / MERGED (snapshot — confirm by git)
| what | ids | design |
|---|---|---|
| CGF adopts the AiShared shell *(windows registered)* | CE-001..010 | `DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md` |
| CGF opens/indexes assets + MCP open/switch/focus + toolbar readable | CE-012..018 | `..._Slice2_Open_Asset.md` |
| editing + hot reload on CGF *(wholesale; live value-write OFF)* | CE-019..024 | `..._Slice3_Editing_HotReload.md` |
| watch: concrete pin survives a reload | BP-508..512 | `DESIGN_Variable_Watch_Pinning.md` §11 |
| network-id allocator unification + Path-B deletion | HN-050..058 | `DESIGN_Deterministic_Network_Ids.md` §11 |
| ⭐ **MCP AI-asset authoring surface** *(8 routes; merged `2026-08-25`)* | MA-001..010 | `DESIGN_Mcp_Authoring.md` §12 *(AS-BUILT)* |
| ⭐ **CGF debug pause/step** *(CgfClusterDebugTimeController; merged `2026-08-25`)* | CE-025..030 | `..._Slice4_Debug_PauseStep.md` §10 *(AS-BUILT)* |
| ⭐⭐ **MCP FULL authoring capability** *(union backbone `POST /graph/command` over all 35 GraphCommand variants; read/discover completeness; `EditDoc` doc-harvest; `/editor/commands` bus; merged `2026-08-25` overnight)* | MA-011..018 | `DESIGN_Mcp_Authoring.md` §15 *(AS-BUILT)* |
| ⭐⭐ **CGF: barrier proven + ruling 67 + CE-016** *(k measured 252–352ms; asset roots from config; time-transport on the toolbar; merged `2026-08-25` overnight)* | CE-031..036 | `..._Slice4_Debug_PauseStep.md` §11 · gap map · ruling 67 |
| ⭐⭐ **MCP per-node diagnostics + federation** *(GET /logs answers on every node incl. headless SimHost; GET /diagnostics/architecture per subsystem; non-editor conformance rail; merged `2026-08-26`)* | MD-001..003 | `DESIGN_Mcp_Diagnostics_Federation.md` §8 *(AS-BUILT)* |
| ⭐⭐ **Axis-B cross-node change-request egress under R-134** *(FDP-internal EntityAttributeChange + EntityWriteRouter in Fdp.Toolkits; egress translator SOLE DDS boundary; drag gizmo on same router; CE-018/035/036; merged `2026-08-26`)* | AX-005..010 · CE-018/035/036 | `DESIGN_Cgf_AxisB_Rotation_Slice.md` §11-§12 *(AS-BUILT)* |

## 3. ✅ NOTHING IN FLIGHT *(both `2026-08-26` batches verified + merged into `d41368090`)*
| merged batch | ids | verify verdict |
|---|---|---|
| **MCP per-node diagnostics + federation** *(was on `sdmspn`, from base `0ee5305a8`)* | MD-001..005 | ✅ rule-8 report complete: system suite 104/0, editor unit 251/0/1, golden ZERO, tree clean, catalog 90→91, two revert red-proofs. CLI-indexed the graph *(192k nodes)* — the fallback rule paid off. Corrected my design in 4 places, folded to §8 *(obligation ⑤)*. **MD-004** *(cluster dump)* + **MD-005** *(aggregator)* **STOPPED with measured blockers** — see §3c |
| **Axis-B cross-node egress under R-134** *(was on UI/CGF lane `qd1qpv`, from base `2aacffa8a`)* | AX-005..010 · CE-018/035/036 | ✅ rule-8 report complete: system suite 102/102, StrictNetworkSeparationTests + red-proofs green. Design premises corrected by build *(binary path already authority-gated per-installer; egress reused existing `UpdateEntityAttributeCommand` — ruling 9)*, folded to §11-§12. **AX-009** *(full `--mode all` round-trip rail)* **kept RED as a live probe** — pre-existing SimHost→IG replication failure, R-131 *(not skipped)* — see §3c |

| **MCP cluster dump + editor-bus rail** *(backend follow-up, merged `8b626168d`)* | MD-006/007/008 | ✅ MD-006/007 = the cluster dump BUILT *(see §3d)*; MD-008 = my `/editor/commands` candidate REFUTED *(see §3b)*, rail shipped. System suite 105/0. Clean descendant of coordinator HEAD; benign shared-file edits *(CgfSubsystem provider member, EditorSubsystem comments)*. |

⭐ **Merge integration verified by ME (coordinator):** both batches edited `CgfSubsystem.cs` + `EditorSubsystem.cs` in different regions — git auto-merged cleanly, and I built **both `Hrot.CGF` and `Hrot.Editor` → 0 errors** *(a clean textual merge of these two files is NOT proof they compile together; the build is)*. Doc gates on the combined tree: rulings 25/25 *(2 pre-existing staleness WARNs)*, tracker 102/346, design-digest 87 OK, mermaid 8/8.
🔴 **ruling-9 note for the NEXT slice:** MA- shipped `GET /assets/{id}/graph/catalog` and MD- shipped the `/diagnostics` prefix — any diagnostics/discovery follow-up must **EXTEND** these, ⛔ NOT build a parallel surface.

## 3d. ⛔ THE TWO HONEST NON-DELIVERABLES from the `2026-08-26` batches *(both need their own design, not a retry)*
- ✅ **MD-004 — cluster dump: BUILT `2026-08-26` as MD-006/007.** The earlier "STOPPED — no read-model" was the backend session measuring the WRONG class. `POST /cluster/diagnostics/dump` + `GET /cluster/diagnostics/status` reuse `ExecuteDiagnosticDumpIntent` *(same as ExCon's Execute button)* + `ClusterUiCache` *(same as its results section)*. Rail probes the master's fan-out line for the transaction id; empty selection refused. ⛔ **MD-005 aggregator — NEVER NEEDED** *(design correction dc4739188)*: the fan-out already happens cluster-side via the intent; no orchestrator records-aggregator route required.
- **AX-009 — the full `--mode all` change-request round-trip rail is RED.** 📐 Cause is a **pre-existing SimHost→IG replication failure**, not the egress *(which the isolated round-trip + StrictNetworkSeparation rails prove works)*. Kept as a live probe *(R-131 — no permanent filter-around)*. ⭐ This is the same class as the `ClusterRunner.Integration.Tests` DDS-allocator noise: a real cluster-replication gap that blocks the end-to-end acceptance vehicle. **Worth its own investigation slice.**

## 3b. ⭐ FILED FOLLOW-UPS from the overnight batches *(small, none blocking)*
- ✅ **CE-035 / CE-036 / CE-018 — ALL MERGED `2026-08-26`** in the axis-b egress batch *(CE-035 routes continue-after-step through `RequestResume`; CE-036 fixed the domain-id skip; CE-018 replaced the two inline `.csproj` walk-ups with `AssetRoots`)*.
- ✅ **schema-exporter on CGF — ALREADY DONE** *(measured `2026-08-26`)*: `Program.cs:520` threads `cgfShell.AssetShellSchemaExporter` into the cluster `DebugApiService` via `AttachSchemaExporter`. The old `none:no-exporter-wired` note is stale.
- ❌ **REFUTED `2026-08-26` by MD-008 — the CGF `/editor/commands` "gap" is NOT one.** I filed it as a silent-default *(Program.cs never calls `AttachEditorCommands` for the CGF cluster service)*. 📐 Both halves true, conclusion FALSE: a CGF node already answers **68 commands** because `ResolveEditorCommands` falls back to `_documents.Active → ContextOf(...).Commands`, and `_documents` arrives via `AttachAssetShell` *(both roots call it)*. ⇒ the attach is redundant; backend reverted it *(kept as a documented override hook)* and shipped the rail `The_editor_command_bus_answers_on_a_non_editor_node`. 📌 ⚠⚠ **This is the LEDGER-MAY-NOT-ASSERT-CODE rule biting ME: I confirmed the call-site and stopped — I never asked the route.** ⛔ This is the CANVAS command bus; the **SHELL** commands + toolbar are still absent on CGF *(that is CE-016 §7 — a DIFFERENT `IEditorCommands` instance, see the gap map)*.
- ⚠ **doc-prose coverage sweep** — `EditDoc` makes 100% node/param prose POSSIBLE; filling it across the catalog is a sweep, not done. The rail prints the % to ratchet.

## 3c. ⭐ MERGED `2026-08-25` (later) + their follow-ups
- ✅ **MCP create-from-recipe** *(MA-019..023)* — CGF creates assets; `GET /assets/recipes`; `POST /assets` gained optional `recipe`. 📄 AQ57 AS-BUILT.
- ✅ **Axis-B first cut** *(AX-001..006)* — registration-time authority gate + `GeoHeading=13` + the attempt-then-check write router + subsystem-agnostic rotator. 📄 `DESIGN_Cgf_AxisB_Rotation_Slice.md` §9. 🔴 **open:** **AX-005** *(no production SENDER of binary attribute records — the DDS egress for `UpdateEntityAttributeRequest.AttributeRecords` is separate work; needed for real cross-node rotation)* · **AX-006** *(`Hrot.Editor` never calls `SetAuthority`, so writes look unowned there — a later "wire the writer everywhere" slice must grant authority on the creating host first)*.
- 📄 **NEW MCP capability designed:** `DESIGN_Mcp_Diagnostics_Federation.md` *(per-node federation + logs/architecture/cluster diagnostics)* + `HANDOFF_Mcp_Diagnostics_Federation.md` *(ready to dispatch)*.

## 4. ⭐ NEXT, QUEUED
0. ✅ **CE-016 §7 — CGF shell-command + main-toolbar adoption (A2): MERGED `2026-08-26` (`2223f7200`, ids `CE-037`..`CE-040`).** One shared derived list *(`CgfEditorShellToolbar`, AiShared)*; CGF toolbar routes through `ToolbarCommandAdapter`+`SilkIconProvider`; `main-toolbar` conformance flipped to a `SUBSET-BY-DESIGN` verdict. ⚠ **Built by the BACKEND session** *(user relayed it there)* — it touched `CgfSubsystem.cs`+`EditorSubsystem.cs` ⇒ the UI/CGF lane must **rule-7 re-sync + reconcile** those two files when it returns. Design §9 AS-BUILT.
   - ⭐ **Two clean follow-ups from its argued deviations** *(design §9.3/§9.4)*: **(a)** compose a picker launcher on CGF ⇒ the Open/New toolbar buttons *(CGF can already CREATE over MCP, MA-019..023)*; **(b)** decide whether CGF's CLUSTER-TIME stepping deserves its OWN toolbar ids *(⛔ NOT the editor's `debug.*` ids — those mean AI-graph stepping; conflating them is what the SAME-by-id rail prevents)*.
0b. ⭐ **UXI-05 menu-follows-focus** — the NEXT slice; its menu registration EXTENDS `CgfEditorShellToolbar`'s one list *(reaching the A1 endpoint incrementally)*. Designed *(`UX/UX_Feature_Menu_Follows_Focus.md`)*, unbuilt; needs its own READY-TO-BUILD design + handoff.
1. ⭐ **`cgf==editor` — the remaining EDITING conversions still need design decisions** *(NOT autonomous-safe — flagged out of the overnight run)*: **AQ25 authoring shell / role-&-mode gating / undo / autosave** *(architect-UNANSWERED)* · **Q25-C behavior-affinity registry** *(can `BehaviorUiCompiler` be schema-driven?)* · **`Hrot.Editor` catalog/`NewAssetService` packaging** *(move-to-shared vs reference)*. ⇒ these want an Architect_Question pass with the user before build.
2. **Axis B** *(map/entity parity — UXI-11/23/10/29)* — gated on the **UXI-30** engine-authority-gate design *(no design doc yet)*.
3. ⭐ **The small follow-ups in §3b** — batchable into any CGF-lane run.

### ⛔ HISTORY — the completed queue *(superseded by the merges above)*
1. ⭐⭐⭐ **MCP DISCOVER + COMPLETE + INVOKE slice** *(user, `2026-08-25`; the old "discovery" + "B" BUNDLED)*. One slice, one catalog regen, **three invoke surfaces on ONE pattern** *(discover-from-registry → invoke-through-one-seam → harvest docs → coverage rail)*:
   - ⭐ **discovery** — list node kinds + a kind's property schema + a node's current properties, from the registries *(`INodeCatalog.All`; `IActionSchemaExporter.DtoFields`)*. 📄 `DESIGN_Mcp_Authoring.md` §10.
   - ⭐ **usage docs harvested** from descriptive attributes, RouteDoc-style, + a doc-coverage rail. 📄 §10.6.
   - ⭐⭐ **completeness** — the WHOLE `GraphCommand` union via one generic route *(attachments=BTree decorators, regions=HSM parallel, all three hosts)*; read/discover/monitor extended to match. 📄 §11 *(APPROVED)*.
   - ⭐⭐ **UI-command actions** *(the old "B", now BUNDLED)* — `GET /commands` · `GET /commands/{id}` · `POST /commands/{id}/invoke` over **`IEditorCommands`** *(self-documenting descriptor; `EditorCommandContext.Args` = params)*. 📄 §10.7. ⛔ **`GlobalActionRegistry`** *(int-keyed, undocumented entity/gizmo actions)* is OUT — it's the Axis-B/entity-action track.
   ⛔ handoff NOT yet issued *(the mutation batch holds the route file)*.
2. **Axis B** *(map/entity parity — UXI-11/23/10/29 + the `GlobalActionRegistry` entity actions)* — gated on the **UXI-30** engine-authority-gate design *(no design doc yet)*.
3. **Watch/variable follow-ups** *(if the UI lane resumes)*: BP-503 restore-from-file reattach; BP-504/514 ruling-9 dup cleanups.

⇒ ⭐ **MCP slice order:** mutation *(RUNNING, MA-)* → **discover+complete+invoke (§10–§11, one bundle)**. Each branches from the prior merged base; ⛔ never concurrent on `DebugApiService.Authoring.cs` + the generated catalog.

## 5. ⭐⭐ KEY DECISIONS MADE THIS SESSION *(so they aren't re-litigated)*
- ⭐⭐⭐ **Build-sink rule** *(`.claude/CLAUDE.md` THREE TEST TIERS, `2026-08-24`)*: ⛔ never full-solution-build in the fix loop *(115 s vs 8 s)*; build the affected PROJECT; T2 = fast everything + new rails; **E2E/system suite = T3, ASYNC, never a foreground blocker**; prove a fix through the rail that reddened, ⛔ don't re-run the whole suite.
- **R-133**: the capability manifest is MEASURED from the route table, never hand-authored.
- **The steer** *(`STEER_Cgf_Shell_Adoption_Slice1.md`)*: take editing WHOLESALE on CGF, ⛔ no artificial gating; keep the live variable-VALUE write OFF *(R-52; variable-model lane)*.
- **AQ56 resolved → `DESIGN_Mcp_Authoring.md`**: guids as-is *(read-then-edit; no determinism scheme; no human-naming layer)*; reuse the human-editing command sink; ⭐ **the read reuses the JSON FORMAT but NOT the save serialization** *(save rewrites to deterministic name-derived pin ids + strips pins — AQ10)*; scenarios ≠ AI assets *(scenario authoring = world manipulation; the file is a snapshot; no "edit a scenario file"; one way, no modes)*; AI-asset editing uniform pre/post-running *(hot reload)*.
- **CE-011 corrections**: the §17 Soft/Hard classification is on the ALC file-watcher path, NOT the QuickReload path *(CE-023)*; a headless origin LOGS, never a cross-node confirm modal *(CE-024, ruling 53)*.
- **The endpoint is not pure wiring**: two PERMANENT ruled divergences — CGF binds networked handlers *(editor networkless)*; CGF writes unowned components as REQUESTS *("editor owns all")*.

## 6. ⭐ COORDINATOR OBLIGATIONS
- **Verify+merge** each returned batch: `git fetch`; read the REPORT + the diff; spot-check ONE surprising claim; ⛔ don't re-run gates *(rule 8)*; `--no-ff` merge into coordinator; push; keep the tree joined so parallel sessions branch from a current base.
- **Design docs carry the AS-BUILT** *(obligation ⑤)* — verify the implementer folded deviations back into the owning design, not just the report.
- **Every design/handoff cites its basis** *(RULE ZERO obligation 1)*; buildable designs carry a classDiagram + sequenceDiagram *(gated by `design-digest.py --check`)*; validate mermaid before pushing.
- **Always give the user GitHub links** on the current branch *(they're on mobile)*.
