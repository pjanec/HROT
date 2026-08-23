<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-23
current-answer: dispatch pointer for Q52 — the schema half of uniform gizmo membership, which is what
  ST-020 (--mode ig dead in bootstrap) actually needs. ⛔ Carries no design: see
  Architect_Question_52_Gizmo_Family_Composition.md, which holds the rule, the inventory and the UML.
known-conflict: none. The regression-net part-B batch owns Hrot.SystemTests/Goldens + the determinism
  rail; this batch's rail is a new file and must not touch theirs (§3).
-->
# HANDOFF — **gizmo schema follows declaration** *(`ST-020`)*

> 📌 **Dispatched at `<STAMP>`.** ⛔ **Scope FROZEN at that sha.** ⭐ Re-sync from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`ST-`**, tracker **Area I**. ⭐ **You allocate them** *(rule 3)* — the series stands
> at `ST-021`.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`Architect_Question_52_Gizmo_Family_Composition.md`](../Architect_Question_52_Gizmo_Family_Composition.md)** —
`READY-TO-BUILD`, carrying **§0 the rule** *(the user's, verbatim)*, **§1 the inventory**, **§2 the fix**,
**§3 the rail** and **§4 the `classDiagram` + `sequenceDiagram`**. ⭐ **Read §0 first — it is four lines and
it is the whole answer.** ⭐ Report per obligation ③: *"the design carries N classes and M sequences; what I
built matches / deviates HERE and why."*

⭐⭐ **You filed `ST-020` correctly and refused the policy call.** ⛔ The three options you named were all
policy — ⭐ **the ruling makes it not a policy call at all**: *support all, decide on current presence of
the component.* ⇒ **register the types; never instantiate them.** ⚠ **Your cascade measurement was right
and is what bounded it** — 📐 the set is **5**, enumerated in §1.

## 1. ⭐⭐⭐ THE ITEMS

| # | task | design | gate |
|---|---|---|---|
| 🔴🔴 **T1** | ⭐⭐⭐ **IG registers the component types its declared projector families require.** 📐 Five, all `Fdp.Toolkits` types IG **already references** ⇒ **zero new project edges**: `BrainBlackboard` · `BehaviorState` · `EqsSensor` · `BallisticProjectile` · `NavigationIntent`. ⚠ **Where they go is yours to decide** — §4.1 draws a **`MapSchemaPack`** because UXI-23's `MapInteractionPack` will need one place to call; ⛔ **if a simpler home is genuinely better, argue it in the report and fold it into §4.1** *(obligation ⑤)* | **§0** · **§2** · **§4** | ⭐⭐ **`--mode ig` starts and ticks** — 📐 `ModeStartupRails` already has the case, so **your own rail is the gate** |
| 🔴 **T2** | ⭐⭐ **REMOVE `ST-020`'s tripwire.** 📌 You wrote it to **fail the day the mode is fixed, naming the row** — ⭐ **that day is this batch**, and it working as designed is the point. ⇒ the `ig` case becomes an ordinary passing mode rail | `ST-019`'s own design *(`R-131`)* | ⭐ **8 / 8 modes**, no quarantine, no skip |
| ⭐⭐ **T3** | ⭐⭐⭐ **THE PER-HOST RAIL: every component required by a projector family this host declares is in this host's registry.** ⛔ Not a comment, not a checklist — §3 | **§3** | ⚠⚠ **IT MAY REDDEN OTHER HOSTS ON FIRST RUN.** ⭐⭐⭐ **Each red is a FINDING — report it, do NOT tune the rail down to fit.** ⛔ If a red is a real omission you cannot safely fix in this batch, **file it and say so**; ⛔ **do not narrow the rail's scope to hide it** |

⭐ **`T1` → `T2` → `T3`?** ⛔ **No — `T3` LAST but write it EARLY enough to discover the other hosts.** ⚠ The
rail is the thing most likely to produce surprises, and a surprise is cheaper to report than to rush.

## 2. ⚠ WHAT WILL BITE — measured, so you do not re-derive it

| ⚠ | |
|---|---|
| ⭐⭐ **registering a TYPE is not an entity CARRYING it** | 📄 **`docs/UX/UX_Tasks_Detail.md` Correction 47** made exactly this conflation and corrected it. ⇒ ⛔ **`RegisterComponent<BrainBlackboard>()` on IG does NOT put a brain on IG** — no IG entity ever carries it, so the projector matches nothing. ⭐ **That is the design, not a loophole** |
| 🔴 **two of the five are IG's OWN gizmos** | `EqsSensorGizmo` *(`EqsSensor`)* and `ProjectilePresentationGizmo` *(`BallisticProjectile`)* live in `Hrot/Subsystems/Hrot.IG/Gizmos/`. ⇒ ⭐ **IG declares projectors it cannot satisfy in its own assembly** — ⛔ a plain omission, and it has been live for however long |
| ⭐ **`Hrot.ScenarioEditor` declares ZERO projectors** | ⇒ that registrar call is a **no-op today**. ⭐ **Leave it** *(support all)* — ⛔ it was never the problem, and removing it is not this batch's business |
| ⭐ **`--mode all` masks all of this** | SimHost's registries supply the missing schema on the shared world ⇒ IG is satisfied **by accident of co-tenancy.** ⚠ **So a green `--mode all` proves nothing about `--mode ig`** |
| ⛔ **no loosening** | 🔒 user, verbatim. `StatelessGizmoRegistry` keeps throwing — ⭐ it is a **bootstrap-time SCHEMA** check, ⛔ not the runtime presence check |

## 3. ⛔ LANE & SCOPE

⭐ **Your surface:** `Hrot.IG` *(and wherever `T1`'s schema home lands)* · **one new rail file**.

⚠⚠ **A PARALLEL BATCH EXISTS** — 📄 `HANDOFF_Regression_Net_Part_B.md` *(ids `HN-`/`MX-`, tracker **Area J**)*
owns `Hrot.SystemTests`'s **`Goldens/`, the golden store, the normalizer, the determinism rail and the panel
rails**. ⭐⭐ **The split is by FILE: your rail is a new file; `ModeStartupRails.cs` is already yours.**
⛔ **Do not add a golden and do not touch theirs.** ⭐ **Rule 4: pull the coordinator branch before your
final commit.**

⛔ **Not this batch:** `MapInteractionPack` *(UXI-23 — 📄 `docs/UX/UX_Feature_Map_Parity.md` §3.2, designed and
not built)* · the `TagMask` layer filter *(UXI-28 — ⭐ a separate operator-facing feature)* · anything CGF-side.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs the
dispatch sha** · a `--no-build` column · every RED confirmed pre-existing **by name** · `tracker-counts.py
--check` · `rulings-check.py` · `design-digest.py --check` · **the `ST-` ids you allocated** *(rule 5)*.
⭐⭐ **Your last batch's `REPORT_Runner_Tick_And_Mode_Rails.md` §2 is the model — keep doing that.**

⭐⭐ **Row 8 — the integration invariant.** ⭐ **`ModeStartupRails` IS the integration gate** *(all 8 modes)*,
plus `Hrot.IG.Tests`. 📐 **Your baseline on the merged tree: `Hrot.SystemTests` `52 / 0`** — ⛔ so any red is
yours until proven otherwise.

⚠ **Known baseline quirks — do not re-derive:** `tracker-counts.py --check` is **blind to `ST-` rows**.
`Fdp.Presentation.Tests` crashes ~18–20 cases in *(`BP-419`, `R-131`)*. `tools/ai-debug-mcp` `verify.mjs`
fails pre-existing *(needs `npm install`)*. `rulings-check.py` emits **2 staleness WARNs**
*(`.claude/CLAUDE.md`, `docs/projects/SOLUTION-OVERVIEW.md`)* — ⭐ already named, **not yours to fix**.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`Architect_Question_52`](../Architect_Question_52_Gizmo_Family_Composition.md)** —
**§4.1's diagram must match where the schema actually landed**, and §5 records what `T3` found on the other
hosts. ⛔ Design content in the design; the report points at it.
