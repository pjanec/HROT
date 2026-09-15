<!--STATUS
state: LIVE
build-state: RESOLVED 2026-08-24 — Option A (reflection). Option B RETIRED; csproj aggregation is the
  backlog alternative. Headless publishing capability stays (gating optional). Build in HANDOFF_Gizmo_Reflection.md.
updated: 2026-08-24
current-answer: §4 — recommendation is Option A (reflect-and-register-all, extended to components+events+
  gizmos as ONE bootstrap step), decisively after two measured findings in §1 (component IDs are explicit;
  the reflection path already ships in RepositoryPriming). §3b answers the user's layering question and
  frames aggregation (Option C) as a separate reopening of Q51. ✅ RESOLVED 2026-08-24: (a) headless publish capability STAYS, gating optional (design §8.5b); (b) Option A
  reflection, Option B retired, aggregation = backlog alternative. P3 recorder fix stands (item ⓪).
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
| ⭐⭐⭐ **component IDs are EXPLICIT, not registration-order** *(coordinator, `2026-08-24`)* | 📐 `ComponentType.cs:119` — *"explicit `[ComponentId]` attribute **required for ALL types**"*, and it **throws** on a duplicate id *(`:133`)*. ⇒ ⭐⭐ **reflect-and-register-all is SAFE for cross-node layout** — the id is baked into the type, so registration order and host subset are irrelevant to the bit index. ⛔ This removes the one hazard that would have killed the reflection idea |
| ⭐⭐⭐ **the reflection mechanism ALREADY EXISTS and is IN PRODUCTION** *(coordinator, `2026-08-24`)* | 📐 `Fdp.Toolkits/ReplayBrowser/Federation/RepositoryPriming.RegisterDiscoveredComponents` — *"reflects all loaded (non-System) assemblies and registers every `[ComponentId]`-annotated type … and every `[EventId]`-annotated struct"*, handling `ReflectionTypeLoadException`, calling the generic `RegisterComponent<T>` via `MakeGenericMethod`. ⭐ **`replaybrowser` already boots this way** *(`ReplayBrowserSubsystem.cs:139`)* — it is how `ST-027` found its registration path. ⛔ So "each host registers all found by reflection" is **adopt an existing path everywhere**, not build a new one |

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

## 3b. ⭐⭐⭐ THE USER'S REFRAME *(`2026-08-24`)* — **reflection is bigger than gizmos, and aggregation dissolves the CLASS**

> 🔒 **User:** *"let each host register all components that are found by reflection, as a shared code, plain
> and simple? Would go together with registering all gizmos found by reflection… we will need to unify the
> host bootstrap code heavily… this would be just another step towards it. Other point of view: if we
> aggregated the csprojs into a much smaller number of assemblies… couldn't the assembly referencing
> problem disappear? where is the layering problem?"*

### ⭐⭐ Reflection is ONE mechanism for BOTH — and it is a bootstrap-unification step

📐 `RegisterDiscoveredComponents` **already registers components AND events by reflection**; the gizmo pack
is the **same principle, one type-attribute over**. ⇒ ⭐⭐⭐ **"prime the world by reflection"** — components,
events, gizmos — is a **single shared bootstrap step every host calls**, replacing the per-role component
registries *(`Ig/Cognitive/Combat/MuscleRole…`)* **and** the five hand-rolled gizmo lists at once. ⭐ That
is exactly the *"unify the host bootstrap heavily"* the user names, and Option A is its gizmo third.

⚠ **The ONE shared risk, stated plainly:** reflection sees only **loaded** assemblies. A component or gizmo
whose assembly no host references transitively is **invisible** to it. ⇒ ⭐⭐ **the same rail guards both**:
*"every `[ComponentId]`/`[GizmoProjector]` in the source tree is present at runtime, in every mode"* — a
miss is a load-order finding, ⛔ not a thing to ignore-list.

### ⭐⭐⭐ WHERE the layering problem IS — and the user is right that aggregation dissolves it

⭐ **The layering problem is ENTIRELY a compile-time cross-assembly reference CYCLE.** The rule: *a
referenced assembly cannot reference back.* Today the graph is
`Fdp.Core → Fdp.Toolkits → Hrot.Common → Hrot.AI.Behaviors → {Hrot.IG · Hrot.SimHost · Hrot.CGF} → composition`.

| ⛔ the squeeze | |
|---|---|
| a shared pack that **hosts CALL** must sit **below** them *(they reference it)* | ⇒ at/under `Hrot.Common` |
| a pack that **references all 7 families** must sit **above** `Hrot.IG/SimHost/CGF` *(it references them)* | ⇒ above the hosts |
| ⭐⭐⭐ **no assembly can be both below and above the hosts** | ⇒ **that is the cycle, and it is the whole problem** |

⭐⭐ **Three escapes, and they are not equivalent:**

| escape | how it kills the cycle | cost |
|---|---|---|
| ⭐⭐ **reflection** *(Option A)* | ⭐ **removes the compile-time reference entirely** — runtime discovery has no edge to cycle | ⭐ near-zero; the load-order rail |
| ⭐ **move files down** *(Option B)* | rearranges which assembly holds what, so one assembly *can* reference all 7 | 5 moves + 2 edges |
| ⭐⭐⭐ **aggregate assemblies** *(Option C — the user's)* | ⭐⭐ **removes the assembly BOUNDARIES** ⇒ within one assembly there are **no reference edges to cycle** — 🔒 **the user is exactly right** | large, structural, its own programme |

⭐⭐ **And Option C dissolves the whole RECURRING CLASS, not just this instance:** 📌 `ST-014` *(the Stride
bootstrapper could not move down — cycle)*, 📌 my own `Hrot.Common.Infrastructure` lean *(refuted as a
cycle)*, 📌 `ST-028` *(this)*. ⚠ **Aggregation was declined `2026-08-23` on BUILD-TIME grounds** *("10–15 s
is not worth it" — `Q51`)*; ⛔ **that framing missed the CYCLE TAX** — the recurring design cost of the
boundaries themselves. ⇒ ⭐ **this reopens `Q51` on a different axis**, but as its **own** large decision —
⛔ **not bundled into the gizmo pack.**

⭐⭐⭐ **The synthesis:** ⭐ **reflection (A) is the cheap answer that serves the gizmo pack AND the broader
bootstrap unification NOW, cycle-free, no restructure.** ⭐⭐ **Aggregation (C) is the deeper answer that
removes the cycle class** — worth reopening as a strategic decision on the cycle-tax argument, and **A does
not block it**: an aggregated future would simply make the reflection an ordinary loop over one assembly.

## ✅ RULED `2026-08-24` — **Option A, as a pluggable-handler primer**

> 🔒 **User:** *"lets go option A (with proper design); unifying/sharing the reflection scan
> (component/gizmos/others…) across hosts; maybe one class with pluggable handlers… The aggregation to
> assemblies… is still something to keep in the backlog."*

⇒ ⭐⭐⭐ **The buildable design is [`DESIGN_Reflection_World_Priming.md`](../DESIGN_Reflection_World_Priming.md)**
*(`READY-TO-BUILD`)* — one scan, N pluggable handlers, generalising `RepositoryPriming`. ⭐ **Option C
(aggregation) is BACKLOG**, reopening `Q51` on the cycle-tax + static-check axis. ⭐ Option B is retired
*(reflection makes the moves unnecessary)*. §4 below is the reasoning that led here.

## 4. ⭐⭐⭐ RECOMMENDATION — **the user rules**

| # | ⭐ recommendation | why |
|---|---|---|
| ⭐⭐⭐ **A — now decisively, not "close"** | 📐 The two findings in §1 tip it: component IDs are **explicit** *(so reflection is layout-safe)* and the reflection path **already exists and ships** *(`RegisterDiscoveredComponents`, replaybrowser)*. ⇒ A is *"extend a production mechanism by one attribute"*, not *"write a novel pack"* | ⭐ smallest blast radius, and it is the **bootstrap-unification step** the user wants, not a gizmo-only fix |
| ⭐⭐ **do it as ONE "prime the world" step** | components + events + gizmos, one shared call every host makes, ⛔ retiring the per-role component registries and the five gizmo lists **together** | 🔒 *"unify the host bootstrap heavily… another step toward it"* |
| ⭐ **B is the fallback, and cheap** | ⛔ only if the load-order rail shows a family's assembly is not loaded in some mode. A's reflection is the same enumeration B would need | ⛔ do not pre-commit to B's moves |
| ⭐⭐ **C (aggregation) — reopen SEPARATELY** | ⭐ it dissolves the cycle **class** *(§3b)*, but it is a large structural programme; ⛔ **not this batch.** A does not block it | ⚠ reopens `Q51` on the cycle-tax axis, not build-time |
| ⛔ **NOT** *"keep the per-host lists"* | 🔒 the user's ruling forbids it — *"replaybrowser is no exception… the host does not curate"* | — |

⚠ **What A must PROVE — the one real risk:** every `[ComponentId]`/`[GizmoProjector]` assembly is **loaded**
at the point the priming runs, in **every** mode. 📐 Reflection over `AppDomain.GetAssemblies()` sees only
loaded assemblies — a type in an assembly no host references transitively is **absent**. ⇒ ⭐⭐ **the first
rail is "priming finds every `[ComponentId]`/`[GizmoProjector]` in the source tree, in every mode"** *(a
source-scan count vs the runtime count)*; a miss is a load-order finding, ⛔ never an ignore-list.
⚠ **And measure the cost** — `ST-027` showed the schema half is free; the full reflection over every
assembly at every boot is **not obviously** free ⇒ report the mode-rail startup delta.

## 5. ⛔ NOT READY TO BUILD

⭐ `build-state: DESIGN` — once A or B is chosen this gains the `classDiagram`/`sequenceDiagram` for the
chosen mechanism and becomes `READY-TO-BUILD`, and invariant `B`'s rail *(`ST-028` item ③)* ships with it.

---

## ⭐⭐ 5. ARCHITECT REVIEW (NotebookLM) + COORDINATOR VERIFICATION — `2026-08-24`

⭐ The NotebookLM architect reviewed the reflection proposal. Each claim **verified against code** *(it has
been inexact before)*:

| # | claim | verdict | evidence |
|---|---|---|---|
| **P2** | the generator injects `GizmoSettingsRegistry` into ctors; a naive `Activator.CreateInstance` would break settings-registering gizmos | ✅ **TRUE** | `GizmoRegistrarGenerator.cs:118-124` detects a `GizmoSettingsRegistry` ctor param and emits `new Gizmo(settings)` vs `new Gizmo()`. ⇒ a reflection loop **must replicate the ctor-injection rule** — real complexity, manageable |
| **P3** | "id-only" registration still pollutes the recorder schema | ✅ **TRUE, and stronger than framed** | `GetOrRegisterManaged` defaults `_isRecordable=true`; `GetRecordableMask`/`BuildSchemaManifest` read the **static** registry. ⇒ ⭐ **my §8.4 was incomplete — id-only needs `SetRecordable(false)` too.** ⛔ AND it is INHERENT to uniform membership *(any host registering a gizmo must resolve its component ids)*, not to reflection |
| **P5** | headless nodes stream gizmos over DDS ⇒ bandwidth | ✅ **TRUE, and previously unconsidered** | SimHost/CGF register `StatelessGizmoSystem`; `DebugPrimitivesBatchPublisherSystem.cs:37` publishes whenever the buffer is non-empty — ⛔ **not demand-gated.** ⚠ **BUT misfiled as A-vs-B** — Option B has the IDENTICAL runtime profile. 📄 The real finding: `DESIGN_Uniform_Gizmo_Membership.md` §8.5 |
| **P1** | reflection misses lazily-loaded assemblies | ⚠ **BOUNDED today** | gizmos live in **statically-referenced** assemblies the host touches at bootstrap; the per-mode completeness rail catches gaps. 🔴 **Real for the FUTURE** — a `[GizmoProjector]` in a hot-reloaded behavior assembly *(`AiHotReloadCoordinator`)* would be missed |
| **P4** | reflection blocks NativeAOT/trimming | ⚠ **TRUE but NOT decisive** | 📐 runtime reflection is **already pervasive** — **47** production files use `GetAssemblies`/`Activator`/`MakeGenericMethod`, and `RepositoryPriming` already reflects. ⇒ the platform is **already** not AOT/trim-compatible; Option A adds no NEW category of debt |

### ⭐⭐⭐ THE RE-READ — **the two strongest points do NOT decide A vs B**

⛔ **NotebookLM concluded "therefore Option B."** 📐 But **P3 and P5 — its strongest points — apply
identically to A and B** *(both register all families on all hosts; both resolve all required component ids;
both execute+publish on headless nodes)*. ⇒ they are not an argument for code-move over reflection; they are
an argument about **whether headless nodes should carry the full set at all** *(§8.5)*.

| ⭐ what genuinely bears on A vs B | |
|---|---|
| **P2** — reflection must replicate ctor-injection | ⭐ mild edge to **B** *(generator stays authoritative)* |
| **P1-future** — hot-reloaded gizmos | ⭐ mild edge to **B** |
| **P4** — AOT | ⛔ **moot** *(already not AOT)* |
| **the cost of B** | ⚠ 5 cross-assembly file moves + 2 project edges |

⇒ ⭐⭐ **Two decisions, now separated:** **(a)** the §8.5 headless-node question *(mechanism-independent, the
one that matters)*; **(b)** A vs B, where B has a mild correctness edge if we accept the file moves. ⛔ **Both
are the user's to rule** — the build is HELD *(§8.6)*.
