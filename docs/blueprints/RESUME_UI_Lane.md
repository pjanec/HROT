<!--STATUS
state: LIVE
updated: 2026-08-23
current-answer: this whole file. It is the UI implementation lane's resumption document — written ahead of
  a compaction so the next window starts grounded. §0 is the MOST RECENT batch, §1–§2 are the BP-399 /
  Panel-observability history, §3 is the standing protocol, §4 is what is carried open.
stale-below: §1 and §2 are HISTORY as of 2026-08-23 — both landed. Read §0 first.
known-conflict: none.
-->
# ⭐⭐⭐ RESUME — **the UI / variable implementation lane**

> 🔒🔒 **Branch: `claude/reset-working-branch-qd1qpv`** *(re-pointed by the USER, `2026-08-23`)*. ⛔ Push
> nowhere else. ids **`BP-`**, tracker areas **`A`–`G`**.
> ⚠⚠ **This lane MOVED from `claude/hrot-implementation-j1jvin`** — ⛔ any document still naming `j1jvin`
> as this lane is stale; `.claude/CLAUDE.md`'s lane table *(`6b14d13fe`)* is authoritative.
> ⚠ **A third lane now exists:** `claude/blueprint-macro-feature-sdmspn` is the **BACKEND** lane
> *(ids `ST-`, tracker area `I` only)* — ⛔ the name is a historical reuse, not this lane.
> ⭐ **The coordinator is pushing to `claude/blueprint-authoring-status-6sr5ld`** — ⚠ CLAUDE.md's table
> still says `…-gm0akp`; **rule 7 syncs from wherever the live handoff is**, confirmed by ancestry.
> ⭐ **RELEARN** before acting on this file if the session is fresh or just compacted.

---

## 0. ✅ MOST RECENT — **the perspective model, Part A** is DONE *(`2026-08-23`)*

📄 **Design: [`../DESIGN_Perspective_Unification.md`](../DESIGN_Perspective_Unification.md) §3** — now
`BUILT`, with per-item **AS-BUILT** notes folded in *(obligation ⑤)*.
📄 **Handoff: [`batches/HANDOFF_Perspective_Model_Part_A.md`](batches/HANDOFF_Perspective_Model_Part_A.md)** ·
**Report: [`batches/REPORT_Perspective_Model_Part_A.md`](batches/REPORT_Perspective_Model_Part_A.md)**.

⭐ **`BP-488`–`BP-497`.** All items landed; ⛔ nothing descoped. ⭐ **Next free id: `BP-498`.**

### ⛔⛔ The four facts a later session must not re-derive

| ⭐ | |
|---|---|
| ⭐⭐⭐ **The editor's perspective id is `"Scenario"`** | ⛔ **not `"Editor"`** — `L6.1b` is DONE. ⚠ The **subsystem** is still named `"Editor"`, and so are its node/log names ⇒ 📌 **a perspective is not a subsystem name**, which was this batch's whole lesson |
| ⭐⭐⭐ **`SwitchPerspective` REFUSES an unclaimed perspective** | ⇒ ⛔ **a rail must REGISTER a claiming window BEFORE switching.** 📐 Four existing rails had the order backwards and passed only because no check existed |
| ⭐⭐ **CGF's perspective is `"Scenario"` too, and there is NO `CGF` perspective** | `perspectiveMap["Scenario"] = "CGF"` — the one entry whose key and value differ |
| ⭐⭐ **`FindResultsWindow`'s `owningPerspective` is REQUIRED**, and the scope is a parameter | ⛔ the `?? "Authoring"` default is gone, and the ctor refuses an anonymous `PerspectiveBound` window or a `Global` one that names a perspective |

⚠ **Two things left for the coordinator** *(§6 of the report)*: CLAUDE.md's coordinator-branch row looks
stale, and `Hrot.Presentation/Windows/FdpEntityInspectorHelper.cs` is on no lane's surface list although
`A1` had to touch it.

---

## 1. ✅ HISTORY — `BP-399` *("one shell")* is **DONE**

> ⚠ **This section and §2 are HISTORY as of `2026-08-23`.** ⭐ Read §0 for where the lane actually is.

📄 **The design: [`DESIGN_Details_Panel_View_Switching.md` §7](DESIGN_Details_Panel_View_Switching.md).**
📄 **The dispatch: [`batches/TASKS_One_Shell_BP399.md`](batches/TASKS_One_Shell_BP399.md).**

| # | what | state |
|---|---|---|
| **S0** | measure whether the Diagnostics/Blackboard `L3` rows were already satisfied | ✅ **yes, no code owed** |
| **S1** | Blueprint gets the real shell *(atomic; `BlueprintDetailsWindow` deleted)* | ✅ `BP-428`–`BP-430` |
| **S2** | `details.nodeproperties` on BTree + HSM at Rank 20 | ✅ `BP-431`–`BP-433` |
| **S2b** | the asset-scoped arms leave `InspectorWindow` **as menus, not views** | ✅ `BP-434`–`BP-437` |
| **S3** | `details.utility`, ported honestly as the stub it is | ✅ `BP-438` |
| **S4** | `details.parametersync`, Rank 15 | ✅ `BP-448` — `R-99` **satisfied**, not waived |
| **S5** | retire `InspectorWindow` | ✅ `BP-449`, `BP-450` — the class is **deleted** |

⛔ **`InspectorWindow` no longer exists.** All six arms are Details views or asset-row menu items.

### ⚠ Two corrections this lane made to its own claims — **do not re-introduce either**

| ⛔ the wrong claim | ⭐ the truth |
|---|---|
| *"`S5` is blocked on `S3` alone"* *(`S2b` report)* | **`BP-439`** — it was **`S4`**; §7.6's ④-before-⑤ order was right. ⚠ The **mirror error**: cleared one blocker, inferred the remainder instead of re-reading the sequence |
| *"`ai_inspector_*` is in no layout file"* *(`S5`)* | **`BP-450`** — ⛔ **FALSE.** My grep used `--include=*.cs`, excluding the very file types a layout lives in. `BP-103b`'s stale-layout rail caught it. ⚠ **An absence claim from grep is an absence in your PATTERN** |
| *"arms ① and ⑥ need a home in the Details panel"* *(`BP-431`)* | 🔒 The user routed all three **OUT**: collisions → Diagnostics, Rename…/Find References → the Asset Browser row menu, Go to Definition → **deleted**. §7.4a |

---

## 2. ⭐⭐⭐ THE NEXT TASK — **[`batches/HANDOFF_Panel_Observability.md`](batches/HANDOFF_Panel_Observability.md)**

> 🔒 **The user's instruction, `2026-08-22`:** *"then your task will be `HANDOFF_Panel_Observability.md`."*

⛔⛔ **READ THE DESIGN FIRST: [`../DESIGN_UI_Observability_Snapshot.md`](../DESIGN_UI_Observability_Snapshot.md)**,
whole — its **§UML** is the contract, and **§Invariant** *(the draw renders ONLY from the VM)* is the
load-bearing rule. ⭐ Umbrella context: [`../DESIGN_Headless_Testability.md`](../DESIGN_Headless_Testability.md).

| phase | what | how |
|---|---|---|
| **1 — `U-obs-1`** | `IPanelViewModel` + `PanelSnapshot` + the opt-in registry + **ONE pilot panel** end-to-end + a stable panel id | ⛔⛔ **HANDS-ON, do NOT fan out** — it is the pattern every later conversion mirrors. ⭐ Then **push a green checkpoint**: it unblocks the time lane's Group T |
| **2 — `U-obs-2+`** | the per-panel fan-out *(Details/blackboard/watch first, then the gizmo peer feed, then value-ordered)* | ⭐ **SONNET subagents**, Opus reviews the real diff and re-runs each panel's gates. ⛔ Review gate: **the INVARIANT** — any drawn value not from the VM is a defect |

⭐ **New tracker area `K` — Panel observability.** ⭐ Dispatch sha `5843055e7`; **scope frozen there.**
⭐ **Run freely — wait for nothing.**

### ⚠ Before writing code

1. **rule 7** — already done this session *(merged `2d95c419`)*; re-merge if the coordinator moves again.
2. **rule 1b** — push an empty `chore: started <batch> at <sha>` marker **immediately**, before any code.
3. **`U1a`'s open call:** the handoff *leans* to homing the contract in `Fdp.Diagnostics.Contracts`
   *(beside `DebugPrimitiveBuffer`)* — ⭐ **confirm the assembly by measurement and say so in the report.**

---

## 3. ⭐ THE STANDING PROTOCOL — **what this lane does every batch**

| | |
|---|---|
| ⭐⭐ **design before code** | `R-129`: intent is in `docs/` *(current)* and `.dev/<programme>/*-DESIGN.md` *(implemented)*, **never** in the code. Cite **doc + section** per item |
| ⭐⭐ **INVENTORY before design** | `search_graph` **first** — grep can only confirm a guess, never enumerate. Record the query + its `total` |
| ⭐⭐⭐ **revert-goes-red per item** | ⛔ un-apply with the **inverse edit**, ⛔⛔ **NEVER `git checkout --`** |
| ⭐ **tiers** | `T0` `scripts/quick-check.sh <csproj> [filter]` while working; the **full gate table ONCE, at the end** |
| ⭐⭐ **the gate report substitutes for the coordinator's run** | 8-row contract: per-gate command + counts + delta · a `--no-build` column · goldens as a **diff shape** · every red **confirmed pre-existing against the base sha** · clean tree · both quarantine counts · `tracker-counts.py --check` + ids allocated · the **integration suite** for a cross-cutting change *(or why it cannot gate)* |
| ⭐⭐⭐ **obligation ⑤** | a deviation goes **back into the owning DESIGN doc**, prior state marked SUPERSEDED — ⛔ the report is ephemeral, the design is not |
| ⭐ **I allocate the ids** | state them in the report. **Next free: `BP-498`** *(⚠ was `BP-453`; `BP-453`–`BP-497` are spent)* |
| ⛔ **no PR** unless the user asks | there has never been one in this programme |
| ⭐ **links for mobile** | `https://github.com/pjanec/HROT/blob/claude/reset-working-branch-qd1qpv/<path>` — ⚠ **push first** |
| ⛔ **plain-text questions** | never the multiple-choice widget |

### ⚠ Known pre-existing — **do not re-diagnose**

| | |
|---|---|
| `StructEdit.Tests` **1 red** | `DocumentBuilderTests.Build_CircularReference_…` — confirmed in a clean worktree at `5d1fd44d` |
| `Fdp.Presentation.Tests` | ⛔ cannot run whole *(`BP-419`, test-host crash)* — gate by `--filter` |
| `ClusterRunner.Integration.Tests` | ⛔ un-gateable, pre-existing DDS-allocator crash *(Batch 101)* |
| `Fdp.Toolkits.Tests` | ⚠ `DEBT-AIB-030` — the failing identity **rotates**; neither red nor green is evidence |
| `rulings-check.py` | ⚠ 1 staleness WARN on `.claude/CLAUDE.md` — pre-existing |
| ⭐ `design-digest.py --check` | ✅ **now fully green** — the coordinator's `8ad6d6aa` cleared the four long-standing failures |
| ⭐ `Hrot.ClusterRunner.Tests` **2 red** | `DataDrivenGizmoPredicateTests.D003_*` — `InvalidCastException` casting a test double to `DebugPrimitiveBuffer` at `DataDrivenGizmoSystem.cs:314`. ⛔ Confirmed pre-existing in a clean worktree at `c6f54318c` |
| ⚠ `Hrot.Editor.Tests` **1 FLAKY** | `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` — asserts an `AssemblyLoadContext` was GC-collected. 📐 Green 2 of 3 whole-suite runs, 3 of 3 filtered ⇒ ⛔ **neither colour is evidence** |
| ⚠ a merge that adds a project | **`dotnet restore` first** — `Hrot.SystemTests` arrived this way and `--no-restore` failed on it |

---

## 4. ⭐ CARRIED OPEN

### ⭐ `S4` / `BP-399`'s tail — **both blockers are now CLOSED; one design call remains**

📄 **[`Architect_Question_49_…`](Architect_Question_49_Subtree_Sync_Identity_Survives_Reload.md)** —
written by the coordinator, **amended by this lane `2026-08-22`**:

| | |
|---|---|
| ✅ **`Q49` option C is BUILT** *(`BP-440`–`BP-442`)* | the identity is recomputed from the catalog resolver already wired at `PerspectiveWorkspaceRegistrar:289`, through ONE derivation *(`SubtreeSyncIdentity`)*, pulled from inside `Emit` so no path can forget *(`R-126`)* |
| ✅ **`Q50` option A + `Q49` option D are BUILT** *(`BP-444`–`BP-447`)* | 🔒 user: *"i hoped the editor automatically adds the subtree's data."* ⭐⭐ **A and D turned out to be the SAME change**: every input is persisted, so it is a **generator-side projection over a document** — no editor, no ordering. `SubtreeSyncProjection` does one walk yielding both the groups and the slice fields, so *"a group without its field"* is unrepresentable |
| ⛔⛔⛔ **RE-MEASURED `2026-08-22` — `BP-446`'s LIMIT WAS DESCRIBED WRONG. 📄 Read [`Q50`](Architect_Question_50_The_Master_Blackboard_Declares_The_Subtree_Slice.md) *"THE LIMIT — re-measured"* BEFORE reasoning about this area** | ⛔ *(was: "a generated Category-2 callee blackboard does not exist in the master's compilation")* — 📐 **all 15 managed assets declare `BrainBlackboard`, an ordinary resolvable type; that skip never fires.** ⭐⭐ **The real wall is the BYTE BUDGET** — 128 bytes vs a 100-byte inline budget ⇒ **"declare the slice as a field" can never hold a Category-2 callee.** ⚠ Architectural, ⛔ not a missing helper |
| 🔴 **THE REACH — the honest state of `S4`** | the panel can only author against a **Category-2** callee *(it needs `BlackboardVariables`)*, and the generator skips **every** Category-2 callee ⇒ ⛔ **the authorable and emittable sets are DISJOINT today.** ⭐ The panel is real and writes real persisted data; ⛔ no authorable binding reaches the runtime yet |
| ✅ **POSTPONED BY THE USER, ON THE RECORD** *(`BP-452`)* | 🔒 *"is that safely postponable, providing you record it thoroughly as such?"* ⇒ ⭐ **yes**: every failure is a **build-time skip**, never a partial emit or a bad runtime copy; 📐 **no corpus asset has a sync binding.** ⭐ Three routes with a lean *(`C′` — declare the slice `Role = State`, reusing the partition tier that already escapes the budget)* — ⚠ it moves the emitted body, so it wants a nod |
| ⛔ **ONE REAL DEFECT, not postponable indefinitely** *(`BP-451`)* | **nothing validates a binding's `FieldName` against the callee's type** ⇒ the generator **can emit CS1061** — 📌 `BP-306` re-armed. ⭐ Unreachable through the UI, reachable by hand-edited JSON. 🛠 Small, needs no design call ⇒ **do it in the next batch touching this generator** |
| ⚠ also required | the **master** blackboard must be `Managed`; a Category-1 master cannot gain a field *(the one claim of `BP-446` that survived)* |
| ⚠ still awaiting a nod | `Q49`'s open sub-question — a **MISSING** subtree at load. ⭐ Recommended: a diagnostic row. Built behaviour: identity left alone, never erased |

### ⚠ Other open `BP-` rows

`BP-405` · `BP-407` · `BP-411` · `BP-416` · `BP-418` · `BP-419` · `BP-426` *(needs a running editor)* ·
`BP-427` · `BP-342` · `BP-399` · ⭐ **`BP-451`** *(a real defect — see above)* · ⭐ **`BP-452`**
*(postponed on the record)*. ⭐ Tracker: **open 92 / done 295**.

### ⭐ The other lanes — **do not touch their files**

| lane | branch | owns |
|---|---|---|
| **coordinator** | ⚠ **`claude/blueprint-authoring-status-6sr5ld`** is where the live handoffs and designs are being pushed *(`2026-08-23`)*; CLAUDE.md's table still says `…-gm0akp` — ⭐ **confirm by ancestry, not by name** | handoffs, designs, the ledger |
| ⭐ **backend** *(new `2026-08-23`)* | `claude/blueprint-macro-feature-sdmspn` | project/reference structure · the Stride cleanup. ids **`ST-`**, tracker **Area I only**. ⚠ **Our only shared file is `Program.cs`** |
| **time / MCP** | see `RESUME_Time_Stride_Session.md` | `Fdp.Toolkits/Time/` · `Hrot.Orchestrator` · `ModuleHostKernel` · the MCP harness. ids **`TM-`**, tracker **Area H only** |

⛔ **A cross-lane edit is a STOP-and-report, not a judgement call.**
