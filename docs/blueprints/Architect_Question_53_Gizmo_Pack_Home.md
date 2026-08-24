<!--STATUS
state: LIVE
build-state: DESIGN
updated: 2026-08-24
current-answer: §4 — the recommendation (Option A: reflection-driven, zero moves) and the alternative
  (Option B: move 5 projectors down + 2 edges). Awaiting the user's ruling.
design-basis: 🔒 user 2026-08-23 (uniform membership) · REPORT_Uniform_Gizmo_Membership.md §2 (the block,
  measured) · DESIGN_Uniform_Gizmo_Membership.md §7.3 (the lane's proposed way out) ·
  Architect_Question_52 §0 (support all, presence decides).
known-conflict: none.
-->
# Q53 — **where does the one gizmo pack live?** *(`ST-028` — item ② is structurally blocked)*

## 0. ⭐⭐⭐ THE BLOCK — **measured, and confirmed by the coordinator**

⭐ `ST-027` shipped the **schema** half *(all 15 component types, every host)*. ⛔ `ST-028` — the single
**declaration** pack `MapGizmoPack` — **cannot be built as designed**, and the reason is not the home I
picked; it is the reference graph.

| 📐 the contradiction | |
|---|---|
| a pack must be **referenced BY** every host | ⇒ it sits **below** them |
| it must **reference** all seven families | ⇒ three of them *(`Hrot.IG`, `Hrot.SimHost`, `Hrot.CGF`)* **are hosts** |
| 🔒 **verified by the coordinator** | `Hrot.IG` · `Hrot.SimHost` · `Hrot.CGF` **all reference `Hrot.Common`** ⇒ a pack in `Hrot.Common` referencing them back is a **cycle**. **No existing assembly references all seven.** |

⇒ ⭐⭐ **The lane was right to refuse to guess a home** — the same discipline it applied to `replaybrowser`.
This is a real architectural fork, so it is yours to rule on.

## 1. INVENTORY

| 📐 | |
|---|---|
| families / projectors | **7 / 22** *(`ST-029` corrected my `6 / 18`)* — `Common` 8 · `ScenarioEditor.Gizmos` 7 · `IG` 3 · `SimHost` 1 · `CGF` 1 · `AI.Behaviors` 1 · `Presentation.Gizmos` 1 |
| the 5 projectors **inside host assemblies** | `IG`: `EffectPresentationGizmo` · `EqsSensorGizmo` · `ProjectilePresentationGizmo` · `SimHost`: `SimHostEntityPresentationGizmo` · `CGF`: `CgfEntityPresentationGizmo` |
| 🔒 **cycle check for Option B** *(coordinator)* | `Hrot.Common` does **not** reference `Hrot.Presentation`; `Hrot.AI.Behaviors` does **not** reference `Hrot.Presentation` ⇒ **`Presentation → Common` and `Presentation → AI.Behaviors` are both cycle-free** |
| ⭐ the generator groups by **namespace, not assembly** | ⇒ a projector file can move assemblies **keeping its namespace**, and every existing `GizmoRegistrar.RegisterAll` call site still compiles — 📐 `VisualEffectState` just demonstrated this in `ST-027` |

## 2. ⭐⭐ OPTION A — **reflection-driven pack, ZERO moves** *(coordinator's addition)*

⭐ **`MapGizmoPack` discovers every `[GizmoProjector]` at runtime and registers it** — no compile-time
reference to any family assembly, so the cycle never arises. ⭐ The assemblies are already loaded *(every
host references them transitively)*; the pack reflects over loaded types, exactly as the completeness rail
*(invariant `B`)* must anyway.

| ⭐ for | ⛔ against |
|---|---|
| ⭐⭐⭐ **zero file moves, zero new project edges** — smallest blast radius | ⚠ **reflection at bootstrap** — a load-order or trimming hazard if an assembly is not yet loaded |
| ⭐⭐ **`B` and the pack become the SAME mechanism** — one reflection over `[GizmoProjector]` namespaces both *declares* and *checks completeness* | ⚠ **the generated per-namespace `RegisterAll` is bypassed** — the pack would call projector ctors directly, re-implementing what the generator emits |
| ⭐ **a seventh family is picked up automatically** — nothing to edit | ⛔ **loses the generator's compile-time guarantee** that a projector is registered with the right settings |

## 3. ⭐⭐ OPTION B — **move the 5 host projectors down** *(the lane's proposal)*

⭐ **Consolidate the 5 host-assembly projector files into `Hrot.Presentation`, keeping their namespaces**,
then add `Presentation → Common` and `Presentation → AI.Behaviors` *(both cycle-free)*. `MapGizmoPack`
then lives in `Hrot.Presentation`, references all seven families, and is referenced by every host.

| ⭐ for | ⛔ against |
|---|---|
| ⭐⭐⭐ **keeps the generator's compile-time registration** — no reflection | ⛔⛔ **5 cross-assembly file moves + 2 new project edges** — materially larger blast radius |
| ⭐⭐ **`MapGizmoPack` is an ordinary static call**, like every existing registrar | ⚠ **moves domain-presentation code** *(projectiles, EQS, entity symbols)* **into the engine-presentation layer** — a layering smell of its own |
| ⭐ the 5 projectors are **barely coupled** to their host *(1 `[GizmoProjector]` each, no host-internal state)* | ⚠ **`git mv` × 5** across assemblies — history follows, but reviewers must check each |

## 4. ⭐⭐⭐ RECOMMENDATION — **the user rules**

| # | ⭐ recommendation | why |
|---|---|---|
| **A vs B** | ⚠ **LEAN A, but it is genuinely close** — ⭐ Option A because the completeness rail *(invariant `B`)* **must reflect over `[GizmoProjector]` anyway**, so A makes the pack and its own check **one mechanism** and costs nothing structural. ⛔ **The load-order hazard is the only real risk**, and it is testable by `ModeStartupRails` — if any mode boots with a family unloaded, A fails loudly and we fall back to B | ⭐ smallest blast radius that satisfies *"the pack decides, the host does not curate"* |
| **the fallback is CHEAP** | ⭐ **build A first; if a mode's bootstrap cannot see a family's assembly, B is the escape** — and A's reflection code is the same enumeration `B`'s rail needs, so little is wasted | ⛔ do not pre-commit to B's moves before A is measured |
| ⛔ **NOT** *"keep the per-host lists"* | 🔒 the user's ruling forbids it — *"replaybrowser is no exception… the host does not curate"* | — |

⚠ **What I do NOT know and A must prove:** whether every family assembly is **loaded** at the point the
pack runs, on **every** mode. ⭐ Reflection over `AppDomain` loaded assemblies is only complete if they are
loaded — a type never referenced can be absent. ⇒ ⭐⭐ **A's first rail is "the pack finds all 7 families in
every mode"**, and a miss there is the signal to switch to B, not to widen an ignore-list.

## 5. ⛔ NOT READY TO BUILD

⭐ `build-state: DESIGN` — once A or B is chosen this gains the `classDiagram`/`sequenceDiagram` for the
chosen mechanism and becomes `READY-TO-BUILD`, and invariant `B`'s rail *(`ST-028` item ③)* ships with it.
