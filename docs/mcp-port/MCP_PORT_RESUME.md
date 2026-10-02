# RESUME — AI Debug API + MCP server port

> **rev 1 · 2026-08-06 · nothing started.**
>
> 📌 **You are a fresh session picking up this work.** Read §0 and §1 before touching anything, then §2
> for the single next action. This file exists so you never have to re-derive the context — including
> after a context compaction.
>
> **Full technical description and inventory: [../UX/MCP_PORT_PLAN.md](../UX/MCP_PORT_PLAN.md).** That
> document is the plan; this one is the orientation and the running log.
>
> **Cross-session protocol: [../SESSION_SYNC.md](../SESSION_SYNC.md).** A parallel UX session shares one
> file with you and you are expected to exchange updates both ways.

---

## 0. The job, in one paragraph

An **AI Debug API + MCP server** was built on `origin/feat/ai-debug-api` in **34 commits over two days
(2026-06-14 → 15)** — 16 batches, **49 MCP tools**, ~3.2k lines of production C# plus 4.0k of tests and a
17-file Node server. It gives an agent programmatic control of a running HROT editor: load a scenario,
spawn entities, play/pause/step, enter/exit preview, save, inspect entities, set breakpoints, checkpoint
and diff state, query logs, arm behavior traces.

**The user wants it merged and kept operational as part of the infrastructure.**

**It cannot be merged.** `feat/ai-debug-api` has **no common ancestor** with `main` or with any current
working branch: it carries the *original* project history (2137 commits, roots to 2025-12-30) while the
trunk was re-created around 2026-07-16 (`main`: 120 commits, three roots). It also lacks **all** of the
blueprint programme's work. So the job is a **forward port of ~26 files onto the trunk line**, plus ~10
hand-written lines of wiring — not a branch merge.

> ⚠ **Do not read "2137 commits" as the size of this work.** That is the branch's old project history.
> The work itself is two days and 34 commits. The topology is the obstacle; the work is small.

### Why it matters beyond itself

- It is **the editor's one network interface** — an `HttpListener` on `http://localhost:{port}/` inside
  `Hrot.Editor`. The editor is otherwise networkless by design *and in code*. The MCP server itself
  speaks **stdio** and is an out-of-process client of that listener.
- It is a **headless harness for the editor**, which the parallel UX programme wants badly: it can
  exercise most of the scenario-authoring mechanics without a GUI session.
- `DebugApiService.cs` (2140 lines) is, in effect, **an inventory of which editor capabilities are
  reachable without ImGui** — which is exactly what the UX programme's new-shell work needs to know.

---

## 1. Way of working

**Canonical statement: [../SESSION_SYNC.md §Shared work habits](../SESSION_SYNC.md#shared-work-habits).**
Condensed here so a compacted session recovers immediately:

1. **Verify before you build.** Every claim in these docs was derived by a session that could not run the
   app. Re-derive from code; fix the doc in the same commit when it is wrong.
2. **Assert the effect, never the report.** Green tests are not evidence a listener is bound or a handler
   is registered.
3. **Revert to watch it go red.**
4. **Grep the production construction sites** — inert-default dependencies have silently disabled
   features three times in this codebase.
5. **Delegate to Sonnet** the mechanical parts (copying files, adding test files, updating `.csproj`);
   stay hands-on for the `EditorSubsystem.cs` wiring and the behavior-id reconciliation. **Re-run the
   gates yourself.**
6. **Architect gate** only if this turns out to need a design decision — the port itself should not.
   If one appears, write `Architect_Question_NN` (UX numbering is at Q25; take the next free number).
7. **Ask questions in plain chat prose** — never the multiple-choice widget.
8. **Report faithfully:** red gate → say so with output; skipped step → say so.

### 🔒 Three hard constraints

**An approach that violates one of these is wrong regardless of its other merits.**

1. **`ClusterRunner` must stay fully operational, continuously.** A third session is developing blueprint
   features against it *right now*. No "will fix after the refactor" states.
2. **The construction kit survives.** The system keeps composing network-distributed variants exactly as
   today (`--mode orchestrator,simhost,cgf`, `ig`, `excon`, `all`).
3. **The editor stays networkless in the DDS/cluster sense.** Adding the loopback HTTP control plane is
   the *intended* exception. ⚠ Do **not** "helpfully" wire the `INetworkFactory` that
   `EditorSubsystem( INetworkFactory _ )` currently discards — that parameter is a dependency that looks
   injected and is not.

### Where you may write

| | |
|---|---|
| ✅ **Freely** | `Hrot.Editor/DebugApi/*` · `tools/ai-debug-mcp/*` · `.dev/_DONE/ai-debug-api/*` · the `DebugApi*Tests.cs` files · this folder |
| ⚠ **Carefully, expect a merge** | `EditorSubsystem.cs` (~10 lines) — see the [sequencing rule](../SESSION_SYNC.md#sequencing-rule); the three behavior-id files |
| ⛔ **Do not touch** | `docs/UX/*` and `docs/blueprints/*` except to append a row to [SHARED_SURFACES.md](../UX/SHARED_SURFACES.md) · the blueprint/BTree/HSM editor windows · `ClusterRunner`'s behaviour |

---

## 2. Status and next action

**Nothing started. No files ported.**

| Artefact | State |
|---|---|
| [../UX/MCP_PORT_PLAN.md](../UX/MCP_PORT_PLAN.md) | ✅ Full inventory, topology analysis, 8-step plan, rejected alternatives, 5 open questions |
| This file | ✅ rev 1 |
| Runs on | ✅ **Linux cloud is sufficient** — no Windows needed (user, 2026-08-06) |
| Branch for this session | ⚠ **not created / not recorded.** Record it in [../SESSION_SYNC.md](../SESSION_SYNC.md#the-sessions) in your first commit |
| Port | ☐ not begun |

### Next action — step 0, before any porting

**Answer the [open questions](#4-open-questions) with the user**, in particular *which branch receives
the port*. Then:

✅ **Windows is not required — confirmed by the user 2026-08-06: "mcp does not need windows at all."**
That is consistent with the code: `HttpListener` runs on .NET on Linux, the suite ships
`DebugApiHeadlessSmokeTests`, and `--headless` is a supported runner mode. **A Linux cloud session can do
this port end to end** — build, run the ADA integration tests, and drive the MCP tools against the
headless runner. No GUI is involved in the API's own verification.

⚠ Still verify early rather than assuming: that the solution builds on Linux in this container, and that
the headless runner starts. If either fails, say so rather than silently degrading the plan.

Then work [the 8-step plan](../UX/MCP_PORT_PLAN.md#the-port-plan). The judgement is concentrated in three
of those steps:

| Step | Why it needs care |
|---|---|
| 3 — wire `EditorSubsystem.cs` | ~10 lines into a 4.2k-line composition root that has changed enormously since `feat` last saw it. The one place all three programmes meet |
| 5 — reconcile the behavior-id files | `BehaviorIds.cs`, `CgfBehaviorIds.cs`, `AiBehaviorFactory.cs` may have drifted under 17 batches of blueprint work. ⚠ These touch the same behavior-id surface an open architect question ([Q25-C](../UX/Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-c--where-does-an-asset-authored-behavior-declare-its-affinity-and-its-parameters)) is about — read it before changing that surface |
| 7 — end-to-end verification | A green suite does not prove the listener is bound. Start the app, hit the loopback API, drive at least one MCP tool through the Node server |

---

## 3. Log

Append one row per working session. Keep it factual.

| Date | Session | What happened | Gates | Notes |
|---|---|---|---|---|
| 2026-08-06 | UX coordinator | Discovered the branch, established the unrelated-history topology, inventoried the port, wrote the plan and this file. **No porting.** | n/a | Facts derived from git + reading `feat` — the app was never run |

---

## 4. Open questions

<a id="4-open-questions"></a>

**Resolve with the user before porting.** Claude should not pick unilaterally.

| # | Question | Why it blocks |
|---|---|---|
| 1 | **Which branch receives the port?** Not a feature branch — this is infrastructure everything builds on. `main`? A dedicated `port/ai-debug-api` that other sessions then merge from? | Everything |
| 2 | **Does the trunk's history topology get fixed?** `main` being an unrelated 120-commit line beside a 2137-commit line is a landmine: the next person to merge any old branch hits the same wall | Not blocking this port, but the same problem will recur |
| 3 | **Is anything else stranded on `feat/ai-debug-api`?** The inventory covered only files **absent** from the trunk line. Files present on **both** but *diverged* were never compared — **there may be ADA-era improvements a port-by-addition silently drops** | Completeness of the port |
| 4 | **Does the Node toolchain matter** for build/CI, given `tools/ai-debug-mcp` sits deliberately outside `IOS-IG-SimHost.sln`? | Packaging / "stays operational" |
| 5 | **What does "stays operational" mean concretely** — is the API expected up in every editor run, behind a flag, or on an explicit port argument? | Wiring decision in step 3 |

---

## 5. Recovering from context compaction

1. Read **§0** (the job + why merge is impossible) and **§1** (how to work, and the three hard
   constraints).
2. Read **§2** for status and the next action.
3. Open [../UX/MCP_PORT_PLAN.md](../UX/MCP_PORT_PLAN.md) for the file-by-file inventory and the 8 steps.
4. Check [../SESSION_SYNC.md](../SESSION_SYNC.md) — has the UX session's branch moved? Merge it before
   starting work.
5. `git log --oneline -15` on this session's branch against §3's log. If commits exist that the log does
   not mention, **the docs are stale — reconcile them first, in their own commit.**

**Keep §2 and §3 current at the end of every working session, in the same commit as the work.** A lagging
RESUME is worse than none — six blueprint-programme docs had to be marked actively misleading for exactly
that reason.
