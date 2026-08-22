<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: this whole file. It is the UI/variable implementation lane's resumption document —
  written ahead of a compaction so the next window starts grounded. §1 is where we are, §2 is the NEXT
  TASK, §3 is the standing protocol, §4 is what is carried open.
stale-below: nothing.
known-conflict: none.
-->
# ⭐⭐⭐ RESUME — **the UI / variable implementation lane**

> 🔒 **Branch: `claude/hrot-implementation-j1jvin`.** ⛔ Push nowhere else. ids **`BP-`**.
> ⭐ **RELEARN** before acting on this file if the session is fresh or just compacted.

---

## 1. ⭐ WHERE WE ARE — `BP-399` *("one shell")* is **4 of 5 done**

📄 **The design: [`DESIGN_Details_Panel_View_Switching.md` §7](DESIGN_Details_Panel_View_Switching.md).**
📄 **The dispatch: [`batches/TASKS_One_Shell_BP399.md`](batches/TASKS_One_Shell_BP399.md).**

| # | what | state |
|---|---|---|
| **S0** | measure whether the Diagnostics/Blackboard `L3` rows were already satisfied | ✅ **yes, no code owed** |
| **S1** | Blueprint gets the real shell *(atomic; `BlueprintDetailsWindow` deleted)* | ✅ `BP-428`–`BP-430` |
| **S2** | `details.nodeproperties` on BTree + HSM at Rank 20 | ✅ `BP-431`–`BP-433` |
| **S2b** | the asset-scoped arms leave `InspectorWindow` **as menus, not views** | ✅ `BP-434`–`BP-437` |
| **S3** | `details.utility`, ported honestly as the stub it is | ✅ `BP-438` |
| **S4** | `details.parametersync` | ⛔ **deferred by design** *(`R-99`)* — see §4 |
| **S5** | retire `InspectorWindow` | ⛔ **blocked on `S4`** *(`BP-439`)* |

⭐ `InspectorWindow` is **301 lines with exactly ONE arm left** — `PARAMETER SYNCHRONIZATION`.

### ⚠ Two corrections this lane made to its own claims — **do not re-introduce either**

| ⛔ the wrong claim | ⭐ the truth |
|---|---|
| *"`S5` is blocked on `S3` alone"* *(`S2b` report)* | **`BP-439`** — blocked on **`S4`**; §7.6's ④-before-⑤ order was right. ⚠ The mechanism was the **mirror error**: cleared one blocker, inferred the remainder instead of re-reading the sequence |
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
| ⭐ **I allocate the ids** | state them in the report. **Next free: `BP-444`** |
| ⛔ **no PR** unless the user asks | there has never been one in this programme |
| ⭐ **links for mobile** | `https://github.com/pjanec/HROT/blob/claude/hrot-implementation-j1jvin/<path>` — ⚠ **push first** |
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
| ⚠ a merge that adds a project | **`dotnet restore` first** — `Hrot.SystemTests` arrived this way and `--no-restore` failed on it |

---

## 4. ⭐ CARRIED OPEN

### ⛔⛔ `S4` / `BP-399`'s tail — **two blockers, not one**

📄 **[`Architect_Question_49_…`](Architect_Question_49_Subtree_Sync_Identity_Survives_Reload.md)** —
written by the coordinator, **amended by this lane `2026-08-22`**:

| | |
|---|---|
| ✅ **option C is BUILT** *(`2026-08-22`, `BP-440`–`BP-442`)* | user approved; the identity is recomputed from the catalog resolver already wired at `PerspectiveWorkspaceRegistrar:289`, through ONE derivation *(`SubtreeSyncIdentity`, in `netstandard2.0` persistence — reachable from generator, AiShared **and** BTree.Editor)*, pulled from inside `Emit` so no path can forget *(`R-126`)*. 11 rails, revert-probed |
| ⛔ **option D is DESIGNED and GATED** | the generator's load path is the `*.btree.json` `AdditionalTexts` it **already receives** — a second projection, not new plumbing. ⛔ **NOT wired**: it would emit `ref master.{Subtree}_{DtoType}` for a field nothing declares ⇒ non-compiling generated code the moment a designer makes a sync binding *(`BP-306`'s shape)* |
| 🛑 **`S4` is blocked on ONE ruling — [`Q50`](Architect_Question_50_The_Master_Blackboard_Declares_The_Subtree_Slice.md)** *(`BP-443`)* | *does the master blackboard DECLARE the auto-allocated slice, and who SIZES it?* ⭐ **Recommended: A (declare it)** — its two deferral reasons have **expired**: *"needs catalog integration"* ⇒ `Q49` delivered it, and *"size unknown until build"* is false in the generator *(`StructSizeResolver.Resolve`)*. 📐 **Zero corpus assets have a sync binding ⇒ byte-identical today.** ⇒ **`Q50` → D → `S4` → `S5` → `BP-399` closes** |
| ⚠ also awaiting a nod | `Q49`'s one open sub-question — what happens when a subtree asset is **MISSING** at load. ⭐ Recommended: a **diagnostic row**. ⭐ Built behaviour today: the identity is **left alone, never erased** |

### ⚠ Other open `BP-` rows

`BP-405` · `BP-407` · `BP-411` · `BP-416` · `BP-418` · `BP-419` · `BP-426` *(needs a running editor)* ·
`BP-427` · `BP-342` · `BP-399`. ⭐ Tracker: **open 92 / done 286**.

### ⭐ The other lanes — **do not touch their files**

| lane | branch | owns |
|---|---|---|
| **coordinator** | `claude/blueprint-authoring-status-gm0akp` | handoffs, designs, the ledger |
| **time / MCP** | see `RESUME_Time_Stride_Session.md` | `Fdp.Toolkits/Time/` · `Hrot.Orchestrator` · `ModuleHostKernel` · the MCP harness. ids **`TM-`**, tracker **Area H only** |

⛔ **A cross-lane edit is a STOP-and-report, not a judgement call.**
