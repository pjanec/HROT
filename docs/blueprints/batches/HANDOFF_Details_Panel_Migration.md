<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-21
current-answer: this is a DISPATCH POINTER, not a design. The design is
  docs/blueprints/DESIGN_Details_Panel_View_Switching.md (READY-TO-BUILD) — build from it.
-->
# HANDOFF — **Details Panel migration** *(autonomous, UI/variable lane)*

> 📌 **Dispatched at `e2c1348a5`.** ⭐ Branch from it *(rule 7)*. ⛔ **Scope FROZEN at this sha.**
> ⭐ **Lane: UI / variable** *(`claude/hrot-implementation-j1jvin`)* — ids **`BP-`**, tracker areas `A`–`G`.
> ⭐ **Rule 1b: push `chore: started details L0 at e2c1348a5` FIRST.** ⭐ **Rule 3: you allocate the ids.**

## 0. ⭐⭐⭐ AUTONOMOUS — **build the design's ladder, layer by layer, without waiting on me**

📄 **THE DESIGN:** [`DESIGN_Details_Panel_View_Switching.md`](../DESIGN_Details_Panel_View_Switching.md) —
`build-state: READY-TO-BUILD`, and it carries **everything you need**: §1 placement · §2 the
**classDiagram** · §2b the **sequenceDiagrams** · §3 ruling→mechanism · §6 **the task ladder**.
⛔ **This handoff does NOT redraw any of that** *(diagrams live in the design — that is the rule)* — ⭐ **build
§6's layers straight from the design.**

⭐⭐ **You own the whole ladder.** Work **one layer per batch**, in order, each **independently shippable
and independently railable** *(the design says so)*. ⛔ **You do NOT need a new handoff from me per
layer** — proceed autonomously, exactly as the time lane runs its T-tasks.

| layer *(from §6)* | note |
|---|---|
| **`L0`** context *(no UI change)* — `L0.1`–`L0.4` | ⚠ **`L0.2` is the highest-risk task** — the bridge REPORTS, never filters *(`R-118`)*; `L0.4` deletes two entity-selection copies, reads `SelectionState` from the World *(`R-122`)* |
| **`L1`** the registry *(no UI change)* | descriptor + instance + registry, registered through the existing claim chain |
| **`L2`** the shell *(first visible layer)* | |
| **`L3`** migrate the views *(all parallel — the delegation layer)* | |
| **`L4`** float and pin | ⚠ **needs `L1`, not `L2`** |
| **`L5`** retire *(per item, after its replacement is live)* | ⛔ retire each surface only once its replacement ships |

## 1. ⭐⭐ CHECK THE UML, REPORT THE MATCH *(obligation ③)*

⭐ Before building each layer, **read §2's classDiagram and §2b's sequenceDiagrams**, and report in your
batch report: *"the design carries N classes / M sequences for this layer; what I built matches / deviates
HERE and why."* ⚠ **A deviation is a finding, argued — not a silent choice.**

## 2. ⛔⛔ WHAT IS **NOT** YOURS

| ⛔ | |
|---|---|
| **the WATCH / staged-write story** *(`W0`–`W5`, the yellow-staged display, `R-130`)* | ⛔ **the coordinator owns it** — do NOT touch the staged-write path, `VariableChangeMonitor`'s pending arm, or `MIN`'s write. A separate watch design + dispatch is coming |
| **any time-lane file** | ⛔ `Fdp.Toolkits/Time/`, `Hrot.Orchestrator`, `ModuleHostKernel`, the integration tests — a cross-lane edit is a **STOP-and-report** *(`R-128`)* |

## 3. ⭐ PER-LAYER GATES & PROTOCOL

⭐ Each batch reports the standing gate contract *(the report SUBSTITUTES for my re-run — rule 8)*:
one row per gate · verbatim command · pass/fail/skip · delta vs baseline · goldens as a diff shape ·
every RED confirmed pre-existing against the base sha · `tracker-counts.py --check` · **the `BP-` ids you
allocated**. ⭐ **`R-106` verdicts per item:** ✅ done · 🛑 blocked *(by what)* · ⚠ partial *(what's
missing)* · ⛔ not started *(why)*. ⭐ **Rule 4: pull the coordinator branch before each batch's final
commit**; ⭐ **rule 7: re-sync from it at the start of each batch.**
