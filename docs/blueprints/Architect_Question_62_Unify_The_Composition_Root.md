<!--STATUS
state: LIVE
build-state: DESIGN — architect question (large blast radius, resolved WITH the user per the WHO-DESIGNS
  amendment). Not buildable until answered; on approval it graduates to a staged design + handoffs.
updated: 2026-08-27
current-answer: §3 the decision sub-questions with Claude's lean. §2 = the measured INVENTORY that frames it
  (~85% of the two composition roots is shared-but-independently-wired — the drift/bug class).
design-basis: ruling 66 ("the editor is a one-node cluster") · PROGRAMME_Cgf_Equals_Editor_Gap_Map.md §2c
  (the editor→shared extraction, E1–E5) · Architect_Question_60 §4b/R1 (whole-editor→shared) ·
  SharedApplicationBootstrapper (the 7-phase base SimHost/IG/Stride already share) · the E2/E3/E4/corrective
  reports (five measured drift instances) · ruling 22 / Q26 (the RULED network/authority divergence — the (c) core).
known-conflict: the biggest structural change in the programme — both host composition roots + the
  IEditorLogic/EditorApplication god-facade. Every lane touches downstream. ⇒ resolve WITH the user before any build.
-->
# Architect Question 62 — **Unify the composition root** *(the structural fix for the CGF↔editor drift class)*

> 🎯 **The user's observation, `2026-08-27`:** the six `--mode cgf` visual-check defects show *"the subsystem
> bootstrap/composition is unified far less than it could be — my naive hope was cgf==editor means CGF shares
> almost all bootstrap and only the network differs."* ⭐ **Measured: that hope is right, and it is already the
> documented intent.** This AQ decides the STRATEGY to reach it.

## 1. ⭐⭐⭐ THE FINDING — one root cause behind five batches
📐 Measured `2026-08-27`. Every recent defect is the SAME shape — **CGF's composition root forgot to wire what
the editor's did**: E2 *(drifted create-core)* · E3 *(hand-rolled center/rotate)* · E4 *(`MutationInterceptor`
never wired)* · corrective *(zero scenario-catalog entries · no perspective toolbar section)*. ⇒ ⭐⭐ **not five
bugs — one root: two divergent, hand-rolled composition roots** *(`EditorSubsystem` ~4.2k ln · `CgfSubsystem`
~1.6k ln)* independently wiring overlapping sets of the SAME shared pieces. Each Axis-C increment collapses ONE
drift site; between increments, drift ships and is caught by EYES *(exactly the 6 the user found)*.

🔴 **And there is already a shared bootstrap the OTHER nodes use** — `SharedApplicationBootstrapper` *(the sealed
7-phase base **SimHost + IG + Stride** share)*. ⛔ **Editor and CGF are the only two hosts that hand-roll their
own root and bypass it.** So the two that drift are precisely the two bespoke ones.

## 2. ⭐⭐ INVENTORY — the (a)/(b)/(c) split *(measured; full table in the batch investigation)*
| class | meaning | share of the two roots |
|---|---|---|
| **(a)** shared, wired identically | already fine | small |
| 🔴 **(b) shared piece, wired INDEPENDENTLY in each root** | **THE DRIFT/BUG CLASS** — breakpoints · AI shell · document factories · catalogs+contributors · gizmos · scenario session · inspector · perspective services · toolbar/menu | ⭐⭐ **~85%** *(gap-map §0 independently: "~85% WIRING, not new capability")* — the whole `RegisterWindows`/`BuildAiShell` half of both |
| **(c) genuinely host-different** | world/kernel construction · DDS participant/network · time authority *(master vs slave)* · **networkless-vs-networked handler binding (ruled — Q26/ruling 22)** · unowned-write authority · orchestrator role · in-process kernel-mode | ⭐ small — the first ~35% of each `Initialize`; **this IS "the few bootstrap stuff"** and is the deliberate endpoint (E5) |

⇒ ⭐⭐⭐ **The (b) mass is ~85% and is drift-prone by construction. The irreducible per-host (c) core is small and already ruled.**

## 3. ⭐⭐⭐ THE DECISION — sub-questions with Claude's lean
| | question | ⭐ recommended lean | blast radius |
|---|---|---|---|
| **Q62-A** | Continue **surface-by-surface** (E5, then chase remaining (b) sites one at a time) **or** make the **structural move**: extract the (b) mass into ONE shared composition root `ComposeEditorExperience(deps)` in `Hrot.Editor.AiShared` that BOTH hosts call, parameterized by the (c) deps? | ✅ **STRUCTURAL move.** Surface-by-surface *eliminates drift sites one at a time and lets new ones ship*; a shared root makes a (b) drift **impossible** *(one wiring site)* — it ends the CLASS the user keeps catching by eye, instead of chasing instances | 🔴 **large — the (b) mass of both monoliths** |
| **Q62-B** | Adopt the existing **`SharedApplicationBootstrapper`** for the (c) NODE-bootstrap mechanics, so Editor+CGF join SimHost/IG/Stride on the one proven template? | ⭐ **YES, evaluate as the (c)-half companion** — it is the proven shared pattern for kernel/participant/phase bootstrap; the UI-experience composition (Q62-A) is the (b)-half. Together = one node bootstrap + one experience composition | ⚠ medium — touches the (c) core |
| **Q62-C** | The PREREQUISITE: extract `IEditorLogic`/`EditorApplication` (the god-facade the editor root uses pervasively) into `Hrot.Editor.AiShared` so the composition can move **behind the assembly wall** *(`Hrot.CGF` cannot reference `Hrot.Editor`)*? | ✅ **YES — this is the enabling first step.** §2c.1 measured `EditorApplication` "mostly host-agnostic (world is a parameter)". ⛔ Without it nothing can move; it is the one hard blocker | 🔴 large — the god-facade |
| **Q62-D** | STAGED or big-bang? | ✅ **STAGED** — ① seam out the god-facade (Q62-C) · ② move the (b) composition into `ComposeEditorExperience`, editor delegates byte-identical · ③ CGF calls the same root, deleting its parallel wiring · ④ optionally fold the (c) core onto `SharedApplicationBootstrapper`. ⛔ Never big-bang two 5k-line monoliths | ⭐ staging bounds the risk |

⇒ ⭐⭐ **Net lean: pivot to the structural move, staged, god-facade first.** The remaining Axis-C surface increments become *"move it into the shared root"* rather than *"wire it into CGF too."* ⛔ **This is a genuine cost** — it front-loads the god-facade extraction — but it is the only option that stops NEW drift bugs from existing, which is the user's actual concern.

## 4. ⚠ THE COUNTER-CASE (stated fairly)
- Surface-by-surface is **lower-risk per step** and has been *working* — E1–E4 shipped, each verified. A shared-root refactor of two monoliths + the god-facade is the largest change the programme has attempted; a mistake there breaks BOTH hosts at once.
- The (c) core still has to be gotten exactly right *(the ruled network/authority divergence)* — a shared root must not accidentally erase it.
- ⇒ ⭐ **If the appetite for a big refactor is low, the fallback is: keep E5 but add a `ui-probe`/composition-parity RAIL that asserts CGF wires every (b) piece the editor does** — it does not end the class, but it makes drift a RED instead of a visual-check finding. *(This is the cheaper half and could ship regardless.)*

## 5. ⭐ ON APPROVAL
Graduate to a staged design *(the god-facade seam first — its own inventory + UML)* + sequenced handoffs. ⭐ Whichever way Q62-A goes, **the composition-parity rail (§4) is worth building now** — it turns the drift class from eyes-only into a gate.
