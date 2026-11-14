<!--STATUS
state: LIVE
build-state: RATIONALE (not buildable) — records a DECISION NOT TO BUILD, with the measurements behind it.
updated: 2026-08-24
current-answer: the whole file, and it is short on purpose. DO NOT reflect-register ECS components on
  simulation hosts — keep the existing role-gated group registries. §1 is the measured why. ⛔ The earlier
  "one primer, pluggable component+event+gizmo handlers" framework is WITHDRAWN — components were the only
  reason it spanned more than gizmos, and components are out. Gizmo reflection lives in
  DESIGN_Uniform_Gizmo_Membership.md §8.
design-basis: 🔒 user 2026-08-24 ("we do not need to [unify-register all ECS components] if there are
  significant cons") · measured against ComponentType.cs, EntityRepository.Sync.cs, AsyncRecorder.cs,
  ModuleHostKernel.cs, CombatTkbTranslator/BehaviorTkbTranslator (§1).
known-conflict: none. ⛔ HISTORY §2 records the withdrawn framework so a reader does not resurrect it.
-->
# DESIGN — **ECS components stay role-gated** *(why NOT to reflect-register them)*

## 0. ⭐⭐⭐ THE DECISION

> 🔒 **User, `2026-08-24`:** *"the critique was… just to unified registering of all ECS components in
> every subsystem. Which is something we do not need to do if there are significant cons of it."*

⭐⭐ **We do not reflect-register ECS components. Every node keeps its existing role-gated group registries**
*(`HrotSharedComponentRegistry` on all nodes · `Cognitive`/`Combat` on Brain · `MuscleRole` on Muscle ·
`IgRole` on IG)*. ⛔ **This is the status quo — there is nothing to build.** ⭐ `replaybrowser` keeps its
reflect-all *(`RepositoryPriming`)* as the **inspection exception**: it deserialises any recording and never
simulates or records.

⭐⭐ **This is a COMPONENT decision only.** 📄 Gizmos are unrelated and DO reflect — a gizmo is data-free
*(a projector with no matching entity draws nothing and needs only a static component id, not a table)* —
see **[`DESIGN_Uniform_Gizmo_Membership.md` §8](DESIGN_Uniform_Gizmo_Membership.md)**.

## INVENTORY — **what was enumerated to reach the decision**

| query run | total | result |
|---|---|---|
| `grep "AppDomain.CurrentDomain.GetAssemblies\|assembly.GetTypes()"` *(non-test, non-`obj`)* | ~40 sites | most are editor/hot-reload tooling; the priming scans are attribute-driven *(below)* |
| `grep "GetCustomAttribute<…Attribute>"` ranked | — | `ComponentId` 10 · `EventId` 11 · `GizmoProjector` 9 · then `BlueprintRegistrar` · `TkbDescriptor` · `DataPolicy` |
| the role-gated registries that already exist | 5 | `HrotSharedComponentRegistry` · `Cognitive` · `Combat` · `MuscleRole` · `IgRole` — the mechanism this decision KEEPS |
| the recorder/save read path | 2 | `EntityRepository.GetRecordableMask` + `AsyncRecorder.BuildSchemaManifest` — both read the **static** `ComponentTypeRegistry` *(§1 #3)* |
| `ComponentTypeRegistry.GetOrRegisterManaged` | 1 | lazy/on-demand, throws on missing `[ComponentId]` — the static registry is controlled by what registers, not an attribute scan *(§1 #4)* |

## 1. ⭐⭐ WHY — **the four downsides, measured against code**

📌 A global component scan was tempting *("register everything, delete the boilerplate")*. Measured
`2026-08-24`, it is harmful, and a **per-subsystem whitelist does NOT save it** because the worst downsides
read the **process-wide static registry**, not the local repo:

| # | downside | 📐 verified |
|---|---|---|
| **1** | **breaks TkbTemplate client-side skipping** | `CombatTkbTranslator.cs:40/49/73` · `BehaviorTkbTranslator.cs:30-78` gate `apply` on `repo.IsComponentTypeRegistered<T>()`. Register a server component on IG's repo ⇒ IG **materialises** it. ⭐ *(A repo whitelist DOES fix this one — it is repo-local.)* |
| **2** | **`CreateFullMask` sync-all blowup** | `ModuleHostKernel.cs:350-378` — a module with no explicit mask syncs **all registered** components |
| **3** 🔴 | **recorder / save schema pollution** | `EntityRepository.GetRecordableMask` *(`Sync.cs:179`)* and `AsyncRecorder.BuildSchemaManifest` *(`:326`)* iterate `ComponentTypeRegistry.GetRecordableTypeIds()` — the **STATIC** registry; `GetRecordableMask` does not even intersect with the repo. ⛔ **A repo whitelist CANNOT stop this** |
| **4** 🔴 | **lazy-load cross-node drift** | `ComponentTypeRegistry.GetOrRegisterManaged` is on-demand *(not an attribute scan)*, and CLR assemblies load lazily ⇒ two nodes that scan different loaded sets get **different** static registries ⇒ DDS layout-hash skew. ⛔ **Also process-wide, not fixed by a whitelist** |

⇒ ⭐⭐⭐ **#3 and #4 are the killers: a whitelist mitigates the repo-local issues (#1, SoD) but leaves the
process-wide recorder/DDS pollution.** The clean answer is the one already in place — **role-gated group
registries** — which register exactly the subset each node understands, keeping recordings, sync masks and
the DDS layout correct by construction.

⚠ **`ST-027`'s `MapSchemaPack` is a small instance of #1/#3** — it registered 15 component *tables* on every
host to satisfy the gizmo registry, when the gizmo needs only the id. That is corrected as part of the gizmo
work *(`DESIGN_Uniform_Gizmo_Membership.md` §8.4)*, not here.

## ⛔ HISTORY — **the withdrawn framework** *(kept so it is not resurrected)*

⭐ An earlier version of this file proposed a `HostBootstrapPrimer` with pluggable component + event + gizmo
handlers — *"one scan, register everything found."* ⛔ **WITHDRAWN `2026-08-24`.** Components were the only
reason it spanned more than gizmos, and components are out *(§0)*. What remains — gizmo discovery by
reflection — is one focused registrar, and it lives in the gizmo design, not a framework. 📌 The lesson:
the critique was **components-only**; bundling gizmos into it and building a framework was scope I added, not
scope the problem had.
