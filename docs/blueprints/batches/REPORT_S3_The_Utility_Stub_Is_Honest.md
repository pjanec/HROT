<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: this is a BATCH REPORT (ephemeral). The durable record is
  DESIGN_Details_Panel_View_Switching.md §7.4b (the as-built) and §7.4's S5-blocker correction,
  plus tracker rows BP-438 / BP-439. Quote THOSE, not this.
stale-below: nothing.
-->
# ⭐ `S3` — **`details.utility`, ported honestly, and the arm had never drawn**

📄 **Design basis:** [`DESIGN_Details_Panel_View_Switching.md` §7.6 ③](../DESIGN_Details_Panel_View_Switching.md),
verbatim: *"port it honestly as a stub, ⛔ do not pretend it is a feature."* As-built folded into **§7.4b**.

**Base sha:** `81499350` *(the rule-1b started-marker)*. **IDs allocated:** `BP-438`, `BP-439`.

---

## 1. ⛔⛔ The finding that came before the code

📐 **The enumeration ran first** *(the `INVENTORY` obligation, graph before grep)*:

| query | total | what it says |
|---|---:|---|
| `search_graph(name_pattern=".*UtilityConsideration.*")` | **7** | 1 runtime struct, 1 selection record, 1 test, 4 doc sections |
| repo-wide grep for `UtilityConsiderationSelection` | **2 C# sites** | its own record, and the `if` this batch replaces |
| `search_graph(name_pattern=".*Utility.*", file_pattern="Hrot/.*")` | **0** | ⛔ **there is no utility-AI editor surface in this repo at all** |

⇒ ⭐⭐⭐ **Nothing raises the selection, so the arm had never drawn — not once, since it was written.**
⚠ The design called it *"a stub"*; it was a stub **and unreachable**, which is a stronger claim and
changes what the rails can honestly assert.

### ⭐ Why it was PORTED and not DELETED

📌 The `2026-08-15` rule — *"unreferenced is not unintentional; search `docs/` first"* — and the corpus
answers directly, in three places:

| where | what it says |
|---|---|
| `docs/designs/utility-ai/Utility_AI_Design_v1_1.md` | a **LIVE** architecture document for the whole layer |
| `.dev/_DONE/utility-ai/Utility_AI_Editor_Wireframes.md` | specifies the **two-pane option × consideration host** this arm belongs to |
| `.dev/_DONE/utility-ai/batches/BATCH-14-INSTRUCTIONS.md` §1d | *"Add `UtilityConsiderationSelection` + inspector dispatch arm"* — added **deliberately, ahead of its producer** |

⇒ **DORMANT**, not **DEAD**: it overwrites nothing and harms nothing. 📌 The two-property distinction —
*unreachable* and *dangerous* are different, and `W3`'s counter-stubs were both.

---

## 2. ⭐ What was built

- `Shell/UtilityConsiderationDetailsView` + `…Descriptor` — `details.utility`, **Rank 20**.
- Registered **UNGATED** by `PerspectiveWorkspaceRegistrar`. ⭐ Its predicate is the *selection's
  existence*, which is a **sharper** statement than any host-kind gate — a gate would be a second,
  weaker copy of the same rule. ⚠ No `R-117` risk: it cannot claim the panel while nothing raises the
  selection.
- ⭐⭐ **It says it is not built, and names the design.** ⛔ The retired arm drew a heading and an index
  pair and stopped, which reads as *"loading"* — and it cited **`P5-02`**, ⚠ **a phase id that does not
  exist in the utility-AI record** *(the corpus's only `P5-02` hits belong to `group-maneuvers`)*.
- `InspectorWindow` **302 → 301** lines, and now has **exactly one arm**.

### ⭐⭐ The load-bearing rail is REGISTRATION, not behaviour

⛔ Every behavioural rail on this view describes a case production cannot reach today. ⇒ the assertion
that carries weight is **the real `EditorSubsystem` registers it** — otherwise the port is a class nobody
constructs, which is `BP-327`'s shape three times over.

---

## 3. ⛔⛔ `BP-439` — **correcting my own claim from the `S2b` report**

📌 The `S2b` report said *"`S5` is now blocked on `S3` alone."* ⛔ **Wrong.**
📐 Measured after `S3`: `InspectorWindow` has **one arm left — `PARAMETER SYNCHRONIZATION`** — and §7.6 ④
**defers it by design** *(`R-99`, after the orchestrator wiring)*. §7.7's own row already said it:
*"Only **parameter sync** is genuinely sequenced."*

⚠⚠ **The mechanism is the MIRROR ERROR** *(`2026-08-17`)*: I reasoned from *"the two arms `BP-431` named
now have homes"* to *"therefore nothing blocks `S5`"* — **without re-reading the ordering table that
lists the other blocker.** ⇒ ⭐ **the fix is one line: after clearing a blocker, re-read the design's own
sequence rather than inferring the remainder.** Folded into §7.4, §7.6 ⑤ and the task table.

⇒ ⛔ **`S5` is NOT attempted this batch.** 📌 `R-106`: that stops `S5`, not the batch — `S3` shipped in full.

---

## 4. ⭐ Revert probe

| probe | expected | measured |
|---|---|---|
| remove `DetailsViews.Add(UtilityConsiderationDetailsViewDescriptor.For())` | the registration rails only | ✅ **3 red** in `TheAiOfferSetsAreUnchangedTests` *(one per perspective)* **+ 3 red** in `EveryPerspective_OffersTheUtilityConsiderationView`; ⭐ the 9 behaviour rails stayed **green**, correctly — they test the descriptor, not the wiring |

⛔ Un-applied with the **inverse edit**, never `git checkout --`.

---

## 5. ⭐ Gates *(the rule-8 contract)*

| # | gate | command | result | Δ vs `81499350` |
|---|---|---|---|---|
| 1 | solution build | `dotnet build IOS-IG-SimHost.sln --no-restore` | ✅ **0 errors** | — |
| 2 | AiShared | `dotnet test … --no-build` | ✅ **1913 / 0 / 1 skip** | **+9** *(the behaviour rails)* |
| 3 | Blueprints | `dotnet test … --no-build` | ✅ **3911 / 0 / 18 skip** | **+3** *(the composition-root rail)* |
| 4 | Hrot.Editor | `dotnet test … --no-build` | ✅ **214 / 0** | 0 |
| 5 | BTree.Editor | `dotnet test … --no-build` | ✅ **622 / 0** | 0 |
| 6 | Hsm.Editor | `dotnet test … --no-build` | ✅ **555 / 0** | 0 |
| 7 | Smoke | `dotnet test … --no-build` | ✅ **4 / 0** | 0 |
| 8 | Fdp.Presentation *(filtered — `BP-419`)* | `--filter "…~Windows\|…~Docking"` | ✅ **11 / 0** | 0 |
| 9 | StructEdit | `dotnet test … --no-build` | ⚠ **191 / 1** | ⛔ **PRE-EXISTING** *(confirmed in a clean worktree at `5d1fd44d` last batch; zero StructEdit files touched since)* |
| 10 | tracker | `tracker-counts.py --check` | ✅ open 91 / done 283 | +2 rows |
| 11 | rulings | `rulings-check.py` | ✅ **22/22** | ⚠ 1 staleness WARN on `.claude/CLAUDE.md` *(pre-existing)* |
| 12 | design docs | `design-digest.py --check` | ✅ **ALL PASS** | ⭐ **the 4 long-standing failures are GONE** — the coordinator's `8ad6d6aa` fixed them and this run's rule-7 merge brought them in |

**`--no-build` column:** gates 2–9 ran `--no-build` **after** gate 1 built the whole solution in the same
tree; all are **in-solution**, so none reports a stale bin.
**Quarantine:** 1 skip in AiShared, 18 in Blueprints — **unchanged**; ⛔ no new skips.
**Working tree clean after every suite run**; **no golden files moved** *(this diff touches no emitter)*.

**Row 8 of the contract — the integration suite.** ⚠ **Not applicable, stated rather than omitted:**
editor-UI only, no clock / kernel schedule / orchestrator / transport / cross-node code. The
`ClusterRunner.Integration.Tests` DDS-allocator crash remains the pre-existing un-gateable finding
Batch 101 filed.

**Merge note.** This run began with the rule-7 merge of `claude/blueprint-authoring-status-gm0akp`. One
conflict, in `ScenarioMenuTests.cs` — **both lanes had independently fixed the same red** *(five scenario
commands vs six after curated-scenarios)*. ⭐ Resolved to the coordinator's countless name
`AllCommands_Registered_InCommandSet`, which is what that test's own doc-comment argues for.

---

## 6. ⭐ Obligation ③ — **design diagrams vs what I built**

§7.4's `classDiagram` and §7.5's `sequenceDiagram` describe the **node-properties** extraction, untouched
here. ⭐ `S3` adds one view alongside them with no new collaborator — no diagram change is owed.
⚠ **The deviation is factual, not structural:** the design said *"a stub"*; it was **a stub that could
never draw**. §7.4b records that, and §7.6 ③'s verdict cell now says so.

---

## 7. ⭐ Next

| | |
|---|---|
| **`S4`** | `details.parametersync` — ⛔ **deferred by design** *(`R-99`, after the orchestrator wiring)*. **This is now the only thing standing between `BP-399` and done** |
| **`S5`** | retire `InspectorWindow` + drop `ai_inspector_*` from the shipped default layout — ⛔ **blocked on `S4`** *(`BP-439`)* |
