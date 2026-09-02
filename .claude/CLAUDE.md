## Codebase Memory MCP

**MANDATORY: use Codebase Memory MCP graph tools FIRST — before reading files, before making code
changes, and before asserting anything about what exists in this codebase.**

This rule applies to every request involving this codebase.

### 🔴 Why this keeps failing — read before deciding it does not apply

The rule used to say only *"before reading files or making changes"*, so during **design and prior-art
research** — where nothing is being edited — it read as inapplicable, and grep felt sufficient. **That is
the failure mode.** It has recurred across sessions.

**The trigger is not editing. It is the shape of the claim you are about to make.**

| Claim shape | Tool | Why grep cannot do it |
|---|---|---|
| *"There are N of these"* · *"these are all the implementations / levels / hosts"* | 🔴 **`search_graph`** | grep answers *"does X exist"*. It **cannot** answer *"what is the complete set of X"* — you only ever see what your pattern happened to match |
| *"Nothing implements / reads / writes this"* — the seam-law claim | 🔴 **`search_graph`**, then open the sites | an absence claim from grep is an absence *in your pattern*, not in the repo |
| *"What is the overall shape here?"* | **`get_architecture`** (try `aspects:["clusters"]` — it surfaces de-facto modules that cut across the folder layout) | — |
| *"Who calls this?"* | ⚠ **`trace_path` AND grep** — see the caveat below | — |
| *"What does this function do?"* | **`get_code_snippet`**, then `Read` for exact lines | — |

⚠ **Measured, 2026-08-13, on the TKB stack** — both directions matter:

- **Graph found what grep missed.** Three rounds of grep produced a **four**-interface inventory;
  a single `search_graph(name_pattern=".*Tkb.*", label="Interface")` returned **five**. The missed one
  (`ITkbHotReloadEvents`) was load-bearing — a cache-invalidation contract with a live subscriber and no
  publisher.
- **Grep found what the graph missed.** `trace_path` on `ITkbDatabase.TryGetByType` reported **3 callers**
  (all tests/examples) where grep found **9+ production sites**. **C# interface dispatch defeats the call
  resolver.**

🔒 **So: graph first for inventory and structure, grep to confirm call sites, and never rely on either
alone for an exhaustive claim.** Use `check_index_coverage` before any negative or exhaustive claim —
coverage is best-effort and is never proof of completeness.

⚠ **If the MCP server is disconnected mid-session** (it does drop), say so explicitly in any inventory
claim made while it was down, and re-verify those claims once it reconnects. Do not silently downgrade to
grep and present the result with the same confidence.

> **Cloud / Claude Code on the web:** if the `mcp__codebase-memory-mcp__*` tools are
> not connected or `dotnet` is missing, this is a fresh cloud VM — run the
> `/cloud-bootstrap` skill (or `bash scripts/cloud-bootstrap.sh`) to install the
> .NET 8 SDK and the codebase-memory-mcp server. Note: if the binary is installed
> mid-session, the graph **MCP tools** connect on the **next** session (MCP servers spawn
> at session start). For session-#1 tools, run the bootstrap from the environment's
> Setup script. Details: `docs/cloud-codebase-memory-mcp.md`. (Local Windows/VS Code
> sessions already have the tools — skip this.)
>
> 🔴 **BUT the MCP tools not being connected is NOT a reason to downgrade to grep-only**
> *(session complaint, `2026-08-25`)*. **The same binary has a CLI mode** — run any graph
> tool through Bash: **`codebase-memory-mcp cli <tool> '<json-args>'`** *(binary at
> `/opt/codebase-memory-mcp/codebase-memory-mcp`, or `~/.local/bin/codebase-memory-mcp`,
> or `$CODEBASE_MEMORY_MCP_BIN`; `cli <tool> --help` shows the flag form)*. ⭐ Every
> `mcp__codebase-memory-mcp__*` tool is available this way — `list_projects`,
> `search_graph`, `query_graph`, `trace_path`, `get_code_snippet`, `index_repository`, … —
> so the **INVENTORY-before-design** rule still holds in a session where the MCP server
> never connected. ⚠ If `list_projects` returns empty, `codebase-memory-mcp cli
> index_repository '{"repo_path":"<repo root>"}'` first *(tens of seconds)*. Example:
> `codebase-memory-mcp cli search_graph '{"project":"<name>","name_pattern":".*Foo.*","label":"Class"}'`.
> ⇒ ⛔ **A report that says "codebase-memory MCP was not connected, so I used grep" is now
> a MISS** — the CLI was available; use it and say so.

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
3. Use `search_graph` to find relevant symbols, `trace_path` for call chains.
4. Use `get_code_snippet` to read specific function implementations.
5. Only use `Read` when you need exact raw content to edit a specific line.

**Opening move for any design / prior-art task** — cheap, and it is the step that keeps getting skipped:

```
search_graph(project, name_pattern=".*<Topic>.*", label="Interface")   # the complete seam inventory
search_graph(project, name_pattern=".*<Topic>.*", label="Class")       # the implementations
get_architecture(project, aspects:["clusters"])                        # the de-facto modules
```

⚠ **`trace_path` names are qualified.** A bare name returns `status:"ambiguous"` with suggestions —
pick the production `qualified_name` from them (the list will be mostly test doubles; that is expected,
not a signal).

### Available Tools (15 MCP tools)

**Indexing:**
- `index_repository(repo_path)` — Index a repository into the knowledge graph
- `list_projects` — List all indexed projects with node/edge counts
- `delete_project(project)` — Remove a project and all its graph data
- `index_status(project)` — Check indexing status
- `check_index_coverage` — 🔴 **run before any negative or exhaustive claim**; coverage is best-effort, never proof of completeness

**Querying:**
- `search_graph(query | name_pattern | semantic_query, label, file_pattern, …)` — 🔴 **the inventory tool.** Three independent modes: `query=` BM25 full-text, `name_pattern=` regex, `semantic_query=[...]` vector. Paginate while `has_more` is true — a truncated page is not the answer
- `trace_path(function_name, direction, depth, mode)` — call chains; `mode` also does `data_flow` and `cross_service`. ⚠ under-reports C# interface dispatch
- `detect_changes(project)` — Map git diff to affected symbols + risk
- `query_graph(query)` — Execute Cypher-like graph queries (read-only)
- `get_graph_schema(project)` — Node/edge counts, relationship patterns
- `get_code_snippet(qualified_name)` — Read source code for a function
- `get_architecture(project, aspects)` — Overview; `clusters` finds the real seams, `cycles` is opt-in
- `search_code(pattern, project)` — Grep-like text search within indexed files
- `manage_adr(action)` — CRUD for Architecture Decision Records
- `ingest_traces(traces)` — Ingest runtime traces to validate HTTP edges

## ⛔⛔ UNREFERENCED IS NOT UNINTENTIONAL — **search `docs/` FIRST, then `.dev/`, before proposing any deletion** *(user ruling, `2026-08-15`; corpus order corrected `2026-08-17` and again `2026-08-21`)*

> ⭐⭐⭐ **User, verbatim:** *"what is not used does not mean it is existing without reason — a design doc
> gives answers."*

📌 **The case that produced this rule.** `CSharpEmitter` emits a standalone `BTreeTick@0` thunk
projecting at `paramIndex * sizeof(Params)` with no bound. Measured: **registered 20+ times, bound by
nothing.** ⛔ **The coordinator's lean was DELETE**, on the precedent of `BP-248`/`W3`'s counter stubs.
🔴 **Wrong.** The design record answers it directly:

| where | what it says |
|---|---|
| **`.dev/_DONE/btree-ai-action-binding/SLICE1-DESIGN.md:82`** | ⭐⭐ **names the expression verbatim**: *"the BTree generator **ignores** the blueprint's standalone `BTreeTick` (with its `paramIndex*sizeof` math)"* — architect ruling *"BTree owns layout, blueprint provides `TickCore`"* |
| **`.dev/_DONE/btree-ai-action-binding/SLICE2-DESIGN.md` §6.2** | *"(The blueprint's own `BTreeTick`/`Memory+8` path stays the **standalone** blueprint-as-behavior hosting.)"* |

⇒ ⭐⭐ **It is an opt-in capability** (`AiPrimitiveHosting.BTreeAction`/`BTreeCondition`), **not a
vestige.** ⛔ **Deleting it removes a capability, not a mistake.** ✅ **The right answer was ROUTE, not
delete** — project at a literal `0` in the same shape the bridge uses, which makes the `@0` key **true
by construction** instead of true by convention.

### ⭐ The rules that follow

1. ⛔⛔ **Before proposing to delete anything registered / emitted / exported but unreferenced, search
   the design corpus for a record — ⭐⭐⭐ `docs/` FIRST, `.dev/` SECOND.**
   🔒 **User, `2026-08-21`:** *"not just `.dev`, there is docs folder as the main source."*
   ⭐⭐ **`docs/` carries the CURRENT intent**; ⭐⭐ **`.dev/<programme>/*-DESIGN.md` carries IMPLEMENTED
   intent — the WHY behind a built feature, often more than the code says** *(coordinator ruling
   `8f950a83`, `2026-08-21`)*. ⚠⚠ **An earlier version of this line called `.dev/` "as-built" and told
   you never to read it for intent — that was too dismissive and is SUPERSEDED.** ⭐ **Read both**;
   ⚠ **check for a newer `docs/` supersession before quoting a `.dev/` one.** 🔴 **There are ~2900
   markdown files in `.dev/` and this programme had never searched them.**
2. ⭐⭐ **"Unreachable" and "dangerous" are TWO properties — do not collapse them.** `W3`'s stubs were
   unreachable **and harmful** *(last-writer-wins overwrite)* ⇒ delete. This one was **dormant**
   *(a unique key that overwrites nothing)* ⇒ route. ⚠ **The precedent applied to the wrong half.**
3. ⭐ **A grep over assets/call sites cannot see intent.** It answers *"is it used?"*, never *"is it
   meant to exist?"* ⇒ ⭐⭐ **the second question has a different source, and that source is `docs/`**
   *(with `.dev/` for "why is it like this")*.
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

### ⭐⭐ Where to look, in order — ⛔⛔ **`docs/` IS THE MAIN SOURCE, NOT `.dev/`**

> ⭐⭐⭐ **USER CORRECTION, `2026-08-17`, verbatim:** *"most designs are in the **docs** folder. in the
> `.dev` those named like 'design' or 'detailed design' describe **what was implemented**."*
> ⚠⚠ **An earlier version of THIS TABLE listed `.dev/*-DESIGN.md` as ① "the INTENT" — that was WRONG**,
> and it contradicted [`RULINGS.md` §4](../docs/blueprints/RULINGS.md) for four days.
> 🔒 **Restated by the user `2026-08-21`:** *"not just `.dev`, there is docs folder as the main source."*

| # | look here | it tells you |
|---|---|---|
| ① | ⭐⭐⭐ **`docs/**/Architect_Question_*_ANSWERS.md`** | ⭐ **THE RULINGS.** ⛔ the non-`ANSWERS` files carry only options |
| ② | ⭐⭐ **their §"Sequencing" tables** | ⛔ **a finding with a planned batch is NOT a new finding** |
| ③ | ⭐⭐ **`docs/` — `DESIGN_*.md` · `PLAN_*.md` · `*_Unification.md` · `BOOTSTRAP_*.md`** | ⭐⭐ **THE INTENT — the model as it is MEANT to be** |
| ④ | ⭐⭐ **`.dev/<programme>/*-DESIGN.md` · `*_Detailed_Design.md`** | ⭐⭐ **IMPLEMENTED INTENT — the WHY behind a built feature, often richer than the code** *(`8f950a83`)*. ⚠ **Check for a newer `docs/` supersession before quoting it** — ⛔ but do NOT dismiss it as mere as-built |
| ⑤ | **`.dev/**/reports/*-REPORT.md`** tails · **`TASK-DETAIL.md`** | ⭐ **the DEBT** *(`DEBT-*` ids are filed here and nowhere else)* · the authorising user decision, usually dated |
| ⛔ | `batches/*-INSTRUCTIONS.md`, `reviews/*` | **least useful — they restate the design** |

⚠ **The one caution that survives:** ⛔ **a document describing what was built can AGREE WITH THE CODE by
construction** — ⭐ so when a `.dev/` design and a newer `docs/` design disagree, **`docs/` wins**; ⛔ but
absence of a `docs/` record does NOT make the `.dev/` one worthless. 📌 **Measured `2026-08-21`:** searching `EditorTimeTransportFacade` returns hits in
**both** trees — `docs/blueprints/{DESIGN_Time_Architecture,PLAN_Time_System_Refactor,Q48}.md` *(the
live intent)* **and** `.dev/_DONE/main-toolbar-1/` *(the batch that built it)*. ⇒ ⭐ **searching one tree
answers half the question.**

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

## ⛔⛔ WHO DESIGNS — **AMENDED `2026-08-26`: coordinator FRAMES, sessions design the DETAILS at scale**

### ⭐⭐⭐ AMENDMENT `2026-08-26` — **FRAME-DELEGATION** *(user ruling — SUPERSEDES the blanket form below)*
> ⭐⭐⭐ **User, verbatim:** *"you are coordinating 3 sessions already. that is too much to design everything
> for everyone, you need to delegate. Let the session do it. give them just a frame and let the details on
> them, they can focus… you are coordinator, but you do not be a designer for all 3 sessions at once."*

⛔⛔ **At multi-lane scale the coordinator does NOT author the full design *(inventory + UML + exact seams)*
for every lane** — that made the coordinator the bottleneck. ⭐⭐ **Instead the coordinator writes a FRAME and
the implementation session designs the details WITHIN it, as step 1 of its batch.**

| ⭐ the coordinator OWNS *(the FRAME)* | ⭐ the session OWNS *(the DETAIL, within the frame)* |
|---|---|
| the **goal** + why · the **lane fences / cross-lane touch-points** · the **key decisions with a lean** · the **design basis to read** *(docs + prior art)* · the **acceptance shape** | the **inventory** *(its own `search_graph`)* · the **class + sequence UML** *(authored in the design doc)* · the exact seams · the item breakdown |
| ⭐⭐ **large-blast-radius decisions still get an architect question resolved WITH the user** *(e.g. a serialization/engine contract)* — ⛔ those are NOT delegated | ⭐ ordinary design calls inside the frame — decide-and-log |

⭐⭐ **Interaction with NO-IMPLEMENTATION-WITHOUT-UML:** the UML still exists before the build — but the
**SESSION authors it** *(design doc, step 1)*, then builds against it, then folds the as-built *(obligations ③+⑤)*.
⭐ **The coordinator VERIFIES the design+UML exists and matches on RETURN** *(obligation ④ becomes verify-on-merge)*
— ⛔ a returned batch with no design/UML is incomplete, send it back.
⭐ **A frame-handoff says: "investigate → write the design (with UML) in `docs/` → build → fold as-built."**

⚠ **The blanket `2026-08-15` rule below still holds for a SINGLE lane / when the coordinator has bandwidth**,
and the coordinator still does its OWN corpus sweep to SET the frame's intent *(R-129)*. ⛔ What changed: the
coordinator no longer draws every lane's UML — it frames and verifies.

### ⛔ HISTORY — the original blanket rule *(`2026-08-15`; SUPERSEDED at scale by the amendment above)*

> ⭐⭐⭐ **User, verbatim:** *"you are doing the designs, not them. if you need info, do your own subagent
> scan."*

📌 **The case that produced this rule:** the coordinator put a `.dev/` design-record sweep into a batch
as *"item one"*. ⛔ **That is research feeding a design decision — coordinator work.** ⭐ **The
implementation session builds; it does not source the design it builds from.**

| ⭐ **coordinator** | ⛔ **implementation session** |
|---|---|
| sweeps `.dev/`, reads the design corpus, **runs its own subagent scans** | writes code, tests, gates |
| decides what the design IS, and revises the plan | reports what the code MEASURES |
| takes contradictions to the user | ⭐ **STOPs and reports** when a premise fails — ⛔⛔ **but STOPS THAT ITEM, NEVER THE BATCH** *(`R-106`, user `2026-08-19`)*: ⭐ **do every item that is not blocked**, and ⛔ **only a genuine DEPENDENCY may cascade, named in the report** |

⭐ **Subagents are the coordinator's instrument for this** — parallel read-only `Explore` agents over
`.dev/`, one per topic, each asked for *record → confirms/refines/contradicts → what it did not cover*.
⛔ **Do not spend an implementation batch on a question a subagent can answer in one pass.**

## ⛔⛔⛔ NO LEAN WITHOUT A CLAIM TABLE — **and STAYING QUIET LONGER IS THE POINT** *(user, `2026-09-01`)*

> ⭐⭐⭐ **User, verbatim, on the third correction in one session:** *"we are constatly hitting the point
> from the beginning - your understanding is shallw before giving lean. hiw to improve?"* — then, on the
> proposal below: 🔒 *"staying quiet longer to give much better answer is exactly what i expect."*

⛔⛔ **THE DIAGNOSIS IS NOT "DIDN'T READ ENOUGH."** 📐 Plenty was read each time. ⭐⭐⭐ **The mechanism is:
the RECOMMENDATION WAS FORMED FIRST, and the reading went looking for support.** ⇒ every lean rested on
**one verified property plus several ASSUMED ones — and the assumed ones were always the ones that decided
the question.**

📌 **Measured on `CE-147` *(where the `NetworkTransform` position-shadow attach belongs)* — three leans,
three corrections, and EVERY ONE broke on a file that was NAMEABLE IN ADVANCE as load-bearing and never opened:**

| the lean | what broke it | had I opened it? |
|---|---|---|
| *"the consumer owns its cache"* ⇒ egress | `NetworkTransform`'s **own header** says **both** sides write it | ⛔ no — the egress *usage*, never the component |
| ⇒ `SpatialCoreTkbTranslator` | the translator loop sits **ABOVE** the authority branch ⇒ **replicas run it too** *(`NetworkSpawningSystem.cs:134`)* | ⛔ no — read the branch, not the ten lines above it |
| my own objection *"structural mutation mid-iteration"* | `EntityQuery` is an **INDEX walk, not a chunk walk** *(`EntityQuery.cs:133`)* | ⛔ no — **invented from ECS folklore** |

### ⭐⭐⭐ The tell — **a PRINCIPLE where a LINE NUMBER should be**

⛔⛔ **Every broken lean was justified by a DESIGN PRINCIPLE, not a measurement** — *"the consumer should
own its cache"* · *"the template is the right home."* ⭐⭐ **Principle-arguments feel strong and stay
UNFALSIFIABLE until the file is opened.** ⇒ 🔒 **when the reason for a lean is a principle rather than a
`file:line`, the work is NOT DONE.**

### ⭐⭐ The cost asymmetry that makes this indefensible

📐 **Measuring = 2–3 tool calls, ~30 s. A wrong lean = a full user round-trip, their attention, and the
risk they ACCEPT it.** ⇒ ⛔ **the threshold for "should I measure this" is NEAR ZERO.** ⚠ Measurement had
been treated as expensive. **It is not.**

### ⭐⭐⭐ The rule — **a CLAIM TABLE in the chat reply, in front of every lean**

| the lean rests on | measured |
|---|---|
| translators run for replicas too | ✅ `NetworkSpawningSystem.cs:134` |
| the ownership path never attaches | ✅ `OwnershipIngressSystem.cs:75` |
| nothing reads `NetworkTransform` authority | ⛔ **assumed** |

| ⭐ | |
|---|---|
| ⭐⭐⭐ **NO ⛔-ASSUMED ROW MAY BE LOAD-BEARING** | ⇒ if it would flip the lean when false, **MEASURE IT BEFORE ANSWERING** |
| ⭐⭐ **an unmeasurable claim still gets its row, marked ⛔** | ⭐ that is the USEFUL case — the user pushes on **that row**, not on the conclusion |
| ⭐⭐⭐ **the generating question, asked BEFORE forming the lean** | 🔒 ***"what would have to be true for this to be WRONG, and which FILE would show me?"*** — 📌 that one question would have caught **all three** misses above |
| ⭐⭐ **why a TABLE and not "be careful"** | ⛔ the user **cannot check the reasoning from a phone** — ⭐ but they CAN check *"four rows measured"* vs *"one."* **It exposes the SHAPE OF THE CONFIDENCE, not just the verdict** — the same reason the `INVENTORY` block and the gate-report contract stuck where *"be diligent"* never has |

⭐⭐⭐ **CONSEQUENCE THE USER EXPLICITLY WANTS: BE SLOWER BEFORE THE FIRST ANSWER.** ⛔ The measured pattern
was *first lean wrong · third lean right*, **because the USER'S CORRECTIONS WERE DOING THE MEASUREMENT.**
⚠⚠ **That is the wrong division of labour — the user was acting as the test suite.** ⇒ ⭐ **spend the tool
calls first; a slower, measured first answer is the deliverable.**

## Assistant interaction preferences

- ⛔⛔⛔ **UNDERSTAND FIRST, THEN ANSWER — and NEVER ask the user to decide without a LEAN** *(user, `2026-09-01`, verbatim)*: ⭐⭐ **the enforcement mechanism is the CLAIM TABLE — see the section directly above.** *"I expect you to understand the code and concepts. I do not see the code, I am on mobile. So when you ask me to decide something, i need your lean. And i expect you first of all study available design documents (as written in claude.md) and then study relevant code using both codebase memory and grep until you fully understand it and only then you derive some solutions or explanations. You shoudl NOT work from incomplete understaning, like shallow code searcg with grep only."*
  ⭐ **The order is fixed: ① design docs *(`R-129` — `docs/` then `.dev/`)* → ② code, via `search_graph` **AND** grep *(never one alone)* → ③ only then a solution or an explanation.**
  ⛔⛔ **A menu of options with no recommendation is work handed back to the user** — they cannot open the code to break the tie. ⭐ **Every choice offered carries a named lean, its blast radius, and what would change the lean.** ⚠ **If understanding is genuinely incomplete, SAY WHICH PART and what would settle it** — ⛔ do not present a half-measured guess in the register of a conclusion.
- ⭐⭐ **GLOSS EVERY ID AND SHORTHAND ON FIRST MENTION IN A REPLY** *(user, `2026-09-01`: "I do not have the list in front of my eyes so i have no idea what these mean. When referring to these, pls give mo a short few words summary in parenthesis like 'CE-147 (here the single line summary what CE 147 is about)'")*.
  ⭐ Applies to **task/ruling ids** — `CE-147 (where the position-shadow attach belongs)` — **and to any local shorthand a document invented**: ⛔ *"host (b)"*, *"lane 3"*, *"case ②"* are meaningless in chat ⇒ **expand them** — *"host (b) — SimHost"*. ⚠ **This is IN ADDITION to the GitHub link below, not instead of it.**
- **Ask questions in plain chat text, never with the question/multiple-choice widget** (do not use the `AskUserQuestion` tool). List options as normal prose the user can reply to.
- **⭐ Always give GitHub links to DOCS *and* TASK IDS** *(user, `2026-08-17`: "i am on mobile"; extended `2026-08-26`: "write task ids like QA-006 and docs like architect questions or handoffs as github links so i can quickly take a look")*. Whenever chat mentions a **document** *(design, architect question, handoff, report, plan)* **OR a task id** *(`QA-006`, `CE-046`, `MX4b`, `BP-###`, `AX-###`, …)*, render it as a GitHub **blob link on the current working branch**:
  `https://github.com/pjanec/HROT/blob/<branch>/<path>` — a doc links to its file *(e.g. `…/docs/blueprints/PLAN_Remaining_Work.md`)*; ⭐ **a task id links to its owning doc** — the tracker row it lives in *(`…/docs/blueprints/Blueprint_Issues_Tracker.md`)*, or the design/report that defines it. ⛔ Do NOT print a bare id with no link — the user is on mobile and cannot grep for it.
  ⚠ **Push first** — a link to an unpushed commit 404s. ⭐ **SVGs too**; GitHub renders them from the blob page.
- **Model delegation (token thrift):** keep Opus for orchestration and hard reviews; delegate heavier work that does not need Opus-level intelligence (mirror-an-existing-pattern slices, mechanical edits, broad searches) to a **Sonnet** subagent. Opus reviews the real diff and re-runs the gates. Do novel scheduler/IR/compiler work hands-on.
- **Build general, not just minimal (round-out):** when a task needs a generic node, implement the whole obvious set rather than only the one value the immediate task needs — e.g. the `Compare` node ships every `ComparisonOperator`, not just `==`; an operator/enum-keyed node covers the full enum. Proactively add closely-similar, generally-useful companions (the arithmetic/boolean peers of a comparison node) when they reuse the same machinery and are plausibly usable. Default toward completeness over minimalism. Balance against the architect's demand-driven caution: if a round-out means a whole new *speculative* vocabulary or contradicts an explicit architect ruling, flag it for a quick nod first rather than silently building it — but don't be stingy with cheap, obvious generality.
- **Prior-art discipline (the seam law):** in this codebase a *"we need a shared X"* almost always means **X already exists and is under-adopted** — 24 measured instances so far. So every design opens with a prior-art pass, and that pass **starts with `search_graph`, not grep** (see the Codebase Memory section above — this is the rule that keeps getting skipped). Two failure modes to name explicitly: ⚠ **never read a reference *count* as adoption** — open the call sites; and ⚠ *"the seam is unused"* has two very different meanings — an interface nobody calls, versus one called every frame with a dead parameter. The fixes differ completely.
- **Architect-questioning discipline (engine-rules gate):** no non-trivial capability / node / slice starts without a design, and no non-trivial design ships without an **architect pass**. For each non-trivial task, draft an `docs/blueprints/Architect_Question_N_*.md` mirroring the existing Q#2–Q#5 docs (decision-shaped sub-questions A/B/C/D + Claude's recommended lean + the reuse-vs-build tradeoff for each), record the answers in that doc, **then** build. Trivial mirror-pattern nodes (a documented recipe already exists) may proceed on a short in-repo design note without a full architect round.
  - ⛔⛔ **`2026-08-16` — the NotebookLM architect is GENERALLY UNAVAILABLE.** ⭐ **User, verbatim:** *"notebooklm architect is generally unavailable now, but lets keep writing architect questions as till now, this helps isolate truly architectural issues with large blast radius."*
  - ⇒ ⭐⭐ **KEEP WRITING THEM — the document is the deliverable, not the relay.** Its value is **triage**: forcing a question into decision-shaped options with leans and blast radius is what separates *"a design call"* from *"a thing to just build."*
  - ⇒ ⛔ **They are no longer relayed. They are resolved JOINTLY with the user** — *"we need to resolve that ourselves, together."* ⚠ **Do not mark one "relay to the architect"**; mark it as an agenda for a working session, and record the resolution in the same doc as before.
  - ⭐ **Historical architect answers stay authoritative** — prior sessions' answers repeatedly redirected the approach, and nothing retracts them. Treat this as load-bearing, not ceremony.
- **Diagrams — ⭐⭐ MERMAID UML for architecture, SVG for explainers** *(user, `2026-08-20`, superseding the earlier SVG-first rule)*.
  - ⭐⭐⭐ **Architecture ⇒ standard UML in Mermaid**: `classDiagram` · `sequenceDiagram` · `stateDiagram-v2` · a `graph TD` package/dependency view. ⭐ **Clear and unambiguous beats pretty** — a class diagram states multiplicity, ownership and realisation in a way prose cannot fudge.
  - ⭐ **Hand-authored SVG stays for NON-UML explainers** — memory layouts, timelines, byte-packing pictures, anything with no standard notation.
  - ⛔ **Never both for the same thing.** Two pictures of one architecture rot apart; delete the loser.
  - ⚠ **VALIDATE every Mermaid block before pushing** — ⛔ a syntax error renders as an error box on GitHub, which is worse than no diagram. 📌 Real case (`2026-08-20`): `Default` is a **reserved word** in `stateDiagram-v2` and only the parser caught it.
    ```bash
    MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file.md>   # parses every block
    ```
  - ⭐ Keep box labels short so text is not clipped.
- **Keep documentation prose short.** Lead with visuals and terse tables; no long prose walls — they go unread.

## ⛔⛔⛔ NO IMPLEMENTATION WITHOUT UML — **the design must name the CLASSES and the SEQUENCES** *(user, `2026-08-20`)*

> ⭐⭐⭐ **User, verbatim:** *"design documents, once they are about to be implemented, MUST describe the
> classes and sequences using the UML diagrams. And this should be checked by any task implementing the
> design. The diagram forces to think about the implementation in exact terms; they should be created
> after thorough analysis of the existing code to avoid duplicating existing implementation. Any
> possibility for reuse should be utilized."*

⭐⭐ **Why a diagram and not more prose.** ⛔ Prose can stay vague about the three things that decide
whether a batch succeeds: **which types exist · which ALREADY exist · what calls what, in what order.**
⭐⭐⭐ **A class diagram cannot.** Drawing one forces a name, a home, a multiplicity and an owner for every
box — and **an existing class drawn on the same canvas as a proposed one makes the duplicate obvious.**

### ⭐ The rule — **five obligations**

| # | ⭐ obligation | owner |
|---|---|---|
| **①** | ⭐⭐⭐ **A design marked buildable carries a `classDiagram` AND a `sequenceDiagram`.** ⭐ Mark it in the STATUS block: `build-state: DESIGN │ READY-TO-BUILD │ BUILDING │ BUILT` | **coordinator** |
| **①b** | ⭐⭐⭐ **The diagrams live in the DESIGN, never in the batch** — ⭐ full rule in its own section below, *"THE DIAGRAMS LIVE IN THE DESIGN, NEVER IN THE BATCH"*. ⛔ Not restated here: 📌 two rule files stating one rule is how the `.dev`/`docs` order rotted | **both** |
| **②** | ⭐⭐⭐ **DRAW THEM AFTER THE ENUMERATION, NEVER BEFORE** — 📌 the `INVENTORY` rule feeds this one. ⭐⭐ **Every box that already exists is drawn as existing, with its file**, so a proposed class that duplicates it is visible on the same page. ⛔ **Any possibility for reuse must be UTILISED, not noted** | **coordinator** |
| **③** | ⭐⭐ **An implementing task CHECKS the diagrams before building**, and reports it: *"the design carries N classes and M sequences; what I built matches / deviates HERE and why."* ⚠ **A deviation is a finding, not a silent choice** — ⭐ argue it in the report, as every good batch already does | **implementation** |
| **④** | ⛔⛔ **A design with no UML is NOT ready to dispatch.** ⭐ A handoff citing one is a defect of the COORDINATOR — 📌 the same class of miss as `BP-355` *(named in a report, never turned into an item)* | **coordinator** |
| **⑤** | ⛔⛔⛔ **WHEN THE BUILD DEVIATES, FOLD THE AS-BUILT TRUTH BACK INTO THE OWNING DESIGN — before the batch closes**, marking the prior state SUPERSEDED. ⭐ Its own section below, *"THE DESIGN MUST REFLECT THE AS-BUILT"* | **implementation** |

### ⛔⛔⛔ THE DIAGRAMS LIVE IN THE DESIGN, NEVER IN THE BATCH — **a handoff REFERENCES them** *(user, `2026-08-21`)*

> ⭐⭐⭐ **User, verbatim:** *"the diagrams are to be written to designs to survive, not to ephemeral
> batches and tasks. batches should reference the designs, not being designs on their own."*

⛔⛔ **A handoff / report / review is EPHEMERAL — it now lives in `docs/blueprints/batches/` and is not
read again after the batch closes.** ⇒ ⭐⭐⭐ **A diagram drawn inside a handoff DIES with the batch, and
the design it belonged to never gets it.** 📌 The case: I put the `classDiagram`/`sequenceDiagram` for
`MIN`, the staged-edit and `T1/T2` **inside the handoffs** — and `T1/T2`'s was a straight duplicate of
`DESIGN_Time_Architecture.md` §9's own diagram.

| ⭐ the rule | |
|---|---|
| ⭐⭐⭐ **UML — and any design content — goes in a `DESIGN_*` / `Architect_Question_*` doc** | that is the long-lived home; it survives compaction and batch archival |
| ⭐⭐ **the handoff CITES the design's diagram by DOC + SECTION** | *"build §9's `ISimClock` classDiagram"* — ⛔ it does not redraw it |
| ⛔⛔ **a batch that CONTAINS a `classDiagram`/`sequenceDiagram` is a SMELL** | either it is inventing design in a place that dies, or duplicating the design ⇒ **move it into the design and reference it** |
| ⭐ **if a change has no owning design yet, CREATE/EXTEND one** *(with the UML)*, then dispatch a handoff that points at it | ⛔ **do not let the handoff BE the design** — 📌 the `MIN` mistake |

⇒ ⭐⭐ **Obligation ① restated with a home:** the `classDiagram`+`sequenceDiagram` live in the **DESIGN doc**
marked `build-state: READY-TO-BUILD`; the handoff is a dispatch pointer to it. ⚠ **`design-digest.py`
already checks designs for UML and EXCLUDES `batches/`** — so a diagram put in a batch is unchecked too.

### ⭐ Gated — **a convention nothing checks is a convention that decays**

```bash
python3 scripts/design-digest.py --check    # buildable design + no classDiagram/sequenceDiagram => FAIL
MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file>   # every block parses
```

⛔ **The gate cannot check that the diagram is TRUE** — ⭐ that is obligation ③'s job, and it is why the
implementing session reports the match rather than the coordinator asserting it.

### ⛔⛔⛔ THE DESIGN MUST REFLECT THE AS-BUILT — **a deviation goes BACK INTO THE DESIGN, not just the report** *(user, `2026-08-22`)*

> ⭐⭐⭐ **User, verbatim:** *"we need to update the design if the implementation went different direction.
> The implementor must make sure the design reflects the recent changes, marking the previous state
> there as superseded."*

⛔⛔ **Obligation ③ says a deviation is a FINDING argued in the report. That is necessary and NOT
sufficient** — 📌 **the report is EPHEMERAL** *(it lives in `docs/blueprints/batches/` and is not read
again after the batch closes — the same fact that put the DIAGRAMS in the design, not the batch)*.
⇒ ⭐⭐⭐ **a deviation recorded ONLY in the report leaves the design LYING**, and the next session reasons
off a design the code already left behind — 📌 exactly the *"code is behind the design"* disease inverted:
here the DESIGN is behind the code.

| ⭐ the rule — **the implementing session, before the batch closes** | |
|---|---|
| ⭐⭐⭐ **UPDATE the owning `DESIGN_*` / `Architect_Question_*` doc to the AS-BUILT** | ⛔ the classes, the sequences, the seam members, the run-state table — whatever the build changed. ⭐ **The diagram is TRUE again**, which is what obligation ③ can only assert, not fix |
| ⭐⭐ **MARK the prior state SUPERSEDED — do not silently overwrite** | ⭐ use the STATUS block *(`known-rot` / `stale-below` / `superseded-by`)* and move dead content to a `## ⛔ HISTORY` heading, or delete it — 📌 the *"DESIGN DOCUMENT FORMAT"* rules. ⚠ **A reader must never quote the pre-deviation state as current** |
| ⭐⭐ **CITE the design edit in the report** | *"deviated HERE; design updated at §N, prior state moved to HISTORY"* — ⛔ the report POINTS to the durable record; it does not BE it |
| ⚠ **the coordinator VERIFIES the design was updated on merge** | ⛔ a report that argues a deviation but leaves the design stale is an INCOMPLETE batch — send it back, do not merge the lie |

⭐⭐ **Why this is the implementor's job and not the coordinator's** *(same logic as obligation ③)*: only
the session that wrote the code knows exactly how it diverged and why. ⛔ **The coordinator asserting the
as-built from a diff is the mirror error** — it reasons from *how it IS* without the *why*. ⇒ ⭐ **the
builder writes the truth into the design; the coordinator checks it is there.**

📌 **Good precedent already exists — name it so it becomes the norm, not the exception:** the TIME lane
folded its `RestorePostTick()`-seam trim and the drain-as-PULL correction back into
`DESIGN_Time_Architecture.md` §10 *(its report says "§10 carries the correction in its own words")*.
⭐ **That is the behaviour;** this rule makes it an OBLIGATION rather than a good habit some batches keep
and others forget.

### ⛔⛔⛔ AN INVESTIGATION THAT LEARNS SOMETHING MUST UPDATE THE OWNING DESIGN — **not just the report** *(user, `2026-09-02`)*

> ⭐⭐⭐ **User, verbatim:** *"we need claude.md rule to actively search update and maintain the relevant
> designs when we have investigated some topic for example during test fixing etc."*

⚠⚠ **`R-129` and obligation ⑤ do NOT cover this, and that is the gap.** ⭐ `R-129` says **READ** the design
before you touch a feature. ⭐ Obligation ⑤ says fold back a **BUILD DEVIATION**. ⛔⛔ **Neither fires when
you learn something while DEBUGGING, TRIAGING A RED, or CHASING A TEST** — and that is where most findings
actually come from. ⇒ **the finding lands in a report, the report is ephemeral, and the design stays
wrong.**

📌 **Measured on `2026-09-02`, in ONE session** — every one of these was learned during test/red work and
**would have died in a batch report** if the user had not pushed:

| what was learned while fixing/triaging | where it belonged |
|---|---|
| `CE-113` already fixed `AccelGain`, so `CE-103`'s tracker explanation was **stale and misleading** | the `CE-103` row |
| the integration harness projected **5 translators where production has 6** | `DESIGN_Entity_Creation_Unification` §6 ⑥ |
| acceptance ⑪'s condition was **too weak** — the pack itself publishes the event | that design's §6 ⑪ |
| IG registers **no scenario-save handler**, which is what actually enforces the IG persistence rule | `DESIGN_Node_Roles_And_Policies` §7.1 |
| the scenario extractor **strips ownership** rather than filtering on it | same doc, §7.2 |

### ⭐ The rule — **three obligations, all cheap**

| # | ⭐ obligation |
|---|---|
| **①** | ⭐⭐⭐ **When an investigation establishes a durable fact** — a root cause, a refuted lead, a measured invariant, *"X is already fixed"*, *"the enforcement is actually Y"* — **SEARCH for the owning design and UPDATE IT.** ⛔ Writing it only in a report, a tracker row's tail, or a commit message is **insufficient** |
| **②** | ⭐⭐ **SEARCH BOTH TREES: `docs/` FIRST, then `.dev/`** — ⚠ and search by TOPIC, not by a guessed filename. 📌 `2026-09-02`: a filename filter of `scenario\|sav\|persist` **missed the `cgf-scn` programme entirely** because the folder says *"scn"*, and the session then asserted *"no such design exists"* — **twice**. ⇒ ⛔ **grep the CONTENT, and never conclude "it does not exist" from a name filter** |
| **③** | ⭐⭐ **If no owning design exists, say so explicitly** — *"searched `<where>`, no owning design; finding recorded in `<X>`"* — ⭐ and **create or extend one when the finding is load-bearing.** ⛔ A durable fact with no durable home is the disease this rule treats |

⭐ **The checkable artefact:** the report/commit **NAMES the design and section it updated** — *"folded into
`DESIGN_Foo.md` §4"* — exactly as obligation ⑤ already requires for deviations. ⛔ **A batch that fixed a
test and touched no design is not automatically wrong** *(many fixes teach nothing)* — ⚠ **but a batch that
ROOT-CAUSED something and touched no design is.**

⚠ **Why this is not just "be diligent":** every finding in the table above was **measured, written down in
chat, and would still have been lost.** ⇒ ⭐ the rule is about the DESTINATION of a finding, not the effort
of making it.

## ⭐⭐⭐ THE THREE TEST TIERS — **run what the change earns** *(user, `2026-08-20`)*

> ⭐⭐ **User:** *"the amount of tests run for every small fix is rendering the iteration time
> unbearable… The tests rarely catch real bugs."*

⛔⛔ **The first half was a MISDIAGNOSIS, and measuring it is what fixed it.** 📐 On this repo:

| step | time |
|---|---|
| `dotnet build` *(restore + dependency graph)* | ⛔ **79 s** |
| `dotnet build --no-restore` | ⭐ **16 s** |
| `dotnet test --no-build --filter <one class>` | ⭐⭐ **3 s** |
| `dotnet test --no-build` *(Blueprints, 3 870 tests)* | ⚠ **179 s** |

⇒ ⭐⭐⭐ **For a small fix the cost was never the tests. It was RESTORE.**
⇒ ⭐ **`scripts/quick-check.sh <proj> [filter] [--isolated]` — measured 8 s end to end.**

⛔⛔ **AND IT REFUSES TO TEST A FAILED BUILD** — 📌 `dotnet test --no-build` runs a **STALE BINARY** and
prints `PASSED`. ⚠ **That happened twice in one session**; both times it looked like a green.

### ⭐ The tiers

| tier | when | what |
|---|---|---|
| **T0** ~8 s | ⭐ **every edit** | `quick-check.sh` — the touched project, filtered to the touched concept |
| **T1** ~1 min | ⭐ **before a push** | the touched project's **whole** suite, `--no-build` |
| **T2** minutes | ⭐⭐ **the BATCH gate, once** — the implementation session's table | ⭐ the **fast** everything *(unit suites of touched projects, `--no-build`)* + the batch's **new rails** + the **targeted rails that reproduce any bug found**. ⛔ **Not per fix.** ⛔⛔ **The E2E/system suite is NOT here — see T3** |
| ⭐⭐ **T3** *(the E2E slow lane)* | ⛔⛔ **NEVER a foreground blocker** | ⭐ `run-system-tests.sh` *(boots the real editor headless per case)* runs **async** — backgrounded, or in CI/nightly — its result lands in the report or the next session. ⛔ **Never sit on it; never re-run it "to be sure"** |

### ⚠⚠ The second half of the complaint is TRUE, and the tally is worth keeping

| what found the defect, batches 94–101 | |
|---|---|
| ⭐⭐ **a NEW rail written for that item** | `BP-366` · `BP-367` · `BP-368` · `BP-370` · `BP-371` + Batch 100's two author mistakes |
| ⛔⛔ **the ~8 000 existing regression tests** | ⚠ **not one I can name.** The single time an old rail fired it was a **false positive** |
| 🔴 **the USER, by opening the editor** | the scalar tree · the width · the `[x]` · the un-drawn form · the Watch `0` · the double-click · the `312`→`0` seeding |

⇒ ⭐⭐⭐ **The value is in the ~5 NEW rails per batch, not in re-running the old ones** — ⛔ so re-running
them per fix has near-zero expected value, and `T2` is where they belong.
⚠ **Stated fairly:** *"caught nothing"* is not *"worthless"* — a suite also deters the breakage it would
catch, and that cannot be measured. ⛔ **But it is not worth 80 s per edit.**

⭐⭐ **Where the real signal is: `R-124`'s frame rails and `DESIGN_Smoke_Suite.md`'s T2 panel-model
assertions** — 📌 those would have caught **4 of the 7** the user found.

### ⛔⛔⛔ `2026-08-24` — **BUILD ONCE · RUN ONCE · NEVER RE-RUN THE SLOW SUITE "TO BE SURE"** *(user: "cant continue like that")*

📌 **The case, MEASURED `2026-08-24` — and the first guess was wrong.** `HN-037`'s tail spent **~30 min**,
and it was **NOT the tests** *(they run `--no-build` in 3–8 s)*. 📐 **The sink was the BUILD:** the report's
Gate 1 is `dotnet build IOS-IG-SimHost.sln` — the **whole 149-project solution** — and deleting
`ScenarioFileService.LoadScenario` broke that file **plus ~8 test files across several projects**, so the
compile-cascade was chased with **repeated full-solution builds.** ⛔⛔ **Measured on this repo, warm,
`--no-restore`:**

| build | wall-clock |
|---|---|
| ⛔ **full solution** *(149 projects — the Gate-1 command)* | 🔴 **115 s** *(more cold / with restore)* |
| ⭐ **just the affected project** *(`Hrot.Editor`)* | ✅ **8 s** |

⇒ ⭐⭐⭐ **14×.** Six-to-eight cascade iterations at ~2 min each **is the 30 minutes** — plus the E2E suite
run twice *(building-from-source)* on top. ⚠ **Both are one disease: rebuilding/re-running far more than the
change touched.** *(The E2E double-run added nothing — its 9 reds were all one bug the targeted rails
`DeterminismRails` 5/0 + five inverse-edit red-proofs already pinned.)*

| ⭐ the rule — **FOUR parts, all checkable** | |
|---|---|
| **⓪** 🔴🔴🔴 **NEVER full-solution-build inside the fix loop — build the AFFECTED PROJECT** | 📐 **115 s → 8 s, measured.** ⛔ `dotnet build <the.sln>` to answer *"does it compile now?"* is the single most expensive habit here. ⭐ `dotnet build <proj> --no-restore` *(or `quick-check.sh <proj>`)* — the project you touched + the specific test projects that reference it, each ~8 s. ⛔⛔ **A batch §Gates row that says `dotnet build <the.sln>` invites exactly this** — the full-solution build belongs at most ONCE, at the end, or in CI |
| **①** ⭐⭐⭐ **BUILD ONCE per batch, then `--no-build` for EVERY run after** | 📐 restore = **79 s**, `--no-restore` = 16 s, `--no-build` run = seconds. ⛔ **A suite invoked without `--no-build` after the first build is a self-inflicted 79 s.** ⭐ `run-system-tests.sh --no-build` and `quick-check.sh` exist for exactly this — 📌 `HN-037` ran the system suite WITHOUT `--no-build`, so each run re-restored |
| **②** ⭐⭐⭐ **A fix is PROVEN through the rail that reddened for it — NOT by re-running the whole suite** | ⛔⛔ once the targeted/new rail that reproduces a failure is green *(ideally with an inverse-edit red-proof, `HN-037` §5)*, the fix is verified. ⚠ **Re-running the full E2E suite to "confirm" is theater** — 📌 the tally: batches 94–101, the full regression suite caught **nothing** a new rail did not |
| **③** ⭐⭐ **The E2E/system suite is `T3` — ASYNC, never a foreground blocker** | ⭐ background it or leave it to CI; its result lands in the report or the next session. ⛔ **A session must NEVER sit idle waiting on `run-system-tests.sh`.** ⚠ **For an overnight batch this is doubly true** — the slow lane runs unattended by definition |

⭐⭐ **The coordinator's half — DELETION SCOPING** *(the `HN-037` mislabel)*: ⛔⛔ **before calling any
deletion "simple / mechanical," MEASURE THE TEST SURFACE, not just production callers.** 📌 `HN-037` Part
B was verified deletion-safe on **production** *(zero callers bar the facade)* and dispatched as *"simple
deletion of unused code"* — but **8 test callers each asserted a claim that had to be RE-HOMED to a
different owning component** *(round-trips → `ScenarioSerializer.Deserialize`, headers →
`ValidateSubsystemType`, zones → `IZoneManagerService.LoadZones`)*. ⇒ ⭐ **`grep`/`search_graph` the
symbol in TEST assemblies too and classify: mechanical `s/old/new/` *(minutes)* vs re-home-each-claim
*(real work)*.** ⛔ **A re-home reroute is NOT a "1-minute deletion" — do not estimate it as one.**

## Two-session protocol (coordinator ⇄ implementation) — **binding on both sessions**

Both sessions share this repo, so **both load this file**. A *coordinator* session owns the tracker,
writes handoffs and verifies returned diffs; an *implementation* session writes the code. Neither writes
in the other's lane.

### ⭐ The lanes — branch names, authoritative

| Lane | Branch | owns |
|---|---|---|
| **Coordinator** (handoffs, design, gates) | ⭐ **`claude/blueprint-authoring-status-6sr5ld`** | — |
| ⭐⭐ **UI lane** *(incl. the frozen variable area)* | ⭐⭐ **`claude/reset-working-branch-qd1qpv`** *(recorded `2026-08-23`; started perspective Part A at `89acf0f20`)*. ⚠ **MOVED from `claude/hrot-implementation-j1jvin`** | variables · working state · blackboard · `AiShared` · Q38/Details · ⭐ **`MIN`** · ⭐ **the perspective model**. ⭐ ids **`BP-`**, tracker areas **`A`–`G`** |
| ⭐⭐ **BACKEND lane** *(new `2026-08-23`)* | ⭐⭐ **`claude/blueprint-macro-feature-sdmspn`** *(started the StrideMock removal at `89acf0f20`)*. ⚠ **The NAME is historical** — it was the Batch-29-era implementation branch, reused | project/reference structure · the Stride cleanup · **test-suite reliability / harness**. ⭐ ids **`ST-` / `QA-`**, tracker areas **`I` + `N`** |
| ⭐⭐ **MCP lane** *(new `2026-08-26`)* | ⭐⭐ **`claude/mcp-authoring-commands-7l3n49`** | the **ai-debug MCP surface** — `DebugApiService*.cs`/`DebugApiHost.cs`/`DebugApiRouteDocs.cs` *(in `Hrot.Editor`)* + the `tools/ai-debug-mcp` node server *(+ generated `tool-catalog.mjs`/`SKILL.md`)*. ⭐ ids **`MX-`** *(continues the MCP series)*. ⛔ Do NOT touch scenario/menu/AiShared *(UI lane)* or test projects *(backend lane)* |
| ⭐⭐ **TIME lane** *(approved `2026-08-21`)* | ⭐ **`claude/time-system-refactor-batch-104-gp617x`** *(recorded `2026-08-21`, started Batch 104 at `404f95e9a`, branched from dispatch `34deca154`)*; 📌 still confirm by ancestry, not by name | `Fdp.Toolkits/Time/` · `Hrot.Orchestrator` · `ModuleHostKernel` · `Hrot.ClusterRunner.Integration.Tests`. ⭐ ids **`TM-`**, tracker area **`H` only** |

> ⛔⛔ **TWO IMPLEMENTATION LANES, `2026-08-21` — the three rules that keep them apart**
> | ⭐ | |
> |---|---|
> | ⭐⭐⭐ **ID PREFIX PER LANE** | `BP-` = UI/variable · **`TM-` = time.** ⛔ **Structural, not coordination** — 📌 id collisions have bitten this programme **three times** |
> | ⭐⭐ **TRACKER PARTITION** | the time lane writes **ONLY** to `Area H — Time & clock`. ⇒ different regions of one file **merge cleanly** |
> | ⭐ **NO CROSS-LANE FILES** | 📐 measured: different assemblies, no shared production file. ⚠ **A cross-lane edit is a STOP-and-report**, not a judgement call |

⚠ **Corrected 2026-08-26 by the user.** The coordinator lane is
`claude/blueprint-authoring-status-6sr5ld` — the live, active coordinator session. An earlier note here
named `…-gm0akp` and called `6sr5ld` "retired"; that note was itself stale and is **SUPERSEDED** — this
table wins. ⛔ Any document still naming `gm0akp` as the coordinator branch is stale.

⚠⚠ **BOTH implementation lanes were re-pointed by the user on `2026-08-23`** — see the table above. ⭐ The
UI lane moved `j1jvin` → **`reset-working-branch-qd1qpv`**, and **`blueprint-macro-feature-sdmspn`** *(an old
Batch-29-era name)* was reused for a **new BACKEND lane**. 📌 Both pushed correct rule-1b started-markers
naming the dispatch sha, so ancestry — not the name — remains the way to confirm.
⭐ **The coordinator must not assume the name** — locate their branch by which one's first commit
descends from a coordinator commit, not by the name in this table.

⭐ **The implementation session ALWAYS branches from, and updates from, the coordinator branch.** Never
from `main`, never from a previous implementation head that has drifted. Start every run with:

```bash
git fetch origin claude/blueprint-authoring-status-6sr5ld
git merge --ff-only origin/claude/blueprint-authoring-status-6sr5ld   # or branch fresh from it
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
| **8** | ⛔⛔⛔ **A CROSS-CUTTING / SYSTEM-LEVEL change NAMES the INTEGRATION suite that exercises its invariant, and reports RUNNING it** — or states, with evidence, WHY it cannot gate | ⭐ **unit rails prove a class; only an integration suite proves the SYSTEM still holds together.** 📌 `2026-08-22`: the TIME lane changed cluster-time control and gated `Fdp.ModuleHost.Tests` + filtered rails but **never named `SimTimeSyncIntegrationTests` / `ClusterRunner.Integration.Tests`** — the suites that assert *nodes stay time-synced*. ⇒ the coordinator had to run them to answer *"did cluster time rot?"* |

⛔⛔ **Row 8, stated as a checkable habit:** if the change touches the clock, the kernel schedule, the
orchestrator, transport, or anything cross-node, the §Gates table has a row for the INTEGRATION suite
that would break if the invariant broke — run in ISOLATION if the full suite is flaky *(`--filter` the
sync tests)* — ⭐ **and if that suite CANNOT gate** *(a pre-existing crash, non-deterministic discovery)*,
⛔ **that is itself a reported FINDING with the base-sha proof**, not a silent omission. 📌 The
`ClusterRunner.Integration.Tests` DDS-allocator crash is exactly such a finding: it makes the suite
un-gateable and it is **pre-existing** — say so, do not let its noise stand in for *"verified."*

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

### ⭐⭐ Rule 1c — **WITHDRAWING an item from a RUNNING batch** *(first used `2026-08-20`)*

⭐ **Rule 1 forbids amending a dispatched handoff; rule 1a allows it only while UNSTARTED.** ⛔ **Neither
covers the case that actually arose:** a batch is **running** and the user rules that one item is
**wrong to build at all** *(here: Properties as a StructEdit document — `R-109`)*.

| ⭐ the legal form — **all four, or it is a silent amendment** | |
|---|---|
| **①** | ⭐⭐ **write a SEPARATE `STEER_*.md`** carrying the user's words, the reasoning and what to build instead — ⛔ **never edit the item's text in place** |
| **②** | ⭐ **mark the handoff section `WITHDRAWN AND REPLACED`** with a link, and say it in the file's `known-conflict` — ⛔ do not delete it, the run may already have read it |
| **③** | ⭐⭐⭐ **the USER relays the steer to the running session** — ⛔ **the coordinator does not reach into a live run** |
| **④** | ⭐⭐ **state explicitly which items are UNCHANGED** — 📌 `R-106`: the withdrawal must not stop the batch |

⚠ **A withdrawal is not a correction of the implementation session** — ⭐ say so, in the note. ⛔ Work
already built against the withdrawn item is **reverted by them, not argued about.**

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

### ✅✅✅ FREEZE LIFTED — `2026-08-25` *(user: "unfreeze the variable model lane, freeze no longer needed, no one is working on variables")*

> ⭐⭐⭐ **The variable-model implementation freeze below is LIFTED.** Any session may now edit variables,
> working state, the blackboard panel, and `Hrot.Editor.AiShared` — ⛔ **do NOT re-apply the freeze** and
> ⛔ **do NOT gate work on it.** ⚠ Ordinary coordination still applies *(two-session protocol, rule 7 re-sync,
> non-colliding files)* — but the SINGLE-SESSION exclusivity is gone. The text below is retained as HISTORY
> so older documents that cite "the freeze" resolve to this lifting.

### ⛔ HISTORY — IMPLEMENTATION FREEZE *(user, `2026-08-15`; ✅ LIFTED `2026-08-25` — see banner above)*

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

#### ⭐⭐⭐ `2026-08-21` — **THE FREEZE IS SCOPED TO THE VARIABLE MODEL. A TIME LANE IS APPROVED.**

🔒 **User, verbatim:** *"the freeze was about the variable model, time lane is fine. approved."*

| ⭐ | |
|---|---|
| ⭐⭐ **the freeze still binds** | variables · working state · the blackboard panel · `Hrot.Editor.AiShared` · the Details/Q38 work — ⛔ **one session only** |
| ⭐⭐⭐ **OUTSIDE it, and now APPROVED for a second session** | ⭐ **the TIME lane** — `FDP/Toolkits/Fdp.Toolkits/Time/` · `Hrot.Orchestrator` · `ModuleHostKernel` · `Hrot.ClusterRunner.Integration.Tests` |
| ⛔⛔ **`MIN` is NOT in the time lane** | 📐 it edits `BlueprintDebugSession` · `BlueprintLiveValueWriter` · `VariableEditCommit` ⇒ **variable-edit code** ⇒ ⭐ **it ships with the UI/variable session** |

📄 **The split, the conflicts and the amendments: [`docs/blueprints/PLAN_Time_System_Refactor.md`](../docs/blueprints/PLAN_Time_System_Refactor.md) §5.**

### Checking "did they see X?" — do it correctly, and name the run

Never say *"they never saw it"* — that reads as a property of the session when it is a property of one
commit. Test against **what they branched from**, not their head:

```bash
git log -1 --format='%p' <their-first-commit-of-that-run>   # the commit they built on
git merge-base --is-ancestor <my-commit> <that-parent>
```

Report *"not in the commit they built from (run starting `<sha>`)"*. The same document is routinely
absent for one run and present for the next — both statements true, about different runs.

## ⛔⛔ SWEEP THE DESIGN CORPUS **BEFORE TRIAGING FINDINGS INTO BATCHES** *(user ruling, `2026-08-17`)*

> ⭐⭐⭐ **User, verbatim:** *"so that means you are issuing corrective batches without having read the
> design intent behind the failures reported — this is not good."*

📌 **The case.** Eight findings came back from the first visual check. The coordinator measured **code**
for every one of them — root causes, line numbers, call paths — and **never read the design roadmap.**
⛔ **`Architect_Question_32_…_ANSWERS.md` §4 is a SEQUENCING TABLE that already specified at least four
of the eight**, and §2.2 had already counted the duplicate surfaces.

| what was issued | ⛔ what the design already said |
|---|---|
| *"rename the three `Variables` windows to unique names"* | ⭐⭐ **`U-16` / ruling 9: RETIRE them** — *"no keeping two implementations for the same concept… `U-16` is **not optional cleanup; it is the acceptance criterion**."* ⚠ **And bigger than assumed: THREE variable surfaces + `InspectorWindow` in two assemblies** |
| *"every section's `[+]` opens the same dialog"* | ⛔⛔ **`Q32` ruling 8 already merged `Variable` ≡ `WorkingState`** ⇒ **the batch was hardening a section the design collapses** |
| *"MEASURE the Details panel, I did not"* | ⭐ **ruling 2's selection routing**, sequenced as `U-6` |
| *"Batch 83: the Watch has no entry points"* | ⭐ **already specified**: *"make `HandlePinValueChanged` real · EDITING through the same dialog · show NOTHING before the run"* |

⇒ ⭐⭐⭐ **A measured root cause tells you WHY IT BROKE. It cannot tell you WHETHER THE THING SHOULD
EXIST.** ⛔ **Fixing a surface the design retires is worse than not fixing it** — it spends a batch and
cements the duplicate.

### ⭐ The rule

1. ⭐⭐ **Before triaging ANY finding into a batch, sweep for its design intent** — ⛔ **not after, not
   "if it looks non-obvious."** ⚠ **The failures above all looked obvious.**
2. ⭐⭐⭐ **Read the SEQUENCING TABLES first.** 📌 `Q32` §4 is the model: a batch-by-batch roadmap with
   *why here*. ⛔ **A finding that already has a planned batch is NOT a new finding.**
3. ⭐ **Order of lookup is the `2026-08-15` table** *(`*-DESIGN.md` → `reports/*-REPORT.md` → `TASK-DETAIL.md`)*,
   ⭐ **plus `docs/blueprints/Architect_Question_*_ANSWERS.md`** — ⚠ **the ANSWERS files carry the
   rulings; the question files carry only the options.**
4. ⛔ **State the design basis IN the handoff, per item.** ⭐ *"design says X, this batch does Y"* — if
   that sentence cannot be written, **the sweep was not done.**

## ⛔⛔⛔ THE LEDGER MAY NOT ASSERT WHAT THE CODE IS *(user ruling, `2026-08-18`)*

> ⭐⭐⭐ **User:** *"i don't know where i can believe your conclusions. how comes it could have happened
> again… is the ledger a good idea? I would rather spend more tokens of well-investigated design/batch
> than keep issuing some wrong ones."*

📌 **The mechanism, stated exactly.** `rulings-check.py` verifies that **a quote still exists in a
document.** ⛔⛔ **It cannot detect that a claim about CODE became false — the document did not change,
the CODE did.** ⚠⚠ **Twice on `2026-08-18` a row was GREEN AND FALSE** — `R-04` *("the tagged type is
the VIEW")* and `R-25` *("`B′` is blocked")* — ⭐ **and both sent me to build things that already
existed.** ⇒ ⛔ **The gate manufactured confidence and I spent it.**

| kind of row | ⭐ verdict |
|---|---|
| ⭐ **DECISION** *(settled by a person: "no two implementations", "no visual checks until X")* | ✅ **canon. Does not decay** |
| ⛔ **STATE CLAIM** *("X is not built", "X is blocked", "there are two Y")* | ⛔⛔ **NOT canon — it rots silently.** ⭐ **It belongs in `RULINGS.md` §M as a QUESTION plus the command that answers it** |

⇒ ⭐⭐⭐ **§M — MEASURE, DON'T MEMORISE. ⛔ Never quote an answer from §M; RUN THE COMMAND.**
⭐ A measurement older than **14 days is a rumour** — `rulings-check.py` warns.

## ⛔⛔⛔ THE INVESTIGATION PHASE — **before any batch or design, not "if it looks non-obvious"**

⭐⭐ **Two parts, BOTH recorded in the artefact with the queries actually run:**

| ⭐ | |
|---|---|
| **①** | ⭐⭐ **Enumerate the code surface with codebase-memory** *(`search_graph`)* — ⛔ **`grep` can only CONFIRM a guess; it cannot enumerate** |
| **②** | ⭐⭐ **Read the NON-SUPERSEDED design markdowns** for the area — ⛔ check each `STATUS` header before quoting it |

### ⛔⛔⛔ THE INTENT IS IN THE DESIGN DOC, NOT THE CODE — **read it BEFORE you touch or design a change** *(user, `2026-08-21`)*

> ⭐⭐⭐ **User, verbatim:** *"you failed to read existing designs before touching or designing changes to
> related existing features … you need to make sure you know the intents BEFORE changing stuff. intents
> are not in code. they are in design docs. code is usually behind."*

⛔⛔ **This is the MOST GENERIC rule and it governs every batch and every design.** 📌 **The case:** I
designed a `LiveWriteFrame` re-sample clock for the watch row after reading only the sampler code and a
few ledger rows — the intent *(optimistic yellow display, already BUILT-but-unwired)* was in
`DESIGN_Variable_Details_And_Editing.md` §4a/§6, which nothing I read pointed to. I ran the docs sweep
only AFTER being corrected.

| ⭐ the checkable habit — **for ANY change to an existing feature** | |
|---|---|
| **①** | ⭐⭐⭐ **`grep -rln docs .dev` for the feature and READ the owning `*-DESIGN`/`*_Detailed_Design`/`DESIGN_*` `*.md` END-TO-END — BEFORE you read the code's current mechanism.** ⚠ **Intent lives across ALL of `docs/` AND `.dev/`** — ⭐ **`docs/`** *(the CURRENT intent — `blueprints/`, `UX/`, `projects/`)* · ⭐⭐ **`.dev/<programme>/*-DESIGN.md`** *(IMPLEMENTED intent — the WHY behind built features, often more than the code; ⚠ check for a newer `docs/` supersession)*. ⛔ The code hands you an *"obvious"* fix that reasons from **how it IS**; a design doc — even an old `.dev` one — says **how it was MEANT to be** |
| **②** | ⭐⭐ **Cite the owning DOC + SECTION in `design-basis`** — ⛔ **NOT only a `RULINGS.md` row.** 📌 The ledger is the INDEX; the design doc is the intent. A ledger citation is not proof you read the design |
| **③** | ⚠ **"the code does X" is never "X is what was intended."** Code is usually behind the design — a mismatch means the code is unfinished, not that the design is wrong |

⭐ *(Indexed as `R-129`; the substance lives HERE because it is generic, not buried in the ledger.)*

### ⭐⭐⭐ And the one rule that would have caught almost every failure of `2026-08-18`

> ⛔⛔⛔ **NEVER claim *"X is not built"* without running the enumeration that would find X.**

📌 **Every wrong turn that day except the two semantic ones was a FALSE NEGATIVE of exactly that
shape** — *"`D1` is not done"*, *"`B′` is blocked"*, *"the emitters still emit separately"*, *"there is
no way to pin"*, *"there are two watch windows"* *(four)*. ⭐ **Cheap, and checkable in the artefact.**

### ⚠ What NONE of this fixes — **stated so nobody over-trusts it**

⛔ **Semantic inference.** Reading `BlueprintSharedState` and taking *"shared"* to mean **cross-entity**;
reading *"already remaps"* and taking it to mean **preserves**. ⭐⭐ **No enumeration catches these.**
⇒ ⭐ **When a claim depends on what a symbol MEANS, read its BODY** — ⛔ **not its name, not its
comment, not its doc header.**

## ⛔⛔⛔ INVENTORY BEFORE DESIGN — **grep cannot enumerate** *(user, `2026-08-18`)*

> ⭐⭐⭐ **User:** *"again you were designing something you did not investigated deep enough. how to
> prevent this? are you using codebase memory before every design?"* ⛔ **Honest answer at the time:
> NO — not once that session, while the graph sat indexed at 171k nodes.**

📌 **The failure is precise, and it is NOT laziness about reading code — I read plenty.**
⭐⭐ **`grep` answers *"does X exist?"* — it can only CONFIRM something I already suspected.**
⛔⛔ **A design decides WHERE something lives, which requires the FULL SET of things it could live
beside. Only the graph enumerates.**

| 📌 three times, same shape | |
|---|---|
| **`R-11`** | ruling 9's target was **three** variable surfaces, not one |
| **`R-72`** | **two** watch windows… |
| ⭐ **then FOUR** | one `search_graph` call found `EntityWatchPanel` and `FdpEntityWatchWindow` too |

### ⭐ The rule — **and it produces a checkable artefact, like every other rule that stuck**

1. ⭐⭐⭐ **Before ANY design document or architect question, run `search_graph` and ENUMERATE.**
   ⛔ **Not after. Not "if it looks non-obvious."** ⚠ **All three misses above looked obvious.**
2. ⭐⭐ **Put the result in an `INVENTORY` section: the query you ran, its `total`, and the list.**
   ⭐ **A count of 1 is a fine answer** — ⛔ **an ABSENT section means the enumeration did not happen.**
3. ⭐ **Gated:** `python3 scripts/design-digest.py --check` **fails** when a recently-changed
   `Architect_Question_*.md` has no `INVENTORY` block. ⭐ Report it with the other gates.
4. ⚠ **The graph may be unindexed in a fresh cloud session** — ⭐ `list_projects`, then
   `index_repository` if empty *(tens of seconds)*. ⛔ **Not a reason to skip the step.**

## ⭐⭐⭐ ARCHITECT QUESTIONS — **I analyse and SUGGEST, the user APPROVES** *(user, `2026-08-17`)*

> ⭐⭐ **User, verbatim:** *"remember no architect will answer the architect question, you and me need to
> resolve those, so you analyze and suggest, i approve."*

⛔⛔ **Do NOT leave an architect question in an OPEN, option-shaped state waiting for someone.**
⚠ **`Q39` was written as "`Q39-A`–`E`, open, not scheduled" — that is the old relay habit**, and the
relay does not exist.

⇒ ⭐ **Every architect question carries a RECOMMENDED ANSWER PER SUB-QUESTION**, with the reasoning and
the blast radius, ⭐⭐ **written so the user can reply "approved" or name the one they want changed.**
⛔ **Options without a recommendation are work handed back to the user.**

## ⛔⛔⛔ RULE ZERO-A — **AFTER `RULINGS.md`, READ YOUR LANE'S RESUME DOC** *(added `2026-08-27`)*

> ⭐⭐⭐ **`CLAUDE.md` is auto-loaded and is the ONLY reliable channel** *(RULE ZERO's own words)*. ⛔⛔ Until
> `2026-08-27` **nothing auto-loaded named any lane's resume doc** — 📐 measured: `grep -c RESUME_UI_Lane`
> over `CLAUDE.md` and `RULINGS.md` returned **0 and 0**. ⇒ 🔴 a post-compaction session read the canon and
> then had to **guess** where the live work was. ⭐ That is the hole this rule closes.

| lane / branch | ⭐ read this SECOND, in full |
|---|---|
| ⭐⭐ **UI lane** — `claude/reset-working-branch-qd1qpv` | 📄 **[`docs/blueprints/RESUME_UI_Lane.md`](../docs/blueprints/RESUME_UI_Lane.md)** — ⭐ its **STATUS block's `current-answer` names the ONE section to start from**; ⛔ do not read the file top-to-bottom, most of it is dated HISTORY |
| **coordinator** — `claude/blueprint-authoring-status-6sr5ld` | 📄 `docs/blueprints/COORDINATOR_RESUMPTION.md` *(+ the `DESIGN BRIEF` obligation)* |
| **other lanes** | ⚠ **no resume doc verified by this session** — ⛔ do not assume one exists; ask, or find the lane's newest `docs/blueprints/` design |

⭐⭐ **The habit, in order:** ① `RULINGS.md` *(canon)* → ② **your lane's resume doc's `current-answer` section**
*(what is IN FLIGHT)* → ③ the design docs it cites. ⛔ **Skipping ② is how a session re-derives a plan that is
already written down**, which is the disease RULE ZERO exists to treat.

## ⛔⛔⛔ RULE ZERO — **READ `docs/blueprints/RULINGS.md` BEFORE ANYTHING ELSE** *(user, `2026-08-17`)*

> ⭐⭐⭐ **User, verbatim:** *"We start over and over after compaction, **you forget all the design
> decisions and then steer the development on wrong base and act as if you never seen any of that.**
> We can not work like that, you need to put to your rules something that fixes this."*

⭐⭐ **The diagnosis, stated once:** ⛔ **CODE ANSWERS *"HOW IT IS." IT CAN NEVER ANSWER "HOW IT WAS
MEANT TO BE."*** ⇒ **every wrong turn this programme has taken came from reasoning off code when the
question was a design question.** ⚠ **Four in one day** *(`2026-08-17`)*: the quick-add ruled
not-a-defect · corrective batches triaged with no design sweep · `Q39` framed as UI when it is
infrastructure · *"the cross-host name is a coincidence"* when `Role` is genuinely shared.

⚠⚠ **The earlier rules did NOT prevent any of them, and the reason matters:** ⛔ **they asked me to
DECIDE to search.** ⭐ **A rule that depends on remembering to be diligent decays across compaction —
that is exactly the failure being fixed.**

### ⭐⭐⭐ The rule — **three obligations, all cheap, all checkable**

| # | obligation | ⭐ why it survives compaction |
|---|---|---|
| **0** | ⭐⭐⭐ **FIRST ACTION of every session, and immediately after every compaction: READ [`docs/blueprints/RULINGS.md`](../docs/blueprints/RULINGS.md) IN FULL.** ⛔ **Before answering anything, before any tool call about the work** | ⭐ **`CLAUDE.md` is auto-loaded; it is the ONLY reliable channel.** ⭐⭐ **The ledger is deliberately SHORT so this is always affordable** |
| **1** | ⭐⭐ **NO design answer, handoff item or architect-question row without a CITED design basis** — ⛔ **or the explicit sentence *"searched `<where>`, no design record found."*** ⚠ **An uncited design claim is a defect, however well measured the code was** | ⭐ **it produces a VISIBLE artefact the user can check** — if the citation is missing, the sweep was not done |
| **2** | ⭐⭐ **Every ruling discovered in the corpus gets a ROW IN THE LEDGER IMMEDIATELY**, with a machine-checkable probe | ⛔ **otherwise the next session pays the same cost again** — ⭐ **that is the whole disease** |

### ⭐ The ledger cannot rot — **it is gated**

```bash
python3 scripts/rulings-check.py      # every quote must still exist verbatim in its cited source
```

⭐⭐ **Run it whenever the ledger or a cited document changes**, and ⭐ **report it in the gate table
alongside `tracker-counts.py --check`.** ⚠ **A failing probe means the design record MOVED — ⛔ find the
new home, NEVER delete the ruling.**

### ⛔ What this does NOT license

⛔ **It is an INDEX, not a replacement for the corpus.** ⭐ **A question with no row is a question that
needs a SEARCH** *(`RULINGS.md` §4 gives the order)* — ⛔ **not a question you may answer from code.**

## ⭐⭐⭐ RULE ZERO, PART 2 — **RE-LEARN WHAT MOVED** *(user, `2026-08-17`)*

> ⭐⭐ **User:** *"how to make it a permanent habit that after every compaction you re-learn the most
> recent (few days) design intents?"*

⛔ **`RULINGS.md` indexes what is SETTLED. It cannot tell you what MOVED LAST WEEK** — and after a
compaction the recent documents are ⭐⭐ **exactly what a session has lost and cannot know it has lost.**

### ⭐ The habit — **two commands, always, before anything else**

```bash
python3 scripts/design-digest.py            # what changed in the last 7 days, with its ruling lines
python3 scripts/rulings-check.py            # the canon still matches its sources
```

⭐⭐⭐ **Run BOTH at session start and immediately after every compaction**, alongside reading
[`RULINGS.md`](../docs/blueprints/RULINGS.md). ⛔ **The digest is a SCRIPT, not a document, on purpose:**
⚠ **a hand-maintained "recent changes" file rots the moment someone forgets it — which is the disease.**
⭐ Generated from git, **it cannot lie about what changed.**

⭐⭐ **`rulings-check.py` now also WARNS when a cited source changed after the ledger did** — ⛔ **the
quote can still match while the ruling around it has moved.** ⚠ **That is exactly how `R-03`/`R-05` came
to cite a table its own document marks SUPERSEDED.**

## ⭐⭐ DESIGN DOCUMENT FORMAT — **so a document can be followed after compaction**

> ⭐⭐ **User:** *"how to formalize the creation of design docs so they are easy to follow after
> compaction?"*

📌 **The failure this fixes, three times in one day:** ⛔⛔ **I read a document and not its supersession
banner.** ⚠ **`Variable_Model_Unification.md` keeps a SUPERSEDED stage table BELOW the live one, and I
quoted the dead half** — twice, and wrote it into the canon.

### ⭐ Every design document under `docs/` starts with a STATUS block

```
<!--STATUS
state: LIVE | SUPERSEDED | WITHDRAWN | HISTORICAL
updated: YYYY-MM-DD
current-answer: <which section holds the CURRENT answer>
stale-below: <what in this file is history and must NOT be quoted>
superseded-by: <path>            (when state is not LIVE)
known-rot: <statements in here that a newer document has overturned>
known-conflict: <another document that disagrees, and that this has not reconciled>
-->
```

| ⭐ rule | why |
|---|---|
| ⭐⭐⭐ **`current-answer` names the live section** | ⛔ **a reader must never have to infer which of two tables is current** |
| ⭐⭐ **`stale-below` names the history** | ⭐ the cheapest possible fix for the failure above |
| ⭐⭐ **`known-rot` / `known-conflict` are FEATURES, not shame** | ⛔ **a document that admits it is partly overtaken is safer than one that reads uniformly authoritative.** ⚠ **`DESIGN_Parameter_Model.md` is marked authoritative AND describes a retired `BP1031`** |
| ⭐ **superseded content moves to the BOTTOM** under a `## ⛔ HISTORY` heading, or is deleted | ⛔ **never left inline above live content** |
| ⭐ **`python3 scripts/design-digest.py --check`** audits it | ⛔ a convention nothing checks is a convention that decays |

⚠ **Retro-fit lazily, not in a sweep** — ⭐ **add a STATUS block to any design document you TOUCH**, and
to any the ledger cites. ⛔ **Do not spend a batch on the back catalogue.**

## ⛔⛔ SCOPE IS FROZEN AT THE DISPATCH SHA *(user, `2026-08-17` — cost: 20 minutes)*

> ⭐⭐ **User:** *"i stopped it because it found your new ruling and tried to adapt to it and it took
> another 20 minutes."*

📌 **What happened.** Rule 1 forbids amending a **dispatched handoff**. ⛔ **Nothing protected the
CORPUS AROUND IT.** The coordinator rewrote `RULINGS.md`, `Q39` and the plan **while Batch 81 ran**, and
⭐ **rule 4 tells the implementation session to pull and read changed design files** — so it did, found a
ruling contradicting its own handoff, and spent 20 minutes adapting.

⇒ ⭐⭐⭐ **Rule 4 worked exactly as written. The rule was incomplete.**

| ⭐ the fix | owner |
|---|---|
| ⭐⭐⭐ **Every handoff states: *"Your scope is FROZEN at `<dispatch-sha>`. Documents that change after it are FYI ONLY."*** | coordinator |
| ⭐⭐ **A later document that INVALIDATES an item ⇒ STOP and REPORT IT. ⛔ Do NOT adapt, do NOT revert** | implementation |
| ⭐ **While a batch is in flight, prefer the NEXT handoff over rewriting canon** — ⚠ if canon must move, say **"does not affect batch N"** in the commit | coordinator |

⭐⭐ **Batch 81 got the SPIRIT right without the rule:** it measured `Q39`'s premises, found them false,
**did not comply and did not silently keep** — it reported and offered the revert. ⭐ **That is the
behaviour; this rule just makes it cheap instead of costing 20 minutes.**

## ⭐⭐⭐ THE MIRROR ERROR — **design-reasoning without measurement** *(`2026-08-17`)*

⛔ **I spent a day building rules against *"reasoning from code without the design."*** ⚠⚠ **Then I
ordered Batch 81 §3b pulled on two premises I never measured** — *"a dialog per section"* **(false: one
modal class)** and *"hardens the split"* **(false: it removed a parallel create path)**.

⇒ ⭐⭐ **A design ruling tells you what SHOULD exist. ⛔ It cannot tell you what a diff ACTUALLY DID.**
⭐⭐⭐ **Before ordering a built thing reverted, MEASURE WHAT IT BUILT.** ⚠ Both directions need
checking — *the design for intent, the code for fact.*

## ⭐⭐⭐ THE DESIGN BRIEF — **how the user VERIFIES the re-learn happened** *(user, `2026-08-17`)*

> ⭐⭐ **User:** *"how do i find out that after compaction you re-learned the design intents? can you
> report it automatically… which forces you to read those first?"*

⛔⛔ **The hook injecting the canon proves it ARRIVED. It does not prove it was ENGAGED WITH.**
⚠ **On `2026-08-17` I had READ documents and still missed their supersession banners four times** —
⭐ **reading is necessary and not sufficient; the step that fails is JOINING the canon to the work.**

⛔⛔ **THE BRIEF IS A COORDINATOR OBLIGATION ONLY** *(`2026-08-18`)*. ⚠ **On `2026-08-18` an
implementation session wrote a brief instead of starting Batch 84** — ⭐ **correctly following a rule
written for the other lane.** ⇒ ⭐ **the hook now detects the branch and tells the implementation lane
to skip it**; ⛔ **if you are not on `claude/blueprint-authoring-status-6sr5ld`, your first move is rule
7 then rule 1b's started-marker, NOT a brief.**

⇒ ⭐⭐⭐ **The FIRST reply of every COORDINATOR session, and the first after every compaction, OPENS with
this block — ⭐ and then ANSWERS THE USER'S QUESTION IN THE SAME REPLY.**

⚠⚠ **`/compact` ends with NO assistant turn**, so a genuinely automatic post-compaction brief is **not
achievable** — it can only land on the next thing the user types. ⛔⛔ **Therefore it must NEVER displace
what they typed.** 📌 **User, `2026-08-18`:** *"it needs to be the automatic reply after compaction, not
ignoring what the user wrote in his first prompt after compaction."*
⚠ **An earlier version said *"and nothing else"* and deferred the answer to a second reply** — ⭐ **that
was rejected, rightly: it made the user pay a round-trip for a check that is my obligation, not theirs.**
⇒ ⭐ **The brief is a HEADER on the reply, ⛔ never a replacement for it.**

```
DESIGN BRIEF (post-compaction)
  ledger      : N rulings, N/N probes verifying, staleness warnings on <files|none>
  in flight   : <batch + the sha its scope is frozen at, or "nothing">
  constrains  : <ruling ids that BIND what I am about to do>
  moved lately: <any doc from the digest that changes it, with its date>
  spot-check  : <the three ids the hook drew, in my own words, joined to the work>
  would have got wrong: <one concrete thing, or "nothing identified">
```

| ⭐ line | why it is there |
|---|---|
| ⭐⭐ **`spot-check`** | ⛔ **three ruling ids drawn AT RANDOM by the hook** ⇒ **a canned answer cannot fit.** ⚠ **Reciting is not the test — JOINING them to the work is** |
| ⭐⭐⭐ **`would have got wrong`** | ⛔ **the money line.** 📌 Every `2026-08-17` failure was me proceeding **confidently**. ⚠ **If this is vacuous or generic, the brief was theatre — ⭐ the user should push back on that line alone** |
| ⭐ **`constrains`** | ⛔ forces the join between canon and the task **in hand**, not in general |

⛔ **If a line cannot be filled, SAY SO.** ⭐ **An empty line is a FINDING about the ledger** — ⛔ never
something to paper over.

### ⭐⭐⭐ `RELEARN` — **the magic phrase, usable ANYWHERE** *(user, `2026-08-18`)*

> ⭐⭐ **User:** *"maybe we could add some 'magic phrase' that i will use whenever i want the session to
> relearn the design decisions? to be used as part of resumption documents, and available everywhere
> else."*

⭐ **The token is `RELEARN`** — ⛔ **all caps, standing alone**, so ordinary prose about relearning does
not trip it.

| ⭐ where it works | what I do |
|---|---|
| ⭐⭐ **typed by the user**, in any message | **stop, run the re-learn, open the reply with the `DESIGN BRIEF`**, then answer what they asked |
| ⭐⭐ **embedded in a repo document** *(resumption docs, handoffs, design files)* | **the same, when I read that document for the task in hand** — ⭐ it is the user's own canon telling me to ground myself before acting on that file |
| ⭐ **`/relearn`** | the same, as a slash command — 📄 `.claude/commands/relearn.md` |

⭐⭐ **What it runs:** `bash scripts/session-design-brief.sh` *(ledger · 7-day digest · probe verdict ·
three RANDOM ruling ids)*, plus a fresh look at both branches and the in-flight batch.
⛔ **It is NOT a promise to re-read everything** — ⭐ it is the same grounding pass the post-compaction
hook forces, on demand.

⚠ **On the implementation branch it is a no-op by design** — the brief is a coordinator obligation.

### ⚠ What this does NOT prove — **stated so nobody over-trusts it**

⭐ It proves the canon was read and applied **at that moment**. ⛔ **It cannot prove I will still apply
it three hours later** — ⭐ **that is what the per-item citation rule covers**: no handoff item or design
answer without a cited basis, which leaves a checkable artefact every time, not just at session start.
