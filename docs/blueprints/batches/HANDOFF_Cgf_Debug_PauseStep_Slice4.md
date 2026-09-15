<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-25
current-answer: dispatch pointer for cgf==editor SLICE 4 (DQ30) — debug pause/step on CGF. Carries NO
  design: cites DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md (classDiagram + sequenceDiagram).
known-conflict: none. ✅ PARALLEL-SAFE with the MCP-authoring session — this touches CgfSubsystem + the
  time layer; authoring owns DebugApiService.Authoring.cs + the generated catalog. Disjoint files.
-->
# HANDOFF — **cgf==editor slice 4 (DQ30): debug pause/step on CGF** *(CGF / backend lane)*

> 📌 **Dispatched at `b47af0919`.** ⛔ **Scope FROZEN at that sha.** ⭐ **Branch fresh from
> `claude/blueprint-authoring-status-6sr5ld`** *(rule 7)* — ⚠ **a NEW branch, distinct from the concurrent
> MCP-authoring session's**; **rule 1b: started-marker naming `b47af0919` BEFORE any code.** ⛔ **No PR.**
> ⭐ **You allocate the ids** *(rule 3)* — continue the `CE-` series, tracker **Area L**; state every id.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER
📄 **[`DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md`](../../DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md)**
*(READY-TO-BUILD)* — §2 inventory, §3 how CGF differs from the editor adapter, §4 classDiagram, §5
sequenceDiagram, §6 items, §7 gates, §8 lane. ⭐ Build what §4/§5 draw; report the match *(obligation ③)*;
fold deviations into the design *(obligation ⑤)*. 📄 Decisions: `Design_Question_30` *(A–E)* ·
`UX_Feature_Cgf_Brain_Diagnostics.md` *(UXI-37 — "the fix is ONE class")* · ruling 53 / CE-024.

## 1. ⛔⛔ NEW BUILD/TEST RULES APPLY
`.claude/CLAUDE.md` → THREE TEST TIERS → the `2026-08-24` rule. Build the AFFECTED PROJECT *(`Hrot.CGF` ·
`Hrot.Diagnostics.Breakpoints` · the time/orchestration project · `Hrot.SystemTests`)*, never the whole
solution in the fix loop. E2E/system suite is T3 — async. Pre-existing reds proven by `git diff`, not rebuild.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §6)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **`CgfClusterDebugTimeController : IEngineDebugTimeController`**, mirroring `MasterSyncTimeControllerAdapter` but with the **REAL cluster roster**; construct it in `CgfSubsystem` and pass it to `DataBreakpointManager` **instead of** `CgfNoOpTimeController` *(retire the no-op)* | ⛔ **halt SIM systems via `TogglableSimulationGroup`, NOT the kernel tick** *(DQ30-A — else resume can't arrive)* |
| ⭐ **②** | **The k-tick barrier drain on resume** — CGF halts at hit tick T, the cluster at barrier T+k ⇒ drain the k queued ingress ticks | ⛔ don't assume T == the barrier |
| ⭐ **③** | **DQ30-C ingress gating** — no world-state ingress while frozen; the control plane keeps polling | ⛔ freezing the control plane deadlocks resume |
| ⭐ **④** | **DQ30-E** — an unanswered freeze halts CGF locally and **LOGs** *"cluster still running"* | ⛔ **no modal on a headless node** *(ruling 53; the CE-024 correction)* |

## 3. ⭐ HOW TO TEST *(design §7)*
Headline rail: set a data breakpoint on a CGF-owned entity → run → **assert paused** *(`IsPausedByDebugger`
true, sim advanced 0)* → **step** → exactly one tick advanced → **resume** → running; shown RED by reverting
to the no-op. ⛔⛔ **name + run the integration/conformance suite that boots a real `--mode all` cluster** —
the barrier is only real with slaves *(run filtered if flaky, or state with base-sha evidence why it cannot
gate)*. ⭐⭐⭐ **You MAY extend the harness/MCP** to set a breakpoint / read paused-state for the test — ⛔ but
if that adds an MCP route touching the **generated catalog** *(`tool-catalog.mjs`/`SKILL.md`/`DebugApiHost`)*,
⚠ **coordinate the regen with the coordinator** *(the authoring session shares those files)*; ⛔ don't fake a pass.

## 4. ⭐ LANE, SCOPE & COLLISION *(design §8)*
⭐ **Yours (CGF/backend lane):** `CgfSubsystem.cs` *(construct the adapter, retire the no-op)* · the new
`CgfClusterDebugTimeController` · the time/orchestration wiring · `Hrot.SystemTests/**`.
✅ **PARALLEL-SAFE with the MCP-authoring session** — disjoint files *(it owns `DebugApiService.Authoring.cs`
+ the generated catalog; you own `CgfSubsystem` + the time layer)*. ⚠ the ONE shared risk is a test-only MCP
hook touching the generated catalog *(§3)* — coordinate it. ⭐ **Rule 4:** re-pull coordinator before the
final commit. ⛔ Not: the §17 Soft/Hard reload classification *(CE-023)*; map/Axis B.

## 5. GATES *(rule 8 contract)*
one row per gate · verbatim command · counts · delta vs `b47af0919` · `--no-build` column · pre-existing reds by diff · `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · the `CE-` ids. **Row 8 rails:** the pause→step→resume headline on a real cluster *(RED by reverting to the no-op)* · sim advances 0 while paused, exactly 1 tick on step · the unanswered-freeze LOG path · the integration/conformance suite as the cross-node gate.

## 6. ⭐ WHEN DONE
Fold the as-built into the design; flip the gap-map `CgfClusterDebugTimeController` row; state the `CE-` ids; report whether the k-tick drain and DQ30-C gating behaved as designed. The report points at the design. Report per obligation ③.
