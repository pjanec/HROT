<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: the whole file. Written for a FRESH session after compaction; assumes no prior
  conversation. This is the TIME lane session, repurposed to Stride and next to the MCP harness.
known-conflict: none.
-->
# RESUME — the **TIME → STRIDE** implementation session

> ⛔ **Self-contained. Assumes no prior conversation.** ⭐ You are an **implementation** session, not
> the coordinator. A separate coordinator session owns the tracker's other areas and writes handoffs.

## 0. ⭐⭐⭐ WHO YOU ARE, AND WHERE

| | |
|---|---|
| **branch** | ⭐ `claude/time-system-refactor-batch-104-gp617x` — **everything is committed and pushed** |
| **head at write time** | `b9ab83b0e` |
| **coordinator branch** | `claude/blueprint-authoring-status-gm0akp` |
| **the other implementation lane** | `claude/hrot-implementation-j1jvin` — UI/variables, currently on the **Details window**. ⚠ Their `EditorSubsystem.cs` is identical to ours; they are not editing it |
| ⭐ **id prefixes YOU own** | **`TM-`** *(time, tracker Area H)* · **`ST-`** *(Stride, tracker **Area I**)*. ⛔ Never `BP-` — that is the UI lane |
| ⚠ **the coordinator dispatches with its own prefix** | the next batch uses **`HN-`** — use what the handoff says |

## 1. ⭐⭐ THE VERY FIRST THING TO DO

```bash
bash scripts/cloud-bootstrap.sh          # if `dotnet` is missing or the graph tools are absent
export PATH="$PATH:$HOME/.dotnet"        # ⛔ dotnet is NOT on PATH by default in this container
git fetch origin claude/blueprint-authoring-status-gm0akp
git merge origin/claude/blueprint-authoring-status-gm0akp --no-edit    # rule 7
git commit --allow-empty -m "chore: started <batch> at <dispatch-sha>" && git push   # rule 1b, BEFORE any code
```

## 2. ⭐⭐⭐ THE NEXT TASK — **the MCP system-test harness**

📄 **Dispatch: `docs/blueprints/batches/HANDOFF_MCP_Harness.md`** *(on the coordinator branch,
stamped `619b90756`; ids **`HN-`**)*.
📄 **Design: `docs/DESIGN_MCP_System_Test_Harness.md`** — marked **READY-TO-BUILD**, carries the UML.
⚠ At write time the coordinator is **6 commits ahead**; the newest (`8ad6d6aaf`) adds MCP-extensions
UML and seam corrections. **Read the design AFTER merging, not before.**

⭐ **Read the design end to end before touching code** *(`R-129`)*, and check its `classDiagram` /
`sequenceDiagram` against what you build *(obligation ③)*. If you deviate, **fold the as-built back
into the design before the batch closes** *(obligation ⑤)* — not only into the batch report, which
nobody re-reads.

## 3. ✅ WHAT THIS SESSION FINISHED — **all pushed, all gated**

| batch | what |
|---|---|
| **TM-110** | `T7` — the two remote caches measured. Not collapsed *(disjoint nodes)*; the **fold** was the duplicate ⇒ `ClusterTimeObservation`. Found `HaltReason.Unknown` reachable after every pause *(⇒ `PauseBarrierPending`)* and **a step silently dropped in the barrier window** *(⇒ queued)* |
| **TM-111** | `T5` — the pause notions enumerated *(98 declarations ⇒ 20 production)*. `ExCon` had **3 documented properties never written**; SimHost's Play/Pause/Step were **inert**. Both routed |
| **ST-101** | the **Bullet Stride port** from `origin/stride-integ-1` — 2 libraries, 3 test projects, the host, the additive nav + raycast seams, `CrowdAgentUpdateSystem` made authority-conditional **per entity**, the twelve `EditorSubsystem` host members, the mannequin animation descriptor |

📄 Designs: `docs/blueprints/DESIGN_Time_Architecture.md` **§15** *(T7)* · **§16** *(T5)* ·
`docs/DESIGN_Stride_Port.md` **§6** *(the Stride as-built)*.
📄 Batches: `batches/BATCH_TM110_The_Barrier_Window.md` · `BATCH_TM111_The_Inert_Controls.md` ·
`BATCH_ST101_The_Stride_Port.md`.

## 4. ⛔⛔ GATE BASELINES — **measured this session; do NOT re-derive, and do NOT read a raw count as a regression**

| suite | baseline | ⚠ |
|---|---|---|
| main solution `IOS-IG-SimHost.sln` | **0 errors** | builds |
| `Fdp.Toolkits.Tests` `~Navigation` | **295 / 0** | |
| `Fdp.Toolkits.Tests` `~Physics` | **31 / 0** | |
| `Fdp.Toolkits.Tests` time filter *(`~ClusterTimeObservation\|~HaltReason\|~MasterSyncController`)* | **64 / 0** | |
| `~ThePauseFlagOnTheClockIsFalseWhilePausedTests` | **4 / 0** | |
| `Fdp.ModuleHost.Tests` | ⚠ **192 / 6** | ⛔ the 6 are **pre-existing, named in `TM-023`** *(Convoy/SoD)* |
| `Hrot.ExCon.Tests` | **378 / 0** | stable across 3 runs |
| `Hrot.Editor.Tests` | ⚠ **207 / 2** | ⛔ `ScenarioMenuTests` ×2, **pre-existing** *(`ST-012`)* |
| `Hrot.Blueprints.Tests` `~Hrot.Blueprints.Tests.Editor` | **1032 / 0**, 9 skipped | ⭐ **THE gate for anything touching `EditorSubsystem`** |
| `Hrot.ClusterRunner.Integration.Tests` `~TimeControlIntegrationTests` | **9 / 0** | ⭐ the cross-node invariant *(rule 8 row 8)* |
| `~BreakpointSubsystemWiringTests` | **25 / 0** | |
| `Fdp.Examples.Scenarios.Tests` | **56 / 0**, 12 skipped | |
| animation ×3 | **195 / 0 · 15 / 0 · 31 / 0** | ⛔ the first two are **not in the solution** — `dotnet restore` them first |
| `Hrot.Presentation.Tests` | ⛔⛔ **UNGATEABLE BY COUNT** *(`TM-032`)* | 3 clean-HEAD runs: **29/0 · 99 w/3 · 99 w/4** — the DISCOVERED TOTAL rotates. **Filter it** |
| `Hrot.SimHost.Tests` | ⛔⛔ **UNGATEABLE BY COUNT** *(`TM-036`)* | 4 runs: **12 · 14 · 13 · 10** failures, names rotate. ⭐ **Stable core is 8**: `CgfLogicPackTests` ×3 + `HillAttack*` ×5. **Filter it** |
| `Fdp.Toolkits.Tests` whole-suite | ⛔ **`DEBT-AIB-030`** — rotating flakes | filter |

⚠⚠ **`tracker-counts.py --check` counts only `**BP-` rows** ⇒ **`TM-`/`ST-` rows are invisible to
it.** Its OK is **not** evidence about your rows.
⚠ `rulings-check.py` currently emits **1 staleness WARN on `.claude/CLAUDE.md`** — arrived with a
coordinator merge, **not** a defect of yours.

## 5. ⛔⛔⛔ THE TRAPS THIS SESSION HIT — **each cost real time**

| # | trap | the habit that fixes it |
|---|---|---|
| **①** | ⛔⛔ **`--no-build` runs a STALE dll and prints PASSED.** Hit **twice**: `Hrot.ExCon.Tests` reported `85/0` *(really 378)*; `Hrot.Editor.Tests` reported `209/0` while carrying 2 reds from an unmerged-into-the-binary coordinator change | ⭐ **build the SOLUTION before a gate run**, and treat a green as only as fresh as the last thing that forced a rebuild |
| **②** | ⛔⛔ **A SCOPED grep read as an ABSENCE claim.** I said the `CharacterAnimationDefDto` family "does not exist on this line" — it does; I had grepped only `FDP/Toolkits/` | ⭐ **look where the thing actually lives**; use `search_graph` to enumerate, never grep to prove absence |
| **③** | ⛔⛔ **A lane rule about FILES applied without measuring the EDIT.** I refused the twelve `EditorSubsystem` members as "cross-lane"; five already existed as `internal`, they were the Stride port's own seam, and the UI lane had no live edit | ⭐ **`git diff HEAD origin/<other-lane> -- <file>`** answers "is anyone standing on this" in one command |
| **④** | ⚠ **A rotating flake looks exactly like fix-one-break-one.** SimHost went 12 → 15 with the same passed count | ⭐ **sample clean HEAD 3×** before calling anything a regression *(`TM-015`'s lesson)* |
| **⑤** | ⚠ **`--` inside an XML comment breaks msbuild**; `dotnet` is not on `PATH` | ⭐ trivial, but each cost a cycle |

## 6. ⚠ STRIDE — **what is true, and what is unverified**

⭐ **Compile-verified, never RUN.** `net8.0-windows` builds here **only** with
`-p:EnableWindowsTargeting=true` *(on restore AND build)*; the suites **cannot execute** — no
`Microsoft.WindowsDesktop.App` runtime for linux-x64; `HrotStrideApp.Windows` **cannot build** at all
*(Stride asset compiler, `--platform=Windows`, exit 150)* — ⭐⭐ **confirmed pre-existing at the base
commit in a worktree.**
⇒ 📄 **`docs/Stride_Host_Visual_Test.md`** — the Windows launch command and what a human should see.
⚠ **Open: `ST-013`** — `CivilianPedestrian` renders as a mannequin but has no animation descriptor
*(matched from the branch deliberately)*.

## 7. ⭐ STANDING RULES THAT BIND YOU

- ⭐⭐⭐ **Read `docs/blueprints/RULINGS.md` in full at session start** *(RULE ZERO)*, then
  `python3 scripts/design-digest.py` and `python3 scripts/rulings-check.py`.
- ⭐⭐ **Intent lives in the DESIGN docs, not the code** *(`R-129`)* — `docs/` first, then `.dev/`.
- ⭐⭐ **Enumerate with `search_graph` before any design or exhaustive claim**; grep only confirms.
- ⭐ **Diagrams live in DESIGNS, never in batches**; validate with
  `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file>` *(npm-install mermaid@11 + jsdom into
  `/tmp/mm` first)*.
- ⭐ **The variable-model FREEZE still binds**: `Hrot.Editor.AiShared`, variables, blackboard, the
  Details panel belong to the UI lane. ⚠ **But "same file" is not "same work"** — measure before
  refusing *(trap ③)*.
- ⭐ **Ask in plain prose, never the multiple-choice widget.** ⭐⭐ **Always give GitHub links** —
  `https://github.com/pjanec/HROT/blob/claude/time-system-refactor-batch-104-gp617x/<path>` — the user
  is often on mobile.
- ⭐ **Report gates as a table**: verbatim command · pass/fail · delta vs baseline · every red
  confirmed pre-existing **by name** against the base sha.
