<!--STATUS
state: LIVE
build-state: DISPATCH — AUTONOMOUS overnight run. CGF continuation: finish slice-4's open barrier proof, then
  the next READY editing conversions (asset roots from config; real main-toolbar on CGF).
updated: 2026-08-25
current-answer: this is a POINTER + the autonomy protocol. Sources named per item below. ⛔ Only READY-TO-BUILD
  (designed) items are in scope; the DESIGN-OPEN ones (§4) are explicitly EXCLUDED because they need a design
  decision with blast radius — not autonomous-safe.
known-conflict: ✅ PARALLEL-SAFE with the concurrent MCP session. That session owns the DebugApi authoring
  surface + the generated catalog; YOU own CgfSubsystem + the CGF time/debug files + Hrot.ClusterRunner.Integration.Tests.
  ⛔ Do NOT touch DebugApiService.Authoring.cs / InMemoryGraphSerializer / tool-catalog.mjs / SKILL.md, and
  ⛔ do NOT touch Hrot.Editor.AiShared internals (variable-model freeze) beyond additive.
-->
# HANDOFF — **cgf==editor: finish slice 4 + next editing conversions** *(AUTONOMOUS overnight — CGF lane)*

> 📌 **Dispatched at `9c0b62991`** *(coordinator HEAD — confirm with `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ **Branch FRESH from `claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: push an empty
> `chore: started cgf editing-conversions at <sha>` marker BEFORE any code.** ⛔ **No PR.** ⭐ **You allocate the
> ids** *(rule 3)* — continue the **`CE-`** series *(Area L)* from `CE-031`; state every id *(rule 5)*.

## 0. ⛔⛔⛔ THE AUTONOMY PROTOCOL — **does NOT stop on unknowns** *(user, `2026-08-25`)*
Identical to the MCP run's §0 *(see `HANDOFF_Mcp_Discover_Complete_Invoke_Autonomous.md` §0)*:
- ⭐⭐⭐ **DECIDE-and-log** on any ambiguity *(DECISION LOG + PROGRESS LOG live in your report, commit periodically)*.
- ⭐⭐ **STOP the ITEM, never the batch** *(R-106)* — only a design premise measured false with blast radius halts an item.
- ⭐⭐ **subagents** for parallel/mechanical work; ⭐ **DONE is the rails** *(§3)*, not a feeling.
- ⚠ **Low risk by design** — this is wiring + a rail + a config read, all on already-designed mechanisms.

## 1. ⛔ NEW BUILD/TEST RULES APPLY
`.claude/CLAUDE.md` THREE TEST TIERS + the `2026-08-24` rule. Build the **AFFECTED PROJECT** *(`Hrot.CGF` ·
`Hrot.SimHost.Tests` · `Hrot.ClusterRunner.Integration.Tests` · the time/orchestration project)*, ⛔ never the whole
solution in the fix loop. Build once, then `--no-build`. The system/integration suite is **T3 — background it.** Reds
proven pre-existing by `git diff` against the started-marker.

## 2. ⭐⭐⭐ WHAT TO BUILD *(three READY items — order as written; each is independent, R-106)*

### 2.1 ⭐⭐⭐ CE-029 — **prove the pause/step barrier across `--mode all` + measure `k`** *(finish slice 4)*
> 🔴 **Framing corrected by the user, `2026-08-25`:** `--mode all` **runs the subsystems as SEPARATE NODES with full
> network** ⇒ the cluster-wide barrier is provable in the **EXISTING** harness — ⛔ NOT deferred hardware. 📌 The slice-4
> report already found `Hrot.ClusterRunner.Integration.Tests`' **`CgfHarness` tests PASS** now *(libddsc.so present)*.
- 📄 Design: [`DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md`](../../DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md) §7/§10.7 · `Design_Question_30` §3 *(measure `k` once; ⛔ "do not treat 'small' as verified")*.
- Build a rail in **`Hrot.ClusterRunner.Integration.Tests`** *(the CgfHarness — ⛔ NOT the shared conformance file the MCP session may touch)*: on a real `--mode all` cluster, a data breakpoint on a CGF-owned entity → run → **assert every node halts on the same tick** *(the barrier)* → step → exactly one tick advances → resume → running. **Measure `k`** *(the CGF-halt-tick vs cluster-barrier-tick gap)* and record it in the design §10.
- ⭐ Red-proof by reverting to `CgfNoOpTimeController` *(inverse edit, ⛔ not `git checkout`)*.
- ⚠ If the `HrotRunnerHarness` DDS-participant crash blocks the full suite, run the CgfHarness FILTERED and state it with base-sha evidence *(the report already characterised this)*.

### 2.2 ⭐⭐ Ruling 67 — **asset roots from config** *(the one true authoring blocker)*
- 📄 Gap map row: *"Asset roots from config (delete the `.csproj` walk-up) — roots are `null` on a deployed node."* 🕳️ **build, designed.**
- Replace the dev-only `.csproj` walk-up root discovery with **roots read from config** so a DEPLOYED CGF node resolves asset roots *(dev keeps working)*. ⭐ Mirror how the editor/other subsystems read their roots from config.
- Rail: a CGF node with config-provided roots resolves + lists its assets *(the `GET /assets` catalog is non-empty on a path with no `.csproj` above it)*.

### 2.3 ⭐ CE-016 — **the real main-toolbar on CGF**
- 📄 Gap map / slice-2 §7: `main-toolbar` publishes on CGF but is **EMPTY** — `EditorSubsystem` is the only caller of `MainToolbar.RegisterEntry`. ⭐ Wire the toolbar entries CGF should carry *(mirror the editor's asset/diagnostics entries; ⛔ do not invent new features — register the ones that already exist and apply to CGF)*.
- Rail: `ClusterConformanceRails`-style — the `main-toolbar` PanelKind on CGF is **non-empty** and its entries match the editor's for the shared set. ⚠ **put this rail in a DISTINCTLY-NAMED new file** to avoid a merge race with the MCP session's conformance additions.

## 3. ⭐⭐ DONE — the rails
| item | rail |
|---|---|
| CE-029 | the `--mode all` barrier rail *(same-tick halt · one-tick step · resume)* green; `k` measured + recorded; red-proof by no-op revert |
| ruling 67 | a deployed-shape node *(no `.csproj` above)* resolves config roots + lists assets |
| CE-016 | the CGF `main-toolbar` PanelKind is non-empty and matches the editor for the shared entries |
| all | affected-project builds green; the CgfHarness/system suite named + run *(T3, background)*; tree clean |

## 4. ⛔⛔ OUT OF SCOPE — **NOT autonomous-safe, do NOT attempt** *(they need a design decision with blast radius)*
- **AQ25 authoring shell / role-&-mode gating / undo / autosave / problems-list** — architect-UNANSWERED *(Answers table empty)*.
- **Behavior-affinity registry (Q25-C)** — pivotal unknown *(can `BehaviorUiCompiler` be schema-driven?)*.
- **Packaging: `Hrot.Editor`'s catalog/`NewAssetService`** — the move-to-shared-vs-reference decision *(flag it, don't decide it)*.
- **Axis B (map/entity parity, UXI-30)** — the one open DESIGN item; engine-gated.
⇒ ⭐ If you hit one of these while doing 2.1–2.3, **record it in the DECISION LOG and route around it** — do not design it.

## 5. ⭐ LANE, SCOPE & COLLISION
⭐ **Yours:** `CgfSubsystem.cs` · the CGF time/debug files · `Hrot.CGF/**` · `Hrot.ClusterRunner.Integration.Tests/**` ·
the config-root plumbing · a new distinctly-named conformance rail file. ✅ **Parallel-safe with the MCP session** — it owns
`DebugApiService.Authoring.cs` + `InMemoryGraphSerializer` + the generated catalog *(`tool-catalog.mjs`/`SKILL.md`/`DebugApiRouteDocs`)*.
⛔ **Do NOT touch those, `Hrot.Editor.AiShared` internals *(freeze)*, or add an MCP route** *(that would race the catalog regen)*.
⭐ **Rule 4:** re-pull coordinator before the final commit.

## 6. GATES *(rule 8 contract)* + WHEN DONE
One row per gate · verbatim command · counts · Δ vs the started-marker · `--no-build` column · pre-existing reds by `git diff` ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · the `CE-` ids · tree clean after every suite.
⭐ **When done:** fold the as-built into the owning designs *(slice-4 §10 for CE-029's `k`; the gap-map rows for roots + toolbar; obligation ⑤)*;
mark the gap-map rows ✅; state the ids; the report carries the DECISION + PROGRESS logs and points at the designs.
