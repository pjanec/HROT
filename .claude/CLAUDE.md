## Codebase Memory MCP

**MANDATORY: use Codebase Memory MCP graph tools FIRST — before reading files or making code changes.**

This rule applies to every request involving this codebase.

> **Cloud / Claude Code on the web:** if the `mcp__codebase-memory-mcp__*` tools are
> not connected or `dotnet` is missing, this is a fresh cloud VM — run the
> `/cloud-bootstrap` skill (or `bash scripts/cloud-bootstrap.sh`) to install the
> .NET 8 SDK and the codebase-memory-mcp server. Note: if the binary is installed
> mid-session, the graph tools connect on the **next** session (MCP servers spawn
> at session start). For session-#1 tools, run the bootstrap from the environment's
> Setup script. Details: `docs/cloud-codebase-memory-mcp.md`. (Local Windows/VS Code
> sessions already have the tools — skip this.)

Always call `list_projects` first when you do not already know the project name, then use the `display_name` or exact `name` returned by that tool.

```json
// Step 0 — discover project names
mcp_codebase-memo_list_projects()

// Step 1 — use the project identifier returned above
mcp_codebase-memo_get_architecture({ "project": "<display_name>" })
```

### Workflow

0. **If `list_projects` returns an empty list** (a fresh cloud VM re-indexes each
   session — the graph is not persisted): **immediately** call
   `index_repository(repo_path="<repo root>")` for this repo, **without asking**,
   then continue. Indexing ~5k C# files takes only tens of seconds. On Linux the
   repo root is the current working directory (e.g. `/home/user/IOS-IG-SimHost-FDP`).
1. Call `list_projects` to discover the correct project name.
2. Call `get_architecture(project)` to understand the codebase structure.
3. Use `search_graph` to find relevant symbols, `trace_call_path` for call chains.
4. Use `get_code_snippet` to read specific function implementations.
5. Only use `read_file` when you need exact raw content to edit a specific line.

### Available Tools (14 MCP tools)

**Indexing:**
- `index_repository(repo_path)` — Index a repository into the knowledge graph
- `list_projects` — List all indexed projects with node/edge counts
- `delete_project(project)` — Remove a project and all its graph data
- `index_status(project)` — Check indexing status

**Querying:**
- `search_graph(name_pattern, name_scope, label, file_pattern, exclude_file_pattern)` — Structured search by label, name/qualified_name, include/exclude file globs
- `trace_call_path(function_name, direction, depth)` — BFS call chain traversal
- `detect_changes(project)` — Map git diff to affected symbols + risk
- `query_graph(query)` — Execute Cypher-like graph queries (read-only)
- `get_graph_schema(project)` — Node/edge counts, relationship patterns
- `get_code_snippet(qualified_name)` — Read source code for a function
- `get_architecture(project)` — Codebase overview: languages, packages, routes, hotspots
- `search_code(pattern, project)` — Grep-like text search within indexed files
- `manage_adr(action)` — CRUD for Architecture Decision Records
- `ingest_traces(traces)` — Ingest runtime traces to validate HTTP edges

## ⛔⛔ UNREFERENCED IS NOT UNINTENTIONAL — **search `.dev/` before proposing any deletion** *(user ruling, `2026-08-15`)*

> ⭐⭐⭐ **User, verbatim:** *"what is not used does not mean it is existing without reason — a design doc
> gives answers."*

📌 **The case that produced this rule.** `CSharpEmitter` emits a standalone `BTreeTick@0` thunk
projecting at `paramIndex * sizeof(Params)` with no bound. Measured: **registered 20+ times, bound by
nothing.** ⛔ **The coordinator's lean was DELETE**, on the precedent of `BP-248`/`W3`'s counter stubs.
🔴 **Wrong.** The design record answers it directly:

| where | what it says |
|---|---|
| **`.dev/btree-ai-action-binding/SLICE1-DESIGN.md:82`** | ⭐⭐ **names the expression verbatim**: *"the BTree generator **ignores** the blueprint's standalone `BTreeTick` (with its `paramIndex*sizeof` math)"* — architect ruling *"BTree owns layout, blueprint provides `TickCore`"* |
| **`.dev/btree-ai-action-binding/SLICE2-DESIGN.md` §6.2** | *"(The blueprint's own `BTreeTick`/`Memory+8` path stays the **standalone** blueprint-as-behavior hosting.)"* |

⇒ ⭐⭐ **It is an opt-in capability** (`AiPrimitiveHosting.BTreeAction`/`BTreeCondition`), **not a
vestige.** ⛔ **Deleting it removes a capability, not a mistake.** ✅ **The right answer was ROUTE, not
delete** — project at a literal `0` in the same shape the bridge uses, which makes the `@0` key **true
by construction** instead of true by convention.

### ⭐ The rules that follow

1. ⛔⛔ **Before proposing to delete anything registered / emitted / exported but unreferenced, search
   `.dev/` for a design record.** 🔴 **There are ~2900 markdown files there and this programme had never
   searched them** — the whole design corpus sat outside every session's reading.
2. ⭐⭐ **"Unreachable" and "dangerous" are TWO properties — do not collapse them.** `W3`'s stubs were
   unreachable **and harmful** *(last-writer-wins overwrite)* ⇒ delete. This one was **dormant**
   *(a unique key that overwrites nothing)* ⇒ route. ⚠ **The precedent applied to the wrong half.**
3. ⭐ **A grep over assets/call sites cannot see intent.** It answers *"is it used?"*, never *"is it
   meant to exist?"* ⇒ **the second question has a different source, and that source is `.dev/`.**
4. ⭐ **When in doubt, prefer ROUTING to DELETING** — routing preserves the capability and still
   collapses the duplicate mechanism (ruling 9). Deletion is only right when the design record says the
   thing is dead, or nothing claims it.

### ⭐⭐ `2026-08-17` extension — **"no rush removals"** *(user ruling)*

⭐ **The rule above covers the UNREFERENCED.** ⛔ **This covers the SUPERSEDED**: a thing whose job a
newer surface now does is **still not a rush removal.**

📌 **The case:** the coordinator carried *"retire `InspectorWindow`'s STATIC PARAMETERS"* for five
batches **on a label it had never measured.** 📐 Measured: it is the **default-value editor for the
`ExpressionTargetField` variable**, its duplicate-CODE half was already resolved (`BP-267`), and what
looked like duplication is a **node-scoped affordance the asset-scoped table does not have.**
⚠ **And the binding it authors is one whose runtime `E7b` is only now building** ⇒ removing it would
have raced its own fix.

⇒ ⭐ **Before proposing a removal, state which of the three it is:** **duplicate CODE** *(route it)* ·
**duplicate SURFACE** *(usually keep — surfaces differ by context)* · **genuinely dead** *(and the
design record agrees)*. ⛔ **"Ruling 9 says one implementation" is about IMPLEMENTATIONS, not about
every place a user can reach one.**

### ⭐⭐ Where to look, in order *(derived by sweeping the corpus, `2026-08-15`)*

| # | look here | it tells you |
|---|---|---|
| ① | the programme's **`*-DESIGN.md` / `*_Detailed_Design.md`** | ⭐ **the INTENT** — what the thing is for |
| ② | its **`reports/*-REPORT.md`**, especially the *notes / debt* tails | ⭐⭐ **the DEBT** — `DEBT-*` ids are filed here and nowhere else |
| ③ | **`TASK-DETAIL.md`** | the **user decision** that authorised it, usually dated |
| ⛔ | `batches/*-INSTRUCTIONS.md`, `reviews/*` | **least useful — they restate the design** |

📌 **Three findings this programme derived the hard way were already written down:** the standalone
`BTreeTick` hosting path (`SLICE1-DESIGN.md:82`) · the netstandard2.0/net8.0 wall duplicating whole
algorithms (**`BATCH-03-REPORT.md:100`**, `2026-06` — ⚠ **described as **`DEBT-AIB-012` (suggested)** and NEVER FILED**; that id belongs to a different, RESOLVED row. **Cite the report line**) · the `MarshalFromBytes` struct arm being *designed in
and never built* (`_DONE/blueprints-1/TASK-DETAIL.md:1840`).

## ⛔ THE SILENT-DEFAULT PATTERN — **a production caller that HAS a dependency must PASS it** *(`2026-08-16`)*

📌 **Found three times in three consecutive batches**, each time as a capability that looked built and
did nothing:

| instance | how it presented |
|---|---|
| `HsmValidator._isStatefulSubtree` / `_sharedScopeKeys` | `_ => false` / `_ => empty` ⇒ **rules 8/8b inert** |
| `BlackboardAuthoringWindow._actionSchemaExporter` | `null` ⇒ the DTO reflection **contributes nothing** |

⛔ **The fix is NOT "ban optional dependencies"** — they exist so tests and lightweight hosts need not
supply everything, and every one of these was **deliberately** optional.
⛔ **Nor a generic detector** — a sweep over every optional parameter flags dozens of correctly-defaulted
ones and gets switched off within a batch. *(One was tried and thrown away, `2026-08-16`.)*

⭐⭐⭐ **What distinguishes the three from the harmless majority is NOT the default — it is that the
CALLER HELD THE VALUE AND DID NOT PASS IT.** `PerspectiveWorkspaceRegistrar` handed the exporter to the
validator **two lines above** the window it did not hand it to.

⇒ ⭐⭐ **The checkable rule: a production caller that HAS a dependency must pass it.**
⇒ ⭐ **The control: a forwarding rail PER DEPENDENCY, asserted on the CONSTRUCTED OBJECT** — not on the
registrar's source. ⚠ **A silent default is only a defect when the caller could have done better.**

## ⛔⛔ WHO DESIGNS — **the coordinator, never the implementation session** *(user ruling, `2026-08-15`)*

> ⭐⭐⭐ **User, verbatim:** *"you are doing the designs, not them. if you need info, do your own subagent
> scan."*

📌 **The case that produced this rule:** the coordinator put a `.dev/` design-record sweep into a batch
as *"item one"*. ⛔ **That is research feeding a design decision — coordinator work.** ⭐ **The
implementation session builds; it does not source the design it builds from.**

| ⭐ **coordinator** | ⛔ **implementation session** |
|---|---|
| sweeps `.dev/`, reads the design corpus, **runs its own subagent scans** | writes code, tests, gates |
| decides what the design IS, and revises the plan | reports what the code MEASURES |
| takes contradictions to the user | ⭐ **STOPs and reports** when a premise fails |

⭐ **Subagents are the coordinator's instrument for this** — parallel read-only `Explore` agents over
`.dev/`, one per topic, each asked for *record → confirms/refines/contradicts → what it did not cover*.
⛔ **Do not spend an implementation batch on a question a subagent can answer in one pass.**

## Assistant interaction preferences

- **Ask questions in plain chat text, never with the question/multiple-choice widget** (do not use the `AskUserQuestion` tool). List options as normal prose the user can reply to.
- **⭐ Always give GitHub links to documents** *(user, `2026-08-17`: "pls always show github link to the documents, i am on mobile")*. Whenever a document is written, updated or referenced in chat, include its link on the **current working branch**:
  `https://github.com/pjanec/HROT/blob/<branch>/<path>` — e.g.
  `https://github.com/pjanec/HROT/blob/claude/blueprint-authoring-status-gm0akp/docs/blueprints/PLAN_Remaining_Work.md`.
  ⚠ **Push first** — a link to an unpushed commit 404s. ⭐ **SVGs too**; GitHub renders them from the blob page.
- **Model delegation (token thrift):** keep Opus for orchestration and hard reviews; delegate heavier work that does not need Opus-level intelligence (mirror-an-existing-pattern slices, mechanical edits, broad searches) to a **Sonnet** subagent. Opus reviews the real diff and re-runs the gates. Do novel scheduler/IR/compiler work hands-on.
- **Build general, not just minimal (round-out):** when a task needs a generic node, implement the whole obvious set rather than only the one value the immediate task needs — e.g. the `Compare` node ships every `ComparisonOperator`, not just `==`; an operator/enum-keyed node covers the full enum. Proactively add closely-similar, generally-useful companions (the arithmetic/boolean peers of a comparison node) when they reuse the same machinery and are plausibly usable. Default toward completeness over minimalism. Balance against the architect's demand-driven caution: if a round-out means a whole new *speculative* vocabulary or contradicts an explicit architect ruling, flag it for a quick nod first rather than silently building it — but don't be stingy with cheap, obvious generality.
- **Architect-questioning discipline (engine-rules gate):** no non-trivial capability / node / slice starts without a design, and no non-trivial design ships without an **architect pass**. For each non-trivial task, draft an `docs/blueprints/Architect_Question_N_*.md` mirroring the existing Q#2–Q#5 docs (decision-shaped sub-questions A/B/C/D + Claude's recommended lean + the reuse-vs-build tradeoff for each), record the answers in that doc, **then** build. Trivial mirror-pattern nodes (a documented recipe already exists) may proceed on a short in-repo design note without a full architect round.
  - ⛔⛔ **`2026-08-16` — the NotebookLM architect is GENERALLY UNAVAILABLE.** ⭐ **User, verbatim:** *"notebooklm architect is generally unavailable now, but lets keep writing architect questions as till now, this helps isolate truly architectural issues with large blast radius."*
  - ⇒ ⭐⭐ **KEEP WRITING THEM — the document is the deliverable, not the relay.** Its value is **triage**: forcing a question into decision-shaped options with leans and blast radius is what separates *"a design call"* from *"a thing to just build."*
  - ⇒ ⛔ **They are no longer relayed. They are resolved JOINTLY with the user** — *"we need to resolve that ourselves, together."* ⚠ **Do not mark one "relay to the architect"**; mark it as an agenda for a working session, and record the resolution in the same doc as before.
  - ⭐ **Historical architect answers stay authoritative** — prior sessions' answers repeatedly redirected the approach, and nothing retracts them. Treat this as load-bearing, not ceremony.
- **Diagrams: prefer hand-authored SVG for anything non-trivial.** Mermaid is acceptable only for simple flowcharts; for richer pictures (memory layouts, timelines, architecture overviews) author SVG — it renders more reliably (Mermaid sometimes clips labels / lays out awkwardly) and looks better. Keep Mermaid box labels short so text is not clipped.
- **Keep documentation prose short.** Lead with visuals and terse tables; no long prose walls — they go unread.

## Two-session protocol (coordinator ⇄ implementation) — **binding on both sessions**

Both sessions share this repo, so **both load this file**. A *coordinator* session owns the tracker,
writes handoffs and verifies returned diffs; an *implementation* session writes the code. Neither writes
in the other's lane.

### ⭐ The lanes — branch names, authoritative

| Lane | Branch |
|---|---|
| **Coordinator** (handoffs, tracker, gates) | ⭐ **`claude/blueprint-authoring-status-gm0akp`** |
| **Implementation** (all feature code) | ⭐ **`claude/hrot-implementation-j1jvin`** |

⚠ **Updated 2026-08-10 by the user.** The coordinator lane was previously
`claude/blueprint-authoring-status-6sr5ld`; that was a **different, now-retired session**. Any document
still naming `6sr5ld` as the coordinator branch is **stale** — this table wins.

⚠ **The implementation lane moved too** (Batch 29, from `claude/blueprint-macro-feature-sdmspn`).
⭐ **The coordinator must not assume the name** — locate their branch by which one's first commit
descends from a coordinator commit, not by the name in this table.

⭐ **The implementation session ALWAYS branches from, and updates from, the coordinator branch.** Never
from `main`, never from a previous implementation head that has drifted. Start every run with:

```bash
git fetch origin claude/blueprint-authoring-status-gm0akp
git merge --ff-only origin/claude/blueprint-authoring-status-gm0akp   # or branch fresh from it
```

⭐ **The mechanic that causes every failure so far:** the implementation session does **not merge** the
coordinator branch — it **builds linearly on top of whatever exists when it starts**. Anything the
coordinator pushes after that moment is invisible for that whole run. Two ID collisions and one wasted
document came from ignoring this.

| # | Rule | Owner |
|---|---|---|
| 1 | ⭐ **Never amend a handoff after dispatch.** New findings go in the *next* handoff, never back into the live one. This is the root cause of both collisions | coordinator |
| 2 | **Stamp `Dispatched at <sha>` in every handoff header**, so an edit after that point is visibly illegal | coordinator |
| 3 | ⭐ **The coordinator allocates NO ids.** `BP-200+` failed too — both sessions reached into the same block (three collisions now). Describe findings; **the implementation session numbers them** when it creates the rows. Any number in a handoff is a placeholder the implementation session may change | coordinator |
| 4 | ⭐ **Before your final commit, pull the coordinator branch again** and read any handoff/design file that changed. This is the cheap half of the fix — it catches late additions rule 1 cannot prevent | implementation |
| 5 | **State the IDs you allocated** in your report, so a collision is caught at merge, not three batches later | implementation |
| 6 | ⭐ **The tracker + detail docs belong to the implementation session for the batch's duration.** The coordinator records findings in conversation and in the next handoff, not as rows | both |
| 7 | ⭐ **Branch from the coordinator branch, and re-sync from it at the START of every run** (lane table above). This is the *other* half of rule 4: rule 4 catches what landed **during** your run, rule 7 catches what landed **before** it. Together they close the mechanic described above | implementation |

### ⭐⭐⭐ Rule 8 — **the coordinator does NOT re-run the gates** *(user, `2026-08-17` — SUPERSEDES the `2026-08-16` version)*

> ⭐⭐⭐ **User, verbatim:** *"you seem to run the same gates as the implementation session has already
> done before reporting to you, this is an enormous waste of time; pls rather ask the implementation
> session to report same detail you want to see from running your gates."*

⚠ **The `2026-08-16` rule already said PROPORTIONATE and the coordinator drifted back to re-running
nearly the whole set every batch** — ~15 minutes of duplicated work per batch, buying **trust that a
structured report already buys.** ⛔ **After `--ff-only` their tree IS my tree; re-execution adds
nothing.**

⇒ ⭐⭐ **THE REPORT SUBSTITUTES FOR THE RUN.** ⛔ **But only if it carries what a run would have told
me** — that is the contract below, and **a missing row is the one thing that sends me to the terminal.**

#### ⭐⭐ THE GATE REPORT CONTRACT — *(put this in every handoff §Gates)*

| # | the implementation session MUST report | ⭐ because a re-run is how the coordinator used to get it |
|---|---|---|
| **1** | **one row per gate: verbatim command · pass/fail/skip counts · the delta vs baseline** | the basic table — unchanged |
| **2** | ⭐⭐ **a `--no-build` COLUMN**, and which gates must build | ⛔ **out-of-solution projects report a STALE BIN** — `NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests`. ⚠ **This is how `Fhsm.Tests` produced a false regression and how it stayed ungated** |
| **3** | ⭐⭐⭐ **golden movement as a DIFF SHAPE, not a yes/no** | *"17 files, purely additive, zero removed lines, two constants per Instance"* — ⭐ **that is what the coordinator was computing by hand**, and it is the difference between a deliberate regeneration and a silent one |
| **4** | ⭐⭐ **every RED confirmed PRE-EXISTING against the base commit**, named | ⚠ **the coordinator built worktrees twice to establish this.** ⭐ **Do it once, at the source, and state the base sha** |
| **5** | ⭐ **the working tree is CLEAN after every suite run** | ⛔ otherwise a golden was regenerated by a test and nobody noticed |
| **6** | ⭐ **both quarantine counts**, and ⛔ **a new skip is a finding, not a fix** | |
| **7** | ⭐ **`tracker-counts.py --check`** and **every id allocated** | |

#### ⭐ What the coordinator still does — ⛔ **narrow, and NOT a gate re-run**

| | |
|---|---|
| ⭐⭐ **spot-verify a SURPRISING claim** | ⚠ **targeted, one command** — e.g. the `activeLeafIds[0]` hard-code, the four `FDP/Examples` call sites. ⭐ **These paid every time; a full suite re-run never did** |
| ⭐⭐ **verify a claim that contradicts a premise of MINE** | ⛔ **that is a design question, not a gate** |
| ⚠ **re-run ONE gate whose row is missing or malformed** | ⛔ **not the set** |
| ⭐ **read the DIFF** | ⛔ **this was always the real review, and it is what the freed time goes to** |

⛔⛔ **`Fdp.Toolkits.Tests` needs no coordinator run at all** — `DEBT-AIB-030`: **seven distinct tests,
the identity ROTATES between runs.** ⭐ **Neither a red nor a green is evidence**; the implementation
session confirms by `--filter`/namespace and says so.

### ⭐ Rule 1a — **an UNSTARTED handoff may be re-dispatched** *(user, `2026-08-16`)*

⭐ **Rule 1 exists because an amendment is invisible to a run ALREADY IN PROGRESS.** ⇒ **if the
implementation branch does not yet contain the dispatch sha, that mechanism cannot bite.**

⛔ **Then: amend and RE-STAMP** *(never silently edit — rule 2)*, and **check ancestry first**:

```bash
git merge-base --is-ancestor <dispatch-sha> origin/<impl-branch>   # NO ⇒ safe to re-dispatch
```

⚠ **If they had already started, the earlier stamp binds** — say so and re-issue as a new batch.

### ⛔⛔ Rule 1b — **rule 1a's ancestry check has a BLIND WINDOW** *(found by the implementation session, `2026-08-17`)*

📌 **What happened.** The coordinator amended a dispatched handoff **twice** under rule 1a, each time
checking `git merge-base --is-ancestor <dispatch-sha> origin/<impl-branch>` and concluding *"no run was
ever in progress."* ⛔ **Correct about the remote, wrong about reality:** the implementation session had
ff-merged the dispatch and **built three items locally**, with **nothing pushed yet**, so the remote
still pointed at the previous batch.

⇒ ⭐⭐ **The guard is blind from the moment the implementation session merges the dispatch to the moment
it first pushes.** ⚠ **No damage that time** *(the same commit's design doc un-pulled the item, and they
had reached the other amendment independently)* — ⛔ **that was luck, not the control working.**

| ⭐ **the fix — BOTH, they are cheap** | owner |
|---|---|
| ⭐⭐ **push an empty `chore: started batch N at <sha>` commit IMMEDIATELY after the rule-7 merge**, before writing any code | **implementation** |
| ⭐ **ASK before re-dispatching** rather than inferring from the remote — ⛔ **the ancestry check is now CORROBORATION, not proof** | **coordinator** |

⚠ **If the started-marker is absent and the user is not there to ask, assume a run IS in progress** —
⭐ **the cost of a needless new batch is one batch; the cost of amending under a live run is a
collision.**

### ⭐ Rule 3a — **architect-question numbers are ids too** *(added `2026-08-14`)*

> **Any session creating `Architect_Question_N_*.md` must first `git fetch` every active branch and
> take the next free `N` ACROSS ALL OF THEM — not the next free `N` on its own branch.**

⚠ **Why this needed saying:** rule 3 names *coordinator* and *implementation*, ⛔ **but the collision
that happened was between two DESIGN sessions** — the blueprint coordinator and the cross-host
variable-model session both created an `Architect_Question_28` on `2026-08-14`, independently.
📌 **Resolved by the coordinator renumbering to `#31`** (theirs was three consecutive with cross-links
from five documents — the cheaper side to keep, not a principled claim).
⭐ **Wording agreed by both sessions.** ⇒ **the same rule applies to any other cross-session numbered
artefact, not just `BP-` rows.**

### ⛔⛔ IMPLEMENTATION FREEZE — **one session builds the unified variable model** *(user, `2026-08-15`)*

> ⭐⭐ **User ruling, verbatim:** *"cross host it is. one single implem session (the one we are using)
> will be implementing for all hosts, no other session will implement until this is all done."*

| | |
|---|---|
| ⭐ **Who builds** | ⭐ **`claude/hrot-implementation-j1jvin`** — **all hosts**: Blueprint, BTree **and** HSM, including everything in **`Hrot.Editor.AiShared`** |
| ⛔ **Every other session** | ⛔⛔ **DOES NOT IMPLEMENT until this is done.** ⭐ **Design, questions, review and documents are fine — code is not.** ⚠ **This explicitly includes the cross-host variable-model session and the HSM visual-editing session** |
| **What "this" is** | the unified variable Details panel + the emitter/access unification — 📄 **[`Architect_Question_32_…_ANSWERS.md`](../docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md)** ⭐ **EXTENDED `2026-08-15` (user): the CROSS-HOST PARAMETER MODEL (`W1`–`W13`) TOO** — 📄 **[`PLAN_Cross_Host_Sequencing.md`](../docs/blueprints/PLAN_Cross_Host_Sequencing.md)**. ⛔ **Phase A is NOT the design session's to build**; the queue is **56 → 58 → 57 → `W4` → …** |
| ⭐ **Why** | the ruling over the whole design is *"no keeping two implementations for the same concept."* ⛔ **Two sessions building one shared panel produces exactly two implementations** — the constraint would be broken by the process before any code disagreed |

⚠ **If you are a session other than the one named above and you are about to write code touching
variables, working state, the blackboard panel or `Hrot.Editor.AiShared` — STOP and ask the user.**

### Checking "did they see X?" — do it correctly, and name the run

Never say *"they never saw it"* — that reads as a property of the session when it is a property of one
commit. Test against **what they branched from**, not their head:

```bash
git log -1 --format='%p' <their-first-commit-of-that-run>   # the commit they built on
git merge-base --is-ancestor <my-commit> <that-parent>
```

Report *"not in the commit they built from (run starting `<sha>`)"*. The same document is routinely
absent for one run and present for the next — both statements true, about different runs.
