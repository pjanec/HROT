# BOOTSTRAP — the cross-host variable & call model: BTree × HSM × Blueprint

> **Paste this whole file as the opening message of the new session.** It is self-contained.
> **Written `2026-08-14`** by the blueprint **coordinator** session (branch
> `claude/blueprint-authoring-status-gm0akp`), which has just finished 9 batches of the blueprint
> variable unification.

---

## 1. What this session is for

⭐⭐ **One question, three hosts:** *how do **parameters**, **working state** and **asset variables**
work across **BTree**, **HSM** and **Blueprint** — and how does a **BTree node or an HSM slot call a
blueprint**?*

⚠⚠ **You are a DESIGN session. Nothing is built here.** Two implementation programmes are already
running and you do not own either. ⭐ **Your output is a settled cross-host model, an architect round,
and then handoffs those sessions can take.**

📌 **Events are IN SCOPE as a known gap** — the blueprint side has `EventDispatcherDecl` /
`CustomEventDecl` / `EventEntryNode` / `WaitForEventNode`, and the HSM side has `HSM-009` open.
⛔ **Event authoring is NOT finished on either side.** ⭐ **Treat it as the fourth topic, and expect it
to be the least settled.**

---

## 2. Where everything lives

| | branch | owns |
|---|---|---|
| **Blueprint coordinator** | `claude/blueprint-authoring-status-gm0akp` | the variable unification, the tracker, the handoffs |
| **Blueprint implementation** | `claude/hrot-implementation-j1jvin` | all blueprint feature code |
| ⭐ **HSM design + visual editor** | `claude/hsm-visual-editing-9ngei4` | the HSM audit, `Hsm_Issues_Tracker.md`, the opening prompt below |
| **you** | 📐 **pick a fresh `claude/…` branch and say which** | this design |

**Read in this order:**

1. ⭐⭐ [`Hsm_Parameters_And_Variables_OPENING_PROMPT.md`](https://github.com/pjanec/HROT/blob/claude/hsm-visual-editing-9ngei4/docs/blueprints/Hsm_Parameters_And_Variables_OPENING_PROMPT.md) — **the HSM session's questions, Q-A…Q-G.** This session exists largely to answer them.
2. `Hsm_Integration_Map.md` · `Hsm_Issues_Tracker.md` *(same branch)*
3. `docs/blueprints/Variable_Model_Unification.md` + `Variable_Editing_UI.md` — ⭐ **the blueprint side's `Role` × `Scope` design**
4. `docs/blueprints/PLAN_Variable_Unification_Tasks.md` — `U-1`…`U-16`, **and which have landed**
5. `docs/blueprints/RESUME_START_HERE.md` §7i–§7x — **the per-batch verification record.** ⚠ **This is where the corrections live.** Several things in the older design docs are known wrong; the batch records say which
6. `BTree_AiActionParameterBinding_Detailed_Design.md` · `Blackboard_Authoring_Detailed_Design.md`
7. `.claude/CLAUDE.md` — ⛔ **binding. Read before writing anything.**

---

## 3. ⭐⭐ What the blueprint side has ACTUALLY built — as of `2026-08-14`

⚠ **The HSM doc's §1 reads the model from the DESIGN docs. Nine batches have shipped since.**
⭐ **Verify each line against the code before answering; here is what moved.**

| landed | what |
|---|---|
| ✅ **`U-1`** | ⭐⭐ **a golden-corpus harness** — 42 assets × two tiers (`StructureHash` + every emitted struct field name/type/offset/size + the diagnostic multiset; and the full generated source stored as files). **Proved to bite.** ⇒ *"nothing changed"* is now falsifiable |
| ✅ **`U-2`** | `Compile` no longer mutates the caller's `Graph` objects |
| ✅ **`U-3`** | 🔴 **`BP-226` closed** — a variable reference now carries **`VariableRef(VariableKind, int)`** from Stage 5 → IR → Stage 7. ⛔ **`VarFieldName(int)` no longer exists** |
| ✅ **`U-4`/`U-5`** | the editor's `bool isParams` is gone (it was **two values over three lists**); the reference count is real; ⭐ **`SupportsRoleScopeEditing`** is a capability with **no default body** |
| ✅ **`U-7`/`U-8`** | 🔴 **`BP-228` closed** — a made-up type id is refused (`BP1671`); the type picker is **safe by construction** |
| ✅ **`U-9`** | ⭐⭐ **`BlueprintDeclaration` + `BlueprintAsset.Declarations`** — one tagged sequence over `Parameters` ∪ `WorkingState` ∪ `Variables`. ⚠ **Built INVERSE of the plan: the tagged type is the VIEW, the three lists are still the STORAGE** |
| ✅ **`U-15`** | all **58** shipped assets canonicalised; ⭐ **the canonical JSON form is now INDENTED** |
| 🟠 **`U-10`** | the `v1 ⇄ v2` transform pair ships and **byte-identity is proved on all 58** — ⛔ **but nothing writes or reads v2 yet** |
| ✅ **`U-11`/`U-14`** | ~135 consumers moved onto `Declarations`; ⭐ **`MakeUniqueName` is now cross-kind** (`BP-232`) |
| ⏭ **`U-12`** | rails restated + **the store flip** — **in flight** |
| ⛔ **`U-6`/`U-13`/`U-16`** | ⚠ **UNSCHEDULED** — they hard-require a human at a screen, and the visual check has not run for 14 batches |

### 3.1 ⭐ The four vocabulary items you will need

| | |
|---|---|
| **`DeclarationKind`** | `Parameter` · `WorkingState` · `Variable`. ⭐ **Deliberately NOT `Ir.VariableKind`** — that enum has an `Unresolved` sentinel no *stored* declaration can be in; the two are bridged by an explicit total mapping |
| **`VariableKind`** *(IR)* | `Unresolved = 0` **is the default on purpose**, so a forgotten assignment throws instead of silently meaning the first list |
| **`Role` × `Scope`** | the unified model: role `input`/`state`; scope `Node` \| `Behavior` \| `Entity`. ⭐ **`Q-k` ruled these READ-ONLY for blueprints — a move, not a toggle** |
| ⭐ **Graph locals** | per-invocation, **legally shadow** an asset variable (`Q27-C1`) ⇒ ⛔ **deliberately OUTSIDE the cross-kind uniqueness rule.** ⚠ **This matters for `Q-E1`** |

---

## 4. ⚠ Corrections to the HSM doc's §1 — coordinator-checked, hand these over early

⭐ **The HSM session asked: *"if any line here is wrong, that correction alone is worth the round trip."***

| their # | status | note |
|---|---|---|
| 1 (role × scope) | ✅ **right, and now built** | but ⚠ **read-only for blueprints** (`Q-k`) — the editor **says so** rather than accepting and discarding |
| 9 (`{MethodFqn}@{offset}`) | ✅ **right, and wider than they think** | ⭐ **the blueprint side already emits the same convention:** `CSharpEmitter.cs:342` registers `"{Ns}.{Class}.BTreeTick@0"` — ⛔ **with the offset HARDCODED to 0** |
| 11 (*"BTree owns layout"*) | ✅ **confirmed in the code, and this is the crux of `Q-A`** | ⭐ **TWO projection formulas coexist today:** the standalone `AiPrimitiveEmitter.cs:277` uses `ref bb.BehaviorParameters[paramIndex * sizeof(Params)]` — **a stride-indexed slot** — while `BTreeBridgeEmitCore.cs:509` uses `ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint){offset})` — **a bin-packed byte offset.** ⚠ **`Q-A` is really: does HSM adopt the offset form, and does the stride form then have any remaining caller?** |
| 12 (`Marshal.OffsetOf` authoritative) | ⭐ **and now checkable** | `U-1`'s **Tier 1 records every emitted field's offset and size for all 42 assets** ⇒ **any layout drift is a red gate, not a discovery** |

⛔ **Do not take this table on trust either — it is the coordinator's reading, and three of its
predecessors were refuted by measurement in the last four batches.** ⭐ **Re-verify.**

---

## 5. The agenda — four topics, in this order

### 5.1 ⭐⭐ Answer the HSM session's `Q-A` first

**Everything else in their doc depends on it.** ⚠ **And §4 above suggests the answer is more
interesting than yes/no:** the offset-in-key convention exists on both sides already, but the
blueprint standalone path hardcodes `@0` and projects by **stride**, not offset.

### 5.2 The variable model across three hosts

📐 **The question the user actually asked: is there ONE model, or three that agree at the edges?**

| | |
|---|---|
| ⭐ **The blueprint side has just spent 9 batches proving one thing** | *a declaration that does not say which kind it is, is a defect* (`BP-226`) — and *two copies of an ordering that must agree is how it happened* |
| ⇒ **the same argument aimed across hosts** | ⛔ **if BTree, HSM and Blueprint each carry their own `Role`/`Scope`/uniqueness rules, that is three copies of an ordering that must agree** |
| ⚠ **But the hosts genuinely differ** | HSM has **orthogonal regions running concurrently** and **re-entry with history**; BTree has one leaf at a time; blueprints have **graph locals that legally shadow** |
| 📐 **So the real question is** | **where does the shared model STOP** — and is the boundary a *capability* (like `SupportsRoleScopeEditing`) or a *kind* (like `DeclarationKind`)? |

⭐ **Their `Q-B` / `Q-D` / `Q-E` are the concrete instances. `Q-E3` (reset-on-entry vs preserved) has
no blueprint analogue and is genuinely new.**

### 5.3 Calling a blueprint from a BTree node or an HSM slot

**What exists, measured:**

| | |
|---|---|
| `BTreeTick` | the standalone entry point, `AiPrimitiveEmitter.cs:267` |
| the per-node adapter | `BTreeBridgeEmitCore.cs` — ⭐ **BTree ignores the standalone and emits its own** |
| `CallablePeers` / `DeclaredCallablePeers` | blueprint→blueprint calls, resolved through the sibling catalog the generator builds from **every** `AdditionalFiles` entry |
| ⛔ **HSM** | `HSM-013` / `HSM-015` / `HSM-016` — **the equivalent path does not work.** ⚠ **Their `Q-G` asks whose lane that is** |

📐 **The design question: is the HSM adapter a third emitter, or does `BTreeBridgeEmitCore`'s shape
generalise?** ⚠ **Two emitters already disagree about how to project params. A third is the moment to
decide whether that is one mechanism or three.**

### 5.4 Events — the least settled, and say so

| blueprint | HSM |
|---|---|
| `EventDispatcherDecl` · `CustomEventDecl` — both carry `List<ParameterDecl>` | `HSM-009` open |
| `EventEntryNode` · `WaitForEventNode` | `ushort eventId` in the guard signature |

⭐ **Both sides have events with PARAMETERS**, which means the whole §5.2 question repeats for event
payloads. ⛔ **Do not design event authoring here.** ⭐ **Do establish whether an event parameter is a
`Parameter` in the unified sense or something else — because if it is, it inherits `Role`/`Scope`,
uniqueness and the type rail for free, and if it is not, say what it is instead.**

---

## 6. ⛔ How to work — `.claude/CLAUDE.md` is binding

| | |
|---|---|
| ⭐⭐ **Architect discipline** | **No non-trivial design ships without an architect pass.** The "architect" is the user's NotebookLM holding the engine design docs — ⛔ **you cannot reach it; the user relays.** ⇒ **draft `docs/blueprints/Architect_Question_N_*.md` mirroring the existing Q#2–Q#27 docs: decision-shaped sub-questions A/B/C/D, your recommended lean, and the reuse-vs-build tradeoff for each.** ⭐ **Prior architect answers repeatedly redirected the approach — this is load-bearing, not ceremony** |
| ⭐ **Ask in plain prose** | ⛔ **never the multiple-choice widget** |
| ⭐ **Docs: short** | lead with **visuals and terse tables**; ⛔ no prose walls. ⭐ **Hand-authored SVG for anything non-trivial** — Mermaid only for simple flowcharts, and keep its labels short |
| ⭐ **Allocate no ids** | ⛔ the tracker rows and diagnostic codes belong to whichever implementation session builds it. ⭐ **Describe findings; let them number** |
| ⭐ **Delegate thrift** | Opus for the model and the rulings; Sonnet for broad searches and mechanical reads |
| ⭐ **Round out, don't gold-plate** | build the whole obvious set when a mechanism is generic — ⚠ **but flag a speculative new vocabulary for a nod first** |

### 6.1 ⭐⭐ The one habit worth inheriting

**Every batch of the last nine found at least one thing by MEASURING what a document asserted.**
Four coordinator claims were refuted that way, each one that would otherwise have been built:

| | |
|---|---|
| *"rebase the WorkingState index"* | ⛔ **would have broken every shipped AiPrimitive** |
| *"the resolver seam already covers type existence"* | ⛔ **it covers methods; one bool cannot say which half failed** |
| *"the round-trip is the tag-must-not-reach-JSON gate"* | ⛔ **it cannot see a leaked tag at all — a written tag is read back too** |
| *"~34 semantic sites"* | ⛔ **135, and ~31 of them on a different type where the sweep would have moved `StructureHash`** |

⇒ ⭐⭐ **For every claim in the HSM doc, in the design docs, and in THIS file: check it against the
code before building on it.** ⛔ **A design that inherits a wrong premise inherits it silently.**

📌 **And the recurring gate defect, three times in three batches:** a test that passes because of
**what else happened to run**. ⭐ **When something is green, ask what else had to be true.**

---

## 7. First actions

```bash
git fetch origin claude/hsm-visual-editing-9ngei4 claude/blueprint-authoring-status-gm0akp
git show origin/claude/hsm-visual-editing-9ngei4:docs/blueprints/Hsm_Parameters_And_Variables_OPENING_PROMPT.md
```

1. ⭐ **Read the HSM opening prompt end to end.** It is well-made and specific; it deserves a specific answer.
2. ⭐⭐ **Verify §4 above against the code yourself** — then send the HSM session a **corrections-to-their-§1** reply. **That is the cheapest high-value output and it unblocks them.**
3. Read `RESUME_START_HERE.md` §7i–§7x for what the nine batches actually established.
4. 📐 **Then propose the agenda you think is right** — ⚠ **§5's ordering is the coordinator's guess, not a ruling.**

⚠ **Two live constraints on anything you propose:**

| | |
|---|---|
| ⛔ **The blueprint store flip (`U-12`) is IN FLIGHT** | ⭐ **anything touching `BlueprintAsset`'s storage must wait or be sequenced against it** |
| ⛔ **The visual check has not run for 14 batches** | ⇒ ⭐ **prefer designs whose acceptance is headless.** The blueprint programme has three tasks stalled purely because their deliverable is *"the panel draws it"* |
