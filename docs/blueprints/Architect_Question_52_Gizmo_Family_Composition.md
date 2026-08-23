<!--STATUS
state: LIVE
build-state: DESIGN
updated: 2026-08-23
current-answer: §4 — the recommended answers per sub-question. §2 is the measurement that changes the
  question's shape (the implementation session had three options; the measurement produces a fourth,
  and the fourth is clean).
design-basis: ST-020 (Blueprint_Issues_Tracker Area I) · DESIGN_Stride_Port.md §9 (the rail that found it)
  · docs/designs/gizmos-1/DESIGN.md · docs/projects/Hrot/Subsystems/Hrot.IG.md §"Namespace Hrot.IG.Gizmos"
  · PROGRAMME_Unification_And_Harness.md D6.
known-conflict: none. ⚠ Hrot.IG.md:305 documents the wide aggregation as AS-BUILT, not as a rationale —
  it records what the code does, so it is not a ruling that settles §3.
-->
# Q52 — **who may declare a gizmo projector family, and against which world?**

> 🔴 **Raised by a rail, not by a person.** `ST-019`'s mode rails found `--mode ig` dead in bootstrap on
> their first run. The implementation session **correctly refused the policy call** and quarantined the
> mode with a tripwire. This is that call.

## 0. ⭐ THE ONE-LINE QUESTION

⭐⭐ **`StatelessGizmoRegistry` throws when a projector's required component is absent from the world.**
⛔ IG declares projector families whose components IG's world never registers. ⇒ **either the declaration
is wrong or the world is wrong** — and the answer decides whether a fail-loud registry stays fail-loud.

## 1. INVENTORY — **the enumeration, with the queries**

| query run | total | result |
|---|---|---|
| `grep ProjectReference Hrot/Subsystems/Hrot.IG/Hrot.IG.csproj` | **9** | ⭐ **`Hrot.SimHost` is NOT among them** — `Hrot.Common`, `Hrot.Core`, `Hrot.Presentation`, `Hrot.AI.Behaviors`, `Hrot.Network.NED`, `Fdp.Core`, `Fdp.Presentation`, `Fdp.Toolkits`, `Fdp.Toolkits.Analyzers` |
| `grep -rl "class {Cognitive,Combat,MuscleRole,IgRole}ComponentRegistry"` | **4** | `IgRoleComponentRegistry` → `Hrot.IG`; the other three → **`Hrot.SimHost`** |
| the 5 component types the cascade names, located | **5 / 5** | ⭐ **all in `Fdp.Toolkits`** — `NavigationIntent` *(Navigation)*, `BrainBlackboard` + `BehaviorState` *(Behavior/Components)*, `EqsSensor` *(Spatial/Eqs)*, `BallisticProjectile` *(Combat/Components)* |
| `grep RegisterComponent Hrot/Subsystems/Hrot.IG/IgRoleComponentRegistry.cs` | **~20** | ⭐⭐ **rendering / perception / combat-visual only** — `ResolvedStyle`, `CullingState`, `SelectionState`, `HistoryTrail`, `VisualEffectState`, `TracerTarget`, `MapOverlayStyle`, `EntityInfo`, `IgHealthState`, `PerceptionReceptor`, `WeaponState`, `PhysicsCollider`, … ⛔ **NOT ONE brain-tier component** |
| `grep "GizmoProjector" Hrot/Subsystems/Hrot.AI.Behaviors/Gizmos/*.cs` | **1** | `HillAttackGizmo` — `[GizmoProjector(typeof(BrainBlackboard), typeof(BehaviorState), typeof(SimTransform))]` |
| `grep Register Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs` | **4 families** | ⭐ IG aggregates **`Hrot.Common`**, **`Hrot.AI.Behaviors`**, **IG-local**, **`Hrot.ScenarioEditor`** |
| `grep -rln "StatelessGizmoRegistry\|GizmoProjector" docs/ .dev/` | **20 docs** | ⭐ the owning design is **`docs/designs/gizmos-1/DESIGN.md`**; ⚠ it specifies the registry and the wire schema, ⛔ **it does not say who may declare a family** — that is the gap this question fills |

## 2. ⭐⭐⭐ THE MEASUREMENT THAT CHANGES THE QUESTION

📌 **`ST-020` framed three candidate fixes.** All three take *"IG must satisfy the contract"* as given and
ask **how**. ⭐⭐ The inventory says the premise is the thing to check:

| ⭐ what the code says | |
|---|---|
| ⭐⭐⭐ **IG's world hosts NO brain tier** | `IgRoleComponentRegistry` registers style, culling, selection, trails, effects, overlays, perception, weapon visuals. ⛔ **No `BehaviorState`, no `BrainBlackboard`, no `BrainBTreeState`, no `MissionPlanQueue`** |
| ⭐⭐⭐ **yet IG declares the brain-tier projector family** | `Hrot.AI.Behaviors.Gizmos.GizmoRegistrar.RegisterAll(...)`, whose `HillAttackGizmo` requires `BrainBlackboard` + `BehaviorState` |
| ⭐⭐ **so the registry is RIGHT and it is telling the truth** | it is fail-loud by design *(`BP4005`'s trap: never trade a loud failure for a silent one)*, and what it is loudly saying is **"you declared a projector over a tier you do not host."** ⛔ That is not a missing registration; **it is a mis-composition** |
| ⚠ **why `--mode all` hides it** | SimHost's `Cognitive`/`Combat`/`MuscleRole` registries put the brain schema on the shared world ⇒ IG's declaration is satisfied **by accident of co-tenancy**, never by IG's own contract |
| ⚠ **and why the mirror-pattern fix cascaded** | adding `NavigationIntent` to `IgRoleComponentRegistry` moved the failure to `BrainBlackboard`. ⭐ **Correctly diagnosed as a cascade** — it is a cascade because it was pushing IG toward hosting a whole tier, one component per push |

⇒ ⭐⭐⭐ **A FOURTH OPTION EXISTS, and neither `ST-020`'s ① ② nor ③ is it: NARROW THE DECLARATION.**

⛔⛔ **`Hrot.IG.md:305** documents the four-family aggregation — ⚠ **but as AS-BUILT, not as a rationale**
*(the project docs describe files; they do not carry intent — `R-129`)*. ⛔ **Do not read it as a ruling.**

## 3. ⭐⭐ THE SUB-QUESTIONS

| # | question |
|---|---|
| **A** | **Is IG meant to draw brain-tier debug gizmos at all?** ⭐ If yes, IG must host (or receive) the schema. If no, the declaration is simply wrong |
| **B** | **Where does the "you may declare a family only if you registered its schema" invariant live** — in the composition root by convention, or structurally, checked? |
| **C** | **Does `StatelessGizmoRegistry` keep throwing?** *(`ST-020` ③ proposed it skip absent components)* |
| **D** | **Does `Hrot.ScenarioEditor`'s family belong in IG?** ⚠ Raised by the same line and never asked |
| **E** | **Sequencing** — before or after the regression net's `N1`? |

## 4. ⭐⭐⭐ RECOMMENDED ANSWERS — **the user approves or names the one to change**

| # | ⭐ recommendation | reasoning | blast radius |
|---|---|---|---|
| **A** | ⭐⭐ **NO — not from IG's own world.** IG is an image generator: it renders what it is *told*, and brain state reaches it as **replicated presentation data** *(`DebugPrimitivesIngressTranslator`, already an IG reference)*, ⛔ not as a locally-simulated brain tier. ⇒ **a brain-debug gizmo projected from IG's own components is a category error** | ⭐ the inventory is one-sided: 0 of ~20 IG components are brain-tier, and the projector needs 2 that IG will never own. ⛔ The alternative makes the image generator host the AI tier | ⭐ **small** — drop one call line, keep the reference *(NED translators still need it)* |
| **B** | ⭐⭐⭐ **STRUCTURALLY, and checked by a rail** — a per-host test that asserts *"every projector family this host declares has its required components in this host's registry."* ⛔ Not a convention in a comment | ⭐ this is the general form of the defect, and it is exactly the shape the programme keeps finding: **the invariant existed, nothing enforced it** ⇒ ⭐⭐ the rail also covers CGF, SimHost, editor and any future host **for free** | ⭐ **one new test file**; ⚠ it may redden other hosts on first run — ⭐⭐ **that is the point, and each red is a finding, not a fix-to-green** |
| **C** | ⭐⭐⭐ **YES — IT KEEPS THROWING. ⛔ Do not soften it.** `ST-020`'s own reasoning is right and stands: skipping absent components lets a genuine typo silently drop a gizmo | ⭐ the registry is the **only** thing that found this; ⛔ softening it removes the sole detector of exactly this class of bug | ⭐ **none** — this is a decision not to change code |
| **D** | ⚠ **MEASURE, then almost certainly DROP.** ⛔ I have not enumerated `Hrot.ScenarioEditor.Gizmos`' projectors ⇒ **stating that honestly rather than guessing.** ⭐ The prior is the same as A: an image generator hosting the *scenario editor's* authoring gizmos is a smell. ⇒ **the `B` rail answers `D` as a side effect** | ⭐ an authoring-time family in a runtime renderer is the same mis-composition one layer over | ⭐ **small**, and ⭐⭐ **`B` makes it self-answering** |
| **E** | ⭐⭐ **AFTER `N1`, BEFORE any golden of an IG panel.** ⛔ Not urgent-blocking: the mode is quarantined **with a tripwire**, so it cannot rot silently. ⭐ But `--mode ig` is a shipped `launchSettings.json` profile that cannot start, so it does not wait long either | ⭐ `N1` gates the whole net *(charter `D6`)*; ⛔ nothing in this question touches determinism | ⭐ **none** — pure ordering |

⭐⭐ **The honest limit of this answer:** `A` rests on *what IG is for*, and I am reading that from its
component registry and its references, ⛔ **not from a design doc that says it** — `docs/designs/gizmos-1/DESIGN.md`
specifies the registry and the wire schema and is **silent on who may declare a family**. ⚠ **If the
intent was ever "IG renders brain gizmos for replicated entities", `A` flips**, and then the right fix is
option ② *(the schema moves down beside its types in `Fdp.Toolkits`)* — ⭐ **never** ① *(a
`Hrot.IG` → `Hrot.SimHost` edge)*, which the inventory shows would invert the layering.

## 5. ⛔ NOT READY TO BUILD

⭐ **`build-state: DESIGN`** — per the UML obligation, this carries no `classDiagram`/`sequenceDiagram` yet
and **must not be dispatched.** ⇒ once `A`–`E` are approved, this doc gains the two diagrams *(the host →
family → schema relation, and the bootstrap sequence that throws)* and becomes `READY-TO-BUILD`.
