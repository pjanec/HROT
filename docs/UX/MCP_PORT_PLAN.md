# AI Debug API + MCP server — port plan

<!--STATUS
state: SUPERSEDED
updated: 2026-08-22
current-answer: ✅ DONE — the port was executed 2026-08-22. The CURRENT as-built state, verification, and
  the open follow-ups live in docs/MCP_Integration.md. This file is kept as the record of the port plan
  and the topology finding; do not read its "what remains" as current.
superseded-by: ../MCP_Integration.md
-->

> ✅ **DONE (2026-08-22).** The port described below was executed onto the coordinator branch. Production
> `DebugApi` code, the Node MCP server, and the `.dev/_DONE/ai-debug-api/` corpus were ported; 3 shared-file
> drifts were reconciled; the host was wired into `EditorSubsystem` (incl. behavior-trace tracer +
> record/replay controller) and **verified end-to-end headless** (record→replay, breakpoints, watch,
> entities). ⇒ **The live state is [`docs/MCP_Integration.md`](../MCP_Integration.md).** The open
> question "which branch receives the port" was answered: the coordinator branch. Deferred: the 15
> harness-dependent unit tests (`DEBT-MCP-001`). Everything below is the ORIGINAL plan, kept for the
> record and for its topology finding.

> **[Original status] DESCRIPTION ONLY, 2026-08-06.** Written because the work interacts with this
> programme in four load-bearing ways ([below](#why-the-ux-programme-cares)), and because the branch
> topology makes the obvious approach — `git merge` — **impossible**.

## What exists, and where

| | |
|---|---|
| **Branch** | `origin/feat/ai-debug-api` |
| **Tip** | `d7b2a6e1` — *"feat(ai-debug-api): BATCH-16 educating semantic errors at the API (Tier 1, C#)"* |
| **Shape** | A C# HTTP API inside `Hrot.Editor` + an external Node.js MCP server that proxies it |
| **Scale of the work** | **34 commits over two days — 2026-06-14 to 2026-06-15.** 16 batches (ADA-BATCH-01…16), 49 MCP tools, groups A–N. **~3.2k lines of production C# + 4.0k of tests + a 17-file Node server.** Recent and compact |
| **Status per its own README** | Groups A–N "fully implemented", including K (behavior traces), L (live mutation / fault injection), M (focus + annotations). ⚠ **Claimed by the branch's docs; not verified against its code by this session** |

### The two halves

```
   agent  ──stdio──▶  tools/ai-debug-mcp (Node 18+, @modelcontextprotocol/sdk)
                            │
                            └──HTTP──▶  DebugApiHost  (HttpListener, http://localhost:{port}/)
                                             │
                                             └──▶  DebugApiService ──▶ the running editor
```

**Precision worth keeping:** the MCP server itself speaks **stdio**, not network. The one genuinely
*networked* surface is `DebugApiHost` — an `HttpListener` bound to **`http://localhost:{port}/`**
(`DebugApiHost.cs:59`), in-process inside `Hrot.Editor`. So the editor stays networkless in the
DDS/cluster sense; it gains a **loopback HTTP control plane**. That is consistent with the user's
"maybe just its MCP server is one of few network interfaces" — the listener is the interface, the MCP
server is an out-of-process client of it.

## <a id="the-blocking-fact-unrelated-histories"></a>🔴 The blocking fact: unrelated histories

| Comparison | Merge base | Meaning |
|---|---|---|
| `main` ↔ `claude/reset-working-branch-qd1qpv` | `327f1433` | related — `main` is an ancestor of our branch |
| `main` ↔ `feat/ai-debug-api` | **none** | **unrelated histories** |
| ours ↔ `feat/ai-debug-api` | **none** | **unrelated histories** |

| Branch | Total commits on the branch | Root commit dates |
|---|---:|---|
| `feat/ai-debug-api` | 2137 | back to **2025-12-30** *("Core ECS with flight recorder — barebones")* |
| `main` | 120 | **2026-07-16 … 2026-08-01**, three separate roots |
| ours | 217 | (descends from `main`) |

> ### ⚠ Do not read 2137 as the size of the MCP work
>
> **It is the branch's entire project history**, going back to the original repo. The ADA/MCP work itself
> is **34 commits over two days (2026-06-14 → 2026-06-15)** — a compact, recent burst sitting on top of
> that old history.
>
> The 2137 matters for exactly one reason: it is why `git merge` is useless here. Because the two lines
> share no ancestor, git sees *all* 2137 as "not in our branch" and would try to reconcile the whole
> disjoint history — not the 34 commits anyone actually wants. **The topology is the obstacle; the work
> is small.**

**Reading:** the trunk was re-created (fresh import or squash) around mid-July 2026. `feat/ai-debug-api`
still carries the **original long project history** and was never re-based onto the new trunk. The two
lines therefore share no ancestry.

**And `feat` is the stale side on everything else:** it contains **none** of the blueprint programme's
work (0 of 8 marker files present). A tree diff from our branch to `feat` reports
`1187 files changed, 23689 insertions(+), 133729 deletions(-)` — those 133k deletions are *our* work that
`feat` lacks.

> ⛔ **Therefore: never merge our line into `feat`, and never `git merge --allow-unrelated-histories`
> in either direction.** The only sane direction is a **port of the ADA files forward onto the trunk
> line.**

## What actually has to move — the whole inventory

**26 non-doc files**, matching the two-day scale of the work:

### Production code (9 files)

| File | Lines | Note |
|---|---:|---|
| `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiService.cs` | 2140 | the endpoint implementations |
| `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiHost.cs` | 730 | `HttpListener`, routing, loopback bind |
| `Hrot/Subsystems/Hrot.Editor/DebugApi/MainThreadJobQueue.cs` | 49 | marshals API calls onto the sim thread |
| `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiSafeFloatConverters.cs` | — | JSON NaN/Inf safety |
| `Hrot/Subsystems/Hrot.Editor/DebugApi/EditorAiTracerCoordinator.cs` | — | behavior-trace arming (Group K) |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/EventSerializationHelper.cs` | — | shared |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/JsonShapeDescriber.cs` | — | shared |
| `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorIds.cs` | — | ⚠ touches the behavior-id surface [Q25-C](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-c--where-does-an-asset-authored-behavior-declare-its-affinity-and-its-parameters) is about |
| `Hrot/Subsystems/Hrot.CGF/Configuration/CgfBehaviorIds.cs` | — | same |
| `Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs` | — | |

### Tests (16 files)

`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/DebugApi*Tests.cs` — `DebugApiBatch04…16Tests`,
`DebugApiFoundationTests`, `DebugApiHeadlessSmokeTests`, `DebugApiScenarioLoadTests`,
`DebugApiServiceTests`, `EventSerializationHelperTests`.

### Non-code

- `tools/ai-debug-mcp/` — **17 files**, Node.js. Its README states it is *"an external companion, NOT part
  of `IOS-IG-SimHost.sln`"*, so it adds a **Node toolchain dependency** but no C# build coupling.
  Includes `SKILL.md` + `skill-parts/` (a generated agent skill guide) and `tool-catalog.mjs` (959 lines,
  single-source tool catalog).
- `.dev/_DONE/ai-debug-api/` — **52 docs**: `DESIGN.md`, `TASK-TRACKER.md`, `TASK-DETAIL.md`, `DEBT-TRACKER.md`,
  16 batch instruction docs + 16 reports. **Port these too** — they are the equivalent of this
  programme's own register, and dropping them would orphan the work.

### <a id="the-one-real-collision-point"></a>🔴 The one real collision point

```csharp
// on feat/ai-debug-api — EditorSubsystem.cs
:198    private Hrot.Editor.DebugApi.DebugApiHost?  _debugApiHost;
:1427   _debugApiHost = new Hrot.Editor.DebugApi.DebugApiHost( … );
:1433   var debugService = new Hrot.Editor.DebugApi.DebugApiService( … );
```

**`EditorSubsystem.cs` is the wiring site — and it is the single most contended file in the repo.** It is
~4.2k lines on our side, carries 17 batches of blueprint changes `feat` has never seen, is the editor's
composition root, and is listed as **co-owned** in [SHARED_SURFACES.md](SHARED_SURFACES.md). The port's
only hand-written work is roughly **10 lines here** — but they land exactly where two other programmes
are active.

## The port plan

**Approach: port the files, re-do the wiring by hand. No history merge.**

**Expected size of the port itself:** copy 26 files + two directories (all new paths, no conflicts),
hand-write ~10 lines in `EditorSubsystem.cs`, reconcile 3 behavior-id files, then build and verify.
The judgement is concentrated in steps 3, 5 and 7 — everything else is mechanical.

| # | Step | Notes |
|---|---|---|
| 1 | **Decide the target branch.** Not this UX branch — this is infrastructure everything else builds on | ⚠ [open question](#open-questions) |
| 2 | `git checkout origin/feat/ai-debug-api -- <the 26 files + tools/ + .dev/>` | Mechanical. No conflicts: every path is **new** on the trunk line |
| 3 | **Hand-wire `EditorSubsystem.cs`** — field + construction, ported into today's composition root | The only judgement call. ~10 lines |
| 4 | Add the 16 test files to `Hrot.ClusterRunner.Integration.Tests`; check the `.csproj` needs nothing | |
| 5 | Reconcile the three behavior-id files against the current behavior surface | ⚠ `BehaviorIds.cs` / `CgfBehaviorIds.cs` / `AiBehaviorFactory.cs` may have drifted — the trunk has 17 batches of blueprint work |
| 6 | Build + run **all** gates, including ClusterRunner's own | 🔒 `ClusterRunner` must stay operational |
| 7 | Run the ADA integration tests; then verify **end-to-end**: start the app, hit the loopback API, drive one MCP tool | A green suite is not proof the listener is wired — [trap #8](UX_Programme_Briefing.md#6-inherited-traps) |
| 8 | Record the port in `.dev/_DONE/ai-debug-api/TASK-TRACKER.md` and in [SHARED_SURFACES.md](SHARED_SURFACES.md#proposed-changes-awaiting-consultation) | It changes a co-owned file |

**Rejected alternatives**

| Option | Why not |
|---|---|
| `git merge --allow-unrelated-histories` | Would try to reconcile two disjoint project histories: 1187 files, 133k deletions in one direction. Catastrophic |
| Cherry-pick the 16 ADA batch commits | Cherry-pick works across unrelated histories (it applies patches), but every batch touches `EditorSubsystem.cs`, so you would resolve the same conflict 16 times against a file that has since changed enormously |
| Leave it on its own branch, run it separately | The HTTP host is **in-process** in `Hrot.Editor`, so this needs two divergent builds of the app. Defeats *"stay operational, part of infrastructure"* |
| Rebase `feat` onto the trunk | No common ancestor, so this is not a rebase of the 34 ADA commits — it is a replay of the branch's entire 2137-commit project history |

## Why the UX programme cares

<a id="why-the-ux-programme-cares"></a>

Four reasons, in descending order of usefulness to us:

1. ⭐ **It is a headless harness for the golden path — and it partly lifts this session's biggest
   limitation.** The [coordinator cannot run the editor](UX_Programme_Briefing.md#510-session-topology),
   which is why ~20 golden-path steps are unverified *predictions*. But the API exposes
   `load_scenario` · `spawn_entity` · `play`/`pause`/`step` · `enter_preview`/`stop_preview` ·
   `save_scenario` · `list_entities` · `send_entity_command` — i.e. **most of Path A's mechanics minus
   the UI**. That does not test *usability*, which is the point of the walk, but it could turn
   "does this even work?" from a prediction into a fact without a Windows session.
2. **It is an independent inventory of what the editor can actually do.** 49 tools mapped 1:1 to
   endpoints is a second opinion on the [capability inventory](UX_Golden_Path.md#capability-inventory)
   the reconnaissance walk will build — and it was derived by someone else, from the other direction.
3. **It is evidence for [UXD-09](UX_Design.md#uxd-09) / [Q25-F-iii](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-iii--how-do-we-combine-the-content-of-existing-windows-into-new-composite-panels).**
   Every endpoint `DebugApiService` implements is a capability already reachable **without ImGui**. That
   is exactly the "is the logic reachable headlessly?" column the new shell needs — **someone has already
   answered a large part of it.** Read `DebugApiService.cs` before hunting for seams by hand.
4. **Groups H and M feed open architect questions.** `checkpoint` / `restore_checkpoint` /
   `capture_diff_baseline` / `diff_state` are directly relevant to
   [Q25-A](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-a--how-do-we-spend-a-cheap-recoverability-budget)
   (recoverability, and specifically Q25-A′ on snapshot cost); `focus_entity` / `add_annotation` touch
   selection and the map surface. ⚠ **Unverified** — the README claims them; nobody has read that code.

### And one obligation it creates

Once ported, the API is a **second consumer of the editor's internals**, on a par with the UI. The new
shell must not break it, and it therefore belongs in
[SHARED_SURFACES.md](SHARED_SURFACES.md#co-owned-surfaces) as a co-owned surface with its own tests as
the gate.

## Open questions

<a id="open-questions"></a>

For the user — Claude should not pick any of these unilaterally:

1. **Which branch receives the port?** `main`? The blueprint line? A dedicated `port/ai-debug-api`
   branch that both programmes then rebase onto? It is infrastructure everything else depends on, so it
   should not land on a feature branch.
2. **Is the trunk's history topology going to be fixed?** `main` being an unrelated 120-commit line while
   a 2137-commit line holds the real history is a landmine — the next person to try merging any old
   branch hits the same wall. Worth a decision independent of this port.
3. **Is anything else stranded on `feat/ai-debug-api`?** This session inventoried only what is *absent
   from our line*. Files that exist on **both** but diverged were not compared — **there may be ADA-era
   improvements to shared files that a port-by-addition would silently miss.**
4. **Who does it?** It needs a Windows session (build + the end-to-end listener check) and it touches the
   most contended file in the repo. Suggest its own session with its own handoff, sequenced **before**
   the new shell work starts, so the `EditorSubsystem.cs` wiring lands once rather than twice.
5. **Does the Node toolchain requirement matter** for the build/CI story, given `tools/ai-debug-mcp` is
   deliberately outside the `.sln`?
