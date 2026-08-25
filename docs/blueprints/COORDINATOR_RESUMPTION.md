<!--STATUS
state: LIVE
doc-type: coordinator resumption snapshot — point-in-time state for picking up AFTER COMPACTION. ⚠ A STATE
  doc, not canon: every "in flight"/"merged" line is a snapshot dated below — ⛔ VERIFY against git before
  acting, never quote it as settled truth (per "THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-08-25
current-answer: the whole file — read it, then re-derive the live state with the commands in §0.
-->
# COORDINATOR RESUMPTION — `2026-08-25`

RELEARN

> ⭐⭐⭐ **You are the COORDINATOR** on `claude/blueprint-authoring-status-6sr5ld`. ⛔ You do NOT implement —
> you design, write handoffs, and verify+merge returned diffs *(rule 8: the REPORT substitutes for
> re-running the gates; read the diff, spot-check surprising claims, do NOT re-run the suite)*.

## 0. ⭐ FIRST MOVES (re-derive live state — ⛔ don't trust the snapshot below without this)
```bash
python3 scripts/session-design-brief.sh            # ledger · 7-day digest · probe verdict · 3 random rulings
# read docs/blueprints/RULINGS.md IN FULL (RULE ZERO)
git fetch origin
git log --oneline -1 origin/claude/blueprint-authoring-status-6sr5ld     # coordinator HEAD (snapshot: a0e47e518)
git log --oneline -3 origin/claude/blueprint-macro-feature-sdmspn        # the MCP-authoring session
git branch -r | grep claude/                                            # find the slice-4 branch (new)
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

## 3. ✅ NOTHING IN FLIGHT *(both `2026-08-25` batches verified + merged)*
| merged batch | ids | verify verdict |
|---|---|---|
| **MCP authoring** *(was on `sdmspn`)* | MA-001..010 | ✅ rule-8 report complete: integration suite 99/0, golden ZERO, tree clean, catalog 74→82, red = pre-existing ST-035 A/B'd. Corrected my design in 4 places, folded to §12 *(obligation ⑤)*. Found + fixed **MA-003** *(CGF save was a silent no-op — editor had the dirty-subscription, CGF didn't)*. `InMemoryGraphSerializer` over `IGraphModel` = one id-space by construction *(better than the clipboard I specified)* |
| **CGF debug pause/step** *(was on UI-lane `qd1qpv`, harness-bound — files disjoint)* | CE-025..030 | ✅ rule-8 report complete: system suite 95/0, reds attributed by git-diff. Corrected my design in 3 places *(CGF is a SLAVE → requests via intents, no roster; k-tick drain was DQ30's REJECTED option; real class `CycloneNetworkIngressSystem`)*, folded to §10. Fixed 3 long-red `CgfLogicPackTests` **CE-030**. |

🔴 **CE-029 framing CORRECTED (user, `2026-08-25`):** the barrier does **NOT** need a "true multi-node run" — **`--mode all` runs the subsystems as separate nodes with full network**, so the barrier + `k` are provable in the EXISTING harness. 📌 The slice-4 report already found `Hrot.ClusterRunner.Integration.Tests`' **`CgfHarness` tests PASS** now *(libddsc.so present)*. ⇒ CE-029 is a cheap follow-up rail *(pause→step→resume across the `--mode all` nodes)*, not deferred hardware work — a natural pickup for the CGF session.

⭐ **Merge integration checked:** both edited `CgfSubsystem.cs` in different regions — auto-merged, `Hrot.CGF` builds 0 errors.
🔴 **ruling-9 note for the NEXT slice:** MA- already SHIPPED `GET /assets/{id}/graph/catalog` *(node-kind list, MA-004)* — the discovery/completeness slice must **EXTEND** it, ⛔ NOT build a parallel `/nodetypes`.

## 3b. ⭐ FILED FOLLOW-UPS from the overnight batches *(small, none blocking)*
- **CE-035** — `IDataBreakpointManager.RequestContinue()` cannot resume a STEPPED node *(RequestStep clears `_isPaused`)*; production bypasses via `RequestResume()`. Neutral-assembly fix, deferred.
- **CE-036** — the `Requires CycloneDDS` skips in `Hrot.ClusterRunner.Integration.Tests` are stale; real cause = **domain id 250 out of CycloneDDS range**, not missing DDS.
- **CE-018** — `EditorSubsystem`'s two inline `.csproj` walk-ups still bypass `AssetRoots` *(another lane's file)*.
- ⭐ **schema-exporter on CGF** — MCP `paramsSource` reports `none:no-exporter-wired` on CGF *(no `IActionSchemaExporter` wired there)*; a one-line CGF-lane follow-up.
- ⚠ **doc-prose coverage sweep** — `EditDoc` makes 100% node/param prose POSSIBLE; filling it across the catalog is a sweep, not done. The rail prints the % to ratchet.

## 4. ⭐ NEXT, QUEUED
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
