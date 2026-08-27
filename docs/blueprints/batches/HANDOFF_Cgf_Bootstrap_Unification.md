<!--STATUS
state: LIVE
build-state: FRAME — UI/CGF lane. AQ62 RESOLVED: unify the two host composition roots into ONE shared,
  parameterized composition, ending the ~85% shared-but-independently-wired drift class. Coordinator gives the
  FRAME + the staging; the SESSION authors the detailed design (inventory + UML per stage) and builds staged.
  ⭐⭐⭐ The composition-parity RAIL is stage 0 and a hard prerequisite (user ruling).
updated: 2026-08-27
current-answer: this handoff is the FRAME. Decision + inventory: Architect_Question_62 (RESOLVED). The (b)/(c)
  split is measured there. The session designs each stage before building it.
known-conflict: the biggest refactor in the programme — both host composition roots + the IEditorLogic/
  EditorApplication god-facade. ⛔ Editor-byte-identical is the gate at EVERY stage. STOP-and-report if a stage
  threatens the ruled (c) divergence (network/authority — Q26/ruling 22).
-->
# FRAME-HANDOFF — **Unify the composition root** *(AQ62 — UI/CGF lane, `CE-`)*

> 📌 **Dispatched at `aac21bc27`.** ⭐ Rule 7 *(re-sync from `claude/blueprint-authoring-status-6sr5ld` first)*; **rule 1b started-marker before code.** ⛔ No PR.
> ⭐ Continue `CE-` ids; you allocate them *(rule 5)*.
> ⭐⭐⭐ **FRAME, not a full design.** Per the WHO-DESIGNS amendment: **YOU author the staging design (`DESIGN_Cgf_Bootstrap_Unification.md`), the inventory, and the class/sequence UML** — per stage, before building it. The coordinator verifies design+UML on return.
> 🎯 **The goal (AQ62):** ~85% of `EditorSubsystem`/`CgfSubsystem` is the SAME shared pieces wired TWICE, independently → drift → the "CGF forgot to wire X" bug class (E2/E3/E4/corrective, and the 6 the user found by eye). Collapse the (b) mass into **ONE shared composition both hosts call**, parameterized by the small genuinely-host-different (c) core. Then parity is **by construction**.

## 1. ⛔⛔ STAGE 0 — the composition-parity RAIL, FIRST *(hard prerequisite — user)*
🔒 **User: "for a refactor like this that rail is absolute must."** Build it BEFORE touching the roots.
- ⭐⭐ **What it is (NOT the existing conformance rails):** the conformance rails compare OUTPUT *(same panel models/menu-sets when driven)* — they were GREEN while CGF was broken. This rail compares **WIRING**: for each shared **(b)** piece, assert it is **composed/wired on BOTH hosts' constructed subsystem** *(assert on the CONSTRUCTED OBJECT — CLAUDE.md's silent-default control)*.
- ⭐ **Seed it from AQ62's (b) inventory + the known drift instances** — create-core, `MutationInterceptor` non-null, scenario-catalog non-empty, perspective toolbar section present, `PerspectiveIconKeys` registered *(CE-058)*, `BlueprintDebugSession` constructed *(CE-059)*, scenario-root resolved to `{staging}/shared/scenarios` *(CE-057)*, center/rotate routed through the shared systems — and grow it to the full (b) list. ⚠ **Proof it has teeth: reverting any one known fix must turn it RED.**
- ⭐⭐ **TWO MORE user-observed `--mode cgf` symptoms `2026-08-27`, almost certainly the same class — investigate their composition root and seed the rail:** ① **the 2D map shows NO entities on some scenarios** *(e.g. hill-attack loads, map is empty of entities)* — a data/render path the editor wires and CGF likely does not; ② **center-on-entity CRASHES the app** *(this is the E3 center/rotate path — the frame already flags "center/rotate routed through the shared systems"; a crash means CGF is missing something that path resolves, not just drift)*. ⛔ These are **not** the freeze/pinning — those **no longer reproduce** *(CE-055/CE-056 confirmed by the user on a windowed box `2026-08-27`)* and are **out of this frame's scope**. ⚠ Each of ①/② must become a rail assertion that reddens on the pre-fix root.
- ⛔ The imperative wiring in two 5k-line methods makes enumeration awkward — that awkwardness is *expected* and is exactly why the shared root (below) is the cure; the rail is the scaffold that makes the cure safe. Design the most tractable form *(per-piece presence assertions on the two built subsystems, headless where possible / `ui-probe` where a piece is windowed)*.

## 2. ⭐⭐ THE STAGED UNIFICATION *(you design each stage's detail + UML; editor byte-identical is the gate throughout)*
| stage | goal | the one thing not to get wrong |
|---|---|---|
| **1 — god-facade seam** | Extract `IEditorLogic`/`EditorApplication` (and what the editor root needs) into `Hrot.Editor.AiShared` so composition can move **behind the assembly wall** *(`Hrot.CGF` cannot ref `Hrot.Editor`)* | ⚠ measured "mostly host-agnostic (world is a parameter)" — but this is the hard blocker; scope it as its own design+UML. ⛔ do not drag the (c) core in |
| **2 — shared `ComposeEditorExperience(deps)`** | Move the (b) composition into ONE shared method/module in AiShared; **the EDITOR delegates to it, byte-identical** | ⛔⛔ **editor byte-identical is THE gate** — the parity rail + the existing suites prove the editor didn't change |
| **3 — CGF calls it** | CGF invokes the same shared composition, **deleting its parallel wiring** *(the ~85%)*; passes only the (c) deps | ⛔ CGF's hand-rolled parallels DIE *(ruling 9)*; the parity rail now holds by construction |
| **4 — (optional, evaluate) `SharedApplicationBootstrapper`** | fold the (c) NODE-bootstrap mechanics onto the 7-phase base SimHost/IG/Stride already share | ⚠ Q62-B — evaluate, not required; only if it simplifies the (c) core without risking the ruled divergence |

⛔ **The (c) core stays per-host and MUST be preserved exactly:** world/kernel construction, DDS participant/network, time authority *(master vs slave)*, **networkless-vs-networked handler binding**, unowned-write authority, orchestrator role, in-process kernel-mode. ⚠ **A stage that would erase or blur any of these is a STOP-and-report** *(R-106)* — that is the ruled divergence (Q26/ruling 22), not drift.

## 3. ⭐ DESIGN BASIS + AUTONOMY
📄 **[`Architect_Question_62`](../Architect_Question_62_Unify_The_Composition_Root.md)** *(RESOLVED — the (b)/(c) split + the decision)* · the batch investigation's side-by-side composition inventory · `PROGRAMME_Cgf_Equals_Editor_Gap_Map.md` §2c *(E1–E5, the assembly wall §2c.1)* · ruling 66 *(one-node cluster)* · `SharedApplicationBootstrapper` *(the existing 7-phase base — the proven shared pattern)* · the E1–E4 designs *(the pieces already extracted — the shared root composes THEM)*. Codebase-memory CLI, ⛔ not grep-only. Build affected projects only; build once then `--no-build`.

## 4. ⭐ ACCEPTANCE + PROCESS
- **Stage 0 rail** exists and reddens on a reverted known gap *(inverse-edit proof)*.
- Each unification stage: **editor byte-identical** *(the gate)*; the parity rail stays green; the (c) core preserved; affected builds; a tiny `--mode cgf` eyes/`ui-probe` re-pass at the end *(R-21 lifted, scoped small)*.
- ⭐⭐ **Process (frame-delegation, per stage):** ① author/extend `DESIGN_Cgf_Bootstrap_Unification.md` with the stage's inventory + class/sequence UML · ② build · ③ fold as-built *(obligation ⑤)* · ④ report → design + DECISION LOG + `CE-` ids + gates. ⛔ **Do NOT big-bang** — one stage per batch is fine; stop the batch cleanly at a stage boundary and report. ⚠ This is large — expect multiple batches; the frame authorizes the DIRECTION, you scope the batch sizes.
