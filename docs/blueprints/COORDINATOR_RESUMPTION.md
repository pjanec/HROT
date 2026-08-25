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

## 3. 🔴 IN FLIGHT — verify + merge when each returns *(snapshot `2026-08-25`)*
| session | branch | from sha | ids | verify checklist |
|---|---|---|---|---|
| ⭐ **MCP authoring** *(RUNNING — started `393612b60`)* | `claude/blueprint-macro-feature-sdmspn` | `8cf450cec` | `MA-` | 📄 `DESIGN_Mcp_Authoring.md`. ⭐⭐ **the read exposes IN-MEMORY guids, NOT the save serialization** *(two id spaces — §3)*; own route file `DebugApiService.Authoring.cs`; round-trip rail *(read→add node+link→re-read shows them→save+reload)*; every route has a RouteDoc **+ a handler in `src/index.mjs`** *(CE-009 §4c gap)*; `test-catalog` green |
| ⭐ **Debug pause/step (slice 4, DQ30)** *(handoff issued at `a0e47e518`; NOT yet started — needs a NEW branch, NOT `sdmspn` which authoring holds)* | *(new)* | `b47af0919` | `CE-` | 📄 `..._Slice4_Debug_PauseStep.md`. `CgfClusterDebugTimeController` replaces the empty `CgfNoOpTimeController`; pause→step→resume rail on a REAL `--mode all` cluster; k-tick barrier drain; DQ30-A halt sim not kernel tick; DQ30-E logs, no modal |

⚠ **The two are PARALLEL-SAFE** *(disjoint files: authoring = DebugApi/catalog; pause-step = CgfSubsystem/time)*.
⛔ The one shared risk: a pause-step test-only MCP hook touching the generated catalog ⇒ coordinate the regen.

## 4. ⭐ NEXT, QUEUED *(all sequence AFTER the running MCP mutation batch merges — shared DebugApi/catalog)*
1. ⭐⭐ **MCP DISCOVERY surface** *(user, `2026-08-25`)*: list node kinds + a kind's property schema + a node's current properties, **auto-discovered from the registries** *(`INodeCatalog.All` = kinds+pins; `IActionSchemaExporter.DtoFields` = editable params)* — the READ companion that makes §7② add-node/set-param usable. 📄 **`DESIGN_Mcp_Authoring.md` §10** *(READY-TO-BUILD; carries UML + a schema-coverage rail = the "measured not authored" proof)*. ⛔ handoff NOT yet issued *(the mutation batch holds the route file)*.
2. **B — generic UI-command invoke over MCP** *(the user's "UI control necessity")*: `POST /commands/{id}`+params over `EditorCommandDescriptor`/`GlobalActionRegistry`. Not yet designed.
3. **Axis B** *(map/entity parity — UXI-11/23/10/29)* — gated on the **UXI-30** engine-authority-gate design *(no design doc yet)*.
4. **Watch/variable follow-ups** *(if the UI lane resumes)*: BP-503 restore-from-file reattach; BP-504/514 ruling-9 dup cleanups.

⇒ ⭐ **MCP slice order:** mutation *(RUNNING, MA-)* → **discovery (§10)** → B *(UI-command invoke)*. Each branches from the prior merged base; ⛔ never concurrent on `DebugApiService.Authoring.cs` + the generated catalog.

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
