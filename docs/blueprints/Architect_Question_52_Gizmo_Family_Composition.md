<!--STATUS
state: LIVE
build-state: DESIGN
updated: 2026-08-23
current-answer: ⛔⛔ READ §0 FIRST. The user's RULING (A/C) stands; the MECHANISM I proposed to implement
  it does NOT. Three live designs in docs/UX/ — which this question never searched — settle the same
  problem a different and better way. §4 records the approval; §0 records what invalidates its mechanism.
  ⛔ NOT dispatchable: reconciliation is owed before any UML.
stale-below: ⛔ §2b's per-projector "verdict" column and §4-A's blast radius — both assume the host
  curates its own declaration, which UXI-23's 2026-08-10 ruling forbids. Read §0 before quoting either.
known-conflict: docs/UX/UX_Feature_Map_Parity.md §3.2 (uniform membership) ·
  docs/UX/UX_Feature_Map_Layers.md §2.2 (per-gizmo TagMask) · docs/UX/UX_Tasks_Detail.md Correction 47
  (registering a component TYPE is not an entity carrying it). §0 states the reconciliation.
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

## 0. ⛔⛔⛔ ON HOLD — **`docs/UX/` ALREADY SOLVED THIS, AND BETTER. THE RULING STANDS; MY MECHANISM DOES NOT.**

> 🔒 **User, `2026-08-23`:** *"could you pls look in docs/UX documents? I think i have been already
> solving the issue what the map should look like once everything is unified"* — ⭐⭐⭐ **they had.**
> ⛔⛔ **This question was written without searching `docs/UX/` at all** — a **37-issue, 21-designed**
> programme with its own tracker, rulings ledger and corrections log. 📌 **`R-129`'s exact failure**: I
> read the code's mechanism and reasoned from *how it IS*.

### ⭐ What survives untouched

| ⭐ | |
|---|---|
| ⭐⭐⭐ **The user's ruling** — *"ig is not meant to draw brain tier gizmos. brain components are not instantiated on IG."* | ✅ **stands, and the UX designs SATISFY it** — see the reconciliation |
| ⭐⭐⭐ **`C` — no loosening** | ✅ **stands, and is now MORE load-bearing.** The registry's throw is the only thing that detects an incomplete schema pack |
| ⭐⭐ **`§2b`'s ENUMERATION** *(4 projectors, 4 components, `ScenarioEditor` declares zero)* | ✅ **the facts hold.** ⛔ Only its *verdict* column is void |

### ⛔ What is invalidated — **three live designs, none of which this question cites**

| 📄 | what it says | ⇒ why my mechanism is wrong |
|---|---|---|
| **`UX_Feature_Map_Parity.md` §3.2** *(UXI-23, `☑ designed`)* | 🔒 **ruled `2026-08-10`:** *"all hosts share the **FULL set**; differences are **data availability** or host rules, **never set membership**"* ⇒ *"the pack decides; **the host does not curate**"*, via a **`MapInteractionPack`** that performs the four gizmo registrars for all five hosts | ⛔⛔ **`§4-A` — "IG drops the brain family call" — IS THE HOST CURATING.** ⚠ And it runs **against the direction of travel**: UXI-22/23's headline defect is that **CGF and SimHost are MISSING `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar`** ⇒ the design wants **more** hosts calling **more** registrars, from one place |
| **`UX_Feature_Map_Layers.md` §2.2** *(UXI-28, `☑ designed`)* | ⭐⭐ **visual kinds — *entities · perception · **ai-helpers*** — are declared **per gizmo type** as a **`TagMask`**, and a tag is hidden with **ALL-semantics** *(hidden if any tag is hidden; untagged always visible)* | ⭐⭐⭐ **THE MECHANISM FOR "IG DOES NOT SHOW BRAIN GIZMOS" ALREADY EXISTS AND IS A LAYER TAG.** ⛔ Not a dropped registrar call. `ai-helpers` is **literally one of the named kinds** |
| **`UX_Tasks_Detail.md` Correction 47** *(`2026-08-14`)* | ⭐⭐⭐ *"`RegisterComponent<T>()` registers a **type with the world**; I read that as both **entity-level presence** and serializer scope. **Two conflations in one sentence**"* | ⛔⛔⛔ **`§2b` COMMITS THAT EXACT CONFLATION** — nine days after it was written down and corrected |

### ⭐⭐⭐ THE RECONCILIATION — **and the user's own wording is the hinge**

🔒 **They wrote *"brain components are not INSTANTIATED on IG."*** ⭐⭐⭐ **Instantiated — not registered.**
📐 Per Correction 47 those are **different facts**, and the difference is the whole answer:

| | |
|---|---|
| ⭐⭐ **register the component TYPE on IG's world** | cheap, uniform, and it makes `StatelessGizmoRegistry`'s contract **satisfiable** ⇒ **bootstrap stops dying** |
| ⭐⭐⭐ **no IG entity ever CARRIES `BrainBlackboard`/`BehaviorState`** | ⇒ `HillAttackGizmo` **matches nothing and never draws.** 🔒 **The ruling holds VERBATIM — by data availability, exactly as UXI-23 §3.2 says it should be** |
| ⭐⭐ **and if it must be hidden even where the data EXISTS** | ⇒ **UXI-28's `TagMask`** — tag it `ai-helpers`, hide the tag. ⭐ *Membership uniform, visibility earned* |

⇒ ⭐⭐⭐ **The bootstrap crash is fixed by making the SCHEMA uniform, not the DECLARATION narrow.** 📐 The
defect's real shape: **the registrar call was shared and copied while the component registration was left
per-host.** ⛔ `MapInteractionPack` as designed would **crash all five hosts** the day it lands, for the
same reason `--mode ig` crashes now ⇒ ⭐⭐ **it needs a schema half — and that, not a rail, is `B`'s answer.**

### ⛔ What is owed before this is dispatchable

| | |
|---|---|
| **①** | ⭐⭐ **Re-answer `A`/`B`/`D` on the UX mechanism** — schema pack + `TagMask`, ⛔ not per-host curation. ⭐ `C` and `E` need no change |
| **②** | ⭐⭐⭐ **Decide whether this belongs to `Q52` AT ALL, or is a task under UXI-23** — 📐 `MapInteractionPack` is **designed and NOT BUILT** *(zero occurrences in `.cs`)*, so `--mode ig` may simply be **its first acceptance case**. ⚠ **A `Q52` that duplicates UXI-23 is exactly the two-designs-for-one-concept failure ruling 9 forbids** |
| **③** | ⭐ **the tripwire stays armed** *(`ST-020`)* — ⛔ nothing here is a reason to disarm it |

⚠⚠ **AND A PROGRAMME-LEVEL FINDING, WORSE THAN A MISSING POINTER:** ⛔⛔ **the pointer was already there.**
📐 `PROGRAMME_Unification_And_Harness.md` §6's **last row is `UX/`**, and it names
**`UX_Feature_Cgf_Brain_Diagnostics.md` explicitly**; §5 already cites `UX_Feature_Perspective_Restore.md`
§3. ⇒ 🔴 **I wrote that row and then never opened what it points at.** ⭐⭐ So this is **not** *"the corpus
was undiscoverable"* — ⛔ **it is "a `docs/` row in my own charter did not survive my own compaction."**
📄 `docs/UX/SHARED_SURFACES.md` closes by asking *"⚠ Open: does the other side read this?"* and answering
*"**that link has not been added**."* 🔴 **I am the other side; the answer was no.**

## 1b. ⭐ THE ONE-LINE QUESTION *(as originally framed — read §0 first)*

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

## 2b. ⛔⛔⛔ THE PER-PROJECTOR ENUMERATION — **and it corrects `§4-A`'s blast radius, which was WRONG**

> ⭐⭐ **This is the measurement `D` asked for, run after approval.** ⛔ It does not overturn `A`, `B` or
> `C` — ⭐ it **refines** them, and it corrects a number I stated too confidently.

📐 **Every `[GizmoProjector]` in the four families IG declares, against `IgRoleComponentRegistry`'s ~20:**

| family | projectors | ⛔ unsatisfied in IG's world |
|---|---|---|
| **`Hrot.Common`** | **7** — `SelectionState+SimTransform` · `NetworkIdentity` · `SimTransform+PerceptionReceptor` · `IgHealthState` · **`NavigationIntent+SimTransform`** · `SimTransform` · `TargetMemory+SimTransform` | ⛔ **1** — `NavigationIntent` |
| **`Hrot.AI.Behaviors`** | **1** — `HillAttackGizmo`: `BrainBlackboard+BehaviorState+SimTransform` | ⛔ **1** — both brain components |
| **`Hrot.IG`** *(its OWN)* | **3** — `EqsSensorGizmo`: `SimTransform+`**`EqsSensor`** · `SimTransform+VisualEffectState` · `ProjectilePresentationGizmo`: **`BallisticProjectile`**`+SimTransform` | 🔴🔴 **2 — IG'S OWN GIZMOS** |
| **`Hrot.ScenarioEditor`** | 🔴 **ZERO** | — |

### ⭐⭐⭐ What that changes

| ⛔ | |
|---|---|
| 🔴🔴 **`§4-A` said *"drop one call line, blast radius small."* THAT WAS WRONG.** | 📐 Dropping the brain family fixes **1 of 4** unsatisfied projectors. ⛔ **Three survive**, and ⭐⭐⭐ **TWO ARE IG'S OWN GIZMOS** — `EqsSensorGizmo` and `ProjectilePresentationGizmo` live in `Hrot/Subsystems/Hrot.IG/Gizmos/`, read `EqsSensor` / `BallisticProjectile`, and IG **never registers either**. ⇒ **IG declares projectors it cannot satisfy in its own assembly** |
| ⭐⭐ **so the cascade was FINITE, and is now bounded** | 📌 The lane measured *"a cascade, not a one-liner"* and stopped — ⭐ **right to stop, and the enumeration is what bounds it: exactly 4 projectors, 4 components.** ⛔ It is **not** the unbounded slide toward hosting the AI tier that made me reach for a family-level answer |
| ⭐⭐⭐ **THE FAMILY IS THE WRONG GRANULARITY — measured, not argued** | 📐 `Hrot.Common`'s 7 projectors span IG-visual *(`IgHealthState`!)*, shared *(`NetworkIdentity`)* **and** one Brain-owned command. ⇒ ⛔ **no family-level keep/drop can express the right answer**, because the family bundles tiers. ⭐⭐ **This STRENGTHENS `B`**: the invariant and its rail must be **per-projector** |
| ⭐ **`D` is answered, and not as I leaned** | 📐 `Hrot.ScenarioEditor` declares **zero** projectors ⇒ the call is a **no-op today**. ⛔ So dropping it is **hygiene, not a fix** — ⚠ but it is a **latent trap**: the day an authoring projector is added there, IG inherits it silently. ⇒ ⭐ **drop it, and say in the commit that it fixed nothing today** |

### ⭐ The per-projector verdict

| projector | missing | ⭐ verdict | why |
|---|---|---|---|
| `HillAttackGizmo` *(AI.Behaviors)* | `BrainBlackboard`, `BehaviorState` | ⛔⛔ **DROP the family call** | 🔒 **user, `2026-08-23`:** *"ig is not meant to draw brain tier gizmos. brain components are not instantiated on IG."* |
| `EqsSensorGizmo` *(**IG's own**)* | `EqsSensor` | ✅ **REGISTER** | ⭐ IG's own gizmo over an `Fdp.Toolkits` type in a project IG already references. ⛔ Zero policy — it is a plain omission |
| `ProjectilePresentationGizmo` *(**IG's own**)* | `BallisticProjectile` | ✅ **REGISTER** | ⭐ same, and IG **already** registers the projectile's visual peers *(`TracerTarget`, `VisualEffectState`)* ⇒ the omission is visible against its own neighbours |
| `NavigationTargetGizmo` *(`Hrot.Common`)* | `NavigationIntent` | ⚠ **REGISTER — and this is the ONE place I am INTERPRETING the ruling, not applying it** | 📐 `NavigationIntent` is a **replicated DDS descriptor** *(`dtNavigationIntent = 52`, `Hrot.Network.NED` — an IG reference)*, so IG legitimately **receives** it. ⭐ The ruling names *brain components*; `BrainBlackboard`/`BehaviorState`/`BrainBTreeState` are brain **working memory**, whereas this is the **published navigation goal**. ⛔ **The alternative is splitting `Hrot.Common`'s shared family for one projector** — worse. ⚠ **If the user reads `NavigationIntent` as brain-tier, this flips and the family must split** |

⇒ ⭐⭐ **`C` is untouched and now better supported:** 🔒 **user: *"no losening."*** 📐 The registry throwing is
the only reason all four of these are known at all — **two of them IG's own bugs, live for however long.**

## 3. ⭐⭐ THE SUB-QUESTIONS

| # | question |
|---|---|
| **A** | **Is IG meant to draw brain-tier debug gizmos at all?** ⭐ If yes, IG must host (or receive) the schema. If no, the declaration is simply wrong |
| **B** | **Where does the "you may declare a family only if you registered its schema" invariant live** — in the composition root by convention, or structurally, checked? |
| **C** | **Does `StatelessGizmoRegistry` keep throwing?** *(`ST-020` ③ proposed it skip absent components)* |
| **D** | **Does `Hrot.ScenarioEditor`'s family belong in IG?** ⚠ Raised by the same line and never asked |
| **E** | **Sequencing** — before or after the regression net's `N1`? |

## 4. ⭐⭐⭐ ANSWERS — ✅ **APPROVED BY THE USER, `2026-08-23`**

> 🔒 **User, verbatim:** *"approved recommended responses to architect questions 52. ig is not meant to
> draw brain tier gizmos. brain components are not instantiated on IG. no losening."*

⭐⭐ **All five recommendations stand as written below.** ⭐ The reply settles `A` and `C` **explicitly and
independently** — ⛔ not merely as assent to my reasoning, which matters because `§2b` then found my
`A` blast radius wrong: ⭐⭐⭐ **the RULING held while my SIZING did not.**

⚠ **One open interpretation, flagged not buried:** `NavigationIntent` — `§2b`'s last row. ⭐ I read
*"brain components"* as brain **working memory** and register it; ⛔ if it should read as brain **tier**,
that projector flips to a drop and `Hrot.Common`'s family must split.


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
